using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Functional;
using Eight = SodaFlow.Functional.Either<int, int, int, int, int, int, int, int>;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one call to each frequent member costs on an <c>Either</c> of two cases
///     and on one of eight.
/// </summary>
/// <remarks>
///     <para>
///         <c>Match</c> is a switch on the case index that passes the functions
///         through, and it is the baseline. <c>MatchVoid</c> wrapped each action in
///         a closure, one for each case. Thus, a call on an <c>Either</c> of eight
///         allocated eight. Each map member captured its function in one closure.
///     </para>
///     <para>
///         Each benchmark holds the first case. The map of the first case runs the
///         function, and the map of the last case makes the value again as it is.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class EitherBenchmarks
{
    private readonly Either<int, int> two = Either<int, int>.First(1);
    private readonly Eight eight = Eight.First(1);

    private int sum;

    /// <summary>Reads the value of an Either of two through Match.</summary>
    [Benchmark(Baseline = true, Description = "Match, two cases")]
    public int MatchTwo() => this.two.Match(onFirst: static v => v, onSecond: static v => -v);

    /// <summary>Runs one of two actions.</summary>
    [Benchmark(Description = "MatchVoid, two cases")]
    public int MatchVoidTwo()
    {
        this.two.MatchVoid(onFirst: v => this.sum += v, onSecond: v => this.sum -= v);
        return this.sum;
    }

    /// <summary>Maps the first case of an Either of two, which is the case it holds.</summary>
    [Benchmark(Description = "MapFirst, two cases")]
    public Either<int, int> MapFirstTwo() => this.two.MapFirst(static v => v + 1);

    /// <summary>Maps the second case of an Either of two, which is not the case it holds.</summary>
    [Benchmark(Description = "MapSecond, two cases")]
    public Either<int, int> MapSecondTwo() => this.two.MapSecond(static v => v + 1);

    /// <summary>Reads the value of an Either of eight through Match.</summary>
    [Benchmark(Description = "Match, eight cases")]
    public int MatchEight() =>
        this.eight.Match(
            onFirst: static v => v,
            onSecond: static v => -v,
            onThird: static v => v,
            onFourth: static v => -v,
            onFifth: static v => v,
            onSixth: static v => -v,
            onSeventh: static v => v,
            onEighth: static v => -v);

    /// <summary>Runs one of eight actions.</summary>
    [Benchmark(Description = "MatchVoid, eight cases")]
    public int MatchVoidEight()
    {
        this.eight.MatchVoid(
            onFirst: v => this.sum += v,
            onSecond: v => this.sum -= v,
            onThird: v => this.sum += v,
            onFourth: v => this.sum -= v,
            onFifth: v => this.sum += v,
            onSixth: v => this.sum -= v,
            onSeventh: v => this.sum += v,
            onEighth: v => this.sum -= v);

        return this.sum;
    }

    /// <summary>Maps the first case of an Either of eight, which is the case it holds.</summary>
    [Benchmark(Description = "MapFirst, eight cases")]
    public Eight MapFirstEight() => this.eight.MapFirst(static v => v + 1);

    /// <summary>Maps the eighth case of an Either of eight, which is not the case it holds.</summary>
    [Benchmark(Description = "MapEighth, eight cases")]
    public Eight MapEighthEight() => this.eight.MapEighth(static v => v + 1);
}
