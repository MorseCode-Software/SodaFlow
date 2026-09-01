using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>Covers the command: enablement, firing, parameter typing and disposal.</summary>
[TestFixture]
public class BindableActionTests
{
    private static IBindableAction<T> Action<T>(StreamSink<T> sink, Cell<bool>? isEnabled = null) =>
        sink.ToBindableActionImpl(isEnabledCell: isEnabled, scheduler: BindingScheduler.Immediate);

    [Test]
    public void IsExecutableByDefault()
    {
        using (IBindableAction<int> a = Action(Stream.CreateSink<int>()))
        {
            Assert.IsTrue(condition: a.CanExecute(null), message: "no enablement cell means always enabled");
        }
    }

    [Test]
    public void FollowsItsEnablementCell()
    {
        CellSink<bool> enabled = Cell.CreateSink(false);

        using (IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: enabled))
        {
            Assert.IsFalse(condition: a.CanExecute(null), message: "the constructor samples the cell");

            int notifications = 0;
            a.CanExecuteChanged += (_, __) => notifications++;

            enabled.Send(true);

            Assert.IsTrue(a.CanExecute(null));
            Assert.AreEqual(expected: 1, actual: notifications);
        }
    }

    [Test]
    public void CarriesItsParameterIntoTheStream()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = new();

        using (IBindableAction<int> a = Action(sink))
        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(42);

            CollectionAssert.AreEqual(expected: new[] { 42 }, actual: fired);
        }
    }

    [Test]
    public void DoesNotFireWhileDisabled()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = new();

        using (IBindableAction<int> a = Action(sink: sink, isEnabled: Cell.Constant(false)))
        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(1);

            CollectionAssert.IsEmpty(fired);
        }
    }

    // The type check is a diagnostic for whoever wrote the XAML, so it has to surface at the call
    // site. Deferring it into the posted send would have thrown somewhere they cannot see.
    [Test]
    public void RejectsAMistypedParameterAtTheCallSite()
    {
        using (IBindableAction<int> a = Action(Stream.CreateSink<int>()))
        {
            Assert.Throws<InvalidOperationException>(() => a.Execute("not an int"));
        }
    }

    [Test]
    public void AcceptsNullForATypeThatCanRepresentIt()
    {
        StreamSink<string> sink = Stream.CreateSink<string>();
        List<string> fired = new();

        using (IBindableAction<string> a = Action(sink))
        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(null);

            Assert.AreEqual(expected: 1, actual: fired.Count);
            Assert.IsNull(fired[0]);
        }
    }

    [Test]
    public void RejectsNullForATypeThatCannot()
    {
        using (IBindableAction<int> a = Action(Stream.CreateSink<int>()))
        {
            Assert.Throws<InvalidOperationException>(() => a.Execute(null));
        }
    }

    [Test]
    public void StopsFiringOnceDisposed()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = new();

        IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Dispose();
            a.Execute(1);

            CollectionAssert.IsEmpty(fired);
            Assert.IsFalse(a.CanExecute(null));
        }
    }

    // A binding engine caches the last CanExecute answer and only asks again when told to, so
    // disposing without notifying leaves a button enabled that does nothing when clicked.
    [Test]
    public void NotifiesTheViewWhenDisposalDisablesIt()
    {
        IBindableAction<int> a = Action(Stream.CreateSink<int>());
        int notifications = 0;
        a.CanExecuteChanged += (_, __) => notifications++;

        a.Dispose();

        Assert.AreEqual(expected: 1, actual: notifications, message: "the view has to be told to re-query");
    }

    [Test]
    public void DoesNotNotifyWhenDisposingAnAlreadyDisabledCommand()
    {
        IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: Cell.Constant(false));
        int notifications = 0;
        a.CanExecuteChanged += (_, __) => notifications++;

        a.Dispose();

        Assert.AreEqual(expected: 0, actual: notifications, message: "nothing changed, so there is nothing to report");
    }

    [Test]
    public void DisposesIdempotently()
    {
        IBindableAction<int> a = Action(Stream.CreateSink<int>());

        a.Dispose();
        int notifications = 0;
        a.CanExecuteChanged += (_, __) => notifications++;
        a.Dispose();

        Assert.AreEqual(expected: 0, actual: notifications, message: "the second dispose does nothing at all");
    }
}