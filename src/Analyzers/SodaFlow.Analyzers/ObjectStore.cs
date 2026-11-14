using System.Collections.Generic;
using System.Collections.Immutable;
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
    private readonly Dictionary<MadeObject, MadeObject> before = [];
    private readonly HashSet<MadeObject> ended = [];
    private readonly Dictionary<MadeObject, Dictionary<ISymbol, MadeObject>> inMembers = [];

    // For the object in a member: its owner, the member, and the type of the member.
    private readonly Dictionary<MadeObject, (MadeObject Owner, ISymbol Member, ITypeSymbol Type)> memberOf = [];

    // The objects of the turns before, for each expression that makes the object of a turn in a
    // frame. The walk reads a loop two times, and each read makes an object of its turn. The two
    // have the same objects before them.
    private readonly Dictionary<(SyntaxNode Place, LoopFrame Frame), MadeObject> olderOf = [];

    // The bodies of the loops that the walk is in, for each loop whose turn made an object. A loop
    // comes after each loop that is around it.
    private readonly List<SyntaxNode> open = [];
    private readonly Dictionary<MadeObject, LoopState> states = [];
    private readonly Dictionary<SyntaxNode, List<MadeObject>> turns = [];

    /// <summary>True while the walk is in a loop whose turn made an object.</summary>
    public bool HasATurn => this.open.Count > 0;

    /// <summary>
    ///     True when a turn of the first loop runs in one turn of the second loop.
    ///     The two are then the same loop, or the second one is around the first
    ///     one. An object of the first loop is then new in each turn of the
    ///     second one.
    /// </summary>
    /// <remarks>
    ///     A loop that the walk is not in is before each loop that it is in. Thus,
    ///     an object whose loop ended is new in no turn.
    /// </remarks>
    /// <param name="turn">The body of the first loop. Null gives false.</param>
    /// <param name="around">The body of the second loop.</param>
    public bool IsIn(SyntaxNode? turn, SyntaxNode around) =>
        turn != null && this.open.IndexOf(turn) >= this.open.IndexOf(around);

    /// <summary>True when the code made no object.</summary>
    public bool IsEmpty => this.states.Count == 0;

    /// <summary>
    ///     Makes the object of an expression that the walk reads in a frame, with
    ///     its first state.
    /// </summary>
    /// <remarks>
    ///     An evaluator reads an expression one time. Code that the walk reads two
    ///     or more times in one frame, as the body of a lambda, makes no object.
    ///     Thus, the store has one object for an expression in a frame.
    /// </remarks>
    /// <param name="frame">The frame.</param>
    /// <param name="type">The type of the expression.</param>
    /// <param name="exact">True when the object has that type, and not a type that derives from it.</param>
    /// <param name="state">The first state of the object.</param>
    public MadeObject Make(LoopFrame frame, ITypeSymbol type, bool exact, LoopState state)
    {
        MadeObject found = new(type: type, exact: exact, frame: frame, turn: null, isOfTurnsBefore: false);
        this.states[found] = state;

        return found;
    }

    /// <summary>
    ///     Makes the object of an expression in the body of a loop, for the turn
    ///     that the walk reads, with its first state.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="type">The type of the expression.</param>
    /// <param name="exact">True when the object has that type, and not a type that derives from it.</param>
    /// <param name="turn">The body of the loop.</param>
    /// <param name="place">The expression.</param>
    /// <param name="state">The first state of the object.</param>
    public MadeObject MakeInATurn(
        LoopFrame frame,
        ITypeSymbol type,
        bool exact,
        SyntaxNode turn,
        SyntaxNode place,
        LoopState state)
    {
        MadeObject found = new(type: type, exact: exact, frame: frame, turn: turn, isOfTurnsBefore: false);
        this.states[found] = state;
        this.InATurn(found: found, turn: turn);

        if (!this.olderOf.TryGetValue(key: (place, frame), value: out MadeObject? older))
        {
            older = found.OfTurnsBefore();
            this.olderOf[(place, frame)] = older;
            this.states[older] = LoopState.None;
        }

        this.before[found] = older;

        return found;
    }

    private void InATurn(MadeObject found, SyntaxNode turn)
    {
        if (!this.turns.TryGetValue(key: turn, value: out List<MadeObject>? made))
        {
            made = [];
            this.turns[turn] = made;
            this.open.Add(turn);
        }

        made.Add(found);
    }

    /// <summary>
    ///     For the object of a turn: the objects that the same code made in the
    ///     turns before. A location that is older than the turn can have one of
    ///     them.
    /// </summary>
    public MadeObject BeforeOf(MadeObject found)
    {
        if (!this.before.TryGetValue(key: found, value: out MadeObject? older))
        {
            // Only the object in a member has none at this time. The objects before it are in that
            // member of the objects before its owner.
            (MadeObject owner, ISymbol member, ITypeSymbol type) = this.memberOf[found];
            MadeObject ofOwner = this.BeforeOf(owner);

            older =
                this.InMember(owner: ofOwner, member: member)
                ?? this.MakeInMember(owner: ofOwner, member: member, type: type, state: LoopState.None);

            this.before[found] = older;
        }

        return older;
    }

    /// <summary>
    ///     The identities that the given objects have now, each one time. After the
    ///     walk of a loop, the object of its turn is one of the objects of the
    ///     turns before.
    /// </summary>
    /// <remarks>
    ///     A read of a value gives these identities before the store gets an
    ///     object from that value. Thus, each other member of the store gets only
    ///     an identity of now.
    /// </remarks>
    public ImmutableArray<MadeObject> Now(ImmutableArray<MadeObject> objects)
    {
        ImmutableArray<MadeObject> now = objects;

        foreach (MadeObject found in objects)
        {
            if (this.ended.Contains(found))
            {
                MadeObject older = this.before[found];
                now = now.Contains(older) ? now.Remove(found) : now.Replace(oldValue: found, newValue: older);
            }
        }

        return now;
    }

    /// <summary>
    ///     Records the end of the walk of a loop. Each object of its turn is then
    ///     one of the objects of the turns before. Those objects get the state that
    ///     the object had at the end of the turn.
    /// </summary>
    /// <param name="turn">The body of the loop.</param>
    public void EndTurns(SyntaxNode turn)
    {
        if (!this.turns.TryGetValue(key: turn, value: out List<MadeObject>? made))
        {
            return;
        }

        this.turns.Remove(turn);
        this.open.Remove(turn);

        foreach (MadeObject found in made)
        {
            MadeObject older = this.BeforeOf(found);
            this.states[older] = this.states[older].Join(this.states[found]);
            this.states.Remove(found);
            this.ended.Add(found);
        }
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

        MadeObject found = owner.InAMember(type);
        members[member] = found;
        this.memberOf[found] = (owner, member, type);
        this.states[found] = state;

        // A member of the object of a turn is of that turn also.
        if (found.Turn is { } turn)
        {
            this.InATurn(found: found, turn: turn);
        }

        return found;
    }

    /// <summary>The state of an object. Null for an object that is not in the store.</summary>
    public LoopState? StateOf(MadeObject found) =>
        this.states.TryGetValue(key: found, value: out LoopState? state) ? state : null;

    /// <summary>Gives an object of the store a new state.</summary>
    public void Set(MadeObject found, LoopState state) => this.states[found] = state;
}
