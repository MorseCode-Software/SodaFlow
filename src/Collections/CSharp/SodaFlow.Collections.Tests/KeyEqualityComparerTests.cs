using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     The immutable part of an item whose key is a string with a comparer that is
///     not strict.
/// </summary>
internal sealed record NamedIdentity(string Name);

/// <summary>The mutable part of such an item.</summary>
internal sealed record NamedState(int Score);

/// <summary>The same immutable part, with its own key, for the form with no selector.</summary>
// ReSharper disable once InheritdocConsiderUsage - the summary says what this identity is for, and the
// interface's summary does not.
file sealed record SelfNamedIdentity(string Name) : IIdentity<string>
{
    public string Key => this.Name;
}

/// <summary>
///     A collection with its own key equality comparer. Each value with a key in a
///     change must agree with the store about the identity of a key. Without that
///     agreement, a consumer that names an item by the key from the collection
///     does not get the change.
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

    /// <summary>
    ///     The form with no selector takes the comparer too, thus an identity that
    ///     holds its own key does not force a strict comparer.
    /// </summary>
    [Test]
    public async Task TheIdentityFormMatchesKeysWithTheGivenComparer()
    {
        StreamSink<CollectionEdit<string, SelfNamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, SelfNamedIdentity, NamedState>>();

        Item<SelfNamedIdentity, NamedState> initial =
            new(identity: new SelfNamedIdentity("ABC"), state: new NamedState(1));

        ReactiveCollection<string, SelfNamedIdentity, NamedState> collection =
            ReactiveCollection.Create(
                keyEqualityComparer: StringComparer.OrdinalIgnoreCase,
                initialEntries: [initial],
                edits);

        await Assert.That(collection.SnapshotCell.Sample().ContainsKey("abc")).IsTrue();

        edits.Send(
            CollectionEdit<string, SelfNamedIdentity, NamedState>.Update(
                key: "abc",
                // ReSharper disable once WithExpressionModifiesAllMembers
                transform: static state => state with { Score = 2 }));

        await Assert.That(
                collection.SnapshotCell.Sample().States.TryGetState(key: "ABC", state: out NamedState? state)
                    ? state.Score
                    : -1)
            .IsEqualTo(2);
    }

    /// <summary>
    ///     The identity form with a comparer also takes its initial contents from a
    ///     cell, which the transaction that builds the collection samples at its
    ///     close.
    /// </summary>
    [Test]
    public async Task TheIdentityFormWithAComparerReadsItsInitialEntriesFromACell()
    {
        StreamSink<CollectionEdit<string, SelfNamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, SelfNamedIdentity, NamedState>>();

        CellSink<IEnumerable<Item<SelfNamedIdentity, NamedState>>> initialEntries =
            Cell.CreateSink<IEnumerable<Item<SelfNamedIdentity, NamedState>>>([]);

        ReactiveCollection<string, SelfNamedIdentity, NamedState> collection =
            Transaction.Run(() =>
            {
                ReactiveCollection<string, SelfNamedIdentity, NamedState> built =
                    ReactiveCollection.Create(
                        keyEqualityComparer: StringComparer.OrdinalIgnoreCase,
                        initialEntries: initialEntries,
                        edits);

                initialEntries.Send([new Item<SelfNamedIdentity, NamedState>(
                    identity: new SelfNamedIdentity("ABC"),
                    state: new NamedState(1))]);

                return built;
            });

        await Assert.That(collection.SnapshotCell.Sample().ContainsKey("abc")).IsTrue();

        edits.Send(
            CollectionEdit<string, SelfNamedIdentity, NamedState>.Update(
                key: "abc",
                // ReSharper disable once WithExpressionModifiesAllMembers
                transform: static state => state with { Score = 2 }));

        await Assert.That(
                collection.SnapshotCell.Sample().States.TryGetState(key: "ABC", state: out NamedState? state)
                    ? state.Score
                    : -1)
            .IsEqualTo(2);
    }

    /// <summary>
    ///     The cache of per-key cells compares keys as the collection does. Thus, one
    ///     key in two spellings gives one cell on the collection, and one on a view.
    /// </summary>
    [Test]
    public async Task PerKeyCellsAreSharedAcrossSpellingsOfOneKey()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        ReactiveCollection<string, NamedIdentity, NamedState> view =
            collection.SortBy(static (_, state) => state.Score);

        await Assert.That(collection.StateCell("abc")).IsSameReferenceAs(collection.StateCell("ABC"));
        await Assert.That(collection.IdentityCell("abc")).IsSameReferenceAs(collection.IdentityCell("ABC"));
        await Assert.That(collection.ItemCell("abc")).IsSameReferenceAs(collection.ItemCell("ABC"));
        await Assert.That(view.StateCell("abc")).IsSameReferenceAs(view.StateCell("ABC"));
    }

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
    ///     The delta with keys from a root edit. An edit that names a different
    ///     spelling of a key changes that key in the delta. This test finds the key
    ///     with the comparer of the collection, thus it cannot see a spelling.
    ///     <see cref="AnUpdateInAnotherSpellingKeepsTheSpellingOfTheStore" /> and
    ///     <see cref="ARemovalInAnotherSpellingReportsTheSpellingOfTheStore" /> make
    ///     sure that the delta has the spelling in the store.
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
            collection.ItemChangesStream.Listen(change =>
            {
                newStatesHasCanonicalKey.Add(change.NewStates.ContainsKey("ABC"));
                removedHasCanonicalKey.Add(change.Removed.Contains("ABC"));
            });

        edits.Send(Score(key: "abc", score: 2));
        edits.Send(CollectionEdit<string, NamedIdentity, NamedState>.Remove("aBc"));

        l.Dispose();

        await Assert.That(newStatesHasCanonicalKey)
            .IsEquivalentTo(expected: [true, false], ordering: CollectionOrdering.Matching);

        await Assert.That(removedHasCanonicalKey)
            .IsEquivalentTo(expected: [false, true], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     An update that names a key in a different spelling writes the state at the
    ///     spelling in the store. The comparer of the collection cannot see a
    ///     spelling, thus this test compares each key with an ordinal comparer.
    /// </summary>
    /// <remarks>
    ///     Before, the state map, the item change, and the sort view had the spelling
    ///     of the edit. The identity map had the spelling of the arrival, and a new
    ///     build of the same sort also had it.
    /// </remarks>
    [Test]
    public async Task AnUpdateInAnotherSpellingKeepsTheSpellingOfTheStore()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1), Item(name: "DEF", score: 2));

        ReactiveCollection<string, NamedIdentity, NamedState> view =
            collection.SortBy(static (_, state) => state.Score);

        List<string> changedKeys = [];
        List<string> operationKeys = [];

        IListener changes = collection.ItemChangesStream.Listen(change => changedKeys.AddRange(change.ChangedKeys));

        IListener operations =
            view.KeyChangesStream.Listen(change =>
                operationKeys.AddRange(change.Operations.Select(static operation => operation.Key)));

        // The new score moves the item to the end of the view.
        edits.Send(Score(key: "abc", score: 3));

        changes.Dispose();
        operations.Dispose();

        CollectionSnapshot<string, NamedIdentity, NamedState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(snapshot.States.Keys.OrderBy(keySelector: static key => key, comparer: StringComparer.Ordinal))
            .IsEquivalentTo(expected: ["ABC", "DEF"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);

        await Assert.That(changedKeys)
            .IsEquivalentTo(expected: ["ABC"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);

        await Assert.That(operationKeys)
            .IsEquivalentTo(expected: ["ABC"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);

        await Assert.That(view.KeysCell.Sample())
            .IsEquivalentTo(expected: ["DEF", "ABC"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A removal that names a key in a different spelling reports the spelling in
    ///     the store. The item change and the operations of the collection both have
    ///     that spelling.
    /// </summary>
    [Test]
    public async Task ARemovalInAnotherSpellingReportsTheSpellingOfTheStore()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1), Item(name: "DEF", score: 2));

        List<string> removedKeys = [];
        List<string> operationKeys = [];

        IListener changes = collection.ItemChangesStream.Listen(change => removedKeys.AddRange(change.Removed));

        IListener operations =
            collection.KeyChangesStream.Listen(change =>
                operationKeys.AddRange(change.Operations.Select(static operation => operation.Key)));

        edits.Send(CollectionEdit<string, NamedIdentity, NamedState>.Remove("aBc"));

        changes.Dispose();
        operations.Dispose();

        await Assert.That(removedKeys)
            .IsEquivalentTo(expected: ["ABC"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);

        await Assert.That(operationKeys)
            .IsEquivalentTo(expected: ["ABC"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     An update of a key that the same edit adds keeps the spelling of the add,
    ///     because that add is the arrival. A removal of the earlier item in a
    ///     different spelling does not change that.
    /// </summary>
    [Test]
    public async Task AnUpdateOfAKeyThatTheEditAddsKeepsTheSpellingOfTheAdd()
    {
        StreamSink<CollectionEdit<string, NamedIdentity, NamedState>> edits =
            Stream.CreateSink<CollectionEdit<string, NamedIdentity, NamedState>>();

        ReactiveCollection<string, NamedIdentity, NamedState> collection =
            Create(edits: edits, Item(name: "ABC", score: 1));

        List<string> changedKeys = [];

        IListener changes = collection.ItemChangesStream.Listen(change => changedKeys.AddRange(change.ChangedKeys));

        edits.Send(
            CollectionEdit<string, NamedIdentity, NamedState>.Add(Item(name: "GHI", score: 4))
                .CombineWith(Score(key: "ghi", score: 5)));

        edits.Send(
            CollectionEdit<string, NamedIdentity, NamedState>.Remove("ABC")
                .CombineWith(CollectionEdit<string, NamedIdentity, NamedState>.Add(Item(name: "abc", score: 6)))
                .CombineWith(Score(key: "Abc", score: 7)));

        changes.Dispose();

        CollectionSnapshot<string, NamedIdentity, NamedState> snapshot = collection.SnapshotCell.Sample();

        await Assert.That(changedKeys)
            .IsEquivalentTo(expected: ["GHI", "abc"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);

        await Assert.That(snapshot.States.Keys.OrderBy(keySelector: static key => key, comparer: StringComparer.Ordinal))
            .IsEquivalentTo(expected: ["GHI", "abc"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);

        await Assert.That(snapshot.Identities.Keys.OrderBy(keySelector: static key => key, comparer: StringComparer.Ordinal))
            .IsEquivalentTo(expected: ["GHI", "abc"], comparer: StringComparer.Ordinal, ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The same delta, from a view and not from the collection.
    /// </summary>
    /// <remarks>
    ///     This test reported nothing, and did not report an incorrect key. The
    ///     construction of the root order did not use the comparer of the collection.
    ///     Thus, it did not find a key that an edit named in a different spelling, it
    ///     sent no operation for that key, and a filter removed the empty change
    ///     before a view read it. The store changed, and each view above it became
    ///     incorrect.
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
            view.ItemChangesStream.Listen(change =>
                newStatesHasCanonicalKey.Add(change.NewStates.ContainsKey("ABC")));

        edits.Send(Score(key: "abc", score: 2));

        l.Dispose();

        await Assert.That(newStatesHasCanonicalKey)
            .IsEquivalentTo(expected: [true], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     A projection keeps one object for each key, and it does the same with a
    ///     comparer of the collection. The key of each cache is a key from the view.
    ///     The collection gives the spelling in the store to each key that an edit
    ///     names. Thus, a key from the view always has that spelling. A comparer that
    ///     is more strict than the comparer of the store cannot give an incorrect
    ///     result now. This test holds that rule and does not reproduce a defect.
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
    ///     A cache holds one cell for each key, thus each spelling of a key reads the
    ///     same cell, and that cell gets an edit that names each spelling.
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
                .Listen(state => scores.Add(state.Match(onSome: static s => s.Score, onNone: static () => -1)));

        edits.Send(Score(key: "AbC", score: 2));

        l.Dispose();

        await Assert.That(scores).IsEquivalentTo(expected: [1, 2], ordering: CollectionOrdering.Matching);
    }
}
