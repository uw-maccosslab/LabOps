using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Engines.Projects;

/// <summary>Reading a lab's, project's or experiment's YAML record (project.py's parse_record).</summary>
public static class Records
{
    /// <summary>
    /// A record's mapping, and what is wrong when it cannot be read (the mapping is then empty).
    /// An empty file is an empty mapping with no problem.
    /// </summary>
    public static (PyDict Record, string? Problem) Parse(string text, string fileName)
    {
        object? value;
        try
        {
            value = YamlLoader.Load(text);
        }
        catch (YamlProblemException ex)
        {
            return (new PyDict(), $"{fileName} is not valid YAML: {ex.Problem} (line {ex.Line})");
        }

        return value switch
        {
            null => (new PyDict(), null),
            PyDict d => (d, null),
            _ => (new PyDict(), $"{fileName} should be a mapping of fields (key: value lines)"),
        };
    }
}
