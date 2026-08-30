using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using NUnit.Framework;

namespace SodaFlow.Bindable.ObjectModel.Tests
{
    /// <summary>
    ///     Covers the three bindable values. Everything here runs against
    ///     <see cref="BindingScheduler.Immediate" />, which is what makes the notifications observable
    ///     without a dispatcher; the ordering it produces is the same one a dispatcher-backed scheduler
    ///     produces, because it defers to the end of the current transaction exactly as that one does.
    /// </summary>
    [TestFixture]
    public class BindableValueTests
    {
        private static IOneWayBindableValue<T> OneWay<T>(Cell<T> cell) =>
            cell.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

        private static ITwoWayBindableValue<T> TwoWay<T>(CellSink<T> sink) =>
            sink.ToTwoWayImpl(scheduler: BindingScheduler.Immediate);

        private static List<string> RecordNotifications(INotifyPropertyChanged source)
        {
            List<string> names = new List<string>();
            source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
            return names;
        }

        [Test]
        public void OneWayStartsAtTheCellsCurrentValue()
        {
            CellSink<int> c = Cell.CreateSink(7);

            using (IOneWayBindableValue<int> b = OneWay<int>(c))
            {
                Assert.AreEqual(7, b.Value, "the constructor samples rather than waiting for an update");
            }
        }

        [Test]
        public void OneWayFollowsTheCellAndNotifiesOnce()
        {
            CellSink<int> c = Cell.CreateSink(0);

            using (IOneWayBindableValue<int> b = OneWay<int>(c))
            {
                List<string> names = RecordNotifications(b);

                c.Send(1);
                c.Send(2);

                Assert.AreEqual(2, b.Value);
                CollectionAssert.AreEqual(new[] { "Value", "Value" }, names);
            }
        }

        // The property name is load-bearing: the documented binding path is {Binding Foo.Value}, so a
        // notification naming anything else silently fails to update the view.
        [Test]
        public void OneWayRaisesForTheValueProperty()
        {
            CellSink<string> c = Cell.CreateSink("a");

            using (IOneWayBindableValue<string> b = OneWay<string>(c))
            {
                List<string> names = RecordNotifications(b);

                c.Send("b");

                CollectionAssert.AreEqual(new[] { "Value" }, names);
            }
        }

        [Test]
        public void OneWayDoesNotNotifyWhenTheValueIsUnchanged()
        {
            CellSink<int> c = Cell.CreateSink(3);

            using (IOneWayBindableValue<int> b = OneWay<int>(c))
            {
                List<string> names = RecordNotifications(b);

                c.Send(3);

                CollectionAssert.IsEmpty(names, "an update carrying the same value is not a change");
            }
        }

        [Test]
        public void OneWayStopsFollowingOnceDisposed()
        {
            CellSink<int> c = Cell.CreateSink(0);
            IOneWayBindableValue<int> b = OneWay<int>(c);

            b.Dispose();
            c.Send(9);

            Assert.AreEqual(0, b.Value, "a disposed bindable is detached from its cell");
        }

        [Test]
        public void TwoWayPushesWritesIntoTheGraph()
        {
            CellSink<int> c = Cell.CreateSink(0);

            using (ITwoWayBindableValue<int> b = TwoWay(c))
            {
                b.Value = 5;

                Assert.AreEqual(5, c.Sample(), "the write reached the sink");
                Assert.AreEqual(5, b.Value);
            }
        }

        [Test]
        public void TwoWayFollowsTheCellWhenTheGraphIsTheWriter()
        {
            CellSink<int> c = Cell.CreateSink(0);

            using (ITwoWayBindableValue<int> b = TwoWay(c))
            {
                List<string> names = RecordNotifications(b);

                c.Send(4);

                Assert.AreEqual(4, b.Value);
                CollectionAssert.AreEqual(new[] { "Value" }, names);
            }
        }

        // The graph is authoritative. A write the graph normalizes has to come back corrected, or the
        // view keeps showing something that was never accepted.
        [Test]
        public void TwoWayReconcilesAWriteTheGraphNormalizes()
        {
            StreamSink<string> edits = Stream.CreateSink<string>();
            Cell<string> upperCased = edits.Map(v => v.ToUpperInvariant()).Hold("");

            using (ITwoWayBindableValue<string> b =
                   upperCased.ToTwoWayImpl(edits, scheduler: BindingScheduler.Immediate))
            {
                b.Value = "abc";

                Assert.AreEqual("ABC", b.Value, "the cell's value wins over the optimistic one");
            }
        }

