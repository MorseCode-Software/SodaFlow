using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A behavior that lets a caller send values into it. It connects the world of I/O to the
///     world of FRP. Code that supplies a <see cref="BehaviorSink{T}" /> for read access only must
///     downcast it to <see cref="Behavior{T}" />.
/// </summary>
/// <typeparam name="T">The type of values in the behavior sink.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class BehaviorSink<T> : Behavior<T>
{
    private readonly StreamSink<T> streamSink;

    internal BehaviorSink(T initialValue)
        : this(streamSink: new StreamSink<T>(static (_, right) => right), initialValue: initialValue)
    {
    }

    internal BehaviorSink(T initialValue, Func<T, T, T> coalesce)
        : this(streamSink: new StreamSink<T>(coalesce), initialValue: initialValue)
    {
    }

    private BehaviorSink(StreamSink<T> streamSink, T initialValue)
        : base(stream: streamSink, initialValue: initialValue) =>
        this.streamSink = streamSink;

    internal void SendImpl(T a) => this.streamSink.SendImpl(a);
}
