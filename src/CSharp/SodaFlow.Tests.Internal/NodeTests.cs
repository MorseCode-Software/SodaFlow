using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

public sealed class NodeTests
{
    [Test]
    public async Task TestNode()
    {
        Node<int> a = new();
        Node<int> b = new();

        TransactionInternal.Apply((trans, _) =>
        {
            a.Link(
                trans: trans,
                action: static (_, _) =>
                {
                },
                target: b);

            trans.Prioritized(
                node: a,
                action: static _ =>
                {
                });

            return UnitInternal.Value;
        });

        await Assert.That(a.Rank).IsLessThan(b.Rank);
    }

    [Test]
    public async Task TestDependency()
    {
        StreamSink<int> streamSink = Stream.CreateSink<int>();
        Stream<int> stream = streamSink.Map(static v => v * 2);

        await Assert.That(streamSink.Node.Rank).IsLessThan(stream.Node.Rank);
    }

    [Test]
    public async Task TestSnapshot()
    {
        CellSink<int> cellSink = Cell.CreateSink(0);
        StreamSink<Unit> streamSink = Stream.CreateSink<Unit>();

        Cell<int> cell =
            cellSink.Map(n =>
                {
                    Cell<int> c = Cell.Constant(0);

                    if (n > 0)
                    {
                        for (int i = 0; i < 50; i++)
                        {
                            c = c.Map(static v => v);
                        }
                    }

                    return n > 1 ? c : streamSink.Snapshot(c).Hold(0);
                })
                .SwitchC();

        long rank1 = cell.UpdatesImpl.Node.Rank;

        cellSink.Send(1);

        long rank2 = cell.UpdatesImpl.Node.Rank;

        cellSink.Send(2);

        long rank3 = cell.UpdatesImpl.Node.Rank;

        await Assert.That(rank1).IsEqualTo(rank2);
        await Assert.That(rank2).IsLessThan(rank3);
    }

    [Test]
    public async Task TestRankAfterTheOnlyListenerIsRemoved()
    {
        Node<int> a = new();
        Node<int> b = new();
        Node<int> c = new();
        Node<int> d = new() { Rank = 10 };

        TransactionInternal.Apply((trans, _) =>
        {
            a.Link(trans: trans, action: static (_, _) => { }, target: b);
            Node<int>.Target bc = b.Link(trans: trans, action: static (_, _) => { }, target: c);
            b.RemoveListener(bc);
            d.Link(trans: trans, action: static (_, _) => { }, target: b);

            return UnitInternal.Value;
        });

        await Assert.That(b.Rank).IsEqualTo(11);
        await Assert.That(c.Rank).IsEqualTo(2);

        TransactionInternal.Apply((trans, _) =>
        {
            b.Link(trans: trans, action: static (_, _) => { }, target: c);

            return UnitInternal.Value;
        });

        await Assert.That(c.Rank).IsEqualTo(12);
    }

    [Test]
    public async Task TestRankAfterAllListenersInTheSetAreRemoved()
    {
        Node<int> a = new();
        Node<int> b = new();
        Node<int> c = new();
        Node<int> e = new();
        Node<int> d = new() { Rank = 10 };
        Node<int> g = new() { Rank = 20 };
        int cRankAfterFirstRemoval = 0;
        int eRankAfterFirstRemoval = 0;
        int bRankAfterSecondRemoval = 0;
        int eRankAfterSecondRemoval = 0;

        TransactionInternal.Apply((trans, _) =>
        {
            a.Link(trans: trans, action: static (_, _) => { }, target: b);
            Node<int>.Target bc = b.Link(trans: trans, action: static (_, _) => { }, target: c);
            Node<int>.Target be = b.Link(trans: trans, action: static (_, _) => { }, target: e);
            b.RemoveListener(bc);
            d.Link(trans: trans, action: static (_, _) => { }, target: b);
            cRankAfterFirstRemoval = c.Rank;
            eRankAfterFirstRemoval = e.Rank;
            b.RemoveListener(be);
            g.Link(trans: trans, action: static (_, _) => { }, target: b);
            bRankAfterSecondRemoval = b.Rank;
            eRankAfterSecondRemoval = e.Rank;
            b.Link(trans: trans, action: static (_, _) => { }, target: e);

            return UnitInternal.Value;
        });

        await Assert.That(cRankAfterFirstRemoval).IsEqualTo(2);
        await Assert.That(eRankAfterFirstRemoval).IsEqualTo(12);
        await Assert.That(bRankAfterSecondRemoval).IsEqualTo(21);
        await Assert.That(eRankAfterSecondRemoval).IsEqualTo(12);
        await Assert.That(e.Rank).IsEqualTo(22);
    }
}
