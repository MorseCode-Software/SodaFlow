using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     Constructs a value that can refer to itself during its own construction.
/// </summary>
/// <typeparam name="T">The type of the value to make.</typeparam>
/// <remarks>
///     A cell loop gives a reference to a cell before the cell exists, and the cell that the
///     reference means closes the loop. This is the same, but it removes the cell. The function
///     gives one value and not a cell of changing values, and a constant cell closes the loop that
///     the function gets. Thus, the reference resolves to that value and never changes.
///     The two members here operate the same, and each closes a cell loop with a constant cell. That is
///     what makes this the loop with one value: the reference resolves to the value the
///     function produced and has nothing more to say.
///     Use this for the cycle between two objects where each one needs the other one at its construction.
///     Without this, the code must make one object incomplete and complete it after the other one.
///     That needs a settable member, which is incorrect after the code builds the graph.
///     For example:
///     <code>
///     Node node = ForwardReference&lt;Node&gt;.WithoutCaptures(
///         reference =&gt; new Node(new Child(reference.AsCell())));
///     </code>
///     The child holds a <see cref="Cell{T}" /> with no meaning until the call returns. It holds
///     the finished node after that. Nothing has to be mutable. There is also no window where a
///     half-built node is reachable, because no code can sample the reference before the
///     construction function returns.
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
    ///     The function gives <typeparamref name="TCaptures" /> to type inference.
    ///     <see cref="ForwardReference{T}" /> itself names <typeparamref name="T" />, which is what
    ///     leaves it free. A lambda gives type inference nothing to work from, and C# does not accept
    ///     only some of the type arguments of a method. Thus, with the two names here, a caller writes
    ///     the two at each call.
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
    ///     <see cref="ForwardReference{T}" /> itself names <typeparamref name="T" />,
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
