using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers an entry in the prioritized queue that changes the rank of its own
///     node. A change of rank puts each entry of the node in the queue again.
///     Thus, an entry that the node holds can run a second time.
/// </summary>
public sealed class PrioritizedTests
{
    // The target of a link holds a delegate or an action as an object. The tests link with a delegate that
    // does nothing.
    private static readonly Action<TransactionInternal, int> NoAction = static (_, _) => { };

    [Test]
    public async Task AnEntryThatRaisesTheRankOfItsOwnNodeRunsOnce()
    {
        Node<int> own = new();
        int runs = 0;

        TransactionInternal.Apply((trans, _) =>
        {
            Node<int> high = Chain(trans: trans, length: 3);

            // An entry with a higher rank stays in the queue after the first entry runs. Thus, the
            // close examines the queue again after the change of rank.
            Node<int> later = Chain(trans: trans, length: 5);
            trans.Prioritized(node: later, action: static _ => { });

            trans.Prioritized(
                node: own,
                action: t =>
                {
                    runs++;
                    high.Link(trans: t, action: NoAction, target: own);
                });

            return UnitInternal.Value;
        });

        await Assert.That(runs).IsEqualTo(1);
    }

    // An entry that throws does not get to the step that removes it from its node. A change of rank
    // in a subsequent transaction then puts it in the queue again. The close applies a change of rank
    // only while the queue holds an entry. Thus, the subsequent transaction also queues one.
    [Test]
    public async Task AnEntryThatThrowsDoesNotRunInASubsequentTransaction()
    {
        Node<int> own = new();
        int runs = 0;

        await Assert.That(() =>
                TransactionInternal.Apply((trans, _) =>
                {
                    trans.Prioritized(
                        node: own,
                        action: _ =>
                        {
                            runs++;
                            throw new InvalidOperationException("abort");
                        });

                    return UnitInternal.Value;
                }))
            .ThrowsExactly<InvalidOperationException>();

        TransactionInternal.Apply((trans, _) =>
        {
            Chain(trans: trans, length: 3).Link(trans: trans, action: NoAction, target: own);
            trans.Prioritized(node: new Node<int>(), action: static _ => { });
            return UnitInternal.Value;
        });

        await Assert.That(runs).IsEqualTo(1);
    }

    // The queue starts each transaction empty. Its minimum rank is then above its maximum rank,
    // and the first entry sets the minimum rank. A second entry below the first lowers it. Thus,
    // the two run in rank order and not in queue order.
    [Test]
    public async Task EntriesQueuedDeepFirstRunInRankOrder()
    {
        List<int> order = [];

        TransactionInternal.Apply((trans, _) =>
        {
            Node<int> deep = Chain(trans: trans, length: 5);
            Node<int> shallow = Chain(trans: trans, length: 2);
            trans.Prioritized(node: deep, action: _ => order.Add(5));
            trans.Prioritized(node: shallow, action: _ => order.Add(2));
            return UnitInternal.Value;
        });

        await Assert.That(order).IsEquivalentTo(expected: [2, 5], ordering: CollectionOrdering.Matching);
    }

    // The queue empties when the only entry runs, and the entry then queues one below its own
    // rank. The queue resets before the action runs, thus the new entry sets the minimum rank
    // again, and runs in the same transaction.
    [Test]
    public async Task AnEntryThatEmptiesTheQueueCanQueueAnEntryBelowItsRank()
    {
        List<int> order = [];

        TransactionInternal.Apply((trans, _) =>
        {
            Node<int> deep = Chain(trans: trans, length: 5);
            Node<int> shallow = Chain(trans: trans, length: 2);

            trans.Prioritized(
                node: deep,
                action: t =>
                {
                    order.Add(5);
                    t.Prioritized(node: shallow, action: _ => order.Add(2));
                });

            return UnitInternal.Value;
        });

        await Assert.That(order).IsEquivalentTo(expected: [5, 2], ordering: CollectionOrdering.Matching);
    }

    // The raw stream of the switch shows each firing. Cell.Updates combines the firings of one
    // transaction, thus it does not show a second one.
    [Test]
    public async Task SwitchCToACellOfHigherRankFiresItsStreamOnce()
    {
        CellSink<int> a = Cell.CreateSink(1);
        Cell<int> deep = Cell.CreateSink(100);

        for (int i = 0; i < 10; i++)
        {
            deep = deep.Map(static x => x + 1);
        }

        CellSink<Cell<int>> selector = Cell.CreateSink<Cell<int>>(a);
        Cell<int> switched = selector.SwitchC();
        List<int> updates = [];

        using IListener l = switched.Updates().ListenImpl(updates.Add);

        selector.Send(deep);

        await Assert.That(updates).IsEquivalentTo(expected: [110], ordering: CollectionOrdering.Matching);
    }

