using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Tests.Internal;

public class NodeTests
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
}
