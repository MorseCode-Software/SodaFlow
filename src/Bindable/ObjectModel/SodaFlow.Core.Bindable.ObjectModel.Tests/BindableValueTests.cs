using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Covers the three bindable values. Everything here runs against
///     <see cref="BindingScheduler.Immediate" />, which is what makes the notifications observable
///     without a dispatcher; the ordering it produces is the same one a dispatcher-backed scheduler
///     produces, because it defers to the end of the current transaction exactly as that one does.
/// </summary>
public class BindableValueTests
{
    private static IOneWayBindableValue<T> OneWay<T>(Cell<T> cell) =>
        cell.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

    private static ITwoWayBindableValue<T> TwoWay<T>(CellSink<T> sink) =>
        sink.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

    private static List<string?> RecordNotifications(INotifyPropertyChanged source)
    {
        List<string?> names = new();
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
        await Assert.That(names).IsEquivalentTo(new[] { "Value", "Value" }, CollectionOrdering.Matching);
    }

    // The property name is load-bearing: the documented binding path is {Binding Foo.Value}, so a
    // notification naming anything else silently fails to update the view.
    [Test]
    public async Task OneWayRaisesForTheValueProperty()
    {
        CellSink<string> c = Cell.CreateSink("a");

        using IOneWayBindableValue<string> b = OneWay(c);

        List<string?> names = RecordNotifications(b);

        c.Send("b");

        await Assert.That(names).IsEquivalentTo(new[] { "Value" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task OneWayDoesNotNotifyWhenTheValueIsUnchanged()
    {
        CellSink<int> c = Cell.CreateSink(3);

        using IOneWayBindableValue<int> b = OneWay(c);

        List<string?> names = RecordNotifications(b);

        c.Send(3);

        await Assert.That(collection: names).IsEmpty().Because("an update carrying the same value is not a change");
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
        await Assert.That(names).IsEquivalentTo(new[] { "Value" }, CollectionOrdering.Matching);
    }

    // The graph is authoritative. A write the graph normalizes has to come back corrected, or the
    // view keeps showing something that was never accepted.
    [Test]
    public async Task TwoWayReconcilesAWriteTheGraphNormalizes()
    {
        StreamSink<string> edits = Stream.CreateSink<string>();
        Cell<string> upperCased = edits.Map(static v => v.ToUpperInvariant()).Hold(string.Empty);

        using ITwoWayBindableValue<string> b =
            upperCased.ToTwoWayImpl(editsStreamSink: edits, scheduler: BindingScheduler.Immediate);

        b.Value = "abc";

        await Assert.That(b.Value).IsEqualTo("ABC").Because("the cell's value wins over the optimistic one");
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

        using IOneWayToSourceBindableValue<int> b = c.ToOneWayToSourceImpl();

        await Assert.That(b.Value).IsEqualTo(0).Because("the getter starts at the sink's value");

        b.Value = 6;

        await Assert.That(c.Sample()).IsEqualTo(6);
        await Assert.That(b.Value).IsEqualTo(6).Because("the getter reads back what the view wrote");
    }

    [Test]
    public async Task OneWayToSourceStopsWritingOnceDisposed()
    {
        CellSink<int> c = Cell.CreateSink(0);
        IOneWayToSourceBindableValue<int> b = c.ToOneWayToSourceImpl();

        b.Dispose();
        b.Value = 3;

        await Assert.That(c.Sample()).IsEqualTo(0).Because("a disposed sink accepts no further writes");
    }

    // A view model builds its bindable objects wherever it happens to be running and has no business
    // knowing which thread the binding engine uses. These construct off the current thread, with
    // no SynchronizationContext to capture, and check the sampled value survives the handover.
    //
    // A visibility bug would not fail this reliably - that is the nature of one - but the value
    // being boxed behind a volatile reference is what makes the handover sound, and a change that
    // reintroduced a same-thread requirement would fail here immediately.
    private static TResult OnAnotherThread<TResult>(Func<TResult> f)
    {
        TResult? result = default;
        Exception? failure = null;

        Thread thread =
            new(() =>
            {
                try
                {
                    await Assert.That(anObject: SynchronizationContext.Current).IsNull().Because("the point is a thread with no context of its own");

                    result = f();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });

        thread.Start();
        await Assert.That(condition: thread.Join(TimeSpan.FromSeconds(10))).IsTrue().Because("construction should not block");

        return failure != null
            ? throw new AssertionException(message: "construction threw on the other thread", inner: failure)
            // ReSharper disable once NullableWarningSuppressionIsUsed - This will be non-null if failure is null.
            : result!;
    }

    [Test]
    public async Task OneWayCanBeConstructedOffTheBindingThread()
    {
        CellSink<int> c = Cell.CreateSink(11);

        using IOneWayBindableValue<int> b = OnAnotherThread(() => OneWay(c));

        await Assert.That(b.Value).IsEqualTo(11).Because("the sample survived the handover");

        c.Send(12);

        await Assert.That(b.Value).IsEqualTo(12).Because("and it keeps following afterward");
    }

    [Test]
    public async Task TwoWayCanBeConstructedOffTheBindingThread()
    {
        CellSink<int> c = Cell.CreateSink(11);

        using ITwoWayBindableValue<int> b = OnAnotherThread(() => TwoWay(c));

        await Assert.That(b.Value).IsEqualTo(11);

        b.Value = 13;

        await Assert.That(c.Sample()).IsEqualTo(13);
    }

    [Test]
    public async Task OneWayToSourceCanBeConstructedOffTheBindingThread()
    {
        CellSink<int> c = Cell.CreateSink(11);

        using IOneWayToSourceBindableValue<int> b = OnAnotherThread(() => c.ToOneWayToSourceImpl());

        await Assert.That(b.Value).IsEqualTo(11);

        b.Value = 13;

        await Assert.That(c.Sample()).IsEqualTo(13);
    }

    [Test]
    public async Task ACommandCanBeConstructedOffTheBindingThread()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);

        using IBindableAction<int> a =
            OnAnotherThread(() =>
                Stream.CreateSink<int>()
                    .ToBindableActionImpl(
                        isEnabledCell: enabled,
                        scheduler: BindingScheduler.Immediate));

        await Assert.That(condition: a.CanExecute(null)).IsTrue().Because("the sampled enablement survived the handover");

        enabled.Send(false);

        await Assert.That(a.CanExecute(null)).IsFalse();
    }

    // Every bindable is disposable through the one marker interface, which is what lets a view
    // model keep them in a single collection and tear them all down together. The write-only one
    // used to be left out of it.
    [Test]
    public async Task EveryBindableIsAnIBindable()
    {
        CellSink<int> c = Cell.CreateSink(0);
        StreamSink<int> edits = Stream.CreateSink<int>();

        List<IBindable> all =
            new()
            {
                OneWay(c),
                TwoWay(c),
                c.ToOneWayToSourceImpl(),
                edits.ToBindableActionImpl(scheduler: BindingScheduler.Immediate)
            };

        foreach (IBindable bindable in all)
        {
            bindable.Dispose();
        }

        await Assert.That(all.Count).IsEqualTo(4);
    }
}
