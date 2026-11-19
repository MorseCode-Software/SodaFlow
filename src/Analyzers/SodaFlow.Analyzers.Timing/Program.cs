using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SodaFlow.Analyzers.Timing;

/// <summary>
///     Times the loop analyzer of two commits. The target Time-Analyzer of
///     build.cake runs the command "measure" for each round in a new process, and
///     then the command "compare".
/// </summary>
file static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        Arguments options = Arguments.Parse([.. arguments.Skip(1)]);

        return arguments.FirstOrDefault() switch
        {
            "measure" => await Measure(options).ConfigureAwait(false),
            "compare" => Compare(options),
            _ => throw new ArgumentException("The first argument must be \"measure\" or \"compare\".")
        };
    }

    private static async Task<int> Measure(Arguments options)
    {
        ImmutableArray<(string Name, string Path)> sides =
        [
            .. options.All("side")
                .Select(static side =>
                    side.Split(separator: '=', count: 2) switch
                    {
                        [var name, var path] => (Name: name, Path: path),
                        _ => throw new ArgumentException($"A side must be name=path: {side}")
                    })
        ];

        ImmutableArray<Sample> samples =
            await Measurement.Run(
                    sides: sides,
                    repository: options.One("repository"),
                    references: options.One("references"),
                    round: options.Integer("round"),
                    warmUp: options.Integer("warm-up"),
                    runs: options.Integer("runs"))
                .ConfigureAwait(false);

        await File.WriteAllLinesAsync(
                path: options.One("output"),
                contents: [Sample.Header, .. samples.Select(static sample => sample.ToLine())])
            .ConfigureAwait(false);

        return 0;
    }

    private static int Compare(Arguments options)
    {
        ImmutableArray<Sample> samples =
        [
            .. Directory.GetFiles(path: options.One("results"), searchPattern: "*.tsv")
                .Order(StringComparer.Ordinal)
                .SelectMany(static file => File.ReadLines(file).Skip(1))
                .Select(Sample.Parse)
        ];

        double thresholdPercent = options.Number("threshold-percent");
        double floorMilliseconds = options.Number("floor-ms");

        ImmutableArray<WorkloadResult> results =
            Comparison.Compare(
                samples: samples,
                thresholdPercent: thresholdPercent,
                floorMilliseconds: floorMilliseconds);

        string report =
            Comparison.Report(
                results: results,
                thresholdPercent: thresholdPercent,
                floorMilliseconds: floorMilliseconds);

        File.WriteAllText(path: options.One("summary"), contents: report);
        Console.WriteLine(report);

        return results.Any(static result => result.Verdict.Fails) ? 1 : 0;
    }
}
