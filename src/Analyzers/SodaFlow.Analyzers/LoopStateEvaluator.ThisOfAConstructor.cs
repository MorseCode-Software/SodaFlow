using System.Collections.Generic;
using System.Collections.Immutable;
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
    private LoopState ThisInTheStore(MadeObject built) =>
        LoopState.None
            .WithMembers(Construction.Of(this.thisMembers.ToImmutableDictionary(SymbolEqualityComparer.Default)))
            .WithObjects(ImmutableArray.Create(built));

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
