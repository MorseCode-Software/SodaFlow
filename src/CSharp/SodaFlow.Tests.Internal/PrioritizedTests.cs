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
                    high.Link(trans: t, action: static (_, _) => { }, target: own);
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
            Chain(trans: trans, length: 3).Link(trans: trans, action: static (_, _) => { }, target: own);
            trans.Prioritized(node: new Node<int>(), action: static _ => { });
            return UnitInternal.Value;
        });

        await Assert.That(runs).IsEqualTo(1);
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

        using IStrongListener l = switched.BehaviorImpl.Updates().ListenStrongImpl(updates.Add);

        selector.Send(deep);

        await Assert.That(updates).IsEquivalentTo(expected: [110], ordering: CollectionOrdering.Matching);
    }

    // Gives the last node of a chain of the given length, which has that rank or more.
    private static Node<int> Chain(TransactionInternal trans, int length)
    {
        Node<int> node = new();

        for (int i = 0; i < length; i++)
        {
            Node<int> next = new();
            node.Link(trans: trans, action: static (_, _) => { }, target: next);
            node = next;
        }

        return node;
    }
}
