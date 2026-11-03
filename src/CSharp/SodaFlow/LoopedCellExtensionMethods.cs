using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="LoopedCell{T}" />.
/// </summary>
[PublicAPI]
public static class LoopedCellExtensionMethods
{
    /// <summary>
    ///     Asserts that no code reads this <see cref="LoopedCell{T}" /> before its loop
    ///     closes, and gives it as a <see cref="Cell{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the looped cell.</typeparam>
    /// <param name="c">The looped cell.</param>
    /// <returns>The same value as a <see cref="Cell{T}" />.</returns>
    /// <remarks>
    ///     Use this to give a placeholder to code that keeps the cell and reads it after the
    ///     close, for example a constructor. The types cannot see that the read comes
    ///     after the close, thus this call is the assertion. A read before the close
    ///     throws an exception. Where the loop is closed, use <see cref="Close{T}" />,
    ///     which checks it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> AsCell<T>(this LoopedCell<T> c) => c.AsCellImpl();

    /// <summary>
    ///     Gives this <see cref="LoopedCell{T}" /> as a <see cref="Cell{T}" />, after a
    ///     check that each loop it depends on is closed.
    /// </summary>
    /// <typeparam name="T">The type of the looped cell.</typeparam>
    /// <param name="c">The looped cell.</param>
    /// <returns>The same value as a <see cref="Cell{T}" />.</returns>
    /// <exception cref="InvalidOperationException">A loop is not closed.</exception>
    /// <remarks>
    ///     A loop block returns a looped cell in its captures, and the type stays
    ///     looped after the block returns and the loop closes. This gives the cell back,
    ///     with a check and not the assertion that <see cref="AsCell{T}" /> makes.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> Close<T>(this LoopedCell<T> c) => c.CloseImpl();
}