    // An entry that waits in the queue moves when a different entry raises the rank of its node. Its
    // node starts below the middle entry, and goes above it. Thus, the entry runs after the middle
    // entry, and not before it.
    [Test]
    public async Task AQueuedEntryRunsAtTheRankThatADifferentEntryGivesItsNode()
    {
        List<string> order = [];

        TransactionInternal.Apply((trans, _) =>
        {
            Node<int> waiting = Chain(trans: trans, length: 2);
            Node<int> middle = Chain(trans: trans, length: 4);
            Node<int> deeper = Chain(trans: trans, length: 6);

            trans.Prioritized(node: waiting, action: _ => order.Add("waiting"));
            trans.Prioritized(node: middle, action: _ => order.Add("middle"));

            trans.Prioritized(
                node: new Node<int>(),
                action: t =>
                {
                    order.Add("first");
                    deeper.Link(trans: t, action: NoAction, target: waiting);
                });

            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(expected: ["first", "middle", "waiting"], ordering: CollectionOrdering.Matching);
    }

    // The same move, in a transaction that then fails. The queue starts the next transaction empty,
    // and the entry that moved does not run again.
    [Test]
    public async Task AQueuedEntryThatMovedDoesNotRunAfterItsTransactionFails()
    {
        List<string> order = [];

        await Assert.That(() =>
                TransactionInternal.Apply((trans, _) =>
                {
                    Node<int> waiting = Chain(trans: trans, length: 2);
                    Node<int> middle = Chain(trans: trans, length: 4);
                    Node<int> deeper = Chain(trans: trans, length: 6);

                    trans.Prioritized(node: waiting, action: _ => order.Add("waiting"));

                    trans.Prioritized(
                        node: middle,
                        action: _ =>
                        {
                            order.Add("middle");
                            throw new InvalidOperationException("abort");
                        });

                    trans.Prioritized(
                        node: new Node<int>(),
                        action: t =>
                        {
                            order.Add("first");
                            deeper.Link(trans: t, action: NoAction, target: waiting);
                        });

                    return UnitInternal.Value;
                }))
            .ThrowsExactly<InvalidOperationException>();

        TransactionInternal.Apply((trans, _) =>
        {
            trans.Prioritized(node: Chain(trans: trans, length: 3), action: _ => order.Add("next"));
            return UnitInternal.Value;
        });

        await Assert.That(order)
            .IsEquivalentTo(expected: ["first", "middle", "next"], ordering: CollectionOrdering.Matching);
    }

    // The same move through the public operators. A switch goes to a cell 20 levels deep in the
    // transaction where a merge below the switch already has an entry. The merge then waits for the
    // value of the deep cell, and fires one time with the two values together.
    [Test]
    public async Task AMergeBelowASwitchToADeeperCellFiresOneTimeWithTheTwoValues()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        CellSink<int> shallow = Cell.CreateSink(1);
        CellSink<int> deepSink = Cell.CreateSink(100);
        Cell<int> deep = deepSink;

        for (int i = 0; i < 20; i++)
        {
            deep = deep.Map(static x => x + 1);
        }

        CellSink<Cell<int>> selector = Cell.CreateSink<Cell<int>>(shallow);
        List<int> seen = [];

        // The listen is in a later transaction than the switch. The updates of a switch also give its
        // initial value in the transaction that makes the switch, and this test is not about that.
        Stream<int> merged =
            Transaction.Run(() => s.Merge(s2: selector.SwitchC().Updates(), f: static (a, b) => a * 1000 + b));

        using IListener l = merged.Listen(seen.Add);

        Transaction.RunVoid(() =>
        {
            s.Send(7);
            selector.Send(deep);
        });

        Transaction.RunVoid(() =>
        {
            s.Send(8);
            deepSink.Send(200);
        });

        await Assert.That(seen).IsEquivalentTo(expected: [7120, 8220], ordering: CollectionOrdering.Matching);
    }

    // Gives the last node of a chain of the given length, which has that rank or more.
    private static Node<int> Chain(TransactionInternal trans, int length)
    {
        Node<int> node = new();

        for (int i = 0; i < length; i++)
        {
            Node<int> next = new();
            node.Link(trans: trans, action: NoAction, target: next);
            node = next;
        }

        return node;
    }
}
