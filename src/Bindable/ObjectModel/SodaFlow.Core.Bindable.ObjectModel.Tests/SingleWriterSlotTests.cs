using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

public sealed class SingleWriterSlotTests
{
    // A value of eight longs is more than one store on each platform. A field alone lets a
    // reader see some of the stores from one write and the others from the next. The scratch
    // harness measured that on .NET 10, with a plain field of this type and a writer that
    // stored without pause. About one read in four was torn, and the slot gave none in the
    // same loop.
    [Test]
    public async Task AReaderNeverSeesAValueThatTheWriterStoredInPart()
    {
        SingleWriterSlot<Wide> slot = new();
        const long writes = 200_000;
        long torn = 0;
        long wentBack = 0;
        long last = 0;
        // A TaskCompletionSource and not an event, because nothing here then needs a Dispose.
        TaskCompletionSource<bool> done = new();

        Thread writer =
            new(() =>
            {
                for (long i = 1; i <= writes; i++)
                {
                    slot.Write(Wide.Of(i));
                }

                done.TrySetResult(true);
            }) { IsBackground = true, Name = "writer" };

        writer.Start();

        while (!done.Task.IsCompleted)
        {
            Wide read = slot.Read();

            if (!read.IsOneValue)
            {
                torn++;
            }

            if (read.A < last)
            {
                wentBack++;
            }

            last = read.A;
        }

        writer.Join();

        await Assert.That(torn).IsEqualTo(0).Because("each read is a value that one write stored in full");
        await Assert.That(wentBack).IsEqualTo(0).Because("a later read never gives an earlier value");
        await Assert.That(slot.Read()).IsEqualTo(Wide.Of(writes));
    }

    // In the test above, a reader reads again only when a read and a write occur at the same time.
    // Thus, a run can complete with no read again, and the coverage of that path changes from one
    // run to the next. This test makes the counter odd, as a write that is not complete does.
    // Thus, each read must read again. The counter is private, and the test sets it by reflection,
    // because the slot has no other procedure to stop a write in the middle.
    [Test]
    public async Task AReaderThatFindsAWriteInProgressReadsAgainUntilTheWriteCompletes()
    {
        SingleWriterSlot<int> slot = new();
        slot.Write(7);

        // This read also compiles Read before the reader starts. Thus, the reader does not wait for it.
        int first = slot.Read();

        FieldInfo version =
            typeof(SingleWriterSlot<int>).GetField(
                name: "version",
                bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The slot has no field with the name version.");

        int even = version.GetValue(slot) is int value
            ? value
            : throw new InvalidOperationException("The version of the slot is not an int.");

        version.SetValue(obj: slot, value: even + 1);

        TaskCompletionSource<bool> entered = new();
        TaskCompletionSource<int> read = new();

        Thread reader =
            new(() =>
            {
                entered.TrySetResult(true);
                read.TrySetResult(slot.Read());
            }) { IsBackground = true, Name = "reader" };

        reader.Start();

        // After the reader sets entered, its only wait is the sleep that SpinWait does after many
        // spins. Thus, a reader in the wait state has read again one or more times.
        for (int polls = 0;
             !(entered.Task.IsCompleted && (reader.ThreadState & ThreadState.WaitSleepJoin) != 0);
             polls++)
        {
            if (polls == 30_000)
            {
                throw new InvalidOperationException("The reader did not wait in 30,000 polls.");
            }

            Thread.Sleep(1);
        }

        bool returnedDuringTheWrite = read.Task.IsCompleted;
        version.SetValue(obj: slot, value: even + 2);
        reader.Join();

        await Assert.That(first).IsEqualTo(7);

        await Assert.That(returnedDuringTheWrite)
            .IsFalse()
            .Because("a read does not complete while a write is in progress");

        await Assert.That(await read.Task).IsEqualTo(7);
    }

    [Test]
    public async Task AReadBeforeTheFirstWriteGivesTheDefault()
    {
        SingleWriterSlot<(int, string?)> slot = new();

        await Assert.That(slot.Read()).IsEqualTo((0, null));
    }

    private readonly record struct Wide(long A, long B, long C, long D, long E, long F, long G, long H)
    {
        public bool IsOneValue =>
            this.A == this.B
            && this.A == this.C
            && this.A == this.D
            && this.A == this.E
            && this.A == this.F
            && this.A == this.G
            && this.A == this.H;

        public static Wide Of(long i) => new(A: i, B: i, C: i, D: i, E: i, F: i, G: i, H: i);
    }
}
