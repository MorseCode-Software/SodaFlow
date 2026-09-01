using System;
using System.ComponentModel;
using System.Windows.Input;

namespace SodaFlow.Bindable.ObjectModel;

/// <summary>
///     Anything this library hands to a view. Every bindable owns a subscription into the FRP graph
///     and must be disposed; this is what lets a view model hold a heterogeneous collection of them.
/// </summary>
public interface IBindable : IDisposable
{
}

/// <summary>
///     The readable half of every bindable value, whichever direction it flows.
/// </summary>
public interface IReadableBindableValue<T> : IBindable
{
    /// <summary>The current value.</summary>
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
public interface IWritableBindableValue<T> : IBindable
{
    /// <summary>Gets the current value, or writes a new one into the FRP graph.</summary>
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
public interface ITwoWayBindableValue<T> : IOneWayBindableValue<T>, IWritableBindableValue<T>
{
    /// <summary>
    ///     Gets the most recent value delivered to the binding thread, or writes a new value into
    ///     the FRP graph. Setting is a no-op if the value is unchanged.
    /// </summary>
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
public interface IOneWayToSourceBindableValue<T> : IWritableBindableValue<T>
{
}

/// <summary>
///     An <see cref="ICommand" /> that carries its <c>CommandParameter</c> through to the stream and
///     whose enablement is driven by a <see cref="Cell{T}" /> of <see cref="bool" />.
/// </summary>
public interface IBindableAction<T> : IBindable, ICommand
{
    /// <summary>The cell driving enablement, for further composition in the FRP graph.</summary>
    Cell<bool> IsEnabledCell { get; }

    /// <summary>Fires once per accepted invocation, carrying the command parameter.</summary>
    Stream<T> FiringsStream { get; }
}