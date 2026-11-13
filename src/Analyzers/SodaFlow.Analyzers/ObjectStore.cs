using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     The one state of each object that the code of a loop region makes. Each
///     name of an object reads its state here, and a change through a name
///     writes it here.
/// </summary>
/// <remarks>
///     A local, a parameter, an element of a collection, and a member of a
///     second object can each be a name of one object. Before the store, each
///     name had a copy of the state, and a change through one name was not a
///     change for the others.
/// </remarks>
internal sealed class ObjectStore
{
    private readonly Dictionary<MadeObject, Dictionary<ISymbol, MadeObject>> inMembers = [];
    private readonly Dictionary<(SyntaxNode Place, LoopFrame Frame), MadeObject> made = [];
    private readonly Dictionary<MadeObject, LoopState> states = [];

    /// <summary>True when the code made no object.</summary>
    public bool IsEmpty => this.states.Count == 0;

    /// <summary>
    ///     The object that a place of the code makes in a frame of the walk. The
    ///     first walk of the place gives the object its first state. A second walk
    ///     of the same place in the same frame is the same object, which can have
    ///     changed.
    /// </summary>
    public MadeObject MadeAt(SyntaxNode place, LoopFrame frame, ITypeSymbol type, LoopState state)
    {
        if (!this.made.TryGetValue(key: (place, frame), value: out MadeObject? found))
        {
            found = new MadeObject(type);
            this.made[(place, frame)] = found;
            this.states[found] = state;
        }

        return found;
    }

    /// <summary>
    ///     The object in a member of an object of the store. Null when no read of
    ///     that member made it.
    /// </summary>
    public MadeObject? InMember(MadeObject owner, ISymbol member) =>
        this.inMembers.TryGetValue(key: owner, value: out Dictionary<ISymbol, MadeObject>? members)
        && members.TryGetValue(key: member, value: out MadeObject? found)
            ? found
            : null;

    /// <summary>
    ///     Makes the object in a member of an object of the store, with its first
    ///     state.
    /// </summary>
    public MadeObject MakeInMember(MadeObject owner, ISymbol member, ITypeSymbol type, LoopState state)
    {
        if (!this.inMembers.TryGetValue(key: owner, value: out Dictionary<ISymbol, MadeObject>? members))
        {
            members = new Dictionary<ISymbol, MadeObject>(SymbolEqualityComparer.Default);
            this.inMembers[owner] = members;
        }

        MadeObject found = new(type);
        members[member] = found;
        this.states[found] = state;

        return found;
    }

    /// <summary>The state of an object. Null for an object that is not in the store.</summary>
    public LoopState? StateOf(MadeObject found) =>
        this.states.TryGetValue(key: found, value: out LoopState? state) ? state : null;

    /// <summary>Gives an object of the store a new state.</summary>
    public void Set(MadeObject found, LoopState state) => this.states[found] = state;
}
