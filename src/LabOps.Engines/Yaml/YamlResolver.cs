using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;

namespace LabOps.Engines.Yaml;

/// <summary>The YAML 1.1 tags PyYAML's SafeLoader knows.</summary>
internal static class YamlTags
{
    public const string Str = "tag:yaml.org,2002:str";
    public const string Int = "tag:yaml.org,2002:int";
    public const string Float = "tag:yaml.org,2002:float";
    public const string Bool = "tag:yaml.org,2002:bool";
    public const string Null = "tag:yaml.org,2002:null";
    public const string Timestamp = "tag:yaml.org,2002:timestamp";
    public const string Merge = "tag:yaml.org,2002:merge";
    public const string Value = "tag:yaml.org,2002:value";
    public const string Yaml = "tag:yaml.org,2002:yaml";
    public const string Map = "tag:yaml.org,2002:map";
    public const string Seq = "tag:yaml.org,2002:seq";
}

/// <summary>A value PyYAML would refuse: the problem, and the line it is on (1-based).</summary>
public sealed class YamlProblemException(string problem, int line) : Exception(problem)
{
    public string Problem { get; } = problem;

    public int Line { get; } = line;
}

/// <summary>
/// PyYAML's implicit resolver (yaml/resolver.py) and SafeConstructor's scalar constructors
/// (yaml/constructor.py), which decide what a plain scalar is: `yes` a bool, `012` an octal int,
/// `2026-10-01` a date, `y` and `2026-1-5` text. YamlDotNet follows YAML 1.2, so this is ours.
/// The patterns are PyYAML's verbose ones with their layout whitespace taken out.
/// </summary>
internal static partial class YamlResolver
{
    [GeneratedRegex(@"^(?:yes|Yes|YES|no|No|NO|true|True|TRUE|false|False|FALSE|on|On|ON|off|Off|OFF)$", RegexOptions.CultureInvariant)]
    private static partial Regex BoolPattern();

    [GeneratedRegex(@"^(?:[-+]?(?:[0-9][0-9_]*)\.[0-9_]*(?:[eE][-+][0-9]+)?|\.[0-9][0-9_]*(?:[eE][-+][0-9]+)?|[-+]?[0-9][0-9_]*(?::[0-5]?[0-9])+\.[0-9_]*|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))$", RegexOptions.CultureInvariant)]
    private static partial Regex FloatPattern();

    [GeneratedRegex(@"^(?:[-+]?0b[0-1_]+|[-+]?0[0-7_]+|[-+]?(?:0|[1-9][0-9_]*)|[-+]?0x[0-9a-fA-F_]+|[-+]?[1-9][0-9_]*(?::[0-5]?[0-9])+)$", RegexOptions.CultureInvariant)]
    private static partial Regex IntPattern();

    [GeneratedRegex(@"^(?:<<)$", RegexOptions.CultureInvariant)]
    private static partial Regex MergePattern();

    [GeneratedRegex(@"^(?:~|null|Null|NULL|)$", RegexOptions.CultureInvariant)]
    private static partial Regex NullPattern();

