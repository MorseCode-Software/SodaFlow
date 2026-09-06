using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Search.ViewModels;

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
///         this interface as through the class. What disposal tears down is more than the bindables
///         here - the asynchronous pipeline goes with them - but that is the implementation's
///         business, not this one's.
///     </para>
/// </remarks>
public interface ISearchViewModel : IDisposable
{
    /// <summary>What the user has typed. Two-way, so the view both reads and writes it.</summary>
    ITwoWayBindableValue<string> Query { get; }

    IOneWayBindableValue<IReadOnlyList<string>> Results { get; }

    /// <summary>"Searching..." while a request is out, otherwise a count.</summary>
    IOneWayBindableValue<string> Summary { get; }

    IOneWayBindableValue<string> Error { get; }

    /// <summary>Separate from <see cref="Error" /> so the view can bind visibility to it.</summary>
    IOneWayBindableValue<bool> HasError { get; }

    IOneWayBindableValue<bool> IsBusy { get; }

    /// <summary>Enabled only while a search is running.</summary>
    IBindableAction Cancel { get; }
}
