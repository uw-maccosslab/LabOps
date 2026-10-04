using System.Data;
using LabOps.App.ViewModels;
using LabOps.Core.Projects;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Projects;

/// <summary>Reading a project's sample table for the View samples grid.</summary>
public sealed class SampleTableTests
{
    private const string Samples =
        "Sample_ID,Client_Sample_ID,QC,Sample_Group,Age,Volume_uL,Tube_Note\n"
        + "S0001,0012,FALSE,Case,64,1.50,\n"
        + "S0002,0013,FALSE,Control,7,2.25,\"hemolysis, slight\"\n"
        + "S0003,0014,FALSE,Case,101,10,\"label said \"\"B\"\"\"\n"
        + "Pool-1,,TRUE,Pool,,1.00,\"two\nlines\"\n"
        + "\n";

    [Fact]
    public void Quoted_fields_keep_their_commas_quotes_and_line_breaks()
    {
        var table = SampleTable.Parse(Samples);

        table.Headers.ShouldBe(["Sample_ID", "Client_Sample_ID", "QC", "Sample_Group", "Age", "Volume_uL", "Tube_Note"]);
        table.Rows.Count.ShouldBe(4);
        table.Rows[1][6].ShouldBe("hemolysis, slight");
        table.Rows[2][6].ShouldBe("label said \"B\"");
        table.Rows[3][6].ShouldBe("two\nlines");
    }

    [Fact]
    public void Qc_rows_are_counted_apart_from_study_samples()
    {
        SampleTable.Parse(Samples).Summary.ShouldBe("4 rows: 3 study samples, 1 QC");
        SampleTable.Parse("Subject,Visit\nA,1\nB,2\n").Summary.ShouldBe("2 rows");
    }

    [Fact]
    public void A_byte_order_mark_crlf_and_ragged_rows_are_read()
    {
        var table = SampleTable.Parse("﻿Sample_ID,,Group\r\nS1,x\r\nS2,y,Case,extra\r\n");

        table.Headers.ShouldBe(["Sample_ID", "Column 2", "Group", "Column 4"]);
        table.Rows.ShouldAllBe(r => r.Length == 4);
        table.Rows[0].ShouldBe(["S1", "x", "", ""]);
    }

    [Fact]
    public void Columns_of_numbers_sort_as_numbers_but_identifiers_with_leading_zeros_stay_text()
    {
        var data = SampleTable.Parse(Samples).ToDataTable();

        data.Columns[SampleTable.ColumnName(0)]!.Caption.ShouldBe("Sample_ID");
        data.Columns[SampleTable.ColumnName(1)]!.DataType.ShouldBe(typeof(string));   // 0012 is an identifier
        data.Columns[SampleTable.ColumnName(4)]!.DataType.ShouldBe(typeof(decimal));  // Age, with a blank
        data.Columns[SampleTable.ColumnName(5)]!.DataType.ShouldBe(typeof(decimal));
        data.Rows[0][SampleTable.ColumnName(5)].ToString().ShouldBe("1.50");           // shown as written

        var view = data.DefaultView;
        view.Sort = $"{SampleTable.ColumnName(4)} DESC";
        view.Cast<DataRowView>().Select(r => r[SampleTable.ColumnName(0)]).ShouldBe(["S0003", "S0001", "S0002", "Pool-1"]);
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData("case", 2)]
    [InlineData("CASE s0003", 1)]
    [InlineData("hemolysis, slight", 1)]
    [InlineData("\"B\"", 1)]
    [InlineData("*", 0)]
    [InlineData("[x] 50% o'brien", 0)]
    public void The_search_keeps_rows_containing_every_word(string query, int rows)
    {
        var view = SampleTable.Parse(Samples).ToDataTable().DefaultView;

        view.RowFilter = SampleTable.SearchFilter(query);

        view.Count.ShouldBe(rows);
    }

    [Fact]
    public void The_window_shows_the_organized_table_first_then_each_file_as_received()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("metadata", "received"));
        File.WriteAllText(dir.Combine("metadata", "samples.csv"), Samples);
        File.WriteAllText(dir.Combine("metadata", "received", "manifest.csv"), "Tube,Box\n1,A\n");
        File.WriteAllText(dir.Combine("metadata", "received", "notes.txt"), "not a table");

        var vm = new SamplesViewModel("Marten-Plasma", dir.Path);

        vm.Title.ShouldBe("Samples: Marten-Plasma");
        vm.Files.Select(f => f.Label).ShouldBe(["Sample table (samples.csv)", "As received: manifest.csv"]);
    }

    [Fact]
    public async Task The_window_reads_the_table_and_filters_it_as_you_type()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("metadata", "received"));
        File.WriteAllText(dir.Combine("metadata", "samples.csv"), Samples);
        File.WriteAllText(dir.Combine("metadata", "received", "manifest.csv"), "Tube,Box\n1,A\n2,B\n");
        var vm = new SamplesViewModel("Marten-Plasma", dir.Path);

        await vm.InitializeAsync();
        vm.Rows.ShouldNotBeNull().Count.ShouldBe(4);
        vm.Summary.ShouldBe("4 rows: 3 study samples, 1 QC");

        vm.Query = "control";
        vm.Rows!.Count.ShouldBe(1);
        vm.Summary.ShouldBe("Showing 1 of 4 rows: 3 study samples, 1 QC");

        vm.SelectedFile = vm.Files[1];
        await vm.Loading;
        vm.Table!.Headers.ShouldBe(["Tube", "Box"]);
        vm.Summary.ShouldBe("Showing 0 of 2 rows");  // the search carries over
        vm.Query = "";
        vm.Summary.ShouldBe("2 rows");
    }
}
