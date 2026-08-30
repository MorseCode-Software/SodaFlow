using System;
using System.Collections.Generic;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel.Tests
{
    /// <summary>
    ///     Covers the one thing the factory exists to do that the extension methods do not: carry a
    ///     single injected scheduler into everything it creates. A method that forgets to pass it still
    ///     produces a working bindable, so nothing but a test of this shape catches the omission.
    /// </summary>
    [TestFixture]
    public class BindableFactoryTests
    {
        /// <summary>
        ///     Records that it was asked, then behaves like the immediate scheduler so the bindable
        ///     under test still works.
        /// </summary>
        private sealed class RecordingScheduler : IBindingScheduler
        {
            public int Posts { get; private set; }

            public void Post(Action action)
            {
                this.Posts++;
                BindingScheduler.Immediate.Post(action);
            }
        }

        [Test]
        public void OneWayUsesTheInjectedScheduler()
        {
            RecordingScheduler scheduler = new RecordingScheduler();
            CellSink<int> c = Cell.CreateSink(0);

            using (IOneWayBindableValue<int> b = new BindableFactory(scheduler).ToOneWay(c))
            {
                c.Send(1);

                Assert.AreEqual(1, b.Value);
                Assert.AreEqual(1, scheduler.Posts);
            }
        }

        [Test]
        public void TwoWayUsesTheInjectedScheduler()
        {
            RecordingScheduler scheduler = new RecordingScheduler();
            CellSink<int> c = Cell.CreateSink(0);

            using (ITwoWayBindableValue<int> b = new BindableFactory(scheduler).ToTwoWay(c))
            {
                c.Send(1);

                Assert.AreEqual(1, b.Value);
                Assert.GreaterOrEqual(scheduler.Posts, 1);
            }
        }

        // The two command overloads were the ones that dropped it, and a command built without the
        // injected scheduler still fires - only its CanExecuteChanged goes to the wrong place.
        [Test]
        public void BindableActionUsesTheInjectedScheduler()
        {
            RecordingScheduler scheduler = new RecordingScheduler();
            CellSink<bool> enabled = Cell.CreateSink(false);

            using (IBindableAction<int> a =
                   new BindableFactory(scheduler).ToBindableAction(Stream.CreateSink<int>(), enabled))
            {
                enabled.Send(true);

                Assert.IsTrue(a.CanExecute(null));
                Assert.AreEqual(1, scheduler.Posts, "the enablement change went through the injected scheduler");
            }
        }

        [Test]
        public void ParameterlessBindableActionUsesTheInjectedScheduler()
        {
            RecordingScheduler scheduler = new RecordingScheduler();
            CellSink<bool> enabled = Cell.CreateSink(false);

            using (IBindableAction a =
                   new BindableFactory(scheduler).ToBindableAction(Stream.CreateSink<Unit>(), enabled))
            {
                enabled.Send(true);

                Assert.IsTrue(a.CanExecute(null));
                Assert.AreEqual(1, scheduler.Posts, "the enablement change went through the injected scheduler");
            }
        }

        [Test]
        public void ParameterlessBindableActionIgnoresItsParameter()
        {
            StreamSink<Unit> sink = Stream.CreateSink<Unit>();
            List<Unit> fired = new List<Unit>();

            using (IBindableAction a =
                   new BindableFactory(BindingScheduler.Immediate).ToBindableAction(sink))
            using (a.FiringsStream.ListenStrong(fired.Add))
            {
                // Whatever the XAML author bound CommandParameter to, a parameterless command has no
                // use for it and must not reject it.
                a.Execute("anything at all");

                Assert.AreEqual(1, fired.Count);
            }
        }
    }
}
