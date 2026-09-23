using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The operations available on a <see cref="BehaviorSink{T}" />.
/// </summary>
/// <remarks>
///     A behavior sink puts a value from other code into the FRP graph. These operations
///     are for interfacing I/O to FRP only, and throw if called in a listener handler.
/// </remarks>
[PublicAPI]
public static class BehaviorSinkExtensionMethods
{
    /// <summary>
    ///     Send a value, modifying the value of the behavior. A caller must not call this method in a handler
    ///     registered with <see cref="StreamExtensionMethods.Listen{T}(Stream{T}, Action{T})" />,
    ///     <see cref="StreamExtensionMethods.ListenStrong{T}(Stream{T}, Action{T})" /> or one of their cell
    ///     equivalents. An exception will be thrown, because sinks are for interfacing I/O to FRP only. They
    ///     are not for the definition of a new primitive.
    /// </summary>
    /// <typeparam name="T">The type of the behavior sink.</typeparam>
    /// <param name="b">The behavior sink.</param>
    /// <param name="a">The value to send.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Send<T>(this BehaviorSink<T> b, T a) => b.SendImpl(a);

    /// <summary>
    ///     Return a reference to this <see cref="BehaviorSink{T}" /> as a <see cref="Behavior{T}" />.
    /// </summary>
    /// <typeparam name="T">The type of the behavior sink.</typeparam>
    /// <param name="b">The behavior sink.</param>
    /// <returns>A reference to this <see cref="BehaviorSink{T}" /> as a <see cref="Behavior{T}" />.</returns>
    public static Behavior<T> AsBehavior<T>(this BehaviorSink<T> b) => b;
}
