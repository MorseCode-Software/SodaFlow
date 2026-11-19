using System;
using System.Globalization;

namespace SodaFlow.Analyzers.Timing;

/// <summary>One pass of one analyzer over one workload.</summary>
/// <param name="Workload">The name of the workload.</param>
/// <param name="Round">The round, which is one process.</param>
/// <param name="Run">
///     The position of the pass in the round, after the warm-up
///     passes.
/// </param>
/// <param name="Side">The name of the analyzer.</param>
/// <param name="Milliseconds">
///     The time in the analyzer, as the telemetry of Roslyn
///     gives it.
/// </param>
/// <param name="AllocatedBytes">
///     The bytes that the process allocated in the pass,
///     the driver included.
/// </param>
/// <param name="Collections">
///     The collections of the garbage collector in the pass.
///     Usually there are none.
/// </param>
/// <param name="Diagnostics">The number of diagnostics that the analyzer reported.</param>
internal sealed record Sample(
    string Workload,
    int Round,
    int Run,
    string Side,
    double Milliseconds,
    long AllocatedBytes,
    int Collections,
    int Diagnostics)
{
    /// <summary>The first line of a file of samples.</summary>
    public const string Header =
        "workload\tround\trun\tside\tmilliseconds\tallocated bytes\tcollections\tdiagnostics";

    /// <summary>Reads a sample from a line that <see cref="ToLine" /> wrote.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The sample.</returns>
    public static Sample Parse(string line) =>
        line.Split('\t') switch
        {
            [
                    var workload,
                    var round,
                    var run,
                    var side,
                    var milliseconds,
                    var bytes,
                    var collections,
                    var diagnostics
                ] =>
                new Sample(
                    Workload: workload,
                    Round: int.Parse(s: round, provider: CultureInfo.InvariantCulture),
                    Run: int.Parse(s: run, provider: CultureInfo.InvariantCulture),
                    Side: side,
                    Milliseconds: double.Parse(s: milliseconds, provider: CultureInfo.InvariantCulture),
                    AllocatedBytes: long.Parse(s: bytes, provider: CultureInfo.InvariantCulture),
                    Collections: int.Parse(s: collections, provider: CultureInfo.InvariantCulture),
                    Diagnostics: int.Parse(s: diagnostics, provider: CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"A line of samples must have eight fields: {line}")
        };

    /// <summary>Writes the sample as one line, with a tab between the fields.</summary>
    /// <returns>The line.</returns>
    public string ToLine() =>
        string.Join(
            separator: '\t',
            this.Workload,
            this.Round.ToString(CultureInfo.InvariantCulture),
            this.Run.ToString(CultureInfo.InvariantCulture),
            this.Side,
            this.Milliseconds.ToString(format: "R", provider: CultureInfo.InvariantCulture),
            this.AllocatedBytes.ToString(CultureInfo.InvariantCulture),
            this.Collections.ToString(CultureInfo.InvariantCulture),
            this.Diagnostics.ToString(CultureInfo.InvariantCulture));
}
