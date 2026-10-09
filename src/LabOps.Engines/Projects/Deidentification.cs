using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>
/// The identifier rules, run on every sheet before Claude reads an original (scan) and on every
/// staged file before a commit (check --staged). The point is to keep identifiers out of git
/// history: once pushed, history keeps them, so the check runs before the commit, not after.
/// The rules look for what identifies a person directly: names, contact details, addresses, record
/// numbers, dates of birth. Collection dates and ages are not checked (Mike's decision of
/// 2026-10-03: for coded samples the funder and UW consider them fine). Messages never include a
/// cell's value, so a scan is safe to share. Ported from project.py, rule for rule.
/// </summary>
public static partial class Deidentification
{
    /// <summary>
    /// One finding: where, and what. Count is how many values, for content rules; Fingerprint, for a
    /// free-text column, identifies its text, so a person's review of it can be recorded (Reviews).
    /// </summary>
    public sealed record Finding(string Level, string File, string? Column, string Message, int? Count = null, string? Fingerprint = null)
    {
        public JsonObject Json()
        {
            var o = new JsonObject { ["level"] = Level, ["file"] = File, ["column"] = Values.Json(Column), ["message"] = Message };
            if (Count is { } n)
            {
                o["count"] = n;
            }

            return o;
        }

        /// <summary>_finding_text: "file [column]: message (N values)".</summary>
        public string Text() => File + (string.IsNullOrEmpty(Column) ? "" : $" [{Column}]") + ": " + Message
                                + (Count is > 0 ? $" ({Count} values)" : "");
    }

    public static Finding Error(string name, string message, string? column = null) => new("ERROR", name, column, message);

    // A header with "name" is identifying unless the word before it says what is being named.
    private static readonly HashSet<string> NameQualifiers = new(StringComparer.Ordinal)
    {
        "sample", "file", "species", "search", "project", "group", "method", "instrument", "protein",
        "peptide", "gene", "condition", "treatment", "strain", "cell", "line", "compound", "drug", "plate",
        "batch", "run", "replicate", "tissue", "assay", "study", "cohort", "site", "column", "well", "box",
        "tube", "vial", "specimen", "fraction", "analyte", "sequence", "acquisition", "raw", "experiment",
        "common", "scientific", "taxon", "organism", "product", "reagent", "kit", "antibody", "chemical",
        "metabolite", "lipid", "feature", "variable", "covariate", "label", "tag", "channel", "lab",
        "precursor", "transition", "modification", "biofluid", "matrix", "source", "folder", "dataset",
    };

    // (words that must all appear, message)
    private static readonly (string[] Needed, string What)[] Identifying =
    [
        (["first", "name"], "a person's name"), (["last", "name"], "a person's name"), (["full", "name"], "a person's name"),
        (["surname"], "a person's name"), (["prenom"], "a person's name"), (["apellido"], "a person's name"),
        (["initials"], "a person's initials"), (["ssn"], "a Social Security number"),
        (["social", "security"], "a Social Security number"), (["mrn"], "a medical record number"),
        (["medical", "record"], "a medical record number"), (["dob"], "a date of birth"), (["birth"], "a date of birth"),
        (["birthdate"], "a date of birth"), (["birthday"], "a date of birth"), (["address"], "an address"),
        (["street"], "an address"), (["city"], "a location smaller than a state"),
        (["county"], "a location smaller than a state"), (["zip"], "a ZIP code"), (["zipcode"], "a ZIP code"),
        (["postal"], "a postal code"), (["postcode"], "a postal code"), (["email"], "an email address"),
        (["e", "mail"], "an email address"), (["phone"], "a phone number"), (["telephone"], "a phone number"),
        (["mobile", "number"], "a phone number"), (["fax"], "a fax number"), (["owner"], "an animal owner's details"),
        (["contact"], "contact details"),
    ];

    private static readonly HashSet<string> Keywords = [.. Identifying.SelectMany(i => i.Needed), "name"];

    private static readonly Dictionary<string, string> IdentifyingMessages = Identifying
        .GroupBy(i => string.Concat(i.Needed)).ToDictionary(g => g.Key, g => g.Last().What, StringComparer.Ordinal);

    // Identifying words that also count inside a run-together header (Homeaddress, Patientemail), and
    // short ones that count at its end (PatientDOB). Short words are not looked for inside others:
    // "Specificity" holds no city, and "mRNA" no medical record number.
    private static readonly string[] RunTogether =
    [
        .. new[]
        {
            "address", "birth", "birthdate", "birthday", "email", "phone", "telephone", "surname", "prenom",
            "apellido", "initials", "firstname", "lastname", "fullname", "zipcode", "postcode", "postal",
            "socialsecurity", "medicalrecord",
        }.Order(StringComparer.Ordinal),
    ];

