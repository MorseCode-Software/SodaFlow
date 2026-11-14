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
internal sealed class Construction(
    in ImmutableDictionary<ISymbol, LoopState> members,
    in ImmutableArray<INamedTypeSymbol> types,
    in bool hasParts)
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
            hasParts: false);

    /// <summary>
    ///     An object of the given type, with no member that the evaluator knows.
    ///     The code can set a member of it after the constructor.
    /// </summary>
    public static Construction Of(INamedTypeSymbol type) => Of(members: Empty.members, type: type);

    /// <summary>An object of the given type, with the members that its constructor set.</summary>
    public static Construction Of(ImmutableDictionary<ISymbol, LoopState> members, INamedTypeSymbol type) =>
        new(members: members, types: ImmutableArray.Create(type), hasParts: members.Keys.Any(IsPart));

    /// <summary>An object with no type that the evaluator knows, with the given members.</summary>
    public static Construction Of(ImmutableDictionary<ISymbol, LoopState> members) =>
        new(members: members, types: ImmutableArray<INamedTypeSymbol>.Empty, hasParts: members.Keys.Any(IsPart));

    /// <summary>An object of the same types, with no member that the evaluator knows.</summary>
    public Construction WithNoState => new(members: Empty.members, types: this.types, hasParts: false);

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
            : new Construction(members: filled.ToImmutable(), types: this.types, hasParts: this.HasParts);
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
    public Construction With(ISymbol member, LoopState state) =>
        new(
            members: this.members.SetItem(key: KeyOf(member), value: state),
            types: this.types,
            hasParts: this.HasParts || IsPart(member));

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

    private static IEnumerable<IPropertySymbol> PropertiesOf(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type;
             current is { DeclaringSyntaxReferences.Length: > 0 };
             current = current.BaseType)
        {
            foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>())
            {
                yield return property;
            }
        }
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

    /// <summary>True when the evaluator knows a state for the member.</summary>
    public bool Has(ISymbol member) => this.members.ContainsKey(KeyOf(member));

    public LoopState StateOf(ISymbol member) =>
        this.members.TryGetValue(key: KeyOf(member), value: out LoopState? state) ? state : LoopState.None;

    /// <summary>The same object, with the state of each member changed.</summary>
    public Construction Select(Func<LoopState, LoopState> change)
    {
        ImmutableDictionary<ISymbol, LoopState>.Builder changed = this.members.ToBuilder();

        foreach (KeyValuePair<ISymbol, LoopState> pair in this.members)
        {
            changed[pair.Key] = change(pair.Value);
        }

        return new Construction(members: changed.ToImmutable(), types: this.types, hasParts: this.HasParts);
    }

    /// <summary>
    ///     The member of the type of this object for a member that the code reads
    ///     through an interface or a base type. The member itself when the
    ///     evaluator does not know the type. A value that is one of two objects
    ///     gives the member of each type.
    /// </summary>
    public IEnumerable<ISymbol> Resolve(ISymbol member) =>
        this.types.IsEmpty
        || (member.ContainingType?.TypeKind != TypeKind.Interface
            && member is { IsAbstract: false, IsVirtual: false, IsOverride: false })
            ? [member]
            : this.OwnersOf(member)
                .Select(type => ResolveIn(type: type, member: member))
                .Distinct(SymbolEqualityComparer.Default);

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
            : new Construction(members: this.members, types: owners, hasParts: this.HasParts);
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
    public LoopState StateThrough(ISymbol member) =>
        this.Resolve(member)
            .Aggregate(seed: LoopState.None, func: (joined, actual) => joined.Join(this.StateOf(actual)));

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
            hasParts: first.HasParts || second.HasParts);
    }

    // An element of a tuple has a name in the code and a position in the type. The position is
    // the key, so that a read by a different name finds the same element.
    private static ISymbol KeyOf(ISymbol member) =>
        member is IFieldSymbol { CorrespondingTupleField: { } element }
            ? element.OriginalDefinition
            : member.OriginalDefinition;
}
