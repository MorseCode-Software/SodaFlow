using System;
using JetBrains.Annotations;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Counter.ViewModels;

/// <summary>
///     The contract that the views bind to.
/// </summary>
/// <remarks>
///     <para>
///         XAML binds by name to the data context, at each type of that data context. Thus a type
///         for the data context has a value. The type shows the bindable members, gives the
///         designer and the compiler a target for the binding paths, and keeps the construction of
///         the view model away from a view.
///     </para>
///     <para>
///         For that cause this interface has only the bound members. <c>Create</c> is not on it,
///         because a view does not build a view model.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is here because it is part of the contract and not a part
///         of the implementation. The code that built one must release it, through this interface
///         and through the class.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface ICounterViewModel : IDisposable
{
    /// <summary>The current count, for code that uses the number.</summary>
    [UsedImplicitly] // This property is actually unused, but provided simply as a sample
    IOneWayBindableValue<int> Count { get; }

    /// <summary>The count as text, in the format of the current culture.</summary>
    IOneWayBindableValue<string> CountText { get; }

    IBindableAction Increment { get; }

    IBindableAction Decrement { get; }

    /// <summary>This is enabled only when the count is not zero.</summary>
    IBindableAction Reset { get; }
}
