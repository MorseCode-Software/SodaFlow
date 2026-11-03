using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What one call to each frequent member of <see cref="Maybe{T}" /> costs.
/// </summary>
/// <remarks>
///     <para>
///         Each member here was a call to <c>Match</c> with a lambda that captured
///         an argument. Thus, each call allocated a closure and a delegate.
///         <c>Map</c> and the action members allocated two. <c>HasValue</c> is the
///         baseline: its lambdas captured nothing, thus the compiler cached their
///         delegates, and it allocated nothing.
///     </para>
///     <para>
///         Each benchmark has a value. A member that reads the value is the
///         frequent case, and the case with no value runs the same code with the
///         other branch.
///     </para>
///     <para>
///         The last three arms are the query members in <c>MaybeMonad</c>. A query
///         with two <c>from</c> clauses calls <c>SelectMany</c>, and a <c>where</c>
///         clause calls <c>Where</c>. The third of them is the query with both.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class MaybeBenchmarks
{
    private readonly Maybe<int> some = Maybe.Some(1);
    private readonly Maybe<int> other = Maybe.Some(2);

    private int sum;

    /// <summary>Reads if there is a value.</summary>
    [Benchmark(Baseline = true, Description = "HasValue")]
    public bool HasValue() => this.some.HasValue();

    /// <summary>Maps the value.</summary>
    [Benchmark(Description = "Map")]
    public Maybe<int> Map() => this.some.Map(static v => v + 1);

    /// <summary>Binds the value.</summary>
    [Benchmark(Description = "Bind")]
    public Maybe<int> Bind() => this.some.Bind(static v => Maybe.Some(v + 1));

    /// <summary>Runs an action with the value.</summary>
    [Benchmark(Description = "MatchSome")]
    public int MatchSome()
    {
        this.some.MatchSome(v => this.sum += v);
        return this.sum;
    }

    /// <summary>Runs one of two actions.</summary>
    [Benchmark(Description = "MatchVoid")]
    public int MatchVoid()
    {
        this.some.MatchVoid(onSome: v => this.sum += v, onNone: () => this.sum--);
        return this.sum;
    }

    /// <summary>Reads the value, with a default for the case with no value.</summary>
    [Benchmark(Description = "ValueOr")]
    public int ValueOr() => this.some.ValueOr(0);

    /// <summary>Gives this value, or the other one when there is no value.</summary>
    [Benchmark(Description = "OrElse")]
    public Maybe<int> OrElse() => this.some.OrElse(this.other);

    /// <summary>Combines two values.</summary>
    [Benchmark(Description = "Lift")]
    public Maybe<int> Lift() => this.some.Lift(b: this.other, f: static (a, b) => a + b);

    /// <summary>Combines two values through a query with two from clauses.</summary>
    [Benchmark(Description = "SelectMany, from two maybes")]
    public Maybe<int> SelectMany() =>
        from a in this.some
        from b in this.other
        select a + b;

    /// <summary>Keeps the value when it satisfies a predicate.</summary>
    [Benchmark(Description = "Where")]
    public Maybe<int> Where() => this.some.Where(static v => v > 0);

    /// <summary>Combines two values through a query with a where clause between its from clauses.</summary>
    [Benchmark(Description = "SelectMany and Where, a query with a where clause")]
    public Maybe<int> SelectManyWhere() =>
        from a in this.some
        where a > 0
        from b in this.other
        select a + b;
}
