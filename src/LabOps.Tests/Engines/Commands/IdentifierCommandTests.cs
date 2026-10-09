using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LabOps.Engines.Projects;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// The identifier rules shared by `scan` (before Claude reads an original) and the pre-commit check.
/// From LabOps-Projects' tests/test_deidentification.py. Its header tests (identifying, ordinary,
/// Patient ID, variants, digits and plurals) are in the golden table DeidentificationTests reads;
/// the rules on a sheet's columns and the scan and sheet commands are here.
/// </summary>
public sealed class IdentifierCommandTests
{
    // The headers of the lab's own blank metadata template (Blank_Metadata.xlsx).
    private static readonly string[] LabTemplate =
    [
        "Box Number", "Box Position", "Sample Identifier", "Sample Type", "Assay", "Sample Group", "QC",
        "Species", "Sex", "Age", "Weight", "Treatment", "Collection date",
        "Other covariate (Relabel column without commas or special characters)",
        "Other covariate (Relabel column without commas or special characters)",
        "Other covariate (Relabel column without commas or special characters)",
    ];

    [Fact]
    public void Phone_numbers_in_other_formats_are_errors_but_barcodes_and_mass_shifts_are_not()
    {
        // The same made-up number twice: written as a phone number it is one, as a bare run of
        // digits it is read as a barcode.
        IReadOnlyList<IReadOnlyList<object?>> rows =
            [["S1", "(206)555-0123", "1234567890", "+57.021464"], ["S2", "+44 20 7946 0958", "2065550123", "C[+57.021]"]];
        var found = Deidentification.SheetFindings(["Sample_ID", "Remarks", "Barcode", "Modification"], rows, "test.csv");
        found.Where(f => f.Level == "ERROR").Select(f => (f.Column, f.Message, f.Count))
            .ShouldBe(new (string?, string, int?)[] { ("Remarks", "contains phone numbers", 2) });
    }

    [Fact]
    public void A_column_without_a_header_is_an_error()
    {
        Findings(["Sample_ID", ""], [["S1", "x"]]).ShouldContain(("ERROR", "column 2", "has values but no header"));
        Findings(["Sample_ID", ""], [["S1", ""]]).ShouldBeEmpty();     // an empty column at the edge is fine
    }

    [Fact]
    public void A_title_row_above_the_headers_is_an_error()
    {
        // The title row is read as the header row, so every column but the first has no header.
        var (headers, rows) = Sheets.SplitRows([["BioTRACK plasma manifest", "", ""], ["Sample_ID", "Last Name", "MRN"],
                                                ["S1", "Doe", "12345678"]]);
        var found = Findings(headers, rows);
        found.ShouldContain(("ERROR", "column 2", "has values but no header"));
        found.ShouldContain(("ERROR", "column 3", "has values but no header"));
    }

