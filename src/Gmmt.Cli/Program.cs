using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Gmmt.Core;
using Gmmt.Pipeline;
using UndertaleModLib;

namespace Gmmt.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("GMMT CLI");

        var translateCommand = new Command("translate-xdelta", "Translate an xdelta patch");
        var patchOption = new Argument<string>("patch", "Path to the xdelta patch file");
        var targetOption = new Argument<string>("target", "Path to the target game.unx file");
        var outputOption = new Option<string>(new[] { "-o", "--output" }, "Path to the output file");
        var vanillaOption = new Option<string>(new[] { "-v", "--vanilla" }, "Path to the vanilla game.unx file (optional, uses library if not specified)");
        var reportOption = new Option<string>(new[] { "-r", "--report" }, "Path to output a JSON report");

        translateCommand.AddArgument(patchOption);
        translateCommand.AddArgument(targetOption);
        translateCommand.AddOption(outputOption);
        translateCommand.AddOption(vanillaOption);
        translateCommand.AddOption(reportOption);

        translateCommand.SetHandler(async (string patch, string target, string output, string vanilla, string report) =>
        {
            var logger = new ConsoleLogger();
            var pipeline = new TranslationPipeline(logger);
            
            var options = new TranslateXDeltaOptions
            {
                PatchFilePath = patch,
                TargetFilePath = target,
                OutputPath = output,
                VanillaPath = vanilla,
                ReportPath = report
            };

            var result = await pipeline.RunXDeltaTranslateAsync(options);
            
            if (result.Success)
            {
                Console.WriteLine("Translation successful.");
            }
            else
            {
                Console.WriteLine($"Translation failed: {result.ErrorMessage}");
            }
        }, patchOption, targetOption, outputOption, vanillaOption, reportOption);

        rootCommand.AddCommand(translateCommand);

        return await rootCommand.InvokeAsync(args);
    }
}

class ConsoleLogger : IPipelineLogger
{
    public void Log(string message) => Console.WriteLine(message);
    public void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARNING: {message}");
        Console.ResetColor();
    }
    public void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }
}
