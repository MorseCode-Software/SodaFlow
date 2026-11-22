using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using ComparedPart =
    (Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Written,
    Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Bound,
    Microsoft.CodeAnalysis.ITypeSymbol? Type,
    SodaFlow.Analyzers.LoopState? State);

namespace SodaFlow.Analyzers;

// The part of the evaluator for the members that C# calls with no call syntax. These are an
// operator, an indexer, a conversion, a deconstruction, a pattern, and a collection expression.
// They are also the members that a record writes, and the parts of a tuple comparison.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     A call that the code writes with no call syntax. The compiler binds an
    ///     operator, a conversion, an accessor, a <c>Deconstruct</c>, and some
    ///     other forms to a member. The evaluator reads that member as a call of
    ///     it. The result is null for a member with no body to read.
    /// </summary>
    /// <param name="call">The node that makes the call. A report about a member is at this node.</param>
    /// <param name="member">The method or the accessor that the node calls.</param>
    /// <param name="owner">
    ///     The state of the object, for an instance member. Null for the object
    ///     that this code is in.
    /// </param>
    /// <param name="arguments">
    ///     The expressions that the member gets, in the order of its parameters,
    ///     each with its state.
    /// </param>
    public Followed? FollowImplied(
        SyntaxNode call,
        IMethodSymbol member,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        if (this.implied.TryGetValue(key: (call, member), value: out Followed? known))
        {
            return known;
        }

        Followed? result =
            this.FollowImpliedOfTheObject(call: call, member: member, owner: owner, arguments: arguments);

        this.implied[(call, member)] = result;

        return result;
    }

    /// <summary>
    ///     The same call as <see cref="FollowImplied" />, but the walk does not
    ///     keep the result for the node. A serializer calls the same member of
    ///     each object in its value at one node. Each object has its own state.
    /// </summary>
    public Followed? FollowImpliedOfTheObject(
        SyntaxNode call,
        IMethodSymbol member,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        // The member of the object, when the code names one of an interface or a base type. A
        // value that is one of two objects has the member of each.
        IMethodSymbol[] actual =
        [
            .. owner?.Members?.Resolve(member.OriginalDefinition).OfType<IMethodSymbol>() ?? []
        ];

        return (actual.Length == 0 ? [member] : actual)
            .Select(static found => found.OriginalDefinition)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, definition) =>
                    Both(
                        first: all,
                        second: this.FollowImpliedDefinition(
                            call: call,
                            definition: definition,
                            owner: owner,
                            arguments: arguments)));
    }

    /// <summary>
    ///     A call of a member that the compiler writes for a record. The member is
    ///     an <c>Equals</c>, a <c>GetHashCode</c>, a <c>ToString</c>, or a
    ///     <c>Deconstruct</c>, and it has no body. Thus, the walk reads it as the
    ///     forms with no call syntax read it.
    /// </summary>
    private Followed? FollowRecordMemberCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!IsWrittenForARecord(method)
            || ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method) is not { } receiver)
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        // The Equals of a record calls no Equals of a member for null or for the same object, as
        // the == of a record does.
        if (method.Name == "Equals"
            && arguments.Count == 1
            && this.ReturnsAtOnce(call: invocation, left: receiver, right: arguments[0].Expression))
        {
            return Followed.Nothing;
        }

        return this.FollowImplied(
            call: invocation,
            member: method,
            owner: this.Self(receiver),
            arguments: [.. arguments.Select(argument => (argument.Expression, this.Eval(argument.Expression)))]);
    }

    private static bool IsWrittenForARecord(IMethodSymbol method) =>
        method is
        {
            IsImplicitlyDeclared: true,
            ContainingType.IsRecord: true,
            Name: "Equals" or "GetHashCode" or "ToString" or "Deconstruct"
        };

    private Followed? FollowImpliedDefinition(
        SyntaxNode call,
        IMethodSymbol definition,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        Followed? result = null;

        switch (definition)
        {
            case { Name: "ToString", IsImplicitlyDeclared: true, ContainingType.IsRecord: true }
                when owner != null && call is ExpressionSyntax printed:
                return this.FollowPrintedMembers(printed: printed, record: definition.ContainingType, owner: owner);
            case { Name: "Deconstruct", IsImplicitlyDeclared: true, ContainingType.IsRecord: true } when owner != null:
                return this.FollowRecordParts(call: call, deconstruct: definition, owner: owner);
            case
            {
                IsImplicitlyDeclared: true,
                ContainingType.IsRecord: true,
                Name: "Equals" or "GetHashCode" or "op_Equality" or "op_Inequality"
            }:
                return this.FollowMemberEquality(
                    call: call,
                    definition: definition,
                    owner: owner,
                    arguments: arguments);
            // The symbol of a member of an anonymous type is the member of object.
            case { Name: "Equals" or "GetHashCode", ContainingType.SpecialType: SpecialType.System_Object }
                when owner is { Members.IsAnonymous: true }:
                return this.FollowMemberEquality(
                    call: call,
                    definition: definition,
                    owner: owner,
                    arguments: arguments);
            case { Name: "ToString", Parameters.IsEmpty: true }
                when owner is { Members.IsAnonymous: true } && call is ExpressionSyntax printed:
                return owner.Members.Members.Aggregate(
                    seed: (Followed?)null,
                    func: (all, property) =>
                        Both(first: all, second: this.FollowToStringOf(call: printed, value: property.Value)));
            // The ToString of a tuple calls the ToString of each item.
            case { Name: "ToString", Parameters.IsEmpty: true }
                when owner is { Members.IsTuple: true } && call is ExpressionSyntax printed:
                return owner.Members.TupleElements.Aggregate(
                    seed: (Followed?)null,
                    func: (all, item) => Both(first: all, second: this.FollowToStringOf(call: printed, value: item)));
        }

        if (!this.region.Symbols.IsSodaFlow(definition) && BodyOf(definition) is { } body)
        {
            // Each argument after the last parameter is one more element of a params array, as a
            // dynamic call can give it.
            ImmutableArray<IParameterSymbol> parameters = definition.Parameters;

            IReadOnlyList<Binding> bindings =
            [
                .. arguments
                    .Take(parameters.LastOrDefault() is { IsParams: true } ? int.MaxValue : parameters.Length)
                    .Select((argument, position) =>
                        new Binding(
                            parameter: parameters[position < parameters.Length ? position : parameters.Length - 1],
                            argument: argument.Written,
                            state: argument.State))
            ];

            result =
                definition.IsStatic
                    ? this.Enter(
                        call: call,
                        name: ImpliedNameOf(definition),
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                        receiver: null,
                        target: null,
                        returnsAnObject: this.DoesNotSayItsClass(definition.ReturnType))
                    : this.Enter(
                        call: call,
                        name: ImpliedNameOf(definition),
                        body: body,
                        bindings: bindings,
                        definer: null,
                        members: owner == null ? this.thisMembers : TaggedMembers(owner),
                        receiver: owner,
                        target: null,
                        returnsAnObject: this.DoesNotSayItsClass(definition.ReturnType));
        }

        return result;
    }

    /// <summary>
    ///     <c>Equals</c>, <c>GetHashCode</c>, <c>==</c>, and <c>!=</c> that the
    ///     compiler writes for a record or an anonymous type. Each one compares or
    ///     hashes each field with <c>EqualityComparer&lt;T&gt;.Default</c>, which
    ///     calls <c>Equals</c> or <c>GetHashCode</c> of the value of the field.
    /// </summary>
    /// <remarks>
    ///     The <c>Equals</c> of the field gets the value of the same field of the
    ///     other object. An <c>Equals</c> of the record that the code writes is
    ///     the <c>Equals</c> that the other members call.
    /// </remarks>
    private Followed? FollowMemberEquality(
        SyntaxNode call,
        ISymbol definition,
        LoopState? owner,
        IEnumerable<(ExpressionSyntax Written, LoopState State)> arguments)
    {
        (ExpressionSyntax Written, LoopState State)[] given = [.. arguments];

        LoopState? self = owner;
        (ExpressionSyntax Written, LoopState State)? other = given.Length == 1 ? given[0] : null;

        // The operators get the two objects as their arguments.
        if (definition.IsStatic)
        {
            self = given.Length == 2 ? given[0].State : null;
            other = given.Length == 2 ? given[1] : null;
        }

        bool hashes = definition.Name == "GetHashCode";
        INamedTypeSymbol type = definition.ContainingType;

        if (self?.Members is not { } members
            || (!hashes
                && type.IsRecord
                && given.Length == 2
                && definition.IsStatic
                && this.ReturnsAtOnce(call: call, left: given[0].Written, right: given[1].Written)))
        {
            return null;
        }

        if (!hashes
            && type.IsRecord
            && other is { } argument
            && type.GetMembers("Equals")
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method =>
                        method is { IsImplicitlyDeclared: false, Parameters.Length: 1 }
                        && SymbolEqualityComparer.Default.Equals(x: method.Parameters[0].Type, y: type))
                is { } written)
        {
            return this.FollowImpliedDefinition(call: call, definition: written, owner: self, arguments: [argument]);
        }

        return ComparedMembersOf(type: type, members: members)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, field) =>
                    Both(
                        first: all,
                        second: this.FollowFieldEquality(
                            call: call,
                            value: StateOfAMember(
                                owner: self,
                                member: field,
                                implementations: this.region.Symbols.Implementations),
                            other: other is { } compared
                                ? (compared.Written,
                                    StateOfAMember(
                                        owner: compared.State,
                                        member: field,
                                        implementations: this.region.Symbols.Implementations))
                                : null,
                            hashes: hashes)));
    }

    // The == and the != of a record call no Equals of a member for null on the right. The same is
    // true for one object on each side. Null on the left gives no members to read,
    // and the walk stops before this test. A record struct has no null, and no name holds it.
    private bool ReturnsAtOnce(SyntaxNode call, ExpressionSyntax left, ExpressionSyntax right)
    {
        if (Unwrapped(right) is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression })
        {
            return true;
        }

        return this.NameOfAnObjectIn(left) is { Owner: null, Index: null } one
               && this.NameOfAnObjectIn(right) is { Owner: null, Index: null } other
               && (SymbolEqualityComparer.Default.Equals(x: one.Of, y: other.Of)
                   || this.AreOneObject(one: one.Of, other: other.Of, use: call, before: null));
    }

    // The members that the equality of a record compares. Each field gives its property, or itself
    // when it has no property. The fields of each base record count also. An anonymous type
    // compares each property.
    private static IEnumerable<ISymbol> ComparedMembersOf(INamedTypeSymbol type, Construction members)
    {
        if (members.IsAnonymous)
        {
            return members.Members.Select(static pair => pair.Key);
        }

        List<ISymbol> fields = [];

        for (INamedTypeSymbol? record = type; record is { IsRecord: true }; record = record.BaseType)
        {
            fields.AddRange(
                record.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(static field => !field.IsStatic)
                    .Select(static field => field.AssociatedSymbol ?? field));
        }

        return fields;
    }

    // EqualityComparer<T>.Default calls Equals of IEquatable<T> when the type of the field has it,
    // and Equals of object otherwise. It calls GetHashCode of object. The members of the object
    // resolve Equals of object to the Equals with one parameter that the type declares. That is
    // also the Equals of IEquatable<T>. Only an object of this compilation has a member to read.
    private Followed? FollowFieldEquality(
        LoopState value,
        SyntaxNode call,
        (ExpressionSyntax Written, LoopState State)? other,
        bool hashes)
    {
        IMethodSymbol? member = hashes ? this.region.Symbols.ObjectGetHashCode : this.region.Symbols.ObjectEquals;

        if (member == null || value.Members is not { } members)
        {
            return null;
        }

        return members.Resolve(member)
            .OfType<IMethodSymbol>()
            .Aggregate(
                seed: (Followed?)null,
                func: (all, actual) =>
                    Both(
                        first: all,
                        second: this.FollowImpliedDefinition(
                            call: call,
                            definition: actual.OriginalDefinition,
                            owner: value,
                            arguments: hashes || other is not { } compared ? [] : [compared])));
    }

    /// <summary>
    ///     The getters that the <c>ToString</c> of a record reads. The compiler
    ///     writes it, and it calls <c>PrintMembers</c>. The <c>PrintMembers</c> of
    ///     the compiler reads each public property of the record and of each base
    ///     record. A <c>PrintMembers</c> of the code replaces it, and the walk
    ///     reads that body.
    /// </summary>
    private Followed? FollowPrintedMembers(ExpressionSyntax printed, INamedTypeSymbol record, LoopState owner)
    {
        Followed? result = null;

        for (INamedTypeSymbol? type = record; type is { IsRecord: true }; type = type.BaseType)
        {
            if (type.GetMembers("PrintMembers")
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(static method => !method.IsImplicitlyDeclared) is { } written)
            {
                return Both(
                    first: result,
                    second: this.FollowImpliedDefinition(
                        call: printed,
                        definition: written,
                        owner: owner,
                        arguments: []));
            }

            result =
                type.GetMembers()
                    .Where(static member =>
                        member is IPropertySymbol
                            {
                                IsStatic: false,
                                IsIndexer: false,
                                IsOverride: false,
                                DeclaredAccessibility: Accessibility.Public,
                                GetMethod: not null
                            }
                            or IFieldSymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public })
                    .Aggregate(
                        seed: result,
                        func: (all, member) =>
                            Both(
                                first: all,
                                second: this.FollowPrinted(printed: printed, member: member, owner: owner)));
        }

        return result;
    }

    // PrintMembers reads the value of each member, and calls the ToString of that value.
    private Followed? FollowPrinted(ExpressionSyntax printed, ISymbol member, LoopState owner)
    {
        Followed? getter =
            member is IPropertySymbol property
                ? this.FollowGetterOn(access: printed, written: property, owner: owner)
                : null;

        return Both(
            first: getter,
            second: this.FollowToStringOf(
                call: printed,
                value: getter?.State
                       ?? StateOfAMember(
                           owner: owner,
                           member: member,
                           implementations: this.region.Symbols.Implementations)));
    }

    /// <summary>
    ///     The <c>ToString</c> of <c>object</c> on a value that a record prints,
    ///     on an item of a tuple, or on a part of a pair. Such a value has no
    ///     node of its own. Thus, the walk does not keep the result for the
    ///     node, as <see cref="FollowImplied" /> does.
    /// </summary>
    /// <remarks>
    ///     A record that holds itself has a state that holds an earlier state of
    ///     the record. A state does not hold itself. Thus, the walk through the
    ///     printed values always ends.
    /// </remarks>
    private Followed? FollowToStringOf(SyntaxNode call, LoopState value) =>
        this.region.Symbols.ObjectToString is { } toString && value.Members is { } members
            ? members.Resolve(toString)
                .OfType<IMethodSymbol>()
                .Aggregate(
                    seed: (Followed?)null,
                    func: (all, actual) =>
                        Both(
                            first: all,
                            second: this.FollowImpliedDefinition(
                                call: call,
                                definition: actual.OriginalDefinition,
                                owner: value,
                                arguments: [])))
            : null;

    /// <summary>
    ///     The <c>Deconstruct</c> that the compiler writes for a record. It gives
    ///     each <c>out</c> parameter the property or the field with the same name.
    ///     That member can be a member of a base record. The walk reads a getter
    ///     of the code as a call.
    /// </summary>
    private Followed FollowRecordParts(SyntaxNode call, IMethodSymbol deconstruct, LoopState owner)
    {
        Followed result = Followed.Nothing;

        foreach (IParameterSymbol parameter in deconstruct.Parameters)
        {
            ISymbol? member = null;
            INamedTypeSymbol? type = deconstruct.ContainingType;

            while (member == null && type != null)
            {
                member =
                    type.GetMembers(parameter.Name)
                        .FirstOrDefault(static found =>
                            found is IPropertySymbol { IsStatic: false, IsIndexer: false }
                                or IFieldSymbol { IsStatic: false });

                type = type.BaseType;
            }

            Followed? getter =
                member is IPropertySymbol { IsStatic: false } property
                && GetterOf(property.OriginalDefinition) is { } body
                    ? this.Enter(
                        call: call,
                        name: property.Name,
                        body: body,
                        bindings: [],
                        definer: null,
                        members: TaggedMembers(owner),
                        receiver: owner,
                        target: null)
                    : null;

            LoopState part =
                getter?.State
                ?? (member == null
                    ? LoopState.None
                    : StateOfAMember(
                        owner: owner,
                        member: member,
                        implementations: this.region.Symbols.Implementations));

            result =
                new Followed(
                    state: LoopState.None,
                    findings: getter == null ? result.Findings : result.Findings.AddRange(getter.Findings),
                    outs: result.Outs.SetItem(key: parameter, value: part),
                    sets: result.Sets,
                    through: result.Through,
                    alwaysGives: result.AlwaysGives);
        }

        return result;
    }

    /// <summary>
    ///     The state of a member of a value with the given state, where no
    ///     getter of the code runs. A property pattern and the
    ///     <c>Deconstruct</c> of a record read a member this way.
    /// </summary>
    public static LoopState StateOfAMember(LoopState owner, ISymbol member, ImplementationIndex implementations)
    {
        if (owner.Members == null)
        {
            return owner;
        }

        // A pair that the evaluator saw made has a state for each of its parts.
        if (SodaFlowSymbols.HoldsParts(member.ContainingType) && owner.Members.Has(member))
        {
            return owner.Members.StateOf(member).FromIfUnknown(owner.Origin);
        }

        if (owner.Members.CanOwn(member))
        {
            return owner.Members.StateThrough(member: member, implementations: implementations)
                .FromIfUnknown(owner.Origin);
        }

        // The members are those of an object that the owner contains, as a pair of the platform
        // contains its value. A member that is not a sequence is one of those values.
        ITypeSymbol? type =
            member switch
            {
                IPropertySymbol read => read.Type,
                IFieldSymbol read => read.Type,
                _ => null
            };

        return SodaFlowSymbols.IsSequence(type) ? owner : owner.AsElement;
    }

    /// <summary>
    ///     The members that a collection expression calls on a collection type
    ///     of this compilation. A type with a collection builder gets the
    ///     elements from the <c>Create</c> method of the builder. Each other type
    ///     gets each element from an <c>Add</c> method.
    /// </summary>
    /// <remarks>
    ///     The version of the compiler that the analyzer builds against has no
    ///     operation for a collection expression. Thus, the evaluator finds the
    ///     <c>Create</c> method from the attribute, and the <c>Add</c> method that
    ///     takes the type of the element.
    /// </remarks>
    public Followed? FollowCollectionExpression(CollectionExpressionSyntax collection)
    {
        if (this.region.TypeOf(collection) is not INamedTypeSymbol { DeclaringSyntaxReferences.IsEmpty: false } made)
        {
            return null;
        }

        if (BuilderOf(made) is { } create)
        {
            return this.FollowImplied(
                call: collection,
                member: create,
                owner: null,
                arguments: [(collection, this.Eval(collection))]);
        }

        if (!this.region.Symbols.DeclaresAdd)
        {
            return null;
        }

        IMethodSymbol[] adds =
        [
            .. Ancestry(made)
                .SelectMany(static type => type.GetMembers("Add"))
                .OfType<IMethodSymbol>()
                .Where(static add => add is { IsStatic: false, Parameters.Length: 1 })
        ];

        IMethodSymbol[]? extensions = null;

        // A spread gives each element of its value to one Add. The walk reads that Add one time,
        // with the state of an element.
        return collection.Elements
            .Aggregate(
                seed: (Followed?)null,
                func: (all, element) =>
                    element switch
                    {
                        ExpressionElementSyntax one => Both(
                            first: all,
                            second: FollowAdd(
                                written: one.Expression,
                                type: this.region.TypeOf(one.Expression),
                                state: this.Eval(one.Expression))),
                        SpreadElementSyntax spread => Both(first: all, second: FollowSpread(spread.Expression)),
                        _ => all
                    });

        Followed? FollowSpread(ExpressionSyntax written)
        {
            ITypeSymbol? type = SodaFlowSymbols.ElementTypeOf(this.region.TypeOf(written));

            return FollowAdd(
                written: written,
                type: type,
                state: ElementOf(collection: this.Eval(written), type: type));
        }

        Followed? FollowAdd(ExpressionSyntax written, ITypeSymbol? type, LoopState state)
        {
            if (adds.FirstOrDefault(Takes) is { } add)
            {
                return this.FollowImplied(
                    call: written,
                    member: add,
                    owner: this.Eval(collection),
                    arguments: [(written, state)]);
            }

            // With no Add of the type, the compiler takes an extension method. That method gets
            // the collection as its first argument.
            extensions ??=
            [
                .. this.region.ModelFor(collection)
                    .LookupSymbols(
                        position: collection.SpanStart,
                        container: made,
                        name: "Add",
                        includeReducedExtensionMethods: true)
                    .OfType<IMethodSymbol>()
                    .Where(static found => found is { ReducedFrom: not null, Parameters.Length: 1 })
            ];

            return extensions.FirstOrDefault(Takes) is { ReducedFrom: { } extension }
                ? this.FollowImplied(
                    call: written,
                    member: extension.OriginalDefinition,
                    owner: null,
                    arguments: [(collection, this.Eval(collection)), (written, state)])
                : null;

            bool Takes(IMethodSymbol candidate) =>
                type == null || this.region.Converts(from: type, to: candidate.Parameters[0].Type);
        }

        static IEnumerable<INamedTypeSymbol> Ancestry(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
            {
                yield return current;
            }
        }
    }

    /// <summary>
    ///     The conversion that a spread makes on each element, when the
    ///     collection has a different type of element. An operator of this
    ///     compilation can make it. No node names that conversion.
    /// </summary>
    public Followed? FollowSpreadConversion(CollectionExpressionSyntax collection, ExpressionSyntax spread) =>
        this.region.Symbols.DeclaresConversions
        && SodaFlowSymbols.ElementTypeOf(this.region.TypeOf(spread)) is { } from
        && SodaFlowSymbols.ElementTypeOf(this.region.TypeOf(collection)) is { } to
        && this.region.ConversionOperator(from: from, to: to) is { } conversion
            ? this.FollowImplied(
                call: spread,
                member: conversion,
                owner: null,
                arguments: [(spread, ElementOf(collection: this.Eval(spread), type: from))])
            : null;

    // The method that the CollectionBuilder attribute of a type names: a static method of the
    // builder type, with one parameter, the span of the elements.
    private static IMethodSymbol? BuilderOf(ISymbol type) =>
        type.GetAttributes()
            .Where(static attribute =>
                attribute.AttributeClass is { Name: "CollectionBuilderAttribute" } builder
                && builder.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices")
            .Select(static attribute => attribute.ConstructorArguments)
            .Where(static arguments => arguments.Length == 2)
            .SelectMany(static arguments =>
                arguments[0].Value is INamedTypeSymbol builder && arguments[1].Value is string name
                    ? builder.GetMembers(name).OfType<IMethodSymbol>()
                    : [])
            .FirstOrDefault(static create => create is { IsStatic: true, Parameters.Length: 1 });

    /// <summary>
    ///     The copy constructor that a <c>with</c> expression on a record calls,
    ///     when the code of the record declares one. The constructor of the
    ///     compiler only copies the fields, and the walk does not read it.
    /// </summary>
    public Followed? FollowCopy(WithExpressionSyntax copy) =>
        this.region.TypeOf(copy.Expression) is INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Class } record
        && record.InstanceConstructors.FirstOrDefault(static constructor =>
                constructor is { IsImplicitlyDeclared: false } && IsCopyConstructor(constructor))
            is { } copier
            ? this.FollowConstructor(call: copy, constructor: copier, arguments: default, copied: copy.Expression)
            : null;

    // The constructor of a record class that gets one value of the record.
    private static bool IsCopyConstructor(IMethodSymbol constructor) =>
        constructor is { ContainingType: { IsRecord: true, TypeKind: TypeKind.Class }, Parameters.Length: 1 }
        && SymbolEqualityComparer.Default.Equals(x: constructor.Parameters[0].Type, y: constructor.ContainingType);

    /// <summary>
    ///     The call that an expression makes with no call syntax. It is the getter
    ///     of an indexer, an operator, or a conversion that the code writes as a
    ///     cast. A concatenation also calls <c>ToString</c>. The result is null for
    ///     an expression with no such call.
    /// </summary>
    public Followed? FollowExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ElementAccessExpressionSyntax access
                when this.region.Symbols.DeclaresIndexers || this.region.Symbols.DeclaresSlice =>
                this.FollowIndexer(access: access, owner: access.Expression, indexes: access.ArgumentList),
            ElementBindingExpressionSyntax binding
                when (this.region.Symbols.DeclaresIndexers || this.region.Symbols.DeclaresSlice)
                     && ConditionalReceiver(binding) is { } owner =>
                this.FollowIndexer(access: binding, owner: owner, indexes: binding.ArgumentList),
            // The == and != of a record are operators that no declaration names.
            BinaryExpressionSyntax binary
                when this.region.Symbols.DeclaresOperators
                     || this.region.Symbols.DeclaresToString
                     || (this.region.Symbols.DeclaresRecords
                         && (binary.IsKind(SyntaxKind.EqualsExpression)
                             || binary.IsKind(SyntaxKind.NotEqualsExpression))) =>
                this.region.SymbolOf(binary) switch
                {
                    IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } operation
                        when binary.IsKind(SyntaxKind.LogicalAndExpression)
                             || binary.IsKind(SyntaxKind.LogicalOrExpression) =>
                        this.FollowShortCircuit(binary: binary, operation: operation),
                    IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } operation =>
                        this.FollowOperator(call: binary, operation: operation, operands: [binary.Left, binary.Right]),
                    // A concatenation calls ToString on an operand that is not a string.
                    IMethodSymbol
                    {
                        MethodKind: MethodKind.BuiltinOperator,
                        ContainingType.SpecialType: SpecialType.System_String
                    } => Both(first: this.FollowToString(binary.Left), second: this.FollowToString(binary.Right)),
                    // The binder finds the operator of a dynamic operand at run time. The compiler
                    // gives such an operation a built-in operator with a dynamic result.
                    IMethodSymbol { MethodKind: MethodKind.BuiltinOperator, ReturnType.TypeKind: TypeKind.Dynamic }
                        when this.region.Symbols.DeclaresOperators =>
                        this.FollowDynamicOperator(operation: binary, operands: [binary.Left, binary.Right]),
                    // A tuple equality has no symbol. It compares each pair of elements.
                    null when this.region.Symbols.DeclaresOperators
                              && (binary.IsKind(SyntaxKind.EqualsExpression)
                                  || binary.IsKind(SyntaxKind.NotEqualsExpression)) =>
                        this.FollowTupleEquality(binary),
                    _ => null
                },
            PrefixUnaryExpressionSyntax prefix when this.region.Symbols.DeclaresOperators =>
                this.region.SymbolOf(prefix)
                    is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator, ReturnType.TypeKind: TypeKind.Dynamic }
                    ? this.FollowDynamicOperator(operation: prefix, operands: [prefix.Operand])
                    : this.FollowOperator(
                        call: prefix,
                        operation: this.region.SymbolOf(prefix),
                        operands: [prefix.Operand]),
            PostfixUnaryExpressionSyntax postfix when this.region.Symbols.DeclaresOperators =>
                this.region.SymbolOf(postfix)
                    is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator, ReturnType.TypeKind: TypeKind.Dynamic }
                    ? this.FollowDynamicOperator(operation: postfix, operands: [postfix.Operand])
                    : this.FollowOperator(
                        call: postfix,
                        operation: this.region.SymbolOf(postfix),
                        operands: [postfix.Operand]),
            CastExpressionSyntax cast when this.region.Symbols.DeclaresOperators => this.FollowCast(cast),
            // The compiler gives a plain assignment no operator, and only an operator of this
            // compilation has a body to read. Thus, these two tests are only for speed.
            AssignmentExpressionSyntax compound
                when this.region.Symbols.DeclaresOperators
                     && !compound.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                this.region.SymbolOf(compound)
                    is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator, ReturnType.TypeKind: TypeKind.Dynamic }
                    ? this.FollowDynamicOperator(operation: compound, operands: [compound.Left, compound.Right])
                    : this.FollowOperator(
                        call: compound,
                        operation: this.region.SymbolOf(compound),
                        operands: [compound.Left, compound.Right]),
            _ => null
        };

    /// <summary>
    ///     A user-defined <c>&amp;&amp;</c> or <c>||</c>. It calls
    ///     <c>operator false</c> or <c>operator true</c> of the type of its
    ///     operator on the left operand. It then calls the operator, unless that
    ///     test decides the result. No node names the test.
    /// </summary>
    private Followed? FollowShortCircuit(BinaryExpressionSyntax binary, ISymbol operation) =>
        Both(
            first: operation.ContainingType
                    .GetMembers(binary.IsKind(SyntaxKind.LogicalAndExpression) ? "op_False" : "op_True")
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(static test => test.Parameters.Length == 1)
                is { } test
                ? this.FollowOperator(call: binary.Left, operation: test, operands: [binary.Left])
                : null,
            second: this.FollowOperator(call: binary, operation: operation, operands: [binary.Left, binary.Right]));

    /// <summary>
    ///     The operators that a tuple equality calls, one for each pair of
    ///     elements. No node or symbol names them. Thus, the evaluator asks the
    ///     compiler which operator a comparison of the two elements binds to.
    /// </summary>
    /// <remarks>
    ///     A side that is not a tuple expression, as a local or the result of a
    ///     call, has no node for each element. The question to the compiler then
    ///     names the element as <c>ItemN</c> of that side. The state of the
    ///     element comes from the members of the state of the side.
    /// </remarks>
    private Followed? FollowTupleEquality(BinaryExpressionSyntax comparison)
    {
        ComparedPart[] lefts =
            [.. this.PartsOf(written: comparison.Left, bound: comparison.Left, type: null, state: null)];

        ComparedPart[] rights =
            [.. this.PartsOf(written: comparison.Right, bound: comparison.Right, type: null, state: null)];

        if (lefts.Length != rights.Length)
        {
            return null;
        }

        (ComparedPart Left, ComparedPart Right)[] pairs =
            [.. lefts.Zip(second: rights, resultSelector: static (one, other) => (one, other))];

        // The evaluator keeps one result for each node and member. Two parts of a tuple that is
        // not a tuple expression have the same node. Thus, each pair of parts of the same two such
        // tuples gets the states of all those pairs, and gives the same result.
        return pairs.Aggregate(
            seed: (Followed?)null,
            func: (all, pair) =>
                Both(
                    first: all,
                    second: pair is { Left.State: not null, Right.State: not null }
                        ? this.FollowPartEquality(
                            comparison: comparison,
                            left: pair.Left with { State = Joined(of: pair, side: static part => part.Left) },
                            right: pair.Right with { State = Joined(of: pair, side: static part => part.Right) })
                        : this.FollowPartEquality(comparison: comparison, left: pair.Left, right: pair.Right)));

        LoopState Joined(
            (ComparedPart Left, ComparedPart Right) of,
            Func<(ComparedPart Left, ComparedPart Right), ComparedPart> side) =>
            pairs
                .Where(other =>
                    other is { Left.State: not null, Right.State: not null }
                    && other.Left.Written == of.Left.Written
                    && other.Right.Written == of.Right.Written)
                .Aggregate(seed: LoopState.None, func: (state, other) => state.Join(side(other).State ?? LoopState.None));
    }

    // The elements that a tuple equality compares, in their order. An element that is a tuple
    // gives its own elements. A part with no state is a node of the code, and the evaluator reads
    // its state and its type there.
    private IEnumerable<ComparedPart> PartsOf(
        ExpressionSyntax written,
        ExpressionSyntax bound,
        ITypeSymbol? type,
        LoopState? state)
    {
        if (state == null && Unwrapped(written) is TupleExpressionSyntax tuple)
        {
            return tuple.Arguments.SelectMany(argument =>
                this.PartsOf(written: argument.Expression, bound: argument.Expression, type: null, state: null));
        }

        ITypeSymbol? actual = state == null ? this.region.TypeOf(written) : type;

        if (actual is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            return [(Written: written, Bound: bound.WithoutTrivia(), Type: actual, State: state)];
        }

        LoopState whole = state ?? this.Eval(written);

        return tupleType.TupleElements.SelectMany((element, position) =>
            this.PartsOf(
                written: written,
                bound: SyntaxFactory.MemberAccessExpression(
                    kind: SyntaxKind.SimpleMemberAccessExpression,
                    expression: SyntaxFactory.ParenthesizedExpression(bound.WithoutTrivia()),
                    name: SyntaxFactory.IdentifierName("Item" + (position + 1))),
                type: element.Type,
                state: whole.Members is { } members && members.Has(element) ? members.StateOf(element) : whole));
    }

    private Followed? FollowPartEquality(CSharpSyntaxNode comparison, ComparedPart left, ComparedPart right)
    {
        // Only an operator of this compilation has a body to read, and it takes a type of this
        // compilation. The question to the compiler is for such a pair only.
        if (left.Type is not { DeclaringSyntaxReferences.IsEmpty: false }
            && right.Type is not { DeclaringSyntaxReferences.IsEmpty: false })
        {
            return null;
        }

        ExpressionSyntax compared =
            SyntaxFactory.BinaryExpression(kind: comparison.Kind(), left: left.Bound, right: right.Bound);

        if (this.region.ModelFor(comparison)
                .GetSpeculativeSymbolInfo(
                    position: comparison.SpanStart,
                    expression: compared,
                    bindingOption: SpeculativeBindingOption.BindAsExpression)
                .Symbol
            is not IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } operation)
        {
            return null;
        }

        // A part of a tuple that is not a tuple expression has the node of the whole tuple. Thus,
        // the call is the node of a part that is a node of the code, when there is one.
        ExpressionSyntax call = left.State == null || right.State != null ? left.Written : right.Written;

        Followed? result =
            this.FollowImplied(
                call: call,
                member: operation,
                owner: null,
                arguments:
                [
                    (left.Written, left.State ?? this.Eval(left.Written)),
                    (right.Written, right.State ?? this.Eval(right.Written))
                ]);

        // The compiler converts a result that is not bool with an operator of the code.
        return operation.ReturnType.SpecialType != SpecialType.System_Boolean
               && this.region.ConversionToBoolean(operation.ReturnType) is { } conversion
            ? Both(
                first: result,
                second: this.FollowImplied(
                    call: call,
                    member: conversion,
                    owner: null,
                    arguments: [(call, result?.State ?? LoopState.None)]))
            : result;
    }

    /// <summary>
    ///     The conversion that the compiler adds to an expression, when an
    ///     operator of this compilation makes it.
    /// </summary>
    public Followed? FollowConversion(ExpressionSyntax expression)
    {
        if (!this.region.Symbols.DeclaresConversions)
        {
            return null;
        }

        if (this.region.ConversionOf(expression) is { } conversion)
        {
            return this.FollowOperator(call: expression, operation: conversion, operands: [expression]);
        }

        if (this.region.ConvertsADynamic(expression))
        {
            return this.region.ModelFor(expression).GetTypeInfo(expression).ConvertedType is { } to
                ? this.FollowDynamicConversion(call: expression, expression: expression, to: to)
                : null;
        }

        return this.region.ConvertsATuple(expression)
            ? this.FollowElementConversions(
                call: expression,
                parts: this.ElementConversions(
                    from: UnderNullable(this.region.TypeOf(expression)),
                    to: UnderNullable(this.region.ModelFor(expression).GetTypeInfo(expression).ConvertedType),
                    whole: this.Eval(expression)))
            : null;
    }

    // A cast with no operator of the code can convert a tuple to a different tuple type. That
    // conversion converts each element. No node or symbol names the operator of an element.
    private Followed? FollowCast(CastExpressionSyntax cast)
    {
        ISymbol? operation = this.region.SymbolOf(cast);

        // The binder converts a dynamic value with the conversion of the type of its object.
        if (operation == null
            && this.region.Symbols.DeclaresConversions
            && this.region.TypeOf(cast.Expression) is { TypeKind: TypeKind.Dynamic }
            && this.region.TypeOf(cast) is { } type)
        {
            return this.FollowDynamicConversion(call: cast, expression: cast.Expression, to: type);
        }

        return operation == null
               && this.region.Symbols.DeclaresConversions
               && UnderNullable(this.region.TypeOf(cast)) is INamedTypeSymbol { IsTupleType: true } to
            ? this.FollowElementConversions(
                call: cast,
                parts: this.ElementConversions(
                    from: UnderNullable(this.region.TypeOf(cast.Expression)),
                    to: to,
                    whole: this.Eval(cast.Expression)))
            : this.FollowOperator(call: cast, operation: operation, operands: [cast.Expression]);
    }

    private static ITypeSymbol? UnderNullable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    /// <summary>
    ///     The conversions of the parts that a <c>Deconstruct</c> method gives
    ///     to the variables of a deconstruction. No node names them, and the
    ///     compiler gives each one in the deconstruction information.
    /// </summary>
    /// <param name="deconstruction">
    ///     The assignment, or the variable of the foreach statement.
    /// </param>
    /// <param name="value">The expression of the value, for a report.</param>
    /// <param name="states">The state of each out parameter, in order.</param>
    public Followed? FollowOutConversions(
        SyntaxNode deconstruction,
        ExpressionSyntax value,
        IReadOnlyList<LoopState> states)
    {
        DeconstructionInfo info =
            this.DeconstructionInfoOf(
                deconstruction.Parent is ForEachVariableStatementSyntax loop && loop.Variable == deconstruction
                    ? loop
                    : deconstruction);

        if (info.Method is not { } method)
        {
            return null;
        }

        IParameterSymbol[] outs = [.. method.Parameters.Where(static parameter => parameter.RefKind == RefKind.Out)];

        return this.FollowElementConversions(
            call: value,
            parts: info.Nested.SelectMany((part, position) =>
            {
                LoopState state = position < states.Count ? states[position] : LoopState.None;

                return part.Conversion is { IsUserDefined: true, MethodSymbol: { } conversion }
                    ? [(conversion, state)]
                    : PartConversions(
                        info: part,
                        type: position < outs.Length ? outs[position].Type : null,
                        whole: state);
            }));
    }

    /// <summary>
    ///     The conversions of the elements that a deconstruction of a tuple
    ///     gives to its variables. No node names them, and the compiler gives
    ///     each one in the deconstruction information.
    /// </summary>
    /// <param name="deconstruction">The assignment or the foreach statement.</param>
    /// <param name="value">The expression of the tuple, for a report.</param>
    /// <param name="tuple">The state of the tuple.</param>
    /// <param name="type">The type of the tuple.</param>
    public Followed? FollowPartConversions(
        SyntaxNode deconstruction,
        ExpressionSyntax value,
        LoopState tuple,
        ITypeSymbol? type)
    {
        DeconstructionInfo info = this.DeconstructionInfoOf(deconstruction);

        return this.FollowElementConversions(call: value, parts: PartConversions(info: info, type: type, whole: tuple));
    }

    private DeconstructionInfo DeconstructionInfoOf(SyntaxNode deconstruction) =>
        deconstruction switch
        {
            AssignmentExpressionSyntax assignment => this.region.ModelFor(assignment).GetDeconstructionInfo(assignment),
            ForEachVariableStatementSyntax loop => this.region.ModelFor(loop).GetDeconstructionInfo(loop),
            _ => default
        };

    // The conversion of each element of a tuple to the element of a second tuple type, with the
    // state of the element. An element that is a tuple gives the conversions of its own elements.
    private IEnumerable<(IMethodSymbol Conversion, LoopState State)> ElementConversions(
        ITypeSymbol? from,
        ITypeSymbol? to,
        LoopState whole)
    {
        if (from is not INamedTypeSymbol { IsTupleType: true } source
            || to is not INamedTypeSymbol { IsTupleType: true } target
            || source.TupleElements.Length != target.TupleElements.Length)
        {
            return [];
        }

        return source.TupleElements.SelectMany((element, position) =>
        {
            LoopState state = StateOfAnElement(whole: whole, element: element);
            ITypeSymbol goal = target.TupleElements[position].Type;

            return this.region.ConversionOperator(from: element.Type, to: goal) is { } conversion
                ? [(conversion, state)]
                : this.ElementConversions(from: element.Type, to: goal, whole: state);
        });
    }

    // The same for the parts of a deconstruction, from the conversion that the compiler gives each
    // part.
    private static IEnumerable<(IMethodSymbol Conversion, LoopState State)> PartConversions(
        DeconstructionInfo info,
        ITypeSymbol? type,
        LoopState whole)
    {
        if (info.Method != null
            || type is not INamedTypeSymbol { IsTupleType: true } tuple
            || tuple.TupleElements.Length != info.Nested.Length)
        {
            return [];
        }

        return tuple.TupleElements.SelectMany((element, position) =>
        {
            DeconstructionInfo part = info.Nested[position];
            LoopState state = StateOfAnElement(whole: whole, element: element);

            return part.Conversion is { IsUserDefined: true, MethodSymbol: { } conversion }
                ? [(conversion, state)]
                : PartConversions(info: part, type: element.Type, whole: state);
        });
    }

    private static LoopState StateOfAnElement(LoopState whole, ISymbol element) =>
        whole.Members is { } members && members.Has(element) ? members.StateOf(element) : whole;

    // The evaluator keeps one result for each node and operator. Two elements that one operator
    // converts thus get one walk, with the states of the two.
    private Followed? FollowElementConversions(
        ExpressionSyntax call,
        IEnumerable<(IMethodSymbol Conversion, LoopState State)> parts) =>
        parts
            .GroupBy<(IMethodSymbol Conversion, LoopState State), IMethodSymbol, LoopState>(
                keySelector: static part => part.Conversion,
                elementSelector: static part => part.State,
                comparer: SymbolEqualityComparer.Default)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, conversion) =>
                    Both(
                        first: all,
                        second: this.FollowImplied(
                            call: call,
                            member: conversion.Key,
                            owner: null,
                            arguments:
                            [
                                (call,
                                    conversion.Aggregate(
                                        seed: LoopState.None,
                                        func: static (one, other) => one.Join(other)))
                            ])));

    // The value that the code holds where the compiler converts an expression with an operator of
    // this compilation. That is the result of the operator, and not the value that it gets. The
    // walk asks at the places where SinkWalker asks. Thus, the two get the one result of
    // FollowImplied for the call.
    private LoopState? ConvertedBy(ExpressionSyntax expression, LoopState value) =>
        expression.Parent is not ParenthesizedExpressionSyntax
        && !SinkWalker.NeverConverts(expression)
        && this.region.ConversionOf(expression) is { } conversion
        && this.FollowImplied(call: expression, member: conversion, owner: null, arguments: [(expression, value)])
            is { } converted
            ? converted.State
            : null;

    /// <summary>
    ///     The call of <c>operator true</c> or <c>operator false</c> that a
    ///     condition makes on its value. No node names that operator, thus the
    ///     evaluator asks for the operation of the condition. It asks only in a
    ///     compilation that declares such an operator.
    /// </summary>
    /// <remarks>
    ///     The walk asks for each expression. Thus, the operation must be a test
    ///     for true or false, and not a different operator of the expression.
    /// </remarks>
    public Followed? FollowTruth(ExpressionSyntax condition) =>
        this.region.Symbols.DeclaresTruth
            ? this.region.ModelFor(condition).GetOperation(condition)?.Parent switch
            {
                IUnaryOperation
                {
                    OperatorKind: UnaryOperatorKind.True or UnaryOperatorKind.False,
                    OperatorMethod: { } truth
                } => this.FollowOperator(call: condition, operation: truth, operands: [condition]),
                // The binder finds the operator of a dynamic value at run time.
                IUnaryOperation
                {
                    OperatorKind: UnaryOperatorKind.True or UnaryOperatorKind.False,
                    Operand.Type.TypeKind: TypeKind.Dynamic
                } test => this.FollowDynamicTruth(
                    condition: condition,
                    name: test.OperatorKind == UnaryOperatorKind.True ? "op_True" : "op_False"),
                _ => null
            }
            : null;

    /// <summary>
    ///     The call of <c>ToString</c> that a concatenation makes on an object of
    ///     this compilation.
    /// </summary>
    private Followed? FollowToString(ExpressionSyntax written) =>
        this.region.Symbols is { ObjectToString: { } toString } symbols
        && (symbols.DeclaresToString || symbols.DeclaresRecords)
        && this.Self(written) is { Members: not null } owner
            ? this.FollowImplied(call: written, member: toString, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The call of <c>ToString</c> that an interpolation makes on its value,
    ///     when the interpolated string formats it now. A handler of this
    ///     compilation gets the value in its <c>AppendFormatted</c>.
    /// </summary>
    public Followed? FollowInterpolation(InterpolationSyntax interpolation)
    {
        // An interpolated string handler of this compilation gets the value in its AppendFormatted,
        // which formats it, and the platform does not. Only the string of a handler has a part for
        // each call of the handler.
        if (this.region.Symbols.DeclaresHandlers
            && interpolation.Parent is InterpolatedStringExpressionSyntax handled
            && this.region.ModelFor(handled).GetOperation(handled) is IInterpolatedStringOperation content
            && content.Parts
                    .OfType<IInterpolatedStringAppendOperation>()
                    .FirstOrDefault(part => interpolation.Span.Contains(part.Syntax.Span))
                    ?.AppendCall
                is IInvocationOperation { TargetMethod: { DeclaringSyntaxReferences.IsEmpty: false } append })
        {
            return content.Parent is IInterpolatedStringHandlerCreationOperation creation
                   && this.AppendsNothing(creation)
                ? null
                : this.FollowImplied(
                    call: interpolation,
                    member: append,
                    owner: LoopState.None,
                    arguments: [(interpolation.Expression, this.Eval(interpolation.Expression))]);
        }

        // A KeyValuePair, a DictionaryEntry, or a Tuple object prints the ToString of each part. Its
        // state has the objects of the parts, as the state of a container has them.
        if (this.region.Symbols is { ObjectToString: not null, DeclaresToString: true }
            && interpolation.Parent is InterpolatedStringExpressionSyntax printed
            && SodaFlowSymbols.HoldsParts(this.region.TypeOf(interpolation.Expression))
            && this.FormatsNow(printed))
        {
            return this.FollowToStringOf(call: interpolation.Expression, value: this.Eval(interpolation.Expression));
        }

        if (this.region.Symbols is not { ObjectToString: { } toString } symbols
            || !(symbols.DeclaresToString || symbols.DeclaresRecords)
            || this.Self(interpolation.Expression) is not { Members: { } members } owner
            || interpolation.Parent is not InterpolatedStringExpressionSyntax whole
            || !this.FormatsNow(whole))
        {
            return null;
        }

        ExpressionSyntax value = interpolation.Expression;

        // The formatter calls TryFormat of ISpanFormattable on an object that implements it. It
        // calls the ToString of IFormattable on each other object that implements that interface,
        // and the ToString of object on each other object. Of two objects, the walk reads each one.
        if (symbols is not { DeclaresToString: true, FormattableToString: { } formattable }
            || !members.Is(formattable.ContainingType))
        {
            return this.FollowImplied(call: value, member: toString, owner: owner, arguments: []);
        }

        return symbols.SpanFormattableTryFormat is { } tryFormat && members.Is(tryFormat.ContainingType)
            ? this.FollowNarrowed(value: value, member: tryFormat, owner: owner, others: FormattedByToString)
            : FormattedByToString();

        Followed? FormattedByToString() =>
            this.FollowNarrowed(
                value: value,
                member: formattable,
                owner: owner,
                others: () => this.FollowImplied(call: value, member: toString, owner: owner, arguments: []));
    }

    /// <summary>
    ///     The constructor of an interpolated string handler of this
    ///     compilation, and its <c>AppendLiteral</c> for each part of text. No
    ///     node names them. A constructor that sets <c>shouldAppend</c> to the
    ///     constant false stops each append.
    /// </summary>
    /// <param name="handled">The interpolated string.</param>
    public Followed? FollowHandler(InterpolatedStringExpressionSyntax handled)
    {
        if (!this.region.Symbols.DeclaresHandlers
            || this.region.ModelFor(handled).GetOperation(handled)
                is not IInterpolatedStringOperation
                {
                    Parent: IInterpolatedStringHandlerCreationOperation creation
                } content)
        {
            return null;
        }

        Followed? made =
            creation.HandlerCreation is IObjectCreationOperation
            {
                Constructor: { DeclaringSyntaxReferences.IsEmpty: false } constructor
            }
                ? this.FollowConstructor(call: handled, constructor: constructor, arguments: default)
                : null;

        return this.AppendsNothing(creation)
            ? made
            : content.Parts
                .OfType<IInterpolatedStringAppendOperation>()
                .Where(static part => part.Syntax is InterpolatedStringTextSyntax)
                .Select(static part => part.AppendCall)
                .OfType<IInvocationOperation>()
                .Where(static literal => !literal.TargetMethod.DeclaringSyntaxReferences.IsEmpty)
                .Aggregate(
                    seed: made,
                    func: (all, literal) =>
                        Both(
                            first: all,
                            second: this.FollowImplied(
                                call: literal.Syntax,
                                member: literal.TargetMethod,
                                owner: LoopState.None,
                                arguments: [])));
    }

    // True when the constructor of a handler sets its shouldAppend parameter only to the constant
    // false. The compiler then calls no append. A compound assignment of false to false gives false.
    private bool AppendsNothing(IInterpolatedStringHandlerCreationOperation creation) =>
        creation is
        {
            HandlerCreationHasSuccessParameter: true,
            HandlerCreation: IObjectCreationOperation
            {
                Constructor: { Parameters: { Length: > 0 } parameters } constructor
            }
        }
        && constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is ConstructorDeclarationSyntax declaration
        && declaration.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == parameters[parameters.Length - 1].Name)
            .All(use =>
                use.Parent is AssignmentExpressionSyntax assignment
                && this.region.ModelFor(assignment).GetConstantValue(assignment.Right)
                    is { HasValue: true, Value: false });

    // The call of a member of an interface on the objects of a value that implement it, and the
    // calls that the other objects get.
    private Followed? FollowNarrowed(
        SyntaxNode value,
        IMethodSymbol member,
        LoopState owner,
        Func<Followed?> others)
    {
        Construction members = owner.Members ?? Construction.Empty;
        Construction narrowed = members.Narrow(member.ContainingType);

        Followed? result =
            this.FollowImplied(call: value, member: member, owner: owner.WithMembers(narrowed), arguments: []);

        return ReferenceEquals(objA: narrowed, objB: members) ? result : Both(first: result, second: others());
    }

    // A FormattableString or an IFormattable keeps the values of the interpolated string, and
    // formats nothing at this point. A method of FormattableString that gets it formats it now.
    private bool FormatsNow(SyntaxNode whole) =>
        this.region.ModelFor(whole).GetOperation(whole)?.Parent is not IConversionOperation
        {
            Type:
            {
                Name: "FormattableString" or "IFormattable",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            }
        } kept
        || kept.Parent is IArgumentOperation
        {
            Parent: IInvocationOperation
            {
                TargetMethod.ContainingType:
                {
                    Name: "FormattableString",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                }
            }
        };

    /// <summary>
    ///     The call of <c>Dispose</c> that a <c>using</c> makes on an object of
    ///     this compilation, when the code leaves the statement or the block.
    /// </summary>
    public Followed? FollowDispose(ExpressionSyntax resource) =>
        this.region.Symbols is { DeclaresDispose: true, DisposableDispose: { } dispose }
        && this.Self(resource) is { Members: not null } owner
            ? this.FollowImplied(call: resource, member: dispose, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The call of <c>DisposeAsync</c> that an <c>await using</c> makes on
    ///     an object of this compilation, and true when the task that the call
    ///     gives is complete. The <c>await using</c> awaits that task.
    /// </summary>
    public (Followed? Followed, bool Complete) FollowDisposeAsync(ExpressionSyntax resource)
    {
        if (!this.region.Symbols.DeclaresDisposeAsync)
        {
            return (null, false);
        }

        // ConfigureAwait of a resource gives a struct of the platform, whose DisposeAsync calls the
        // member of IAsyncDisposable on the resource. Otherwise, the compiler looks for the method on
        // the type of the local, which can be an interface of the type of its initializer.
        ExpressionSyntax? configured = this.ResourceOfConfigureAwait(resource);

        IMethodSymbol? dispose =
            configured != null
                ? this.region.Symbols.AsyncDisposableDisposeAsync
                : this.region.ModelFor(resource).GetTypeInfo(resource).ConvertedType is { } type
                    ? this.DisposeAsyncOf(type)
                    : null;

        if (dispose == null)
        {
            return (null, false);
        }

        LoopState owner = this.Self(configured ?? resource);

        return (
            owner.Members == null
                ? null
                : this.FollowImplied(call: resource, member: dispose, owner: owner, arguments: []),
            this.GivesOnlyCompleteTasks(owner: owner, member: dispose));
    }

    // The resource of ConfigureAwait of the platform, as in r.ConfigureAwait(false). Null for each
    // other expression, also for a ConfigureAwait of this compilation.
    private ExpressionSyntax? ResourceOfConfigureAwait(SyntaxNode resource) =>
        resource is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } access
        }
        && this.region.SymbolOf(resource) is IMethodSymbol
        {
            ContainingType.Name: "TaskAsyncEnumerableExtensions"
        }
            ? access.Expression
            : null;

    // The compiler calls a DisposeAsync of the type that needs no argument and that the code can
    // call. Otherwise, it calls the member of IAsyncDisposable. Thus, a type with both calls its own
    // method. The run of a shape holds this order.
    private IMethodSymbol? DisposeAsyncOf(ITypeSymbol type)
    {
        for (ITypeSymbol? owner = type; owner != null; owner = owner.BaseType)
        {
            if (owner.GetMembers("DisposeAsync")
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(static method =>
                        method is
                        {
                            IsStatic: false,
                            DeclaredAccessibility: Accessibility.Public
                            or Accessibility.Internal
                            or Accessibility.ProtectedOrInternal
                        }
                        && method.Parameters.All(static parameter => parameter.IsOptional || parameter.IsParams))
                is { } found)
            {
                return found;
            }
        }

        return this.region.Symbols.AsyncDisposableDisposeAsync is { ContainingType: { } contract } member
               && type.AllInterfaces.Contains(value: contract, comparer: SymbolEqualityComparer.Default)
            ? member
            : null;
    }

    /// <summary>
    ///     The call of a <c>Deconstruct</c> method. Each parameter gets an
    ///     argument, thus the result has the state that the method gives to each
    ///     <c>out</c> parameter.
    /// </summary>
    /// <param name="call">The deconstruction, or the part of a pattern.</param>
    /// <param name="method">The method that the compiler found.</param>
    /// <param name="value">The expression for the value, for a report.</param>
    /// <param name="owner">The state of the value.</param>
    public Followed? FollowDeconstruct(SyntaxNode call, IMethodSymbol method, ExpressionSyntax value, LoopState owner)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        // An extension method gets the value as its first argument.
        return this.FollowImplied(
            call: call,
            member: definition,
            owner: definition.IsStatic ? null : owner,
            arguments:
            [
                .. definition.Parameters.Select((_, position) =>
                    (Written: value, State: definition.IsStatic && position == 0 ? owner : LoopState.None))
            ]);
    }

    /// <summary>
    ///     Forgets the result of a <c>Deconstruct</c> that the evaluator followed
    ///     at a node. The next follow at that node reads the method again, with
    ///     the state that it gets then.
    /// </summary>
    /// <param name="call">The node of the deconstruction.</param>
    /// <param name="method">The method that the compiler found.</param>
    public void ForgetsTheDeconstructOf(SyntaxNode call, IMethodSymbol method) =>
        this.implied.Remove((call, (method.ReducedFrom ?? method).OriginalDefinition));

    /// <summary>
    ///     The members that a list pattern calls on a type of this compilation:
    ///     <c>Count</c> or <c>Length</c>, the indexer, and <c>Slice</c>. No node
    ///     names them, thus the evaluator asks for the operation of the pattern.
    /// </summary>
    /// <param name="pattern">The list pattern.</param>
    /// <param name="owner">The state of the value that the pattern is for.</param>
    public Followed? FollowListPattern(ListPatternSyntax pattern, LoopState owner)
    {
        if (!this.region.Symbols.DeclaresIndexers
            || this.region.ModelFor(pattern).GetOperation(pattern)
                is not IListPatternOperation
                {
                    IndexerSymbol: IPropertySymbol { GetMethod: { DeclaringSyntaxReferences.IsEmpty: false } getter }
                } list)
        {
            return null;
        }

        Followed? elements =
            Both(
                first: list.LengthSymbol is IPropertySymbol { GetMethod: { } count }
                    ? this.FollowImplied(call: pattern, member: count, owner: owner, arguments: [])
                    : null,
                second: this.FollowImplied(call: pattern, member: getter, owner: owner, arguments: []));

        // Only a slice with a pattern of its own calls Slice, or the indexer that takes a range.
        IMethodSymbol? slice =
            list.Patterns.OfType<ISlicePatternOperation>().FirstOrDefault() is { Pattern: not null } part
                ? part.SliceSymbol switch
                {
                    IMethodSymbol method => method,
                    IPropertySymbol { GetMethod: { } ranged } => ranged,
                    _ => null
                }
                : null;

        return slice is { DeclaringSyntaxReferences.IsEmpty: false }
            ? Both(
                first: elements,
                second: this.FollowImplied(call: pattern, member: slice, owner: owner, arguments: []))
            : elements;
    }

    // A range binds to a Slice method. An index from the end or a range on a type of this
    // compilation also reads Count or Length first. The operation of the access names that
    // property, and the evaluator asks for it only then.
    private Followed? FollowIndexer(SyntaxNode access, ExpressionSyntax owner, BaseArgumentListSyntax indexes)
    {
        IMethodSymbol? member =
            this.region.SymbolOf(access) switch
            {
                IPropertySymbol { GetMethod: { } getter } => getter,
                // The symbol of an element that the code converts can be the conversion, which is not
                // a Slice.
                IMethodSymbol { DeclaringSyntaxReferences.IsEmpty: false, MethodKind: MethodKind.Ordinary } slice =>
                    slice,
                // The binder finds the indexer of a dynamic value at run time.
                null when this.region.TypeOf(owner) is { TypeKind: TypeKind.Dynamic } =>
                    this.DynamicIndexerOf(owner: owner, indexes: indexes.Arguments.Count)?.GetMethod,
                _ => null
            };

        if (member == null)
        {
            return null;
        }

        LoopState self = this.Self(owner);

        Followed? length =
            !member.DeclaringSyntaxReferences.IsEmpty
            && indexes.Arguments.Count == 1
            && SodaFlowSymbols.IsIndexOrRange(this.region.TypeOf(indexes.Arguments[0].Expression))
            && this.region.ModelFor(access).GetOperation(access)
                is IImplicitIndexerReferenceOperation { LengthSymbol: IPropertySymbol { GetMethod: { } count } }
                ? this.FollowImplied(call: access, member: count, owner: self, arguments: [])
                : null;

        return Both(
            first: length,
            second: this.FollowImplied(
                call: access,
                member: member,
                owner: self,
                arguments: [.. indexes.Arguments.Select(index => (index.Expression, this.Eval(index.Expression)))]));
    }

    private Followed? FollowOperator(SyntaxNode call, ISymbol? operation, IEnumerable<ExpressionSyntax> operands) =>
        operation is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } method
            ? this.FollowImplied(
                call: call,
                member: method,
                owner: null,
                arguments: [.. operands.Select(operand => (operand, this.Eval(operand)))])
            : null;

    // The name of a member in a report: the property for an accessor, and the operator as the
    // code declares it.
    private static string ImpliedNameOf(IMethodSymbol definition) =>
        definition.AssociatedSymbol?.Name
        ?? (definition.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion
            ? definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : definition.Name);

    /// <summary>
    ///     The read of a property that a property pattern names, on a value with
    ///     the given state. A pattern has no expression for the owner of the
    ///     property, thus the walk gives the state of the owner.
    /// </summary>
    /// <param name="name">The name of the property in the pattern.</param>
    /// <param name="owner">The state of the value that the pattern is for.</param>
    public Followed? FollowPatternMember(ExpressionSyntax name, LoopState owner)
    {
        if (this.followed.TryGetValue(key: name, value: out Followed? known))
        {
            return known;
        }

        Followed? result =
            this.region.SymbolOf(name) is IPropertySymbol written && !this.region.Symbols.IsSodaFlow(written)
                ? this.FollowGetterOn(access: name, written: written, owner: owner)
                : null;

        this.followed[name] = result;

        return result;
    }
}
