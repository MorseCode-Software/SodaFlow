using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     Covers the order of the two locks that a request for a per-key cell takes:
///     the gate of the caches and the transaction lock.
/// </summary>
public sealed class PerKeyCellLockOrderTests
{
    private const int Rounds = 20;

    private const int RequestsPerRound = 50;

    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(10);

    private static readonly CellProjection<ItemState, int> Score =
        new(onPresent: static state => state.Score, onAbsent: static () => -1);

    // Thread A asks for cells with no transaction open. Thread B sends edits, and a listener in
    // each of those transactions asks for a cell. Before the fix, A took the gate and then waited
    // for the transaction lock inside the creation of a cell. B held the transaction lock and
    // waited for the gate. Each request here is for a new key, thus each one misses the cache
    // and takes both locks. The probe that found the defect stopped in its first round.
    //
    // If this test fails, a stuck thread holds the transaction lock. The threads are background
    // threads, thus the process can still end, but each test after this one waits for that lock.
    [Test]
    public async Task ARequestOutsideATransactionAndOneInsideItDoNotDeadlock()
    {
        for (int round = 0; round < Rounds; round++)
        {
            StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
                Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

            ReactiveCollection<int, ItemIdentity, ItemState> collection =
                ReactiveCollection<int, ItemIdentity, ItemState>.Create(
                    keySelector: TestUtil.KeyOf,
                    initialItems: [TestUtil.Item(number: 1, name: "one", score: 0)],
                    edits);

            List<Cell<int>> madeOutside = [];
            List<Cell<int>> madeInside = [];
            int sent = 0;

            IListener listener = collection.ItemChangesStream.ListenStrong(_ =>
                madeInside.Add(collection.StateCellImpl(key: 1000 + sent, projection: Score)));

            TaskCompletionSource<bool> go = new();

            Thread outside = new(() =>
            {
                go.Task.Wait();

                for (int i = 0; i < RequestsPerRound; i++)
                {
                    madeOutside.Add(collection.StateCellImpl(key: 1 + i, projection: Score));
                }
            }) { IsBackground = true };

            Thread inside = new(() =>
            {
                go.Task.Wait();

                for (int i = 0; i < RequestsPerRound; i++)
                {
                    sent = i;
                    edits.Send(TestUtil.Score(key: 1, score: i));
                }
            }) { IsBackground = true };

            outside.Start();
            inside.Start();
            go.SetResult(true);

            bool outsideFinished = outside.Join(Watchdog);
            bool insideFinished = inside.Join(Watchdog);

            listener.Unlisten();

            await Assert.That(outsideFinished).IsTrue().Because($"round {round}: the thread with no transaction is stuck");
            await Assert.That(insideFinished).IsTrue().Because($"round {round}: the thread in a transaction is stuck");
            await Assert.That(madeOutside.Count).IsEqualTo(RequestsPerRound);
            await Assert.That(madeInside.Count).IsEqualTo(RequestsPerRound);
        }
    }
}
