using System.Text;
using LabOps.Engines.Python;

namespace LabOps.Engines.Projects;

/// <summary>
/// Reading a collaborator's sheet (.csv, .xlsx, .xlsm) as rows, the way project.py did with
/// Python's csv module and openpyxl: the same text decoding, the same CSV quoting rules, and the
/// same cell values, so the identifier check sees exactly what it saw before.
/// </summary>
public static partial class Sheets
{
    public sealed record Sheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

    /// <summary>[(sheet name, headers, rows)] for a .csv or an .xlsx. .xlsx cells keep their types (dates arrive as dates).</summary>
    public static IReadOnlyList<Sheet> Read(string path, string? sheet = null)
    {
        var name = Path.GetFileName(path);
        var suffix = Path.GetExtension(path).ToLowerInvariant();
        if (suffix == ".csv")
        {
            var (headers, rows) = CsvTable(DecodeText(File.ReadAllBytes(path), name), name, titles: true);
            return [new Sheet(name, headers, rows)];
        }

        if (suffix is ".xlsx" or ".xlsm")
        {
            return [.. Workbook.Read(path, name).Where(s => sheet is null || sheet.Length == 0 || s.Name == sheet)
                .Select(s => { var (h, r) = SplitRows(s.Rows, titles: true); return new Sheet(s.Name, h, r); })];
        }

        throw new EngineError($"{name}: only .csv and .xlsx sheets can be read");
    }

    /// <summary>
    /// Text as collaborators save it: UTF-8 with or without a BOM, UTF-16 with a BOM (Excel's
    /// Unicode Text), or Windows-1252 (what Excel's "CSV (Comma delimited)" writes on Windows).
    /// </summary>
    public static string DecodeText(byte[] data, string name)
    {
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
        {
            try
            {
                return new UnicodeEncoding(bigEndian: data[0] == 0xFE, byteOrderMark: false, throwOnInvalidBytes: true)
                    .GetString(data, 2, data.Length - 2);
            }
            catch (ArgumentException)
            {
                throw new EngineError($"{name}: the text encoding is not one this can read; save the file as CSV UTF-8");
            }
        }

        // Text never holds NUL. UTF-16 without its byte-order mark does, and as UTF-8 it would read
        // as letters with NULs between them, which no header rule would recognize.
        if (Array.IndexOf(data, (byte)0) >= 0)
        {
            throw new EngineError($"{name}: the text encoding is not one this can read; save the file as CSV UTF-8");
        }

        try
        {
            var start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data, start, data.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252(data);
        }
    }

    // Python's cp1252 codec with errors="replace": the five bytes Windows-1252 leaves undefined
    // become U+FFFD (.NET's code page maps them to C1 controls instead).
    private static readonly int[] Cp1252High =
    [
        0x20AC, 0xFFFD, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0xFFFD, 0x017D, 0xFFFD,
        0xFFFD, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0xFFFD, 0x017E, 0x0178,
    ];

    private static string Windows1252(byte[] data)
    {
        var text = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            text.Append(b is >= 0x80 and <= 0x9F ? (char)Cp1252High[b - 0x80] : (char)b);
        }

        return text.ToString();
    }

    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows) CsvTable(string text, string name, bool titles = false)
    {
        try
        {
            return SplitRows([.. CsvReader.Parse(text).Select(r => (IReadOnlyList<object?>)[.. r])], titles);
        }
        catch (CsvReader.CsvError ex)
        {
            throw new EngineError($"{name} could not be read as CSV ({ex.Message}); save it as CSV UTF-8");
        }
    }

    /// <summary>
    /// _split_rows: empty rows go, the first row left is the headers (as stripped text), and every
    /// row is padded to the widest. `titles` first drops title rows above the headers (TitleRows),
    /// for a collaborator's sheet; a file being committed keeps its first row as the headers.
    /// </summary>
    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows) SplitRows(IReadOnlyList<IReadOnlyList<object?>> all, bool titles = false)
    {
        var rows = all.Where(r => r.Any(c => c is not (null or ""))).ToList();
        if (rows.Count == 0)
        {
            return ([], []);
        }

        if (titles)
        {
            rows = rows[TitleRows(rows)..];
        }

        var width = rows.Max(r => r.Count);
        var headers = rows[0].Select(c => c is null ? "" : Py.Strip(PyText.Str(c)))
            .Concat(Enumerable.Repeat("", width - rows[0].Count)).ToList();
        var body = rows.Skip(1).Select(r => (IReadOnlyList<object?>)[.. r, .. Enumerable.Repeat<object?>(null, width - r.Count)]).ToList();
        return (headers, body);
    }

    /// <summary>
    /// How many rows at the top are titles over the headers, such as a merged "Plasma samples" or
    /// "Meta Data" above the columns it groups. Read as the headers, a title row leaves every column
    /// but a few without one, and the check refuses the sheet. A row is a title only when the row
    /// under it is plainly the headers: at least twice as many cells, covering every cell the title
    /// fills, all distinct words rather than numbers or dates, and over at least one column that is
    /// mostly numbers or dates. The last condition is what keeps a first row of data under a mostly
    /// blank header row from being taken for the headers, which would let a column of names through
    /// under a name for a header: data rows hold numbers where headers hold words. A file being
    /// committed does not get this at all (SplitRows), so git history's guard does not rest on it.
    /// </summary>
    private static int TitleRows(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        static bool Filled(object? c) => c is not (null or "") && (c is not string s || s.Trim().Length > 0);
        static bool NumberOrDate(object? c) => c switch
        {
            string s => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                        || DateText().IsMatch(s.Trim()),
            null or bool => false,
            _ => true,
        };

        static bool MostlyNumbers(IEnumerable<object?> column)
        {
            var values = column.Where(Filled).ToList();
            return values.Count > 0 && values.Count(NumberOrDate) * 2 > values.Count;
        }

        var skip = 0;
        while (skip < 3 && skip + 2 < rows.Count)
        {
            var (title, headers) = (rows[skip], rows[skip + 1]);
            var titled = Enumerable.Range(0, title.Count).Where(i => Filled(title[i])).ToList();
            var named = Enumerable.Range(0, headers.Count).Where(i => Filled(headers[i])).ToList();
            var words = named.Select(i => headers[i]).ToList();
            var isTitle = named.Count >= 2 * titled.Count && named.Count >= 2
                          && titled.All(named.Contains)
                          && words.All(w => w is string && !NumberOrDate(w))
                          && words.Select(w => Py.Strip((string)w!)).Distinct(StringComparer.Ordinal).Count() == words.Count
                          && named.Any(i => MostlyNumbers(rows.Skip(skip + 2).Select(r => i < r.Count ? r[i] : null)));
            if (!isTitle)
            {
                break;
            }

            skip++;
        }

        return skip;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{1,4}[-/.]\d{1,2}[-/.]\d{1,4}$")]
    private static partial System.Text.RegularExpressions.Regex DateText();

    /// <summary>A cell as text: a date as YYYY-MM-DD, a whole float without .0, the rest as str().</summary>
    public static string CellText(object? v) => v switch
    {
        null => "",
        PyDateTime dt => dt.Value.TimeOfDay == TimeSpan.Zero && dt.Offset is null
            ? DateOnly.FromDateTime(dt.Value).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : dt.IsoFormat(' '),
        DateOnly d => PyText.Str(d),
        double f when double.IsFinite(f) && f == Math.Floor(f) => PyText.Str(PyText.ToInt(f)),
        _ => PyText.Str(v),
    };
}

