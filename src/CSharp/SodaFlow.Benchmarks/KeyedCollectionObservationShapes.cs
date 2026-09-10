using System;
using System.Collections.Generic;
using System.Linq;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks;

/// <summary>Where a per-item observer is bound, and what it is bound to.</summary>
internal enum ObservationStyle
{
    /// <summary>
    ///     On the collection itself, which is what the other benchmarks measure.
    /// </summary>
    OnRoot,

    /// <summary>
    ///     Through a filtered view. Today this hands back the collection's own cell - the same
    ///     object, not an equal one - so it should measure exactly what <see cref="OnRoot" /> does.
    ///     It is here to prove that, and to fail loudly if it ever stops being true.
    /// </summary>
    ThroughView,

    /// <summary>
    ///     Through a filtered view, with the cell lifted against that view's membership so it holds
    ///     nothing for a key the view does not have.
    /// </summary>
    /// <remarks>
    ///     This is not what the library does. It is what view-scoping <c>StateCell</c> would
    ///     produce, built by hand so the cost of that change can be measured before deciding
    ///     whether to make it.
    /// </remarks>
    ViewScoped,

    /// <summary>
    ///     The same, with each observer's membership held as its own value and calmed, so that a
    ///     change to the view which does not move <i>this</i> key propagates no further than the
    ///     comparison that says so.
    /// </summary>
    /// <remarks>
    ///     <see cref="ViewScoped" /> is the obvious way to write view-scoping and wakes every
    ///     observer whenever the view reorders. This is the careful way, and the question it answers
    ///     is how much of that cost was the idea and how much was the writing.
    /// </remarks>
    ViewScopedPerKey,
}

/// <summary>
///     A collection with observers bound to some of its items, one of three ways.
/// </summary>
/// <remarks>
///     <para>
///         The view is a filter over a sort, which is the arrangement that makes the question
///         interesting: membership can change without an observed item changing, and order can
///         change without membership changing. An observer that watches the view's keys wakes for
///         both; one that watches its own item wakes for neither.
///     </para>
///     <para>
///         Every style observes the same keys and every one holds its listeners, because a cell
///         nobody listens to is never evaluated and would measure nothing.
///     </para>
/// </remarks>
internal sealed class ObservationShape
{
    private readonly StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits;

    // Load-bearing: these are what keep the observed cells alive and evaluated.
    // ReSharper disable once NotAccessedField.Local
    private readonly IReadOnlyList<IListener> listeners;

    private ObservationShape(
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        IReadOnlyList<IListener> listeners)
    {
        this.edits = edits;
        this.listeners = listeners;
    }

    /// <summary>How many items the observers cover.</summary>
    internal const int ObserverCount = 20;

    /// <summary>
    ///     The filter every view here uses. It keeps the even-numbered items, so the observed keys
    ///     below are inside it and the odd ones outside.
    /// </summary>
    private static bool Passes(ItemIdentity identity) => identity.Number % 2 == 0;

