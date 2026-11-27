using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Tests the nodes that link to their inputs only while their output has a
///     listener. See <see cref="DemandStreamNode{T}" />.
/// </summary>
public sealed class DemandLinkingTests
{
    // The unlink is lazy. After an unlisten and a transaction, the links stay. The first firing of an
    // input with no listener unlinks them.
    [Test]
    public async Task AMergeWithNoListenerUnlinksItsInputsWhenAnInputFires()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> merged = a.OrElse(b);

        IListener listener = merged.Listen(static _ => { });
        int linkedBefore = a.Node.GetListenersCopy().Count;
        listener.Dispose();
        Transaction.RunVoid(static () => { });
        bool unlinkedBeforeTheFiring = ((DemandStreamNode<int>)merged.Node).IsUnlinked;
        int linkedBeforeTheFiring = a.Node.GetListenersCopy().Count;
        a.Send(1);

        await Assert.That(linkedBefore).IsEqualTo(1);
        await Assert.That(unlinkedBeforeTheFiring).IsFalse();
        await Assert.That(linkedBeforeTheFiring).IsEqualTo(1);
        await Assert.That(((DemandStreamNode<int>)merged.Node).IsUnlinked).IsTrue();
        await Assert.That(a.Node.GetListenersCopy().Count).IsEqualTo(0);
        await Assert.That(b.Node.GetListenersCopy().Count).IsEqualTo(0);
    }

    [Test]
    public async Task AListenerAfterTheUnlinkLinksTheInputsAgain()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> merged = a.OrElse(b);
        List<int> received = [];

        merged.Listen(static _ => { }).Dispose();
        a.Send(0);
        bool unlinked = ((DemandStreamNode<int>)merged.Node).IsUnlinked;

        using IListener listener = merged.Listen(received.Add);
        a.Send(5);
        b.Send(6);

        await Assert.That(unlinked).IsTrue();
        await Assert.That(((DemandStreamNode<int>)merged.Node).IsUnlinked).IsFalse();
        await Assert.That(a.Node.GetListenersCopy().Count).IsEqualTo(1);
        await Assert.That(received).IsEquivalentTo(expected: [5, 6], ordering: CollectionOrdering.Matching);
    }

    // The unlink waits for the end of the transaction. Thus, a listener that comes back in the same
    // transaction finds the links that it left, and the merge folds each input one time.
    [Test]
    public async Task AListenerThatLeavesAndComesBackInOneTransactionGetsOneFold()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> sum = a.Merge(s2: b, f: static (x, y) => x + y);
        List<int> received = [];
        IListener first = sum.Listen(static _ => { });
        IListener? second = null;

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
            first.Dispose();
            second = sum.Listen(received.Add);
        });

        second?.Dispose();

        await Assert.That(received).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);
    }

    // A link again in a transaction after the inputs fired gives the merge their firings of that
    // transaction, as a first link did.
    [Test]
    public async Task ALinkAgainAfterTheInputsFiredGetsTheirFirings()
    {
        StreamSink<int> a = Stream.CreateSink<int>();
        StreamSink<int> b = Stream.CreateSink<int>();
        Stream<int> sum = a.Merge(s2: b, f: static (x, y) => x + y);
        List<int> received = [];
        IListener? listener = null;

        sum.Listen(static _ => { }).Dispose();
        a.Send(0);
        bool unlinked = ((DemandStreamNode<int>)sum.Node).IsUnlinked;

        Transaction.RunVoid(() =>
        {
            a.Send(1);
            b.Send(2);
            listener = sum.Listen(received.Add);
        });

        listener?.Dispose();

        await Assert.That(unlinked).IsTrue();
        await Assert.That(received).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);
    }

    // The shape of the Blotter: a switch over the merge of a list of rows. The merge that the switch
    // leaves unlinks, thus a row in it gets no work from it.
    [Test]
    public async Task TheMergeThatASwitchLeavesUnlinks()
    {
        StreamSink<int> r1 = Stream.CreateSink<int>();
        StreamSink<int> r2 = Stream.CreateSink<int>();
        StreamSink<int> r3 = Stream.CreateSink<int>();
        StreamSink<int> r4 = Stream.CreateSink<int>();
        CellSink<IReadOnlyList<Stream<int>>> rows = Cell.CreateSink<IReadOnlyList<Stream<int>>>([r1, r2, r3]);
        Stream<int> edits = rows.Map(static list => list.OrElse()).SwitchS();
        List<int> received = [];

        using IListener listener = edits.Listen(received.Add);
        int r1Before = r1.Node.GetListenersCopy().Count;

        rows.Send([r2, r3, r4]);
        r2.Send(7);

        await Assert.That(r1Before).IsEqualTo(1);
        await Assert.That(r1.Node.GetListenersCopy().Count).IsEqualTo(0);
        await Assert.That(r2.Node.GetListenersCopy().Count).IsEqualTo(1);
        await Assert.That(received).IsEquivalentTo(expected: [7], ordering: CollectionOrdering.Matching);
    }

    // A binding listens to the coalesce of the updates of a cell, which is UpdatesImpl.
    // In this project the internal Updates() of a behavior hides the public extension, and gives
    // the updates of the derivation itself.
    [Test]
    public async Task AMapWithNoListenerUnlinksAndReadsItsSource()
    {
        CellSink<int> source = Cell.CreateSink(1);
        int calls = 0;

        Cell<int> doubled = source.Map(x =>
        {
            calls++;
            return x * 2;
        });

        IListener listener = doubled.UpdatesImpl.Listen(static _ => { });
        int linkedBefore = source.Updates().Node.GetListenersCopy().Count;
        listener.Dispose();
        source.Send(3);
        int linkedAfter = source.Updates().Node.GetListenersCopy().Count;
        bool unlinked = ((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked;

        int callsBeforeSend = calls;
        source.Send(5);
        int callsAfterSend = calls;
        int first = doubled.Sample();
        int callsAfterFirst = calls;
        int second = doubled.Sample();
        int callsAfterSecond = calls;

        await Assert.That(unlinked).IsTrue();
        await Assert.That(linkedBefore - linkedAfter).IsEqualTo(1);
        await Assert.That(callsAfterSend).IsEqualTo(callsBeforeSend);
        await Assert.That(first).IsEqualTo(10);
        await Assert.That(callsAfterFirst).IsEqualTo(callsAfterSend + 1);
        await Assert.That(second).IsEqualTo(10);
        await Assert.That(callsAfterSecond).IsEqualTo(callsAfterFirst);
    }

    [Test]
    public async Task ALiftWithNoListenerUnlinksAndReadsItsInputs()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(10);
        int calls = 0;

        Cell<int> sum = a.Lift(c2: b, f: (x, y) =>
        {
            calls++;
            return x + y;
        });

        sum.UpdatesImpl.Listen(static _ => { }).Dispose();
        a.Send(1);
        bool unlinked = ((DemandStreamNode<int>)sum.Updates().Node).IsUnlinked;

        int callsBeforeSends = calls;
        a.Send(2);
        b.Send(20);
        int callsAfterSends = calls;
        int first = sum.Sample();
        int second = sum.Sample();
        int callsAfterSamples = calls;
        b.Send(30);
        int third = sum.Sample();

        await Assert.That(unlinked).IsTrue();
        await Assert.That(callsAfterSends).IsEqualTo(callsBeforeSends);
        await Assert.That(first).IsEqualTo(22);
        await Assert.That(second).IsEqualTo(22);
        await Assert.That(callsAfterSamples).IsEqualTo(callsAfterSends + 1);
        await Assert.That(third).IsEqualTo(32);
    }

    [Test]
    public async Task AMapLinksAgainWithItsValueAndItsChanges()
    {
        CellSink<int> source = Cell.CreateSink(1);
        Cell<int> doubled = source.Map(static x => x * 2);
        List<int> received = [];

        doubled.UpdatesImpl.Listen(static _ => { }).Dispose();
        source.Send(5);
        bool unlinkedBefore = ((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked;

        using IListener listener = doubled.UpdatesImpl.Listen(received.Add);
        bool unlinked = ((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked;
        int afterLink = doubled.Sample();
        source.Send(6);

        await Assert.That(unlinkedBefore).IsTrue();
        await Assert.That(unlinked).IsFalse();
        await Assert.That(afterLink).IsEqualTo(10);
        await Assert.That(received).IsEquivalentTo(expected: [12], ordering: CollectionOrdering.Matching);
        await Assert.That(doubled.Sample()).IsEqualTo(12);
    }

    // A link again in a transaction after the source fired gives the map that firing, and the
    // listener gets the new value one time.
    [Test]
    public async Task AMapThatLinksAgainAfterItsSourceFiredGetsTheChange()
    {
        CellSink<int> source = Cell.CreateSink(1);
        Cell<int> doubled = source.Map(static x => x * 2);
        List<int> received = [];
        IListener? listener = null;

        doubled.UpdatesImpl.Listen(static _ => { }).Dispose();
        source.Send(2);
        bool unlinked = ((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked;

        Transaction.RunVoid(() =>
        {
            source.Send(7);
            listener = doubled.UpdatesImpl.Listen(received.Add);
        });

        int after = doubled.Sample();
        listener?.Dispose();

        await Assert.That(unlinked).IsTrue();
        await Assert.That(received).IsEquivalentTo(expected: [14], ordering: CollectionOrdering.Matching);
        await Assert.That(after).IsEqualTo(14);
    }

    [Test]
    public async Task AChainOfMapsUnlinksToItsSource()
    {
        CellSink<int> source = Cell.CreateSink(1);
        Cell<int> inner = source.Map(static x => x + 1);
        Cell<int> outer = inner.Map(static x => x * 10);

        // One firing unlinks all of the chain. The unlink of the outer map unlinks the inner one.
        IListener listener = outer.UpdatesImpl.Listen(static _ => { });
        int linkedBefore = source.Updates().Node.GetListenersCopy().Count;
        listener.Dispose();
        source.Send(4);
        int linkedAfter = source.Updates().Node.GetListenersCopy().Count;

        await Assert.That(((DemandStreamNode<int>)inner.Updates().Node).IsUnlinked).IsTrue();
        await Assert.That(((DemandStreamNode<int>)outer.Updates().Node).IsUnlinked).IsTrue();
        await Assert.That(linkedBefore - linkedAfter).IsEqualTo(1);
        await Assert.That(outer.Sample()).IsEqualTo(50);
    }

    // A snapshot reads the value from before the transaction, also from a map that is not linked.
    [Test]
    public async Task ASnapshotOfAnUnlinkedMapReadsTheValueBeforeTheTransaction()
    {
        CellSink<int> source = Cell.CreateSink(1);
        Cell<int> doubled = source.Map(static x => x * 2);
        StreamSink<int> ticks = Stream.CreateSink<int>();
        List<int> received = [];

        doubled.UpdatesImpl.Listen(static _ => { }).Dispose();
        source.Send(2);
        bool unlinked = ((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked;
        using IListener listener = ticks.Snapshot(doubled).Listen(received.Add);

        source.Send(3);
        ticks.Send(0);

        Transaction.RunVoid(() =>
        {
            source.Send(4);
            ticks.Send(0);
        });

        await Assert.That(unlinked).IsTrue();
        await Assert.That(received).IsEquivalentTo(expected: [6, 6], ordering: CollectionOrdering.Matching);
        await Assert.That(doubled.Sample()).IsEqualTo(8);
    }

    // A map that no code listened to works for one firing of its source, and then unlinks.
    [Test]
    public async Task AMapThatNoCodeListensToUnlinksAfterOneFiring()
    {
        CellSink<int> source = Cell.CreateSink(1);
        int calls = 0;

        Cell<int> doubled = source.Map(x =>
        {
            calls++;
            return x * 2;
        });

        int callsBefore = calls;
        source.Send(2);
        int callsAfterFirst = calls;
        source.Send(3);
        int callsAfterSecond = calls;

        await Assert.That(callsAfterFirst).IsEqualTo(callsBefore + 1);
        await Assert.That(callsAfterSecond).IsEqualTo(callsAfterFirst);
        await Assert.That(doubled.Sample()).IsEqualTo(6);
        await Assert.That(((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked).IsTrue();
    }

    // Nothing above a sink can unlink. Thus, the coalesce of the updates of a sink stays linked, and a
    // bind on a sink does not link again.
    [Test]
    public async Task TheUpdatesOfASinkStayLinked()
    {
        CellSink<int> source = Cell.CreateSink(1);
        int linkedBefore = source.Updates().Node.GetListenersCopy().Count;

        source.UpdatesImpl.Listen(static _ => { }).Dispose();
        source.Send(2);

        // The coalesce adds one link to the updates of the sink, and keeps it after the send.
        await Assert.That(source.UpdatesImpl.Node is DemandStreamNode<int>).IsFalse();
        await Assert.That(source.Updates().Node.GetListenersCopy().Count - linkedBefore).IsEqualTo(1);
    }

    // Each pure stream operator unlinks after one firing of its input with no listener. This also
    // applies to a filter that drops the value, which never sends.
    [Test]
    public async Task EachPureStreamOperatorUnlinksAfterOneFiringWithNoListener()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        StreamSink<int> second = Stream.CreateSink<int>();
        StreamSink<int> third = Stream.CreateSink<int>();
        CellSink<int> cell = Cell.CreateSink(10);

        Stream<int>[] operators =
        [
            source.Map(static x => x + 1),
            source.Filter(static _ => true),
            source.Filter(static _ => false),
            source.Snapshot(cell),
            source.Snapshot(c: cell, f: static (x, y) => x + y),
            source.Choose(static x => Maybe.Some(x)),
            source.Choose(static _ => Maybe<int>.None),
            source.Merge(second).Map(static _ => 0),
            source.Merge(s2: second, s3: third).Map(static _ => 0)
        ];

        foreach (Stream<int> s in operators)
        {
            s.Listen(static _ => { }).Dispose();
        }

        int linkedBefore = source.Node.GetListenersCopy().Count;
        source.Send(1);

        await Assert.That(linkedBefore).IsEqualTo(operators.Length);
        await Assert.That(source.Node.GetListenersCopy().Count).IsEqualTo(0);
        await Assert.That(second.Node.GetListenersCopy().Count).IsEqualTo(0);
        await Assert.That(third.Node.GetListenersCopy().Count).IsEqualTo(0);

        foreach (Stream<int> s in operators)
        {
            await Assert.That(IsUnlinked(s)).IsTrue();
        }
    }

    [Test]
    public async Task FilterSomeUnlinksWhenItsInputFires()
    {
        StreamSink<Maybe<int>> source = Stream.CreateSink<Maybe<int>>();
        Stream<int> some = source.FilterSome();

        some.Listen(static _ => { }).Dispose();
        source.Send(Maybe<int>.None);

        await Assert.That(IsUnlinked(some)).IsTrue();
        await Assert.That(source.Node.GetListenersCopy().Count).IsEqualTo(0);
    }

    // One firing unlinks a chain of stream operators. The unlink of the last unlinks the ones above it.
    [Test]
    public async Task AChainOfStreamOperatorsUnlinksAfterOneFiring()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        Stream<int> first = source.Map(static x => x + 1);
        Stream<int> second = first.Filter(static x => x > 0);
        Stream<int> third = second.Map(static x => x * 2);

        third.Listen(static _ => { }).Dispose();
        source.Send(1);

        await Assert.That(IsUnlinked(first)).IsTrue();
        await Assert.That(IsUnlinked(second)).IsTrue();
        await Assert.That(IsUnlinked(third)).IsTrue();
        await Assert.That(source.Node.GetListenersCopy().Count).IsEqualTo(0);
    }

    // A map that links again in a transaction after its input fired gets that firing.
    [Test]
    public async Task AStreamMapThatLinksAgainGetsTheFiringsOfItsTransaction()
    {
        StreamSink<int> source = Stream.CreateSink<int>();
        Stream<int> doubled = source.Map(static x => x * 2);
        List<int> received = [];
        IListener? listener = null;

        doubled.Listen(static _ => { }).Dispose();
        source.Send(1);
        bool unlinked = IsUnlinked(doubled);

        Transaction.RunVoid(() =>
        {
            source.Send(5);
            listener = doubled.Listen(received.Add);
        });

        source.Send(6);
        listener?.Dispose();

        await Assert.That(unlinked).IsTrue();
        await Assert.That(received).IsEquivalentTo(expected: [10, 12], ordering: CollectionOrdering.Matching);
    }

    // A snapshot that links again reads the value of its cell at that time, also after the cell
    // changed while the snapshot was unlinked.
    [Test]
    public async Task ASnapshotThatLinksAgainReadsTheCellAsItIsNow()
    {
        StreamSink<int> ticks = Stream.CreateSink<int>();
        CellSink<int> cell = Cell.CreateSink(1);
        Cell<int> doubled = cell.Map(static x => x * 2);
        Stream<int> sampled = ticks.Snapshot(doubled);
        List<int> received = [];

        sampled.Listen(static _ => { }).Dispose();
        ticks.Send(0);
        bool unlinked = IsUnlinked(sampled);
        cell.Send(4);

        using IListener listener = sampled.Listen(received.Add);
        ticks.Send(0);
        cell.Send(5);
        ticks.Send(0);

        await Assert.That(unlinked).IsTrue();
        await Assert.That(received).IsEquivalentTo(expected: [8, 10], ordering: CollectionOrdering.Matching);
    }

    // Listen on a cell goes through the stream of its value. After the listener goes, the next update
    // unlinks that stream, and with it the map below it.
    [Test]
    public async Task AMapThatAListenerLeftUnlinksAfterItsNextUpdate()
    {
        CellSink<int> source = Cell.CreateSink(1);
        Cell<int> doubled = source.Map(static x => x * 2);
        List<int> received = [];

        doubled.Listen(static _ => { }).Dispose();
        int linkedBefore = source.Updates().Node.GetListenersCopy().Count;
        source.Send(2);
        int linkedAfter = source.Updates().Node.GetListenersCopy().Count;
        bool unlinked = ((DemandStreamNode<int>)doubled.Updates().Node).IsUnlinked;

        using IListener listener = doubled.Listen(received.Add);
        source.Send(3);

        await Assert.That(linkedBefore - linkedAfter).IsEqualTo(1);
        await Assert.That(unlinked).IsTrue();
        await Assert.That(received).IsEquivalentTo(expected: [4, 6], ordering: CollectionOrdering.Matching);
    }

    // The lift of a list is a derivation. With no listener, it unlinks after one update of an input.
    // A read then runs the function on the inputs, one time for each change.
    [Test]
    public async Task AListLiftWithNoListenerUnlinksAndReadsItsInputs()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(10);
        CellSink<int> c = Cell.CreateSink(100);
        int calls = 0;

        Cell<int> sum = new Cell<int>[] { a, b, c }.Lift(values =>
        {
            calls++;

            return values.Sum();
        });

        int linkedBefore = a.Updates().Node.GetListenersCopy().Count + b.Updates().Node.GetListenersCopy().Count;
        sum.UpdatesImpl.Listen(static _ => { }).Dispose();
        a.Send(2);
        bool unlinked = ((DemandStreamNode<int>)sum.Updates().Node).IsUnlinked;
        int linkedAfter = a.Updates().Node.GetListenersCopy().Count + b.Updates().Node.GetListenersCopy().Count;

        int callsBeforeSends = calls;
        b.Send(20);
        c.Send(200);
        int callsAfterSends = calls;
        int first = sum.Sample();
        int second = sum.Sample();
        int callsAfterSamples = calls;

        await Assert.That(unlinked).IsTrue();
        await Assert.That(linkedBefore - linkedAfter).IsEqualTo(2);
        await Assert.That(callsAfterSends).IsEqualTo(callsBeforeSends);
        await Assert.That(first).IsEqualTo(222);
        await Assert.That(second).IsEqualTo(222);
        await Assert.That(callsAfterSamples).IsEqualTo(callsAfterSends + 1);
    }

    // A list lift that links again takes its value from its inputs, and then gets each change.
    [Test]
    public async Task AListLiftLinksAgainWithItsValueAndItsChanges()
    {
        CellSink<int> a = Cell.CreateSink(1);
        CellSink<int> b = Cell.CreateSink(2);
        Cell<int> product = new Cell<int>[] { a, b }.Lift(static values => values[0] * values[1]);
        List<int> received = [];

        product.UpdatesImpl.Listen(static _ => { }).Dispose();
        a.Send(3);
        b.Send(4);

        using IListener listener = product.UpdatesImpl.Listen(received.Add);
        int afterLink = product.Sample();
        a.Send(5);

        await Assert.That(afterLink).IsEqualTo(12);
        await Assert.That(received).IsEquivalentTo(expected: [20], ordering: CollectionOrdering.Matching);
        await Assert.That(product.Sample()).IsEqualTo(20);
    }

    // A lift of no cells has no input to link. It keeps the value of the function.
    [Test]
    public async Task AListLiftOfNoCellsGivesTheValueOfTheFunction()
    {
        Cell<int> none = Array.Empty<Cell<int>>().Lift(static values => values.Count + 7);
        List<int> received = [];

        using IListener listener = none.Listen(received.Add);

        await Assert.That(none.Sample()).IsEqualTo(7);
        await Assert.That(received).IsEquivalentTo(expected: [7], ordering: CollectionOrdering.Matching);
    }

    // A switch of streams with no listener unlinks from its inner stream and its outer cell after one
    // firing of the inner stream.
    [Test]
    public async Task ASwitchOfStreamsWithNoListenerUnlinks()
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        CellSink<Stream<int>> outer = Cell.CreateSink<Stream<int>>(first);
        Stream<int> switched = outer.SwitchS();

        switched.Listen(static _ => { }).Dispose();
        int outerBefore = outer.Updates().Node.GetListenersCopy().Count;
        first.Send(1);

        await Assert.That(IsUnlinked(switched)).IsTrue();
        await Assert.That(first.Node.GetListenersCopy().Count).IsEqualTo(0);
        await Assert.That(outerBefore - outer.Updates().Node.GetListenersCopy().Count).IsEqualTo(1);
    }

    // A switch that links again selects the stream that the outer cell holds then, also after the
    // outer cell changed while the switch was unlinked.
    [Test]
    public async Task ASwitchOfStreamsThatLinksAgainSelectsTheStreamOfNow()
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        StreamSink<int> second = Stream.CreateSink<int>();
        CellSink<Stream<int>> outer = Cell.CreateSink<Stream<int>>(first);
        Stream<int> switched = outer.SwitchS();
        List<int> received = [];

        switched.Listen(static _ => { }).Dispose();
        first.Send(1);
        outer.Send(second);

        using IListener listener = switched.Listen(received.Add);
        first.Send(2);
        second.Send(3);
        outer.Send(first);
        first.Send(4);

        await Assert.That(received).IsEquivalentTo(expected: [3, 4], ordering: CollectionOrdering.Matching);
    }

    // The inner stream fires with no listener, which queues the unlink. In the same transaction, the
    // outer cell selects a new stream, which queues the selection after the unlink. The selection must
    // not link the new stream to the unlinked switch.
    [Test]
    public async Task ASelectionAfterTheUnlinkInOneTransactionLinksNothing()
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        StreamSink<int> second = Stream.CreateSink<int>();
        CellSink<Stream<int>> outer = Cell.CreateSink<Stream<int>>(first);
        Stream<int> switched = outer.SwitchS();

        switched.Listen(static _ => { }).Dispose();

        Transaction.RunVoid(() =>
        {
            first.Send(1);
            outer.Send(second);
        });

        await Assert.That(IsUnlinked(switched)).IsTrue();
        await Assert.That(first.Node.GetListenersCopy().Count).IsEqualTo(0);
        await Assert.That(second.Node.GetListenersCopy().Count).IsEqualTo(0);
    }

    // A switch of cells is a derivation. With no listener, it unlinks from the outer cell and from
    // the selection after one value. A read then selects from the outer cell as it is now.
    // The initial value of a new switch is such a value. Thus, the counts are from before the switch.
    [Test]
    public async Task ASwitchOfCellsWithNoListenerUnlinksAndReadsTheSelectionOfNow()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<int> second = Cell.CreateSink(10);
        CellSink<Cell<int>> outer = Cell.CreateSink<Cell<int>>(first);
        int outerBefore = outer.Updates().Node.GetListenersCopy().Count;
        int firstBefore = first.Updates().Node.GetListenersCopy().Count;
        Cell<int> switched = outer.SwitchC();

        IListener listener = switched.Updates().Listen(static _ => { });
        int outerLinked = outer.Updates().Node.GetListenersCopy().Count;
        int firstLinked = first.Updates().Node.GetListenersCopy().Count;
        listener.Dispose();
        first.Send(2);
        bool unlinked = ((DemandStreamNode<int>)switched.Updates().Node).IsUnlinked;
        int outerAfter = outer.Updates().Node.GetListenersCopy().Count;
        int firstAfter = first.Updates().Node.GetListenersCopy().Count;

        int afterFirst = switched.Sample();
        outer.Send(second);
        second.Send(20);
        int afterSecond = switched.Sample();

        await Assert.That(unlinked).IsTrue();
        await Assert.That(outerLinked - outerBefore).IsEqualTo(1);
        await Assert.That(firstLinked - firstBefore).IsEqualTo(1);
        await Assert.That(outerAfter).IsEqualTo(outerBefore);
        await Assert.That(firstAfter).IsEqualTo(firstBefore);
        await Assert.That(afterFirst).IsEqualTo(2);
        await Assert.That(afterSecond).IsEqualTo(20);
    }

    // A switch of cells that links again has the value of the cell that the outer cell holds now.
    // Then it gets each change of that cell only. The link again gives no update of its own.
    [Test]
    public async Task ASwitchOfCellsThatLinksAgainSelectsTheCellOfNow()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<int> second = Cell.CreateSink(10);
        CellSink<Cell<int>> outer = Cell.CreateSink<Cell<int>>(first);
        Cell<int> switched = outer.SwitchC();
        List<int> received = [];

        switched.Updates().Listen(static _ => { }).Dispose();
        first.Send(2);
        outer.Send(second);

        using IListener listener = switched.Updates().Listen(received.Add);
        int afterLink = switched.Sample();
        first.Send(3);
        second.Send(30);
        outer.Send(first);
        first.Send(4);

        await Assert.That(afterLink).IsEqualTo(10);
        await Assert.That(received).IsEquivalentTo(expected: [30, 3, 4], ordering: CollectionOrdering.Matching);
        await Assert.That(switched.Sample()).IsEqualTo(4);
    }

    // The selection fires with no listener, which queues the unlink, and the outer cell selects a new
    // cell in the same transaction. That selection must not keep a link to the unlinked switch.
    [Test]
    public async Task ASwitchOfCellsKeepsNoLinkFromASelectionInTheTransactionOfTheUnlink()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<int> second = Cell.CreateSink(10);
        CellSink<Cell<int>> outer = Cell.CreateSink<Cell<int>>(first);
        Cell<int> switched = outer.SwitchC();
        int secondBefore = second.Updates().Node.GetListenersCopy().Count;

        switched.Updates().Listen(static _ => { }).Dispose();

        Transaction.RunVoid(() =>
        {
            first.Send(2);
            outer.Send(second);
        });

        await Assert.That(((DemandStreamNode<int>)switched.Updates().Node).IsUnlinked).IsTrue();
        await Assert.That(second.Updates().Node.GetListenersCopy().Count).IsEqualTo(secondBefore);
        await Assert.That(switched.Sample()).IsEqualTo(10);
    }

    // SwitchB uses the same switch as SwitchC. A behavior has no updates of its own, thus a snapshot
    // reads it.
    [Test]
    public async Task ASwitchOfBehaviorsWithNoListenerReadsTheSelectionOfNow()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<int> second = Cell.CreateSink(10);
        CellSink<Behavior<int>> outer = Cell.CreateSink(first.AsBehavior());
        Behavior<int> switched = outer.SwitchB();

        switched.Updates().Listen(static _ => { }).Dispose();
        first.Send(2);
        bool unlinked = ((DemandStreamNode<int>)switched.Updates().Node).IsUnlinked;
        outer.Send(second.AsBehavior());
        second.Send(20);

        await Assert.That(unlinked).IsTrue();
        await Assert.That(switched.Sample()).IsEqualTo(20);
    }

    // A listen in a transaction that fails stays, as each link that the body made does. Thus, a
    // switch that links again in that transaction must keep its new selection.
    [Test]
    public async Task ASwitchOfCellsThatLinksAgainInAFailedTransactionFollowsItsSelection()
    {
        CellSink<int> first = Cell.CreateSink(1);
        CellSink<Cell<int>> outer = Cell.CreateSink<Cell<int>>(first);
        Cell<int> switched = outer.SwitchC();
        List<int> received = [];
        IListener? listener = null;

        switched.Updates().Listen(static _ => { }).Dispose();
        first.Send(2);

        try
        {
            Transaction.RunVoid(() =>
            {
                listener = switched.Updates().Listen(received.Add);
                throw new InvalidOperationException("fail");
            });
        }
        catch (InvalidOperationException)
        {
        }

        first.Send(3);
        listener?.Dispose();

        await Assert.That(received).IsEquivalentTo(expected: [3], ordering: CollectionOrdering.Matching);
    }

    // The same for a switch of streams, which selects again at rank zero.
    [Test]
    public async Task ASwitchOfStreamsThatLinksAgainInAFailedTransactionFollowsItsSelection()
    {
        StreamSink<int> first = Stream.CreateSink<int>();
        CellSink<Stream<int>> outer = Cell.CreateSink<Stream<int>>(first);
        Stream<int> switched = outer.SwitchS();
        List<int> received = [];
        IListener? listener = null;

        switched.Listen(static _ => { }).Dispose();
        first.Send(1);

        try
        {
            Transaction.RunVoid(() =>
            {
                listener = switched.Listen(received.Add);
                throw new InvalidOperationException("fail");
            });
        }
        catch (InvalidOperationException)
        {
        }

        first.Send(2);
        listener?.Dispose();

        await Assert.That(received).IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching);
    }

    // A map that unlinks keeps its committed value for the version of its source. Thus, a read does
    // not run the function again. Otherwise, a function that makes a cell makes a second cell.
    [Test]
    public async Task AMapThatUnlinksKeepsTheValueOfItsFunction()
    {
        CellSink<int> source = Cell.CreateSink(1);
        int calls = 0;

        Cell<int> mapped = source.Map(v =>
        {
            calls++;
            return v * 10;
        });

        mapped.Updates().Listen(static _ => { }).Dispose();
        source.Send(2);
        int callsAfterUnlink = calls;
        int first = mapped.Sample();
        int second = mapped.Sample();

        await Assert.That(((DemandStreamNode<int>)mapped.Updates().Node).IsUnlinked).IsTrue();
        await Assert.That(first).IsEqualTo(20);
        await Assert.That(second).IsEqualTo(20);
        await Assert.That(calls).IsEqualTo(callsAfterUnlink);
    }

    // A lazy sample reads the value at the end of its transaction. An unlinked cell calculates this
    // value from its inputs. Each type of derived cell must give the value of a copy with a listener.
    // The test sends into each input alone, because an input that the check of updates does not read
    // gives the previous value.
    [Test]
    public async Task ALazySampleOfAnUnlinkedCellGivesTheValueAtTheEndOfItsTransaction()
    {
        CellSink<int>[] s = [.. Enumerable.Range(start: 1, count: 8).Select(static i => Cell.CreateSink(i))];
        Cell<int>[] inputs = [.. s];
        CellSink<Cell<int>> outer = Cell.CreateSink<Cell<int>>(s[0]);

        Func<Cell<int>>[] kinds =
        [
            () => s[0].Map(static a => a * 3),
            () => s[0].Map(static a => a + 1).Map(static a => a * 2),
            () => s[0].Lift(c2: s[1], f: static (a, b) => a + 2 * b),
            () => s[0].Lift(c2: s[1], c3: s[2], f: static (a, b, c) => a + 2 * b + 3 * c),
            () => s[0].Lift(
                c2: s[1],
                c3: s[2],
                c4: s[3],
                f: static (a, b, c, d) => a + 2 * b + 3 * c + 4 * d),
            () => s[0].Lift(
                c2: s[1],
                c3: s[2],
                c4: s[3],
                c5: s[4],
                f: static (a, b, c, d, e) => a + 2 * b + 3 * c + 4 * d + 5 * e),
            () => s[0].Lift(
                c2: s[1],
                c3: s[2],
                c4: s[3],
                c5: s[4],
                c6: s[5],
                f: static (a, b, c, d, e, f) => a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f),
            () => s[0].Lift(
                c2: s[1],
                c3: s[2],
                c4: s[3],
                c5: s[4],
                c6: s[5],
                c7: s[6],
                f: static (a, b, c, d, e, f, g) =>
                    a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f + 7 * g),
            () => s[0].Lift(
                c2: s[1],
                c3: s[2],
                c4: s[3],
                c5: s[4],
                c6: s[5],
                c7: s[6],
                c8: s[7],
                f: static (a, b, c, d, e, f, g, h) =>
                    a + 2 * b + 3 * c + 4 * d + 5 * e + 6 * f + 7 * g + 8 * h),
            () => inputs.Lift(static values => values.Select(static (v, i) => (i + 1) * v).Sum()),
            outer.SwitchC,
            () => outer.SwitchC().Map(static a => a - 1)
        ];

        Cell<int>[] listened = [.. kinds.Select(static k => k())];
        Cell<int>[] unlistened = [.. kinds.Select(static k => k())];
        List<IListener> listeners = [.. listened.Select(static c => c.UpdatesImpl.Listen(static _ => { }))];

        foreach (Cell<int> c in unlistened)
        {
            c.UpdatesImpl.Listen(static _ => { }).Dispose();
        }

        for (int i = 0; i < s.Length; i++)
        {
            s[i].Send(i + 11);
        }

        bool unlinkedBefore = unlistened.All(static c => ((DemandStreamNode<int>)c.Updates().Node).IsUnlinked);

        Action[] rounds =
        [
            .. Enumerable.Range(start: 0, count: s.Length).Select(i => (Action)(() => s[i].Send(100 * (i + 1)))),
            () => outer.Send(s[1]),
            () =>
            {
                outer.Send(s[2]);
                s[2].Send(999);
            }
        ];

        LazySample<int>[][] lazies =
        [
            .. rounds.Select(send => Transaction.Run(() =>
            {
                send();
                return listened.Concat(unlistened).Select(static c => c.SampleLazy()).ToArray();
            }))
        ];

        int[] expected = [.. lazies.SelectMany(l => l.Take(kinds.Length)).Select(static l => l.Force())];
        int[] actual = [.. lazies.SelectMany(l => l.Skip(kinds.Length)).Select(static l => l.Force())];

        bool unlinkedAfter = unlistened.All(static c => ((DemandStreamNode<int>)c.Updates().Node).IsUnlinked);
        int[] sampledAfter = [.. unlistened.Select(static c => c.Sample())];
        int[] listenedAfter = [.. listened.Select(static c => c.Sample())];

        foreach (IListener listener in listeners)
        {
            listener.Dispose();
        }

        await Assert.That(unlinkedBefore).IsTrue();
        await Assert.That(unlinkedAfter).IsTrue();
        await Assert.That(actual).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
        await Assert.That(sampledAfter).IsEquivalentTo(expected: listenedAfter, ordering: CollectionOrdering.Matching);
    }

    private static bool IsUnlinked<T>(Stream<T> s) => ((DemandStreamNode<T>)s.Node).IsUnlinked;
}
