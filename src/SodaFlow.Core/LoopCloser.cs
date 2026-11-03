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
///     can make one. It converts a value that depends on its own loop. A value that
///     also depends on a different loop, which is not closed, keeps its guard: a
///     read before that loop closes throws an exception.
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
    /// <remarks>
    ///     Each cell that an operation makes is a <see cref="Cell{T}" />, thus this
    ///     usually gives the same object. Only a placeholder is not one, and for it
    ///     this makes a cell over the same value.
    /// </remarks>
    public Cell<T> Close<T>(LoopedCell<T> cell) => cell.AsCellImpl();

    /// <summary>
    ///     Gives a looped behavior as a <see cref="Behavior{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the behavior.</typeparam>
    /// <param name="behavior">The looped behavior.</param>
    /// <returns>The same value as a <see cref="Behavior{T}" />.</returns>
    /// <remarks>
    ///     Each behavior that an operation makes is a <see cref="Behavior{T}" />,
    ///     thus this usually gives the same object. Only a placeholder is not one,
    ///     and for it this makes a behavior over the same value.
    /// </remarks>
    public Behavior<T> Close<T>(LoopedBehavior<T> behavior) => behavior.AsBehaviorImpl();
}
