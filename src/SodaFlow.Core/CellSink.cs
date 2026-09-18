using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A cell that lets a caller send values into it. It connects the world of I/O to the world of
///     FRP. Code that supplies a <see cref="CellSink{T}" /> for read access only must downcast it
///     to <see cref="Cell{T}" />.
/// </summary>
/// <typeparam name="T">The type of values in the cell sink.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class CellSink<T> : Cell<T>
{
    private readonly CellStreamSink<T> streamSink;

    internal CellSink(T initialValue)
        : this(streamSink: new CellStreamSink<T>(), initialValue: initialValue)
    {
    }

    internal CellSink(T initialValue, Func<T, T, T> coalesce)
        : this(streamSink: new CellStreamSink<T>(coalesce), initialValue: initialValue)
    {
    }

    private CellSink(CellStreamSink<T> streamSink, T initialValue)
        : this(streamSink: streamSink, behavior: new Behavior<T>(stream: streamSink, initialValue: initialValue))
    {
    }

    private CellSink(CellStreamSink<T> streamSink, Behavior<T> behavior)
        : base(behavior) =>
        this.streamSink = streamSink;

    internal void SendImpl(T a) => this.streamSink.SendImpl(a);
}
