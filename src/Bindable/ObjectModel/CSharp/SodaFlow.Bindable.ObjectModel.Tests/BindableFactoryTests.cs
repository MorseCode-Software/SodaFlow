using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     Tests the one operation that the factory does and the extension methods do
///     not: it moves one supplied scheduler into each object that it creates. A
///     method that does not pass the scheduler makes a bindable that operates.
///     Thus, only a test of this shape finds that error.
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

    // Each type of command from the factory gets Execute from the core command. That Execute
    // reads the enablement cell when it sends. The three tests below show that each type sends
    // nothing when the cell is false at the send. Each type sends its parameter when the cell is
    // true.
    [Test]
    public async Task ACommandWithAParameterDoesNotSendWhenItsCellIsFalseAtTheSend()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = [];

        using IBindableAction<int> a =
            new BindableFactory(BindingScheduler.Immediate).CreateBindableAction(
                firingsStreamSink: sink,
                isEnabledCell: enabled);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            ExecuteThenDisable(command: a, parameter: 1, enabled: enabled);
            EnableThenExecute(command: a, parameter: 2, enabled: enabled);

            await Assert.That(fired)
                .IsEquivalentTo(expected: [2], ordering: CollectionOrdering.Matching)
                .Because("only the second call found the cell true");
        }
    }

    [Test]
    public async Task ACommandWithNoParameterDoesNotSendWhenItsCellIsFalseAtTheSend()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<Unit> sink = Stream.CreateSink<Unit>();
        List<Unit> fired = [];

        using IBindableAction a =
            new BindableFactory(BindingScheduler.Immediate).CreateBindableAction(
                firingsStreamSink: sink,
                isEnabledCell: enabled);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            ExecuteThenDisable(command: a, parameter: null, enabled: enabled);
            EnableThenExecute(command: a, parameter: null, enabled: enabled);

            await Assert.That(fired.Count).IsEqualTo(1).Because("only the second call found the cell true");
        }
    }

    [Test]
    public async Task ACommandWithAMaybeParameterDoesNotSendWhenItsCellIsFalseAtTheSend()
    {
        CellSink<bool> enabled = Cell.CreateSink(true);
        StreamSink<Maybe<int>> sink = Stream.CreateSink<Maybe<int>>();
        List<Maybe<int>> fired = [];

        using IBindableAction<Maybe<int>> a =
            new BindableFactory(BindingScheduler.Immediate).CreateBindableAction(
                firingsStreamSink: sink,
                isEnabledCell: enabled);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            ExecuteThenDisable(command: a, parameter: null, enabled: enabled);

            // An int and not a Maybe. Only the command for a Maybe parameter accepts it, thus
            // this also shows that the factory built that command.
            EnableThenExecute(command: a, parameter: 2, enabled: enabled);

            await Assert.That(fired)
                .IsEquivalentTo(expected: [Maybe.Some(2)], ordering: CollectionOrdering.Matching)
                .Because("only the second call found the cell true");
        }
    }

    /// <summary>
    ///     The factory is the only way to build a bindable, and each bindable needs a
    ///     scheduler.
    ///     Thus, a factory with no scheduler fails at its construction, and not at the
    ///     first notification on an incorrect thread.
    /// </summary>
    [Test]
    public async Task TheFactoryRejectsANullScheduler() =>
        // ReSharper disable once NullableWarningSuppressionIsUsed - Testing for exception on null.
        await Assert.That(static () => new BindableFactory(null!)).ThrowsExactly<ArgumentNullException>();

    /// <summary>
    ///     Calls the command and makes the cell false in one transaction. The send
    ///     runs after that transaction closes, thus it finds the cell false.
    /// </summary>
    private static void ExecuteThenDisable(ICommand command, object? parameter, CellSink<bool> enabled) =>
        Transaction.RunVoid(() =>
        {
            command.Execute(parameter);
            enabled.Send(false);
        });

    /// <summary>Makes the cell true, and then calls the command.</summary>
    private static void EnableThenExecute(ICommand command, object? parameter, CellSink<bool> enabled)
    {
        enabled.Send(true);
        command.Execute(parameter);
    }

    /// <summary>
    ///     Keeps a record that the code asked it, and then operates as the immediate
    ///     scheduler.
    ///     Thus, the bindable in the test continues to operate.
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
