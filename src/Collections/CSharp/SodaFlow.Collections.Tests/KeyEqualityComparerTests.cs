using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>The immutable portion of an item keyed by a string that is matched loosely.</summary>
internal sealed record NamedIdentity(string Name);

/// <summary>The mutable portion of one.</summary>
internal sealed record NamedState(int Score);

/// <summary>
///     A collection given its own key equality comparer. Every keyed thing a change carries has to
///     agree with the store about what one key is, or a consumer that addresses an item by the key
///     the collection reports it under misses the change.
/// </summary>
public sealed class KeyEqualityComparerTests
{
    private static ReactiveCollection<string, NamedIdentity, NamedState> Create(
        Stream<CollectionEdit<string, NamedIdentity, NamedState>> edits,
        params Item<NamedIdentity, NamedState>[] initial) =>
        ReactiveCollection<string, NamedIdentity, NamedState>.Create(
            keySelector: static identity => identity.Name,
            keyEqualityComparer: StringComparer.OrdinalIgnoreCase,
            initialItems: initial,
            edits);

    private static Item<NamedIdentity, NamedState> Item(string name, int score) =>
        new(identity: new NamedIdentity(name), state: new NamedState(score));

    private static CollectionEdit<string, NamedIdentity, NamedState> Score(string key, int score) =>
        CollectionEdit<string, NamedIdentity, NamedState>.Update(
            key: key,
            // ReSharper disable once WithExpressionModifiesAllMembers
            transform: state => state with { Score = score });

    /// <summary>The store itself, which is the baseline everything else has to match.</summary>
    [Test]
    public async Task TheStoreMatchesKeysWithTheGivenComparer()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        await Assert.That(collection.SnapshotCell.Sample().ContainsKey("abc")).IsTrue();

        edits.Send(Score(key: "abc", score: 2));

        await Assert.That(
                collection.SnapshotCell.Sample().States.TryGetState(key: "ABC", state: out NamedState? state)
                    ? state.Score
                    : -1)
            .IsEqualTo(2);
    }

    /// <summary>
    ///     The keyed delta a root edit resolves to. An update addressed by one spelling lands in the
    ///     store under the spelling the item arrived with, so the delta has to answer to that one -
    ///     it is the only spelling a consumer reading the collection has ever been shown.
    /// </summary>
    [Test]
    public async Task ItemChangesMatchKeysWithTheGivenComparer()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        List<bool> newStatesHasCanonicalKey = [];
        List<bool> removedHasCanonicalKey = [];

        IListener l =
            collection.ItemChangesStream.ListenStrong(change =>
            {
                newStatesHasCanonicalKey.Add(change.NewStates.ContainsKey("ABC"));
                removedHasCanonicalKey.Add(change.Removed.Contains("ABC"));
            });

        edits.Send(Score(key: "abc", score: 2));
        edits.Send(CollectionEdit<string, NamedIdentity, NamedState>.Remove("aBc"));

        l.Unlisten();

        await Assert.That(newStatesHasCanonicalKey)
            .IsEquivalentTo(expected: [true, false], ordering: CollectionOrdering.Matching);

        await Assert.That(removedHasCanonicalKey)
            .IsEquivalentTo(expected: [false, true], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The same delta, taken from a view rather than from the collection.
    /// </summary>
    /// <remarks>
    ///     This one failed by reporting nothing at all rather than the wrong key. The root ordering was
    ///     built without the collection's comparer, so it could not find a key an edit addressed by
    ///     another spelling, emitted no operation for it, and the empty change was filtered out before
    ///     any view saw it - the store moved and every view above it went stale.
    /// </remarks>
    [Test]
    public async Task AViewsItemChangesMatchKeysWithTheGivenComparer()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        ReactiveCollection<string, NamedIdentity, NamedState> view =
            collection.SortBy(static (_, state) => state.Score);

        List<bool> newStatesHasCanonicalKey = [];

        IListener l =
            view.ItemChangesStream.ListenStrong(change =>
                newStatesHasCanonicalKey.Add(change.NewStates.ContainsKey("ABC")));

        edits.Send(Score(key: "abc", score: 2));

        l.Unlisten();

        await Assert.That(newStatesHasCanonicalKey)
            .IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A projection keeps one object per key, and keeps doing so under a comparer of the
    ///     collection's own. Its caches are keyed by what the view lists, which is always the
    ///     spelling the store holds, so matching those more tightly than the store does cannot go
    ///     wrong today - this holds the invariant rather than reproducing a fault.
    /// </summary>
    [Test]
    public async Task AProjectionKeepsOneObjectPerKeyUnderTheGivenComparer()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        List<string> projected = [];

        MappedItems<string> rows =
            collection.Map(key =>
            {
                projected.Add(key);

                return key;
            });

        await Assert.That(rows.Items.Sample().Count).IsEqualTo(1);

        // A structural edit, so the view moves and the projection runs again over every key.
        edits.Send(CollectionEdit<string, NamedIdentity, NamedState>.Add(Item(name: "DEF", score: 3)));

        await Assert.That(rows.Items.Sample().Count).IsEqualTo(2);

        // "ABC" was projected once and kept; only "DEF" is new.
        await Assert.That(projected).IsEquivalentTo(expected: ["ABC", "DEF"], ordering: CollectionOrdering.Matching);

        rows.Dispose();
    }

    /// <summary>
    ///     A per-item cell is cached per key, so either spelling reaches the same cell and that
    ///     cell hears an edit addressed by either.
    /// </summary>
    [Test]
    public async Task APerItemCellIsSharedAcrossSpellingsOfOneKey()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        List<int> scores = [];

        IListener l =
            collection.StateCell("abc")
                .ListenStrong(state => scores.Add(state.Match(onSome: static s => s.Score, onNone: static () => -1)));

        edits.Send(Score(key: "AbC", score: 2));

        l.Unlisten();

        await Assert.That(scores).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
    }
}
