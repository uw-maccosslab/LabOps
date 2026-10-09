using System.Text;
using LabOps.Engines.CommandLine;

namespace LabOps.Cli;

/// <summary>
/// labops.exe: hosts the labops command line (LabopsCommandLine in LabOps.Engines) on the console,
/// writing UTF-8 with LF line ends on every system.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n" };
        var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        try
        {
            return LabopsCommandLine.Run(args, stdout, stderr, Directory.GetCurrentDirectory());
        }
        finally
        {
            stdout.Flush();
        }
    }
}
