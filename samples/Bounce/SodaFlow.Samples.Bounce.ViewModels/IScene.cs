using System.Collections.Generic;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     One of the arrangements in this sample, from the smallest to the one with input.
/// </summary>
/// <remarks>
///     This is not <see cref="System.IDisposable" />, and the other samples are. No code here
///     subscribes to a value. The balls are behaviors, a view reads them with a sample, and the
///     graph that sets the timers of the simulation holds those timers. There is no subscription
///     to release, thus there is no code to release one.
/// </remarks>
public interface IScene
{
    /// <summary>The name of the scene, for the tab that holds it.</summary>
    string Name { get; }

    /// <summary>The subject of this scene, which the view shows above it.</summary>
    string Summary { get; }

    /// <summary>The width of the box that holds the balls.</summary>
    double Width { get; }

    /// <summary>The height of the box that holds the balls.</summary>
    double Height { get; }

    IReadOnlyList<Ball> Balls { get; }
}

/// <summary>
///     A scene that also reacts to the pointer.
/// </summary>
/// <remarks>
///     This is not part of <see cref="IScene" />, thus three empty methods are not necessary for
///     a scene that does not use the pointer. A view tests the scene that it shows against this
///     type.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface IInteractiveScene : IScene
{
    /// <summary>Holds the ball below the given point, when there is one.</summary>
    void Grab(double x, double y);

    /// <summary>Moves the ball that the scene holds, when it holds one.</summary>
    void MoveTo(double x, double y);

    /// <summary>Releases the ball and throws it at the speed of the pointer.</summary>
    void Release();
}
