using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SodaFlow.Analyzers.Timing;

/// <summary>
///     Compares the candidate analyzer with the base analyzer, and the
///     control analyzer with the base analyzer, over the samples of all jobs.
/// </summary>
/// <remarks>
///     <para>
///         The control is a second copy of the base analyzer. Thus, its
///         difference from the base is noise only. The comparison uses a ratio
///         for each round, and then the median of the ratios. A round is one
///         process, and the sides of one round share its state, thus a ratio
///         cancels most of that state.
///     </para>
///     <para>
///         A job is one runner, and it measures all three sides. The ratio of
///         the candidate changes from one runner to the next by more than the
///         control of one runner shows. Thus, each figure of the comparison is
///         the median of the figures of the jobs. With one job, the figures are
///         those of that job.
///     </para>
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
    /// <param name="jobs">The samples of each job.</param>
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
        ImmutableArray<Job> jobs,
        double thresholdPercent,
        double floorMilliseconds) =>
    [
        .. jobs
            .SelectMany(static job => job.Samples)
            .GroupBy(keySelector: static sample => sample.Workload, comparer: StringComparer.Ordinal)
            .Select(workload =>
                Result(
                    workload: workload.Key,
                    figures: [.. jobs.Select(job => Figures(job: job, workload: workload.Key))],
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
             it fails also. Each job measures all three sides on a runner of its own. Each figure is the median
             over the jobs of the figure of each job, and the figure of a job is the median over its rounds of
             the median of each round. With more than one job, the ratio of each job follows in parentheses.
             """));

        text.AppendLine();

        text.AppendLine(
            Row(
                "Workload",
                "Jobs",
                "Rounds",
                "Base ms",
                "Candidate ms",
                "Candidate / base",
                "Control / base",
                "Candidate bytes / base",
                "Diagnostics (base, candidate)",
                "Passes with a collection",
                "Result"));

        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (WorkloadResult result in results)
        {
            text.AppendLine(
                Row(
                    result.Workload,
                    result.JobCandidateRatios.Length.ToString(CultureInfo.InvariantCulture),
                    result.Rounds.ToString(CultureInfo.InvariantCulture),
                    result.BaseMilliseconds.ToString(format: "0.0", provider: CultureInfo.InvariantCulture),
                    result.CandidateMilliseconds.ToString(format: "0.0", provider: CultureInfo.InvariantCulture),
                    Percents(median: result.CandidateRatio, jobs: result.JobCandidateRatios),
                    Percents(median: result.ControlRatio, jobs: result.JobControlRatios),
                    Percent(result.CandidateBytesRatio),
                    result.BaseDiagnostics + ", " + result.CandidateDiagnostics,
                    result.CollectedPasses.ToString(CultureInfo.InvariantCulture),
                    result.Verdict.Text));
        }

        return text.ToString();
    }

    private static WorkloadResult Result(
        string workload,
        ImmutableArray<JobFigures> figures,
        ImmutableArray<Sample> samples,
        double thresholdPercent,
        double floorMilliseconds)
    {
        double baseMilliseconds = Median(figures.Select(static job => job.BaseMilliseconds));
        double candidateMilliseconds = Median(figures.Select(static job => job.CandidateMilliseconds));
        double controlMilliseconds = Median(figures.Select(static job => job.ControlMilliseconds));
        double candidateRatio = Median(figures.Select(static job => job.CandidateRatio));
        double controlRatio = Median(figures.Select(static job => job.ControlRatio));
        double limit = 1 + thresholdPercent / 100;

        // The control is noise only. When the control is outside the limits in either direction, the
        // run cannot find a slowdown of the size of the limits. The median of the jobs decides, as for
        // the candidate, thus the noise of one runner does not fail the check.
        bool noisy =
            (controlRatio > limit || controlRatio < 1 / limit)
            && Math.Abs(controlMilliseconds - baseMilliseconds) > floorMilliseconds;

        bool slower = candidateRatio > limit && candidateMilliseconds - baseMilliseconds > floorMilliseconds;

        return new WorkloadResult(
            Workload: workload,
            Rounds: figures.Sum(static job => job.Rounds),
            BaseMilliseconds: baseMilliseconds,
            CandidateMilliseconds: candidateMilliseconds,
            CandidateRatio: candidateRatio,
            ControlRatio: controlRatio,
            JobCandidateRatios: [.. figures.Select(static job => job.CandidateRatio)],
            JobControlRatios: [.. figures.Select(static job => job.ControlRatio)],
            CandidateBytesRatio: Median(figures.Select(static job => job.CandidateBytesRatio)),
            BaseDiagnostics: Diagnostics(samples: samples, side: Base),
            CandidateDiagnostics: Diagnostics(samples: samples, side: Candidate),
            CollectedPasses: samples.Count(static sample => sample.Collections > 0),
            Verdict: slower
                ? Verdict.Slower
                : noisy
                    ? Verdict.Noisy
                    : Verdict.Pass);
    }

    // The figures of one job for one workload. Each figure is the median over the rounds of the job.
    private static JobFigures Figures(Job job, string workload)
    {
        ImmutableArray<ImmutableArray<Sample>> rounds =
        [
            .. job.Samples
                .Where(sample =>
                    string.Equals(a: sample.Workload, b: workload, comparisonType: StringComparison.Ordinal))
                .GroupBy(static sample => sample.Round)
                .OrderBy(static round => round.Key)
                .Select(static round => round.ToImmutableArray())
        ];

        return rounds.IsEmpty
            ? throw new InvalidOperationException($"The job {job.Name} has no samples of the workload {workload}.")
            : new JobFigures(
                Rounds: rounds.Length,
                BaseMilliseconds: OfRounds(static round => RoundMedian(round: round, side: Base, value: Time)),
                CandidateMilliseconds:
                OfRounds(static round => RoundMedian(round: round, side: Candidate, value: Time)),
                ControlMilliseconds: OfRounds(static round => RoundMedian(round: round, side: Control, value: Time)),
                CandidateRatio: OfRounds(static round => Ratio(round: round, side: Candidate, value: Time)),
                ControlRatio: OfRounds(static round => Ratio(round: round, side: Control, value: Time)),
                CandidateBytesRatio: OfRounds(static round => Ratio(round: round, side: Candidate, value: Bytes)));

        // The median over the rounds of a figure of each round.
        double OfRounds(Func<ImmutableArray<Sample>, double> figure) => Median(rounds.Select(figure));
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

    // The median, and then the ratio of each job when there is more than one job.
    private static string Percents(double median, ImmutableArray<double> jobs) =>
        jobs.Length > 1
            ? Percent(median) + " (" + string.Join(separator: ", ", values: jobs.Select(Percent)) + ")"
            : Percent(median);

    private static string Row(params ReadOnlySpan<string> cells) =>
        "| " + string.Join(separator: " | ", value: cells) + " |";

    private sealed record JobFigures(
        int Rounds,
        double BaseMilliseconds,
        double CandidateMilliseconds,
        double ControlMilliseconds,
        double CandidateRatio,
        double ControlRatio,
        double CandidateBytesRatio);
}

/// <summary>The samples of one job, which is one runner.</summary>
/// <param name="Name">The name of the job, for a message.</param>
/// <param name="Samples">The samples of all rounds of the job.</param>
internal sealed record Job(string Name, ImmutableArray<Sample> Samples);

/// <summary>The comparison of the analyzers over one workload.</summary>
/// <param name="Workload">The name of the workload.</param>
/// <param name="Rounds">The number of rounds of all jobs.</param>
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
/// <param name="JobCandidateRatios">
///     The ratio of the candidate time to the
///     base time in each job, in the order of the jobs.
/// </param>
/// <param name="JobControlRatios">
///     The ratio of the control time to the base
///     time in each job, in the order of the jobs.
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
    ImmutableArray<double> JobCandidateRatios,
    ImmutableArray<double> JobControlRatios,
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
