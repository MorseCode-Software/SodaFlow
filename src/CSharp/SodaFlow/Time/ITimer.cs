using System;
using JetBrains.Annotations;

namespace SodaFlow.Time;

/// <summary>
///     An interface for a handle to cancel a timer.
/// </summary>
/// <remarks>
///     Disposing of the timer has the same effect as calling <see cref="Cancel" />.
///     Only one or the other needs to be called to cancel the timer.
///     Otherwise, objects implementing this interface do not need to be disposed.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public interface ITimer : IDisposable
{
    /// <summary>
    ///     Cancels the timer, so that it will not fire.
    /// </summary>
    /// <remarks>
    ///     Has no effect if the timer has already fired or already been canceled, so it is safe to
    ///     call more than once. Disposing the timer does the same thing.
    /// </remarks>
    void Cancel();
}
