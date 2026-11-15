using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The exception that SodaFlow throws when the deferred work of one
///     transaction makes more deferred work too many times in sequence.
/// </summary>
/// <remarks>
///     <para>
///         A posted action, and each item of a split, runs in a transaction of its
///         own after the transaction that deferred it. That transaction can defer
///         work also. A value can go back to its own source through
///         <c>Operational.Defer</c>, <c>Operational.Split</c>, or
///         <c>Transaction.Post</c>. With nothing that stops it, the chain has no
///         end. Before, the call that started it did not return, and nothing threw.
///     </para>
///     <para>
///         The limit is on the depth of the chain. A split of one long list is one
///         step of the chain. The number of items in the list does not change that.
///     </para>
///     <para>
///         The exception comes out of the call that started the first transaction.
///         That transaction committed, and so did each deferred transaction before
///         the limit. Their values stay in the graph. The work that remained at the
///         limit did not run. To correct the cause, give the chain an end. A
///         <c>Calm</c> stops a value that does not change. A filter stops a value
///         that is complete.
///     </para>
///     <para>
///         It is an <see cref="InvalidOperationException" />, as each other failure
///         of the library is.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class DeferredChainLimitException : InvalidOperationException
{
    internal const string DefaultMessage = "The deferred work of a transaction did not end.";

    /// <inheritdoc />
    /// <remarks>The message is the default one.</remarks>
    public DeferredChainLimitException()
        : base(DefaultMessage)
    {
    }

    /// <inheritdoc />
    public DeferredChainLimitException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public DeferredChainLimitException(string message, Exception innerException)
        : base(message: message, innerException: innerException)
    {
    }

    // The message for a chain that came to the given limit.
    internal static string MessageFor(int limit) =>
        $"{DefaultMessage} It made more deferred work {limit} times in sequence, which is the limit. A value "
        + "that goes back to its own source through Operational.Defer, Operational.Split, or Transaction.Post does "
        + "this when nothing stops it. A Calm or a filter in the chain gives it an end.";
}