    private static readonly string[] RunTogetherEndings = [.. new[] { "dob", "mrn", "ssn", "zip" }.Order(StringComparer.Ordinal)];

    // "Initial" alone, or someone's initial: Initial, Middle Initial, Patient Initial.
    private static readonly HashSet<string> InitialOf = new(StringComparer.Ordinal) { "patient", "subject", "donor", "participant", "pt", "middle", "owner" };

    private static readonly HashSet<string> FreeTextHeaders = new(StringComparer.Ordinal)
        { "notes", "note", "comments", "comment", "description", "remarks", "remark", "observations" };

    public const string Unheaded = "has values but no header, so the check cannot tell what it holds; give it a header that says what "
                                   + "it is, or remove it (if the sheet has a title row above its headers, delete that row)";

    public const string SharedPage = "has an email address or phone number; the wiki page is shared with the collaborators, so leave "
                                     + "contact details out";

    // What Python's \s matches (str.isspace: \t-\r, \x1c-\x1f, \x85 and the Unicode separators),
    // to go inside a character class. .NET's \s leaves out \x1c-\x1f.
    private const string S = @"\t\n\x0B\x0C\r\x1C-\x1F\x85\p{Zs}\p{Zl}\p{Zp}";

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    public static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?1[-." + S + @"]?)?(?:\(\d{3}\)[-." + S + @"]?|\d{3}[-." + S + @"])\d{3}[-." + S + @"]\d{4}(?!\d)")]
    private static partial Regex PhonePattern();

    // An international number starts with + and its country code (+44 20 7946 0958). Dots are not
    // taken as separators, so a mass shift such as +57.021464 is not a phone number. Python's \w is
    // letters, numbers and _.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_+])\+\d[\d" + S + @"()-]{6,}\d")]
    private static partial Regex IntlPhonePattern();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelCase();

    [GeneratedRegex(@"(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])")]
    private static partial Regex LetterDigit();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonWord();

    /// <summary>
    /// A header's words, lowercase: "Patient_Name" and "PatientName" are [patient, name]. Fullwidth
    /// letters, accents and invisible characters are folded away first; digits stand apart
    /// ("Address1" is [address, 1]); and the plural of an identifying word is the word ("Names").
    /// </summary>
    public static List<string> HeaderWords(string header)
    {
        var text = header.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormKD);
        var kept = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.Format))
            {
                kept.Append(rune.ToString());
            }
        }

        var spaced = CamelCase().Replace(kept.ToString(), "$1 $2");
        spaced = LetterDigit().Replace(spaced, " ");
        return [.. NonWord().Split(spaced.ToLowerInvariant()).Where(w => w.Length > 0).Select(Singular)];
    }

    private static string Singular(string word)
    {
        if (Keywords.Contains(word))
        {
            return word;
        }

        foreach (var (ending, replacement) in (ReadOnlySpan<(string, string)>)[("ies", "y"), ("es", ""), ("s", "")])
        {
            if (word.EndsWith(ending, StringComparison.Ordinal) && Keywords.Contains(word[..^ending.Length] + replacement))
            {
                return word[..^ending.Length] + replacement;
            }
        }

        return word;
    }

    /// <summary>A North American number written with separators, or an international one. Ten digits run together are not one: sample barcodes look the same.</summary>
    public static bool HasPhone(string text) =>
        PhonePattern().IsMatch(text) || IntlPhonePattern().Matches(text).Any(m => m.Value.Count(char.IsDigit) >= 8);

    public static bool HasContactDetails(string text) => EmailPattern().IsMatch(text) || HasPhone(text);

    /// <summary>(level, reason) when a column header names identifying information.</summary>
    public static (string Level, string Reason)? CheckHeader(string header)
    {
        var words = HeaderWords(header);
        var compact = string.Concat(words);
        foreach (var (needed, what) in Identifying)
        {
            var joined = string.Concat(needed);
            if (needed.All(words.Contains) || words.Contains(joined) || joined == compact)
            {
                return ("ERROR", $"looks like {what}");
            }
        }

        const string Rename = "looks like a person's name (rename it if it names something else, for example Sample Name)";
        var i = words.IndexOf("name");
        if (i >= 0 && (i == 0 || !NameQualifiers.Contains(words[i - 1])))
        {
            return ("ERROR", Rename);
        }

        foreach (var w in words)
        {
            if (w.EndsWith("name", StringComparison.Ordinal) && w != "name" && !NameQualifiers.Contains(w[..^4]))
            {
                return ("ERROR", Rename);
            }
        }

        foreach (var w in words)
        {
            var hit = RunTogether.FirstOrDefault(t => w.Contains(t, StringComparison.Ordinal))
                      ?? RunTogetherEndings.FirstOrDefault(t => w.EndsWith(t, StringComparison.Ordinal));
            if (hit is not null)
            {
                return ("ERROR", $"looks like {IdentifyingMessages[hit]}");
            }
        }

        if (words.Contains("initial") && (words.Count == 1 || words.Any(InitialOf.Contains)))
        {
            return ("ERROR", "looks like a person's initials");
        }

        if (words.Contains("patient"))
        {
            return ("WARN", "check that this is a study code, not a medical record number");
        }

        return null;
    }

    /// <summary>Findings for one sheet.</summary>
    public static List<Finding> SheetFindings(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, string source) =>
        ColumnFindings([.. headers.Select((h, i) => (h, (IReadOnlyList<object?>)[.. rows.Select(r => r[i])]))], source);

    /// <summary>Findings for (header, values) columns, from a sheet or from structured data.</summary>
    public static List<Finding> ColumnFindings(IReadOnlyList<(string Header, IReadOnlyList<object?> Values)> columns, string source, bool repeatedHeaders = true)
    {
        var found = new List<Finding>();
        var seen = new List<(string Header, int Count)>();
        for (var i = 0; i < columns.Count; i++)
        {
            var (h, values) = columns[i];
            if (h.Length == 0)
            {
                // A column the check cannot name is one it cannot clear. A title row above the headers
                // leaves every column but the first without one.
                if (values.Any(v => v is not (null or "")))
                {
                    found.Add(new Finding("ERROR", source, $"column {i + 1}", Unheaded));
                }

                continue;
            }

            var at = seen.FindIndex(s => s.Header == h);
            if (at < 0)
            {
                seen.Add((h, 1));
            }
            else
            {
                seen[at] = (h, seen[at].Count + 1);
            }

            if (CheckHeader(h) is { } hit)
            {
                found.Add(new Finding(hit.Level, source, h, $"{hit.Reason}; remove the column or replace it with a study code"));
            }
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var (h, all) = columns[i];
            var values = all.Where(v => v is not (null or "")).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            var label = h.Length > 0 ? h : $"column {i + 1}";
            var texts = values.Select(Sheets.CellText).ToList();
            var words = HeaderWords(h).ToHashSet(StringComparer.Ordinal);
            var emails = texts.Count(t => EmailPattern().IsMatch(t));
            if (emails > 0)
            {
                found.Add(new Finding("ERROR", source, label, "contains email addresses", emails));
            }

            var phones = texts.Count(HasPhone);
            if (phones > 0)
            {
                found.Add(new Finding("ERROR", source, label, "contains phone numbers", phones));
            }

            var longText = texts.Count(t => PyText.Split(t).Count >= 5);
            if (words.Overlaps(FreeTextHeaders) || longText >= Math.Max(2, texts.Count / 5))
            {
                found.Add(new Finding("WARN", source, label, "is free text; check it for names, contact details or other identifiers",
                    Fingerprint: h.Length > 0 ? Reviews.Fingerprint(texts) : null));
            }
        }

        if (repeatedHeaders)
        {
            foreach (var (h, n) in seen.Where(s => s.Count > 1))
            {
                found.Add(new Finding("WARN", source, h, $"header appears {n} times; give each column its own name"));
            }
        }

        return found;
    }

    private const string NoKey = "(values with no key)";

    /// <summary>JSON or YAML sample data, checked like a sheet: every key is a header holding every value found under it anywhere.</summary>
    public static List<Finding> StructuredFindings(object? doc, string source)
    {
        var columns = new List<(string Header, List<object?> Values)>();
        List<object?> Column(string key)
        {
            var at = columns.FindIndex(c => c.Header == key);
            if (at >= 0)
            {
                return columns[at].Values;
            }

            columns.Add((key, []));
            return columns[^1].Values;
        }

        void Walk(object? value, string key)
        {
            switch (value)
            {
                case PyDict d:
                    foreach (var (k, v) in d)
                    {
                        Column(PyText.Str(k));
                        Walk(v, PyText.Str(k));
                    }

                    break;
                case List<object?> list:
                    foreach (var v in list)
                    {
                        Walk(v, key);
                    }

                    break;
                case null:
                    break;
                default:
                    Column(key).Add(value);
                    break;
            }
        }

        Walk(doc, NoKey);
        var found = ColumnFindings([.. columns.Select(c => (c.Header, (IReadOnlyList<object?>)c.Values))], source, repeatedHeaders: false);
        if (columns.Any(c => c.Header == NoKey && c.Values.Count > 0))
        {
            found.Add(new Finding("ERROR", source, null, "has values that are not under any key, so the check cannot tell what they are; "
                                                         + "write the data as records with named fields, or as CSV"));
        }

        return found;
    }

    [GeneratedRegex("^:?-+:?$")]
    private static partial Regex MarkdownSeparator();

    /// <summary>Markdown sample data: each table checked like a sheet, every other line for email addresses and phone numbers.</summary>
    public static List<Finding> MarkdownFindings(string text, string source)
    {
        var found = new List<Finding>();
        var table = new List<IReadOnlyList<object?>>();
        int emails = 0, phones = 0;
        void Flush()
        {
            var rows = table.Where(r => !r.Cast<string>().Where(c => c.Length > 0).All(c => MarkdownSeparator().IsMatch(c))).ToList();
            if (rows.Count > 0)
            {
                var (headers, body) = Sheets.SplitRows(rows);
                found.AddRange(SheetFindings(headers, body, source));
            }

            table.Clear();
        }

        foreach (var line in SplitLines(text))
        {
            var stripped = Py.Strip(line);
            if (stripped.StartsWith('|'))
            {
                table.Add([.. stripped.Trim('|').Split('|').Select(c => (object?)Py.Strip(c))]);
                continue;
            }

            Flush();
            emails += EmailPattern().IsMatch(line) ? 1 : 0;
            phones += HasPhone(line) ? 1 : 0;
        }

        Flush();
        foreach (var (count, what) in (ReadOnlySpan<(int, string)>)[(emails, "email addresses"), (phones, "phone numbers")])
        {
            if (count > 0)
            {
                found.Add(new Finding("ERROR", source, null, $"contains {what}", count));
            }
        }

        return found;
    }

    /// <summary>str.splitlines(): every Python line boundary, not just \n.</summary>
    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\n' or '\r' or '\x0B' or '\x0C' or '\x1C' or '\x1D' or '\x1E' or '\x85' || c is (char)0x2028 or (char)0x2029)
            {
                lines.Add(text[start..i]);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>Sample data, checked like a sheet whatever its format: CSV, JSON, YAML or Markdown.</summary>
    public static List<Finding> DataFindings(string name, string text)
    {
        var suffix = Suffix(name);
        try
        {
            if (suffix == ".csv")
            {
                var (headers, rows) = Sheets.CsvTable(text, name);
                return SheetFindings(headers, rows, name);
            }

            if (suffix == ".md")
            {
                return MarkdownFindings(text, name);
            }

            return StructuredFindings(suffix == ".json" ? PyJsonDecoder.Loads(text) : YamlLoader.Load(text), name);
        }
        catch (EngineError ex)
        {
            return [Error(name, $"could not be read, so it cannot be checked: {ex.Message}")];
        }
        catch (PyJsonException ex)
        {
            return [Error(name, $"is not valid JSON, so it cannot be checked ({ex.Message})")];
        }
        catch (YamlProblemException ex)
        {
            return [Error(name, $"is not valid YAML, so it cannot be checked ({ex.Problem} (line {ex.Line}))")];
        }
    }

    /// <summary>Path(name).suffix, lowercase: ".csv", or "" for ".gitkeep" and "README".</summary>
    public static string Suffix(string name)
    {
        var file = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        var i = file.LastIndexOf('.');
        return i > 0 && i < file.Length - 1 ? file[i..].ToLowerInvariant() : "";
    }

    /// <summary>_wiki_strings: every string in a value, with where it is (a.b[2].c).</summary>
    public static IEnumerable<(string Where, string Text)> Strings(object? value, string where = "")
    {
        switch (value)
        {
            case PyDict d:
                foreach (var (k, v) in d)
                {
                    foreach (var s in Strings(v, where.Length > 0 ? $"{where}.{PyText.Str(k)}" : PyText.Str(k)))
                    {
                        yield return s;
                    }
                }

                break;
            case List<object?> list:
                for (var i = 0; i < list.Count; i++)
                {
                    foreach (var s in Strings(list[i], $"{where}[{i}]"))
                    {
                        yield return s;
                    }
                }

                break;
            case string text:
                yield return (where, text);
                break;
        }
    }
}
