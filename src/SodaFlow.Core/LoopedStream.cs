using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A forward reference to a <see cref="Stream{T}" />. It is equal to the
///     <see cref="Stream{T}" /> that the loop supplies.
/// </summary>
/// <typeparam name="T">The type of values fired by the stream loop.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class LoopedStream<T> : Stream<T>
{
    private readonly object isAssignedLock = new();
    private bool isAssigned;

    internal LoopedStream()
    {
    }

    internal bool IsAssigned
    {
        get
        {
            lock (this.isAssignedLock)
            {
                return this.isAssigned;
            }
        }
    }

    internal void Loop(TransactionInternal trans, Stream<T> stream)
    {
        lock (this.isAssignedLock)
        {
            if (this.isAssigned)
            {
                throw new InvalidOperationException("Loop was looped more than once.");
            }

            this.isAssigned = true;
        }

        this.AttachListenerInternal(stream.Listen(target: this.Node, action: this.Send));

        lock (stream.KeepListenersAlive)
        {
            stream.KeepListenersAlive.Use(this.KeepListenersAlive);
        }
    }
}
