using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests the command. It covers availability, firing, the type of the
///     parameter, and disposal.
/// </summary>
public sealed class BindableActionTests
{
    private static IBindableAction<T> Action<T>(StreamSink<T> sink, Cell<bool>? isEnabled = null)
        where T : notnull =>
        sink.ToBindableActionImpl(isEnabledCell: isEnabled, scheduler: BindingScheduler.Immediate);

    [Test]
    public async Task IsExecutableByDefault()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        await Assert.That(a.CanExecute(null)).IsTrue().Because("no enablement cell means always enabled");
    }

    [Test]
    public async Task FollowsItsEnablementCell()
    {
        CellSink<bool> enabled = Cell.CreateSink(false);

        using IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: enabled);

        await Assert.That(a.CanExecute(null)).IsFalse().Because("the constructor samples the cell");

        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        enabled.Send(true);

        await Assert.That(a.CanExecute(null)).IsTrue();
        await Assert.That(notifications).IsEqualTo(1);
    }

    [Test]
    public async Task CarriesItsParameterIntoTheStream()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(42);

            await Assert.That(fired).IsEquivalentTo(expected: [42], ordering: CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task DoesNotFireWhileDisabled()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink: sink, isEnabled: Cell.Constant(false));

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(1);

            await Assert.That(fired).IsEmpty();
        }
    }

    // CanExecute reads a copy of the cell, and the scheduler moves each change of the cell to that
    // copy. Here the graph disables the command before the binding thread gets the change. Thus,
    // CanExecute gives true, and only the send can find that the cell is false.
    [Test]
    public async Task DoesNotFireWhenTheGraphDisabledItBeforeTheViewKnew()
    {
        QueueingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = sink.ToBindableActionImpl(isEnabledCell: enabled, scheduler: scheduler);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            enabled.Send(false);

            await Assert.That(a.CanExecute(null)).IsTrue().Because("the change of the copy is still in the queue");

            a.Execute(1);

            await Assert.That(fired).IsEmpty().Because("the cell was false in the transaction of the send");

            scheduler.RunAll();

            await Assert.That(a.CanExecute(null)).IsFalse().Because("the copy now agrees with the cell");
        }
    }

    // A call in an open transaction sends after that transaction closes. A change of the cell in
    // that transaction occurs first. Thus, the send must read the cell when it runs, and not when
    // the call occurs.
    [Test]
    public async Task DoesNotFireWhenTheTransactionOfTheCallDisablesIt()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink: sink, isEnabled: enabled);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            Transaction.RunVoid(() =>
            {
                // ReSharper disable once AccessToDisposedClosure - Transaction.RunVoid runs the lambda before the
                // using disposes a.
                a.Execute(1);
                enabled.Send(false);
            });

            await Assert.That(fired).IsEmpty().Because("the cell was false when the send ran");
        }
    }

    // The control for the two tests above: the send reads the cell, and a true cell lets the send occur.
    [Test]
    public async Task FiresWhenTheCellIsTrueAtTheSend()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a = Action(sink: sink, isEnabled: enabled);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            Transaction.RunVoid(() =>
            {
                // ReSharper disable once AccessToDisposedClosure - Transaction.RunVoid runs the lambda before the
                // using disposes a.
                a.Execute(1);
                enabled.Send(false);
            });

            enabled.Send(true);

            // ReSharper disable once AccessToDisposedClosure - Transaction.RunVoid runs the lambda before the
            // using disposes a.
            Transaction.RunVoid(() => a.Execute(2));

            await Assert.That(fired)
                .IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching)
                .Because("only the second call found the cell true");
        }
    }

    // The test of the type is a diagnostic for the author of the XAML, thus it must occur at the
    // call site. A test in the posted send throws at a point that the author cannot see.
    [Test]
    public async Task RejectsAMistypedParameterAtTheCallSite()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        // ReSharper disable once AccessToDisposedClosure - the assertion runs the lambda before
        // the using disposes a.
        await Assert.That(() => a.Execute("not an int")).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RejectsNullForAReferenceType()
    {
        using IBindableAction<string> a = Action(Stream.CreateSink<string>());

        // ReSharper disable once AccessToDisposedClosure - the assertion runs the lambda before
        // the using disposes a.
        await Assert.That(() => a.Execute(null)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RejectsNullForAValueType()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        // ReSharper disable once AccessToDisposedClosure - the assertion runs the lambda before
        // the using disposes a.
        await Assert.That(() => a.Execute(null)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task StopsFiringOnceDisposed()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Dispose();
            a.Execute(1);

            await Assert.That(fired).IsEmpty();
            await Assert.That(a.CanExecute(null)).IsFalse();
        }
    }

    // A binding engine caches the last answer from CanExecute and asks again only when the
    // command tells it to. Thus, a call to Dispose with no notification leaves a button available,
    // and a click on that button does nothing.
    [Test]
    public async Task NotifiesTheViewWhenDisposalDisablesIt()
    {
        IBindableAction<int> a = Action(Stream.CreateSink<int>());
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        a.Dispose();

        await Assert.That(notifications).IsEqualTo(1).Because("the view has to be told to re-query");
    }

    [Test]
    public async Task DoesNotNotifyWhenDisposingAnAlreadyDisabledCommand()
    {
        IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: Cell.Constant(false));
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;

        a.Dispose();

        await Assert.That(notifications).IsEqualTo(0).Because("nothing changed, so there is nothing to report");
    }

    [Test]
    public async Task DisposesIdempotently()
    {
        IBindableAction<int> a = Action(Stream.CreateSink<int>());

        a.Dispose();
        int notifications = 0;
        a.CanExecuteChanged += (_, _) => notifications++;
        a.Dispose();

        await Assert.That(notifications).IsEqualTo(0).Because("the second dispose does nothing at all");
    }
}
