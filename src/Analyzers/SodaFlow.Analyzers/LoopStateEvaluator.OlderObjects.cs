using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    ///     does not read a callee that gets no state. Such a callee can make its
    ///     result, or give an object that it finds, as an element of an array in
    ///     a static field. Thus, its result is an older object too, where the code
    ///     around the call calls the method again. The second call must be able
    ///     to give the same object. That is not so where each return of the code
    ///     of the method gives the object of a creation.
    /// </remarks>
    private LoopState NotesAnOlderResult(SyntaxNode call, LoopState made)
    {
        // The evaluator followed the call before it made the object, thus it keeps the answer.
        if (!ReferenceEquals(objA: this.Follow(call), objB: Followed.Nothing)
            || (call is InvocationExpressionSyntax invocation
                && this.region.SymbolOf(call) is IMethodSymbol method
                && !this.region.Symbols.MakesItsResult(method.OriginalDefinition)
                && this.IsCalledAgain(invocation)))
        {
            this.region.NotesOlderObjects(made.Objects);
        }
        else if (call is InvocationExpressionSyntax unread
                 && this.region.SymbolOf(call) is IMethodSymbol helper
                 && !this.region.Symbols.MakesItsResult(helper.OriginalDefinition))
        {
            this.NotesAnUnreadResult(call: unread, made: made);
        }

        return made;
    }

    // True when the function of a call has a second call that can give the same object.
    private bool IsCalledAgain(InvocationExpressionSyntax call) =>
        FunctionOf(call) is { } code
        && code.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(other => other != call && this.CanGiveOneObject(one: call, other: other));

    // True when two calls of one method can give one object. They cannot where the walk knows two
    // different arguments at one position. Those are two different constants, or two static fields
    // that each keep the object of their creation.
    private bool CanGiveOneObject(InvocationExpressionSyntax one, InvocationExpressionSyntax other)
    {
        SeparatedSyntaxList<ArgumentSyntax> first = one.ArgumentList.Arguments;
        SeparatedSyntaxList<ArgumentSyntax> second = other.ArgumentList.Arguments;

        // The name comes first, because it needs no binding.
        if (NameOfTheCallee(one) != NameOfTheCallee(other)
            || first.Count != second.Count
            || this.region.SymbolOf(one) is not IMethodSymbol method
            || this.region.SymbolOf(other) is not IMethodSymbol again
            || !SymbolEqualityComparer.Default.Equals(x: method.OriginalDefinition, y: again.OriginalDefinition))
        {
            return false;
        }

        return !first.Where((argument, at) => this.Differ(one: argument.Expression, other: second[at].Expression))
            .Any();

        static string? NameOfTheCallee(InvocationExpressionSyntax call) =>
            call.Expression switch
            {
                SimpleNameSyntax name => name.Identifier.ValueText,
                MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                _ => null
            };
    }

    // True when the walk knows that two arguments are different: see CanGiveOneObject.
    private bool Differ(ExpressionSyntax one, ExpressionSyntax other)
    {
        Optional<object?> first = this.region.ModelFor(one).GetConstantValue(one);
        Optional<object?> second = this.region.ModelFor(other).GetConstantValue(other);

        if (first.HasValue && second.HasValue)
        {
            return !Equals(objA: first.Value, objB: second.Value);
        }

        return this.region.SymbolOf(Unwrapped(one)) is IFieldSymbol { IsStatic: true } field
               && this.region.SymbolOf(Unwrapped(other)) is IFieldSymbol { IsStatic: true } otherField
               && !SymbolEqualityComparer.Default.Equals(x: field, y: otherField)
               && KeepsItsCreation(field)
               && KeepsItsCreation(otherField);

        bool KeepsItsCreation(IFieldSymbol named) =>
            this.region.Symbols.Unchanged.CreationOf(named) != null || MakesItsOwnArray(named);
    }

    // True for a readonly static field whose initializer makes a new array or collection, where no
    // static constructor names the field. No code can then give it a different object.
    private static bool MakesItsOwnArray(IFieldSymbol field) =>
        field.IsReadOnly
        && field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax
            {
                Initializer.Value: CollectionExpressionSyntax
                or ArrayCreationExpressionSyntax
                or ImplicitArrayCreationExpressionSyntax
            }
        && field.ContainingType.StaticConstructors
            .SelectMany(static constructor => constructor.DeclaringSyntaxReferences)
            .SelectMany(static declared => declared.GetSyntax().DescendantNodes())
            .OfType<IdentifierNameSyntax>()
            .All(name => name.Identifier.ValueText != field.Name);

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
        && (owner.Objects.Any(this.region.IsAnOlderObject) || this.IsWrittenThroughASecondResult(owner))
        && owner.Members?.Has(member) != true;
}
