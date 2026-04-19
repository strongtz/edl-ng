using System.CommandLine;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Core;
using Qualcomm.EmergencyDownload.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class RawProgramCommand
{
    private static readonly Argument<string[]> XmlFilePatternsArgument =
        new("xmlfile_patterns", "Paths or patterns for rawprogram and patch XML files (e.g., rawprogram0.xml patch0.xml rawprogram*.xml patch*.xml).")
        { Arity = ArgumentArity.OneOrMore };

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("rawprogram", "Processes rawprogramN.xml and patchN.xml files for flashing.")
        {
            XmlFilePatternsArgument
        };

        command.SetHandler(ExecuteAsync, globalOptionsBinder, XmlFilePatternsArgument);
        return command;
    }

    private static async Task<int> ExecuteAsync(EdlOptions globalOptions, string[] xmlFilePatterns)
    {
        Logging.Log("Executing 'rawprogram' command...", LogLevel.Trace);

        var resolved = RawProgramRunner.ResolveXmlFiles(xmlFilePatterns);
        if (resolved.Count == 0)
        {
            Logging.Log("No XML files found after resolving patterns.", LogLevel.Error);
            return 1;
        }
        Logging.Log($"Total unique XML files to process: {resolved.Count}", LogLevel.Debug);

        var grouped = RawProgramRunner.GroupByLun(resolved);

        return await CommandExecutor.RunAsync("rawprogram", async () =>
        {
            using var manager = new EdlManager(globalOptions);

            var maxPrefix = 0;
            foreach (var (_, file) in grouped.RawProgramByLun)
            {
                // Scan the XML later to compute prefix; cheaper to just pad to a generous width here.
                var hint = $"Writing (LUN file {file.Name}): ";
                if (hint.Length > maxPrefix)
                {
                    maxPrefix = hint.Length;
                }
            }
            maxPrefix = Math.Max(maxPrefix, 40);

            void Progress(RawProgramRunner.RawProgramProgress p)
            {
                var pct = p.BytesTotal == 0 ? 100 : p.BytesDone * 100.0 / p.BytesTotal;
                var speed = p.ElapsedSeconds > 0.1 ? p.BytesDone / p.ElapsedSeconds : 0;
                var speedStr = p.ElapsedSeconds > 0.1 ? ProgressReporter.FormatSpeed(speed) : "N/A";
                var prefix = $"Writing {p.Label} ({p.Filename}): ".PadRight(maxPrefix);
                Console.Write($"\r{prefix}{pct,5:F1}% ({p.BytesDone / (1024.0 * 1024.0),6:F2} / {p.BytesTotal / (1024.0 * 1024.0),6:F2} MiB) [{speedStr,-10}]    ");
                if (p.BytesTotal > 0 && p.BytesDone >= p.BytesTotal)
                {
                    Console.WriteLine();
                }
            }

            return await RawProgramRunner.RunAsync(manager, grouped, Progress);
        });
    }
}