using System;

namespace SodaFlow;

internal static class CoalesceHandler
{
    internal static Action<TransactionInternal, T> Create<T>(Func<T, T, T> f, Stream<T> @out)
    {
        bool accumValid = false;
        T? accum = default;

        // A transaction that fails can stop before the send runs. Then accumValid must go back to
        // false, because the handler queues the send only when it is false. Otherwise, each
        // subsequent value goes into the accumulator and the stream does not fire again. This
        // captures no argument of the handler, thus the handler builds it one time and not in each
        // transaction.
        Action discard =
            () =>
            {
                accumValid = false;
                accum = default;
            };

        return (trans1, a) =>
        {
            if (accumValid)
            {
                // ReSharper disable once NullableWarningSuppressionIsUsed - accum will always have a non-null value
                // when accumValid is true.
                accum = f(arg1: accum!, arg2: a);
            }
            else
            {
                accum = a;
                accumValid = true;

                trans1.Prioritized(
                    node: @out.Node,
                    action: trans2 =>
                    {
                        // ReSharper disable once AccessToModifiedClosure
                        @out.Send(trans: trans2, a: accum);
                        accumValid = false;
                        accum = default;
                    },
                    discard: discard);
            }
        };
    }
}
