using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics.Telemetry;

namespace SodaFlow.Analyzers.Timing;

/// <summary>
///     One round of the timing: one process that times each analyzer over
///     each workload.
/// </summary>
internal static class Measurement
{
    private const string AnalyzerTypeName = "SodaFlow.Analyzers.LoopReadAnalyzer";

    // Three times the allocations of the largest pass.
    private const long NoCollectionBytes = 1_000_000_000;

    /// <summary>Times each analyzer over each workload, and gives each pass.</summary>
    /// <param name="sides">The name and the analyzer file of each side.</param>
    /// <param name="repository">The root of the repository, which has the workloads.</param>
    /// <param name="references">
    ///     The folder of the library assemblies and the TUnit
    ///     assemblies.
    /// </param>
    /// <param name="round">
    ///     The number of the round. It sets the first order of the
    ///     sides.
    /// </param>
    /// <param name="warmUp">The passes of each side that the round does not record.</param>
    /// <param name="runs">The passes of each side that the round records.</param>
    /// <returns>The recorded passes.</returns>
    public static async Task<ImmutableArray<Sample>> Run(
        ImmutableArray<(string Name, string Path)> sides,
        string repository,
        string references,
        int round,
        int warmUp,
        int runs)
    {
        ImmutableArray<(string Name, Func<DiagnosticAnalyzer> Create)> loaded =
        [
            .. sides.Select(static side => (side.Name, Load(name: side.Name, path: side.Path)))
        ];

        List<Sample> samples = [];

        foreach (Workload workload in Workload.All)
        {
            CSharpCompilation compilation = workload.Compile(repository: repository, references: references);

            for (int pass = 0; pass < warmUp + runs; pass++)
            {
                // The sides run one after the other in each pass, and the first side moves by one in
                // each pass and in each round. Thus, a slow period of the machine has the same effect
                // on each side.
                int first = (pass + round) % loaded.Length;

                foreach ((string name, Func<DiagnosticAnalyzer> create) in
                         loaded.Skip(first).Concat(loaded.Take(first)))
                {
                    Sample sample =
                        await Time(
                                workload: workload.Name,
                                round: round,
                                run: pass - warmUp,
                                side: name,
                                analyzer: create(),
                                compilation: compilation)
                            .ConfigureAwait(false);

                    if (pass >= warmUp)
                    {
                        samples.Add(sample);
                    }
                }
            }
        }

        return [.. samples];
    }

    // Each analyzer file goes in a load context of its own. Thus, two files of the same assembly
    // load side by side, and each one gets its own compiled code. The context gives Roslyn from the
    // default context, because it does not load Roslyn itself.
    private static Func<DiagnosticAnalyzer> Load(string name, string path)
    {
        AssemblyLoadContext context = new(name: name, isCollectible: false);

        Type type =
            context.LoadFromAssemblyPath(Path.GetFullPath(path)).GetType(name: AnalyzerTypeName, throwOnError: true)
            ?? throw new InvalidOperationException($"{path} has no {AnalyzerTypeName}.");

        return () =>
            (DiagnosticAnalyzer)(Activator.CreateInstance(type)
                                 ?? throw new InvalidOperationException($"No {AnalyzerTypeName}."));
    }

    private static async Task<Sample> Time(
        string workload,
        int round,
        int run,
        string side,
        DiagnosticAnalyzer analyzer,
        Compilation compilation)
    {
        // A collection occurs here, before the pass, and not in the pass.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The driver allocates about 320 MB in a pass over SodaFlow.Tests, and the analyzer allocates
        // a small part of it. Without this region, a collection stops the analyzer at a random point,
        // and the time of the analyzer gets the pause. A measurement without the region showed two
        // times the time and two times the range. The allocated bytes show what the analyzer adds to
        // the collections.
        bool region = GC.TryStartNoGCRegion(NoCollectionBytes);
        int collections = GC.CollectionCount(generation: 0);
        long before = GC.GetTotalAllocatedBytes(precise: true);

        // One thread, thus the time of one analyzer callback does not include the time of a different callback.
        CompilationWithAnalyzers withAnalyzers =
            compilation.WithAnalyzers(
                analyzers: [analyzer],
                analysisOptions: new CompilationWithAnalyzersOptions(
                    options: new AnalyzerOptions([]),
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: true));

        // The analysis goes one file at a time, and not over the full compilation in one call. In one
        // call, the driver also compiles the code on a different thread while the analyzer runs.
        // On GitHub runners, 80% of the passes over SodaFlow.Tests then took from 33 ms to 293 ms.
        // An analyzer that did each walk two times did not always show as slower. A probable cause is
        // that the time of the analyzer includes waits for the other thread, but no test examined it.
        // One file at a time, the same pass took about 22 ms, and the noise was 3% or less. The
        // analyzer with two walks then showed as 34% to 40% slower.
        ImmutableArray<Diagnostic> diagnostics = [];

        foreach (SyntaxTree tree in withAnalyzers.Compilation.SyntaxTrees)
        {
            ImmutableArray<Diagnostic> found =
                await withAnalyzers.GetAnalyzerSemanticDiagnosticsAsync(
                        model: withAnalyzers.Compilation.GetSemanticModel(tree),
                        filterSpan: null,
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);

            diagnostics = diagnostics.AddRange(found);
        }

        long after = GC.GetTotalAllocatedBytes(precise: true);
        int collected = GC.CollectionCount(generation: 0) - collections;

        // A collection in the pass ends the region.
        if (region && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
        {
            GC.EndNoGCRegion();
        }

        AnalyzerTelemetryInfo telemetry =
            await withAnalyzers
                .GetAnalyzerTelemetryInfoAsync(analyzer: analyzer, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

        return new Sample(
            Workload: workload,
            Round: round,
            Run: run,
            Side: side,
            Milliseconds: telemetry.ExecutionTime.TotalMilliseconds,
            AllocatedBytes: after - before,
            Collections: collected,
            Diagnostics: diagnostics.Length);
    }
}
