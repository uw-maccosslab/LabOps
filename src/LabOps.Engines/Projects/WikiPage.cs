using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>
/// The project's wiki page. LabOps publishes it to the project's folder on Panorama (wiki: in
/// project.yaml), so everyone who can open that folder, the collaborators included, sees where the
/// work stands. The written parts (summary, plan, the samples' description, a sentence per data
/// folder) are in the project's wiki.yaml, by Claude with the update-wiki skill; everything else
/// comes from the records each time. Text in wiki.yaml may use [links](https://...) and **bold**.
/// Ported from project.py's render_wiki, whose page this matches byte for byte (the footer's
/// fingerprints included, so pages published before keep updating on their own).
/// </summary>
public static partial class WikiPage
{
    public sealed record Page(string Project, string? Folder, string PageName, string Title, string Html, bool Written, string WrittenHash);

    // The footer's id. LabOps replaces a page on its own only when the page has it, so a page
    // written by hand is never overwritten without someone choosing to.
    public const string Mark = "labops-wiki";
    private const string LabRepoUrl = "https://github.com/uw-maccosslab/LabOps-Projects";
    private const string HomeLab = "UW-MacCoss";   // the lab's own projects need no "with" line
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly Dictionary<string, string> PanoramaLabels = new() { ["raw"] = "Raw files", ["results"] = "Results", ["qc"] = "Process control" };

    // The house style, inline because a wiki page has no style sheet of its own.
    private static readonly Dictionary<string, string> S = new()
    {
        ["page"] = "max-width:1050px;font-size:14.5px;line-height:1.55;color:#333",
        ["eyebrow"] = "margin:0 0 4px;font-size:12.5px;letter-spacing:.04em;text-transform:uppercase;color:#6b7680",
        ["title"] = "margin:0 0 6px;font-size:24px;font-weight:500;color:#222;line-height:1.25",
        ["lead"] = "margin:0 0 18px;font-size:15.5px;color:#444;max-width:820px",
        ["cards"] = "display:grid;grid-template-columns:repeat(auto-fill,minmax(min(220px,100%),1fr));gap:12px;margin:0 0 8px",
        ["card"] = "padding:14px 16px;background:#f6f9fb;border:1px solid #e3ecf2;border-radius:8px",
        ["card_value"] = "display:block;font-size:21px;font-weight:500;color:#0d4f75;line-height:1.2",
        ["card_detail"] = "display:block;margin-top:3px;color:#6b7680;font-size:13px;line-height:1.35",
        ["h2"] = "margin:28px 0 10px;padding:0 0 6px;font-size:19px;font-weight:500;color:#222;border-bottom:2px solid #116596",
        ["h3"] = "margin:16px 0 6px;font-size:15.5px;font-weight:500;color:#222",
        ["plan"] = "display:grid;grid-template-columns:repeat(auto-fill,minmax(min(240px,100%),1fr));gap:12px;margin:0 0 8px;padding:0;list-style:none",
        ["plan_item"] = "padding:12px 14px;background:#fff;border:1px solid #dde3e8;border-radius:8px",
        ["plan_number"] = "display:inline-block;width:24px;height:24px;margin-right:8px;border-radius:50%;background:#116596;"
                          + "color:#fff;font-size:12.5px;font-weight:500;line-height:24px;text-align:center",
        ["table"] = "width:100%;border-collapse:collapse;font-size:13.5px;margin:0 0 6px",
        ["th"] = "padding:7px 10px;border-bottom:2px solid #c6d9e5;text-align:left;font-weight:500;color:#222;background:#f6f9fb",
        ["group"] = "padding:10px 10px 5px;font-weight:500;color:#116596;border-bottom:1px solid #dde3e8",
        ["td"] = "padding:7px 10px;border-bottom:1px solid #edf0f3;vertical-align:top",
        ["data"] = "display:grid;grid-template-columns:repeat(auto-fill,minmax(min(300px,100%),1fr));gap:12px;margin:0 0 8px",
        ["data_card"] = "padding:12px 16px;background:#fff;border:1px solid #dde3e8;border-radius:8px",
        ["data_h3"] = "margin:0 0 6px;font-size:15.5px;font-weight:500;color:#0d4f75",
        ["icon"] = "margin-right:7px;color:#116596",
        ["footer"] = "margin:28px 0 0;padding:10px 0 0;border-top:1px solid #dde3e8;font-size:12.5px;color:#6b7680",
        ["badge"] = "display:inline-block;padding:1px 8px;border-radius:10px;font-size:12px;font-weight:500;white-space:nowrap;",
    };