        [Test]
        public void TwoWayThrowsOnceDisposed()
        {
            CellSink<int> c = Cell.CreateSink(0);
            ITwoWayBindableValue<int> b = TwoWay(c);

            b.Dispose();

            Assert.Throws<ObjectDisposedException>(() => b.Value = 1);
        }

        [Test]
        public void OneWayToSourcePushesWritesIntoTheGraph()
        {
            CellSink<int> c = Cell.CreateSink(0);

            using (IOneWayToSourceBindableValue<int> b = c.ToOneWayToSourceImpl())
            {
                Assert.AreEqual(0, b.Value, "the getter starts at the sink's value");

                b.Value = 6;

                Assert.AreEqual(6, c.Sample());
                Assert.AreEqual(6, b.Value, "the getter reads back what the view wrote");
            }
        }

        [Test]
        public void OneWayToSourceStopsWritingOnceDisposed()
        {
            CellSink<int> c = Cell.CreateSink(0);
            IOneWayToSourceBindableValue<int> b = c.ToOneWayToSourceImpl();

            b.Dispose();
            b.Value = 3;

            Assert.AreEqual(0, c.Sample(), "a disposed sink accepts no further writes");
        }

        // A view model builds its bindables wherever it happens to be running and has no business
        // knowing which thread the binding engine uses. These construct off the current thread, with
        // no SynchronizationContext to capture, and check the sampled value survives the handover.
        //
        // A visibility bug would not fail this reliably - that is the nature of one - but the value
        // being boxed behind a volatile reference is what makes the handover sound, and a change that
        // reintroduced a same-thread requirement would fail here immediately.
        private static TResult OnAnotherThread<TResult>(Func<TResult> f)
        {
            TResult result = default!;
            Exception? failure = null;

            Thread thread = new Thread(
                () =>
                {
                    try
                    {
                        Assert.IsNull(
                            SynchronizationContext.Current,
                            "the point is a thread with no context of its own");

                        result = f();
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                });

            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "construction should not block");

            if (failure != null)
            {
                throw new AssertionException("construction threw on the other thread", failure);
            }

            return result;
        }

        [Test]
        public void OneWayCanBeConstructedOffTheBindingThread()
        {
            CellSink<int> c = Cell.CreateSink(11);

            using (IOneWayBindableValue<int> b = OnAnotherThread(() => OneWay<int>(c)))
            {
                Assert.AreEqual(11, b.Value, "the sample survived the handover");

                c.Send(12);

                Assert.AreEqual(12, b.Value, "and it keeps following afterwards");
            }
        }

        [Test]
        public void TwoWayCanBeConstructedOffTheBindingThread()
        {
            CellSink<int> c = Cell.CreateSink(11);

            using (ITwoWayBindableValue<int> b = OnAnotherThread(() => TwoWay(c)))
            {
                Assert.AreEqual(11, b.Value);

                b.Value = 13;

                Assert.AreEqual(13, c.Sample());
            }
        }

        [Test]
        public void OneWayToSourceCanBeConstructedOffTheBindingThread()
        {
            CellSink<int> c = Cell.CreateSink(11);

            using (IOneWayToSourceBindableValue<int> b = OnAnotherThread(() => c.ToOneWayToSourceImpl()))
            {
                Assert.AreEqual(11, b.Value);

                b.Value = 13;

                Assert.AreEqual(13, c.Sample());
            }
        }

        [Test]
        public void ACommandCanBeConstructedOffTheBindingThread()
        {
            CellSink<bool> enabled = Cell.CreateSink(true);

            using (IBindableAction<int> a = OnAnotherThread(
                       () => Stream.CreateSink<int>()
                           .ToBindableActionImpl(
                               isEnabledCell: enabled,
                               scheduler: BindingScheduler.Immediate)))
            {
                Assert.IsTrue(a.CanExecute(null), "the sampled enablement survived the handover");

                enabled.Send(false);

                Assert.IsFalse(a.CanExecute(null));
            }
        }

        // Every bindable is disposable through the one marker interface, which is what lets a view
        // model keep them in a single collection and tear them all down together. The write-only one
        // used to be left out of it.
        [Test]
        public void EveryBindableIsAnIBindable()
        {
            CellSink<int> c = Cell.CreateSink(0);
            StreamSink<int> edits = Stream.CreateSink<int>();

            List<IBindable> all = new List<IBindable>
            {
                OneWay<int>(c),
                TwoWay(c),
                c.ToOneWayToSourceImpl(),
                edits.ToBindableActionImpl(scheduler: BindingScheduler.Immediate)
            };

            foreach (IBindable bindable in all)
            {
                bindable.Dispose();
            }

            Assert.AreEqual(4, all.Count);
        }
    }
}