    internal static ObservationShape Build(int itemCount, ObservationStyle style)
    {
        List<Item<ItemIdentity, ItemState>> items = new(itemCount);

        for (int number = 0; number < itemCount; number++)
        {
            items.Add(new Item<ItemIdentity, ItemState>(
                ItemSeed.Identity(number),
                ItemSeed.State(number)));
        }

        return Transaction.Run(() =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    items,
                    edits);

            IReactiveCollection<int, ItemIdentity, ItemState> view = collection
                .FilterByIdentity(static identity => Passes(identity))
                .SortByDescending(static (_, state) => state.Score);

            List<IListener> listeners = [.. ObservedKeys(itemCount).Select(key => Observe(
                collection,
                view,
                key,
                style))];

            return new ObservationShape(edits, listeners);
        });
    }

    /// <summary>The keys observers are bound to: even, so the filter keeps them, and spread out.</summary>
    /// <remarks>
    ///     The stride is even for every size this runs at, so every key is too. Checked rather than
    ///     assumed, by <see cref="VerifyPremises" /> - an earlier version of this multiplied the
    ///     stride and took a remainder, which wrapped and produced half as many distinct keys as
    ///     observers, so the benchmark would have bound ten and reported twenty.
    /// </remarks>
    internal static IReadOnlyList<int> ObservedKeys(int itemCount)
    {
        int stride = itemCount / ObserverCount;

        return [.. Enumerable.Range(0, ObserverCount).Select(index => index * stride)];
    }

    /// <summary>
    ///     Checks what the arms below assume: that there are as many distinct observed keys as
    ///     observers, that the filter keeps every one of them, and that observing through a view is
    ///     the same cell as observing the collection.
    /// </summary>
    /// <exception cref="InvalidOperationException">If any of that stops being true.</exception>
    internal static void VerifyPremises(int itemCount)
    {
        IReadOnlyList<int> keys = ObservedKeys(itemCount);

        if (keys.Distinct().Count() != ObserverCount)
        {
            throw new InvalidOperationException(
                $"{ObserverCount} observers were meant to watch {ObserverCount} different keys, but "
                + $"the keys are [{string.Join(", ", keys)}].");
        }

        if (!keys.All(static key => Passes(ItemSeed.Identity(key))))
        {
            throw new InvalidOperationException(
                "The filter drops some of the observed keys, so the membership-lifted arm would "
                + $"measure observers holding nothing. The keys are [{string.Join(", ", keys)}].");
        }

        if (Passes(ItemSeed.Identity(UnobservedKeyInView)) is false || keys.Contains(UnobservedKeyInView))
        {
            throw new InvalidOperationException(
                $"Key {UnobservedKeyInView} is meant to be in the view and watched by nobody.");
        }

        Transaction.RunVoid(static () =>
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    static identity => identity.Number,
                    [new Item<ItemIdentity, ItemState>(ItemSeed.Identity(0), ItemSeed.State(0))],
                    edits);

            IReactiveCollection<int, ItemIdentity, ItemState> view =
                collection.FilterByIdentity(static identity => Passes(identity));

            if (!ReferenceEquals(collection.StateCell(0), view.StateCell(0)))
            {
                throw new InvalidOperationException(
                    "Observing through a view is no longer the collection's own cell, so the arm "
                    + "that assumes it is now measures something else and should be re-read.");
            }
        });
    }

    /// <summary>An even key no observer watches, which the view holds.</summary>
    internal static int UnobservedKeyInView => 2;

    private static IListener Observe(
        IReactiveCollection<int, ItemIdentity, ItemState> collection,
        IReactiveCollection<int, ItemIdentity, ItemState> view,
        int key,
        ObservationStyle style)
    {
        Cell<Maybe<ItemState>> cell = style switch
        {
            ObservationStyle.OnRoot => collection.StateCell(key),
            ObservationStyle.ThroughView => view.StateCell(key),

            // The shape view-scoping would produce: the collection's cell, and nothing for a key
            // this view does not hold. Lifting against KeysCell is the natural way to write it,
            // and it is also why this is worth measuring - KeysCell moves when the view reorders,
            // not only when its membership changes.
            ObservationStyle.ViewScoped =>
                collection.StateCell(key).Lift<Maybe<ItemState>, IOrderedKeys<int, ItemIdentity, ItemState>, Maybe<ItemState>>(
                    view.KeysCell,
                    (state, keys) => keys.Contains(key) ? state : Maybe<ItemState>.None),

            // Membership as one boolean per observer. The map still runs when the view's keys move,
            // because whether this key is among them has to be re-asked - but Calm stops there
            // unless the answer changed, so the cell below it recomputes only when this key really
            // enters or leaves.
            _ => collection.StateCell(key).Lift<Maybe<ItemState>, bool, Maybe<ItemState>>(
                view.KeysCell.Map(keys => keys.Contains(key)).Calm(),
                static (state, isMember) => isMember ? state : Maybe<ItemState>.None),
        };

        return cell.Updates().ListenStrong(static _ => { });
    }

    internal void Replace(int key, ItemState state) =>
        this.edits.Send(CollectionEdit<int, ItemIdentity, ItemState>.Update(key, _ => state));
}
