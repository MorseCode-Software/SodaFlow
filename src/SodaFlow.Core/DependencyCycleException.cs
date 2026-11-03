using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     The exception that SodaFlow throws when a node depends on itself in the
///     same transaction, with no delay between the two ends of the cycle.
/// </summary>
/// <remarks>
///     <para>
///         A loop that closes on a map of its own output is the usual cause. A
///         <c>Hold</c> or a <c>Snapshot</c> on the way back makes the cycle legal.
///         SodaFlow finds the cycle when it gives the ranks to the nodes. That is at
///         the construction of the loop, and not at the first send.
///     </para>
///     <para>
///         A cycle is a defect in the construction of the graph, and no code catches
///         this exception to continue. The type lets code that builds a graph catch
///         a cycle apart from each other failure. It also lets a test assert the
///         type and not the message. It is an
///         <see cref="InvalidOperationException" />, as each other failure of the
///         library is.
///     </para>
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class DependencyCycleException : InvalidOperationException
{
    internal const string DefaultMessage = "A dependency cycle was detected.";

    /// <inheritdoc />
    /// <remarks>The message is the default one.</remarks>
    public DependencyCycleException()
        : base(DefaultMessage)
    {
    }

    /// <inheritdoc />
    public DependencyCycleException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public DependencyCycleException(string message, Exception innerException)
        : base(message: message, innerException: innerException)
    {
    }
}
