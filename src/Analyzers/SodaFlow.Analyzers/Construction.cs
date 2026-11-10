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
/// <param name="type">
///     The type of the object, when the evaluator knows the constructor that
///     made it. A member that the code reads through an interface or a base type
///     is then the member of this type.
/// </param>
internal sealed class Construction(in ImmutableDictionary<ISymbol, LoopState> members, in INamedTypeSymbol? type)
{
    private readonly ImmutableDictionary<ISymbol, LoopState> members = members;
    private readonly INamedTypeSymbol? type = type;

    /// <summary>An object with no member that the evaluator knows.</summary>
    public static Construction Empty { get; } =
        new(members: ImmutableDictionary.Create<ISymbol, LoopState>(SymbolEqualityComparer.Default), type: null);

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
        new(members: this.members.SetItem(key: KeyOf(member), value: state), type: this.type);

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

        return new Construction(members: changed.ToImmutable(), type: this.type);
    }

    /// <summary>
    ///     The member of the type of this object for a member that the code reads
    ///     through an interface or a base type. The member itself when the
    ///     evaluator does not know the type.
    /// </summary>
    public ISymbol Resolve(ISymbol member)
    {
        if (this.type == null
            || (member.ContainingType?.TypeKind != TypeKind.Interface
                && member is { IsAbstract: false, IsVirtual: false, IsOverride: false }))
        {
            return member;
        }

        for (INamedTypeSymbol? current = this.type; current != null; current = current.BaseType)
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
        this.type == null
            ? []
            : this.type.AllInterfaces
                .Where(isInterface)
                .SelectMany(static contract => contract.GetMembers().OfType<IMethodSymbol>())
                .Select(this.type.FindImplementationForInterfaceMember)
                .OfType<IMethodSymbol>();

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
            type: SymbolEqualityComparer.Default.Equals(x: first.type, y: second.type) ? first.type : null);
    }

    // An element of a tuple has a name in the code and a position in the type. The position is
    // the key, so that a read by a different name finds the same element.
    private static ISymbol KeyOf(ISymbol member) =>
        member is IFieldSymbol { CorrespondingTupleField: { } element }
            ? element.OriginalDefinition
            : member.OriginalDefinition;
}
