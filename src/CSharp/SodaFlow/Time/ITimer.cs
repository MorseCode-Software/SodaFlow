using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     An interface for a handle to cancel a timer.
/// </summary>
/// <remarks>
///     Disposing of the timer has the same effect as calling <see cref="Cancel" />.
///     Only one or the other needs to be called to cancel the timer.
///     Otherwise, a caller does not have to dispose of an object that implements this interface.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface ITimer : IDisposable
{
    /// <summary>
    ///     Cancels the timer, so that it will not fire.
    /// </summary>
    /// <remarks>
    ///     This does nothing when the timer fired, and when a call canceled it, thus it is safe to
    ///     call more than one time. Disposing the timer does the same thing.
    /// </remarks>
    void Cancel();
}
