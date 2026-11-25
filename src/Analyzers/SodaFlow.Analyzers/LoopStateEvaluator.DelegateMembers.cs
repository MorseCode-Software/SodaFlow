using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a delegate that a member or a getter of a different object gives.
internal sealed partial class LoopStateEvaluator
{
    // The state of a delegate member of a different object, when the walk knows the object but not
    // that member. The walk did not read the code that gave the member a value, as with a constructor
    // that gets no state. Thus, the value comes from the code of the type, as for a member that
    // holds a build. A member with readonly and an initializer gets its value from WithItsInitializer.
    // Null for each other member.
    private LoopState? OfAnUnknownDelegateMember(
        ExpressionSyntax read,
        ExpressionSyntax owner,
        LoopState ofTheOwner,
        ISymbol member) =>
        member is IFieldSymbol { Type.TypeKind: TypeKind.Delegate }
            or IPropertySymbol { Type.TypeKind: TypeKind.Delegate }
        && !(member is IFieldSymbol { IsReadOnly: true } or IPropertySymbol { IsReadOnly: true }
             && this.region.InitializerOf(member.OriginalDefinition) != null)
        && this.WithTheBuildOfAMember(heldIn: read, state: LoopState.None) is { IsNone: false } built
            ? this.OnTheObject(state: built, owner: owner, ofTheOwner: ofTheOwner)
            : null;

    // The state of a delegate that the getter of an object returns. The value of base is not an
    // object with members. A different name of this is not a different object.
    private LoopState OnTheObjectOfAGetter(ExpressionSyntax read, IPropertySymbol property, LoopState state) =>
        property.Type.TypeKind == TypeKind.Delegate
        && read is MemberAccessExpressionSyntax { Expression: not BaseExpressionSyntax and var owner }
        && !this.IsANameOfThis(owner)
            ? this.OnTheObject(state: state, owner: owner, ofTheOwner: null)
            : state;

    // The same state, where each method group of this in the code of the object is a method group
    // of the object through its owner. That code returned. Thus, a call of the method reads the
    // members that the object has at the call, as for owner.Method.
    private LoopState OnTheObject(LoopState state, ExpressionSyntax owner, LoopState? ofTheOwner)
    {
        foreach (FunctionValue function in state.Functions)
        {
            if (function.OfThis == null)
            {
                continue;
            }

            ofTheOwner ??= this.Eval(owner);

            state =
                state.WithoutFunctionsOf(function.Declaration)
                    .Join(
                        LoopState.OfFunction(
                            new FunctionValue(
                                declaration: function.Declaration,
                                definer: null,
                                receiver: ofTheOwner,
                                ofThis: null,
                                owner: (this, owner),
                                extended: function.Extended)));
        }

        return state;
    }
}
