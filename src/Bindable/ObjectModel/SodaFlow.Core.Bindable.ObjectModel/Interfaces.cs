using System;
using System.ComponentModel;
using System.Windows.Input;
using JetBrains.Annotations;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Anything this library hands to a view. Every bindable owns a subscription into the FRP graph
///     and must be disposed; this is what lets a view model hold a heterogeneous collection of them.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IBindable : IDisposable
{
}

/// <summary>
///     The readable half of every bindable value, whichever direction it flows.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IReadableBindableValue<T> : IBindable
{
    /// <summary>The current value. Safe to read from any thread.</summary>
    T Value { get; }

    /// <summary>The cell backing this value, for further composition in the FRP graph.</summary>
    Cell<T> Cell { get; }
}

/// <summary>
///     A bindable value the view can write to. Capability rather than direction: implemented by
///     both <see cref="ITwoWayBindableValue{T}" /> and
///     <see cref="IOneWayToSourceBindableValue{T}" />, so a helper that only needs to push a value
///     into the graph can accept either.
/// </summary>
/// <remarks>
///     <para>
///         Write from the binding thread. A bindable may be constructed on any thread and read
///         from any thread, but the setter is the one operation that is not marshaled: it updates
///         the cached value on the calling thread, and the notifications the scheduler delivers
///         update that same value on the binding thread. Writing from anywhere else races them.
///         This constrains application code rather than the view — a binding engine is already
///         calling from the thread in question.
///     </para>
///     <para>
///         A write reaches the graph inside a transaction, and transactions are serialized
///         process-wide, so setting blocks until any transaction in flight elsewhere has closed.
///         That is ordinarily immeasurable, and is the same guarantee that means none of this
///         needs synchronizing by hand — but a long-running transaction on a background thread
///         will hold up the setter, and with it the binding thread, for as long as it runs.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IWritableBindableValue<T> : IBindable
{
    /// <summary>
    ///     Gets the current value, or writes a new one into the FRP graph. Write from the binding
    ///     thread; see the remarks on this interface.
    /// </summary>
    T Value { get; set; }
}

/// <summary>
///     A read-only value that a XAML binding engine can observe. Values originate in the FRP
///     graph and flow outward to the view; the view can never write to it.
/// </summary>
/// <remarks>
///     <para>
///         The property name raised on <see cref="INotifyPropertyChanged.PropertyChanged" /> is always
///         <c>"Value"</c>, so the binding path is <c>{Binding SomeProperty.Value}</c>.
///     </para>
///     <para>
///         Deliberately invariant in <typeparamref name="T" /> rather than covariant, so that
///         <see cref="IReadableBindableValue{T}.Cell" /> can be part of the contract.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IOneWayBindableValue<T> : IReadableBindableValue<T>, INotifyPropertyChanged
{
    /// <summary>The most recent value delivered to the binding thread.</summary>
    new T Value { get; }
}

/// <summary>
///     A value the view can both observe and write to. Writes are pushed back into the FRP graph;
///     the authoritative value always comes back out of the graph.
/// </summary>
/// <remarks>
///     <see cref="Value" /> is redeclared to resolve the two inherited declarations — without it,
///     every access through this interface would be ambiguous.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface ITwoWayBindableValue<T> : IOneWayBindableValue<T>, IWritableBindableValue<T>
{
    /// <summary>
    ///     Gets the most recent value delivered to the binding thread, or writes a new value into
    ///     the FRP graph.
    /// </summary>
    /// <remarks>
    ///     Setting is a no-op when the value is unchanged — unless an update is still on its way
    ///     to the binding thread, in which case it is written regardless, because until that
    ///     update arrives the value read back is not known to be the graph's.
    /// </remarks>
    new T Value { get; set; }
}

/// <summary>
///     A write-only sink for <c>OneWayToSource</c> bindings: the view pushes values in, the FRP
///     graph consumes them. Does not implement <see cref="INotifyPropertyChanged" /> — nothing ever
///     flows back out to the view.
/// </summary>
/// <remarks>
///     A getter is exposed because both WPF and Avalonia read the source property when establishing
///     a <c>OneWayToSource</c> binding. It returns the last value written by the view.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IOneWayToSourceBindableValue<T> : IWritableBindableValue<T>
{
}

/// <summary>
///     An <see cref="ICommand" /> that carries its <c>CommandParameter</c> through to the stream and
///     whose enablement is driven by a <see cref="Cell{T}" /> of <see cref="bool" />.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface IBindableAction<T> : IBindable, ICommand
{
    /// <summary>The cell driving enablement, for further composition in the FRP graph.</summary>
    Cell<bool> IsEnabledCell { get; }

    /// <summary>Fires once per accepted invocation, carrying the command parameter.</summary>
    Stream<T> FiringsStream { get; }
}
