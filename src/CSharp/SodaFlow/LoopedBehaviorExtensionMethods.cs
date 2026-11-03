using System;
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
    ///     throws an exception. Where the loop is closed, use <see cref="Close{T}" />,
    ///     which checks it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> AsBehavior<T>(this LoopedBehavior<T> b) => b.AsBehaviorImpl();

    /// <summary>
    ///     Gives this <see cref="LoopedBehavior{T}" /> as a <see cref="Behavior{T}" />,
    ///     after a check that each loop it depends on is closed.
    /// </summary>
    /// <typeparam name="T">The type of the looped behavior.</typeparam>
    /// <param name="b">The looped behavior.</param>
    /// <returns>The same value as a <see cref="Behavior{T}" />.</returns>
    /// <exception cref="InvalidOperationException">A loop is not closed.</exception>
    /// <remarks>
    ///     A loop block returns a looped behavior in its captures, and the type
    ///     stays looped after the block returns and the loop closes. This gives the behavior
    ///     back, with a check and not the assertion that
    ///     <see cref="AsBehavior{T}" /> makes.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Behavior<T> Close<T>(this LoopedBehavior<T> b) => b.CloseImpl();
}
