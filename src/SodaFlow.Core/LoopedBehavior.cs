using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Behavior{T}" />. It is equal to the
///     <see cref="Behavior{T}" /> that the loop supplies.
/// </summary>
/// <typeparam name="T">The type of values in the behavior loop.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class LoopedBehavior<T> : Behavior<T>
{
    private readonly LoopedStream<T> streamLoop;

    private Lazy<T>? lazyInitialValue;

    internal LoopedBehavior()
        : this(new LoopedStream<T>())
    {
    }

    private LoopedBehavior(LoopedStream<T> streamLoop)
        // ReSharper disable once NullableWarningSuppressionIsUsed - initialValue is assigned to valueProperty on
        // the base class. Only SampleNoTransaction() reads that value, and this class overrides
        // that method to make sure that the value is set before the return.
        : base(stream: streamLoop, initialValue: default!) =>
        this.streamLoop = streamLoop;

    internal void Loop(TransactionInternal trans, Behavior<T> b)
    {
        this.streamLoop.Loop(trans: trans, stream: b.Updates());
        this.lazyInitialValue = b.SampleLazy(trans);
    }

    /// <summary>
    ///     Releases the deferred initial value when this behavior gets a value of its own.
    /// </summary>
    /// <remarks>
    ///     A looped behavior gets its initial value from the value that closes the loop, and it
    ///     reads that value only when a caller asks for it. The value that closes the loop is
    ///     not known when SodaFlow creates the loop. After the behavior gets a value, the
    ///     deferred value is no longer necessary. Thus SodaFlow releases it and does not keep it
    ///     alive for the full life of the behavior.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    protected override void NotUsingInitialValue()
    {
        base.NotUsingInitialValue();

        this.lazyInitialValue = null;
    }

    internal override T SampleNoTransaction()
    {
        if (!this.streamLoop.IsAssigned)
        {
            throw new InvalidOperationException("BehaviorLoop was sampled before it was looped.");
        }

        this.EnsureValueIsCreated();

        return this.ValueProperty;
    }

    private void EnsureValueIsCreated()
    {
        if (this.UsingInitialValue && this.lazyInitialValue != null)
        {
            this.ValueProperty = this.lazyInitialValue.Value;
            this.lazyInitialValue = null;
        }
    }
}
