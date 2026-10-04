using System.Text;
using System.Text.Json;

namespace ChargeState.Core.Claude;

/// <summary>
/// Remembers "allow steps like this" answers until the app closes, separately for each repository.
/// </summary>
/// <remarks>
/// <para>
/// A shell step is remembered by the programs it runs, not by its exact text. Claude often chains
/// commands (<c>quote.py build ... &amp;&amp; sed -n ...</c>), and the next step chains them a little
/// differently, so remembering the whole text, or only its first program, asks again and again.
/// A step is allowed when every program in it has been allowed.
/// </para>
/// <para>
/// A step that runs a command inside another (<c>$(...)</c>, backticks) is never remembered: what
/// it runs is hidden inside an allowed program's arguments. Such a step is always asked about.
/// Other tools are remembered by name.
/// </para>
/// </remarks>
public sealed class PermissionMemory
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>True when every program (or the tool) in the step has been allowed in this repository.</summary>
    public bool IsAllowed(string repository, PermissionRequest request)
    {
        var keys = Keys(request);
        lock (_gate)
        {
            return keys.Count > 0 && keys.All(k => _allowed.Contains($"{repository}|{k}"));
        }
    }

    /// <summary>Allows every program (or the tool) in the step from now on, in this repository.</summary>
    /// <returns>False when the step cannot be remembered (it hides a command inside another).</returns>
    public bool Remember(string repository, PermissionRequest request)
    {
        var keys = Keys(request);
        lock (_gate)
        {
            foreach (var key in keys)
            {
                _allowed.Add($"{repository}|{key}");
            }
        }

        return keys.Count > 0;
    }

    /// <summary>What the checkbox would remember, for its label: "uv and sed", or "Edit".</summary>
    public static string? Describe(PermissionRequest request)
    {
        var names = Keys(request).Select(k => k.Split(':', 2) is [_, var program] ? program : k).ToList();
        return names.Count switch
        {
            0 => null,
            1 => names[0],
            _ => string.Join(", ", names[..^1]) + " and " + names[^1],
        };
    }

    /// <summary>"Bash:uv", "Bash:sed" for a shell step; the tool name otherwise; nothing when it cannot be remembered.</summary>
    internal static IReadOnlyList<string> Keys(PermissionRequest request)
    {
        if (request.ToolName is not ("Bash" or "PowerShell"))
        {
            return [request.ToolName];
        }

        if (request.Input.ValueKind != JsonValueKind.Object
            || !request.Input.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var programs = Programs(command.GetString() ?? "");
        return programs is null ? [] : programs.Select(p => $"{request.ToolName}:{p}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The program each part of a command line runs, splitting on unquoted <c>&amp;&amp;</c>,
    /// <c>||</c>, <c>;</c>, <c>|</c>, <c>&amp;</c> and line breaks. Null when a command is hidden
    /// inside another, so the step must be asked about.
    /// </summary>
    internal static IReadOnlyList<string>? Programs(string commandLine)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
                else if (quote == '"' && (c == '`' || (c == '$' && i + 1 < commandLine.Length && commandLine[i + 1] == '(')))
                {
                    return null;
                }

                current.Append(c);
                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    current.Append(c);
                    break;
                case '`':
                    return null;
                case '$' when i + 1 < commandLine.Length && commandLine[i + 1] == '(':
                    return null;
                case '<' or '>' when i + 1 < commandLine.Length && commandLine[i + 1] == '(':
                    return null;
                case '&' when (i > 0 && commandLine[i - 1] == '>') || (i + 1 < commandLine.Length && commandLine[i + 1] == '>'):
                    // A redirection (2>&1, &>file), not a separator.
                    current.Append(c);
                    break;
                case ';' or '|' or '&' or '\n' or '\r':
                    segments.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        segments.Add(current.ToString());

        var programs = new List<string>();
        foreach (var segment in segments)
        {
            var words = segment.Trim().TrimStart('(', '{').Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            // Skip leading variable assignments (NAME=value command ...).
            var program = words.SkipWhile(w => w.Contains('=', StringComparison.Ordinal) && !w.StartsWith('=')).FirstOrDefault();
            if (program is not null)
            {
                programs.Add(program.Trim('\'', '"'));
            }
        }

        return programs;
    }
}
