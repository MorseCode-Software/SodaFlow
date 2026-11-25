using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

/// <summary>
///     Covers the stop of a listener while a transaction runs. A stream that fires
///     queues one firing for each of its listeners. A listener that stops after
///     that, before its firing runs, must get nothing.
/// </summary>
public sealed class ListenerStopTests
{
    // The two listeners are at one rank, and their sequence is not a contract. Thus, the test
    // requires one call, and not a call of a given listener.
    [Test]
    public async Task OfTwoListenersThatStopEachOtherOnlyTheFirstRuns()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> calls = [];
        List<IListener> listeners = [];

        listeners.Add(
            s.Listen(v =>
            {
                calls.Add("a" + v);
                listeners[1].Dispose();
            }));

        listeners.Add(
            s.Listen(v =>
            {
                calls.Add("b" + v);
                listeners[0].Dispose();
            }));

        s.Send(1);

        await Assert.That(calls.Count).IsEqualTo(1);
    }

    // The listener on the map is a rank above the listener on the sink. Thus, the map fires before
    // the first listener runs, and its firing waits in the queue when the first listener stops
    // the second.
    [Test]
    public async Task AListenerThatAnEarlierListenerStopsGetsNothing()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> calls = [];
        List<IListener> late = [];

        using IListener early = s.Listen(v =>
        {
            calls.Add("early" + v);

            if (late.Count > 0)
            {
                late[0].Dispose();
                late.Clear();
            }
        });

        late.Add(s.Map(static v => v * 10).Listen(v => calls.Add("late" + v)));

        s.Send(1);
        s.Send(2);

        await Assert.That(calls).IsEquivalentTo(expected: ["early1", "early2"], ordering: CollectionOrdering.Matching);
    }

    // The same with a stop from a different thread, which does not hold the transaction lock.
    // The stop returns before the queued firing runs. Thus, that firing must see it.
    [Test]
    public async Task AListenerThatADifferentThreadStopsDuringTheTransactionGetsNothing()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<string> calls = [];
        IListener late = s.Map(static v => v * 10).Listen(v => calls.Add("late" + v));

        using IListener early = s.Listen(v =>
        {
            calls.Add("early" + v);

            Thread other = new(late.Dispose);
            other.Start();
            other.Join();
        });

        s.Send(1);

        await Assert.That(calls).IsEquivalentTo(expected: ["early1"], ordering: CollectionOrdering.Matching);
    }

    // A listener that a transaction makes after its stream fired gets the earlier firings again.
    // When it stops before that replay runs, it must get nothing.
    [Test]
    public async Task AListenerThatStopsBeforeItsReplayGetsNothing()
    {
        StreamSink<int> s = Stream.CreateSink<int>();
        List<int> calls = [];

        using IListener maker = s.Listen(_ =>
        {
            IListener made = s.Listen(calls.Add);
            made.Dispose();
        });

        s.Send(1);

        await Assert.That(calls).IsEmpty();
    }
}
