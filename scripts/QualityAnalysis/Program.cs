using System.Text.Json;
using Microsoft.Crap4CSharp;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: QualityAnalysis <source-directory> <merged-cobertura.xml> <report.json>");
    return 1;
}

var sourceDirectory = Path.GetFullPath(args[0]);
var coverageFile = Path.GetFullPath(args[1]);
var outputFile = Path.GetFullPath(args[2]);
if (!Directory.Exists(sourceDirectory) || !File.Exists(coverageFile))
{
    Console.Error.WriteLine("The source directory and coverage report must exist.");
    return 1;
}

var files = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "obj" or "bin"))
    .Order(StringComparer.Ordinal)
    .ToArray();
if (files.Length == 0 || CoberturaCoverageParser.Parse(coverageFile).Count == 0)
{
    Console.Error.WriteLine("CRAP analysis requires source files and populated coverage data.");
    return 1;
}

var methods = files.SelectMany(file => CrapAnalyzer.Analyze([file], coverageFile)
    .Select(metric => new
    {
        File = Path.GetRelativePath(sourceDirectory, file),
        Metric = metric,
        MaximumCrapScore = metric.CrapScore ?? CrapScore.Calculate(metric.Complexity, 0)!.Value,
        MissingCoverage = metric.CoveragePercent is null
    })).ToArray();
var violations = methods.Where(method => method.MaximumCrapScore >= 20).ToArray();
var report = new
{
    Analyzer = "microsoft/crap4csharp",
    Revision = "9e155ec73008af7314b78b8e7f51e5edc2fd2a41",
    ThresholdExclusive = 20,
    Methods = methods,
    Violations = violations
};
Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
File.WriteAllText(outputFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (var method in violations)
{
    Console.Error.WriteLine(
        $"{method.MaximumCrapScore:F2} CRAP: {method.File} {method.Metric.ClassName}.{method.Metric.MethodName}" +
        (method.MissingCoverage ? " (unmatched coverage; conservative zero-coverage bound)" : ""));
}

Console.WriteLine($"Microsoft CRAP: {methods.Length} members, {violations.Length} at or above 20.");
return violations.Length == 0 ? 0 : 2;
