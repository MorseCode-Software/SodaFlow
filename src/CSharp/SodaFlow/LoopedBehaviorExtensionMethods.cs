using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="LoopedBehavior{T}" />.
/// </summary>
[PublicAPI]
public static class LoopedBehaviorExtensionMethods
{
    /// <summary>
    ///     Asserts that no code reads this <see cref="LoopedBehavior{T}" /> before its
    ///     loop closes, and gives it as a <see cref="Behavior{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the looped behavior.</typeparam>
    /// <param name="b">The looped behavior.</param>
    /// <returns>The same value as a <see cref="Behavior{T}" />.</returns>
    /// <remarks>
    ///     Use this to give a placeholder to code that keeps the behavior and reads it after the
    ///     close, for example a constructor. The types cannot see that the read comes
    ///     after the close, thus this call is the assertion. A read before the close
    ///     throws an exception. For a capture of a loop, use the
    ///     <see cref="LoopCloser" /> that the loop gives when it closes.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> AsBehavior<T>(this LoopedBehavior<T> b) => b.AsBehaviorImpl();
}
