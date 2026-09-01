/// <summary>
///     Creating behaviors and combining them.
/// </summary>
/// <remarks>
///     A behavior is a value that varies over time. It always has a value, so it can be sampled
///     at any moment, but it does not expose when it changes.
///
///     A cell is a behavior which also exposes the stream of its own changes; see <c>Cell</c> for
///     the same operations plus the ones built on those updates. Where the changes are not needed,
///     a behavior is the cheaper choice.
///
///     Build the graph inside <c>Transaction.run</c> so that no first firing is missed.
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
///     For a constant that is expensive to produce, or that is not yet available when the graph
///     is being built - the value is forced only when the behavior is first sampled.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let constantLazy value = BehaviorInternal.ConstantLazyImpl value

/// <summary>
///     Builds a behavior which refers to itself, closing the loop within one transaction.
/// </summary>
/// <param name="f">
///     Given the forward reference, returns a struct tuple of the behavior it stands for and
///     anything else the caller wants back out.
/// </param>
/// <returns>
///     A struct tuple of the behavior the forward reference was closed with, and whatever
///     <paramref name="f" /> returned alongside it.
/// </returns>
/// <remarks>
///     A behavior defined in terms of itself needs a forward reference to exist before the value
///     it refers to does. Both the reference and its resolution must happen in a single
///     transaction, which this opens if none is running.
///
///     Use <c>loopWithNoCaptures</c> where nothing but the behavior itself is needed.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let loop f =
    TransactionInternal.Apply
        (fun transaction _ ->
            let l = LoopedBehavior ()
            let struct (s, r) = f l
            l.Loop (transaction, s)
            struct (s, r))

/// <summary>
///     Builds a self-referential behavior where nothing but the behavior itself is wanted back.
/// </summary>
/// <param name="f">Given the forward reference, returns the behavior it stands for.</param>
/// <returns>The behavior the forward reference was closed with.</returns>
/// <remarks>
///     <c>loop</c> where something more than the behavior needs to escape the loop.
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
///     May be used inside the functions passed to the primitives that apply them to streams, where
///     it means the same as snapshotting. Outside a transaction it opens one of its own, so the
///     value read is never a half-updated one.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sample (behavior : Behavior<_>) = behavior.SampleImpl ()

/// <summary>
///     Gets a behavior's value as of now, without forcing it yet.
/// </summary>
/// <param name="behavior">The behavior to sample.</param>
/// <returns>A lazy value which yields what the behavior held at the moment of this call.</returns>
/// <remarks>
///     The value is pinned now and computed later. This is what <c>Stream.holdLazy</c> and the
///     looping constructs need: at the point a loop is being closed the value is not yet known,
///     but which moment it is to be taken from already is.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sampleLazy (behavior : Behavior<_>) = behavior.SampleLazyImpl ()

/// <summary>
///     Transforms a behavior with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="behavior">The behavior to transform.</param>
/// <returns>
///     A behavior whose value is <paramref name="f" /> applied to the input behavior's current
///     value.
/// </returns>
/// <remarks>
///     <paramref name="f" /> may construct FRP logic or sample behaviors and cells; apart from
///     that it must be pure, since it may be called more than once for one input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (behavior : Behavior<_>) = behavior.MapImpl (Func<_,_> f)

/// <summary>
///     Applies a behavior of functions to a behavior of values.
/// </summary>
/// <param name="f">The behavior holding the function to apply.</param>
/// <param name="behavior">The behavior holding the value to apply it to.</param>
/// <returns>
///     A behavior whose value is the current function in <paramref name="f" /> applied to the
///     input behavior's current value.
/// </returns>
/// <remarks>
///     The primitive all of the <c>lift</c> functions are built from. Reach for <c>lift2</c> and
///     its siblings first; this is for the cases they do not cover.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let apply f (behavior : Behavior<_>) = behavior.ApplyImpl (f |> map (fun f -> Func<_,_> f))

/// <summary>
///     Combines two behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <returns>
///     A behavior whose value is <paramref name="f" /> applied to the current values of the two
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 f ((behavior : Behavior<_>), behavior2) = behavior.LiftImpl (behavior2, Func<_,_,_> f)

/// <summary>
///     Combines three behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <returns>
///     A behavior whose value is <paramref name="f" /> applied to the current values of the three
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3 f ((behavior : Behavior<_>), behavior2, behavior3) = behavior.LiftImpl (behavior2, behavior3, Func<_,_,_,_> f)

