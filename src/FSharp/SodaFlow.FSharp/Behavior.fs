/// <summary>
///     Creating behaviors and combining them.
/// </summary>
/// <remarks>
///     A behavior is a value that changes with time. It always has a value, thus code can sample it
///     at each moment, but it does not give the times when it changes.
///
///     A cell is a behavior that also gives the stream of its own changes. See <c>Cell</c> for the
///     same operations and the operations that use those updates. Where the changes are not necessary,
///     a behavior has a lower cost.
///
///     Build the graph in <c>Transaction.run</c> so the graph keeps the first firing.
/// </remarks>
module SodaFlow.Behavior

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

/// <summary>
///     Creates a behavior with a value that never changes.
/// </summary>
/// <param name="value">The value the behavior always has.</param>
/// <returns>A behavior whose value is always <paramref name="value" />.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let constant value = BehaviorInternal.ConstantImpl value

/// <summary>
///     Creates a behavior with a value that never changes, computed on first use.
/// </summary>
/// <param name="value">The lazy value the behavior always has.</param>
/// <returns>A behavior whose value is always the value of <paramref name="value" />.</returns>
/// <remarks>
///     For a constant with a high cost, or that is not available when the code builds the graph.
///     The code forces the value only at the first sample of the behavior.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let constantLazy value = BehaviorInternal.ConstantLazyImpl value

/// <summary>
///     Builds a behavior which refers to itself, closing the loop in one transaction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the behavior it stands for and
///     anything else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the behavior that closed the forward reference, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     A behavior that refers to itself needs a forward reference. The code makes the reference
///     before the value that it refers to. The reference and its resolution must occur in one
///     transaction, which this opens when no transaction is open.
///
///     Use <c>loopWithNoCaptures</c> where the caller needs only the behavior.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let loop f =
    TransactionInternal.Apply(fun transaction _ ->
        let l = LoopedBehavior()
        let struct (s, r) = f l
        l.Loop(transaction, s)
        struct (s, r))

/// <summary>
///     Builds a self-referential behavior where the caller needs only the behavior.
/// </summary>
/// <param name="f">Given the forward reference, returns the behavior it stands for.</param>
/// <returns>The behavior that closed the forward reference.</returns>
/// <remarks>
///     <c>loop</c> where the caller needs more than the behavior from the loop.
/// </remarks>
let loopWithNoCaptures f =
    let struct (l, _) = loop (fun s -> struct (f s, ()))
    l

/// <summary>
///     Gets a behavior's current value.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>The value the behavior has at this moment.</returns>
/// <remarks>
///     The functions that the primitives give to a stream can call this, and there it is the same as
///     a snapshot. With no transaction it opens its own transaction, thus a read never gives a value
///     in the middle of an update.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sample (behavior: Behavior<_>) = behavior.SampleImpl()

/// <summary>
///     Gets the current value of a behavior, and does not force it.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>A lazy value which yields what the behavior held at the moment of this call.</returns>
/// <remarks>
///     This code sets the value now and calculates it after this. This is necessary for
///     <c>Stream.holdLazy</c> and the loop constructs. At the moment that the code closes a loop,
///     the value is not known, but the moment of the value is known.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sampleLazy (behavior: Behavior<_>) = behavior.SampleLazyImpl()

/// <summary>
///     Transforms a behavior with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="behavior">The behavior to transform.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the input behavior's current
///     value.
/// </returns>
/// <remarks>
///     <paramref name="f" /> can build FRP logic, and it can sample a behavior and a cell. It must
///     be pure in each other operation, because this code can call it more than one time for one
///     input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (behavior: Behavior<_>) = behavior.MapImpl(Func<_, _> f)

/// <summary>
///     Applies a behavior of functions to a behavior of values.
/// </summary>
/// <param name="f">The behavior holding the function to apply.</param>
/// <param name="behavior">The behavior holding the value to apply it to.</param>
/// <returns>
///     A behavior whose value is the result of the current function in <paramref name="f" /> on the
///     input behavior's current value.
/// </returns>
/// <remarks>
///     This is the primitive of all the <c>lift</c> functions. Use <c>lift2</c> and the other
///     <c>lift</c> functions first. Use this function only for the conditions that they do not cover.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let apply f (behavior: Behavior<_>) =
    behavior.ApplyImpl(f |> map (fun f -> Func<_, _> f))

