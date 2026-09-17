using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using JetBrains.Annotations;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Just the drainable-key tracker's update step, away from the rest of a transaction: the fold
///     OptimizedDrain used to have, a single-pass one, and the hybrid it has now, for a change shaped
///     like a Pay and one shaped like a Drain.
/// </summary>
/// <remarks>
///     <para>
///         Inside a view model this step is too small a part of a transaction to read - a Pay takes
///         over 20 microseconds and allocates about 21 KB - so it is measured here on its own.
///     </para>
///     <para>
///         The inputs mirror what <c>ItemChange</c> hands the fold, including its types: new states
///         through <see cref="IReadOnlyDictionary{TKey,TValue}" />, removed keys through a
///         <see cref="HashSet{T}" /> behind <see cref="IReadOnlyCollection{T}" />, and changed keys
///         as <c>NewStates.Keys.Concat(Removed)</c>, which is how <c>ChangedKeys</c> is defined.
///         <see cref="State" /> stands in for the view model's internal <c>AccountState</c>.
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
        // About a quarter frozen, as in the seed; every frozen account starts with money.
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

        // A Pay: one active account's balance goes up, and it stays undrainable.
        int active = Enumerable.Range(start: 0, count: AccountCount).First(key => !this.drainable.Contains(key));
        this.payNewStates = new Dictionary<int, State> { [active] = new(Balance: 200_00, IsFrozen: false) };

        // A Drain: every drainable account goes to zero, so all of them leave the set.
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
    ///     Looks before it leaps only until something moves: <c>Contains</c> on the original set while
    ///     nothing has changed, so a Pay allocates nothing, then straight to the builder, whose
    ///     <c>Add</c> and <c>Remove</c> already do nothing for a key that is where it should be.
    /// </summary>
    private static ImmutableHashSet<int> Hybrid(
        ImmutableHashSet<int> keys,
        IReadOnlyDictionary<int, State> newStates,
        IEnumerable<int> removed)
    {
        ImmutableHashSet<int>.Builder? builder = null;

        foreach (KeyValuePair<int, State> pair in newStates)
        {
            bool drainable = CanDrain(pair.Value);

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

    /// <summary>The fold OptimizedDrain had before the hybrid, line for line.</summary>
    private static ImmutableHashSet<int> Former(
        ImmutableHashSet<int> drainableAccountKeys,
        IReadOnlyDictionary<int, State> newStates,
        IReadOnlyCollection<int> removed)
    {
        IEnumerable<int> changedKeys = newStates.Keys.Concat(removed);

        ImmutableHashSet<int>.Builder builder = drainableAccountKeys.ToBuilder();
        builder.ExceptWith(removed.Concat(changedKeys));

        builder.UnionWith(
            newStates.Where(static pair => CanDrain(pair.Value))
                .Select(static pair => pair.Key));

        return builder.ToImmutable();
    }

    /// <summary>One pass over the change, and a builder only once a key's membership really moves.</summary>
    private static ImmutableHashSet<int> SinglePass(
        ImmutableHashSet<int> keys,
        IReadOnlyDictionary<int, State> newStates,
        IEnumerable<int> removed)
    {
        ImmutableHashSet<int>.Builder? builder = null;

        foreach (KeyValuePair<int, State> pair in newStates)
        {
            bool drainable = CanDrain(pair.Value);

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

    private static bool CanDrain(State state) => state.IsFrozen && state.Balance != 0;

    private sealed record State(long Balance, bool IsFrozen);
}
