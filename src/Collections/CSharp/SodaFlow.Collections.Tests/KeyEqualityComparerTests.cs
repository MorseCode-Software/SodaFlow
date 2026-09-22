using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>The immutable part of an item whose key is a string with a comparer that is not
/// strict.</summary>
internal sealed record NamedIdentity(string Name);

/// <summary>The mutable part of such an item.</summary>
internal sealed record NamedState(int Score);

/// <summary>
///     A collection with its own key equality comparer. Each value with a key in a change must
///     agree with the store about the identity of a key. Without that agreement, a consumer that
///     names an item by the key from the collection does not get the change.
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

    /// <summary>The store, which is the base that each other value must agree with.</summary>
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
    ///     The delta with keys from a root edit. An update that names one spelling of a key goes
    ///     into the store at the spelling from the arrival of the item. Thus the delta must use
    ///     that spelling, because it is the only spelling that a consumer of the collection
    ///     reads.
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
    ///     The same delta, from a view and not from the collection.
    /// </summary>
    /// <remarks>
    ///     This test reported nothing, and did not report an incorrect key. The construction of the
    ///     root order did not use the comparer of the collection. Thus it did not find a key that an
    ///     edit named in a different spelling, it sent no operation for that key, and a filter
    ///     removed the empty change before a view read it. The store changed, and each view above it
    ///     became incorrect.
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
    ///     A projection keeps one object for each key, and it does the same with a comparer of the
    ///     collection. The key of each cache is a key from the view, which is always the spelling
    ///     in the store. Thus a comparer that is more strict than the comparer of the store cannot
    ///     give an incorrect result now. This test holds that rule and does not reproduce a
    ///     defect.
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

        // This is a structural edit, thus the view changes and the projection runs again for each
        // key.
        edits.Send(CollectionEdit<string, NamedIdentity, NamedState>.Add(Item(name: "DEF", score: 3)));

        await Assert.That(rows.Items.Sample().Count).IsEqualTo(2);

        // The projection made "ABC" one time and kept it, and only "DEF" is new.
        await Assert.That(projected).IsEquivalentTo(expected: ["ABC", "DEF"], ordering: CollectionOrdering.Matching);

        rows.Dispose();
    }

    /// <summary>
    ///     A cache holds one cell for each key, thus each spelling of a key reads the same cell,
    ///     and that cell gets an edit that names each spelling.
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
