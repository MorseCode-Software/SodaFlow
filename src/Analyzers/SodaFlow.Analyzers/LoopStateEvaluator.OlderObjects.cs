using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for an object that a call gives and that the walk did not see made. A
// second call can give the same object. Thus, a write through one name of such an object goes to the
// fields of the region also. A read through a second name can get it there.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when the value can be an object that is older than the code that
    ///     gives it. The result of a call or of a member is such an object, when
    ///     the walk did not see the creation. An object that the store does not
    ///     know is one also.
    /// </summary>
    private static bool CanBeAnOlderObject(LoopState value) => value.Objects.Any(static one => !one.IsNew);

    /// <summary>
    ///     The object of the result of a call, which the store did not know. The
    ///     region keeps it as an older object when the walk read the callee.
    /// </summary>
    /// <remarks>
    ///     The walk read the callee and found no creation for the result. Thus,
    ///     the result is an object that a different call can give also. The walk
    ///     does not read a callee that gets no state, and such a callee can make
    ///     its result.
    /// </remarks>
    private LoopState NotesAnOlderResult(SyntaxNode call, LoopState made)
    {
        // The evaluator followed the call before it made the object, thus it keeps the answer.
        if (!ReferenceEquals(objA: this.Follow(call), objB: Followed.Nothing))
        {
            this.region.NotesOlderObjects(made.Objects);
        }

        return made;
    }

    /// <summary>
    ///     True when a read of the member through the owner also gets the state
    ///     that the region has for the field. The owner can be the result of a
    ///     call that the walk read. No write through this name gave it a state
    ///     for the member.
    /// </summary>
    /// <remarks>
    ///     A write through this name keeps its own state, which the next write
    ///     through it replaces. Thus, the read joins the region only when the
    ///     name has no such state.
    /// </remarks>
    private bool ReadsAnOlderObject(LoopState owner, ISymbol member, LoopState field) =>
        !field.IsNone
        && owner.Objects.Any(this.region.IsAnOlderObject)
        && owner.Members?.Has(member) != true;
}
