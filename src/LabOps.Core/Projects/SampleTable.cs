using System.Data;
using System.Globalization;
using System.Text;

namespace LabOps.Core.Projects;

/// <summary>
/// A project's organized sample table (metadata/samples.csv), or one of the deidentified files
/// it was made from (metadata/received/*.csv), read for viewing. The engine owns what these files
/// must contain; this only reads CSV as the engine and Claude write it (RFC 4180, UTF-8).
/// </summary>
public sealed class SampleTable
{
    /// <summary>The hidden column the search runs on: every cell of the row, tab separated.</summary>
    public const string SearchColumn = "Search";

    private const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private SampleTable(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        Headers = headers;
        Rows = rows;
        var qc = IndexOf("QC");
        QcRows = qc < 0 ? null : rows.Count(r => string.Equals(r[qc].Trim(), "TRUE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The column headers, in file order. A row longer than the header adds "Column N".</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>The rows below the header, each exactly as long as <see cref="Headers"/>.</summary>
    public IReadOnlyList<string[]> Rows { get; }

    /// <summary>Rows whose QC column is TRUE; null when there is no QC column (a file as received).</summary>
    public int? QcRows { get; }

    /// <summary>The rows, and the study samples and QC among them when the table says.</summary>
    public string Summary => QcRows is { } qc
        ? $"{Rows.Count} rows: {Rows.Count - qc} study samples, {qc} QC"
        : $"{Rows.Count} rows";

    public static SampleTable Read(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static SampleTable Parse(string text)
    {
        var records = ParseCsv(text);
        var width = records.Count == 0 ? 0 : records.Max(r => r.Length);
        var headers = Enumerable.Range(0, width)
            .Select(i => records[0].Length > i && records[0][i].Trim().Length > 0 ? records[0][i].Trim() : $"Column {i + 1}")
            .ToList();
        var rows = records.Skip(1).Select(r => r.Length == width ? r : [.. r, .. Enumerable.Repeat("", width - r.Length)]).ToList();
        return new SampleTable(headers, rows);
    }

    /// <summary>
    /// The CSV files to show for a project: the organized table first, then each received file.
    /// </summary>
    public static IReadOnlyList<string> FilesIn(string projectFolder)
    {
        var metadata = Path.Combine(projectFolder, "metadata");
        var files = new List<string>();
        var samples = Path.Combine(metadata, "samples.csv");
        if (File.Exists(samples))
        {
            files.Add(samples);
        }

        var received = Path.Combine(metadata, "received");
        if (Directory.Exists(received))
        {
            files.AddRange(Directory.EnumerateFiles(received, "*.csv").Order(StringComparer.OrdinalIgnoreCase));
        }

        return files;
    }

    /// <summary>The grid's name for column <paramref name="index"/>; headers can repeat or hold characters a binding cannot.</summary>
    public static string ColumnName(int index) => $"c{index}";

    /// <summary>
    /// The table for a grid: columns named by <see cref="ColumnName"/> with the header as each
    /// caption, numbers typed as numbers so they sort as numbers, and the hidden search column.
    /// </summary>
    public DataTable ToDataTable()
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture, CaseSensitive = false };
        var numeric = new bool[Headers.Count];
        for (var c = 0; c < Headers.Count; c++)
        {
            // Only when every value reads back exactly as written: "0012" stays text, "1.50" stays "1.50".
            numeric[c] = Rows.Any(r => r[c].Length > 0) && Rows.All(r => r[c].Length == 0 || IsNumber(r[c]));
            table.Columns.Add(ColumnName(c), numeric[c] ? typeof(decimal) : typeof(string)).Caption = Headers[c];
        }

        table.Columns.Add(SearchColumn, typeof(string));
        table.BeginLoadData();
        foreach (var row in Rows)
        {
            var values = new object[Headers.Count + 1];
            for (var c = 0; c < Headers.Count; c++)
            {
                values[c] = !numeric[c] ? row[c]
                    : row[c].Length == 0 ? DBNull.Value
                    : decimal.Parse(row[c], NumberStyle, CultureInfo.InvariantCulture);
            }

            values[^1] = string.Join('\t', row);
            table.Rows.Add(values);
        }

        table.EndLoadData();
        table.AcceptChanges();
        return table;
    }

    /// <summary>
    /// A DataView row filter keeping rows that contain every word of <paramref name="query"/>
    /// (any column, any case); empty for no query.
    /// </summary>
    public static string SearchFilter(string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" AND ", words.Select(w => $"{SearchColumn} LIKE '*{EscapeLike(w)}*'"));
    }

    private int IndexOf(string header)
    {
        for (var i = 0; i < Headers.Count; i++)
        {
            if (string.Equals(Headers[i], header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsNumber(string s) =>
        decimal.TryParse(s, NumberStyle, CultureInfo.InvariantCulture, out var d)
        && d.ToString(CultureInfo.InvariantCulture) == s;

    // In a LIKE pattern, * % [ ] are wildcards or brackets unless bracketed; a quote is doubled.
    private static string EscapeLike(string word) => string.Concat(word.Select(ch => ch switch
    {
        '*' or '%' or '[' or ']' => $"[{ch}]",
        '\'' => "''",
        _ => ch.ToString(),
    }));

    /// <summary>Splits CSV into records: quoted fields may hold commas, quotes ("") and line breaks; blank lines are skipped.</summary>
    internal static List<string[]> ParseCsv(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (ch == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }

        return records;

        void EndRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Count > 1 || fields[0].Length > 0)
            {
                records.Add([.. fields]);
            }

            fields.Clear();
        }
    }
}
