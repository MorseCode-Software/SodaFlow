using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     What an object built in a loop region holds. The evaluator finds it
///     from the constructor, with the parameters bound to the arguments. A tuple
///     is such an object also, and its elements are its members.
/// </summary>
/// <param name="members">
///     The state of each field and property that an initializer
///     or the constructor sets. A parameter of a primary constructor is here
///     also, because a method of the object can read it.
/// </param>
/// <param name="types">
///     The type of the object, when the evaluator knows the constructor that
///     made it. A member that the code reads through an interface or a base type
///     is then the member of this type. A value that is one of two objects, as
///     after a conditional, has the type of each. Empty when the evaluator does
///     not know the type.
/// </param>
/// <param name="hasParts">
///     True when a member is a part of a pair of the platform. A join of two
///     states asks for it each time, thus the object keeps the answer.
/// </param>
/// <param name="hasInterfaceMembers">
///     True when a write named a property of an interface. A read of a
///     property of a class asks for it before it looks for such a key: see
///     <see cref="StateImplementedTogether" />.
/// </param>
internal sealed class Construction(
    in ImmutableDictionary<ISymbol, LoopState> members,
    in ImmutableArray<INamedTypeSymbol> types,
    in bool hasParts,
    in bool hasInterfaceMembers)
{
    private readonly ImmutableDictionary<ISymbol, LoopState> members = members;

    // Each type is the definition of the type of an object. For a generic type, the code names
    // that type with its type arguments, which the definition does not have.
    private readonly ImmutableArray<INamedTypeSymbol> types = types;

    /// <summary>An object with no member that the evaluator knows.</summary>
    public static Construction Empty { get; } =
        new(
            members: ImmutableDictionary.Create<ISymbol, LoopState>(SymbolEqualityComparer.Default),
            types: ImmutableArray<INamedTypeSymbol>.Empty,
            hasParts: false,
            hasInterfaceMembers: false);

    /// <summary>
    ///     An object of the given type, with no member that the evaluator knows.
    ///     The code can set a member of it after the constructor.
    /// </summary>
    public static Construction Of(INamedTypeSymbol type) => Of(members: Empty.members, type: type);

    /// <summary>An object of the given type, with the members that its constructor set.</summary>
    public static Construction Of(ImmutableDictionary<ISymbol, LoopState> members, INamedTypeSymbol type) =>
        new(
            members: members,
            types: ImmutableArray.Create(type),
            hasParts: members.Keys.Any(IsPart),
            hasInterfaceMembers: members.Keys.Any(IsOfAnInterface));

    /// <summary>An object with no type that the evaluator knows, with the given members.</summary>
    public static Construction Of(ImmutableDictionary<ISymbol, LoopState> members) =>
        new(
            members: members,
            types: ImmutableArray<INamedTypeSymbol>.Empty,
            hasParts: members.Keys.Any(IsPart),
            hasInterfaceMembers: members.Keys.Any(IsOfAnInterface));

    /// <summary>An object of the same types, with no member that the evaluator knows.</summary>
    public Construction WithNoState =>
        new(members: Empty.members, types: this.types, hasParts: false, hasInterfaceMembers: false);

    /// <summary>True when the evaluator knows a state for one member or more.</summary>
    public bool HasState => this.members.Values.Any(static state => !state.IsNone);

    /// <summary>A number for what the members say, as <see cref="LoopState.Shape" /> is for a state.</summary>
    public int Shape =>
        this.members.Aggregate(
            seed: 0,
            func: static (sum, member) => unchecked(
                sum + ShapeNumber.Of(location: ShapeNumber.Of(member.Key.Name), state: member.Value.Shape)));

    /// <summary>
    ///     True when a member is a part of a pair of the platform that the
    ///     evaluator saw made. <see cref="SodaFlowSymbols.HoldsParts" /> names the
    ///     types.
    /// </summary>
    public bool HasParts { get; } = hasParts;

    private static bool IsPart(ISymbol member) => SodaFlowSymbols.HoldsParts(member.ContainingType);

    /// <summary>True when a write named a property of an interface.</summary>
    public bool HasInterfaceMembers { get; } = hasInterfaceMembers;

    private static bool IsOfAnInterface(ISymbol member) =>
        member is IPropertySymbol { ContainingType.TypeKind: TypeKind.Interface };

    /// <summary>
    ///     This object, where each part that a second state does not know has
    ///     that state also.
    /// </summary>
    /// <remarks>
    ///     The second state can be a pair that the evaluator did not see made, as
    ///     a pair from a dictionary is. Each part of such a pair has the state of
    ///     all that the pair contains. Without this, a value that is one of the
    ///     two pairs has, for that part, only the state from the first pair.
    /// </remarks>
    public Construction FillParts(LoopState other)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder? filled = null;

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.members
                     .Where(pair => IsPart(pair.Key) && other.Members?.members.ContainsKey(pair.Key) != true))
        {
            filled ??= this.members.ToBuilder();
            filled[pair.Key] = pair.Value.Join(other.AsElement);
        }

        return filled == null
            ? this
            : new Construction(
                members: filled.ToImmutable(),
                types: this.types,
                hasParts: this.HasParts,
                hasInterfaceMembers: this.HasInterfaceMembers);
    }

    /// <summary>The state of each member that the evaluator knows.</summary>
    public IEnumerable<KeyValuePair<ISymbol, LoopState>> Members => this.members;

    /// <summary>
    ///     The origin of one member that has an origin, for a report about the
    ///     object and not about one member.
    /// </summary>
    public ISymbol? AnyOrigin =>
        this.members.Values.Select(static state => state.AnyOrigin).FirstOrDefault(static origin => origin != null);

    /// <summary>
    ///     The same object after an initializer sets one member. The new state
    ///     replaces the state from the constructor.
    /// </summary>
    /// <remarks>
    ///     The write sets the member that <see cref="Resolve" /> finds for a read
    ///     of the same name. It also sets each alias of that member that the
    ///     object has.
    /// </remarks>
    public Construction With(ISymbol member, LoopState state) =>
        new(
            // Most writes name the member that a read finds. Such a write does not call Resolve.
            members: this.IsItsOwnMember(member)
                ? SetWithTheAliases(members: this.members, key: KeyOf(member), state: state)
                : this.SetEachResolved(member: member, state: state),
            types: this.types,
            hasParts: this.HasParts || IsPart(member),
            hasInterfaceMembers: this.HasInterfaceMembers || IsOfAnInterface(member));

    private ImmutableDictionary<ISymbol, LoopState> SetEachResolved(ISymbol member, LoopState state) =>
        this.Resolve(member)
            .Aggregate(
                seed: this.members,
                func: (set, actual) => SetWithTheAliases(members: set, key: KeyOf(actual), state: state));

    private static ImmutableDictionary<ISymbol, LoopState> SetWithTheAliases(
        ImmutableDictionary<ISymbol, LoopState> members,
        ISymbol key,
        LoopState state)
    {
        ImmutableDictionary<ISymbol, LoopState> set = members.SetItem(key: key, value: state);

        // Most writes stop here: the object has no member, or the member cannot have an alias.
        return members.IsEmpty || !CanHaveAnAlias(key)
            ? set
            : SetTheAliases(members: members, set: set, key: key, state: state);
    }

    private static ImmutableDictionary<ISymbol, LoopState> SetTheAliases(
        ImmutableDictionary<ISymbol, LoopState> members,
        ImmutableDictionary<ISymbol, LoopState> set,
        ISymbol key,
        LoopState state) =>
        members.Keys
            .Where(other => AreAliases(first: other, second: key))
            .Aggregate(seed: set, func: (aliased, other) => aliased.SetItem(key: other, value: state));

    /// <summary>
    ///     The state of a key, joined with the state of each alias of the key.
    ///     The function gives each state from its key and the state that the
    ///     dictionary has for that key.
    /// </summary>
    /// <remarks>
    ///     A write keys a state with the member that the code names. A read can
    ///     name an alias of that member. The alias is a second key for the same
    ///     location.
    /// </remarks>
    public static LoopState StateWithTheAliases(
        IReadOnlyDictionary<ISymbol, LoopState> states,
        ISymbol key,
        Func<ISymbol, LoopState, LoopState> stateOf)
    {
        LoopState? own = states.TryGetValue(key: key, value: out LoopState? found) ? found : null;
        LoopState state = own != null ? stateOf(arg1: key, arg2: own) : LoopState.None;

        // Most reads stop here: no different key has a state, or the member cannot have an alias.
        return states.Count <= (own != null ? 1 : 0) || !CanHaveAnAlias(key)
            ? state
            : JoinTheAliases(states: states, key: key, own: own, state: state, stateOf: stateOf);
    }

    private static LoopState JoinTheAliases(
        IReadOnlyDictionary<ISymbol, LoopState> states,
        ISymbol key,
        LoopState? own,
        LoopState state,
        Func<ISymbol, LoopState, LoopState> stateOf) =>
        states
            .Where(pair => !ReferenceEquals(objA: pair.Value, objB: own) && AreAliases(first: pair.Key, second: key))
            .Aggregate(seed: state, func: (joined, pair) => joined.Join(stateOf(arg1: pair.Key, arg2: pair.Value)));

    /// <summary>
    ///     The join of the states of each property that one property of a type
    ///     in the compilation implements with the key. One of the two is of an
    ///     interface. The function gives each state from its key and the state
    ///     that the dictionary has for that key.
    /// </summary>
    /// <remarks>
    ///     The walk does not know the type of an object that a call gives. A
    ///     write through an interface and a read through a second name then use
    ///     two keys for one location. The second name is a second interface, or a
    ///     base class that does not have the interface. The write keeps its key,
    ///     thus the read joins the two states. The last argument is true when a
    ///     key of the states is of an interface.
    /// </remarks>
    public static LoopState StateImplementedTogether(
        IReadOnlyDictionary<ISymbol, LoopState> states,
        ISymbol key,
        Func<ISymbol, LoopState, LoopState> stateOf,
        ImplementationIndex implementations,
        bool hasInterfaceMembers) =>
        // Most reads stop here: no different key has a state, or no key can be such a property.
        states.Count <= (states.ContainsKey(key) ? 1 : 0)
        || !CanBeImplementedTogether(key: key, with: hasInterfaceMembers)
            ? LoopState.None
            : JoinImplementedTogether(states: states, key: key, stateOf: stateOf, implementations: implementations);

    // A property of an interface can have such a key. A property of a class can have one only when
    // a key is of an interface and the class is not sealed. A derived class then gives the property
    // to that interface.
    private static bool CanBeImplementedTogether(ISymbol key, bool with) =>
        key is IPropertySymbol { ContainingType: var type }
        && (type.TypeKind == TypeKind.Interface || (with && type is { TypeKind: TypeKind.Class, IsSealed: false }));

    private static LoopState JoinImplementedTogether(
        IReadOnlyDictionary<ISymbol, LoopState> states,
        ISymbol key,
        Func<ISymbol, LoopState, LoopState> stateOf,
        ImplementationIndex implementations) =>
        states
            .Where(pair =>
                // One property implements two properties only when the two have its name. The test of the
                // name comes first, thus a key with a different name does not make the index.
                pair.Key is IPropertySymbol other
                && other.Name == key.Name
                && implementations.AreTogether(first: other, second: key))
            .Aggregate(
                seed: LoopState.None,
                func: (joined, pair) => joined.Join(stateOf(arg1: pair.Key, arg2: pair.Value)));

    // Only a property can have an alias. It is a property of an interface, an abstract property, a
    // virtual property, an override, or a property of a type that has an interface.
    private static bool CanHaveAnAlias(ISymbol key) =>
        key is IPropertySymbol property
        && (property.IsVirtual
            || property.IsAbstract
            || property.IsOverride
            || property.ContainingType.AllInterfaces.Length > 0);

    // Two properties are aliases when one access on one object can find the two. This is true when
    // one overrides the other, or when the two override one property. It is true also for a
    // property of an interface and the property that implements it in its own type.
    private static bool AreAliases(ISymbol first, ISymbol second) =>
        first is IPropertySymbol
        && !SymbolEqualityComparer.Default.Equals(x: first, y: second)
        && (SymbolEqualityComparer.Default.Equals(x: RootOf(first), y: RootOf(second))
            || Implements(contract: first, member: second)
            || Implements(contract: second, member: first));

    private static bool Implements(ISymbol contract, ISymbol member) =>
        contract.ContainingType is { TypeKind: TypeKind.Interface } face
        && member.ContainingType is { } owner
        && owner.AllInterfaces.Any(one =>
            SymbolEqualityComparer.Default.Equals(x: one.OriginalDefinition, y: face.OriginalDefinition))
        && SymbolEqualityComparer.Default.Equals(
            x: RootOf(ResolveIn(type: owner, member: contract)),
            y: RootOf(member));

    /// <summary>
    ///     The definition of the property at the base of each override of the
    ///     property. It is the property itself when it is not an override.
    /// </summary>
    public static ISymbol RootOf(ISymbol member)
    {
        ISymbol root = member;

        while (root is IPropertySymbol { OverriddenProperty: { } overridden })
        {
            root = overridden;
        }

        return root.OriginalDefinition;
    }

    /// <summary>
    ///     True when the evaluator knows the type of this object, and an object of
    ///     that type is a value of the given type. The given type is then the type
    ///     of the object, one of its base types, or one of its interfaces.
    /// </summary>
    public bool Is(ITypeSymbol? type)
    {
        if (this.types.IsEmpty || type == null)
        {
            return false;
        }

        // A nullable struct holds the struct.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        return SaysNothing(type) || this.types.Any(own => IsOne(own: own, type: type));
    }

    // True when an object of the first type is a value of the second one.
    private static bool IsOne(INamedTypeSymbol own, ISymbol type)
    {
        bool isGeneric = HasTypeParameters(own);

        for (INamedTypeSymbol? current = own; current != null; current = current.BaseType)
        {
            if (IsSame(candidate: current, type: type, isGeneric: isGeneric))
            {
                return true;
            }
        }

        return own.AllInterfaces.Any(contract => IsSame(candidate: contract, type: type, isGeneric: isGeneric));
    }

    // The type arguments of a generic object are not known here. For such an object, two types
    // are the same when they have one definition.
    private static bool IsSame(ISymbol candidate, ISymbol type, bool isGeneric) =>
        SymbolEqualityComparer.Default.Equals(x: candidate, y: type)
        || (isGeneric
            && SymbolEqualityComparer.Default.Equals(x: candidate.OriginalDefinition, y: type.OriginalDefinition));

    // A type in a generic type has the type parameters of that type.
    private static bool HasTypeParameters(INamedTypeSymbol? type)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            if (!current.TypeParameters.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     False when the evaluator knows the type of this object, and an object
    ///     of that type is not a value of the given type. A list of such objects
    ///     has their members in its state, and the list is not one of them. A
    ///     tuple has no type here. It is not a value of a struct or of a class
    ///     that is not a tuple, such as a pair that contains it.
    /// </summary>
    public bool CanBe(ITypeSymbol? type) =>
        this.types.IsEmpty ? !this.IsTuple || !IsNotATuple(type) : type == null || this.Is(type);

    /// <summary>
    ///     False when this object cannot be the owner of the member. A member
    ///     of <c>Nullable</c> is not a member of the struct that the nullable
    ///     holds.
    /// </summary>
    public bool CanOwn(ISymbol member) =>
        member.ContainingType is not { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
        && this.CanBe(member.ContainingType);

    /// <summary>
    ///     The properties of the type of this object and of its base types that
    ///     this compilation declares. A serializer reads the getters of some of
    ///     them.
    /// </summary>
    public IEnumerable<IPropertySymbol> Properties =>
        this.types.SelectMany(PropertiesOf).Distinct<IPropertySymbol>(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The fields of the type of this object and of its base types that this
    ///     compilation declares. A serializer reads some of them.
    /// </summary>
    public IEnumerable<IFieldSymbol> Fields =>
        this.types.SelectMany(FieldsOf).Distinct<IFieldSymbol>(SymbolEqualityComparer.Default);

    /// <summary>The types of this object. A value that is one of two objects has two.</summary>
    public IEnumerable<INamedTypeSymbol> Types => this.types;

    /// <summary>
    ///     The properties of a type and of its base types that this compilation
    ///     declares.
    /// </summary>
    public static IEnumerable<IPropertySymbol> PropertiesOf(INamedTypeSymbol type) =>
        MembersOf<IPropertySymbol>(type);

    /// <summary>
    ///     The fields of a type and of its base types that this compilation
    ///     declares.
    /// </summary>
    public static IEnumerable<IFieldSymbol> FieldsOf(INamedTypeSymbol type) => MembersOf<IFieldSymbol>(type);

    private static IEnumerable<T> MembersOf<T>(INamedTypeSymbol type)
        where T : ISymbol
    {
        for (INamedTypeSymbol? current = type;
             current is { DeclaringSyntaxReferences.Length: > 0 };
             current = current.BaseType)
        {
            foreach (T member in current.GetMembers().OfType<T>())
            {
                yield return member;
            }
        }
    }

    /// <summary>
    ///     The instance property with the name that each type of this object
    ///     has. It is the property of the type itself, or of the nearest base
    ///     type that declares one. A read through <c>dynamic</c> binds to it.
    /// </summary>
    public IEnumerable<IPropertySymbol> PropertiesNamed(string name) =>
        this.types
            .Select(type => PropertyNamed(type: type, name: name))
            .OfType<IPropertySymbol>()
            .Distinct<IPropertySymbol>(SymbolEqualityComparer.Default);

    private static IPropertySymbol? PropertyNamed(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property)
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>True for a tuple that the code wrote with parentheses.</summary>
    public bool IsTuple =>
        !this.members.IsEmpty && this.members.Keys.All(static key => key.ContainingType is { IsTupleType: true });

    // A tuple is a value of object, of ValueType, and of each interface that its struct has.
    private static bool IsNotATuple(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        return type is INamedTypeSymbol
        {
            IsTupleType: false,
            TypeKind: TypeKind.Struct or TypeKind.Class,
            SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType)
        };
    }

    /// <summary>
    ///     True for a type that each object is a value of: <c>object</c>,
    ///     <c>dynamic</c>, or a type parameter. It is true also when the type is
    ///     not known. Such a type does not tell a list from one of its elements.
    /// </summary>
    public static bool SaysNothing(ITypeSymbol? type) =>
        type == null
        || type.SpecialType == SpecialType.System_Object
        || type.TypeKind is TypeKind.TypeParameter or TypeKind.Dynamic;

    /// <summary>True when the evaluator knows the type of this object.</summary>
    public bool HasType => !this.types.IsEmpty;

    /// <summary>The state of each element, when this object is a tuple.</summary>
    public IEnumerable<LoopState> TupleElements =>
        this.members
            .Where(static pair => pair.Key.ContainingType is { IsTupleType: true })
            .Select(static pair => pair.Value);

    /// <summary>
    ///     The state of each part, for a type from
    ///     <see cref="SodaFlowSymbols.HoldsParts" />, or of each element of
    ///     <see cref="TupleElements" />, with its position. The position is the
    ///     place of the type parameter of the part in the type of the pair. It is
    ///     -1 for a part whose type is not a type parameter.
    /// </summary>
    /// <remarks>The members have no order. Thus, the position comes from the member.</remarks>
    public IEnumerable<(int Position, LoopState State)> PartsAt(bool ofATuple) =>
        this.members
            .Where(pair => ofATuple ? pair.Key.ContainingType is { IsTupleType: true } : IsPart(pair.Key))
            .Select(static pair =>
                (pair.Key switch
                {
                    IFieldSymbol { Type: ITypeParameterSymbol parameter } => parameter.Ordinal,
                    IPropertySymbol { Type: ITypeParameterSymbol parameter } => parameter.Ordinal,
                    _ => -1
                }, pair.Value));

    /// <summary>
    ///     The instance methods of the types of this object that have the name
    ///     and the number of parameters.
    /// </summary>
    public IEnumerable<IMethodSymbol> MethodsOf(string name, int parameters) =>
        this.types
            .SelectMany(type => type.GetMembers(name))
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsStatic: false, MethodKind: MethodKind.Ordinary }
                             && method.Parameters.Length == parameters);

    /// <summary>True when the evaluator knows a state for the member.</summary>
    public bool Has(ISymbol member) => this.members.ContainsKey(KeyOf(member));

    public LoopState StateOf(ISymbol member) =>
        StateWithTheAliases(states: this.members, key: KeyOf(member), stateOf: static (_, state) => state);

    /// <summary>The same object, with the state of each member changed.</summary>
    public Construction Select(Func<LoopState, LoopState> change)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder changed = this.members.ToBuilder();

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.members)
        {
            changed[pair.Key] = change(pair.Value);
        }

        return new Construction(
            members: changed.ToImmutable(),
            types: this.types,
            hasParts: this.HasParts,
            hasInterfaceMembers: this.HasInterfaceMembers);
    }

    /// <summary>
    ///     The member of the type of this object for a member that the code reads
    ///     through an interface or a base type. The member itself when the
    ///     evaluator does not know the type. A value that is one of two objects
    ///     gives the member of each type.
    /// </summary>
    public IEnumerable<ISymbol> Resolve(ISymbol member) =>
        this.IsItsOwnMember(member)
            ? [member]
            : this.OwnersOf(member)
                .Select(type => ResolveIn(type: type, member: member))
                .Distinct(SymbolEqualityComparer.Default);

    // True when a read finds the member that the code names: see Resolve.
    private bool IsItsOwnMember(ISymbol member) =>
        this.types.IsEmpty
        || (member.ContainingType?.TypeKind != TypeKind.Interface
            && member is { IsAbstract: false, IsVirtual: false, IsOverride: false });

    /// <summary>
    ///     The same object, as a value of the given type. Of two objects, only
    ///     one whose type is a value of the given type can be that value. A type
    ///     that says nothing keeps the two.
    /// </summary>
    public Construction Narrow(ITypeSymbol? type)
    {
        if (this.types.Length < 2 || type == null || SaysNothing(type))
        {
            return this;
        }

        ImmutableArray<INamedTypeSymbol> owners =
            this.types.Where(own => IsOne(own: own, type: type)).ToImmutableArray();

        return owners.Length == this.types.Length
            ? this
            : new Construction(
                members: this.members,
                types: owners,
                hasParts: this.HasParts,
                hasInterfaceMembers: this.HasInterfaceMembers);
    }

    // Of two objects, only the one whose type has the member can be the object that the code
    // reads it on. The key of a pair is not its value. When no type has the member, the
    // evaluator does not know the object, and each type stays.
    private ImmutableArray<INamedTypeSymbol> OwnersOf(ISymbol member)
    {
        if (this.types.Length == 1 || member.ContainingType is not { } declaring)
        {
            return this.types;
        }

        ImmutableArray<INamedTypeSymbol> owners =
            this.types.Where(type => IsOne(own: type, type: declaring)).ToImmutableArray();

        return owners.IsEmpty ? this.types : owners;
    }

    /// <summary>
    ///     The state of a member that the code reads through an interface or a
    ///     base type. For a value that is one of two objects, it is the join of
    ///     the states of the member of each.
    /// </summary>
    /// <remarks>
    ///     The state of each property that the compilation implements together
    ///     with the member is in the join also: see
    ///     <see cref="StateImplementedTogether" />.
    /// </remarks>
    public LoopState StateThrough(ISymbol member, ImplementationIndex implementations) =>
        this.Resolve(member)
            .Aggregate(seed: LoopState.None, func: (joined, actual) => joined.Join(this.StateOf(actual)))
            .Join(
                StateImplementedTogether(
                    states: this.members,
                    key: KeyOf(member),
                    stateOf: static (_, state) => state,
                    implementations: implementations,
                    hasInterfaceMembers: this.HasInterfaceMembers));

    private static ISymbol ResolveIn(INamedTypeSymbol type, ISymbol member)
    {
        // The compiler knows the member that a type has for a member of an interface. An explicit
        // member has the name of the interface before its own name, thus the search by name
        // below does not find it.
        if (type.FindImplementationForInterfaceMember(member) is { } implemented)
        {
            return implemented;
        }

        // A caller gives the member of the definition of a generic interface, such as
        // IComparable<T>. The type has that interface with its type arguments.
        if (member.ContainingType is { TypeKind: TypeKind.Interface, IsGenericType: true })
        {
            foreach (INamedTypeSymbol contract in type.AllInterfaces)
            {
                if (contract.GetMembers(member.Name)
                        .FirstOrDefault(candidate =>
                            SymbolEqualityComparer.Default.Equals(
                                x: candidate.OriginalDefinition,
                                y: member.OriginalDefinition)) is { } ofContract
                    && type.FindImplementationForInterfaceMember(ofContract) is { } constructed)
                {
                    return constructed;
                }
            }
        }

        for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
        {
            ISymbol? found =
                current.GetMembers(member.Name)
                    .FirstOrDefault(candidate =>
                        candidate.Kind == member.Kind
                        && !candidate.IsAbstract
                        && (candidate is not IMethodSymbol method
                            || method.Parameters.Length == ((IMethodSymbol)member).Parameters.Length));

            if (found != null)
            {
                return found;
            }
        }

        return member;
    }

    /// <summary>
    ///     The members of two objects that one expression can name, such as the
    ///     two arms of a conditional. A member of either is kept, with the states
    ///     joined.
    /// </summary>
    /// <summary>
    ///     The methods that the type of this object has for each of its interfaces
    ///     which the given test accepts. A callee that gets the object through such
    ///     an interface calls these methods.
    /// </summary>
    public IEnumerable<IMethodSymbol> Implementations(Func<INamedTypeSymbol, bool> isInterface) =>
        this.types
            .SelectMany(type =>
                type.AllInterfaces
                    .Where(isInterface)
                    .SelectMany(static contract => contract.GetMembers().OfType<IMethodSymbol>())
                    .Select(type.FindImplementationForInterfaceMember)
                    .OfType<IMethodSymbol>())
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);

    public static Construction? Merge(Construction? first, Construction? second)
    {
        if (first == null || ReferenceEquals(objA: first, objB: second))
        {
            return second;
        }

        if (second == null)
        {
            return first;
        }

        // A value that is one of two objects has the members and the types of the two. A member
        // has the symbol of its type as its key, thus the members of two types stay apart.
        ImmutableDictionary<ISymbol, LoopState>.Builder merged = first.members.ToBuilder();

        foreach (KeyValuePair<ISymbol, LoopState> pair in second.members)
        {
            merged[pair.Key] =
                merged.TryGetValue(key: pair.Key, value: out LoopState? existing)
                    ? existing.Join(pair.Value)
                    : pair.Value;
        }

        return new Construction(
            members: merged.ToImmutable(),
            types: first.types
                .Union<INamedTypeSymbol>(second: second.types, comparer: SymbolEqualityComparer.Default)
                .ToImmutableArray(),
            hasParts: first.HasParts || second.HasParts,
            hasInterfaceMembers: first.HasInterfaceMembers || second.HasInterfaceMembers);
    }

    // An element of a tuple has a name in the code and a position in the type. The position is
    // the key, so that a read by a different name finds the same element.
    private static ISymbol KeyOf(ISymbol member) =>
        member is IFieldSymbol { CorrespondingTupleField: { } element }
            ? element.OriginalDefinition
            : member.OriginalDefinition;
}
