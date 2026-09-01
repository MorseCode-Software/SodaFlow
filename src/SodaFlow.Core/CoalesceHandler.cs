using System;

namespace SodaFlow
{
    internal static class CoalesceHandler
    {
        internal static Action<TransactionInternal, T> Create<T>(Func<T, T, T> f, Stream<T> @out)
        {
            bool accumValid = false;
            T accum = default;

            return (trans1, a) =>
            {
                if (accumValid)
                {
                    accum = f(arg1: accum, arg2: a);
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
                        });
                }
            };
        }
    }
}