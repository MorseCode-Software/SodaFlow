using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests.Internal;

/// <summary>
///     Covers what a lazy sample gives after the transaction that took it failed.
///     A <see cref="Lazy{T}" /> kept the first value it gave. Thus, a transaction
///     that failed after the sample phase left that value in each cell that
///     started from it.
/// </summary>
public sealed class LazySampleTests
{
    // A hold whose initial value throws fails the transaction in its sample phase. The captures
    // and the forces that the code registered before it ran first. That is the failure that a
    // Lazy did not take back: the captures ran, and the code stored the values that they gave.
    private static void FailInTheSamplePhase() =>
        Stream.Never<int>()
            .HoldLazy(LazySample.Defer<int>(static () => throw new InvalidOperationException("from the sample phase")));

    [Test]
    public async Task ASampleTakenInATransactionThatFailsAfterItsCaptureGivesTheCommittedValue()
    {
        CellSink<int> cell = Cell.CreateSink(1);
        LazySample<int>? sample = null;
        LazySample<int>? mapped = null;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    sample = cell.SampleLazyImpl();
                    mapped = sample.MapImpl(static v => v * 10);
                    cell.Send(5);
                    FailInTheSamplePhase();
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(cell.Sample()).IsEqualTo(1);
        await Assert.That(sample?.Force()).IsEqualTo(1);
        await Assert.That(mapped?.Force()).IsEqualTo(10);
    }

    [Test]
    public async Task AHoldLazyMadeInATransactionThatFailsStartsFromTheCommittedValue()
    {
        CellSink<int> cell = Cell.CreateSink(1);
        StreamSink<int> updates = Stream.CreateSink<int>();
        Cell<int>? held = null;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    held = updates.HoldLazy(cell.SampleLazy());
                    cell.Send(5);
                    FailInTheSamplePhase();
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(held?.Sample()).IsEqualTo(1);

        updates.Send(7);

        await Assert.That(held?.Sample()).IsEqualTo(7);
    }

    [Test]
    public async Task AnAccumLazyMadeInATransactionThatFailsStartsFromTheCommittedValue()
    {
        CellSink<int> cell = Cell.CreateSink(1);
        StreamSink<int> additions = Stream.CreateSink<int>();
        Cell<int>? total = null;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    total = additions.AccumLazy(initialState: cell.SampleLazy(), f: static (a, s) => s + a);
                    cell.Send(5);
                    FailInTheSamplePhase();
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(total?.Sample()).IsEqualTo(1);

        additions.Send(2);

        await Assert.That(total?.Sample()).IsEqualTo(3);
    }

    // A looped cell forces its sample when a read asks for its value. A read after the sample
    // phase gets the captured value, and the transaction can fail after that read. The
    // placeholder is the cell under test here, because that is the object that holds the sample.
    [Test]
    public async Task ALoopedCellReadInATransactionThatFailsStartsFromTheCommittedValue()
    {
        CellSink<int> cell = Cell.CreateSink(1);
        LoopedCell<int>? placeholder = null;

        await Assert.That(() =>
                Transaction.RunVoid(() =>
                {
                    (_, placeholder) = Cell.Loop<int>().WithCaptures(l => (Cell: cell, Captures: l));
                    cell.Send(5);

                    LoopedCell<int> placeholderNow = placeholder;
                    Stream.Never<int>().HoldLazy(LazySample.Defer(placeholderNow.Sample));
                    FailInTheSamplePhase();
                }))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(placeholder?.Sample()).IsEqualTo(1);
    }

    [Test]
    public async Task ASampleThatForcesItselfThrows()
    {
        LazySample<int>[] box = new LazySample<int>[1];
        box[0] = new LazySample<int>(trans => box[0].Force(trans));

        Exception? thrown = await Assert.That(box[0].Force).ThrowsExactly<InvalidOperationException>();

        await Assert.That(thrown?.Message)
            .IsEqualTo(
                "This lazy sample forces itself. The loop that it samples is not closed, or the initial value of a "
                + "cell reads that cell.");
    }
}
