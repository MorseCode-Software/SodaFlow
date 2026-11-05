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