    [Theory]
    [InlineData("cp1252")]
    [InlineData("utf-16")]
    [InlineData("utf-8-sig")]
    public void Scan_reads_a_csv_as_excel_saves_it(string encoding)
    {
        // Excel's "CSV (Comma delimited)" is Windows-1252 on Windows, and "Unicode Text" UTF-16.
        using var repo = TestRepo.Create();
        var path = Path.Combine(repo.Root, "inbox", "manifest.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encode("Sample_ID,Volume,Contact email\nS1,50 µL,pat@example.org\n", encoding));
        var output = repo.Ok("scan", path);
        ShouldBeJson(output["sheets"], """[{"sheet": "manifest.csv", "rows": 1, "columns": ["Sample_ID", "Volume", "Contact email"]}]""");
        output["errors"]!.GetValue<int>().ShouldBe(2);
        ShouldBeJson(repo.Ok("sheet", path)["sheets"]![0]!["rows"], """[["S1", "50 µL", "pat@example.org"]]""");
    }

    [Fact]
    public void Scan_says_how_to_save_a_csv_it_cannot_read()
    {
        using var repo = TestRepo.Create();
        var path = Path.Combine(repo.Root, "inbox", "manifest.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.Unicode.GetBytes("Sample_ID,QC\nS1,FALSE\n"));    // UTF-16 without its mark
        repo.Fails("scan", path)["error"]!.GetValue<string>().ShouldContain("save the file as CSV UTF-8", Case.Sensitive);
    }

    [Fact]
    public void Scan_reads_past_the_size_a_workbook_claims()
    {
        // Some programs write a sheet's size too small; read-only openpyxl would stop there.
        using var repo = TestRepo.Create();
        var path = Path.Combine(repo.Root, "inbox", "manifest.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteWorkbook(path, StringCells.Inline, new WorkbookSheet("Sheet",
            [["Sample_ID", "QC", "First Name"], ["S1", "FALSE", "Pat"], ["S2", "FALSE", "Lee"], ["S3", "FALSE", "Kim"]],
            Dimension: "A1:B2"));
        var output = repo.Ok("scan", path);
        var sheet = output["sheets"]![0]!;
        ShouldBeJson(sheet["columns"], """["Sample_ID", "QC", "First Name"]""");
        sheet["rows"]!.GetValue<int>().ShouldBe(3);
        output["findings"]!.AsArray()
            .ShouldContain(f => f!["level"]!.GetValue<string>() == "ERROR" && f["column"]!.GetValue<string>() == "First Name");
        repo.Ok("sheet", path)["sheets"]![0]!["total_rows"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public void Email_and_phone_values_are_errors_in_every_study()
    {
        var found = Findings(["Sample_ID", "Remarks"], [["S1", "dog owner jo@example.org"], ["S2", "(206) 555-0142"], ["S3", "ok"]]);
        found.ShouldContain(("ERROR", "Remarks", "contains email addresses"));
        found.ShouldContain(("ERROR", "Remarks", "contains phone numbers"));
    }

    [Fact]
    public void Collection_dates_and_ages_are_not_flagged()
    {
        // Mike's decision: coded samples with collection dates and ages (MNRF's, for example) are fine,
        // and the work is not human subjects research. A manifest like MNRF's must pass as it is.
        string[] headers = ["Sample_ID", "Collection date", "Draw date", "Age", "Visit", "Specimen Created On"];
        IReadOnlyList<IReadOnlyList<object?>> rows =
        [
            ["S1", "2019-03-14", "43538", "91", "3/14/19", new PyDateTime(new DateTime(2019, 3, 14, 9, 30, 0), null)],
            ["S2", "2019-04-02", "43557", "90+", "V2", new PyDateTime(new DateTime(2019, 4, 2), null)],
            ["S3", "2019", "45000", "54", "V3", "14-Mar-2019"],
        ];
        Findings(headers, rows).ShouldBeEmpty();
    }

    [Fact]
    public void The_lab_template_passes_as_it_is()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows =
            [["1", "A1", "S001", "plasma", "DIA", "Case", "FALSE", "human", "F", "94", "", "none", "2021-06-01", "a", "b", "c"]];
        var found = Deidentification.SheetFindings(LabTemplate, rows, "test.csv");
        found.Where(f => f.Level == "ERROR").ShouldBeEmpty();
        found.ShouldContain(f => f.Level == "WARN" && f.Message.Contains("appears 3 times", StringComparison.Ordinal));
    }

    [Fact]
    public void Free_text_columns_are_flagged()
    {
        Findings(["Sample_ID", "Notes"], [["S1", "Arrived thawed, relabeled by the courier on Tuesday"], ["S2", "fine"]])
            .ShouldContain(("WARN", "Notes", "is free text"));
    }

    [Theory]
    [InlineData(StringCells.Inline)]
    [InlineData(StringCells.Shared)]
    public void Scan_reports_columns_and_findings_but_never_values(StringCells strings)
    {
        using var repo = TestRepo.Create();
        var path = Path.Combine(repo.Root, "inbox", "manifest.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteWorkbook(path, strings, new WorkbookSheet("Samples",
        [
            ["Sample Identifier", "Patient Name", "Collection date", "Contact email"],
            ["S001", "Jane Roe", new DateTime(2019, 3, 14), "jane.roe@example.org"],
            ["S002", "John Doe", new DateTime(2019, 4, 2), "john.doe@example.org"],
        ]));

        var result = repo.Run("scan", path);
        var output = result.Json;
        ShouldBeJson(output["sheets"], """
            [{"sheet": "Samples", "rows": 2, "columns": ["Sample Identifier", "Patient Name", "Collection date", "Contact email"]}]
            """);
        var errors = output["findings"]!.AsArray().Where(f => f!["level"]!.GetValue<string>() == "ERROR")
            .Select(f => f!["column"]!.GetValue<string>()).ToHashSet();
        errors.ShouldBe(["Patient Name", "Contact email"], ignoreOrder: true);
        output["errors"]!.GetValue<int>().ShouldBe(3);  // Contact email: its header and its values
        var text = result.Stdout + repo.RunText("scan", path).Stdout;
        foreach (var value in (string[])["Jane", "Roe", "example.org", "2019-03-14"])
        {
            text.ShouldNotContain(value, Case.Sensitive);
        }
    }

    [Theory]
    [InlineData(StringCells.Inline)]
    [InlineData(StringCells.Shared)]
    public void Sheet_prints_a_workbook_as_csv(StringCells strings)
    {
        using var repo = TestRepo.Create();
        var path = Path.Combine(repo.Root, "inbox", "two.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteWorkbook(path, strings, new WorkbookSheet("First", [["Sample", "Value"], ["A", 1.0]]), new WorkbookSheet("Second", [["Other"]]));
        var output = repo.Ok("sheet", path, "--sheet", "First");
        ShouldBeJson(output["sheets"], """[{"sheet": "First", "headers": ["Sample", "Value"], "rows": [["A", "1"]], "total_rows": 1}]""");
    }

    // -- helpers --------------------------------------------------------------------------------

    /// <summary>(level, column, the message up to its first ; or ,) for each finding on a sheet.</summary>
    private static HashSet<(string Level, string? Column, string Message)> Findings(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows) =>
        [.. Deidentification.SheetFindings(headers, rows, "test.csv").Select(f => (f.Level, f.Column, f.Message.Split(';')[0].Split(',')[0]))];

    private static void ShouldBeJson(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).ShouldBeTrue($"{actual?.ToJsonString()} is not {expected}");

    /// <summary>Text as Python's str.encode writes it: "utf-16" and "utf-8-sig" put the byte-order mark first.</summary>
    private static byte[] Encode(string text, string encoding) => encoding switch
    {
        "cp1252" => CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(text),
        "utf-16" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
        "utf-8-sig" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
        _ => throw new ArgumentException($"no encoding {encoding}", nameof(encoding)),
    };

    /// <summary>Where a workbook keeps its text; the engine reads either.</summary>
    public enum StringCells
    {
        /// <summary>In each cell (t="inlineStr"), as openpyxl 3.1 saves every string: the Python tests' workbooks.</summary>
        Inline,

        /// <summary>In the shared-string table (t="s"), as Excel saves them.</summary>
        Shared,
    }

    /// <summary>One worksheet: its rows, and the size it claims (the real one when not given).</summary>
    private sealed record WorkbookSheet(string Name, object[][] Rows, string? Dimension = null);

    /// <summary>
    /// An .xlsx in the parts the engine reads, its text in each cell or in a shared-string table
    /// (see <see cref="StringCells"/>). A number is in its shortest round-trip form, which for the
    /// whole numbers here is what openpyxl's "%.16g" writes (1.0 is 1), and a datetime is its
    /// serial number in the "yyyy-mm-dd h:mm:ss" format, as openpyxl writes one.
    /// </summary>
    private static void WriteWorkbook(string path, StringCells strings, params WorkbookSheet[] sheets)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.";

        var shared = strings == StringCells.Shared;
        var table = new List<string>();
        XElement Cell(object value, int column, int row)
        {
            var cell = new XElement(main + "c", new XAttribute("r", $"{(char)('A' + column)}{row}"));
            switch (value)
            {
                case string s when !shared:
                    cell.Add(new XAttribute("t", "inlineStr"), new XElement(main + "is", new XElement(main + "t", s)));
                    break;
                case string s:
                    var index = table.IndexOf(s);
                    if (index < 0)
                    {
                        index = table.Count;
                        table.Add(s);
                    }

                    cell.Add(new XAttribute("t", "s"), new XElement(main + "v", index));
                    break;
                case DateTime d:
                    cell.Add(new XAttribute("s", 1), new XElement(main + "v", Number((d - new DateTime(1899, 12, 30)).TotalDays)));
                    break;
                default:
                    cell.Add(new XElement(main + "v", Number(Convert.ToDouble(value, CultureInfo.InvariantCulture))));
                    break;
            }

            return cell;
        }

        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Part(string name, XElement root)
        {
            using var stream = zip.CreateEntry(name).Open();
            new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(stream);
        }

        for (var i = 0; i < sheets.Length; i++)
        {
            var rows = sheets[i].Rows;
            var size = $"A1:{(char)('A' + rows.Max(row => row.Length) - 1)}{rows.Length}";
            Part($"xl/worksheets/sheet{i + 1}.xml", new XElement(main + "worksheet",
                new XElement(main + "dimension", new XAttribute("ref", sheets[i].Dimension ?? size)),
                new XElement(main + "sheetData", rows.Select((row, y) => new XElement(main + "row", new XAttribute("r", y + 1),
                    row.Select((value, x) => Cell(value, x, y + 1)))))));
        }

        // openpyxl writes no table at all when its strings are inline, so neither is there one here.
        if (shared)
        {
            Part("xl/sharedStrings.xml", new XElement(main + "sst", new XAttribute("uniqueCount", table.Count),
                table.Select(s => new XElement(main + "si", new XElement(main + "t", s)))));
        }

        Part("xl/styles.xml", new XElement(main + "styleSheet",
            new XElement(main + "numFmts", new XAttribute("count", 1),
                new XElement(main + "numFmt", new XAttribute("numFmtId", 164), new XAttribute("formatCode", "yyyy-mm-dd h:mm:ss"))),
            new XElement(main + "cellXfs", new XAttribute("count", 2),
                new XElement(main + "xf", new XAttribute("numFmtId", 0)),
                new XElement(main + "xf", new XAttribute("numFmtId", 164), new XAttribute("applyNumberFormat", 1)))));
        Part("xl/workbook.xml", new XElement(main + "workbook", new XAttribute(XNamespace.Xmlns + "r", r),
            new XElement(main + "sheets", sheets.Select((s, i) => new XElement(main + "sheet",
                new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(r + "id", $"rId{i + 1}"))))));
        Part("xl/_rels/workbook.xml.rels", new XElement(rels + "Relationships",
            sheets.Select((_, i) => Relationship(rels, $"rId{i + 1}", RelType + "worksheet", $"worksheets/sheet{i + 1}.xml"))
                .Append(Relationship(rels, $"rId{sheets.Length + 1}", RelType + "styles", "styles.xml")),
            shared ? Relationship(rels, $"rId{sheets.Length + 2}", RelType + "sharedStrings", "sharedStrings.xml") : null));
        Part("_rels/.rels", new XElement(rels + "Relationships", Relationship(rels, "rId1", RelType + "officeDocument", "xl/workbook.xml")));
        Part("[Content_Types].xml", new XElement(types + "Types",
            new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            Override(types, "/xl/workbook.xml", ContentType + "sheet.main+xml"),
            sheets.Select((_, i) => Override(types, $"/xl/worksheets/sheet{i + 1}.xml", ContentType + "worksheet+xml")),
            Override(types, "/xl/styles.xml", ContentType + "styles+xml"),
            shared ? Override(types, "/xl/sharedStrings.xml", ContentType + "sharedStrings+xml") : null));

        static XElement Relationship(XNamespace rels, string id, string type, string target) =>
            new(rels + "Relationship", new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));

        static XElement Override(XNamespace types, string part, string contentType) =>
            new(types + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", contentType));
    }
}
