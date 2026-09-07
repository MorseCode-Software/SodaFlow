using System;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Counter.ViewModels;

/// <summary>
///     What the views bind to.
/// </summary>
/// <remarks>
///     <para>
///         XAML binds by name against whatever the data context happens to be, so the data context
///         is worth stating as a type: it is what makes the bindable members discoverable, gives
///         the designer and the compiler something to check the binding paths against, and keeps a
///         view from depending on how the view model is built.
///     </para>
///     <para>
///         Which is why this is only the bound surface. <c>Create</c> is not on it, because
///         building a view model is not something a view does with one.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is here because it is part of the contract rather than an
///         implementation detail: whoever built one has to release it, and that is as true through
///         this interface as through the class.
///     </para>
/// </remarks>
public interface ICounterViewModel : IDisposable
{
    /// <summary>The current count, for anything that wants the number itself.</summary>
    IOneWayBindableValue<int> Count { get; }

    /// <summary>The count as text, formatted for the current culture.</summary>
    IOneWayBindableValue<string> CountText { get; }

    IBindableAction Increment { get; }

    IBindableAction Decrement { get; }

    /// <summary>Enabled only when the count is not already zero.</summary>
    IBindableAction Reset { get; }
}
