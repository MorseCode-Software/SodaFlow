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
    ///     Use this at a read that the types cannot check, for example in a test after
    ///     the block returns. The call is the assertion that the loop is closed, and a
    ///     read before the close throws an exception. An object that is built in the
    ///     block, for example a row, takes the looped type and does not use this. For a
    ///     capture of a loop, use the <see cref="LoopCloser" /> that the loop gives
    ///     when it closes.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Cell<T> AsCell<T>(this LoopedCell<T> c) => c.AsCellImpl();
}
