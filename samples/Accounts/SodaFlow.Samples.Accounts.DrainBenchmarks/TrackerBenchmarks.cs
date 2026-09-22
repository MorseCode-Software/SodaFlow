using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BenchmarkDotNet.Attributes;
using JetBrains.Annotations;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Only the update step of the drainable-key tracker, and no other part of a transaction. There are
///     three folds: the fold that OptimizedDrain had, a fold that reads the change one time, and the mixed
///     fold that it has now. Each one gets a change of the Pay shape and a change of the Drain shape.
/// </summary>
/// <remarks>
///     <para>
///         In a view model this step is too small a part of a transaction to measure. A Pay takes
///         more than 20 microseconds and allocates approximately 21 KB. Thus, this benchmark
///         measures the step alone.
///     </para>
///     <para>
///         The inputs are the values that <c>ItemChange</c> gives to the fold, with the same
///         types. The new states come through <see cref="IReadOnlyDictionary{TKey,TValue}" />, the
///         removed keys come through a <see cref="HashSet{T}" /> behind <see
///         cref="IReadOnlyCollection{T}" />, and the changed keys are
///         <c>NewStates.Keys.Concat(Removed)</c>, which is the definition of <c>ChangedKeys</c>.
///         <see cref="State" /> replaces the internal <c>AccountState</c> of the view model.
///     </para>
/// </remarks>
[UsedImplicitly]
[MemoryDiagnoser]
public class TrackerBenchmarks
{
    private const int AccountCount = 100_000;

    // ReSharper disable NullableWarningSuppressionIsUsed - Set in Setup
    private IReadOnlyDictionary<int, State> drainNewStates = null!;
    private ImmutableHashSet<int> drainable = null!;
    private IReadOnlyDictionary<int, State> payNewStates = null!;
    private IReadOnlyCollection<int> removed = null!;

    // ReSharper restore NullableWarningSuppressionIsUsed - Set in Setup

    [GlobalSetup]
    public void Setup()
    {
        // Approximately one quarter of the accounts are frozen, as in the seed, and each frozen
        // account starts with a balance.
        Random random = new(42);
        List<int> frozen = [];

        for (int key = 0; key < AccountCount; key++)
        {
            if (random.Next(4) == 0)
            {
                frozen.Add(key);
            }
        }

        this.drainable = [.. frozen];
        this.removed = new HashSet<int>();

        // A Pay increases the balance of one active account, and a drain cannot empty that
        // account.
        int active = Enumerable.Range(start: 0, count: AccountCount).First(key => !this.drainable.Contains(key));
        this.payNewStates = new Dictionary<int, State> { [active] = new(Balance: 200_00, IsFrozen: false) };

        // A Drain sets each drainable account to zero, thus all of them go out of the set.
        this.drainNewStates = frozen.ToDictionary(keySelector: static key => key, elementSelector: static _ => new State(Balance: 0, IsFrozen: true));
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pay")]
    public ImmutableHashSet<int> FormerPay() => Former(drainableAccountKeys: this.drainable, newStates: this.payNewStates, removed: this.removed);

    [Benchmark]
    [BenchmarkCategory("Pay")]
    public ImmutableHashSet<int> SinglePassPay() => SinglePass(keys: this.drainable, newStates: this.payNewStates, removed: this.removed);

    [Benchmark]
    [BenchmarkCategory("Drain")]
    public ImmutableHashSet<int> FormerDrain() => Former(drainableAccountKeys: this.drainable, newStates: this.drainNewStates, removed: this.removed);

    [Benchmark]
    [BenchmarkCategory("Drain")]
    public ImmutableHashSet<int> SinglePassDrain() => SinglePass(keys: this.drainable, newStates: this.drainNewStates, removed: this.removed);

    [Benchmark]
    [BenchmarkCategory("Pay")]
    public ImmutableHashSet<int> HybridPay() => Hybrid(keys: this.drainable, newStates: this.payNewStates, removed: this.removed);

    [Benchmark]
    [BenchmarkCategory("Drain")]
    public ImmutableHashSet<int> HybridDrain() => Hybrid(keys: this.drainable, newStates: this.drainNewStates, removed: this.removed);

    /// <summary>
    ///     This calls <c>Contains</c> on the initial set while no membership changes, thus a Pay
    ///     allocates nothing. From the first change it uses the builder, and <c>Add</c> and
    ///     <c>Remove</c> on the builder do nothing for a key in the correct condition.
    /// </summary>
    private static ImmutableHashSet<int> Hybrid(
        ImmutableHashSet<int> keys,
        IReadOnlyDictionary<int, State> newStates,
        IEnumerable<int> removed)
    {
        ImmutableHashSet<int>.Builder? builder = null;

        foreach (KeyValuePair<int, State> pair in newStates)
        {
            bool drainable = pair.Value.IsDrainable;

            if (builder is null)
            {
                if (drainable == keys.Contains(pair.Key))
                {
                    continue;
                }

                builder = keys.ToBuilder();
            }

            if (drainable)
            {
                builder.Add(pair.Key);
            }
            else
            {
                builder.Remove(pair.Key);
            }
        }

        foreach (int key in removed)
        {
            if (builder is null)
            {
                if (!keys.Contains(key))
                {
                    continue;
                }

                builder = keys.ToBuilder();
            }

            builder.Remove(key);
        }

        return builder is null ? keys : builder.ToImmutable();
    }

    /// <summary>The fold that OptimizedDrain had before the mixed fold, line for line.</summary>
    private static ImmutableHashSet<int> Former(
        ImmutableHashSet<int> drainableAccountKeys,
        IReadOnlyDictionary<int, State> newStates,
        IReadOnlyCollection<int> removed)
    {
        IEnumerable<int> changedKeys = newStates.Keys.Concat(removed);

        ImmutableHashSet<int>.Builder builder = drainableAccountKeys.ToBuilder();
        builder.ExceptWith(removed.Concat(changedKeys));

        builder.UnionWith(
            newStates.Where(static pair => pair.Value.IsDrainable)
                .Select(static pair => pair.Key));

        return builder.ToImmutable();
    }

    /// <summary>This reads the change one time, and uses a builder only after the membership of
    /// a key changes.</summary>
    private static ImmutableHashSet<int> SinglePass(
        ImmutableHashSet<int> keys,
        IReadOnlyDictionary<int, State> newStates,
        IEnumerable<int> removed)
    {
        ImmutableHashSet<int>.Builder? builder = null;

        foreach (KeyValuePair<int, State> pair in newStates)
        {
            bool drainable = pair.Value.IsDrainable;

            if (drainable == keys.Contains(pair.Key))
            {
                continue;
            }

            builder ??= keys.ToBuilder();

            if (drainable)
            {
                builder.Add(pair.Key);
            }
            else
            {
                builder.Remove(pair.Key);
            }
        }

        foreach (int key in removed.Where(keys.Contains))
        {
            (builder ??= keys.ToBuilder()).Remove(key);
        }

        return builder is null ? keys : builder.ToImmutable();
    }

    private sealed record State(long Balance, bool IsFrozen)
    {
        public bool IsDrainable => this.IsFrozen && this.Balance != 0;
    }
}
