using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the object that a constructor builds. In the constructor, this is the object of the
// store that the creation makes. Thus, a name that gets this in the constructor is a name of that object.
internal sealed partial class LoopStateEvaluator
{
    // The object of the store that the code builds. The walk of a constructor has it, and so has
    // each constructor that it calls first and each member that they call on this object. Null for
    // each other code.
    private MadeObject? thisInTheStore;

    // For each creation of this code: the object of the store that the walk made before it read the
    // constructor. MadeHere gives the creation that object.
    private readonly Dictionary<SyntaxNode, MadeObject> builtBeforeTheConstructor = [];

    // The object of the store for the creation that a constructor runs for, made before the walk of
    // the constructor. The creation makes the object only after the constructor in the code. But
    // the constructor can give this to a name that the block reads after the creation. Null when
    // MadeHere makes no object that is one time in the frame of this code.
    private MadeObject? BuiltBeforeTheConstructor(SyntaxNode call)
    {
        if (this.quiet
            || call is not BaseObjectCreationExpressionSyntax creation
            || !this.RunsOneTime(creation))
        {
            return null;
        }

        TypeInfo info = this.region.ModelFor(creation).GetTypeInfo(creation);

        if ((info.Type ?? info.ConvertedType) is not INamedTypeSymbol { IsReferenceType: true } type)
        {
            return null;
        }

        MadeObject built =
            this.region.Objects.Make(
                frame: this.Frame,
                type: type,
                exact: true,
                state: LoopState.None.WithMembers(Construction.Of(type)));

        this.builtBeforeTheConstructor[creation] = built;

        return built;
    }

    // True when the walk reads a constructor that gets no state, because its code gives this a
    // second name. A put of this into a static is usual for a registry. The block can then write
    // through one name and read through the other. The limits of ReadsForObjects apply.
    private bool ReadsForThis(IMethodSymbol definition) =>
        this.region.Symbols.GivesThisAway(definition)
        && this.depth < MaxInlineDepth
        && this.region.TryEnterForObjects();

    // True for a call on the object that a constructor builds, of a method that gives this away.
    // The walk reads such a method also with no state, because it can give the new object a name.
    private bool GivesTheBuiltObjectAway(SyntaxNode call, Dictionary<ISymbol, LoopState> members) =>
        this.thisInTheStore != null
        && ReferenceEquals(objA: members, objB: this.thisMembers)
        && this.region.SymbolOf(call) is IMethodSymbol method
        && this.region.Symbols.GivesThisAway(method.OriginalDefinition);

    // The value of this that a name keeps in the code of a constructor: the object of the store, with
    // no state for a member. The store has the members of the object. A copy of them in the name does
    // not get the next write to the object.
    private LoopState ForANameOfTheBuiltObject(LoopState state, ExpressionSyntax? from) =>
        from != null
        && Unwrapped(from) is ThisExpressionSyntax
        && this.thisInTheStore is { } built
        && this.builtType is { } type
        && state.Objects.Contains(built)
        && state.Objects.All(made => this.region.Objects.StateOf(made) != null)
            ? state.WithMembers(Construction.Of(type))
            : state;

    // The object that the walk made for a creation before it read the constructor, with the state
    // that the creation gives it. Null for each other expression.
    private MadeObject? BuiltFor(SyntaxNode place, LoopState state)
    {
        if (this.builtBeforeTheConstructor.Count == 0
            || !this.builtBeforeTheConstructor.TryGetValue(key: place, value: out MadeObject? built))
        {
            return null;
        }

        this.region.Objects.Set(found: built, state: state);

        return built;
    }

    // The state of this in the code of a constructor: the members that the walk knows now, and the
    // object of the store.
    private LoopState ThisInTheStore(MadeObject built, SyntaxNode at) =>
        this.ThisWithItsMembers(at).WithObjects(ImmutableArray.Create(built));

    // The state of this with the members that the walk knows now. A call that gets this can change
    // them: see GivesThisToACall.
    private LoopState ThisWithItsMembers(SyntaxNode at)
    {
        this.GivesThisToACall(at);

        return LoopState.None.WithMembers(
            Construction.Of(this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default)));
    }

    // In the walk of a constructor, a call or a creation can get this as an argument. Its code can
    // then give a delegate member of this a different value. The walk does not read that code when
    // the call gets no state. Thus, after such a call, each such member also has each value that the
    // code of the class gives it.
    private void GivesThisToACall(SyntaxNode at)
    {
        if (at.Parent is not ArgumentSyntax
            {
                Parent: ArgumentListSyntax
                {
                    Parent: InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax
                }
            }
            || this.builtType is not { } type)
        {
            return;
        }

        foreach (ISymbol member in Construction.FieldsOf(type)
                     .Concat<ISymbol>(Construction.PropertiesOf(type))
                     .Where(static member =>
                         member is { IsStatic: false }
                             and (IFieldSymbol { Type.TypeKind: TypeKind.Delegate }
                             or IPropertySymbol { Type.TypeKind: TypeKind.Delegate })))
        {
            ImmutableArray<ExpressionSyntax> values = MemberBuilds.Of(member: member, type: type);

            if (values.IsEmpty)
            {
                continue;
            }

            ISymbol key = member.OriginalDefinition;

            this.thisMembers[key] = values.Aggregate(
                seed: this.thisMembers.TryGetValue(key: key, value: out LoopState? known) ? known : LoopState.None,
                func: (all, each) => all.Join(this.Eval(each)));

            this.WritesThisInTheStore(key);
        }
    }

    // Gives the object of the store the state that a write through this gave a member. A write
    // through a second name of the object changes the store only. Thus, the store has the last write
    // through each name.
    private void WritesThisInTheStore(ISymbol key)
    {
        if (this.thisInTheStore is { } built
            && !this.quiet
            && this.region.Objects.StateOf(built) is { } old)
        {
            this.region.Objects.Set(
                found: built,
                state: old.WithMembers(
                    (old.Members ?? Construction.Empty).With(member: key, state: this.thisMembers[key])));
        }
    }
}
