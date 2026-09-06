using System.Collections.Generic;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     One of the arrangements this sample shows, from the smallest to the one that takes input.
/// </summary>
/// <remarks>
///     Not <see cref="System.IDisposable" />, and that is worth a word given the other samples
///     are. Nothing here subscribes to anything: the balls are behaviors, a view reads them by
///     sampling, and the timers the simulation arms are held by the graph that arms them. There is
///     no subscription to release, so there is nothing to release it.
/// </remarks>
public interface IScene
{
    /// <summary>The scene's name, for the tab that holds it.</summary>
    string Name { get; }

    /// <summary>What this scene demonstrates, shown above it.</summary>
    string Summary { get; }

    /// <summary>The width of the box the balls are confined to.</summary>
    double Width { get; }

    /// <summary>The height of the box the balls are confined to.</summary>
    double Height { get; }

    IReadOnlyList<Ball> Balls { get; }
}

/// <summary>
///     A scene that also responds to the pointer.
/// </summary>
/// <remarks>
///     Separate from <see cref="IScene" /> so that the scenes which ignore the pointer are not
///     obliged to say so with three empty methods. A view asks whether the scene it is showing is
///     one of these.
/// </remarks>
public interface IInteractiveScene : IScene
{
    /// <summary>Takes hold of whichever ball is under the given point, if any.</summary>
    void Grab(double x, double y);

    /// <summary>Moves the held ball, if one is held.</summary>
    void MoveTo(double x, double y);

    /// <summary>Lets go, throwing the held ball at the speed the pointer was moving.</summary>
    void Release();
}
