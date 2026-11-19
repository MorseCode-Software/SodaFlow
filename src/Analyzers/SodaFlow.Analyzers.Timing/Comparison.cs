using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SodaFlow.Analyzers.Timing;

/// <summary>
///     Compares the candidate analyzer with the base analyzer, and the
///     control analyzer with the base analyzer, over the samples of all rounds.
/// </summary>
/// <remarks>
///     The control is a second copy of the base analyzer. Thus, its
///     difference from the base is noise only. The comparison uses a ratio for
///     each round, and then the median of the ratios. A round is one process, and
///     the sides of one round share its state, thus a ratio cancels most of that
///     state.
/// </remarks>
internal static class Comparison
{
    /// <summary>The side that the comparison uses as the reference.</summary>
    private const string Base = "base";

    /// <summary>The side that the check is about.</summary>
    private const string Candidate = "candidate";

    /// <summary>The second copy of the base side.</summary>
    private const string Control = "control";

    /// <summary>Gives the result of each workload.</summary>
    /// <param name="samples">The samples of all rounds.</param>
    /// <param name="thresholdPercent">
    ///     The largest slowdown in percent that is not a
    ///     failure.
    /// </param>
    /// <param name="floorMilliseconds">
    ///     The largest slowdown in milliseconds that is
    ///     not a failure. It is for a workload on which the analyzer uses a few
    ///     milliseconds only.
    /// </param>
    /// <returns>The result of each workload, in the order of the samples.</returns>
    public static ImmutableArray<WorkloadResult> Compare(
        ImmutableArray<Sample> samples,
        double thresholdPercent,
        double floorMilliseconds) =>
    [
        .. samples
            .GroupBy(keySelector: static sample => sample.Workload, comparer: StringComparer.Ordinal)
            .Select(workload =>
                Result(
                    workload: workload.Key,
                    samples: [.. workload],
                    thresholdPercent: thresholdPercent,
                    floorMilliseconds: floorMilliseconds))
    ];

    /// <summary>Writes the results as a Markdown table, with the rule of the check.</summary>
    /// <param name="results">The result of each workload.</param>
    /// <param name="thresholdPercent">The threshold in percent.</param>
    /// <param name="floorMilliseconds">The floor in milliseconds.</param>
    /// <returns>The Markdown.</returns>
    public static string Report(
        ImmutableArray<WorkloadResult> results,
        double thresholdPercent,
        double floorMilliseconds)
    {
        StringBuilder text = new();

        text.AppendLine("## Loop analyzer timing");
        text.AppendLine();

        text.AppendLine(
            string.Create(
                provider: CultureInfo.InvariantCulture,
                handler: $"""
             A workload fails when the candidate is more than {thresholdPercent:0.#}% slower than the base
             and also more than {floorMilliseconds:0.##} ms slower. The control is a second copy of the base.
             When the control differs from the base by the same limits, the run is too noisy to decide, and
             it fails also. Each figure is the median over the rounds of the median of each round.
             """));

        text.AppendLine();

        text.AppendLine(
            Row(
                "Workload",
                "Rounds",
                "Base ms",
                "Candidate ms",
                "Candidate / base",
                "Control / base",
                "Candidate bytes / base",
                "Diagnostics (base, candidate)",
                "Passes with a collection",
                "Result"));

        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (WorkloadResult result in results)
        {
            text.AppendLine(
                Row(
                    result.Workload,
                    result.Rounds.ToString(CultureInfo.InvariantCulture),
                    result.BaseMilliseconds.ToString(format: "0.0", provider: CultureInfo.InvariantCulture),
                    result.CandidateMilliseconds.ToString(format: "0.0", provider: CultureInfo.InvariantCulture),
                    Percent(result.CandidateRatio),
                    Percent(result.ControlRatio),
                    Percent(result.CandidateBytesRatio),
                    result.BaseDiagnostics + ", " + result.CandidateDiagnostics,
                    result.CollectedPasses.ToString(CultureInfo.InvariantCulture),
                    result.Verdict.Text));
        }

        return text.ToString();
    }

