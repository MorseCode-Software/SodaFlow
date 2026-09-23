using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Constructs a value which can refer to itself while it is being constructed.
/// </summary>
/// <typeparam name="T">The type of the value to make.</typeparam>
/// <remarks>
///     A cell loop lets a cell be referred to before it exists, and is closed with the cell the
///     reference turned out to mean. This is that with the cell taken back out: the function
///     produces a single value rather than a cell of changing ones, and the loop it is handed
///     resolves to that value and never changes again.
///     The two members here operate the same, and each closes a cell loop with a constant cell. That is
///     what makes this the loop with one value: the reference resolves to the value the
///     function produced and has nothing more to say.
///     Use this for the cycle between two objects where each one needs the other one at its construction.
///     Ordinarily one of them has to be built half-formed and completed afterward, with a
///     settable member that has no business as a settable member after the code builds the graph:
///     <code>
///     Node node = ForwardReference&lt;Node&gt;.WithoutCaptures(
///         reference =&gt; new Node(new Child(reference.AsCell())));
///     </code>
///     The child holds a <see cref="Cell{T}" /> which is empty of meaning until the call
///     returns, and holds the finished node from then on. Nothing has to be mutable, and there
///     is no window in which a half-built node is reachable, because the reference cannot be
///     sampled before the constructing function returns.
///     That last point is the constraint worth remembering. The reference is a promise about
///     what a value will be, and not the value. A read of it during the construction, with
///     <see cref="CellExtensionMethods.Sample{T}" /> or anything built on it - asks a question
///     which has no answer now. The read throws an exception to say this.
///     Everything here builds one transaction of its own, thus a caller can call this out of a
///     transaction and in one.
/// </remarks>
[PublicAPI]
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
    ///     The captures are for the parts that this code makes with the value, and that the value does
    ///     not give. Examples are a sink that sends to it, and an internal cell to monitor. No code can
    ///     get these after the function returns.
    ///     <typeparamref name="TCaptures" /> is inferred from the function.
    ///     <typeparamref name="T" /> is named on <see cref="ForwardReference{T}" /> itself,
    ///     which is what leaves it free: a lambda gives type inference nothing to work from, and C#
    ///     does not accept only some of the type arguments of a method. Thus, with the two names here,
    ///     a caller writes the two at each call.
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
