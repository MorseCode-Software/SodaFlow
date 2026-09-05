using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Covers the one thing the factory exists to do that the extension methods do not: carry a
///     single injected scheduler into everything it creates. A method that forgets to pass it still
///     produces a working bindable, so nothing but a test of this shape catches the omission.
/// </summary>
public class BindableFactoryTests
{
    /// <summary>
    ///     Records that it was asked, then behaves like the immediate scheduler so the bindable
    ///     under test still works.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class RecordingScheduler : IBindingScheduler
    {
        public int Posts { get; private set; }

        /// <inheritdoc />
        public bool CheckAccess() => true;

        public void Post(Action action)
        {
            this.Posts++;
            BindingScheduler.Immediate.Post(action);
        }
    }

    [Test]
    public async Task OneWayUsesTheInjectedScheduler()
    {
        RecordingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using IOneWayBindableValue<int> b = new BindableFactory(scheduler).CreateOneWay(c);

        c.Send(1);

        await Assert.That(b.Value).IsEqualTo(1);
        await Assert.That(scheduler.Posts).IsEqualTo(1);
    }

    [Test]
    public async Task TwoWayUsesTheInjectedScheduler()
    {
        RecordingScheduler scheduler = new();
        CellSink<int> c = Cell.CreateSink(0);

        using ITwoWayBindableValue<int> b = new BindableFactory(scheduler).CreateTwoWay(c);

        c.Send(1);

        await Assert.That(b.Value).IsEqualTo(1);
        await Assert.That(scheduler.Posts).IsGreaterThanOrEqualTo(1);
    }

    // The two command overloads were the ones that dropped it, and a command built without the
    // injected scheduler still fires - only its CanExecuteChanged goes to the wrong place.
    [Test]
    public async Task BindableActionUsesTheInjectedScheduler()
    {
        RecordingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        using IBindableAction<int> a =
            new BindableFactory(scheduler).CreateBindableAction(
                firingsStreamSink: Stream.CreateSink<int>(),
                isEnabledCell: enabled);

        enabled.Send(true);

        await Assert.That(a.CanExecute(null)).IsTrue();

        await Assert.That(scheduler.Posts).IsEqualTo(1).Because("the enablement change went through the injected scheduler");
    }

    [Test]
    public async Task ParameterlessBindableActionUsesTheInjectedScheduler()
    {
        RecordingScheduler scheduler = new();
        CellSink<bool> enabled = Cell.CreateSink(false);

        using IBindableAction a =
            new BindableFactory(scheduler).CreateBindableAction(
                firingsStreamSink: Stream.CreateSink<Unit>(),
                isEnabledCell: enabled);

        enabled.Send(true);

        await Assert.That(a.CanExecute(null)).IsTrue();

        await Assert.That(scheduler.Posts).IsEqualTo(1).Because("the enablement change went through the injected scheduler");
    }

    [Test]
    public async Task ParameterlessBindableActionIgnoresItsParameter()
    {
        StreamSink<Unit> sink = Stream.CreateSink<Unit>();
        List<Unit> fired = new();

        using IBindableAction a =
            new BindableFactory(BindingScheduler.Immediate).CreateBindableAction(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            // Whatever the XAML author bound CommandParameter to, a parameterless command has no
            // use for it and must not reject it.
            a.Execute("anything at all");

            await Assert.That(fired.Count).IsEqualTo(1);
        }
    }
}
