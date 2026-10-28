using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to sort an array of Maybe values by their own ordering.
/// </summary>
/// <remarks>
///     <para>
///         A sort of n values calls CompareTo approximately n log n times, thus
///         the cost of one call is what it measures. The baseline sorts an array
///         of <see cref="Nullable{T}" /> with the same values in the same order.
///         That type gives the same ordering: no value sorts before each value.
///     </para>
///     <para>
///         The first version of <see cref="Maybe{T}.CompareTo" /> was two nested
///         <c>Match</c> calls. Each one captured a value in a closure, thus each
///         call allocated two objects.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class MaybeCompareBenchmarks
{
    private Maybe<int>[] maybeSource = [];
    private Maybe<int>[] maybeBuffer = [];
    private int?[] nullableSource = [];
    private int?[] nullableBuffer = [];

    /// <summary>How many values the arrays hold.</summary>
    [Params(1_000, 100_000)]
    public int Count { get; [UsedImplicitly] set; }

    /// <summary>
    ///     Fills the source arrays with the same values. One value in four has no
    ///     value.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        Random random = new(Seed: 12345);
        this.maybeSource = new Maybe<int>[this.Count];
        this.nullableSource = new int?[this.Count];

        for (int i = 0; i < this.Count; i++)
        {
            int value = random.Next();
            bool hasValue = value % 4 != 0;
            this.maybeSource[i] = hasValue ? Maybe.Some(value) : Maybe<int>.None;
            this.nullableSource[i] = hasValue ? value : null;
        }

        this.maybeBuffer = new Maybe<int>[this.Count];
        this.nullableBuffer = new int?[this.Count];
    }

    /// <summary>Copies the nullable values and sorts the copy.</summary>
    [Benchmark(Baseline = true, Description = "sort Nullable<int>")]
    public void SortNullable()
    {
        Array.Copy(sourceArray: this.nullableSource, destinationArray: this.nullableBuffer, length: this.Count);
        Array.Sort(this.nullableBuffer);
    }

    /// <summary>Copies the Maybe values and sorts the copy.</summary>
    [Benchmark(Description = "sort Maybe<int>")]
    public void SortMaybe()
    {
        Array.Copy(sourceArray: this.maybeSource, destinationArray: this.maybeBuffer, length: this.Count);
        Array.Sort(this.maybeBuffer);
    }
}
