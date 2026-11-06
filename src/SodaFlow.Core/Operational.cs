using System.Collections.Generic;

namespace SodaFlow;

internal static class OperationalInternal
{
    internal static Stream<T> UpdatesImpl<T>(Behavior<T> b) =>
        TransactionInternal.Apply(
            code: static (trans, _, b) => b.Updates().Coalesce(trans1: trans, f: static (_, right) => right),
            state: b);

    internal static Stream<T> ValueImpl<T>(Behavior<T> b) =>
        TransactionInternal.Apply(code: static (trans, _, b) => b.Value(trans), state: b);

    internal static Stream<T> DeferImpl<T>(Stream<T> s) => SplitImpl<T, T[]>(s.MapImpl(static a => new[] { a }));

    internal static Stream<T> SplitImpl<T, TCollection>(Stream<TCollection> s)
        where TCollection : IEnumerable<T>
    {
        Stream<T> @out = new(s.KeepListenersAlive);

        return @out.UnsafeAttachListener(
            s.Listen(target: new Node<T>(), action: new SplitAction<T, TCollection>(@out)));
    }

    private sealed class SplitAction<T, TCollection>(in Stream<T> @out) : StreamAction<TCollection>
        where TCollection : IEnumerable<T>
    {
        private readonly Stream<T> @out = @out;

        public override void Fire(TransactionInternal trans, TCollection value)
        {
            int childIx = 0;

            foreach (T a in value)
            {
                trans.Split(index: childIx, action: trans1 => this.@out.Send(trans: trans1, a: a));
                childIx++;
            }
        }
    }
}
