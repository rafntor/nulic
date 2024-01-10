using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.CommandLine;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("unit_tests")]

namespace nulic;

internal class Program
{
    static RootCommand CreateApp()
    {
        var path = new Argument<string>("path", () => ".", "[<Solution|Project|Directory>]");

        var fileOption = new Option<FileInfo?>(
        name: "--file",
        description: "The file to read and display on the console.");

        var rootCommand = new RootCommand("Sample app for System.CommandLine");
        rootCommand.AddArgument(path);
        rootCommand.AddOption(fileOption);

        rootCommand.SetHandler((path) =>
        {
               Process(path);
        },
            path);

        return rootCommand;
    }
    static void Process(string path)
    {
        var projects = MSBuildProject.LoadFrom(path);
        Console.WriteLine($"path:{path} - {projects.Count()} projects");

        foreach (var project in projects)
        {
            var nugets = NugetMetadata.GetFrom(project);

            Console.WriteLine($"project:{project.FilePath} - {nugets.Count()} nugets");
            foreach (var nuget in nugets)
                Console.WriteLine($" - {nuget}");
        }

        {
            var nugets = projects.SelectMany(NugetMetadata.GetFrom).Distinct();

            Console.WriteLine($"{nugets.ToJson(Newtonsoft.Json.Formatting.Indented)}");
        }
    }
    static async Task Main(string[] args)
    {
        var app2 = CreateApp();

        await app2.InvokeAsync(args);
    }
}
