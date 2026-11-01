using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A stream that lets a caller send values into it. It connects the world of
///     I/O to the world of FRP. Code that supplies a stream sink for read access
///     only must downcast it to <see cref="Stream{T}" />.
/// </summary>
/// <typeparam name="T">The type of values fired by the stream sink.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class StreamSink<T> : Stream<T>
{
    private readonly Action<TransactionInternal, T> coalescer;

    internal StreamSink()
        : this(static (_, _) =>
            throw new InvalidOperationException(
                "Send was called more than once in a transaction, which isn't allowed.  To combine the streams, pass a coalescing function to the StreamSink constructor."))
    {
    }

    internal StreamSink(Func<T, T, T> coalesce) => this.coalescer = CoalesceHandler.Create(f: coalesce, @out: this);

    // A static lambda with a state, and an entry with fields: no closure and no delegate for a
    // send from outside a transaction. The timer system's start hook sends one time in each
    // transaction in the process, which is where this showed.
    internal void SendImpl(T a) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
            {
                if (trans.InCallback > 0)
                {
                    throw new InvalidOperationException("Send may not be called inside a callback.");
                }

                trans.Send(new SendEntry(sink: state.Sink, value: state.Value));

                return UnitInternal.Value;
            },
            state: (Sink: this, Value: a));

    private sealed class SendEntry(in StreamSink<T> sink, in T value) : TransactionInternal.SendEntry
    {
        private readonly StreamSink<T> sink = sink;
        private readonly T value = value;

        internal override void Run(TransactionInternal trans) => this.sink.coalescer(arg1: trans, arg2: this.value);
    }
}
