using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Just the drainable-key tracker's update step, away from the rest of a transaction: b1398a3's
///     fold against a single-pass one, for a change shaped like a Pay and one shaped like a Drain.
/// </summary>
/// <remarks>
///     <para>
///         Inside a view model this step is lost in noise - a Pay allocates around 100 KB and varies
///         by more than that from one block of Pays to the next - so it is measured here on its own.
///     </para>
///     <para>
///         The inputs mirror what <c>ItemChange</c> hands the fold, including its types: new states
///         through <see cref="IReadOnlyDictionary{TKey,TValue}" />, removed keys through a
///         <see cref="HashSet{T}" /> behind <see cref="IReadOnlyCollection{T}" />, and changed keys
///         as <c>NewStates.Keys.Concat(Removed)</c>, which is how <c>ChangedKeys</c> is defined.
///         <see cref="State" /> stands in for the view model's internal <c>AccountState</c>.
///     </para>
/// </remarks>
[MemoryDiagnoser]
public class TrackerBenchmarks
{
    private const int AccountCount = 100_000;

    private IReadOnlyDictionary<int, State> drainNewStates = null!;
    private ImmutableHashSet<int> drainable = null!;
    private IReadOnlyDictionary<int, State> payNewStates = null!;
    private IReadOnlyCollection<int> removed = null!;

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
        int active = Enumerable.Range(0, AccountCount).First(key => !this.drainable.Contains(key));
        this.payNewStates = new Dictionary<int, State> { [active] = new State(Balance: 200_00, IsFrozen: false) };

        // A Drain: every drainable account goes to zero, so all of them leave the set.
        this.drainNewStates = frozen.ToDictionary(static key => key, static _ => new State(Balance: 0, IsFrozen: true));
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Pay")]
    public ImmutableHashSet<int> CommittedPay() => Committed(this.drainable, this.payNewStates, this.removed);

    [Benchmark]
    [BenchmarkCategory("Pay")]
    public ImmutableHashSet<int> SinglePassPay() => SinglePass(this.drainable, this.payNewStates, this.removed);

    [Benchmark]
    [BenchmarkCategory("Drain")]
    public ImmutableHashSet<int> CommittedDrain() => Committed(this.drainable, this.drainNewStates, this.removed);

    [Benchmark]
    [BenchmarkCategory("Drain")]
    public ImmutableHashSet<int> SinglePassDrain() => SinglePass(this.drainable, this.drainNewStates, this.removed);

    [Benchmark]
    [BenchmarkCategory("Pay")]
    public ImmutableHashSet<int> HybridPay() => Hybrid(this.drainable, this.payNewStates, this.removed);

    [Benchmark]
    [BenchmarkCategory("Drain")]
    public ImmutableHashSet<int> HybridDrain() => Hybrid(this.drainable, this.drainNewStates, this.removed);

    /// <summary>
    ///     Looks before it leaps only until something moves: <c>Contains</c> on the original set while
    ///     nothing has changed, so a Pay allocates nothing, then straight to the builder, whose
    ///     <c>Add</c> and <c>Remove</c> already do nothing for a key that is where it should be.
    /// </summary>
    private static ImmutableHashSet<int> Hybrid(
        ImmutableHashSet<int> keys,
        IReadOnlyDictionary<int, State> newStates,
        IReadOnlyCollection<int> removed)
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

    /// <summary>b1398a3's fold, line for line.</summary>
    private static ImmutableHashSet<int> Committed(
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
        IReadOnlyCollection<int> removed)
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

        foreach (int key in removed)
        {
            if (keys.Contains(key))
            {
                (builder ??= keys.ToBuilder()).Remove(key);
            }
        }

        return builder is null ? keys : builder.ToImmutable();
    }

    private static bool CanDrain(State state) => state.IsFrozen && state.Balance != 0;

    public sealed record State(long Balance, bool IsFrozen);
}
