using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Exceptions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests the three bindable values. Each test runs with
///     <see cref="BindingScheduler.Immediate" />, which shows a notification with no
///     dispatcher. It gives the same sequence as a scheduler on a dispatcher, because it defers to
///     the end of the current transaction as that scheduler does.
/// </summary>
public sealed class BindableValueTests
{
    private static IOneWayBindableValue<T> OneWay<T>(Cell<T> cell) =>
        cell.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

    private static ITwoWayBindableValue<T> TwoWay<T>(CellSink<T> sink) =>
        sink.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

    private static List<string?> RecordNotifications(INotifyPropertyChanged source)
    {
        List<string?> names = [];
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }

    [Test]
    public async Task OneWayStartsAtTheCellsCurrentValue()
    {
        CellSink<int> c = Cell.CreateSink(7);

        using IOneWayBindableValue<int> b = OneWay(c);

        await Assert.That(b.Value).IsEqualTo(7).Because("the constructor samples rather than waiting for an update");
    }

    [Test]
    public async Task OneWayFollowsTheCellAndNotifiesOnce()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = OneWay(c);

        List<string?> names = RecordNotifications(b);

        c.Send(1);
        c.Send(2);

        await Assert.That(b.Value).IsEqualTo(2);
        string?[] expected = ["Value", "Value"];

