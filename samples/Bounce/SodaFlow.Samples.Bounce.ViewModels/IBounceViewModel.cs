using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     The contract that the views bind to.
/// </summary>
/// <remarks>
///     <para>
///         XAML binds by name to the data context, at each type of that data context. Thus, a type
///         for the data context has a value. The type shows the bindable members, gives the
///         designer and the compiler a target for the binding paths, and keeps the construction of
///         the view model away from a view.
///     </para>
///     <para>
///         For that cause this interface has only the bound members. <c>Create</c> is not on it,
///         because a view does not build a view model. The interface also has no other member
///         that a view must not call.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is here because it is part of the contract and not a part
///         of the implementation. The code that built one must release it, through this interface
///         and through the class.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface IBounceViewModel : IDisposable
{
    /// <summary>The scenes to show, with the smallest scene first.</summary>
    IReadOnlyList<IScene> Scenes { get; }

    /// <summary>
    ///     The scene that shows now.
    /// </summary>
    /// <remarks>
    ///     This is two-way, thus it is a value and not a property of a control. Bind the selected
    ///     item of a tab control to <c>SelectedScene.Value</c>, and write that value to change the
    ///     tab that shows.
    /// </remarks>
    ITwoWayBindableValue<IScene> SelectedScene { get; }

    /// <summary>The description of the selected scene, as a function of the selection.</summary>
    IOneWayBindableValue<string> SelectedSummary { get; }

    /// <summary>
    ///     True when damping applies to the selected scene.
    /// </summary>
    /// <remarks>
    ///     The scene with one ball is elastic, thus the controls show only for the other two
    ///     scenes. This is a function of the selection, as the summary is.
    /// </remarks>
    IOneWayBindableValue<bool> IsDampingAvailable { get; }

    /// <summary>
    ///     True when a bounce changes the speed and does not keep it. This is two-way, for a
    ///     checkbox.
    /// </summary>
    ITwoWayBindableValue<bool> DampingEnabled { get; }

    /// <summary>
    ///     The multiplier for the speed at a bounce while damping is on. This is two-way, for a
    ///     slider.
    /// </summary>
    /// <remarks>
    ///     Below one a body loses speed at each bounce and stops. At one it bounces continuously.
    ///     Above one it gets speed and goes higher. The code reads this at the moment of each
    ///     bounce, thus a move of the slider changes the next bounce and not the current flight.
    /// </remarks>
    ITwoWayBindableValue<double> Damping { get; }

    /// <summary>The minimum value of <see cref="Damping" />.</summary>
    double MinimumDamping { get; }

    /// <summary>The maximum value of <see cref="Damping" />.</summary>
    double MaximumDamping { get; }
}
