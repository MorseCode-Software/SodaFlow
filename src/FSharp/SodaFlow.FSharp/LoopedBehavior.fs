/// <summary>
///     Operations on a behavior that can be in a loop that is not closed.
/// </summary>
/// <remarks>
///     A <c>LoopedBehavior</c> has each operation of a behavior except a read of the current
///     value. A loop block gives one as the placeholder. Each operation here starts
///     its result from the current value of its input, which is not available before the loop
///     closes, thus the result is a looped behavior too. The functions in <c>Behavior</c> take a
///     <c>Behavior</c>, which is a looped behavior that has a value now.
///
///     <c>close</c> gives the behavior back when the loop is closed, with a check.
///     <c>asBehavior</c> asserts it before the close.
/// </remarks>
module SodaFlow.LoopedBehavior

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

/// <summary>
///     Asserts that no code reads the looped behavior before its loop closes, and gives it as a
///     behavior.
/// </summary>
/// <param name="behavior">The looped behavior.</param>
/// <returns>The same value as a behavior.</returns>
/// <remarks>
///     Use this to give a placeholder to code that keeps the behavior and reads it after the close. The
///     types cannot see that the read comes after the close, thus this call is the assertion. A
///     read before the close throws an exception. Where the loop is closed, use <c>close</c>,
///     which checks it.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let asBehavior (behavior: LoopedBehavior<_>) = behavior.AsBehaviorImpl()

/// <summary>
///     Gives the looped behavior as a behavior, after a check that each loop it depends on has
///     closed.
/// </summary>
/// <param name="behavior">The looped behavior.</param>
/// <returns>The same value as a behavior.</returns>
/// <exception cref="InvalidOperationException">A loop is not closed.</exception>
/// <remarks>
///     A loop block returns a looped behavior in its captures, and the type stays looped
///     after the block returns and the loop closes. This gives the behavior back, with a check and
///     not the assertion that <c>asBehavior</c> makes.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let close (behavior: LoopedBehavior<_>) = behavior.CloseImpl()

/// <summary>
///     Transforms a looped behavior with a function.
/// </summary>
/// <param name="f">Transforms the value.</param>
/// <param name="behavior">The looped behavior to transform.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on the input's value.
/// </returns>
/// <remarks>
///     See <c>Behavior.map</c> for the contract of <paramref name="f" />.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let map f (behavior: LoopedBehavior<_>) : LoopedBehavior<_> =
    behavior.MapImpl(Func<_, _> f) :> LoopedBehavior<_>

/// <summary>
///     Applies a looped behavior of functions to a looped behavior of values.
/// </summary>
/// <param name="f">The looped behavior holding the function to apply.</param>
/// <param name="behavior">The looped behavior holding the value to apply it to.</param>
/// <returns>
///     A looped behavior whose value is the result of the current function in <paramref name="f" />
///     on the input's value.
/// </returns>
/// <remarks>
///     See <c>Behavior.apply</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let apply f (behavior: LoopedBehavior<_>) : LoopedBehavior<_> =
    behavior.ApplyImpl(f |> map (fun f -> Func<_, _> f)) :> LoopedBehavior<_>

/// <summary>
///     Combines two behaviors, of which one or more is looped, into one looped behavior.
/// </summary>
/// <param name="f">Combines the two current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on the two inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift2</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift2 f (behavior: LoopedBehavior<_>, behavior2: LoopedBehavior<_>) : LoopedBehavior<_> =
    behavior.LiftImpl(behavior2, Func<_, _, _> f) :> LoopedBehavior<_>

/// <summary>
///     Combines three behaviors, of which one or more is looped, into one looped behavior.
/// </summary>
/// <param name="f">Combines the three current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on the three inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift3</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift3
    f
    (behavior: LoopedBehavior<_>, behavior2: LoopedBehavior<_>, behavior3: LoopedBehavior<_>)
    : LoopedBehavior<_> =
    behavior.LiftImpl(behavior2, behavior3, Func<_, _, _, _> f) :> LoopedBehavior<_>

/// <summary>
///     Combines four behaviors, of which one or more is looped, into one looped behavior.
/// </summary>
/// <param name="f">Combines the four current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on the four inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift4</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift4
    f
    (behavior: LoopedBehavior<_>,
     behavior2: LoopedBehavior<_>,
     behavior3: LoopedBehavior<_>,
     behavior4: LoopedBehavior<_>)
    : LoopedBehavior<_> =
    behavior.LiftImpl(behavior2, behavior3, behavior4, Func<_, _, _, _, _> f) :> LoopedBehavior<_>

/// <summary>
///     Combines five behaviors, of which one or more is looped, into one looped behavior.
/// </summary>
/// <param name="f">Combines the five current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on the five inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift5</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift5
    f
    (behavior: LoopedBehavior<_>,
     behavior2: LoopedBehavior<_>,
     behavior3: LoopedBehavior<_>,
     behavior4: LoopedBehavior<_>,
     behavior5: LoopedBehavior<_>)
    : LoopedBehavior<_> =
    behavior.LiftImpl(behavior2, behavior3, behavior4, behavior5, Func<_, _, _, _, _, _> f)
    :> LoopedBehavior<_>