        await Assert.That(names).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
    }

    // The name of the property is necessary. The documented binding path is
    // {Binding Foo.Value}. Thus, a notification with a different name does not update the view,
    // and it gives no warning.
    [Test]
    public async Task OneWayRaisesForTheValueProperty()
    {
        CellSink<string> c = Cell.CreateSink("a");

        using IOneWayBindableValue<string> b = OneWay(c);

        List<string?> names = RecordNotifications(b);

        c.Send("b");

        string?[] expected = ["Value"];

        await Assert.That(names).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
    }

    [Test]
    public async Task OneWayDoesNotNotifyWhenTheValueIsUnchanged()
    {
        CellSink<int> c = Cell.CreateSink(3);

        using IOneWayBindableValue<int> b = OneWay(c);

        List<string?> names = RecordNotifications(b);

        c.Send(3);

        await Assert.That(names).IsEmpty().Because("an update carrying the same value is not a change");
    }

    [Test]
    public async Task OneWayStopsFollowingOnceDisposed()
    {
        CellSink<int> c = Cell.CreateSink(0);
        IOneWayBindableValue<int> b = OneWay(c);

        b.Dispose();
        c.Send(9);

        await Assert.That(b.Value).IsEqualTo(0).Because("a disposed bindable is detached from its cell");
    }

    [Test]
    public async Task TwoWayPushesWritesIntoTheGraph()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = TwoWay(c);

        b.Value = 5;

        await Assert.That(c.Sample()).IsEqualTo(5).Because("the write reached the sink");
        await Assert.That(b.Value).IsEqualTo(5);
    }

    [Test]
    public async Task TwoWayFollowsTheCellWhenTheGraphIsTheWriter()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = TwoWay(c);

        List<string?> names = RecordNotifications(b);

        c.Send(4);

        await Assert.That(b.Value).IsEqualTo(4);
        string?[] expected = ["Value"];

        await Assert.That(names).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
    }

    // More than one control usually binds to a two-way value. A checkbox writes it, and the same
    // answer makes other controls available or visible. Only the control that wrote knows the
    // value. Thus, a write that the graph accepts with no change is a change to each other
    // binding, and this class must announce it or those bindings do not change.
    [Test]
    public async Task TwoWayNotifiesWhenTheViewIsTheWriter()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = TwoWay(c);

        List<string?> names = RecordNotifications(b);

        b.Value = 5;

        string?[] expected = ["Value"];

        await Assert.That(names)
            .IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching)
            .Because("a second binding to this property has no other way to learn of the write");

        await Assert.That(b.Value).IsEqualTo(5);
    }

    // The opposite test. A notification for a write that changed nothing makes each binding read
    // the value again for no cause. The equality test prevents that.
    [Test]
    public async Task TwoWayDoesNotNotifyForAWriteThatChangesNothing()
    {
        CellSink<int> c = Cell.CreateSink(5);

        using ITwoWayBindableValue<int> b = TwoWay(c);

        List<string?> names = RecordNotifications(b);

        b.Value = 5;

        await Assert.That(names).IsEmpty().Because("nothing changed, so there is nothing to announce");
    }

    // A second write of the same value announces nothing. Thus, a control that writes at each
    // keystroke does not make the other controls read the value at each keystroke.
    [Test]
    public async Task TwoWayNotifiesOncePerActualChange()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = TwoWay(c);

        List<string?> names = RecordNotifications(b);

        b.Value = 1;
        b.Value = 1;
        b.Value = 2;
        b.Value = 2;

        string?[] expected = ["Value", "Value"];

        await Assert.That(names).IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching);
    }

    // A value that the comparer calls equal is equal to each part of this class. A value of the
    // cell that differs only in a part that the comparer ignores must not replace the value that
    // the view shows. There is no notification for such a value. Thus, the replacement stays with
    // no correction, and the property reports a value that the view never showed.
    [Test]
    public async Task TwoWayKeepsItsValueWhenTheComparerCallsTheCellsEquivalent()
    {
        CellSink<string> c = Cell.CreateSink("abc");

        using ITwoWayBindableValue<string> b =
            c.ToTwoWayImpl(scheduler: BindingScheduler.Immediate, comparer: StringComparer.OrdinalIgnoreCase);

        List<string?> names = RecordNotifications(b);

        c.Send("ABC");

        await Assert.That(names).IsEmpty().Because("the comparer says nothing changed");

        await Assert.That(b.Value)
            .IsEqualTo("abc")
            .Because("an unannounced change would leave the property disagreeing with the view");
    }

    // The graph is the authority. A write that the graph changes must come back corrected, or
    // the view continues to show a value that the graph did not accept.
    [Test]
    public async Task TwoWayReconcilesAWriteTheGraphNormalizes()
    {
        StreamSink<string> edits = Stream.CreateSink<string>();
        Cell<string> upperCased = edits.Map(static v => v.ToUpperInvariant()).Hold(string.Empty);

        using ITwoWayBindableValue<string> b =
            upperCased.ToTwoWayImpl(editsStreamSink: edits, scheduler: BindingScheduler.Immediate);

        List<string?> names = RecordNotifications(b);

        b.Value = "abc";

        await Assert.That(b.Value).IsEqualTo("ABC").Because("the cell's value wins over the optimistic one");

        string?[] expected = ["Value"];

        await Assert.That(names)
            .IsEquivalentTo(expected: expected, ordering: CollectionOrdering.Matching)
            .Because("announced once, carrying what the graph settled on rather than what was written");
    }

    [Test]
    public async Task TwoWayThrowsOnceDisposed()
    {
        CellSink<int> c = Cell.CreateSink(0);
        ITwoWayBindableValue<int> b = TwoWay(c);

        b.Dispose();

        await Assert.That(() => b.Value = 1).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task OneWayToSourcePushesWritesIntoTheGraph()
    {
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayToSourceBindableValue<int> b = c.ToOneWayToSourceImpl(scheduler: BindingScheduler.Immediate);

        await Assert.That(b.Value).IsEqualTo(0).Because("the getter starts at the sink's value");

        b.Value = 6;

        await Assert.That(c.Sample()).IsEqualTo(6);
        await Assert.That(b.Value).IsEqualTo(6).Because("the getter reads back what the view wrote");
    }

    [Test]
    public async Task OneWayToSourceStopsWritingOnceDisposed()
    {
        CellSink<int> c = Cell.CreateSink(0);
        IOneWayToSourceBindableValue<int> b = c.ToOneWayToSourceImpl(scheduler: BindingScheduler.Immediate);

        b.Dispose();
        b.Value = 3;

        await Assert.That(c.Sample()).IsEqualTo(0).Because("a disposed sink accepts no further writes");
    }

    // A view model builds its bindable objects on the thread where it runs, and it does not have
    // to know which thread the binding engine uses. These tests build on a different thread,
    // with no SynchronizationContext, and then test that the sampled value is
    // correct.
    //
    // A defect in memory visibility does not always fail this test, because such a defect is
    // not repeatable. But a box behind a volatile reference holds the value, and that makes the
    // move between threads correct. A change that needs one thread again fails here
    // immediately.
    private static async Task<TResult> OnAnotherThread<TResult>(Func<TResult> f)
    {
        TResult? result = default;
        Exception? failure = null;
        SynchronizationContext? contextOnTheOtherThread = null;

        Thread thread =
            new(() =>
            {
                try
                {
                    // This code keeps the result and does not assert it here, because no code in
                    // a thread body can await.
                    contextOnTheOtherThread = SynchronizationContext.Current;

                    result = f();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });

        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(10));

        await Assert.That(finished).IsTrue().Because("construction should not block");

        await Assert.That(contextOnTheOtherThread)
            .IsNull()
            .Because("the point is a thread with no context of its own");

        return failure != null
            ? throw new AssertionException(
                message: "construction threw on the other thread",
                innerException: failure)
            // ReSharper disable once NullableWarningSuppressionIsUsed - This will be non-null if failure is null.
            : result!;
    }

    [Test]
    public async Task OneWayCanBeConstructedOffTheBindingThread()
    {
        CellSink<int> c = Cell.CreateSink(11);

        using IOneWayBindableValue<int> b = await OnAnotherThread(() => OneWay(c));

        await Assert.That(b.Value).IsEqualTo(11).Because("the sample survived the handover");

        c.Send(12);

        await Assert.That(b.Value).IsEqualTo(12).Because("and it keeps following afterward");
    }

    [Test]
    public async Task TwoWayCanBeConstructedOffTheBindingThread()
    {
        CellSink<int> c = Cell.CreateSink(11);

        using ITwoWayBindableValue<int> b = await OnAnotherThread(() => TwoWay(c));

        await Assert.That(b.Value).IsEqualTo(11);

        b.Value = 13;

        await Assert.That(c.Sample()).IsEqualTo(13);
    }

    [Test]
    public async Task OneWayToSourceCanBeConstructedOffTheBindingThread()
    {
        CellSink<int> c = Cell.CreateSink(11);

        using IOneWayToSourceBindableValue<int> b = await OnAnotherThread(() => c.ToOneWayToSourceImpl(scheduler: BindingScheduler.Immediate));

        await Assert.That(b.Value).IsEqualTo(11);

        b.Value = 13;

        await Assert.That(c.Sample()).IsEqualTo(13);
    }

    [Test]
    public async Task ACommandCanBeConstructedOffTheBindingThread()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);

        using IBindableAction<int> a =
            await OnAnotherThread(() =>
                Stream.CreateSink<int>()
                    .ToBindableActionImpl(
                        isEnabledCell: enabled,
                        scheduler: BindingScheduler.Immediate));

        await Assert.That(a.CanExecute(null)).IsTrue().Because("the sampled enablement survived the handover");

        enabled.Send(false);

        await Assert.That(a.CanExecute(null)).IsFalse();
    }

    // Each bindable has the one interface that makes it disposable. Thus, a view model can
    // keep all of them in one collection and dispose all of them together. Before, the value that
    // a caller can only write did not implement that interface.
    [Test]
    public async Task EveryBindableIsAnIBindable()
    {
        CellSink<int> c = Cell.CreateSink(0);
        StreamSink<int> edits = Stream.CreateSink<int>();

        List<IBindable> all =
        [
            OneWay(c),
            TwoWay(c),
            c.ToOneWayToSourceImpl(scheduler: BindingScheduler.Immediate),
            edits.ToBindableActionImpl(scheduler: BindingScheduler.Immediate)
        ];

        foreach (IBindable bindable in all)
        {
            bindable.Dispose();
        }

        await Assert.That(all.Count).IsEqualTo(4);
    }

    // No bindable selects a scheduler for itself. Without one, a bindable built on a thread with
    // no SynchronizationContext raised its notifications on whichever thread sent the value, and
    // the binding engine failed later and far from the cause. Each construction now fails at once.
    [Test]
    public async Task EachBindableRejectsANullScheduler()
    {
        CellSink<int> c = Cell.CreateSink(0);
        StreamSink<int> edits = Stream.CreateSink<int>();

        // ReSharper disable NullableWarningSuppressionIsUsed - Testing for exception on null.
        await Assert.That(() => c.ToOneWayImpl(scheduler: null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => c.ToTwoWayImpl(scheduler: null!)).ThrowsExactly<ArgumentNullException>();

        await Assert.That(() => c.ToTwoWayImpl(editsStreamSink: edits, scheduler: null!))
            .ThrowsExactly<ArgumentNullException>();

        await Assert.That(() => c.ToOneWayToSourceImpl(scheduler: null!)).ThrowsExactly<ArgumentNullException>();

        await Assert.That(() => edits.ToOneWayToSourceImpl(initialValue: 0, scheduler: null!))
            .ThrowsExactly<ArgumentNullException>();

        await Assert.That(() => edits.ToBindableActionImpl(scheduler: null!)).ThrowsExactly<ArgumentNullException>();
        // ReSharper restore NullableWarningSuppressionIsUsed
    }
}
