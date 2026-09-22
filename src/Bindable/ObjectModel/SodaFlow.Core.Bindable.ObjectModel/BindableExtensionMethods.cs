using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Extension methods that give you a bindable. Each implementation is a private nested type.
///     Thus, the public surface is the four interfaces only.
/// </summary>
public static partial class BindableCoreExtensionMethods
{
    /// <summary>Shows a cell as a bindable property that a caller cannot write.</summary>
    internal static IOneWayBindableValue<T> ToOneWayImpl<T>(
        this Cell<T> cell,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        new OneWayBindableValue<T>(cell: cell, scheduler: scheduler, comparer: comparer);

    /// <summary>
    ///     Shows a cell sink as a two-way bindable property. This is the simplest condition. The
    ///     view is the only writer, and the sink holds the value that is the authority.
    /// </summary>
    internal static ITwoWayBindableValue<T> ToTwoWayImpl<T>(
        this CellSink<T> sink,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        new TwoWayBindableValue<T>(cell: sink, write: sink.SendImpl, scheduler: scheduler, comparer: comparer);

    /// <summary>
    ///     Shows a cell as a two-way bindable property, and sends the writes of the view into
    ///     <paramref name="editsStreamSink" />.
    /// </summary>
    internal static ITwoWayBindableValue<T> ToTwoWayImpl<T>(
        this Cell<T> cell,
        StreamSink<T> editsStreamSink,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null)
    {
        if (editsStreamSink == null)
        {
            throw new ArgumentNullException(nameof(editsStreamSink));
        }

        return new TwoWayBindableValue<T>(
            cell: cell,
            write: editsStreamSink.SendImpl,
            scheduler: scheduler,
            comparer: comparer);
    }

    /// <summary>
    ///     Creates a one-way-to-source bindable property with an initial value, and sends the
    ///     writes of the view into <paramref name="sink" />.
    /// </summary>
    internal static IOneWayToSourceBindableValue<T> ToOneWayToSourceImpl<T>(
        this CellSink<T> sink,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        new OneWayToSourceBindableValue<T>(
            write: sink.SendImpl,
            initialValue: sink.SampleImpl(),
            scheduler: scheduler,
            comparer: comparer);

    /// <summary>
    ///     Creates a one-way-to-source bindable property with an initial value, and sends the
    ///     writes of the view into <paramref name="editsStreamSink" />.
    /// </summary>
    internal static IOneWayToSourceBindableValue<T> ToOneWayToSourceImpl<T>(
        this StreamSink<T> editsStreamSink,
        T initialValue,
        IBindingScheduler? scheduler = null,
        IEqualityComparer<T>? comparer = null) =>
        new OneWayToSourceBindableValue<T>(
            write: editsStreamSink.SendImpl,
            initialValue: initialValue,
            scheduler: scheduler,
            comparer: comparer);

    /// <summary>
    ///     Shows a sink that exists as a command that carries its <c>CommandParameter</c>.
    /// </summary>
    /// <remarks>
    ///     For a <c>StreamSink&lt;Unit&gt;</c> the compiler selects the overload with no type
    ///     parameter. To get the overload with a parameter for a unit sink, write
    ///     <c>ToBindableAction&lt;Unit&gt;(...)</c>.
    /// </remarks>
    internal static IBindableAction<T> ToBindableActionImpl<T>(
        this StreamSink<T> firingsStreamSink,
        Cell<bool>? isEnabledCell = null,
        IBindingScheduler? scheduler = null)
        where T : notnull =>
        new BindableAction<T>(
            firingsStreamSink: firingsStreamSink,
            isEnabledCell: isEnabledCell,
            scheduler: scheduler);

    /// <summary>
    ///     Listens to the updates of a cell only. It does not give the initial value, because a
    ///     caller samples that value in the same transaction.
    /// </summary>
    /// <remarks>
    ///     This subscription is weak, and that is deliberate. The node holds the handler with a
    ///     weak reference. Thus, the listener that this method gives you is the only object that
    ///     keeps the subscription alive. A caller MUST keep it in a field and MUST NOT let it
    ///     become a local variable that looks unused. In exchange, the garbage collector removes
    ///     a bindable that becomes unreachable with no call to Dispose, and removes its
    ///     subscription with it. Without this, the sink keeps the bindable alive for the full
    ///     life of the sink. A second reference to the cell is not necessary, because the
    ///     listener references the stream that it listens to, and that stream keeps the objects
    ///     above it alive.
    /// </remarks>
    private static IListener ListenToUpdates<T>(Cell<T> cell, Action<T> handler) =>
        cell.UpdatesImpl.ListenImpl(handler);

    /// <summary>
    ///     Sends a value into the graph after the current <see cref="TransactionInternal" />
    ///     ends. Thus, the send cannot run in a callback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When no transaction is open, this method opens one and runs immediately, thus the
    ///         behavior does not change. That is the usual condition, a binding setter that an
    ///         idle dispatcher calls. When a transaction is open, this method runs in a new
    ///         transaction that it opens after the current one ends. A transaction is open only
    ///         when a nested message loop delivered the write from a callback. The send is correct
    ///         in each condition. Do not call <c>Send</c> on a sink directly from a binding
    ///         setter.
    ///     </para>
    ///     <para>
    ///         Each posted callback runs as its own transaction. Thus, two writes cannot occur in
    ///         one transaction. For that cause a <c>StreamSink&lt;T&gt;</c> is sufficient as a
    ///         write target, and a variant that combines writes is not necessary.
    ///     </para>
    /// </remarks>
    private static void PostWrite(Action write) => TransactionInternal.PostImpl(write);

    /// <summary>
    ///     Throws when the caller is not on the binding thread.
    /// </summary>
    /// <remarks>
    ///     The cached value below the <c>Value</c> property of each bindable is a usual field.
    ///     It is safe because one thread touches the property. Before this method, no code made
    ///     that true. An incorrect thread gave a stale read, or an incomplete read for a large
    ///     struct. Both are silent, and you cannot make either one occur on demand. This method
    ///     makes an exception at the call site that caused it.
    ///     It throws only when the scheduler is sure. See
    ///     <see cref="IBindingScheduler.CheckAccess()" />. Thus, it reports nothing that it cannot
    ///     show.
    /// </remarks>
    private static void VerifyAccess(this IBindingScheduler scheduler, string member)
    {
        if (scheduler.CheckAccess())
        {
            return;
        }

        throw new InvalidOperationException(
            $"{member} was accessed from a thread other than the binding thread. A bindable's "
            + "Value exists for the binding engine, and is read and written on the binding thread "
            + "only; the value behind it is not synchronized for anything else. To read or change "
            + "this from elsewhere, go through the FRP graph - sample the cell, or send to the "
            + "sink - rather than through this property.");
    }
}
