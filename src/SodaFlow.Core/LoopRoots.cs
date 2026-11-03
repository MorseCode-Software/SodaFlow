using System;
using System.Linq;

namespace SodaFlow;

/// <summary>
///     Operations on the set of loop placeholders that a value depends on. Null is
///     the empty set, thus a value with no loop pays nothing.
/// </summary>
internal static class LoopRoots
{
    /// <summary>
    ///     Puts two sets together. A set that is in both comes one time.
    /// </summary>
    internal static ILoopRoot[]? Union(ILoopRoot[]? a, ILoopRoot[]? b)
    {
        if (a == null)
        {
            return b;
        }

        if (b == null || ReferenceEquals(objA: a, objB: b))
        {
            return a;
        }

        ILoopRoot[] extra = [.. b.Where(root => Array.IndexOf(array: a, value: root) < 0)];

        if (extra.Length == 0)
        {
            return a;
        }

        ILoopRoot[] result = new ILoopRoot[a.Length + extra.Length];
        a.CopyTo(array: result, index: 0);
        extra.CopyTo(array: result, index: a.Length);

        return result;
    }

    /// <summary>
    ///     Throws an exception when a loop in the set is not closed.
    /// </summary>
    /// <exception cref="InvalidOperationException">A loop is not closed.</exception>
    internal static void ThrowIfOpen(ILoopRoot[]? roots)
    {
        if (roots != null && roots.Any(static root => !root.IsClosed))
        {
            throw new InvalidOperationException(
                "The loop that this value depends on is not closed. Close the loop before this conversion, or "
                + "assert the conversion with AsCell or AsBehavior.");
        }
    }
}
