using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SodaFlow;

namespace SodaFlow.Analyzers.ListenerShapes;

// Each method is one shape of a listener. The comment SF2001 at the end of a line says that the
// rule reports the call on that line. ListenerAnalyzerTests reads these comments, runs the
// analyzer, and then runs each shape. A shape gets a sink and a list. It listens with the Add of
// the list, and then Probe.Fire collects and sends 1. Thus, the list has 1 when the listener lived
// to the send. Each shape gets the full optimization of the JIT at its first call. Without it, the
// JIT keeps each local alive to the end of the method, and a local that nothing reads still holds
// its listener. This file is test data: the harness compiles it and the analyzer reads it, and the
// project does not compile it.
public static class ListenerShapes
{
    // L01. A statement of the listen alone, which drops its result.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenAsAStatement(StreamSink<int> s, List<int> seen)
    {
        s.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L02. The same with ListenOnce.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenOnceAsAStatement(StreamSink<int> s, List<int> seen)
    {
        s.ListenOnce(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L03. The same on a cell.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void CellListenAsAStatement(StreamSink<int> s, List<int> seen)
    {
        s.Hold(0).Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L04. The same at the end of a chain of operators.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenOnAMappedStream(StreamSink<int> s, List<int> seen)
    {
        s.Map(static x => x).Filter(static _ => true).Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L05. A discard.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoADiscard(StreamSink<int> s, List<int> seen)
    {
        _ = s.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L06. A local that nothing reads.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalThatNothingReads(StreamSink<int> s, List<int> seen)
    {
        var listener = s.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L07. The same, with the type of the local written.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoATypedLocalThatNothingReads(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L08. A local with the name _, which is a local and not a discard.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalNamedUnderscore(StreamSink<int> s, List<int> seen)
    {
        var _ = s.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L09. A local that is set after its declaration, and that nothing reads.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalSetLater(StreamSink<int> s, List<int> seen)
    {
        IListener listener;
        listener = s.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L10. A local that gets a second listener before a read. The read is of the second one.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalThatIsSetAgain(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add); // SF2001
        listener = s.Listen(static _ => { });
        Probe.Fire(s);
        listener.Unlisten();
    }

    // L11. A ListenOnce into a local that nothing reads.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenOnceIntoALocalThatNothingReads(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.ListenOnce(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L12. The body of a lambda whose delegate returns nothing.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenInAnExpressionLambda(StreamSink<int> s, List<int> seen)
    {
        Action listen = () => s.Listen(seen.Add); // SF2001
        listen();
        Probe.Fire(s);
    }

    // L13. The same as a statement of the lambda.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenInAStatementLambda(StreamSink<int> s, List<int> seen)
    {
        Action listen = () =>
        {
            s.Listen(seen.Add); // SF2001
        };
        listen();
        Probe.Fire(s);
    }

    // L14. The body of a local function that returns nothing.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenInALocalFunction(StreamSink<int> s, List<int> seen)
    {
        void Listen() => s.Listen(seen.Add); // SF2001
        Listen();
        Probe.Fire(s);
    }

    // L15. A conditional access.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenThroughAConditionalAccess(StreamSink<int> s, List<int> seen)
    {
        StreamSink<int>? sink = s;
        sink?.Listen(seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L16. A listen in the function of a transaction.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenInATransaction(StreamSink<int> s, List<int> seen)
    {
        Transaction.RunVoid(() => s.Listen(seen.Add)); // SF2001
        Probe.Fire(s);
    }

    // L17. The static form of the extension method.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenCalledAsAStaticMethod(StreamSink<int> s, List<int> seen)
    {
        StreamExtensionMethods.Listen(s, seen.Add); // SF2001
        Probe.Fire(s);
    }

    // L18. A local of a lambda that is set after its declaration, and that nothing reads.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalOfALambda(StreamSink<int> s, List<int> seen)
    {
        Action listen = () =>
        {
            IListener listener;
            listener = s.Listen(seen.Add); // SF2001
        };
        listen();
        Probe.Fire(s);
    }

    // K01. A local read after the send.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalReadAfter(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add);
        Probe.Fire(s);
        listener.Unlisten();
    }

    // K02. A local that GC.KeepAlive reads.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalKeptAlive(StreamSink<int> s, List<int> seen)
    {
        var listener = s.Listen(seen.Add);
        Probe.Fire(s);
        GC.KeepAlive(listener);
    }

    // K03. A local read on one path only, after the send.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalReadOnOnePath(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add);
        Probe.Fire(s);

        if (seen.Count == 0)
        {
            listener.Unlisten();
        }
    }

    // K04. A static field.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoAStaticField(StreamSink<int> s, List<int> seen)
    {
        Holder.Listener = s.Listen(seen.Add);
        Probe.Fire(s);
        Holder.Listener = null;
    }

    // K05. The field of an object that the shape keeps.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoTheFieldOfAKeptOwner(StreamSink<int> s, List<int> seen)
    {
        Owner owner = new(s: s, seen: seen);
        Probe.Fire(s);
        GC.KeepAlive(owner);
    }

    // K06. An element of a list that the shape keeps.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoAKeptList(StreamSink<int> s, List<int> seen)
    {
        List<IListener> keep = [s.Listen(seen.Add)];
        Probe.Fire(s);
        GC.KeepAlive(keep);
    }

    // K07. An argument.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenAsAnArgument(StreamSink<int> s, List<int> seen)
    {
        List<IListener> keep = [];
        keep.Add(s.Listen(seen.Add));
        Probe.Fire(s);
        GC.KeepAlive(keep);
    }

    // K08. A local that a lambda reads, and the shape runs the lambda after the send.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalThatALambdaReads(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add);
        Action stop = () => listener.Unlisten();
        Probe.Fire(s);
        stop();
    }

    // K09. A local read in a finally block.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalReadInAFinally(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add);

        try
        {
            Probe.Fire(s);
        }
        finally
        {
            listener.Unlisten();
        }
    }

    // K10. A local set in one pass of a loop and read in the next.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalReadInTheNextPass(StreamSink<int> s, List<int> seen)
    {
        IListener? listener = null;

        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 0)
            {
                listener = s.Listen(seen.Add);
                Probe.Fire(s);
            }
            else
            {
                listener?.Unlisten();
            }
        }
    }

    // K11. A lambda that returns the listener.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenAsTheResultOfALambda(StreamSink<int> s, List<int> seen)
    {
        Func<IListener> listen = () => s.Listen(seen.Add);
        IListener listener = listen();
        Probe.Fire(s);
        listener.Unlisten();
    }

    // K12. ListenOnceAsync roots its listener, and its result is a task.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenOnceAsyncIsRooted(StreamSink<int> s, List<int> seen)
    {
        Task<int> next = s.ListenOnceAsync();
        Probe.Fire(s);

        if (next.IsCompleted)
        {
            seen.Add(next.Result);
        }
    }

    // K13. A ListenOnce kept until its firing.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenOnceIntoALocalReadAfter(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.ListenOnce(seen.Add);
        Probe.Fire(s);
        GC.KeepAlive(listener);
    }

    // K14. A method of the project with the name Listen, which keeps the listener of the library.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenOfTheProject(StreamSink<int> s, List<int> seen)
    {
        Holder.Listen(s: s, handler: seen.Add);
        Probe.Fire(s);
        Holder.Listener = null;
    }

    // K15. A local that a later declarator of the same statement reads.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalReadInItsStatement(StreamSink<int> s, List<int> seen)
    {
        IListener first = s.Listen(seen.Add), second = first;
        Probe.Fire(s);
        GC.KeepAlive(second);
    }

    // K16. A local that a lambda made before the listen reads, and the shape runs the lambda after
    // the send.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalThatAnEarlierLambdaReads(StreamSink<int> s, List<int> seen)
    {
        IListener? listener = null;
        Action stop = () => listener?.Unlisten();
        listener = s.Listen(seen.Add);
        Probe.Fire(s);
        stop();
    }

    // K17. A local that the body of an expression lambda sets. The lambda captures the local.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalThatAnExpressionLambdaSets(StreamSink<int> s, List<int> seen)
    {
        IListener? listener = null;
        Action listen = () => listener = s.Listen(seen.Add);
        listen();
        Probe.Fire(s);
        GC.KeepAlive(listener);
    }

    // M01. The field of an owner that nothing holds. The rule does not see the owner.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoTheFieldOfADroppedOwner(StreamSink<int> s, List<int> seen)
    {
        _ = new Owner(s: s, seen: seen);
        Probe.Fire(s);
    }

    // M02. An element of a list that nothing holds.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoADroppedList(StreamSink<int> s, List<int> seen)
    {
        List<IListener> keep = [s.Listen(seen.Add)];
        Probe.Fire(s);
    }

    // M03. A local that a lambda reads, and nothing holds the lambda.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ListenIntoALocalThatADroppedLambdaReads(StreamSink<int> s, List<int> seen)
    {
        IListener listener = s.Listen(seen.Add);
        Action stop = () => listener.Unlisten();
        Probe.Fire(s);
    }
}

public static class Holder
{
    public static IListener? Listener;

    public static IListener Listen(StreamSink<int> s, Action<int> handler) => Listener = s.Listen(handler);
}

public sealed class Owner
{
    private readonly IListener listener;

    public Owner(StreamSink<int> s, List<int> seen) => this.listener = s.Listen(seen.Add);

    public void Stop() => this.listener.Unlisten();
}

public static class Probe
{
    // Collects, so that a listener that nothing holds goes, and then sends 1.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fire(StreamSink<int> s)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        s.Send(1);
    }
}
