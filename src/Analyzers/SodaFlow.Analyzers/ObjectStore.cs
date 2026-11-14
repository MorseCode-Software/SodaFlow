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
    private readonly Dictionary<MadeObject, LoopState> states = [];
    private readonly Dictionary<SyntaxNode, List<MadeObject>> turns = [];
    private int inATurn;

    /// <summary>True while the walk is in a loop whose turn made an object.</summary>
    public bool HasATurn => this.inATurn > 0;

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
    /// <param name="state">The first state of the object.</param>
    public MadeObject MakeInATurn(LoopFrame frame, ITypeSymbol type, bool exact, SyntaxNode turn, LoopState state)
    {
        MadeObject found = new(type: type, exact: exact, frame: frame, turn: turn, isOfTurnsBefore: false);
        this.states[found] = state;
        this.InATurn(found);

        return found;
    }

    private void InATurn(MadeObject found)
    {
        if (!this.turns.TryGetValue(key: found.Turn!, value: out List<MadeObject>? made))
        {
            made = [];
            this.turns[found.Turn!] = made;
        }

        made.Add(found);
        this.inATurn++;
    }

    /// <summary>True for the object of a turn while the walk is in the loop of that turn.</summary>
    public bool IsOfATurnNow(MadeObject found) => found.Turn != null && !this.ended.Contains(found);

    /// <summary>
    ///     For the object of a turn: the objects that the same code made in the
    ///     turns before. A place that is not of the turn can have one of them.
    /// </summary>
    public MadeObject BeforeOf(MadeObject found)
    {
        if (!this.before.TryGetValue(key: found, value: out MadeObject? older))
        {
            older = found.OfTurnsBefore();
            this.before[found] = older;
            this.states[older] = LoopState.None;
        }

        return older;
    }

    /// <summary>
    ///     The identity that an object has now. After the walk leaves a loop, the
    ///     object of its turn is one of the objects of the turns before.
    /// </summary>
    public MadeObject Now(MadeObject found) => this.ended.Contains(found) ? this.before[found] : found;

    /// <summary>The identities that the given objects have now, each one time.</summary>
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
    ///     Records that the walk left a loop. Each object of its turn is then one
    ///     of the objects of the turns before, and those get the state that the
    ///     object had at the end of the turn. A member of the object goes the same
    ///     way.
    /// </summary>
    /// <param name="turn">The body of the loop.</param>
    public void EndTurns(SyntaxNode turn)
    {
        if (!this.turns.TryGetValue(key: turn, value: out List<MadeObject>? made))
        {
            return;
        }

        this.turns.Remove(turn);
        this.inATurn -= made.Count;

        foreach (MadeObject found in made)
        {
            MadeObject older = this.BeforeOf(found);
            this.states[older] = this.states[older].Join(this.states[found]);
            this.states.Remove(found);
            this.ended.Add(found);

            if (!this.inMembers.TryGetValue(key: found, value: out Dictionary<ISymbol, MadeObject>? members))
            {
                continue;
            }

            Dictionary<ISymbol, MadeObject> inOlder = new(SymbolEqualityComparer.Default);
            this.inMembers[older] = inOlder;

            foreach (KeyValuePair<ISymbol, MadeObject> member in members)
            {
                inOlder[member.Key] = this.BeforeOf(member.Value);
            }
        }
    }

    /// <summary>
    ///     The object in a member of an object of the store. Null when no read of
    ///     that member made it.
    /// </summary>
    public MadeObject? InMember(MadeObject owner, ISymbol member) =>
        this.inMembers.TryGetValue(key: this.Now(owner), value: out Dictionary<ISymbol, MadeObject>? members)
        && members.TryGetValue(key: member, value: out MadeObject? found)
            ? found
            : null;

    /// <summary>
    ///     Makes the object in a member of an object of the store, with its first
    ///     state.
    /// </summary>
    public MadeObject MakeInMember(MadeObject owner, ISymbol member, ITypeSymbol type, LoopState state)
    {
        bool afterItsTurn = this.ended.Contains(owner);
        owner = this.Now(owner);

        if (!this.inMembers.TryGetValue(key: owner, value: out Dictionary<ISymbol, MadeObject>? members))
        {
            members = new Dictionary<ISymbol, MadeObject>(SymbolEqualityComparer.Default);
            this.inMembers[owner] = members;
        }

        MadeObject found = owner.InAMember(declared: type, afterItsTurn: afterItsTurn);
        members[member] = found;
        this.states[found] = state;

        // A member of the object of a turn is of that turn also.
        if (found.Turn != null)
        {
            this.InATurn(found);
        }

        return found;
    }

    /// <summary>The state of an object. Null for an object that is not in the store.</summary>
    public LoopState? StateOf(MadeObject found) =>
        this.states.TryGetValue(key: this.Now(found), value: out LoopState? state) ? state : null;

    /// <summary>Gives an object of the store a new state.</summary>
    public void Set(MadeObject found, LoopState state) => this.states[this.Now(found)] = state;
}