    private const string Green = "background:#e3f4e8;color:#1d6b34";
    private const string Blue = "background:#e1effa;color:#0d4f75";
    private const string Gray = "background:#eef1f4;color:#5b6670";
    private const string Amber = "background:#fff4dc;color:#8a5a00";

    private static readonly Dictionary<string, (string, string)> StepBadges = new()
    {
        ["done"] = ("Done", Green), ["in_progress"] = ("In progress", Blue), ["pending"] = ("Not started", Gray), ["skipped"] = ("Skipped", Gray),
    };

    private static readonly Dictionary<string, (string, string)> StatusBadges = new()
    {
        ["active"] = ("Active", Green), ["on_hold"] = ("On hold", Amber), ["closed"] = ("Closed", Gray),
    };

    private static readonly Dictionary<string, string> Icons = new() { ["raw"] = "fa-database", ["results"] = "fa-line-chart", ["qc"] = "fa-check-square-o" };

    // Python's \s, which .NET's differs from in \x1c-\x1f.
    private const string Space = @"\t\n\x0B\x0C\r\x1C-\x1F\x85\p{Zs}\p{Zl}\p{Zp}";

    // A link goes to a web address or to a page on Panorama (/...), never to another site through //.
    [GeneratedRegex(@"\[([^\]]+)\]\(((?:https?://|/(?!/))[^)" + Space + @"]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex MarkdownBold();

    /// <summary>html.escape(str(text), quote=True).</summary>
    public static string Esc(object? text) => Escape(text is null ? "" : PyText.Str(text));

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#x27;", StringComparison.Ordinal);

    private static string Bold(string escaped) => MarkdownBold().Replace(escaped, m => $"<strong>{m.Groups[1].Value}</strong>");

    /// <summary>Text for the page, escaped, keeping [links](https://... or /...) and **bold**.</summary>
    public static string Inline(object? text)
    {
        var s = PyText.Truthy(text) ? PyText.Str(text) : "";
        var output = new StringBuilder();
        var pos = 0;
        foreach (Match m in MarkdownLink().Matches(s))
        {
            output.Append(Bold(Escape(s[pos..m.Index])));
            output.Append($"<a href=\"{Escape(m.Groups[2].Value)}\">{Bold(Escape(m.Groups[1].Value))}</a>");
            pos = m.Index + m.Length;
        }

        output.Append(Bold(Escape(s[pos..])));
        return output.ToString();
    }

    /// <summary>_date: a date or datetime as it is, text read as an ISO date, anything else null.</summary>
    public static object? Date(object? v)
    {
        if (v is DateOnly or PyDateTime)
        {
            return v;
        }

        return PyText.Truthy(v) ? FromIsoFormat(PyText.Str(v)) : null;
    }

    // date.fromisoformat: YYYY-MM-DD, YYYYMMDD, and the week forms YYYY-Www-D, YYYYWwwD, YYYY-Www.
    [GeneratedRegex(@"^(?<y>[0-9]{4})(?:-(?<m>[0-9]{2})-(?<d>[0-9]{2})|(?<m>[0-9]{2})(?<d>[0-9]{2})|-?W(?<w>[0-9]{2})(?:-?(?<wd>[0-9]))?)\z")]
    private static partial Regex IsoDate();

    internal static DateOnly? FromIsoFormat(string text)
    {
        var m = IsoDate().Match(text);
        if (!m.Success)
        {
            return null;
        }

        try
        {
            var year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            if (m.Groups["w"].Success)
            {
                var week = int.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
                var day = m.Groups["wd"].Success ? int.Parse(m.Groups["wd"].Value, CultureInfo.InvariantCulture) : 1;
                if (week < 1 || week > ISOWeek.GetWeeksInYear(year) || day < 1 || day > 7)
                {
                    return null;
                }

                return DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, (DayOfWeek)(day % 7)));
            }

            return new DateOnly(year, int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static (int Year, int Month, int Day) Parts(object d) =>
        d is DateOnly x ? (x.Year, x.Month, x.Day) : (((PyDateTime)d).Value.Year, ((PyDateTime)d).Value.Month, ((PyDateTime)d).Value.Day);

    public static string FormatDate(object d)
    {
        var (y, m, day) = Parts(d);
        return $"{Months[m - 1]} {day}, {y}";
    }

    /// <summary>Sep 9, 2026; Sep 23–29, 2026; Sep 10 – Oct 3, 2026; Dec 30, 2026 – Jan 2, 2027.</summary>
    public static string Range(object? start, object? end)
    {
        var a = Date(start);
        var b = Date(end);
        if (a is null && b is null)
        {
            return "";
        }

        if (a is null || b is null || PyText.Eq(a, b))
        {
            return FormatDate((a ?? b)!);
        }

        var (ay, am, ad) = Parts(a);
        var (by, bm, bd) = Parts(b);
        if (ay != by)
        {
            return $"{FormatDate(a)} – {FormatDate(b)}";
        }

        if (am != bm)
        {
            return $"{Months[am - 1]} {ad} – {Months[bm - 1]} {bd}, {by}";
        }

        return $"{Months[am - 1]} {ad}–{bd}, {by}";
    }

    private static string Badge(string label, string colors) => $"<span style=\"{S["badge"]}{colors}\">{Escape(label)}</span>";

    private static string H2(string text) => $"<h2 style=\"{S["h2"]}\">{Escape(text)}</h2>";

    /// <summary>The project's wiki.yaml, or an empty mapping when Claude has not written one yet.</summary>
    private static PyDict Doc(ProjectRepository repo, string folder)
    {
        var path = Path.Combine(folder, Validation.WikiFile);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var w = YamlLoader.Load(YamlText.ReadText(path, repo.Rel(path)));
            return w as PyDict ?? [];
        }
        catch (YamlProblemException ex)
        {
            throw new EngineError($"{repo.Rel(path)} is not valid YAML: {ex.Problem} (line {ex.Line}); fix it first (run check)");
        }
    }

    private sealed record Counts(int Study, int Qc, List<(string Group, int Count)> Groups);

    /// <summary>Study samples, QC samples and the study samples' groups, from metadata/samples.csv.</summary>
    private static Counts? SampleCounts(string folder)
    {
        var path = Path.Combine(folder, "metadata", "samples.csv");
        if (!File.Exists(path))
        {
            return null;
        }

        var sheets = Sheets.Read(path);
        if (sheets.Count != 1)
        {
            return null;
        }

        var (_, headers, rows) = sheets[0];
        var qc = Validation.IndexOf(headers, "QC");
        var group = Validation.IndexOf(headers, "Sample_Group");
        var study = rows.Where(r => qc < 0 || Sheets.CellText(r[qc]).ToUpperInvariant() != "TRUE").ToList();
        var groups = new List<(string Group, int Count)>();
        if (group >= 0)
        {
            foreach (var r in study)
            {
                var g = Sheets.CellText(r[group]);
                g = g.Length > 0 ? g : "(none)";
                var at = groups.FindIndex(x => x.Group == g);
                if (at < 0)
                {
                    groups.Add((g, 1));
                }
                else
                {
                    groups[at] = (g, groups[at].Count + 1);
                }
            }
        }

        return new Counts(study.Count, rows.Count - study.Count, groups);
    }

    private static string StepDates(PyDict s)
    {
        switch (Steps.Status(s))
        {
            case "done":
                return Range(s["started"], s["finished"]);
            case "in_progress":
                return Date(s["started"]) is { } started ? $"Since {FormatDate(started)}" : "";
            case "skipped":
                return Date(s["finished"]) is not null ? Range(s["finished"], null) : "";
            default:
                return "";
        }
    }

    private static string StepWho(PyDict s, Dictionary<string, string> names)
    {
        var login = Steps.Status(s) is "done" or "skipped"
            ? (PyText.Truthy(s["by"]) ? s["by"] : s["assigned"])
            : s["assigned"];
        return PyText.Truthy(login) ? names.GetValueOrDefault(PyText.Str(login), PyText.Str(login)) : "";
    }

    private static IEnumerable<string> ProgressRows(string group, IEnumerable<PyDict> steps, Dictionary<string, string> names)
    {
        yield return $"<tr><td colspan=\"5\" style=\"{S["group"]}\">{Escape(group)}</td></tr>";
        foreach (var s in steps)
        {
            // project.py stopped on a status it did not know; show it as it is written.
            var (label, colors) = StepBadges.TryGetValue(Steps.Status(s), out var badge) ? badge : (Steps.Status(s), Gray);
            yield return $"<tr><td style=\"{S["td"]};font-weight:500\">{Escape(Steps.Label(s))}</td>"
                         + $"<td style=\"{S["td"]}\">{Badge(label, colors)}</td>"
                         + $"<td style=\"{S["td"]};white-space:nowrap\">{Escape(StepDates(s))}</td>"
                         + $"<td style=\"{S["td"]};white-space:nowrap\">{Escape(StepWho(s, names))}</td>"
                         + $"<td style=\"{S["td"]};color:#555\">{Escape(Values.Text(s["note"]) ?? "")}</td></tr>";
        }
    }

    private static string FundingLine(Values.Funding f)
    {
        if (f.Type == "grant" && f.Grant is not null)
        {
            return $"Funded by {f.Grant}";
        }

        if (f.Type == "quote" && f.Quotes.Count > 0)
        {
            return $"Proteomics Services quote {string.Join(", ", f.Quotes)}";
        }

        return "";
    }

    /// <summary>urllib.parse.quote(text): UTF-8, with letters, digits, _.-~ and / kept.</summary>
    public static string Quote(string text)
    {
        var output = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '~' or '/')
            {
                output.Append(c);
            }
            else
            {
                output.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return output.ToString();
    }

    /// <summary>A file area opens as its WebDAV listing; a folder opens at its begin page.</summary>
    private static string PanoramaHref(string folder)
    {
        var quoted = Quote(folder.TrimEnd('/'));
        return folder.Contains("/@files", StringComparison.Ordinal) ? $"/_webdav{quoted}/" : $"{quoted}/project-begin.view";
    }

    public static bool SameFolder(object? a, object? b) =>
        string.Equals(Py.RStrip(Py.Strip(PyText.Truthy(a) ? PyText.Str(a) : ""), "/").ToLowerInvariant(),
            Py.RStrip(Py.Strip(PyText.Truthy(b) ? PyText.Str(b) : ""), "/").ToLowerInvariant(), StringComparison.Ordinal);

    private static string DocumentLine(object? doc)
    {
        var d = doc as PyDict ?? [];
        var parts = new List<string>();
        if (Values.Int(d["replicates"]) is { } replicates)
        {
            parts.Add($"{Thousands(replicates)} replicates");
        }

        var peptides = Values.Int(d["peptides"]);
        var proteins = Values.Int(d["proteins"]);
        if (peptides is not null)
        {
            parts.Add($"{Thousands(peptides)} peptides" + (proteins is not null ? $" from {Thousands(proteins)} proteins" : ""));
        }

        var uploaded = Date(d["uploaded"]);
        var text = Esc(PyText.Truthy(d["name"]) ? d["name"] : "Skyline document") + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "");
        return text + (uploaded is not null ? $" (uploaded {FormatDate(uploaded)})" : "");
    }

    private static string Thousands(object n) => PyText.ToBig(n).ToString("#,0", CultureInfo.InvariantCulture);

    private static string Sha12(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    /// <summary>
    /// The project's page. `documents` maps a Panorama folder to its Skyline documents ({name,
    /// replicates, peptides, proteins, uploaded}), which LabOps reads from Panorama; without it the
    /// folders are listed without them.
    /// </summary>
    public static Page Render(ProjectRepository repo, string folder, PyDict? documents = null, DateOnly? when = null)
    {
        documents ??= [];
        var today = when ?? repo.Today();
        // A record that cannot be read stops the page rather than leaving part of the project off it.
        var p = ReadRecord(repo, Path.Combine(folder, "project.yaml"));
        var labFolder = Path.GetDirectoryName(folder)!;
        var lab = File.Exists(Path.Combine(labFolder, "lab.yaml")) ? ReadRecord(repo, Path.Combine(labFolder, "lab.yaml")) : [];
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var person in repo.PeopleList())
        {
            names[person.Login] = person.Name ?? person.Login;
        }

        var w = Doc(repo, folder);
        var experiments = new List<(string Name, PyDict Record)>();
        foreach (var ef in ProjectRepository.Records(folder, 1, "experiment.yaml"))
        {
            var e = ReadRecord(repo, ef);
            if (!Equals(e["status"], "closed"))
            {
                experiments.Add((Path.GetFileName(Path.GetDirectoryName(ef))!, e));
            }
        }

        var counts = SampleCounts(folder);
        var title = Values.Text(p["title"]) ?? Path.GetFileName(folder);
        var output = new List<string> { $"<div style=\"{S["page"]}\">" };

        // Who and how it is paid for, the title, the summary.
        var eyebrow = new List<string> { "MacCoss Lab project" };
        if (Path.GetFileName(labFolder) != HomeLab && (PyText.Truthy(lab["pi"]) || PyText.Truthy(lab["institution"])))
        {
            eyebrow.Add("with " + string.Join(", ", new[] { lab["pi"], lab["institution"] }.Where(PyText.Truthy).Select(PyText.Str)));
        }

        if (FundingLine(Values.FundingOf(p["funding"])) is { Length: > 0 } funding)
        {
            eyebrow.Add(funding);
        }

        output.Add($"<p style=\"{S["eyebrow"]}\">{string.Join(" &middot; ", eyebrow.Select(Escape))}</p>");
        var (statusLabel, statusColors) = StatusBadges.GetValueOrDefault(PyText.Str(p["status"]), ("", Gray));
        output.Add($"<h2 style=\"{S["title"]}\">{Escape(title)} {(statusLabel.Length > 0 ? Badge(statusLabel, statusColors) : "")}</h2>");
        if (PyText.Truthy(w["summary"]))
        {
            output.Add($"<p style=\"{S["lead"]}\">{Inline(w["summary"])}</p>");
        }

        // The figures at a glance.
        var cards = new List<(string Value, object? Detail)>();
        if (counts is not null)
        {
            cards.Add(($"{counts.Study} samples", PyText.Truthy(w["samples_card"]) ? w["samples_card"]
                : string.Join(", ", new[] { p["sample_type"], p["species"] }.Where(PyText.Truthy).Select(PyText.Str))));
            if (counts.Qc > 0)
            {
                cards.Add(($"{counts.Qc} QC samples", PyText.Truthy(w["qc_card"]) ? w["qc_card"] : "quality control samples"));
            }
        }
        else if (Values.Int(p["expected_samples"]) is { } expected && PyText.Truthy(expected))
        {
            cards.Add(($"{PyText.Str(expected)} samples", "expected; the sample table is not organized yet"));
        }

        foreach (var (name, e) in experiments.Take(3))
        {
            var acquisition = Steps.Read(e).FirstOrDefault(s => Equals(s["kind"], "data_acquisition") && !Steps.IsInvalid(s));
            var state = "";
            if (acquisition is not null)
            {
                state = Steps.Status(acquisition) switch
                {
                    "done" => $"acquired {Range(acquisition["started"], acquisition["finished"])}",
                    "in_progress" => PyText.Truthy(acquisition["started"]) ? $"acquiring since {Range(acquisition["started"], null)}" : "acquiring",
                    "skipped" => "acquisition skipped",
                    _ => "not acquired yet",
                };
            }

            var first = PyText.Truthy(e["instrument"]) ? Values.Text(e["title"]) : name;
            cards.Add((Values.Text(e["instrument"]) ?? Values.Text(e["title"]) ?? name,
                string.Join("; ", new[] { first, state }.Where(x => !string.IsNullOrEmpty(x)))));
        }

        var timelines = new List<(string Group, List<PyDict> Steps)> { ("Samples", Steps.Read(p)) };
        timelines.AddRange(experiments.Select(x => (x.Name, Steps.Read(x.Record))));
        var now = timelines.Select(t => (t.Group, Step: Steps.Current(t.Steps.Where(x => !Steps.IsInvalid(x)))))
            .FirstOrDefault(t => t.Step is not null);
        if (now.Step is { } current)
        {
            var started = Date(current["started"]);
            cards.Add((Steps.Label(current), $"current step{(now.Group == "Samples" ? "" : $" of {now.Group}")}"
                                             + (started is not null ? $", started {FormatDate(started)}" : "")));
        }
        else
        {
            cards.Add(("Complete", "every step is done"));
        }

        output.Add($"<div style=\"{S["cards"]}\">" + string.Concat(cards.Select(c =>
            $"<div style=\"{S["card"]}\"><span style=\"{S["card_value"]}\">{Escape(c.Value)}</span>"
            + $"<span style=\"{S["card_detail"]}\">{Inline(c.Detail)}</span></div>")) + "</div>");

        // The plan: as written, or each experiment in turn.
        var plan = (PyText.Truthy(w["plan"]) ? Values.Iterate(w["plan"]) : []).Where(PyText.Truthy).Select(PyText.Str).ToList();
        if (plan.Count == 0)
        {
            plan = [.. experiments.Select(x => Values.Text(x.Record["title"]) ?? x.Name)];
        }

        if (plan.Count > 0)
        {
            output.Add(H2("Plan"));
            output.Add($"<ol style=\"{S["plan"]}\">" + string.Concat(plan.Select((x, i) =>
                $"<li style=\"{S["plan_item"]}\"><span style=\"{S["plan_number"]}\">{i + 1}</span>{Inline(x)}</li>")) + "</ol>");
        }

        // Every step, with its dates, who, and its note.
        var header = string.Concat(new[] { "Step", "Status", "Dates", "Who", "Notes" }.Select(h => $"<th style=\"{S["th"]}\">{h}</th>"));
        var rows = new List<string> { $"<tr>{header}</tr>" };
        rows.AddRange(ProgressRows("Samples", Steps.Read(p).Where(s => !Steps.IsInvalid(s)), names));
        foreach (var (name, e) in experiments)
        {
            rows.AddRange(ProgressRows($"{Values.Text(e["title"]) ?? name} ({name})", Steps.Read(e).Where(s => !Steps.IsInvalid(s)), names));
        }

        output.Add(H2("Progress"));
        output.Add($"<div style=\"overflow-x:auto\"><table style=\"{S["table"]}\">" + string.Concat(rows) + "</table></div>");

        // The samples: as Claude described them, or the counts.
        output.Add(H2("Samples"));
        var samples = w["samples"] as PyDict ?? [];
        if (samples.Count > 0)
        {
            if (PyText.Truthy(samples["intro"]))
            {
                output.Add($"<p>{Inline(samples["intro"])}</p>");
            }

            foreach (var section in PyText.Truthy(samples["sections"]) ? Values.Iterate(samples["sections"]) : [])
            {
                if (section is not PyDict sec)
                {
                    continue;
                }

                if (PyText.Truthy(sec["heading"]))
                {
                    output.Add($"<h3 style=\"{S["h3"]}\">{Inline(sec["heading"])}</h3>");
                }

                if (PyText.Truthy(sec["text"]))
                {
                    output.Add($"<p>{Inline(sec["text"])}</p>");
                }

                if (PyText.Truthy(sec["bullets"]))
                {
                    output.Add("<ul>" + string.Concat(Values.Iterate(sec["bullets"]).Select(b => $"<li>{Inline(b)}</li>")) + "</ul>");
                }
            }
        }
        else if (counts is not null)
        {
            var text = $"{counts.Study} study samples" + (counts.Qc > 0 ? $" and {counts.Qc} QC samples" : "") + " in the deidentified sample table.";
            output.Add($"<p>{Escape(text)}</p>");
            if (counts.Groups.Count > 1)
            {
                output.Add("<ul>" + string.Concat(counts.Groups.OrderBy(g => g.Group, StringComparer.Ordinal).ThenBy(g => g.Count)
                    .Select(g => $"<li>{Escape(g.Group)}: {g.Count}</li>")) + "</ul>");
            }
        }
        else
        {
            output.Add("<p>The sample table has not been organized yet.</p>");
        }

        // The protocols used, at the version used: the exact text is in LabOps-Protocols.
        var protocolRows = new List<string>();
        foreach (var (group, d) in new[] { ("Samples", p) }.Concat(experiments.Select(x => (Values.Text(x.Record["title"]) ?? x.Name, x.Record))))
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in Steps.Read(d).Where(s => !Steps.IsInvalid(s)))
            {
                labels[PyText.Str(s["id"])] = Steps.Label(s);
            }

            foreach (var x in Values.Protocols(d))
            {
                var step = labels.TryGetValue(x.Step ?? "", out var l) ? l : x.Step;
                var used = string.Join(", ", new[] { step, group == "Samples" ? null : group }.Where(v => !string.IsNullOrEmpty(v)));
                var version = x.Version is not null && PyText.Truthy(x.Version) ? $"version {PyText.Str(x.Version)}" : "";
                protocolRows.Add($"<tr><td style=\"{S["td"]};font-weight:500\">{Esc(x.Title ?? x.Id)}</td>"
                                 + $"<td style=\"{S["td"]};white-space:nowrap\">{Escape(version)}</td>"
                                 + $"<td style=\"{S["td"]}\">{Escape(used)}</td></tr>");
            }
        }

