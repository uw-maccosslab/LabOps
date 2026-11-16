using LabOps.Core.Acquisition;

namespace LabOps.Tests.Acquisition;

/// <summary>
/// Method text parsing, ported with the parser from maccosslab-raw-method-check's
/// MethodTextParserTests: the same method summaries, embedded in real lab raw files (the
/// instrument computers' names replaced), and the same expectations, so the two copies agree.
/// </summary>
public sealed class MethodTextParserTests
{
    private static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "method-text", name));

    [Fact]
    public void Parses_an_Astral_DIA_method()
    {
        var m = MethodTextParser.ParseMsMethod(Text("astral-dia.ms.txt"));

        m.Title.ShouldBe("Orbitrap Astral Method Summary");
        m.LastModified.ShouldBe("01/19/2024 07:44:58");
        m.GlobalSettings["Use Ion Source Settings from Tune"].ShouldBe("Checked");
        m.GlobalFlags.ShouldContain("Use Static Source Gasses");
        m.Experiments.Count.ShouldBe(2);

        var ms1 = m.Experiments[0].Blocks.ShouldHaveSingleItem();
        ms1.Kind.ShouldBe("Scan");
        ms1.Name.ShouldBe("MasterScan");
        ms1.Settings["Orbitrap Resolution"].ShouldBe("240000");

        var dia = m.Experiments[1];
        dia.Settings["Experiment Name"].ShouldBe("DIA");
        var scan = dia.Blocks.ShouldHaveSingleItem();
        scan.Name.ShouldBe("DIAScan");
        // The Astral editor spells it "Normalised"; Find matches either spelling.
        MethodTextParser.Find(scan.Settings, "Normalized AGC Target (%)").ShouldBe("250");
        MethodTextParser.Find(scan.Settings, "RF Lens (%)").ShouldBe("50");
        scan.MassList.ShouldNotBeNull().RowCount.ShouldBe(125);
        scan.MassList.Columns.ShouldBe(["m/z", "m/z Range"]);
        scan.MassList.Rows[0][1].ShouldBe("400.4319-404.433719");
    }

    [Fact]
    public void Parses_Astral_Zoom_settings()
    {
        var m = MethodTextParser.ParseMsMethod(Text("astral-zoom-dia.ms.txt"));
        var scan = m.Experiments[1].Blocks.ShouldHaveSingleItem();

        m.Title.ShouldBe("Orbitrap Astral Zoom Method Summary");
        m.GlobalSettings["Use Ion Source Settings from Tune"].ShouldBe("Not Checked");
        MethodTextParser.Find(m.GlobalSettings, "Spray Voltage: Positive Ion (V)").ShouldBe("2500");
        scan.Settings["Pre-Accumulation"].ShouldBe("True");
        scan.Settings["Absolute AGC Target"].ShouldBe("20000");
        scan.MassList!.RowCount.ShouldBe(167);
    }

    [Fact]
    public void Parses_Eclipse_DDA_filters_and_scan_events()
    {
        var m = MethodTextParser.ParseMsMethod(Text("eclipse-dda.ms.txt"));
        var blocks = m.Experiments.ShouldHaveSingleItem().Blocks;

        blocks.Select(b => $"{b.Kind} {b.Name}".Trim()).ShouldBe(
            ["Scan MasterScan", "Filter MIPS", "Filter ChargeState", "Filter DynamicExclusion", "Filter IntensityThreshold", "Data Dependent Properties", "Scan ddMSnScan"]);
        m.Experiments[0].Settings["Cycle Time (sec)"].ShouldBe("3");
        blocks.Single(b => b.Name == "DynamicExclusion").Settings["Exclusion duration (s)"].ShouldBe("15");
        blocks.Single(b => b.Name == "ChargeState").Settings["Include charge state(s)"].ShouldBe("2-7");
        // "Data Dependent Mode= Cycle Time" has no space before '='.
        blocks.Single(b => b.Kind == "Data Dependent Properties").Settings["Data Dependent Mode"].ShouldBe("Cycle Time");
        var dd = blocks.Single(b => b.Name == "ddMSnScan");
        dd.ScanEvent.ShouldBe(1);
        dd.Settings["Isolation Window"].ShouldBe("1.4");
        blocks[0].ScanEvent.ShouldBeNull();
    }

    [Fact]
    public void Parses_staggered_Stellar_experiments()
    {
        var m = MethodTextParser.ParseMsMethod(Text("stellar-superoverlap.ms.txt"));

        m.Experiments.Count.ShouldBe(5);
        var dia = m.Experiments.Skip(1).ToList();
        dia.Select(e => e.Settings["Experiment Name"]).ShouldBe(["DIA- Cycle A", "DIA- Cycle B", "DIA- Cycle C", "DIA- Cycle D"]);
        dia.ShouldAllBe(e => e.Blocks.Single().MassList!.RowCount == 126);
        dia[1].Blocks.Single().Settings["Precursor Mass Range (m/z)"].ShouldBe("401.4324-901.6599");
    }

    [Fact]
    public void Parses_a_targeted_mass_list()
    {
        var m = MethodTextParser.ParseMsMethod(Text("stellar-prm-synthetic.ms.txt"));
        var tmsn = m.Experiments[1].Blocks.ShouldHaveSingleItem();

        tmsn.Name.ShouldBe("tMSn");
        m.Experiments[1].Settings["Cycle Time (sec)"].ShouldBe("1.2");
        tmsn.MassList!.Columns.Count.ShouldBe(8);
        tmsn.MassList.RowCount.ShouldBe(3);
        tmsn.MassList.Rows[2][0].ShouldBe("PEPTIDECK+3");
        tmsn.MassList.Rows[2][6].ShouldBe("11.60");
    }

    [Fact]
    public void Parses_a_Stellar_DIA_method_and_keeps_duplicate_keys()
    {
        var m = MethodTextParser.ParseMsMethod(Text("stellar-dia.ms.txt"));

        m.Title.ShouldBe("Stellar Method Summary");
        m.Experiments[1].Settings["Cycle Time (sec)"].ShouldBe("1.71");
        m.Experiments[1].Blocks.Single().Settings["Maximum Injection Time Mode"].ShouldBe("Dynamic");

        // A key repeated in one block is kept with a suffix, not overwritten.
        var dup = MethodTextParser.ParseMsMethod("Global Settings\n\tA = 1\n\tA = 2\n");
        dup.GlobalSettings["A"].ShouldBe("1");
        dup.GlobalSettings["A #2"].ShouldBe("2");
    }

    [Fact]
    public void Leading_number_and_find()
    {
        MethodTextParser.LeadingNumber("2500").ShouldBe(2500);
        MethodTextParser.LeadingNumber("1.300 [µl/min]").ShouldBe(1.3);
        MethodTextParser.LeadingNumber("60K").ShouldBe(60);
        MethodTextParser.LeadingNumber("1E+20").ShouldBe(1e20);
        MethodTextParser.LeadingNumber("-45.0").ShouldBe(-45);
        MethodTextParser.LeadingNumber("\"%B\"").ShouldBeNull();
        MethodTextParser.LeadingNumber("").ShouldBeNull();
        MethodTextParser.LeadingNumber(null).ShouldBeNull();

        var settings = new Dictionary<string, string> { ["RF Lens(%)"] = "50", ["Normalised AGC Target (%)"] = "200" };
        MethodTextParser.Find(settings, "RF Lens (%)").ShouldBe("50");
        MethodTextParser.Find(settings, "normalized agc target (%)").ShouldBe("200");
        MethodTextParser.Find(settings, "Scan Range (m/z)").ShouldBeNull();
        MethodTextParser.Find(null, "anything").ShouldBeNull();
    }

    [Fact]
    public void Parses_a_Vanquish_Neo_trap_and_elute_method()
    {
        var lc = MethodTextParser.ParseLcMethod(Text("neo-trap-elute.lc.txt"), "SII");

        lc.Recognized.ShouldBeTrue();
        lc.SystemSetup.ShouldBe("VanquishNeo");
        lc.InjectionWorkflow.ShouldBe("TrapAndElute");
        lc.FlowRegime.ShouldBe("NanoCap");
        lc.RunTimeMinutes.ShouldBe(24);
        lc.InstrumentLine.ShouldBe("Vanquish_Neo on instrument-pc");
        lc.AnalyticalColumn!.InnerDiameterUm.ShouldBe(150);
        lc.AnalyticalColumn.LengthCm.ShouldBe(15);
        lc.TrapColumn!.InnerDiameterUm.ShouldBe(300);
        lc.TrapColumn.LengthCm.ShouldBe(0.5);
        lc.SolventALabel.ShouldBe("H2O");
        lc.SolventBLabel.ShouldBe("ACN80");
        lc.AutosamplerTemperatureC.ShouldBe(7);
        lc.ColumnTemperatureC.ShouldBeNull();

        // The gradient starts at the Run step; the equilibration before the injection is not part of it.
        lc.Gradient.Select(p => p.TimeMin).ShouldBe([0, 0.7, 1, 21, 21.5, 22, 24]);
        lc.Gradient.Select(p => p.PercentB).ShouldBe([4, 6, 6.5, 40, 55, 99, 99]);
        lc.Gradient.Select(p => p.FlowUlPerMin).ShouldBe([1.3, 1.3, 1, 1, 1.3, 1.3, 1.3]);
        var segment = lc.LongestGradientSegment!;
        (segment.StartMin, segment.EndMin, segment.StartPercentB, segment.EndPercentB, segment.DurationMin).ShouldBe((1, 21, 6.5, 40, 20));
    }

    [Fact]
    public void Reads_the_column_compartment_temperature()
    {
        var lc = MethodTextParser.ParseLcMethod(Text("neo-column-chamber.lc.txt"), "SII");

        lc.ColumnTemperatureC.ShouldBe(30);
        lc.AnalyticalColumn!.LengthCm.ShouldBe(8);
        lc.LongestGradientSegment!.EndPercentB.ShouldBe(38);
    }

    [Fact]
    public void Leaves_an_unknown_LC_format_unrecognized()
    {
        var lc = MethodTextParser.ParseLcMethod("EASY-nLC method\nGradient:\n  0:00  5%\n", "Thermo EASY-nLC");

        lc.Recognized.ShouldBeFalse();
        lc.Gradient.ShouldBeEmpty();
        lc.LongestGradientSegment.ShouldBeNull();
    }

    /// <summary>LabOps' own path: the method text read from a raw file's start, then parsed.</summary>
    [Fact]
    public void A_raw_files_embedded_method_is_parsed_as_its_LC_and_MS_parts()
    {
        var file = RawFileTests.MakeRawFile(new DateTime(2026, 10, 1, 9, 0, 0), lcText: Text("neo-trap-elute.lc.txt"), msText: Text("astral-zoom-dia.ms.txt"));
        var layout = ThermoRawHeader.ReadLayout(file.AsSpan(0, ThermoRawHeader.StartLength), file.Length);

        var method = ThermoMethod.Read(ThermoRawHeader.ReadMethods(file, layout));

        method.Ms!.Title.ShouldBe("Orbitrap Astral Zoom Method Summary");
        method.Ms.Experiments[1].Blocks.Single().MassList!.RowCount.ShouldBe(167);
        method.Lc!.MethodEditor.ShouldBe("SiiXcalibur");
        method.Lc.AnalyticalColumn!.LengthCm.ShouldBe(15);
        method.Lc.LongestGradientSegment!.DurationMin.ShouldBe(20);
    }
}
