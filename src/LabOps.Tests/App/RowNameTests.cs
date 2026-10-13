using LabOps.App.ViewModels;
using LabOps.Core.Panorama;
using LabOps.Core.Projects;
using LabOps.Core.Quotes;

namespace LabOps.Tests.App;

/// <summary>
/// What a screen reader (and UI Automation) reads for a row of a list: the row's ToString, which a
/// record would otherwise make every field and nested list of the row.
/// </summary>
public sealed class RowNameTests
{
    [Fact]
    public void A_project_row_reads_as_its_name_and_title()
    {
        var lab = new LabSummary { Lab = "Zoo-Keeper" };

        new ProjectRow(lab, new ProjectSummary { Project = "Otter-Plasma", Title = "Otter plasma proteome" }, null).ToString()
            .ShouldBe("Otter-Plasma, Otter plasma proteome");
        new ProjectRow(lab, new ProjectSummary { Project = "Otter-Plasma" }, null).ToString().ShouldBe("Otter-Plasma");
    }

    [Fact]
    public void A_quote_row_reads_as_its_number_and_title()
    {
        new QuoteSummary { QuoteNumber = "MacCoss-2026-ZOO-OT", Title = "Otter plasma proteome" }.ToString()
            .ShouldBe("MacCoss-2026-ZOO-OT, Otter plasma proteome");
        new QuoteSummary { QuoteNumber = "MacCoss-2026-ZOO-OT", Short = "Otter plasma" }.ToString().ShouldBe("MacCoss-2026-ZOO-OT, Otter plasma");
        new QuoteSummary { QuoteNumber = "MacCoss-2026-ZOO-OT" }.ToString().ShouldBe("MacCoss-2026-ZOO-OT");
    }

    [Fact]
    public void A_notebook_row_reads_as_its_id_and_title()
    {
        new PanoramaNotebook(42, "ELN-42-20260101-1", "Otter plasma prep", "inProgress", "someone", null, false).ToString()
            .ShouldBe("ELN-42-20260101-1, Otter plasma prep");
        new PanoramaNotebook(42, "ELN-42-20260101-1", null, null, null, null, false).ToString().ShouldBe("ELN-42-20260101-1");
    }
}