/// <summary>
///     Combines four behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <returns>
///     A behavior whose value is <paramref name="f" /> applied to the current values of the four
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4 f ((behavior : Behavior<_>), behavior2, behavior3, behavior4) = behavior.LiftImpl (behavior2, behavior3, behavior4, Func<_,_,_,_,_> f)

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
///     A behavior whose value is <paramref name="f" /> applied to the current values of the five
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5 f ((behavior : Behavior<_>), behavior2, behavior3, behavior4, behavior5) = behavior.LiftImpl (behavior2, behavior3, behavior4, behavior5, Func<_,_,_,_,_,_> f)

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
///     A behavior whose value is <paramref name="f" /> applied to the current values of the six
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6 f ((behavior : Behavior<_>), behavior2, behavior3, behavior4, behavior5, behavior6) = behavior.LiftImpl (behavior2, behavior3, behavior4, behavior5, behavior6, Func<_,_,_,_,_,_,_> f)

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
///     A behavior whose value is <paramref name="f" /> applied to the current values of the seven
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
let lift7 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7) =
    ((behavior, behavior2, behavior3, behavior4, behavior5, behavior6) |> lift6 tuple6S, behavior7) |> lift2 (fun struct (a, b, c, d, e, f') g -> f a b c d e f' g)

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
///     A behavior whose value is <paramref name="f" /> applied to the current values of the eight
///     inputs.
/// </returns>
/// <remarks>
///     Glitch-free: when several of the inputs change in one transaction, the result updates
///     once, with all of the new values, rather than once per input.
/// </remarks>
let lift8 f (behavior, behavior2, behavior3, behavior4, behavior5, behavior6, behavior7, behavior8) =
    ((behavior, behavior2, behavior3, behavior4, behavior5, behavior6) |> lift6 tuple6S, behavior7, behavior8) |> lift3 (fun struct (a, b, c, d, e, f') g h -> f a b c d e f' g h)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllCollection f (behaviors : IReadOnlyCollection<'Behavior>) = BehaviorExtensionMethodsInternal.LiftBehaviorsImpl (behaviors, (Func<_,_> f))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllSeq f (behaviors : seq<'Behavior>) = BehaviorExtensionMethodsInternal.LiftBehaviorsImpl (behaviors, (Func<_,_> f))

/// <summary>
///     Combines any number of behaviors into one whose value is a function of all of theirs.
/// </summary>
/// <param name="f">Combines the current values, given in the order the behaviors were supplied.</param>
/// <param name="behaviors">The behaviors to combine.</param>
/// <returns>A behavior whose value is <paramref name="f" /> applied to all of the current values.</returns>
/// <remarks>
///     The <c>lift</c> family where the number of inputs is not known until run time. Glitch-free
///     in the same way: however many of the inputs change in one transaction, the result updates
///     once.
/// </remarks>
let liftAll f (behaviors : seq<'Behavior>) =
    match behaviors with
    | :? IReadOnlyCollection<'Behavior> as behaviors -> liftAllCollection f behaviors
    | behaviors -> liftAllSeq f behaviors

/// <summary>
///     Unwraps a behavior of behaviors into a behavior which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior holding another behavior.</param>
/// <returns>A behavior whose value is the current value of the currently held behavior.</returns>
/// <remarks>
///     This is how a graph changes shape at run time: the outer behavior chooses which inner one
///     is being followed.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchB behavior = BehaviorExtensionMethodsInternal.SwitchBImpl behavior

/// <summary>
///     Unwraps a behavior of cells into a cell which follows whichever one is current.
/// </summary>
/// <param name="behavior">The behavior holding a cell.</param>
/// <returns>A cell whose value is the current value of the currently held cell.</returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchC behavior = BehaviorExtensionMethodsInternal.SwitchCImpl behavior

/// <summary>
///     Unwraps a behavior of streams into a stream which fires whatever the current one fires.
/// </summary>
/// <param name="behavior">The behavior holding a stream.</param>
/// <returns>A stream firing the firings of the currently held stream.</returns>
/// <remarks>
///     On the transaction where the behavior changes, the firing taken is the one from the stream
///     held at the start of that transaction, not the newly selected one.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchS behavior = BehaviorExtensionMethodsInternal.SwitchSImpl behavior
