using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>Covers the command: enablement, firing, parameter typing and disposal.</summary>
public class BindableActionTests
{
    private static IBindableAction<T> Action<T>(StreamSink<T> sink, Cell<bool>? isEnabled = null)
        where T : notnull =>
        sink.ToBindableActionImpl(isEnabledCell: isEnabled, scheduler: BindingScheduler.Immediate);

    [Test]
    public async Task IsExecutableByDefault()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        await Assert.That(condition: a.CanExecute(null)).IsTrue().Because("no enablement cell means always enabled");
    }

    [Test]
    public async Task FollowsItsEnablementCell()
    {
        CellSink<bool> enabled = Cell.CreateSink(false);

        using IBindableAction<int> a = Action(sink: Stream.CreateSink<int>(), isEnabled: enabled);

        await Assert.That(condition: a.CanExecute(null)).IsFalse().Because("the constructor samples the cell");

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
        List<int> fired = new();

        using IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(42);

            await Assert.That(fired).IsEquivalentTo(new[] { 42 }, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task DoesNotFireWhileDisabled()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = new();

        using IBindableAction<int> a = Action(sink: sink, isEnabled: Cell.Constant(false));

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Execute(1);

            await Assert.That(fired).IsEmpty();
        }
    }

    // The type check is a diagnostic for whoever wrote the XAML, so it has to surface at the call
    // site. Deferring it into the posted send would have thrown somewhere they cannot see.
    [Test]
    public async Task RejectsAMistypedParameterAtTheCallSite()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        await Assert.That(() => a.Execute("not an int")).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RejectsNullForAReferenceType()
    {
        using IBindableAction<string> a = Action(Stream.CreateSink<string>());

        await Assert.That(() => a.Execute(null)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RejectsNullForAValueType()
    {
        using IBindableAction<int> a = Action(Stream.CreateSink<int>());

        await Assert.That(() => a.Execute(null)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task StopsFiringOnceDisposed()
    {
        StreamSink<int> sink = Stream.CreateSink<int>();
        List<int> fired = new();

        IBindableAction<int> a = Action(sink);

        using (a.FiringsStream.ListenStrong(fired.Add))
        {
            a.Dispose();
            a.Execute(1);

            await Assert.That(fired).IsEmpty();
            await Assert.That(a.CanExecute(null)).IsFalse();
        }
    }

    // A binding engine caches the last CanExecute answer and only asks again when told to, so
    // disposing without notifying leaves a button enabled that does nothing when clicked.
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
