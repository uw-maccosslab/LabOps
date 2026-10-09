using System.Text.Json;
using LabOps.Engines.Projects;

namespace LabOps.Tests.Engines;

/// <summary>
/// The identifier rules against project.py's: every header from LabOps-Projects' own tests
/// (identifying, ordinary, digits and plurals, the lab's template) plus Unicode and run-together
/// forms, and the phone and email rules on text. A difference here could let an identifier through.
/// </summary>
public sealed class DeidentificationTests
{
    private static readonly JsonElement Table = Golden.Table("deidentification.json");

    [Fact]
    public void Headers_are_judged_as_the_Python_engine_judged_them()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Table.GetProperty("headers").EnumerateArray())
        {
            var header = c.GetProperty("header").GetString()!;
            var words = c.GetProperty("words").EnumerateArray().Select(w => w.GetString()!).ToList();
            var actualWords = Deidentification.HeaderWords(header);
            mismatches.Check(actualWords.SequenceEqual(words),
                () => $"words of {JsonSerializer.Serialize(header)}: python [{string.Join(", ", words)}] c# [{string.Join(", ", actualWords)}]");

            var hit = c.GetProperty("hit");
            var expected = hit.ValueKind == JsonValueKind.Null ? null : $"{hit[0].GetString()}: {hit[1].GetString()}";
            var actual = Deidentification.CheckHeader(header) is { } h ? $"{h.Level}: {h.Reason}" : null;
            mismatches.Check(actual == expected, () => $"{JsonSerializer.Serialize(header)}\n  python: {expected}\n  c#:     {actual}");
        }

        mismatches.Checked.ShouldBeGreaterThan(200);
        mismatches.ShouldBeNone();
    }

    [Fact]
    public void Phone_numbers_and_email_addresses_are_found_as_the_Python_engine_found_them()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Table.GetProperty("contacts").EnumerateArray())
        {
            var text = c.GetProperty("text").GetString()!;
            mismatches.Check(Deidentification.HasPhone(text) == c.GetProperty("phone").GetBoolean(), () => $"phone in {JsonSerializer.Serialize(text)}");
            mismatches.Check(Deidentification.HasContactDetails(text) == c.GetProperty("contact").GetBoolean(), () => $"contact in {JsonSerializer.Serialize(text)}");
        }

        mismatches.ShouldBeNone();
    }

    [Theory]
    [InlineData("First Name")]
    [InlineData("PatientDOB")]
    [InlineData("Homeaddress")]
    [InlineData("Owner")]
    public void Identifying_headers_are_errors(string header) => Deidentification.CheckHeader(header)!.Value.Level.ShouldBe("ERROR");

    [Theory]
    [InlineData("Sample Name")]
    [InlineData("mRNA level")]
    [InlineData("Specificity")]
    [InlineData("Collection date")]
    public void Ordinary_headers_pass(string header) => Deidentification.CheckHeader(header).ShouldBeNull();
}
