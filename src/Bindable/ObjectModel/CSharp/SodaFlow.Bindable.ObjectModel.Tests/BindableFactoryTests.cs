using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests the one operation that the factory does and the extension methods do not: it moves
///     one supplied scheduler into each object that it creates. A method that does not pass the
///     scheduler makes a bindable that operates. Thus only a test of this shape finds that
///     error.
/// </summary>
public sealed class BindableFactoryTests
{
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

    // The two command overloads did not supply the scheduler. A command that SodaFlow builds
    // without the supplied scheduler continues to fire. Only its CanExecuteChanged goes to an incorrect
    // thread.
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

        await Assert.That(scheduler.Posts)
            .IsEqualTo(1)
            .Because("the enablement change went through the injected scheduler");
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

        await Assert.That(scheduler.Posts)
            .IsEqualTo(1)
            .Because("the enablement change went through the injected scheduler");
    }

    [Test]
    public async Task ParameterlessBindableActionIgnoresItsParameter()
    {
        StreamSink<Unit> sink = Stream.CreateSink<Unit>();
        List<Unit> fired = [];

        using IBindableAction a =
            new BindableFactory(BindingScheduler.Immediate).CreateBindableAction(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            // A command with no parameter does not use the CommandParameter that the author of
            // the XAML supplied, and it must accept that parameter.
            a.Execute("anything at all");

            await Assert.That(fired.Count).IsEqualTo(1);
        }
    }

    /// <summary>
    ///     Keeps a record that the code asked it, and then operates as the immediate scheduler.
    ///     Thus the bindable in the test continues to operate.
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
}