    [GeneratedRegex(@"^(?:[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]|[0-9][0-9][0-9][0-9]-[0-9][0-9]?-[0-9][0-9]?(?:[Tt]|[ \t]+)[0-9][0-9]?:[0-9][0-9]:[0-9][0-9](?:\.[0-9]*)?(?:[ \t]*(?:Z|[-+][0-9][0-9]?(?::[0-9][0-9])?))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^(?:=)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValuePattern();

    [GeneratedRegex(@"^(?:!|&|\*)$", RegexOptions.CultureInvariant)]
    private static partial Regex YamlPattern();

    [GeneratedRegex(@"^(?<year>[0-9][0-9][0-9][0-9])-(?<month>[0-9][0-9]?)-(?<day>[0-9][0-9]?)(?:(?:[Tt]|[ \t]+)(?<hour>[0-9][0-9]?):(?<minute>[0-9][0-9]):(?<second>[0-9][0-9])(?:\.(?<fraction>[0-9]*))?(?:[ \t]*(?<tz>Z|(?<tz_sign>[-+])(?<tz_hour>[0-9][0-9]?)(?::(?<tz_minute>[0-9][0-9]))?))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampParts();

    // Resolvers by first character, in the order PyYAML adds them: bool, float, int, merge, null,
    // timestamp, value, yaml. The first whose pattern matches gives the tag.
    private static readonly (string Tag, Func<Regex> Pattern, string First)[] Resolvers =
    [
        (YamlTags.Bool, BoolPattern, "yYnNtTfFoO"),
        (YamlTags.Float, FloatPattern, "-+0123456789."),
        (YamlTags.Int, IntPattern, "-+0123456789"),
        (YamlTags.Merge, MergePattern, "<"),
        (YamlTags.Null, NullPattern, "~nN"),
        (YamlTags.Timestamp, TimestampPattern, "0123456789"),
        (YamlTags.Value, ValuePattern, "="),
        (YamlTags.Yaml, YamlPattern, "!&*"),
    ];

    /// <summary>The tag a plain scalar resolves to (str when no pattern matches).</summary>
    public static string ResolvePlain(string value)
    {
        if (value.Length == 0)
        {
            return NullPattern().IsMatch(value) ? YamlTags.Null : YamlTags.Str;
        }

        foreach (var (tag, pattern, first) in Resolvers)
        {
            if (first.Contains(value[0], StringComparison.Ordinal) && pattern().IsMatch(value))
            {
                return tag;
            }
        }

        return YamlTags.Str;
    }

    private static readonly Dictionary<string, bool> BoolValues = new(StringComparer.Ordinal)
    {
        ["yes"] = true, ["no"] = false, ["true"] = true, ["false"] = false, ["on"] = true, ["off"] = false,
    };

    /// <summary>
    /// The value of a scalar with a known tag, as SafeConstructor builds it. Where Python would
    /// crash (an impossible date, `!!int abc`), this throws a FormatException for the loader to
    /// report as a problem with the record.
    /// </summary>
    public static object? Construct(string tag, string value) => tag switch
    {
        YamlTags.Str => value,
        YamlTags.Null => null,
        YamlTags.Bool => BoolValues.TryGetValue(value.ToLowerInvariant(), out var b)
            ? b : throw new FormatException($"{value} is not a yes/no value"),
        YamlTags.Int => ConstructInt(value),
        YamlTags.Float => ConstructFloat(value),
        YamlTags.Timestamp => ConstructTimestamp(value),
        _ => throw new FormatException($"could not determine a constructor for the tag '{tag}'"),
    };

    private static object ConstructInt(string text)
    {
        var value = text.Replace("_", "", StringComparison.Ordinal);
        if (value.Length == 0)
        {
            throw new FormatException($"{text} is not a number");
        }

        var sign = value[0] == '-' ? -1 : 1;
        if (value[0] is '+' or '-')
        {
            value = value[1..];
        }

        object magnitude;
        if (value == "0")
        {
            return 0L;
        }
        else if (value.StartsWith("0b", StringComparison.Ordinal))
        {
            magnitude = Digits(value[2..], 2, text);
        }
        else if (value.StartsWith("0x", StringComparison.Ordinal))
        {
            magnitude = Digits(value[2..], 16, text);
        }
        else if (value.Length > 0 && value[0] == '0')
        {
            magnitude = Digits(value, 8, text);
        }
        else if (value.Contains(':', StringComparison.Ordinal))
        {
            BigInteger total = BigInteger.Zero, place = BigInteger.One;
            foreach (var part in value.Split(':').Reverse())
            {
                total += ToBig(Digits(part, 10, text)) * place;
                place *= 60;
            }

            magnitude = Py.Shrink(total);
        }
        else
        {
            magnitude = Digits(value, 10, text);
        }

        return sign < 0 ? Py.Shrink(-ToBig(magnitude)) : magnitude;
    }

    private static object Digits(string digits, int radix, string text)
    {
        if (digits.Length == 0)
        {
            throw new FormatException($"{text} is not a number");
        }

        return Py.ParseInt(digits, radix);
    }

    private static BigInteger ToBig(object v) => v is long l ? l : (BigInteger)v;

    private static double ConstructFloat(string text)
    {
        var value = text.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        if (value.Length == 0)
        {
            throw new FormatException($"{text} is not a number");
        }

        var sign = value[0] == '-' ? -1.0 : 1.0;
        if (value[0] is '+' or '-')
        {
            value = value[1..];
        }

        if (value == ".inf")
        {
            return sign * double.PositiveInfinity;
        }

        if (value == ".nan")
        {
            return double.NaN;
        }

        if (value.Contains(':', StringComparison.Ordinal))
        {
            double total = 0, place = 1;
            foreach (var part in value.Split(':').Reverse())
            {
                total += PythonFloat(part, text) * place;
                place *= 60;
            }

            return sign * total;
        }

        return sign * PythonFloat(value, text);
    }

    private static double PythonFloat(string value, string text) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : throw new FormatException($"{text} is not a number");

    private static object ConstructTimestamp(string value)
    {
        var m = TimestampParts().Match(value);
        if (!m.Success)
        {
            throw new FormatException($"{value} is not a date");
        }

        int Group(string name) => int.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
        try
        {
            var year = Group("year");
            var month = Group("month");
            var day = Group("day");
            if (!m.Groups["hour"].Success || m.Groups["hour"].Value.Length == 0)
            {
                return new DateOnly(year, month, day);
            }

            var fraction = 0;
            if (m.Groups["fraction"].Value is { Length: > 0 } f)
            {
                fraction = int.Parse(f.Length > 6 ? f[..6] : f.PadRight(6, '0'), CultureInfo.InvariantCulture);
            }

            var local = new DateTime(year, month, day, Group("hour"), Group("minute"), Group("second"), DateTimeKind.Unspecified)
                .AddTicks(fraction * 10L);
            TimeSpan? offset = null;
            if (m.Groups["tz_sign"].Success)
            {
                var delta = new TimeSpan(Group("tz_hour"), m.Groups["tz_minute"].Success ? Group("tz_minute") : 0, 0);
                if (delta >= TimeSpan.FromHours(24))
                {
                    throw new FormatException($"{value} has an offset of a day or more");
                }

                offset = m.Groups["tz_sign"].Value == "-" ? -delta : delta;
            }
            else if (m.Groups["tz"].Success)
            {
                offset = TimeSpan.Zero;
            }

            return new PyDateTime(local, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new FormatException($"{value} is not a real date");
        }
    }
}