/// <summary>
///     Combines two behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the two
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 f (behavior: Behavior<_>, behavior2) =
    behavior.LiftImpl(behavior2, Func<_, _, _> f)

/// <summary>
///     Combines three behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the three
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3 f (behavior: Behavior<_>, behavior2, behavior3) =
    behavior.LiftImpl(behavior2, behavior3, Func<_, _, _, _> f)

/// <summary>
///     Combines four behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the four
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4 f (behavior: Behavior<_>, behavior2, behavior3, behavior4) =
    behavior.LiftImpl(behavior2, behavior3, behavior4, Func<_, _, _, _, _> f)

/// <summary>
///     Combines five behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the five current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the five
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5 f (behavior: Behavior<_>, behavior2, behavior3, behavior4, behavior5) =
    behavior.LiftImpl(behavior2, behavior3, behavior4, behavior5, Func<_, _, _, _, _, _> f)

/// <summary>
///     Combines six behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the six current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the six
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6 f (behavior: Behavior<_>, behavior2, behavior3, behavior4, behavior5, behavior6) =
    behavior.LiftImpl(behavior2, behavior3, behavior4, behavior5, behavior6, Func<_, _, _, _, _, _, _> f)

/// <summary>
///     Combines seven behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the seven current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <param name="behavior7">The seventh behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the seven
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
let lift7 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7) =
    ((behavior, behavior2, behavior3, behavior4, behavior5, behavior6)
     |> lift6 tuple6S,
     behavior7)
    |> lift2 (fun struct (a, b, c, d, e, f') g -> f a b c d e f' g)

/// <summary>
///     Combines eight behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the eight current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <param name="behavior7">The seventh behavior.</param>
/// <param name="behavior8">The eighth behavior.</param>
/// <returns>
///     A behavior whose value is the result of <paramref name="f" /> on the current values of the eight
///     inputs.
/// </returns>
/// <remarks>
///     There is no glitch. When some of the inputs change in one transaction, the result updates one
///     time, with each new value, and not one time for each input.
/// </remarks>
let lift8 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7, behavior8) =
    ((behavior, behavior2, behavior3, behavior4, behavior5, behavior6)
     |> lift6 tuple6S,
     behavior7,
     behavior8)
    |> lift3 (fun struct (a, b, c, d, e, f') g h -> f a b c d e f' g h)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllCollection f (behaviors: IReadOnlyCollection<'Behavior>) =
    BehaviorExtensionMethodsInternal.LiftBehaviorsImpl(behaviors, (Func<_, _> f))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllSeq f (behaviors: seq<'Behavior>) =
    BehaviorExtensionMethodsInternal.LiftBehaviorsImpl(behaviors, (Func<_, _> f))

/// <summary>
///     Combines any number of behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, in the sequence of the behaviors.</param>
/// <param name="behaviors">The behaviors to put together.</param>
/// <returns>A behavior whose value is the result of <paramref name="f" /> on all the current values.</returns>
/// <remarks>
///     The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
///     in the same manner. At each count of the inputs that change in one transaction, the result
///     updates one time.
/// </remarks>
let liftAll f (behaviors: seq<'Behavior>) =
    match behaviors with
    | :? IReadOnlyCollection<'Behavior> as behaviors -> liftAllCollection f behaviors
    | behaviors -> liftAllSeq f behaviors

/// <summary>
///     Unwraps a behavior of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior that holds a second behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
/// <remarks>
///     This is how a graph changes its shape at run time. The external behavior selects the
///     internal behavior to follow.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchB behavior =
    BehaviorExtensionMethodsInternal.SwitchBImpl behavior

/// <summary>
///     Unwraps a behavior of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior holding a cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchC behavior =
    BehaviorExtensionMethodsInternal.SwitchCImpl behavior

/// <summary>
///     Unwraps a behavior of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="behavior">The behavior holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
///     In the transaction where the behavior changes, the result takes the firing from the stream
///     at the start of that transaction. It does not use the firing from the new stream.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchS behavior =
    BehaviorExtensionMethodsInternal.SwitchSImpl behavior
