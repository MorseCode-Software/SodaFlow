using System;

namespace SodaFlow
{
    /// <summary>
    ///     Constructs a value which can refer to itself while it is being constructed.
    /// </summary>
    /// <remarks>
    ///     A cell loop lets a cell be referred to before it exists, and is closed with the cell the
    ///     reference turned out to mean. This is that with the cell taken back out: the function
    ///     produces a single value rather than a cell of changing ones, and the loop it is handed
    ///     resolves to that value and never changes again.
    ///
    ///     What it is for is the knot two objects tie when each needs the other at construction.
    ///     Ordinarily one of them has to be built half-formed and completed afterwards, with a
    ///     settable member that has no business being settable once the graph is built:
    ///
    ///     <code>
    ///     Node node = ForwardReference.WithoutCaptures&lt;Node&gt;(
    ///         reference =&gt; new Node(new Child(reference.AsCell())));
    ///     </code>
    ///
    ///     The child holds a <see cref="Cell{T}" /> which is empty of meaning until the call
    ///     returns, and holds the finished node from then on. Nothing has to be mutable, and there
    ///     is no window in which a half-built node is reachable, because the reference cannot be
    ///     sampled before the constructing function returns.
    ///
    ///     That last point is the constraint worth remembering. The reference is a promise about
    ///     what a value will be, not the value: reading it during construction - with
    ///     <see cref="CellExtensionMethods.Sample{T}" /> or anything built on it - asks a question
    ///     which has no answer yet, and says so by throwing.
    ///
    ///     Everything here builds one transaction of its own, so this can be called from outside a
    ///     transaction as well as within one.
    /// </remarks>
    public static class ForwardReference
    {
        /// <summary>
        ///     Constructs a value which can refer to itself, along with anything else worth keeping
        ///     from its construction.
        /// </summary>
        /// <typeparam name="T">The type of the value to construct.</typeparam>
        /// <typeparam name="TCaptures">The type of the captures to return.</typeparam>
        /// <param name="f">
        ///     A function which takes a forward reference to the value it is producing, and returns
        ///     that value along with the captures.
        /// </param>
        /// <returns>A value tuple containing the constructed value and the captures.</returns>
        /// <remarks>
        ///     The captures are for the parts built along the way which the value itself does not
        ///     expose - a sink to feed it, an inner cell to observe - and which would otherwise be
        ///     unreachable once the function has returned.
        ///
        ///     Both type arguments have to be written out. <typeparamref name="TCaptures" /> could
        ///     be inferred from the function, but <typeparamref name="T" /> cannot be inferred from
        ///     a lambda, and C# does not allow only some of a method's type arguments to be given.
        /// </remarks>
        [Pure]
        public static (T Value, TCaptures Captures) WithCaptures<T, TCaptures>(
            Func<LoopedCell<T>, (T Value, TCaptures Captures)> f) =>
            Cell.Loop<T>()
                .WithCaptures<(T Value, TCaptures Captures)>(
                    reference =>
                    {
                        (T Value, TCaptures Captures) result = f(reference);

                        // The loop is closed with a constant cell, which is what makes this the
                        // single-valued case of a cell loop: the reference resolves to the value
                        // the function produced and has nothing further to say.
                        return (Cell: Cell.Constant(result.Value), Captures: result);
                    })
                .Captures;

        /// <summary>
        ///     Constructs a value which can refer to itself.
        /// </summary>
        /// <typeparam name="T">The type of the value to construct.</typeparam>
        /// <param name="f">
        ///     A function which takes a forward reference to the value it is producing, and returns
        ///     that value.
        /// </param>
        /// <returns>The constructed value.</returns>
        /// <remarks>
        ///     The type argument has to be written out, since it cannot be inferred from a lambda.
        /// </remarks>
        [Pure]
        public static T WithoutCaptures<T>(Func<LoopedCell<T>, T> f) =>
            WithCaptures<T, UnitInternal>(
                reference => (Value: f(reference), Captures: UnitInternal.Value)).Value;
    }
}
