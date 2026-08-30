using System;

namespace SodaFlow
{
    /// <summary>
    ///     Constructs a value which can refer to itself while it is being constructed.
    /// </summary>
    /// <typeparam name="T">The type of the value to construct.</typeparam>
    /// <remarks>
    ///     A cell loop lets a cell be referred to before it exists, and is closed with the cell the
    ///     reference turned out to mean. This is that with the cell taken back out: the function
    ///     produces a single value rather than a cell of changing ones, and the loop it is handed
    ///     resolves to that value and never changes again.
    ///     Both members here work the same way, by closing a cell loop with a constant cell. That is
    ///     what makes this the single-valued case of a loop: the reference resolves to the value the
    ///     function produced and has nothing further to say.
    ///     What it is for is the knot two objects tie when each needs the other at construction.
    ///     Ordinarily one of them has to be built half-formed and completed afterward, with a
    ///     settable member that has no business being settable once the graph is built:
    ///     <code>
    ///     Node node = ForwardReference&lt;Node&gt;.WithoutCaptures(
    ///         reference =&gt; new Node(new Child(reference.AsCell())));
    ///     </code>
    ///     The child holds a <see cref="Cell{T}" /> which is empty of meaning until the call
    ///     returns, and holds the finished node from then on. Nothing has to be mutable, and there
    ///     is no window in which a half-built node is reachable, because the reference cannot be
    ///     sampled before the constructing function returns.
    ///     That last point is the constraint worth remembering. The reference is a promise about
    ///     what a value will be, not the value: reading it during construction - with
    ///     <see cref="CellExtensionMethods.Sample{T}" /> or anything built on it - asks a question
    ///     which has no answer yet, and says so by throwing.
    ///     Everything here builds one transaction of its own, so this can be called from outside a
    ///     transaction as well as within one.
    /// </remarks>
    public static class ForwardReference<T>
    {
        /// <summary>
        ///     Constructs a value which can refer to itself, along with anything else worth keeping
        ///     from its construction.
        /// </summary>
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
        ///     <typeparamref name="TCaptures" /> is inferred from the function.
        ///     <typeparamref name="T" /> is named on <see cref="ForwardReference{T}" /> itself,
        ///     which is what leaves it free to be: a lambda gives type inference nothing to work
        ///     from, and C# does not allow only some of a method's type arguments to be given, so
        ///     naming both here would have meant writing both at every call.
        /// </remarks>
        [Pure]
        public static (T Value, TCaptures Captures) WithCaptures<TCaptures>(
            Func<LoopedCell<T>, (T Value, TCaptures Captures)> f) =>
            Cell.Loop<T>()
                .WithCaptures(reference =>
                {
                    (T Value, TCaptures Captures) result = f(reference);
                    return (Cell: Cell.Constant(result.Value), Captures: result);
                })
                .Captures;

        /// <summary>
        ///     Constructs a value which can refer to itself.
        /// </summary>
        /// <param name="f">
        ///     A function which takes a forward reference to the value it is producing, and returns
        ///     that value.
        /// </param>
        /// <returns>The constructed value.</returns>
        /// <remarks>
        ///     <typeparamref name="T" /> is named on <see cref="ForwardReference{T}" /> itself,
        ///     since a lambda gives type inference nothing to work from.
        /// </remarks>
        [Pure]
        public static T WithoutCaptures(Func<LoopedCell<T>, T> f) =>
            Cell.Loop<T>()
                .WithCaptures(reference =>
                {
                    T value = f(reference);
                    return (Cell: Cell.Constant(value), Captures: value);
                })
                .Captures;
    }
}
