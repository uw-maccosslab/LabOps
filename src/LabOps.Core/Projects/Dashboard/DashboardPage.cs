using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LabOps.Core.Projects.Dashboard;

/// <summary>What publishing the dashboard would do to the page on Panorama.</summary>
public enum DashboardDecision
{
    /// <summary>Write the new page.</summary>
    Publish,

    /// <summary>The page already says this; write nothing.</summary>
    Unchanged,

    /// <summary>
    /// There is no page yet (only a person creates it), or the page there was not made by LabOps:
    /// leave it alone.
    /// </summary>
    NotOurs,

    /// <summary>Someone changed LabOps's page on Panorama; only a person may replace it.</summary>
    Edited,
}

/// <summary>
/// The lab dashboard as a Panorama wiki page: the bounded summary, then a footer that marks the
/// page as LabOps's and fingerprints everything above it, so a later edit on Panorama is noticed
/// and never overwritten without a person saying so.
/// </summary>
public static partial class DashboardPage
{
    /// <summary>The footer's id, which marks the page as LabOps's to keep up to date.</summary>
    public const string Mark = "labops-dashboard";

    public const string Title = "Lab projects";

    /// <summary>The page for today: the summary and its footer.</summary>
    public static string Build(ProjectList list, DateOnly today)
    {
        var body = DashboardHtml.PanoramaSummary(list, today) + "\n";
        return body + $"<div id=\"{Mark}\" data-body=\"{Fingerprint(body)}\" style=\"margin-top:16px;padding-top:6px;"
                    + "border-top:1px solid #d9e0e6;color:#5b6670;font-size:11px;font-family:'Segoe UI',Arial,sans-serif\">"
                    + "Kept up to date by LabOps from the lab's project records. Change the records in LabOps rather than this "
                    + "page: LabOps stops updating a page edited here until someone publishes it again.</div>\n";
    }

    public static bool IsOurs(string body) => body.Contains($"id=\"{Mark}\"", StringComparison.Ordinal);

    /// <summary>The text above LabOps's footer no longer matches the footer's fingerprint of it.</summary>
    public static bool EditedOnPanorama(string body) =>
        Footer().Match(body) is { Success: true } m && !Fingerprint(body[..m.Index]).Equals(m.Groups[1].Value, StringComparison.Ordinal);

    /// <summary>
    /// What to do, given the page on Panorama (null when there is none) and the new one. A person
    /// publishing (<paramref name="byPerson"/>) creates the page and may replace an edited one;
    /// the background updates only a page LabOps made and nobody has changed since. Neither ever
    /// writes over a page someone else made.
    /// </summary>
    public static DashboardDecision Decide(string? existing, string page, bool byPerson)
    {
        if (existing is null)
        {
            return byPerson ? DashboardDecision.Publish : DashboardDecision.NotOurs;
        }

        if (!IsOurs(existing))
        {
            return DashboardDecision.NotOurs;
        }

        if (string.Equals(existing.Trim(), page.Trim(), StringComparison.Ordinal))
        {
            return DashboardDecision.Unchanged;
        }

        return EditedOnPanorama(existing) && !byPerson ? DashboardDecision.Edited : DashboardDecision.Publish;
    }

    private static string Fingerprint(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    [GeneratedRegex("<div id=\"labops-dashboard\"[^>]*data-body=\"([0-9a-f]+)\"")]
    private static partial Regex Footer();
}
