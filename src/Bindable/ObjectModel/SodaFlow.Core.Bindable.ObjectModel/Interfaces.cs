using System;
using System.ComponentModel;
using System.Windows.Input;
using JetBrains.Annotations;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     An object that this library gives to a view. Each bindable owns a
///     subscription into the FRP graph and a caller must dispose it. This lets a
///     view model keep all of them in one collection.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IBindable : IDisposable;

/// <summary>
///     The part of a bindable value that a caller can read, in each direction of
///     flow.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IReadableBindableValue<T> : IBindable, INotifyPropertyChanged
{
    /// <summary>
    ///     The current value, for the binding engine to read. This is not for the code
    ///     of an application.
    ///     See the remarks on <see cref="IWritableBindableValue{T}" />, which apply to
    ///     a read and to a write.
    /// </summary>
    T Value { get; }

    /// <summary>The cell below this value, to use again in the FRP graph.</summary>
    Cell<T> Cell { get; }
}

/// <summary>
///     The part of a bindable value that the view can write. This is the analog of
///     <see cref="IReadableBindableValue{T}" />, and the remarks here apply to each
///     bindable value.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Value" /> is for the binding engine to read and write, and
///         for no other code. A read or a write from the code of an application is
///         a procedural path around the graph. The value that it gives is a value
///         that the graph holds, and a value that you send into it is a value that
///         you can send to a sink. Use cells and streams instead.
///     </para>
///     <para>
///         The rule about threads comes from that. You can construct a bindable on
///         any thread, but only the binding thread touches the property, because a
///         binding engine calls from there. Thus, the cached value below it is a
///         usual field, with no synchronization and no allocation for each change.
///         A read or a write from a different thread removes that condition, and
///         gives no warning. The result is a stale value, or an incomplete value
///         when <typeparamref name="T" /> is a large struct.
///     </para>
///     <para>
///         A write goes into the graph in a transaction, and SodaFlow runs one
///         transaction at a time across the process. Thus, a set waits until each
///         open transaction closes.
///         That wait is usually too small to measure, and it is the same guarantee
///         that makes synchronization by hand unnecessary. But a transaction that
///         runs for a long time on a background thread delays the setter, and with
///         it the binding thread, for the full time that it runs.
///     </para>
///     <para>
///         A throw from the graph does not leave the setter. A binding engine
///         catches a throw from a setter, and can discard it or show it as a
///         validation error. A throw from the graph is a defect, thus the setter
///         gives it to the scheduler, which throws it on the binding thread. A
///         scheduler on a message loop lets it reach that loop.
///         <see cref="BindingScheduler.Immediate" /> throws it immediately, thus the
///         caller of the setter gets it. A write in an open transaction waits for
///         the close of that transaction, and a throw leaves that close.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IWritableBindableValue<T> : IBindable
{
    /// <summary>
    ///     Gets the current value, or writes a new value into the FRP graph. This is
    ///     for the binding engine, on the binding thread. See the remarks on this
    ///     interface.
    /// </summary>
    T Value { get; set; }
}

/// <summary>
///     A value that a XAML binding engine can read but cannot write. A value
///     starts in the FRP graph and moves out to the view. The view cannot write to
///     it.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="INotifyPropertyChanged.PropertyChanged" /> always gives the
///         property name <c>"Value"</c>. Thus, the binding path is
///         <c>{Binding SomeProperty.Value}</c>.
///     </para>
///     <para>
///         This interface is invariant in <typeparamref name="T" /> and not
///         covariant. That is deliberate, and it lets
///         <see cref="IReadableBindableValue{T}.Cell" /> be part of the contract.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IOneWayBindableValue<T> : IReadableBindableValue<T>
{
    /// <summary>The last value that SodaFlow gave to the binding thread.</summary>
    new T Value { get; }
}

/// <summary>
///     A value that the view can read and also write. A write goes into the FRP
///     graph, and the graph always supplies the value that is the authority.
/// </summary>
/// <remarks>
///     This interface declares <see cref="Value" /> again, to select between the
///     two declarations that it inherits. Without that, each access through this
///     interface is ambiguous.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface ITwoWayBindableValue<T> : IOneWayBindableValue<T>, IWritableBindableValue<T>
{
    /// <summary>
    ///     Gets the last value that SodaFlow gave to the binding thread, or writes a
    ///     new value into the FRP graph.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A set does nothing when the value does not change. But when an update
    ///         is on its path to the binding thread, the setter writes the value.
    ///         Until that update arrives, the value that a caller reads back can be
    ///         different from the value in the graph.
    ///     </para>
    ///     <para>
    ///         A write that changes the value raises
    ///         <see cref="INotifyPropertyChanged.PropertyChanged" /> after the graph
    ///         becomes stable, and carries the value that the graph settled on. Thus,
    ///         many controls can bind to this property, in each direction, and all of
    ///         them follow a write from any one of them.
    ///     </para>
    /// </remarks>
    new T Value { get; set; }
}

/// <summary>
///     An <see cref="ICommand" /> that moves its <c>CommandParameter</c> to the
///     stream. A <see cref="Cell{T}" /> of <see cref="bool" /> controls when the
///     command is available.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IBindableAction<T> : IBindable, ICommand
{
    /// <summary>The cell that controls availability, to use again in the FRP graph.</summary>
    Cell<bool> IsEnabledCell { get; }

    /// <summary>
    ///     Fires one time for each accepted call, and carries the command
    ///     parameter.
    /// </summary>
    /// <remarks>
    ///     The command accepts a call to <see cref="ICommand.Execute" /> when
    ///     <see cref="ICommand.CanExecute" /> gives true, and when
    ///     <see cref="IsEnabledCell" /> is true in the transaction that sends the
    ///     parameter. The second test is necessary, because CanExecute reads a copy
    ///     of the cell, and that copy can be older than the cell. A send from other
    ///     code into the same sink also fires this stream.
    /// </remarks>
    Stream<T> FiringsStream { get; }
}
