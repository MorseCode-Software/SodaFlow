using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Functional;
using Eight = SodaFlow.Functional.Either<int, int, int, int, int, int, int, int>;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What a match that reads a local costs. One form is a lambda that captures
///     the local, and the other is the overload that takes the local as a state.
/// </summary>
/// <remarks>
///     <para>
///         A lambda that reads a local captures it. The compiler then makes a
///         closure for the local and a delegate for each lambda, at each call. A
///         static lambda captures nothing, thus the compiler makes its delegate one
///         time. The overloads with a state let a static lambda read the local.
///     </para>
///     <para>
///         Each pair here is the same match in the two forms. The form with a
///         capture is the baseline of its pair.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class MatchWithStateBenchmarks
{
    private readonly Maybe<int> some = Maybe.Some(1);
    private readonly Either<int, int> two = Either<int, int>.First(1);
    private readonly Eight eight = Eight.First(1);

    // Each call changes this. A constant needs no capture, and then the two forms of a pair are
    // the same code.
    private int offset;

    private int sum;

    /// <summary>Matches a Maybe with two lambdas that capture a local.</summary>
    [Benchmark(Baseline = true, Description = "Maybe.Match, a captured local")]
    public int MaybeMatchWithACapture()
    {
        int local = ++this.offset;

        return this.some.Match(onSome: v => v + local, onNone: () => local);
    }

    /// <summary>Matches a Maybe with the local as the state.</summary>
    [Benchmark(Description = "Maybe.Match, a state")]
    public int MaybeMatchWithAState() =>
        this.some.Match(state: ++this.offset, onSome: static (local, v) => v + local, onNone: static local => local);

    /// <summary>Runs one of two actions of a Maybe that capture this object.</summary>
    [Benchmark(Description = "Maybe.MatchVoid, a captured object")]
    public int MaybeMatchVoidWithACapture()
    {
        this.some.MatchVoid(onSome: v => this.sum += v, onNone: () => this.sum--);

        return this.sum;
    }

    /// <summary>Runs one of two actions of a Maybe with this object as the state.</summary>
    [Benchmark(Description = "Maybe.MatchVoid, a state")]
    public int MaybeMatchVoidWithAState()
    {
        this.some.MatchVoid(state: this, onSome: static (self, v) => self.sum += v, onNone: static self => self.sum--);

        return this.sum;
    }

    /// <summary>Maps a Maybe with a lambda that captures a local.</summary>
    [Benchmark(Description = "Maybe.Map, a captured local")]
    public Maybe<int> MaybeMapWithACapture()
    {
        int local = ++this.offset;

        return this.some.Map(v => v + local);
    }

    /// <summary>Maps a Maybe with the local as the state.</summary>
    [Benchmark(Description = "Maybe.Map, a state")]
    public Maybe<int> MaybeMapWithAState() => this.some.Map(state: ++this.offset, f: static (local, v) => v + local);

    /// <summary>Matches an Either of two with two lambdas that capture a local.</summary>
    [Benchmark(Description = "Either of two, Match, a captured local")]
    public int EitherTwoMatchWithACapture()
    {
        int local = ++this.offset;

        return this.two.Match(onFirst: v => v + local, onSecond: v => v - local);
    }

    /// <summary>Matches an Either of two with the local as the state.</summary>
    [Benchmark(Description = "Either of two, Match, a state")]
    public int EitherTwoMatchWithAState() =>
        this.two.Match(
            state: ++this.offset,
            onFirst: static (local, v) => v + local,
            onSecond: static (local, v) => v - local);

    /// <summary>Runs one of eight actions of an Either that capture this object.</summary>
    [Benchmark(Description = "Either of eight, MatchVoid, a captured object")]
    public int EitherEightMatchVoidWithACapture()
    {
        this.eight.MatchVoid(
            onFirst: v => this.sum += v,
            onSecond: v => this.sum += v,
            onThird: v => this.sum += v,
            onFourth: v => this.sum += v,
            onFifth: v => this.sum += v,
            onSixth: v => this.sum += v,
            onSeventh: v => this.sum += v,
            onEighth: v => this.sum += v);

        return this.sum;
    }

    /// <summary>Runs one of eight actions of an Either with this object as the state.</summary>
    [Benchmark(Description = "Either of eight, MatchVoid, a state")]
    public int EitherEightMatchVoidWithAState()
    {
        this.eight.MatchVoid(
            state: this,
            onFirst: static (self, v) => self.sum += v,
            onSecond: static (self, v) => self.sum += v,
            onThird: static (self, v) => self.sum += v,
            onFourth: static (self, v) => self.sum += v,
            onFifth: static (self, v) => self.sum += v,
            onSixth: static (self, v) => self.sum += v,
            onSeventh: static (self, v) => self.sum += v,
            onEighth: static (self, v) => self.sum += v);

        return this.sum;
    }
}
