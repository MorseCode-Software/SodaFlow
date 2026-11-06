using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Converts a value that depends on a loop back to the plain type. A loop gives
///     one when it closes, thus the conversion comes after the close.
/// </summary>
/// <remarks>
///     A loop block gives it to the function that converts the captures, and
///     <see cref="CellLoop{T}.Loop(Cell{T})" /> and
///     <see cref="BehaviorLoop{T}.Loop(Behavior{T})" /> return it. No other code
///     can make one. It reads the value that it converts. Thus, a value that also
///     depends on a different loop, which is not closed, throws an exception at the
///     call. Convert such a value with the closer of the last loop that it depends
///     on.
/// </remarks>
[PublicAPI]
public sealed class LoopCloser
{
    internal static readonly LoopCloser Instance = new();

    private LoopCloser()
    {
    }

    /// <summary>
    ///     Gives a looped cell as a <see cref="Cell{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the cell.</typeparam>
    /// <param name="cell">The looped cell.</param>
    /// <returns>The same value as a <see cref="Cell{T}" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The value depends on a loop that is not closed.
    /// </exception>
    /// <remarks>
    ///     Each cell that an operation makes is a <see cref="Cell{T}" />, thus this
    ///     usually gives the same object. Only a placeholder is not one, and for it
    ///     this makes a cell over the same value.
    /// </remarks>
    public Cell<T> Close<T>(LoopedCell<T> cell)
    {
        Cell<T> result = cell.AsCellImpl();

        // The read is the check. The sample phase of this transaction forces the initial value,
        // thus the read adds no cost. A value that depends on a loop that is not closed throws
        // here, and not at a read that comes after this.
        result.PlainBehaviorImpl.SampleImpl();

        return result;
    }

    /// <summary>
    ///     Gives a looped behavior as a <see cref="Behavior{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <param name="behavior">The looped behavior.</param>
    /// <returns>The same value as a <see cref="Behavior{T}" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The value depends on a loop that is not closed.
    /// </exception>
    /// <remarks>
    ///     Each behavior that an operation makes is a <see cref="Behavior{T}" />,
    ///     thus this usually gives the same object. Only a placeholder is not one,
    ///     and for it this makes a behavior over the same value.
    /// </remarks>
    public Behavior<T> Close<T>(LoopedBehavior<T> behavior)
    {
        Behavior<T> result = behavior.AsBehaviorImpl();

        // See the cell overload: the read is the check.
        result.SampleImpl();

        return result;
    }
}
