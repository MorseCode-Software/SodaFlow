using System;
using System.Collections.Generic;
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

        // The member of the object, when the code names one of an interface or a base type. A
        // value that is one of two objects has the member of each.
        IMethodSymbol[] actual =
        [
            .. owner?.Members?.Resolve(member.OriginalDefinition).OfType<IMethodSymbol>() ?? []
        ];

        Followed? result =
            (actual.Length == 0 ? [member] : actual)
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

        this.implied[(call, member)] = result;

        return result;
    }

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
        }

        if (!this.region.Symbols.IsSodaFlow(definition) && BodyOf(definition) is { } body)
        {
            IReadOnlyList<Binding> bindings =
            [
                .. arguments
                    .Take(definition.Parameters.Length)
                    .Select((argument, position) =>
                        new Binding(
                            parameter: definition.Parameters[position],
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
                    .OfType<IPropertySymbol>()
                    .Where(static property =>
                        property is
                        {
                            IsStatic: false,
                            IsIndexer: false,
                            IsOverride: false,
                            DeclaredAccessibility: Accessibility.Public,
                            GetMethod: not null
                        })
                    .Aggregate(
                        seed: result,
                        func: (all, property) =>
                            Both(
                                first: all,
                                second: this.FollowGetterOn(access: printed, written: property, owner: owner)));
        }

        return result;
    }

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
                getter?.State ?? (member == null ? LoopState.None : StateOfAMember(owner: owner, member: member));

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
    public static LoopState StateOfAMember(LoopState owner, ISymbol member)
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
            return owner.Members.StateThrough(member).FromIfUnknown(owner.Origin);
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
            BinaryExpressionSyntax binary
                when this.region.Symbols.DeclaresOperators || this.region.Symbols.DeclaresToString =>
                this.region.SymbolOf(binary) switch
                {
                    IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } operation =>
                        this.FollowOperator(call: binary, operation: operation, operands: [binary.Left, binary.Right]),
                    // A concatenation calls ToString on an operand that is not a string.
                    IMethodSymbol
                    {
                        MethodKind: MethodKind.BuiltinOperator,
                        ContainingType.SpecialType: SpecialType.System_String
                    } => Both(first: this.FollowToString(binary.Left), second: this.FollowToString(binary.Right)),
                    // A tuple equality has no symbol. It compares each pair of elements.
                    null when this.region.Symbols.DeclaresOperators
                              && (binary.IsKind(SyntaxKind.EqualsExpression)
                                  || binary.IsKind(SyntaxKind.NotEqualsExpression)) =>
                        this.FollowTupleEquality(binary),
                    _ => null
                },
            PrefixUnaryExpressionSyntax prefix when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(call: prefix, operation: this.region.SymbolOf(prefix), operands: [prefix.Operand]),
            PostfixUnaryExpressionSyntax postfix when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(
                    call: postfix,
                    operation: this.region.SymbolOf(postfix),
                    operands: [postfix.Operand]),
            CastExpressionSyntax cast when this.region.Symbols.DeclaresOperators =>
                this.FollowOperator(call: cast, operation: this.region.SymbolOf(cast), operands: [cast.Expression]),
            // The compiler gives a plain assignment no operator, and only an operator of this
            // compilation has a body to read. Thus, these two tests are only for speed.
            AssignmentExpressionSyntax compound
                when this.region.Symbols.DeclaresOperators
                     && !compound.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                this.FollowOperator(
                    call: compound,
                    operation: this.region.SymbolOf(compound),
                    operands: [compound.Left, compound.Right]),
            _ => null
        };

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
    public Followed? FollowConversion(ExpressionSyntax expression) =>
        this.region.Symbols.DeclaresConversions && this.region.ConversionOf(expression) is { } conversion
            ? this.FollowOperator(call: expression, operation: conversion, operands: [expression])
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
        && this.region.ModelFor(condition).GetOperation(condition)?.Parent
            is IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.True or UnaryOperatorKind.False,
                OperatorMethod: { } truth
            }
            ? this.FollowOperator(call: condition, operation: truth, operands: [condition])
            : null;

    /// <summary>
    ///     The call of <c>ToString</c> that a concatenation makes on an object of
    ///     this compilation.
    /// </summary>
    public Followed? FollowToString(ExpressionSyntax written) =>
        this.region.Symbols is { ObjectToString: { } toString } symbols
        && (symbols.DeclaresToString || symbols.DeclaresRecords)
        && this.Self(written) is { Members: not null } owner
            ? this.FollowImplied(call: written, member: toString, owner: owner, arguments: [])
            : null;

    /// <summary>
    ///     The call of <c>ToString</c> that an interpolation makes on its value,
    ///     when the interpolated string formats it now.
    /// </summary>
    public Followed? FollowInterpolation(InterpolationSyntax interpolation) =>
        this.region.Symbols is { ObjectToString: { } toString } symbols
        && (symbols.DeclaresToString || symbols.DeclaresRecords)
        && this.Self(interpolation.Expression) is { Members: not null } owner
        && interpolation.Parent is InterpolatedStringExpressionSyntax whole
        && this.FormatsNow(whole)
            ? this.FollowImplied(call: interpolation.Expression, member: toString, owner: owner, arguments: [])
            : null;

    // A FormattableString or an IFormattable keeps the values of the interpolated string, and
    // formats nothing at this point. A method of FormattableString that gets it formats it now.
    private bool FormatsNow(InterpolatedStringExpressionSyntax whole) =>
        this.region.ModelFor(whole).GetOperation(whole)?.Parent is not IConversionOperation
        {
            Type.Name: "FormattableString" or "IFormattable",
            Type.ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
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
                IMethodSymbol { DeclaringSyntaxReferences.IsEmpty: false } slice => slice,
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