/// <summary>
/// CPython's csv.reader with the default (excel) dialect, over io.StringIO(text): the same state
/// machine (Modules/_csv.c), so quoting, line breaks inside quotes, and the errors match.
/// </summary>
internal static class CsvReader
{
    public sealed class CsvError(string message) : Exception(message);

    private const int FieldLimit = 131072;

    private enum State { StartRecord, StartField, InField, InQuotedField, QuoteInQuotedField, EatCrnl }

    public static IEnumerable<List<string>> Parse(string text)
    {
        // io.StringIO(text) yields lines ending at each \n (a lone \r does not end one).
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        var state = State.StartRecord;
        var fields = new List<string>();
        var field = new StringBuilder();
        var fieldLength = 0;

        void SaveField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldLength = 0;
        }

        void AddChar(Rune c)
        {
            if (fieldLength >= FieldLimit)
            {
                throw new CsvError($"field larger than field limit ({FieldLimit})");
            }

            field.Append(c.ToString());
            fieldLength++;
        }

        const int Eol = -2;
        foreach (var line in lines.Append(null))
        {
            if (line is null)
            {
                // End of input inside a record: Python saves what it has (strict is off).
                if (fieldLength != 0 || state == State.InQuotedField)
                {
                    SaveField();
                    yield return fields;
                }

                yield break;
            }

            foreach (var c in line.EnumerateRunes().Select(r => r.Value).Append(Eol))
            {
                var rune = c == Eol ? default : new Rune(c);
                switch (state)
                {
                    case State.StartRecord:
                        if (c == Eol)
                        {
                            break;
                        }

                        if (c is '\n' or '\r')
                        {
                            state = State.EatCrnl;
                            break;
                        }

                        state = State.StartField;
                        goto case State.StartField;
                    case State.StartField:
                        if (c is '\n' or '\r' or Eol)
                        {
                            SaveField();
                            state = c == Eol ? State.StartRecord : State.EatCrnl;
                        }
                        else if (c == '"')
                        {
                            state = State.InQuotedField;
                        }
                        else if (c == ',')
                        {
                            SaveField();
                        }
                        else
                        {
                            AddChar(rune);
                            state = State.InField;
                        }

                        break;
                    case State.InField:
                        if (c is '\n' or '\r' or Eol)
                        {
                            SaveField();
                            state = c == Eol ? State.StartRecord : State.EatCrnl;
                        }
                        else if (c == ',')
                        {
                            SaveField();
                            state = State.StartField;
                        }
                        else
                        {
                            AddChar(rune);
                        }

                        break;
                    case State.InQuotedField:
                        if (c == Eol)
                        {
                        }
                        else if (c == '"')
                        {
                            state = State.QuoteInQuotedField;
                        }
                        else
                        {
                            AddChar(rune);
                        }

                        break;
                    case State.QuoteInQuotedField:
                        if (c == '"')
                        {
                            AddChar(rune);
                            state = State.InQuotedField;
                        }
                        else if (c == ',')
                        {
                            SaveField();
                            state = State.StartField;
                        }
                        else if (c is '\n' or '\r' or Eol)
                        {
                            SaveField();
                            state = c == Eol ? State.StartRecord : State.EatCrnl;
                        }
                        else
                        {
                            AddChar(rune);
                            state = State.InField;
                        }

                        break;
                    case State.EatCrnl:
                        if (c is '\n' or '\r')
                        {
                        }
                        else if (c == Eol)
                        {
                            state = State.StartRecord;
                        }
                        else
                        {
                            throw new CsvError("new-line character seen in unquoted field - do you need to open the file with newline=''?");
                        }

                        break;
                }
            }

            if (state == State.StartRecord)
            {
                yield return fields;
                fields = [];
            }
        }
    }
}
