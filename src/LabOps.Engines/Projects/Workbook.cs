using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LabOps.Engines.Python;

namespace LabOps.Engines.Projects;

/// <summary>
/// Reads an .xlsx or .xlsm the way openpyxl's load_workbook(read_only=True, data_only=True) and
/// iter_rows(values_only=True) do, after ws.reset_dimensions(): every worksheet (hidden ones too,
/// chart sheets not), in order; formulas as their last calculated value; numbers as int or float;
/// dates where the cell's number format says so (openpyxl's is_date_format, with the 1900 or 1904
/// epoch); each row as wide as its last cell. Read straight from the file's XML, as openpyxl does,
/// so the rules can be the same.
/// </summary>
internal static partial class Workbook
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string WorksheetType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string StringsType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
    private const string StylesType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string DocumentType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    private static readonly DateTime WindowsEpoch = new(1899, 12, 30);
    private static readonly DateTime MacEpoch = new(1904, 1, 1);

    public sealed record RawSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);

    public static IReadOnlyList<RawSheet> Read(string path, string name)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var document = Target("", Relationships(zip, "_rels/.rels").FirstOrDefault(r => r.Type == DocumentType).Target ?? "xl/workbook.xml");
            var folder = document.Contains('/', StringComparison.Ordinal) ? document[..(document.LastIndexOf('/') + 1)] : "";
            var rels = Relationships(zip, folder + "_rels/" + document[(document.LastIndexOf('/') + 1)..] + ".rels");
            var workbook = Load(zip, document) ?? throw new InvalidDataException("no workbook part");

            var epoch = workbook.Root!.Element(Main + "workbookPr")?.Attribute("date1904")?.Value is "1" or "true" ? MacEpoch : WindowsEpoch;
            var strings = rels.Where(r => r.Type == StringsType).Select(r => Load(zip, Target(folder, r.Target))).FirstOrDefault() is { } s
                ? SharedStrings(s) : [];
            var (dates, durations) = rels.Where(r => r.Type == StylesType).Select(r => Load(zip, Target(folder, r.Target))).FirstOrDefault() is { } st
                ? DateStyles(st) : ([], []);

            var sheets = new List<RawSheet>();
            foreach (var sheet in workbook.Root!.Element(Main + "sheets")?.Elements(Main + "sheet") ?? [])
            {
                var id = sheet.Attribute(OfficeRels + "id")?.Value;
                var rel = rels.FirstOrDefault(r => r.Id == id);
                if (rel.Type != WorksheetType)
                {
                    continue;   // a chart sheet or a dialog sheet: wb.worksheets leaves them out
                }

                var part = zip.GetEntry(Target(folder, rel.Target));
                if (part is null)
                {
                    continue;
                }

                using var stream = part.Open();
                sheets.Add(new RawSheet(sheet.Attribute("name")?.Value ?? "", Rows(stream, strings, dates, durations, epoch)));
            }

            return sheets;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException or FormatException)
        {
            throw new EngineError($"{name} could not be read as a workbook ({ex.Message}); save it again from Excel, or as CSV UTF-8");
        }
    }

    private readonly record struct Relationship(string? Id, string? Type, string? Target);

    private static List<Relationship> Relationships(ZipArchive zip, string part) =>
        Load(zip, part)?.Root?.Elements(PackageRels + "Relationship")
            .Select(r => new Relationship(r.Attribute("Id")?.Value, r.Attribute("Type")?.Value, r.Attribute("Target")?.Value)).ToList() ?? [];

    private static string Target(string folder, string? target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return "";
        }

        if (target.StartsWith('/'))
        {
            return target[1..];
        }

        var parts = new List<string>(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var piece in target.Split('/'))
        {
            if (piece == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (piece is not ("." or ""))
            {
                parts.Add(piece);
            }
        }

        return string.Join('/', parts);
    }

    private static XDocument? Load(ZipArchive zip, string part)
    {
        var entry = zip.GetEntry(part);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    /// <summary>read_string_table: each si's text, plain and every run, with x005F_ taken out.</summary>
    private static List<string> SharedStrings(XDocument doc) =>
        [.. doc.Root!.Elements(Main + "si").Select(si => Content(si).Replace("x005F_", "", StringComparison.Ordinal))];

    /// <summary>Text.content: the plain t, then each run's t; phonetic runs are left out.</summary>
    private static string Content(XElement text) =>
        string.Concat(text.Elements(Main + "t").Take(1).Select(t => t.Value)
            .Concat(text.Elements(Main + "r").Select(r => r.Element(Main + "t")?.Value ?? "")));

    // ------------------------------------------------------------------ number formats

    private static readonly Dictionary<int, string> BuiltinFormats = new()
    {
        [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00",
        [5] = "\"$\"#,##0_);(\"$\"#,##0)", [6] = "\"$\"#,##0_);[Red](\"$\"#,##0)",
        [7] = "\"$\"#,##0.00_);(\"$\"#,##0.00)", [8] = "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
        [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??",
        [14] = "mm-dd-yy", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy", [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss", [22] = "m/d/yy h:mm",
        [37] = "#,##0_);(#,##0)", [38] = "#,##0_);[Red](#,##0)", [39] = "#,##0.00_);(#,##0.00)",
        [40] = "#,##0.00_);[Red](#,##0.00)", [41] = @"_(* #,##0_);_(* \(#,##0\);_(* ""-""_);_(@_)",
        [42] = @"_(""$""* #,##0_);_(""$""* \(#,##0\);_(""$""* ""-""_);_(@_)",
        [43] = @"_(* #,##0.00_);_(* \(#,##0.00\);_(* ""-""??_);_(@_)",
        [44] = @"_(""$""* #,##0.00_)_(""$""* \(#,##0.00\)_(""$""* ""-""??_)_(@_)",
        [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mmss.0", [48] = "##0.0E+0", [49] = "@",
    };

    [GeneratedRegex(@"("".*?"")|(\[(?!hh?\]|mm?\]|ss?\])[^\]]*\])")]
    private static partial Regex StripPattern();

    [GeneratedRegex(@"(?<![_\\])[dmhysDMHYS]")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\[hh?\](:mm(:ss(\.0*)?)?)?|\[mm?\](:ss(\.0*)?)?|\[ss?\](\.0*)?", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    public static bool IsDateFormat(string? format) =>
        format is not null && DatePattern().IsMatch(StripPattern().Replace(format.Split(';')[0], ""));

    public static bool IsDurationFormat(string? format) =>
        format is not null && DurationPattern().IsMatch(format.Split(';')[0]);

    /// <summary>Stylesheet._normalise_numbers: which cell styles (by index) are dates, and which durations.</summary>
    private static (HashSet<int> Dates, HashSet<int> Durations) DateStyles(XDocument styles)
    {
        var custom = new Dictionary<int, string>();
        foreach (var f in styles.Root!.Element(Main + "numFmts")?.Elements(Main + "numFmt") ?? [])
        {
            if (int.TryParse(f.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                custom[id] = f.Attribute("formatCode")?.Value ?? "";
            }
        }

        var dates = new HashSet<int>();
        var durations = new HashSet<int>();
        var index = 0;
        foreach (var xf in styles.Root!.Element(Main + "cellXfs")?.Elements(Main + "xf") ?? [])
        {
            var id = int.TryParse(xf.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            var format = custom.TryGetValue(id, out var c) ? c : BuiltinFormats.GetValueOrDefault(id);
            if (IsDateFormat(format))
            {
                dates.Add(index);
            }

            if (IsDurationFormat(format))
            {
                durations.Add(index);
            }

            index++;
        }

        return (dates, durations);
    }

    // ------------------------------------------------------------------ cells

    private static List<IReadOnlyList<object?>> Rows(Stream stream, List<string> strings, HashSet<int> dates, HashSet<int> durations, DateTime epoch)
    {
        var rows = new List<IReadOnlyList<object?>>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
        using var reader = XmlReader.Create(stream, settings);
        var rowCounter = 0;
        var counter = 1;   // the next row number _cells_by_row will yield
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != Main.NamespaceName)
            {
                reader.Read();
                continue;
            }

            // ReadFrom leaves the reader on the node after the row, which may be the next row.
            var row = (XElement)XNode.ReadFrom(reader);
            var r = row.Attribute("r")?.Value;
            rowCounter = r is null ? rowCounter + 1 : (int)double.Parse(r, CultureInfo.InvariantCulture);
            var colCounter = 0;
            var cells = new List<(int Column, object? Value)>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var coordinate = c.Attribute("r")?.Value;
                int column;
                if (!string.IsNullOrEmpty(coordinate))
                {
                    column = ColumnIndex(coordinate);
                    colCounter = column;
                }
                else
                {
                    column = ++colCounter;
                }

                cells.Add((column, CellValue(c, strings, dates, durations, epoch)));
            }

            // Missing rows read as empty; a row number already passed is skipped.
            for (; counter < rowCounter; counter++)
            {
                rows.Add([]);
            }

            if (counter <= rowCounter)
            {
                var width = cells.Count == 0 ? 0 : cells[^1].Column;
                var values = new object?[Math.Max(0, width)];
                foreach (var (column, value) in cells)
                {
                    if (column >= 1 && column <= width)
                    {
                        values[column - 1] = value;
                    }
                }

                rows.Add(values);
                counter++;
            }
        }

        return rows;
    }

    private static int ColumnIndex(string coordinate)
    {
        var column = 0;
        foreach (var ch in coordinate)
        {
            if (char.IsAsciiDigit(ch))
            {
                break;
            }

            if (ch != '$')
            {
                column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
        }

        return column;
    }

    private static object? CellValue(XElement c, List<string> strings, HashSet<int> dates, HashSet<int> durations, DateTime epoch)
    {
        var type = c.Attribute("t")?.Value ?? "n";
        var style = int.TryParse(c.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
        if (type == "inlineStr")
        {
            return c.Element(Main + "is") is { } inline ? Content(inline) : null;
        }

        var text = c.Element(Main + "v")?.Value;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        switch (type)
        {
            case "n":
                object number = text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal) || text.Contains('e', StringComparison.Ordinal)
                    ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                    : PyText.ToInt(text) ?? throw new FormatException($"{text} is not a number");
                return dates.Contains(style) ? FromExcel(PyText.ToDouble(number), epoch, durations.Contains(style)) : number;
            case "s":
                return strings[int.Parse(text, CultureInfo.InvariantCulture)];
            case "b":
                return int.Parse(text, CultureInfo.InvariantCulture) != 0;
            case "d":
                return FromIso8601(text);
            default:
                return text;   // "str" (a formula's text) and "e" (an error such as #N/A)
        }
    }

    /// <summary>openpyxl's from_excel: a serial number as a datetime, a time (under one day), or a timedelta.</summary>
    private static object FromExcel(double value, DateTime epoch, bool duration)
    {
        try
        {
            if (duration)
            {
                var micro = (long)Math.Round(value * 86_400_000_000.0);
                if (micro % 1_000_000 != 0)
                {
                    // timedelta rounded to the millisecond, as openpyxl does
                    var seconds = Math.Floor(micro / 1_000_000.0);
                    var rest = micro - (long)seconds * 1_000_000;
                    micro = (long)seconds * 1_000_000 + (long)Math.Round(rest / 1000.0, MidpointRounding.ToEven) * 1000;
                }

                return new PyTimeDelta(micro);
            }

            var day = Math.Floor(value);
            var fraction = value - day;
            var ms = (long)Math.Round(fraction * 86_400 * 1000, MidpointRounding.ToEven);
            var diff = TimeSpan.FromMilliseconds(ms);
            if (value >= 0 && value < 1 && diff.Days == 0)
            {
                return new PyTime(diff);
            }

            if (value > 0 && value < 60 && epoch == WindowsEpoch)
            {
                day += 1;
            }

            return new PyDateTime(epoch.AddDays(day) + diff, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "#VALUE!";
        }
    }

    [GeneratedRegex(@"^(?<date>(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}))?T?(?<time>(?<hour>\d{2}):(?<minute>\d{2})(:(?<second>\d{2})(?<microsecond>\.\d{1,3})?)?)?Z?")]
    private static partial Regex IsoPattern();

    private static object FromIso8601(string text)
    {
        var m = IsoPattern().Match(text);
        int G(string n) => m.Groups[n].Success ? int.Parse(m.Groups[n].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Success && (m.Groups["date"].Success || m.Groups["time"].Success))
        {
            var micro = m.Groups["microsecond"].Success ? (int)(double.Parse(m.Groups["microsecond"].Value, CultureInfo.InvariantCulture) * 1_000_000) : 0;
            var time = new TimeSpan(0, G("hour"), G("minute"), G("second")) + TimeSpan.FromTicks(micro * 10L);
            if (!m.Groups["date"].Success)
            {
                return new PyTime(time);
            }

            var date = new DateOnly(G("year"), G("month"), G("day"));
            return m.Groups["time"].Success ? new PyDateTime(date.ToDateTime(TimeOnly.MinValue) + time, null) : date;
        }

        throw new FormatException($"Invalid datetime value {text}");
    }
}