    private static WorkloadResult Result(
        string workload,
        ImmutableArray<Sample> samples,
        double thresholdPercent,
        double floorMilliseconds)
    {
        ImmutableArray<ImmutableArray<Sample>> rounds =
        [
            .. samples.GroupBy(static sample => sample.Round)
                .OrderBy(static round => round.Key)
                .Select(static round => round.ToImmutableArray())
        ];

        double baseMilliseconds = Median(rounds.Select(static round => RoundMedian(round: round, side: Base, value: Time)));

        double candidateMilliseconds =
            Median(rounds.Select(static round => RoundMedian(round: round, side: Candidate, value: Time)));

        double controlMilliseconds =
            Median(rounds.Select(static round => RoundMedian(round: round, side: Control, value: Time)));

        double candidateRatio = Median(rounds.Select(static round => Ratio(round: round, side: Candidate, value: Time)));
        double controlRatio = Median(rounds.Select(static round => Ratio(round: round, side: Control, value: Time)));
        double limit = 1 + thresholdPercent / 100;

        // The control is noise only. When the control is outside the limits in either direction, the
        // run cannot find a slowdown of the size of the limits.
        bool noisy =
            (controlRatio > limit || controlRatio < 1 / limit)
            && Math.Abs(controlMilliseconds - baseMilliseconds) > floorMilliseconds;

        bool slower = candidateRatio > limit && candidateMilliseconds - baseMilliseconds > floorMilliseconds;

        return new WorkloadResult(
            Workload: workload,
            Rounds: rounds.Length,
            BaseMilliseconds: baseMilliseconds,
            CandidateMilliseconds: candidateMilliseconds,
            CandidateRatio: candidateRatio,
            ControlRatio: controlRatio,
            CandidateBytesRatio: Median(rounds.Select(static round => Ratio(round: round, side: Candidate, value: Bytes))),
            BaseDiagnostics: Diagnostics(samples: samples, side: Base),
            CandidateDiagnostics: Diagnostics(samples: samples, side: Candidate),
            CollectedPasses: samples.Count(static sample => sample.Collections > 0),
            Verdict: slower
                ? Verdict.Slower
                : noisy
                    ? Verdict.Noisy
                    : Verdict.Pass);
    }

    private static double Time(Sample sample) => sample.Milliseconds;

    private static double Bytes(Sample sample) => sample.AllocatedBytes;

    private static double Ratio(ImmutableArray<Sample> round, string side, Func<Sample, double> value) =>
        RoundMedian(round: round, side: side, value: value) / RoundMedian(round: round, side: Base, value: value);

    private static double RoundMedian(ImmutableArray<Sample> round, string side, Func<Sample, double> value) =>
        Median(round.Where(sample => sample.Side == side).Select(value));

    // The analyzer is deterministic, thus each pass of a side gives the same diagnostics.
    private static string Diagnostics(ImmutableArray<Sample> samples, string side) =>
        string.Join(
            separator: "/",
            values: samples.Where(sample => sample.Side == side)
                .Select(static sample => sample.Diagnostics)
                .Distinct()
                .Order()
                .Select(static count => count.ToString(CultureInfo.InvariantCulture)));

    private static double Median(IEnumerable<double> values)
    {
        ImmutableArray<double> sorted = [.. values.Order()];

        return sorted.Length == 0
            ? throw new InvalidOperationException("A side has no samples.")
            : sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Percent(double ratio) =>
        ((ratio - 1) * 100).ToString(format: "+0.0;-0.0;0.0", provider: CultureInfo.InvariantCulture) + "%";

    private static string Row(params ReadOnlySpan<string> cells) =>
        "| " + string.Join(separator: " | ", value: cells) + " |";
}

/// <summary>The comparison of the analyzers over one workload.</summary>
/// <param name="Workload">The name of the workload.</param>
/// <param name="Rounds">The number of rounds.</param>
/// <param name="BaseMilliseconds">The median time of the base analyzer.</param>
/// <param name="CandidateMilliseconds">The median time of the candidate analyzer.</param>
/// <param name="CandidateRatio">
///     The median ratio of the candidate time to the base
///     time.
/// </param>
/// <param name="ControlRatio">
///     The median ratio of the control time to the base
///     time.
/// </param>
/// <param name="CandidateBytesRatio">
///     The median ratio of the allocated bytes of
///     the candidate to the base.
/// </param>
/// <param name="BaseDiagnostics">The diagnostic count of the base analyzer.</param>
/// <param name="CandidateDiagnostics">
///     The diagnostic count of the candidate
///     analyzer.
/// </param>
/// <param name="CollectedPasses">
///     The passes of all sides that had a collection of
///     the garbage collector.
/// </param>
/// <param name="Verdict">The result of the check for the workload.</param>
internal sealed record WorkloadResult(
    string Workload,
    int Rounds,
    double BaseMilliseconds,
    double CandidateMilliseconds,
    double CandidateRatio,
    double ControlRatio,
    double CandidateBytesRatio,
    string BaseDiagnostics,
    string CandidateDiagnostics,
    int CollectedPasses,
    Verdict Verdict);

/// <summary>The result of the check for one workload.</summary>
/// <param name="Text">The text in the report.</param>
/// <param name="Fails">True when the result fails the check.</param>
internal sealed record Verdict(string Text, bool Fails)
{
    /// <summary>The candidate is not slower than the limits.</summary>
    public static Verdict Pass { get; } = new(Text: "pass", Fails: false);

    /// <summary>The candidate is slower than the limits.</summary>
    public static Verdict Slower { get; } = new(Text: "**slower**", Fails: true);

    /// <summary>The control is outside the limits, thus the run cannot give a result.</summary>
    public static Verdict Noisy { get; } = new(Text: "**too noisy, run again**", Fails: true);
}