        if (protocolRows.Count > 0)
        {
            var protocolHeader = string.Concat(new[] { "Protocol", "Version", "Used for" }.Select(h => $"<th style=\"{S["th"]}\">{h}</th>"));
            output.Add(H2("Protocols"));
            output.Add($"<div style=\"overflow-x:auto\"><table style=\"{S["table"]}\"><tr>{protocolHeader}</tr>" + string.Concat(protocolRows) + "</table></div>");
        }

        // Where the data and results are on Panorama.
        var foldersText = w["folders"] as PyDict ?? [];
        var dataCards = new List<string>();
        foreach (var kind in (string[])["raw", "results", "qc"])
        {
            foreach (var (name, e) in experiments)
            {
                foreach (var f in Values.Links(e["panorama"], Values.PanoramaFields))
                {
                    if (f["kind"] != kind || f["folder"] is not { } panoramaFolder)
                    {
                        continue;
                    }

                    var described = foldersText.FirstOrDefault(kv => SameFolder(kv.Key, panoramaFolder)).Value;
                    var last = panoramaFolder.TrimEnd('/').Split('/')[^1];
                    // Without a sentence about it, a folder says which experiment it is from when there are several.
                    var body = new List<string>
                    {
                        PyText.Truthy(described) ? $"<p>{Inline(described)}</p>" : experiments.Count > 1 ? $"<p>From {Escape(name)}.</p>" : "",
                    };
                    // _documents_for: the first matching folder's list; null when LabOps read no such folder.
                    var docs = documents.Where(kv => SameFolder(kv.Key, panoramaFolder)).Select(kv => kv.Value).FirstOrDefault();
                    if (kind == "raw")
                    {
                        body.Add($"<p><a href=\"{Escape(PanoramaHref(panoramaFolder))}\">{Escape(last)} folder</a></p>");
                    }
                    else
                    {
                        body.Add($"<p>Skyline documents in <a href=\"{Escape(PanoramaHref(panoramaFolder))}\">{Escape(last)}</a>"
                                 + (PyText.Truthy(docs) ? ":</p>" : ".</p>"));
                        if (PyText.Truthy(docs))
                        {
                            body.Add("<ul>" + string.Concat(Values.Iterate(docs).Select(x => $"<li>{DocumentLine(x)}</li>")) + "</ul>");
                        }
                        else if (docs is not null)
                        {
                            body.Add("<p>No Skyline documents yet.</p>");
                        }
                    }

                    dataCards.Add($"<div style=\"{S["data_card"]}\"><h3 style=\"{S["data_h3"]}\">"
                                  + $"<span class=\"fa {Icons[kind]}\" style=\"{S["icon"]}\" aria-hidden=\"true\"></span>"
                                  + $"{Escape(PanoramaLabels[kind])}</h3>{string.Concat(body)}</div>");
                }
            }
        }