/// <summary>
///     Combines six behaviors, of which one or more is looped, into one looped behavior.
/// </summary>
/// <param name="f">Combines the six current values into the result.</param>
/// <param name="behavior">The first behavior.</param>
/// <param name="behavior2">The second behavior.</param>
/// <param name="behavior3">The third behavior.</param>
/// <param name="behavior4">The fourth behavior.</param>
/// <param name="behavior5">The fifth behavior.</param>
/// <param name="behavior6">The sixth behavior.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on the six inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift6</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift6
    f
    (behavior: LoopedBehavior<_>,
     behavior2: LoopedBehavior<_>,
     behavior3: LoopedBehavior<_>,
     behavior4: LoopedBehavior<_>,
     behavior5: LoopedBehavior<_>,
     behavior6: LoopedBehavior<_>)
    : LoopedBehavior<_> =
    behavior.LiftImpl(behavior2, behavior3, behavior4, behavior5, behavior6, Func<_, _, _, _, _, _, _> f)
    :> LoopedBehavior<_>

/// <summary>
///     Combines seven behaviors, of which one or more is looped, into one looped behavior.
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
///     A looped behavior whose value is the result of <paramref name="f" /> on the seven inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift7</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift7
    f
    (behavior: LoopedBehavior<_>,
     behavior2: LoopedBehavior<_>,
     behavior3: LoopedBehavior<_>,
     behavior4: LoopedBehavior<_>,
     behavior5: LoopedBehavior<_>,
     behavior6: LoopedBehavior<_>,
     behavior7: LoopedBehavior<_>)
    : LoopedBehavior<_> =
    behavior.LiftImpl(
        behavior2,
        behavior3,
        behavior4,
        behavior5,
        behavior6,
        behavior7,
        Func<_, _, _, _, _, _, _, _> f
    )
    :> LoopedBehavior<_>

/// <summary>
///     Combines eight behaviors, of which one or more is looped, into one looped behavior.
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
///     A looped behavior whose value is the result of <paramref name="f" /> on the eight inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.lift8</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let lift8
    f
    (behavior: LoopedBehavior<_>,
     behavior2: LoopedBehavior<_>,
     behavior3: LoopedBehavior<_>,
     behavior4: LoopedBehavior<_>,
     behavior5: LoopedBehavior<_>,
     behavior6: LoopedBehavior<_>,
     behavior7: LoopedBehavior<_>,
     behavior8: LoopedBehavior<_>)
    : LoopedBehavior<_> =
    behavior.LiftImpl(
        behavior2,
        behavior3,
        behavior4,
        behavior5,
        behavior6,
        behavior7,
        behavior8,
        Func<_, _, _, _, _, _, _, _, _> f
    )
    :> LoopedBehavior<_>

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllCollection f (behaviors: IReadOnlyCollection<'Behavior>) =
    BehaviorExtensionMethodsInternal.LiftBehaviorsImpl(behaviors, (Func<_, _> f))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private liftAllSeq f (behaviors: seq<'Behavior>) =
    BehaviorExtensionMethodsInternal.LiftBehaviorsImpl(behaviors, (Func<_, _> f))

/// <summary>
///     Combines any number of behaviors, of which one or more is looped, into one looped
///     behavior.
/// </summary>
/// <param name="f">Combines the current values, in the sequence of the behaviors.</param>
/// <param name="behaviors">The behaviors to put together.</param>
/// <returns>
///     A looped behavior whose value is the result of <paramref name="f" /> on all the inputs.
/// </returns>
/// <remarks>
///     See <c>Behavior.liftAll</c>.
/// </remarks>
let liftAll f (behaviors: seq<'Behavior>) : LoopedBehavior<_> =
    match behaviors with
    | :? IReadOnlyCollection<'Behavior> as behaviors -> liftAllCollection f behaviors :> LoopedBehavior<_>
    | behaviors -> liftAllSeq f behaviors :> LoopedBehavior<_>

/// <summary>
///     Unwraps a looped behavior of behaviors into a looped behavior which follows the current
///     one.
/// </summary>
/// <param name="behavior">The looped behavior that holds a second behavior.</param>
/// <returns>A looped behavior whose value is the current value of the current behavior.</returns>
/// <remarks>
///     See <c>Behavior.switchB</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchB (behavior: LoopedBehavior<_>) : LoopedBehavior<_> =
    BehaviorExtensionMethodsInternal.SwitchBImpl behavior :> LoopedBehavior<_>

/// <summary>
///     Unwraps a looped behavior of cells into a looped cell which follows the current one.
/// </summary>
/// <param name="behavior">The looped behavior holding a cell.</param>
/// <returns>A looped cell whose value is the current value of the current cell.</returns>
/// <remarks>
///     See <c>Behavior.switchC</c>.
/// </remarks>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchC (behavior: LoopedBehavior<_>) : LoopedCell<_> =
    BehaviorExtensionMethodsInternal.SwitchCImpl behavior :> LoopedCell<_>
