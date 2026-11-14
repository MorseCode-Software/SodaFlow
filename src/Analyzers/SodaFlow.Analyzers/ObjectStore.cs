using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

    // The collections that hold only objects that the walk saw go in.
    private readonly HashSet<MadeObject> complete = [];

    // The collections that only the code of one block can get to.
    private readonly HashSet<MadeObject> apart = [];
    private readonly HashSet<MadeObject> ended = [];
    private readonly Dictionary<MadeObject, Dictionary<ISymbol, MadeObject>> inMembers = [];

    // For the object in a member: its owner, the member, and the type of the member.
    private readonly Dictionary<MadeObject, (MadeObject Owner, ISymbol Member, ITypeSymbol Type)> memberOf = [];

    // The objects of the turns before, for each expression that makes the object of a turn in the
    // code of a call. The walk reads a loop two times, and each read makes an object of its turn.
    // The two have the same objects before them.
    private readonly Dictionary<(SyntaxNode Place, object Code), MadeObject> olderOf = [];

    // For an expression in a loop that is in a second loop: the objects that it made in the turns
    // before of its loop. The turns are those in this turn of the second loop. The key has the
    // body of the second loop, and the entry is there while the walk is in a turn of that loop.
    private readonly Dictionary<(SyntaxNode Place, object Code, SyntaxNode Around), MadeObject> inATurnAround = [];

    private readonly Dictionary<MadeObject, (SyntaxNode Place, object Code, SyntaxNode Around)> keyInATurnAround = [];

    // An identity for the code that each call runs. The key is the identity of the code that has
    // the call, and the call.
    private readonly Dictionary<(object Parent, SyntaxNode Call), object> codes = [];

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
        MadeObject found =
            new(
                type: type,
                exact: exact,
                frame: frame,
                turn: null,
                isOfTurnsBefore: false,
                shapeOf: null,
                shapePart: 0);

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
    /// <param name="code">The identity of the code that has the expression, from <see cref="CodeOf" />.</param>
    /// <param name="state">The first state of the object.</param>
    /// <param name="around">The bodies of the loops around the loop, the nearest first.</param>
    /// <remarks>
    ///     The object has one identity for each loop around its loop also. That
    ///     identity is the objects of the turns before of its loop, in this turn
    ///     of that loop around. Those objects are new in each turn of the loop
    ///     around. Thus, a member of such an object is of this outer turn.
    /// </remarks>
    public MadeObject MakeInATurn(
        LoopFrame frame,
        ITypeSymbol type,
        bool exact,
        SyntaxNode turn,
        SyntaxNode place,
        object code,
        LoopState state,
        ImmutableArray<SyntaxNode> around)
    {
        if (!this.olderOf.TryGetValue(key: (place, code), value: out MadeObject? last))
        {
            last = MadeObject.OfTurnsBefore(type: type, exact: exact, frame: frame);
            this.olderOf[(place, code)] = last;
            this.states[last] = LoopState.None;
        }

        MadeObject found = last.OfThisTurn(turn: turn, frame: frame);
        MadeObject older = last;
        this.states[found] = state;

        // The loop that is around each other loop gets its identity first. Thus, a loop comes
        // after each loop around it in the list of the open loops.
        for (int i = around.Length - 1; i >= 0; i--)
        {
            (SyntaxNode Place, object Code, SyntaxNode Around) key = (place, code, around[i]);

            if (!this.inATurnAround.TryGetValue(key: key, value: out MadeObject? inThatTurn))
            {
                inThatTurn = last.OfTurnsBeforeIn(around: around[i], frame: frame);
                this.inATurnAround[key] = inThatTurn;
                this.keyInATurnAround[inThatTurn] = key;
                this.states[inThatTurn] = LoopState.None;
                this.before[inThatTurn] = older;
                this.InATurn(found: inThatTurn, turn: around[i]);
            }

            older = inThatTurn;
        }

        this.InATurn(found: found, turn: turn);
        this.before[found] = older;

        return found;
    }

    /// <summary>
    ///     An identity for the code that a call runs. Each read of the same call
    ///     from the same code gets the same identity. Thus, the second read of a
    ///     loop finds the objects of the turns before of a callee.
    /// </summary>
    /// <param name="parent">The identity of the code that has the call.</param>
    /// <param name="call">The call.</param>
    public object CodeOf(object parent, SyntaxNode call)
    {
        if (!this.codes.TryGetValue(key: (parent, call), value: out object? code))
        {
            code = new object();
            this.codes[(parent, call)] = code;
        }

        return code;
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

                // The loop of that identity can have its end also.
                while (this.ended.Contains(older))
                {
                    older = this.before[older];
                }

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

            // The next read makes the object of its turn again, with a mark of its own. A mark
            // that stays gives each read a different number, and the reads of the loop do not end.
            this.complete.Remove(found);
            this.apart.Remove(found);

            // The next turn of this loop gets a new identity for the turns before of a loop in it.
            if (this.keyInATurnAround.TryGetValue(
                    key: found,
                    value: out (SyntaxNode Place, object Code, SyntaxNode Around) key))
            {
                this.inATurnAround.Remove(key);
                this.keyInATurnAround.Remove(found);
            }
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

        MadeObject found = owner.InAMember(declared: type, member: member);
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

    /// <summary>
    ///     Records that a collection holds only objects that the walk saw go in.
    ///     The walk saw the collection made empty, or made from values that the
    ///     store knows.
    /// </summary>
    public void IsComplete(MadeObject collection) => this.complete.Add(collection);

    /// <summary>Records that a collection can hold an object that the walk did not see go in.</summary>
    public void CanHoldMore(MadeObject collection) => this.complete.Remove(collection);

    /// <summary>
    ///     Records that only the code of one block can get to a collection. No
    ///     other code has a name for it, and no object holds it.
    /// </summary>
    public void IsApart(MadeObject collection) => this.apart.Add(collection);

    /// <summary>
    ///     Records that each collection can hold an object that the walk did not
    ///     see go in. Code that the walk cannot read ran, and it can get to a
    ///     collection through an object that it has. It cannot get to a collection
    ///     that is apart.
    /// </summary>
    public void EachCanHoldMore() => this.complete.IntersectWith(this.apart);

    /// <summary>True for a collection that holds only objects that the walk saw go in.</summary>
    public bool HoldsOnlyWhatWentIn(MadeObject collection) => this.complete.Contains(collection);

    /// <summary>A number for what the store says about its objects, from <see cref="LoopState.Shape" />.</summary>
    public int Shape =>
        unchecked(
            this.states.Aggregate(
                seed: 0,
                func: static (sum, one) => unchecked(
                    sum + ShapeNumber.Of(location: one.Key.Shape, state: one.Value.Shape)))
            + this.complete.Aggregate(seed: 0, func: static (sum, made) => unchecked(sum + made.Shape * 43)));

    /// <summary>The state of an object. Null for an object that is not in the store.</summary>
    public LoopState? StateOf(MadeObject found) =>
        this.states.TryGetValue(key: found, value: out LoopState? state) ? state : null;

    /// <summary>Gives an object of the store a new state.</summary>
    public void Set(MadeObject found, LoopState state) => this.states[found] = state;
}