        if (dataCards.Count > 0)
        {
            output.Add(H2("Data on Panorama"));
            output.Add($"<div style=\"{S["data"]}\">" + string.Concat(dataCards) + "</div>");
        }

        // Where the records are.
        var where = repo.Rel(folder);
        output.Add(H2("Project records"));
        output.Add($"<p>The project is tracked in the lab's <a href=\"{LabRepoUrl}/tree/main/{Quote(where)}\">"
                   + $"LabOps-Projects repository</a> (private on GitHub), in <code>{Escape(where)}</code>:</p>");
        var records = new List<string> { "<li><code>project.yaml</code>: funding, the samples' steps, and links</li>" };
        records.AddRange(experiments.Select(x => $"<li><code>{Escape(x.Name)}/experiment.yaml</code>: {Escape(Values.Text(x.Record["title"]) ?? "an experiment")}, "
                                                 + "its Panorama folders and steps</li>"));
        if (File.Exists(Path.Combine(folder, "metadata", "samples.csv")) || Directory.Exists(Path.Combine(folder, "metadata", "samples.csv")))
        {
            records.Add("<li><code>metadata/samples.csv</code>: the organized, deidentified sample table</li>");
        }

        if (Directory.Exists(Path.Combine(folder, "metadata", "received")))
        {
            records.Add("<li><code>metadata/received/</code>: the collaborator's files as received, deidentified</li>");
        }

