using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Cell{T}" />. It is equal to the cell that
///     closes the loop.
/// </summary>
/// <typeparam name="T">The type of values in the cell loop.</typeparam>
/// <remarks>
///     A loop block makes one and closes it when the block returns. The
///     explicit form is a call to the constructor in a transaction, and a call to
///     <see cref="Loop(Cell{T})" /> in the same transaction. The type is a
///     <see cref="LoopedCell{T}" />, thus no code can read its value before the
///     close. The checks are those of the <see cref="BehaviorLoop{T}" /> that holds
///     the value.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class CellLoop<T> : LoopedCell<T>
{
    private readonly BehaviorLoop<T> behaviorLoop;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CellLoop{T}" /> class, a
    ///     forward reference to a cell with no definition at this point.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when there is no explicit transaction open, or, at the end of that
    ///     transaction, if <see cref="Loop(Cell{T})" /> was never called on this instance.
    /// </exception>
    /// <remarks>
    ///     A loop only makes sense in a single transaction, thus one must be open -
    ///     make it with <c>Transaction.Run</c> or <c>Transaction.RunVoid</c>. Resolve
    ///     the loop by calling <see cref="Loop(Cell{T})" /> before that transaction ends. A loop
    ///     with no resolution is a defect, and not a no-op, thus this reports it as a
    ///     defect.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public CellLoop()
        : this(new BehaviorLoop<T>())
    {
    }

    private CellLoop(BehaviorLoop<T> behaviorLoop)
        : base(behaviorLoop) =>
        this.behaviorLoop = behaviorLoop;

    /// <summary>
    ///     Resolve the loop to specify what the <see cref="CellLoop{T}" /> was a
    ///     forward reference to.  This method must run in the same transaction as the
    ///     one that made this <see cref="CellLoop{T}" /> created and used.
    ///     This needs an explicit transaction from <c>Transaction.Run</c> or
    ///     <c>Transaction.RunVoid</c>.
    /// </summary>
    /// <param name="c">The cell of the forward reference.</param>
    /// <returns>
    ///     The <see cref="LoopCloser" />, which converts a value that depends on this
    ///     loop to the plain type.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public LoopCloser Loop(Cell<T> c) => this.behaviorLoop.Loop(c.PlainBehaviorImpl);

    internal LoopCloser Loop(TransactionInternal trans, Cell<T> c) =>
        this.behaviorLoop.Loop(trans: trans, b: c.PlainBehaviorImpl);
}
