using System.Globalization;

namespace LabOps.Engines.CommandLine;

/// <summary>A usage error: what argparse would print after "error:", and exit 2.</summary>
public sealed class UsageError(string message) : Exception(message);

public enum OptionKind
{
    Value,
    Flag,
    Append,
}

/// <summary>An option such as --title T, --human or --quote Q (repeatable).</summary>
public sealed record Option(string Name, OptionKind Kind = OptionKind.Value, bool Required = false, string[]? Choices = null, bool Integer = false, string? Default = null);

/// <summary>A positional argument: one, optional ('?'), one or more ('+') or any number ('*').</summary>
public sealed record Positional(string Name, char Count = '1', string[]? Choices = null);

/// <summary>What was given on the command line, by name (options without their dashes).</summary>
public sealed class Parsed
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    public void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var list))
        {
            _values[name] = list = [];
        }

        list.Add(value);
    }

    public void Set(string name, string value) => _values[name] = [value];

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var v) ? v[^1] : null;

    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var v) ? v : [];

    public bool Flag(string name) => _values.ContainsKey(name);

    public long? Int(string name) => Get(name) is { } v ? long.Parse(v.Trim(), CultureInfo.InvariantCulture) : null;
}

/// <summary>
/// argparse's rules for one command, as project.py's commands used them: options anywhere among the
/// positionals, --opt=value, an unambiguous beginning of a long option (--inst for --institution),
/// choices and integers checked, and every required argument named when any is missing.
/// </summary>
public static class Arguments
{
    private static bool IsOption(string token) =>
        token.Length > 1 && token[0] == '-' && !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    public static Parsed Parse(IReadOnlyList<string> tokens, IReadOnlyList<Positional> positionals, IReadOnlyList<Option> options)
    {
        var parsed = new Parsed();
        var loose = new List<string>();
        var onlyPositionals = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!onlyPositionals && token == "--")
            {
                onlyPositionals = true;
                continue;
            }

            if (onlyPositionals || !IsOption(token))
            {
                loose.Add(token);
                continue;
            }

            var eq = token.IndexOf('=');
            var name = eq > 0 ? token[..eq] : token;
            string? inline = eq > 0 ? token[(eq + 1)..] : null;
            var option = Resolve(name, options);
            switch (option.Kind)
            {
                case OptionKind.Flag:
                    if (inline is not null)
                    {
                        throw new UsageError($"argument {option.Name}: ignored explicit argument '{inline}'");
                    }

                    parsed.Set(Key(option), "true");
                    break;
                default:
                    var value = inline;
                    if (value is null)
                    {
                        if (i + 1 >= tokens.Count || IsOption(tokens[i + 1]))
                        {
                            throw new UsageError($"argument {option.Name}: expected one argument");
                        }

                        value = tokens[++i];
                    }

                    Check(option.Name, value, option.Choices, option.Integer);
                    if (option.Kind == OptionKind.Append)
                    {
                        parsed.Add(Key(option), value);
                    }
                    else
                    {
                        parsed.Set(Key(option), value);
                    }

                    break;
            }
        }

        var missing = new List<string>();
        var needed = positionals.Count(p => p.Count is '1' or '+');
        var at = 0;
        foreach (var p in positionals)
        {
            var available = loose.Count - at;
            switch (p.Count)
            {
                case '1':
                    needed--;
                    if (available < 1)
                    {
                        missing.Add(p.Name);
                        break;
                    }

                    Check(p.Name, loose[at], p.Choices, false);
                    parsed.Set(p.Name, loose[at++]);
                    break;
                case '?':
                    if (available > needed)
                    {
                        Check(p.Name, loose[at], p.Choices, false);
                        parsed.Set(p.Name, loose[at++]);
                    }

                    break;
                case '*':
                    while (available-- > needed)
                    {
                        parsed.Add(p.Name, loose[at++]);
                    }

                    break;
                default:
                    needed--;
                    var take = Math.Max(0, available - needed);
                    if (take == 0)
                    {
                        missing.Add(p.Name);
                        break;
                    }

                    for (var k = 0; k < take; k++)
                    {
                        parsed.Add(p.Name, loose[at++]);
                    }

                    break;
            }
        }

        missing.AddRange(options.Where(o => o.Required && !parsed.Has(Key(o))).Select(o => o.Name));
        if (missing.Count > 0)
        {
            throw new UsageError($"the following arguments are required: {string.Join(", ", missing)}");
        }

        if (at < loose.Count)
        {
            throw new UsageError($"unrecognized arguments: {string.Join(' ', loose.Skip(at))}");
        }

        foreach (var o in options.Where(o => o.Default is not null && !parsed.Has(Key(o))))
        {
            parsed.Set(Key(o), o.Default!);
        }

        return parsed;
    }

    /// <summary>An option's key: its name without the dashes (--lab-contact is lab-contact).</summary>
    public static string Key(Option option) => option.Name.TrimStart('-');

    private static Option Resolve(string name, IReadOnlyList<Option> options)
    {
        var exact = options.FirstOrDefault(o => o.Name == name);
        if (exact is not null)
        {
            return exact;
        }

        var candidates = name.StartsWith("--", StringComparison.Ordinal)
            ? options.Where(o => o.Name.StartsWith(name, StringComparison.Ordinal)).ToList()
            : [];
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new UsageError($"unrecognized arguments: {name}"),
            _ => throw new UsageError($"ambiguous option: {name} could match {string.Join(", ", candidates.Select(c => c.Name))}"),
        };
    }

    private static void Check(string name, string value, string[]? choices, bool integer)
    {
        if (choices is not null && !choices.Contains(value))
        {
            throw new UsageError($"argument {name}: invalid choice: '{value}' (choose from {string.Join(", ", choices)})");
        }

        if (integer && !long.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            throw new UsageError($"argument {name}: invalid int value: '{value}'");
        }
    }
}