        if (Directory.Exists(Path.Combine(folder, "layout")))
        {
            records.Add("<li><code>layout/</code>: the plate layout from Octopus</li>");
        }

        output.Add("<ul>" + string.Concat(records) + "</ul>");

        // The footer marks the page as LabOps's, with a fingerprint of the written parts: LabOps
        // republishes on its own only while they are the ones last published, so new text is
        // reviewed first. data-body fingerprints everything above the footer, exactly as written,
        // so LabOps can tell that someone edited the page on Panorama since it was published.
        var written = w.Count > 0 ? Sha12(PyJson.Dumps(w)) : "none";
        var bodyHash = Sha12(string.Join('\n', output) + "\n");
        var contact = names.TryGetValue(PyText.Str(p["lab_contact"]), out var n) ? n : Values.Text(p["lab_contact"]);
        output.Add($"<div id=\"{Mark}\" data-written=\"{written}\" data-body=\"{bodyHash}\" style=\"{S["footer"]}\">Generated by "
                   + $"LabOps from the LabOps-Projects repository on {FormatDate(today)}."
                   + (string.IsNullOrEmpty(contact) ? "" : $" Lab contact: {Escape(contact)}.") + "</div>");
        output.Add("</div>");
        var wiki = p["wiki"] as PyDict ?? [];
        return new Page(Path.GetFileName(folder), Values.Text(wiki["folder"]), Values.Text(wiki["page"]) ?? "default", title,
            string.Join('\n', output) + "\n", w.Count > 0, written);
    }

    /// <summary>read_record: a record's mapping, or an error to fix it first.</summary>
    public static PyDict ReadRecord(ProjectRepository repo, string path)
    {
        var (d, problem) = Records.Parse(YamlText.ReadText(path, repo.Rel(path)), Path.GetFileName(path));
        return problem is null ? d : throw new EngineError($"{repo.Rel(path)}: {problem}; fix it first (run check)");
    }
}
