using System;
using System.Runtime.CompilerServices;

namespace SodaFlow
{
    /// <summary>
    ///     The operations available on a <see cref="CellSink{T}" />.
    /// </summary>
    /// <remarks>
    ///     A cell sink is how a value from outside the FRP graph is pushed into it. These operations are
    ///     for interfacing I/O to FRP only, and throw if called from inside a listener handler.
    /// </remarks>
    public static class CellSinkExtensionMethods
    {
        /// <summary>
        ///     Send a value, modifying the value of the cell.  This method may not be called from inside handlers registered with
        ///     <see cref="StreamExtensionMethods.Listen{T}(Stream{T}, Action{T})" />,
        ///     <see cref="StreamExtensionMethods.ListenStrong{T}(Stream{T}, Action{T})" /> or either of their cell
        ///     equivalents.
        ///     An exception will be thrown, because sinks are for interfacing I/O to FRP only.  They are not meant to be used to
        ///     define new primitives.
        /// </summary>
        /// <typeparam name="T">The type of the cell sink.</typeparam>
        /// <param name="c">The cell sink.</param>
        /// <param name="a">The value to send.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Send<T>(this CellSink<T> c, T a) => c.SendImpl(a);

        /// <summary>
        ///     Return a reference to this <see cref="CellSink{T}" /> as a <see cref="Cell{T}" />.
        /// </summary>
        /// <typeparam name="T">The type of the cell sink.</typeparam>
        /// <param name="c">The cell sink.</param>
        /// <returns>A reference to this <see cref="CellSink{T}" /> as a <see cref="Cell{T}" />.</returns>
        public static Cell<T> AsCell<T>(this CellSink<T> c) => c;
    }
}