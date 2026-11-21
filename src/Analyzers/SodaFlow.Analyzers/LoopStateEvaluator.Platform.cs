using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the members that code of the platform calls. It reads a call to
// the platform, the comparers of the platform, and the values that the platform keeps.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The members of objects of this compilation that a call to code of the
    ///     platform runs, with no call of them in this code.
    ///     <see cref="PlatformCallers" /> says which members a method calls. A
    ///     callee that gets a sequence of this compilation also enumerates it.
    /// </summary>
    /// <remarks>
    ///     A report about such a member is at the value that has it, and not at
    ///     the call. One call can give two values, and each has its own members.
    /// </remarks>
    public Followed? FollowPlatformCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!this.region.Symbols.DeclaresPlatformMembers)
        {
            return null;
        }

        ExpressionSyntax? receiver = ReceiverOf(symbols: this.region.Symbols, invocation: invocation, method: method);
        method = this.ImplementationOf(method: method, receiver: receiver);
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        List<Operand> operands = [];

        // In a static call of an extension method, the receiver is one of the arguments. In a call
        // on the receiver, it is the argument of the first parameter.
        if (receiver != null && arguments.All(argument => argument.Expression != receiver))
        {
            operands.Add(
                method.ReducedFrom != null
                    ? new Operand(written: receiver, parameter: definition.Parameters[0].Type, isReceiver: false)
                    : new Operand(written: receiver, parameter: null, isReceiver: true));
        }

        // The callee does not compare a value that it only keeps.
        operands.AddRange(
            arguments
                .Select(static (argument, position) => (Argument: argument, Position: position))
                .Where(pair =>
                    !PlatformCallers.IsOnlyKept(
                        ParameterFor(method: method, argument: pair.Argument, position: pair.Position)))
                .Select(pair =>
                    new Operand(
                        written: pair.Argument.Expression,
                        parameter: this.KnownTypeFor(method: method, argument: pair.Argument, position: pair.Position),
                        isReceiver: false)));

        PlatformCallbacks calls = PlatformCallers.Of(method);

        // A put of one value into an empty collection compares nothing, because the collection has
        // no element for the value. It can make a hash code from the value. A run on each runtime
        // gave that for each method of the list.
        if ((calls & ComparesTwo) != PlatformCallbacks.None
            && receiver != null
            && method.ReducedFrom == null
            && PutsOfOneValue.Contains(definition.Name)
            && this.IsEmptyHere(collection: receiver, put: invocation))
        {
            calls &= ~ComparesTwo;
        }

        // A tuple in a local of a type that says nothing, such as object. The call is the one of
        // the tuple, which calls the same member of each element.
        if (calls == PlatformCallbacks.None
            && receiver != null
            && this.Eval(receiver).Members is { IsTuple: true })
        {
            calls = PlatformCallers.OfTuple(definition.Name);
        }

        return this.FollowPlatformAndItsValues(
            method: method,
            calls: calls,
            operands: operands,
            keeper: method.ReducedFrom == null ? receiver : null,
            also: LoopState.None,
            given: () => this.Moved(invocation: invocation, method: method, withReceiver: true).AsElement,
            returned: () => this.Follow(invocation)?.State ?? LoopState.None,
            firstIsStored: true);
    }

    /// <summary>
    ///     The method that a call through an interface of the platform runs, when
    ///     the code says the type of the object. The method of the interface
    ///     otherwise.
    /// </summary>
    /// <remarks>
    ///     A member of an interface has a row only when each collection of the
    ///     platform compares in it. <c>Add</c> of <c>ICollection</c> has no row,
    ///     because a list adds with no comparison. Thus, the row of the method of
    ///     the type is more correct. When that method has no row, the row of the
    ///     interface member holds.
    /// </remarks>
    private IMethodSymbol ImplementationOf(IMethodSymbol method, ExpressionSyntax? receiver) =>
        receiver != null
        && method.ContainingType is { TypeKind: TypeKind.Interface } contract
        && SodaFlowSymbols.IsPlatform(contract)
        && this.KnownTypeOf(receiver) is INamedTypeSymbol type
        && type.FindImplementationForInterfaceMember(method) is IMethodSymbol implemented
        && SodaFlowSymbols.IsPlatform(implemented.ContainingType)
        && PlatformCallers.Of(implemented) != PlatformCallbacks.None
            ? implemented
            : method;

    /// <summary>
    ///     The type of the object that an expression gives, when it is a class
    ///     or a struct. A cast does not change the object. A local that no code
    ///     changes after its declaration has the type of the value that it got.
    ///     A call of a method of this compilation has the type that each of its
    ///     returns gives.
    /// </summary>
    /// <param name="written">The expression.</param>
    /// <param name="depth">The number of locals and calls that the evaluator went through before this one.</param>
    private ITypeSymbol? KnownTypeOf(ExpressionSyntax written, int depth = 0)
    {
        // A local can get its value from a second local.
        for (; depth <= MaxKnownTypeDepth; depth++)
        {
            ExpressionSyntax inner = Unwrapped(written);

            if (this.region.TypeOf(inner)
                is
                {
                    TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Array,
                    SpecialType: not SpecialType.System_Object
                } known)
            {
                return known;
            }

            if (inner is InvocationExpressionSyntax call)
            {
                return this.ReturnedTypeOf(call: call, depth: depth + 1);
            }

            if (inner is not IdentifierNameSyntax name
                || this.region.SymbolOf(name) is not ILocalSymbol local
                || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is not VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                || NamesOf(local: local, declarator: declarator, region: this.region) is not { } uses
                || !uses.All(static use => !IsWritten(use)))
            {
                return null;
            }

            written = made;
        }

        return null;
    }

    /// <summary>
    ///     The type of the object that a call of a method of this compilation
    ///     gives. It is known when each return of the method gives an object of
    ///     that type. A return of a parameter gives the object of the argument,
    ///     when the method does not change that parameter.
    /// </summary>
    /// <remarks>
    ///     A method that a type can override can run a different body. Thus, it
    ///     gives no type.
    /// </remarks>
    private ITypeSymbol? ReturnedTypeOf(InvocationExpressionSyntax call, int depth)
    {
        if (depth > MaxKnownTypeDepth
            || this.region.SymbolOf(call) is not IMethodSymbol { ReducedFrom: null } method
            || method is { IsVirtual: true, ContainingType.IsSealed: false }
            || method is { IsOverride: true, IsSealed: false, ContainingType.IsSealed: false }
            || method.OriginalDefinition is not { } definition
            || BodyOf(definition) is not { } body)
        {
            return null;
        }

        ITypeSymbol? type = null;
        IEnumerable<ExpressionSyntax> returns = body is ExpressionSyntax expression ? [expression] : ReturnExpressions(body);

        foreach (ExpressionSyntax given in returns.Select(returned =>
                     this.ArgumentFor(call: call, definition: definition, returned: returned) ?? returned))
        {
            if (this.KnownTypeOf(written: given, depth: depth) is not { } one
                || (type != null && !SymbolEqualityComparer.Default.Equals(x: type, y: one)))
            {
                return null;
            }

            type = one;
        }

        return type;
    }

    // The argument of a call for the parameter that a return of the callee gives. Null when the
    // return gives no parameter, or when the callee writes that parameter.
    private ExpressionSyntax? ArgumentFor(
        InvocationExpressionSyntax call,
        IMethodSymbol definition,
        ExpressionSyntax returned)
    {
        if (Unwrapped(returned) is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not IParameterSymbol parameter
            || IsWrittenIn(parameter: parameter, region: this.region))
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        return arguments
            .Where((argument, position) =>
                SymbolEqualityComparer.Default.Equals(
                    x: SodaFlowSymbols.ParameterOf(callee: definition, argument: argument, position: position),
                    y: parameter))
            .Select(static argument => argument.Expression)
            .FirstOrDefault();
    }

    // True for a parameter that the code of its method writes.
    private static bool IsWrittenIn(ISymbol parameter, LoopRegion region) =>
        parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().Parent?.Parent is not { } declaration
        || declaration.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(name => name.Identifier.ValueText == parameter.Name
                         && IsWritten(name)
                         && SymbolEqualityComparer.Default.Equals(x: region.SymbolOf(name), y: parameter));

    // Each name of a local in the block that declares it. Null for a local of the top-level
    // statements, which have no block.
    private static IEnumerable<IdentifierNameSyntax>? NamesOf(
        ISymbol local,
        SyntaxNode declarator,
        LoopRegion region) =>
        declarator.FirstAncestorOrSelf<BlockSyntax>()
            ?.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == local.Name
                           && SymbolEqualityComparer.Default.Equals(x: region.SymbolOf(name), y: local));

    /// <summary>The same as <see cref="FollowPlatformCall" />, for a constructor of the platform.</summary>
    public Followed? FollowPlatformCreation(IMethodSymbol constructor, SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        this.region.Symbols.DeclaresPlatformMembers
            ? this.FollowPlatformAndItsValues(
                method: constructor,
                calls: PlatformCallers.Of(constructor),
                operands:
                [
                    .. arguments.Select((argument, position) =>
                        new Operand(
                            written: argument.Expression,
                            parameter: SodaFlowSymbols.ParameterOf(
                                    callee: constructor.OriginalDefinition,
                                    argument: argument,
                                    position: position)
                                ?.Type,
                            isReceiver: false))
                ],
                keeper: null,
                also: LoopState.None,
                given: null,
                returned: null,
                firstIsStored: false)
            : null;

    /// <summary>
    ///     The same as <see cref="FollowPlatformCall" />, for a member of the
    ///     platform that the code calls with no call syntax. It is the
    ///     <c>Add</c> of a collection initializer or of a collection expression, or
    ///     an accessor of an indexer.
    /// </summary>
    /// <param name="member">The method or the accessor.</param>
    /// <param name="collection">The collection, which has the values that it got before.</param>
    /// <param name="values">The values that the member gets, in the order of its parameters.</param>
    /// <param name="alone">
    ///     True when the value is the only one of a new collection, as the one
    ///     element of a collection expression is. The member then has no second
    ///     object to compare it with.
    /// </param>
    /// <param name="others">
    ///     Each expression for a collection whose objects the collection has
    ///     before the values. That is the collection itself when code before made
    ///     it, an argument of its constructor, or a spread. The member can call a
    ///     member of one of those objects. The walk reads each value of a literal
    ///     where the literal gives it, thus the literal is not here.
    /// </param>
    /// <param name="before">
    ///     For an element of a literal: the values of each element that the
    ///     literal gave before it. The values of one element are in the order of
    ///     the parameters of the member. The collection has those objects when it
    ///     gets this element, and the member gives the call to one side, as a call
    ///     of it does. Null for a member that is not an element of a literal.
    /// </param>
    public Followed? FollowPlatformMember(
        IMethodSymbol member,
        ExpressionSyntax? collection,
        IEnumerable<ExpressionSyntax> values,
        bool alone,
        IEnumerable<ExpressionSyntax> others,
        IEnumerable<IEnumerable<ExpressionSyntax>>? before)
    {
        member = this.ImplementationOf(method: member, receiver: collection);

        // A member with no row can compare the values of a dictionary, as the indexer of a builder
        // of an immutable dictionary does.
        if (!this.region.Symbols.DeclaresKeyMembers
            || (PlatformCallers.Of(member) == PlatformCallbacks.None
                && PlatformCallers.ValueReceiverOf(member) == null))
        {
            return null;
        }

        ImmutableArray<IParameterSymbol> parameters = member.OriginalDefinition.Parameters;

        // Of an element before, the collection compares only the values that the member compares,
        // as the key of a pair of a dictionary.
        ImmutableHashSet<ITypeParameterSymbol>? compared =
            PlatformCallers.ComparedTypeParameters(method: member, ofValues: false);

        IEnumerable<ExpressionSyntax> earlier =
            (before ?? []).SelectMany(element =>
                element.Where((_, position) =>
                    compared == null
                    || position >= parameters.Length
                    || parameters[position].Type is not ITypeParameterSymbol parameter
                    || compared.Contains(parameter)));

        List<Operand> operands =
        [
            .. values.Select((value, position) =>
                new Operand(
                    written: value,
                    parameter: position < parameters.Length ? parameters[position].Type : null,
                    isReceiver: false)),
            .. earlier.Select(static value => new Operand(written: value, parameter: null, isReceiver: true)),
            .. others.Select(static other => new Operand(written: other, parameter: null, isReceiver: true))
        ];

        return this.FollowPlatformAndItsValues(
            method: member,
            calls: alone ? PlatformCallers.Of(member) & ~ComparesTwo : PlatformCallers.Of(member),
            operands: operands,
            keeper: collection,
            also: this.Eval(collection),
            given: null,
            returned: null,
            firstIsStored: false);
    }

    /// <summary>
    ///     The calls that code of the platform makes when it enumerates a sequence
    ///     of this compilation. The first is <c>GetEnumerator</c>. The others are
    ///     <c>MoveNext</c>, <c>Current</c>, and <c>Dispose</c> of an enumerator of
    ///     this compilation.
    ///     The result has the state of the elements. It is null for a value that is
    ///     not such a sequence.
    /// </summary>
    /// <remarks>
    ///     A query of the platform enumerates its source when something
    ///     enumerates the query, which can be after the block. The analyzer reads
    ///     the call as the time, as it does for a function that a query gets.
    /// </remarks>
    /// <param name="written">The expression for the sequence.</param>
    /// <param name="byThePattern">
    ///     True for an enumeration that the compiler writes, as for a spread. The
    ///     compiler calls a <c>GetEnumerator</c> method that it finds by its name,
    ///     and the members of the enumerator by their names. The type then needs
    ///     no interface.
    /// </param>
    public Followed? FollowEnumeration(ExpressionSyntax written, bool byThePattern = false)
    {
        if (!this.region.Symbols.DeclaresEnumerators
            || this.Self(written) is not { Members: { HasType: true } members } sequence)
        {
            return null;
        }

        if (byThePattern && PatternMemberOf(type: this.region.TypeOf(written), name: "GetEnumerator") is { } named)
        {
            return this.FollowEnumerationFrom(written: written, sequence: sequence, start: named, byThePattern: true);
        }

        // The method for the generic interface. The other one calls it. A value that is one of
        // two sequences has the method of each type.
        return members.Implementations(SodaFlowSymbols.IsEnumerableInterface)
            .GroupBy(
                keySelector: static method => method.ContainingType,
                comparer: SymbolEqualityComparer.Default)
            .Select(static ofOneType =>
                ofOneType.FirstOrDefault(static method =>
                    method.ReturnType is INamedTypeSymbol { IsGenericType: true })
                ?? ofOneType.First())
            .Aggregate(
                seed: (Followed?)null,
                func: (all, start) =>
                    Both(
                        first: all,
                        second: this.FollowEnumerationFrom(
                            written: written,
                            sequence: sequence,
                            start: start,
                            byThePattern: byThePattern)));
    }

    // The public instance method with no parameter that a type or a base type declares with the
    // given name. The compiler takes GetEnumerator and MoveNext by this pattern.
    private static IMethodSymbol? PatternMemberOf(ITypeSymbol? type, string name)
    {
        for (ITypeSymbol? current = type; current != null; current = current.BaseType)
        {
            if (current.GetMembers(name)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(static method =>
                        method is
                        {
                            IsStatic: false,
                            Parameters.IsEmpty: true,
                            DeclaredAccessibility: Accessibility.Public
                        }) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private Followed? FollowEnumerationFrom(
        SyntaxNode written,
        LoopState sequence,
        IMethodSymbol start,
        bool byThePattern)
    {
        if (this.FollowImplied(call: written, member: start, owner: sequence, arguments: []) is not { } enumerator)
        {
            return null;
        }

        // An iterator has no enumerator object of this compilation, and a method can give the
        // enumerator of a collection of the platform. The result then has the state of the
        // elements.
        if (enumerator.State.Members is not { } made || !made.Is(start.ReturnType))
        {
            return enumerator;
        }

        Followed? all = enumerator;
        LoopState elements = LoopState.None;

        IEnumerable<IMethodSymbol> steps = made.Implementations(SodaFlowSymbols.IsEnumeratorInterface);

        // An enumerator that the compiler reads by the pattern has MoveNext and Current by their
        // names, with no interface.
        if (byThePattern)
        {
            IMethodSymbol?[] named =
            [
                PatternMemberOf(type: start.ReturnType, name: "MoveNext"),
                PatternMemberOf(type: start.ReturnType, name: "get_Current")
            ];

            steps = steps.Concat(named.OfType<IMethodSymbol>()).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        foreach (IMethodSymbol member in steps)
        {
            if (member.Name is "Reset" || !member.Parameters.IsEmpty)
            {
                continue;
            }

            Followed? step = this.FollowImplied(call: written, member: member, owner: enumerator.State, arguments: []);
            all = Both(first: all, second: step);

            if (member.MethodKind == MethodKind.PropertyGet)
            {
                elements = elements.Join(step?.State ?? LoopState.None);
            }
        }

        // The code that enumerates calls Dispose of an enumerator that is IDisposable at the end.
        if (this.region.Symbols.DeclaresDispose)
        {
            all =
                made.Implementations(static contract => contract.SpecialType == SpecialType.System_IDisposable)
                    .Aggregate(
                        seed: all,
                        func: (followed, dispose) =>
                            Both(
                                first: followed,
                                second: this.FollowImplied(
                                    call: written,
                                    member: dispose,
                                    owner: enumerator.State,
                                    arguments: [])));
        }

        return new Followed(
            state: elements,
            findings: all?.Findings ?? ImmutableArray<Finding>.Empty,
            outs: Followed.Nothing.Outs,
            sets: Followed.Nothing.Sets,
            through: Followed.Nothing.Through,
            alwaysGives: Followed.Nothing.AlwaysGives);
    }

    /// <summary>
    ///     The call that the getter of an indexer of the platform makes on its
    ///     key, as the indexer of a dictionary does.
    /// </summary>
    public Followed? FollowPlatformIndexer(ElementAccessExpressionSyntax access) =>
        this.region.Symbols.DeclaresKeyMembers
        && this.region.SymbolOf(access)
            is IPropertySymbol { GetMethod: { DeclaringSyntaxReferences.IsEmpty: true } getter }
            ? this.FollowPlatformMember(
                member: getter,
                collection: access.Expression,
                values: access.ArgumentList.Arguments.Select(static index => index.Expression),
                alone: false,
                others: [access.Expression],
                before: null)
            : null;

    // True for the static member Empty of a collection of the platform, as
    // ImmutableDictionary<TKey, TValue>.Empty. A member of it compares nothing.
    private bool IsTheEmptyCollection(ExpressionSyntax written) =>
        Unwrapped(written) is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Empty" } access
        && this.region.SymbolOf(access) is IFieldSymbol { IsStatic: true } empty
        && SodaFlowSymbols.IsPlatform(empty.ContainingType);

    /// <summary>
    ///     The calls that a method of the platform makes for its row. A method that
    ///     also compares the values of a dictionary gets a second step for those.
    /// </summary>
    private Followed? FollowPlatformAndItsValues(
        IMethodSymbol method,
        PlatformCallbacks calls,
        IReadOnlyList<Operand> operands,
        ExpressionSyntax? keeper,
        LoopState also,
        Func<LoopState>? given,
        Func<LoopState>? returned,
        bool firstIsStored)
    {
        Followed? forTheRow =
            this.FollowPlatform(
                method: method,
                calls: calls,
                operands: operands,
                keeper: keeper,
                also: also,
                given: given,
                returned: returned,
                firstIsStored: firstIsStored,
                forValues: null);

        return PlatformCallers.ValueReceiverOf(method) is { } forValues
            ? Both(
                first: forTheRow,
                second: this.FollowPlatform(
                    method: method,
                    calls: calls,
                    operands: operands,
                    keeper: keeper,
                    also: also,
                    given: given,
                    returned: returned,
                    firstIsStored: firstIsStored,
                    forValues: forValues))
            : forTheRow;
    }

    /// <param name="method">The method of the platform.</param>
    /// <param name="calls">The members that the method calls on an object that it gets.</param>
    /// <param name="operands">The values that the method gets, with the owner of the call first.</param>
    /// <param name="keeper">The collection that keeps a comparer for the method, when there is one.</param>
    /// <param name="also">The state of one more object that a member can get as its argument.</param>
    /// <param name="given">The state of the values that the method gives to a function.</param>
    /// <param name="returned">The state of what a function that is not a lambda returns.</param>
    /// <param name="firstIsStored">
    ///     True when the first operand is the collection of the call, for a
    ///     method with no owner, as the array is for <c>Array.IndexOf</c>.
    /// </param>
    /// <param name="forValues">
    ///     Null for the calls of the row of the method. For a member of a
    ///     dictionary that also compares its values, a second step reads the call
    ///     again. Then, this is the receiver of those calls, and the step is for
    ///     <c>Equals</c> of the values only.
    /// </param>
    private Followed? FollowPlatform(
        IMethodSymbol method,
        PlatformCallbacks calls,
        IReadOnlyList<Operand> operands,
        ExpressionSyntax? keeper,
        LoopState also,
        Func<LoopState>? given,
        Func<LoopState>? returned,
        bool firstIsStored,
        PlatformReceiver? forValues)
    {
        // A callee that gets a sequence enumerates it. The step for the row has that.
        Followed? all =
            forValues != null
                ? null
                : operands
                    .Where(static operand => SodaFlowSymbols.IsSequence(operand.Parameter))
                    .Aggregate(
                        seed: (Followed?)null,
                        func: (followed, operand) =>
                            Both(first: followed, second: this.FollowEnumeration(operand.Written)));

        if (forValues == null)
        {
            all = Both(
                first: all,
                second: this.FollowKeptComparer(method: method, calls: calls, operands: operands, keeper: keeper));
        }

        PlatformCallbacks callbacks =
            (forValues == null ? calls : PlatformCallbacks.Equals) & this.region.Symbols.DeclaredCallbacks;

        // A method with a composite format, as string.Format has, formats each value as an
        // interpolation does.
        bool formats =
            (callbacks & PlatformCallbacks.Text) != PlatformCallbacks.None
            && method.Parameters.Any(static parameter => parameter.Name == "format");

        bool pads = formats && this.CanPad(operands);

        // A callee that gets a comparer calls the comparer. It does not call CompareTo, Equals, or
        // GetHashCode of an object. A factory of a dictionary gets the comparer of its values
        // after the comparer of its keys.
        if (operands.Count(operand => IsComparison(operand.Parameter) && !this.IsNoValue(operand.Written))
            > (forValues == null ? 0 : 1))
        {
            callbacks &= PlatformCallbacks.Text;
        }

        // A dictionary that the code made with a comparer for its values calls that comparer.
        if (forValues != null && keeper != null && this.GotAValueComparer(keeper))
        {
            callbacks &= PlatformCallbacks.Text;
        }

        // A callee that compares the elements of one sequence with each other compares nothing
        // when the sequence has one element. It can make a hash code or text from that element.
        // A factory that gets its elements one by one, as Create does, is the same with one value.
        if ((callbacks & ComparesTwo) != PlatformCallbacks.None && PlatformCallers.ComparesOnlyItsElements(method))
        {
            ExpressionSyntax[] sequences =
            [
                .. operands
                    .Where(static operand => operand.IsReceiver || SodaFlowSymbols.IsSequence(operand.Parameter))
                    .Select(static operand => operand.Written)
            ];

            if (sequences.Length == 0 ? operands.Count == 1 : sequences.Length == 1 && this.HasOneElement(sequences[0]))
            {
                callbacks &= ~ComparesTwo;
            }
        }

        if (callbacks == PlatformCallbacks.None)
        {
            return all;
        }

        ImmutableArray<ITypeSymbol> compared =
            PlatformCallers.ComparedTypes(method: method, ofValues: forValues != null);

        // A type can be the type of two type parameters, as in a queue whose elements and priorities
        // have one type. The type parameter then says which values the method compares.
        ImmutableHashSet<ITypeParameterSymbol>? comparedParameters =
            PlatformCallers.ComparedTypeParameters(method: method, ofValues: forValues != null);

        INamedTypeSymbol collection = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;

        // A callee that gets a function compares what the function returns, as a sort by a key
        // does. It does not compare the values that it gives to the function.
        // For the values of a dictionary, a function for the key says nothing. The method
        // compares the elements of its source when no function makes the values.
        bool byFunction =
            !PlatformCallers.ComparesWhatAFunctionChooses(method)
            && operands.Any(static operand => operand.Parameter?.TypeKind == TypeKind.Delegate)
            && !(forValues != null && PlatformCallers.MakesItsValuesFromItsElements(method));

        // Some of those methods also compare the values of a sequence that they get, as ExceptBy
        // compares the keys of its second sequence.
        bool alsoKeys = byFunction && PlatformCallers.ComparesKeysBesideAFunction(method);

        // A member of LINQ to XML knows its content as object, and makes text from each object of
        // a sequence in it.
        bool takesContent = SodaFlowSymbols.TakesXmlContent((method.ReducedFrom ?? method).OriginalDefinition);
        List<(ExpressionSyntax Written, LoopState State, Construction Members, bool Gets)> owners = [];

        // For some methods, each runtime gives the call to the same one of the two objects.
        bool hasCollection = operands.Any(static operand => operand.IsReceiver);

        PlatformReceiver receiver =
            !hasCollection && !firstIsStored
                ? PlatformReceiver.Either
                : forValues ?? this.ReceiverFor(method: method, operands: operands, keeper: keeper);

        // The object that gets the call can get a second object of its own side as the argument.
        bool among = receiver is PlatformReceiver.StoredAlsoAmong or PlatformReceiver.GivenAlsoAmong;

        int firstValue = operands.TakeWhile(static operand => operand.IsReceiver).Count();
        LoopState argument = LoopState.None;

        for (int position = 0; position < operands.Count; position++)
        {
            Operand operand = operands[position];

            // The element of Enqueue gets no call, also when it has the type of the priority. The
            // elements of the first sequence of ExceptBy get none: the method compares their keys.
            if (comparedParameters != null
                && (operand.Parameter as ITypeParameterSymbol
                    ?? SodaFlowSymbols.ElementTypeOf(operand.Parameter) as ITypeParameterSymbol) is { } own
                && !comparedParameters.Contains(own))
            {
                continue;
            }

            bool isStored = hasCollection ? operand.IsReceiver : position == 0;

            bool gets =
                receiver switch
                {
                    PlatformReceiver.Stored or PlatformReceiver.StoredAlsoAmong => isStored,
                    PlatformReceiver.Given or PlatformReceiver.GivenAlsoAmong => !isStored,
                    PlatformReceiver.FirstValue => position == firstValue,
                    _ => true
                };

            // The parts of a pair that the method compares. The key of a pair has the type parameter
            // for the key.
            IReadOnlyList<ITypeSymbol>? partTypes =
                comparedParameters == null
                    ? null
                    : operand.IsReceiver
                        ? PartTypesOf(SodaFlowSymbols.ElementTypeOf(collection))
                          ?? (collection.TypeParameters.Length > 1 ? collection.TypeParameters : null)
                        : PartTypesOf(operand.Parameter);

            Func<int, bool>? keeps =
                partTypes == null || comparedParameters == null
                    ? null
                    : part => part < 0
                              || part >= partTypes.Count
                              || partTypes[part] is not ITypeParameterSymbol parameter
                              || comparedParameters.Contains(parameter);

            if (!gets || among)
            {
                argument = argument.Join(
                    KeptParts(
                        state: this.Eval(operand.Written),
                        written: this.TypeBeforeObject(operand.Written),
                        keeps: keeps));
            }

            if (byFunction)
            {
                if (given != null && operand.Written is AnonymousFunctionExpressionSyntax lambda)
                {
                    LoopState parameters = given();

                    LoopState result =
                        this.EvalLambdaResult(
                            lambda: lambda,
                            boundParameters: ParametersOf(lambda).Select(_ => parameters).ToImmutableArray());

                    owners.AddRange(
                        CallbackOwners(state: result, written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members, true)));
                }
                else if (returned != null && operand.Parameter?.TypeKind == TypeKind.Delegate)
                {
                    // A method, a local function, or a lambda in a local. The evaluator follows it
                    // as the call of it, and that result has the state of what it returns.
                    owners.AddRange(
                        CallbackOwners(state: returned(), written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members, true)));
                }

                if (!alsoKeys)
                {
                    continue;
                }
            }

            // A method that makes text from its arguments does not read its receiver, which has
            // the text so far.
            if (callbacks == PlatformCallbacks.Text && operand.IsReceiver && operands.Count > 1)
            {
                continue;
            }

            // The ToString of a Lazy<T> calls the ToString of the value, where the value exists. The
            // walk takes the value that the function of the Lazy gives.
            if (operand.IsReceiver
                && method.Name == "ToString"
                && SodaFlowSymbols.IsLazy(method.ContainingType)
                && method.ContainingType.TypeArguments.FirstOrDefault() is { } value)
            {
                if (this.FollowLazy(
                        access: operand.Written,
                        written: operand.Written,
                        lazy: this.Eval(operand.Written),
                        value: value) is { } made)
                {
                    owners.AddRange(
                        CallbackOwners(state: made.State, written: null, asSequence: false, compared: compared)
                            .Select(owner => (operand.Written, owner.State, owner.Members, gets)));
                }

                continue;
            }

            owners.AddRange(
                CallbackOwners(
                        state: this.Eval(operand.Written),
                        written: this.TypeBeforeObject(operand.Written),
                        asSequence: operand.IsReceiver
                                    || SodaFlowSymbols.IsSequence(operand.Parameter)
                                    || (takesContent
                                        && SodaFlowSymbols.IsSequence(this.region.TypeOf(operand.Written))),
                        compared: compared,
                        keeps: keeps)
                    .Select(owner => (operand.Written, owner.State, owner.Members, gets)));
        }

        bool knowsTheReceiver = receiver != PlatformReceiver.Either;

        // The object that gets no call is the argument of the member. The object that the method
        // compares can be a part of a value, as the priority of a pair in a queue is.
        if (knowsTheReceiver)
        {
            argument = owners
                .Where(static owner => !owner.Gets)
                .Aggregate(seed: argument, func: static (joined, owner) => joined.Join(owner.State));
        }

        // The object that gets the call can be one that the evaluator did not see made. An element
        // of a list from a different place is such an object. It has the type of the other
        // object, and no state. The empty collection of the platform has no such object.
        if (knowsTheReceiver
            && !owners.Any(static owner => owner.Gets)
            && !operands.Any(operand => operand.IsReceiver && this.IsTheEmptyCollection(operand.Written)))
        {
            owners.AddRange(
                owners.ToArray()
                    .Select(static owner =>
                        (owner.Written, LoopState.None.WithMembers(owner.Members.WithNoState),
                            owner.Members.WithNoState, true)));
        }

        // An object with no member that has a state reads nothing through its own members. Its
        // member can read a second object that it gets. When the analyzer does not know which
        // object gets the call, the walk of that second object reports the read. A field can give
        // an object a state that is not in its members.
        if (!this.region.HasFields)
        {
            owners.RemoveAll(owner =>
                !owner.Members.HasState && !(knowsTheReceiver && owner.Gets && !argument.IsNone));
        }

        if (owners.Count == 0)
        {
            return all;
        }

        // The platform gives one object to a member of a second one, as a sort does to CompareTo.
        // When the analyzer does not know which of them gets the call, each object can be the
        // parameter.
        LoopState others =
            (knowsTheReceiver
                ? argument
                : owners.Aggregate(seed: also, func: static (joined, owner) => joined.Join(owner.State)))
            .WithNoObjects;

        return owners.Aggregate(
            seed: all,
            func: (followed, owner) =>
            {
                // A member with a parameter gets one more of the objects.
                (ExpressionSyntax Written, LoopState State)[] one = [(owner.Written, others)];

                // An object that is only the argument gets no call with two objects. The method
                // can make its hash code or its text.
                PlatformCallbacks its = owner.Gets ? callbacks : callbacks & ~ComparesTwo;

                return this.CallbackMembers(members: owner.Members, callbacks: its, formats: formats, pads: pads)
                    .Aggregate(
                        seed: followed,
                        func: (found, member) =>
                            Both(
                                first: found,
                                second: this.FollowImplied(
                                    call: owner.Written,
                                    member: member,
                                    owner: owner.State,
                                    arguments: member.Parameters.Length == 1 ? one : [])));
            });
    }

    /// <summary>
    ///     Which object gets the call from a method of the platform. A set
    ///     operation can use a different path in the platform when its argument is
    ///     a set with the same comparer.
    /// </summary>
    /// <remarks>
    ///     The platform uses that path only when the two comparers are equal. The
    ///     analyzer knows that when it sees that each set was made with the default
    ///     comparer. When it sees that only one of them got a different comparer,
    ///     the method has its row. In each other case, the analyzer reads the two
    ///     objects. A collection expression for a sequence makes a list or an
    ///     array, which is not a set. An argument whose type the analyzer does not
    ///     know can be such a set, and then the analyzer reads the two objects
    ///     also.
    /// </remarks>
    private PlatformReceiver ReceiverFor(
        IMethodSymbol method,
        IEnumerable<Operand> operands,
        ExpressionSyntax? keeper)
    {
        PlatformReceiver row = PlatformCallers.ReceiverOf(method);

        if (!PlatformCallers.DependsOnItsArgument(method)
            || operands.Where(static operand => !operand.IsReceiver && SodaFlowSymbols.IsSequence(operand.Parameter))
                .Select(static operand => operand.Written)
                .FirstOrDefault() is not { } argument
            || (Unwrapped(argument) is CollectionExpressionSyntax
                && this.region.TypeOf(argument) is { TypeKind: TypeKind.Interface }))
        {
            return row;
        }

        if (this.KnownTypeOf(argument) is not { } type)
        {
            return PlatformReceiver.Either;
        }

        if (PlatformCallers.ReceiverForASet(method: method, argument: type) is not { } forASet || forASet == row)
        {
            return row;
        }

        return (keeper == null ? KeptComparer.NotKnown : this.ComparerOf(keeper), this.ComparerOf(argument)) switch
        {
            (KeptComparer.Default, KeptComparer.Default) => forASet,
            (KeptComparer.Default, KeptComparer.Different) or (KeptComparer.Different, KeptComparer.Default) => row,
            _ => PlatformReceiver.Either
        };
    }

    // What the analyzer knows about the comparer that a set of the platform keeps.
    private enum KeptComparer
    {
        // The code made the set with no comparer, with null, or with the Default of the platform.
        Default,

        // The code made the set with a new comparer, or with a static member that is not Default.
        Different,
        NotKnown
    }

    // True when the code made the dictionary with a comparer for its values: a factory or
    // WithComparers that gets a second comparer. A member of a dictionary that gives a new
    // dictionary keeps its comparers. A local that no code changes has the dictionary that its
    // declaration made.
    private bool GotAValueComparer(ExpressionSyntax written)
    {
        for (int depth = 0; depth <= MaxKnownTypeDepth; depth++)
        {
            switch (Unwrapped(written))
            {
                case IdentifierNameSyntax name
                    when this.region.SymbolOf(name) is ILocalSymbol local
                         && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                             is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                         && NamesOf(local: local, declarator: declarator, region: this.region) is { } uses
                         && uses.All(static use => !IsWritten(use)):
                    written = made;

                    continue;
                case InvocationExpressionSyntax call
                    when this.region.SymbolOf(call) is IMethodSymbol maker
                         && SodaFlowSymbols.IsPlatform(maker.ContainingType):
                    if ((maker.ReducedFrom ?? maker).Parameters
                        .Where(static parameter => SodaFlowSymbols.IsComparer(parameter.Type))
                        .Skip(1)
                        .FirstOrDefault() is { } forTheValues)
                    {
                        return this.ComparerIn(
                                   arguments: call.ArgumentList.Arguments,
                                   maker: maker,
                                   parameter: forTheValues)
                               != KeptComparer.Default;
                    }

                    if (maker.IsStatic || call.Expression is not MemberAccessExpressionSyntax onADictionary)
                    {
                        return false;
                    }

                    written = onADictionary.Expression;

                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    // The comparer of a set that the code made. A creation, a collection expression, and a factory
    // of the platform say it. A local that no code changes has the set that its declaration made.
    private KeptComparer ComparerOf(ExpressionSyntax written)
    {
        for (int depth = 0; depth <= MaxKnownTypeDepth; depth++)
        {
            ExpressionSyntax inner = Unwrapped(written);

            switch (inner)
            {
                case IdentifierNameSyntax name
                    when this.region.SymbolOf(name) is ILocalSymbol local
                         && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                             is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
                         && NamesOf(local: local, declarator: declarator, region: this.region) is { } uses
                         && uses.All(static use => !IsWritten(use)):
                    written = made;

                    continue;
                case CollectionExpressionSyntax:
                    return KeptComparer.Default;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments =
                inner switch
                {
                    ObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments ?? default,
                    ImplicitObjectCreationExpressionSyntax creation => creation.ArgumentList.Arguments,
                    InvocationExpressionSyntax call => call.ArgumentList.Arguments,
                    _ => default
                };

            if (inner is not (BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax)
                || this.region.SymbolOf(inner) is not IMethodSymbol maker
                || !SodaFlowSymbols.IsPlatform(maker.ContainingType)
                || (maker.MethodKind != MethodKind.Constructor && !(maker.ReducedFrom ?? maker).IsStatic))
            {
                return KeptComparer.NotKnown;
            }

            return (maker.ReducedFrom ?? maker).Parameters
                .Where(static parameter => SodaFlowSymbols.IsComparer(parameter.Type))
                .Select(parameter => this.ComparerIn(arguments: arguments, maker: maker, parameter: parameter))
                .DefaultIfEmpty(KeptComparer.Default)
                .First();
        }

        return KeptComparer.NotKnown;
    }

    // The comparer that a call gives for a parameter. A call that gives none, null, default, or
    // the Default of the platform gives the default comparer.
    private KeptComparer ComparerIn(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol maker,
        IParameterSymbol parameter)
    {
        // The object of an extension method is not in the list of its arguments.
        int position = parameter.Ordinal - (maker.ReducedFrom == null ? 0 : 1);

        ExpressionSyntax? given =
            arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == parameter.Name)
                ?.Expression
            ?? (position >= 0 && position < arguments.Count && arguments[position].NameColon == null
                ? arguments[position].Expression
                : null);

        if (given == null || this.IsNoValue(given))
        {
            return KeptComparer.Default;
        }

        return this.region.SymbolOf(Unwrapped(given)) switch
        {
            IPropertySymbol { IsStatic: true, Name: "Default" } property
                when SodaFlowSymbols.IsPlatform(property.ContainingType)
                     && property.ContainingType.Name is "EqualityComparer" or "Comparer" => KeptComparer.Default,
            IMethodSymbol { MethodKind: MethodKind.Constructor } => KeptComparer.Different,
            IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true } => KeptComparer.Different,
            _ => KeptComparer.NotKnown
        };
    }

    /// <summary>
    ///     The calls that a method of the platform makes on a comparer of this
    ///     compilation which its collection keeps. The constructor of a sorted set
    ///     keeps the comparer that it gets, and calls nothing. Then, each method
    ///     of the set that compares calls the comparer. A constructor that gets
    ///     items and a comparer calls the comparer itself.
    /// </summary>
    /// <param name="method">The method of the platform.</param>
    /// <param name="calls">
    ///     The members that this call of the method calls. They are not those of
    ///     the row when the call has no two values to compare.
    /// </param>
    /// <param name="operands">The values that the method gets.</param>
    /// <param name="keeper">The collection that the code calls the method on. Null when there is none.</param>
    private Followed? FollowKeptComparer(
        IMethodSymbol method,
        PlatformCallbacks calls,
        IReadOnlyCollection<Operand> operands,
        ExpressionSyntax? keeper)
    {
        const PlatformCallbacks comparing =
            PlatformCallbacks.Compare | PlatformCallbacks.Equals | PlatformCallbacks.Hash;

        if ((PlatformCallers.Of(method) & comparing) == PlatformCallbacks.None)
        {
            return null;
        }

        // A put into an empty collection calls only the member that makes a hash code.
        bool comparesTwo = (calls & ComparesTwo) != PlatformCallbacks.None;

        // The collection has the state of the comparer that its constructor got.
        IEnumerable<ExpressionSyntax> holders =
            method.MethodKind == MethodKind.Constructor
                ? operands.Any(static operand => SodaFlowSymbols.IsSequence(operand.Parameter))
                    ? operands
                        .Where(static operand => SodaFlowSymbols.IsComparer(operand.Parameter))
                        .Select(static operand => operand.Written)
                    : []
                : keeper != null
                    ? [keeper]
                    : [];

        Followed? all = null;

        foreach (ExpressionSyntax holder in holders)
        {
            if (this.Eval(holder) is not { Members: { HasType: true } members } kept)
            {
                continue;
            }

            all =
                members.Implementations(SodaFlowSymbols.IsComparerInterface)
                    .Where(member => comparesTwo || MakesAHashCode(member))
                    .Aggregate(
                        seed: all,
                        func: (found, member) =>
                            Both(
                                first: found,
                                second: this.FollowImplied(
                                    call: holder,
                                    member: member,
                                    owner: kept,
                                    arguments: [])));
        }

        return all;
    }

    private static bool MakesAHashCode(IMethodSymbol member) =>
        member.Name == nameof(GetHashCode)
        || member.ExplicitInterfaceImplementations.Any(static contract => contract.Name == nameof(GetHashCode));

    /// <summary>
    ///     The objects of this compilation in a value that a method of the
    ///     platform gets, when the method can call a member of them. The value is
    ///     such an object, a sequence of them, or a tuple that has one.
    /// </summary>
    /// <param name="state">The state of the value.</param>
    /// <param name="written">The type of the expression for the value. Null when it is not known.</param>
    /// <param name="asSequence">True when the method reads the elements of a sequence that it gets here.</param>
    /// <param name="compared">The types of the values that the method compares. Empty for each type.</param>
    /// <param name="keeps">
    ///     For a pair, true for the position of each part that the method
    ///     compares. Null when the method compares each part that has a type of
    ///     <paramref name="compared" />.
    /// </param>
    private static IEnumerable<(LoopState State, Construction Members)> CallbackOwners(
        LoopState state,
        ITypeSymbol? written,
        bool asSequence,
        ImmutableArray<ITypeSymbol> compared,
        Func<int, bool>? keeps = null)
    {
        if (state.Members is not { } members)
        {
            return [];
        }

        // The state of a tuple, of a Tuple object, or of a KeyValuePair has the state of each part.
        bool ofATuple = !members.HasType;

        if (ofATuple || (SodaFlowSymbols.HoldsParts(written) && members.HasParts))
        {
            return members.PartsAt(ofATuple)
                .Where(part => keeps?.Invoke(part.Position) ?? true)
                .SelectMany(part =>
                    CallbackOwners(state: part.State, written: null, asSequence: false, compared: compared));
        }

        // A list has the members of its elements in its state. The list is one of those objects
        // only for a method that reads elements. A type such as object does not tell a list from
        // an element, and the state then says which the value is.
        // The elements of a dictionary are pairs, and the method compares the keys in them. The
        // elements of a lookup are groups, and each group has its key.
        ITypeSymbol? element = asSequence ? SodaFlowSymbols.ElementTypeOf(written) : null;

        // A collection with no type argument, as an ArrayList is, does not say the type of its
        // elements. The object in its state is then one of them.
        bool isOwner =
            ((written == null || members.Is(written)) && !(state.IsContainer && Construction.SaysNothing(written)))
            || (element != null
                && (members.Is(element)
                    || (element is INamedTypeSymbol pair
                        && (SodaFlowSymbols.HoldsParts(pair) || SodaFlowSymbols.IsGrouping(pair))
                        && pair.TypeArguments.Any(members.Is))))
            || (asSequence && element == null && SodaFlowSymbols.IsSequence(written));

        return isOwner && (compared.IsEmpty || compared.Any(members.Is)) ? [(state, members)] : [];
    }

    // The state of a value without the parts of a pair that the method does not compare.
    private static LoopState KeptParts(LoopState state, ITypeSymbol? written, Func<int, bool>? keeps)
    {
        if (keeps == null
            || state.Members is not { } members
            || (members.HasType && !(SodaFlowSymbols.HoldsParts(written) && members.HasParts)))
        {
            return state;
        }

        return members.PartsAt(ofATuple: !members.HasType)
            .Where(part => keeps(part.Position))
            .Aggregate(seed: LoopState.None, func: static (joined, part) => joined.Join(part.State));
    }

    // The type of each part of a pair: a tuple, a KeyValuePair, or a Tuple object. For a sequence, the
    // parts of its element. Null for each other type.
    private static IReadOnlyList<ITypeSymbol>? PartTypesOf(ITypeSymbol? type) =>
        type switch
        {
            INamedTypeSymbol { IsTupleType: true } tuple => [.. tuple.TupleElements.Select(static part => part.Type)],
            INamedTypeSymbol { TypeArguments.IsEmpty: false } holder when SodaFlowSymbols.HoldsParts(holder) =>
                holder.TypeArguments,
            _ when SodaFlowSymbols.ElementTypeOf(type) is { } element => PartTypesOf(element),
            _ => null
        };

    private IEnumerable<IMethodSymbol> CallbackMembers(
        Construction members,
        PlatformCallbacks callbacks,
        bool formats,
        bool pads)
    {
        SodaFlowSymbols symbols = this.region.Symbols;

        if ((callbacks & PlatformCallbacks.Compare) != PlatformCallbacks.None)
        {
            foreach (IMethodSymbol compare in members.Implementations(SodaFlowSymbols.IsComparableInterface))
            {
                yield return compare;
            }
        }

        if ((callbacks & PlatformCallbacks.Equals) != PlatformCallbacks.None)
        {
            foreach (IMethodSymbol typed in members.Implementations(SodaFlowSymbols.IsEquatableInterface))
            {
                yield return typed;
            }

            if (symbols.ObjectEquals is { } general)
            {
                yield return general;
            }
        }

        if ((callbacks & PlatformCallbacks.Hash) != PlatformCallbacks.None && symbols.ObjectGetHashCode is { } hash)
        {
            yield return hash;
        }

        if ((callbacks & PlatformCallbacks.Text) != PlatformCallbacks.None && symbols.ObjectToString is { } text)
        {
            // A composite format calls TryFormat of ISpanFormattable on an object that implements it.
            // It calls the ToString of IFormattable where TryFormat fails, or where a width puts spaces
            // before the text.
            if (formats
                && symbols is { SpanFormattableTryFormat: { } tryFormat, FormattableToString: { } formattable }
                && members.Is(tryFormat.ContainingType))
            {
                yield return tryFormat;

                if (pads || !AlwaysFormats(members: members, tryFormat: tryFormat))
                {
                    yield return formattable;
                }

                if (ReferenceEquals(objA: members.Narrow(tryFormat.ContainingType), objB: members))
                {
                    yield break;
                }
            }

            yield return text;
        }
    }

    // True when the TryFormat of each type of the object returns only the literal true. Then, it
    // never fails.
    private static bool AlwaysFormats(Construction members, IMethodSymbol tryFormat) =>
        members.Types.Any()
        && members.Types.All(type =>
            type.FindImplementationForInterfaceMember(tryFormat)
                    ?.DeclaringSyntaxReferences.FirstOrDefault()
                    ?.GetSyntax()
                is MethodDeclarationSyntax declaration
            && (declaration.ExpressionBody is { } arrow
                ? arrow.Expression.IsKind(SyntaxKind.TrueLiteralExpression)
                : declaration.Body is { } body
                  && body.DescendantNodes(descendIntoChildren: static node =>
                          node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                      .OfType<ReturnStatementSyntax>()
                      .All(static returned => returned.Expression.IsKind(SyntaxKind.TrueLiteralExpression))));

    // True when a composite format can put spaces before a value: a run found that only a positive
    // width does that. The format is the first argument of type string. A format that is not a
    // constant can have such a width.
    private bool CanPad(IEnumerable<Operand> operands) =>
        operands.FirstOrDefault(static operand => operand.Parameter is { SpecialType: SpecialType.System_String })
            is not { Written: { } format }
        || this.region.ModelFor(format).GetConstantValue(format) is not { Value: string text }
        || HasAPositiveWidth(text);

    // True for a composite format with an item such as {0,5}. Two { characters in a sequence are one
    // { character of the text.
    private static bool HasAPositiveWidth(string format)
    {
        for (int at = format.IndexOf('{'); at >= 0; at = format.IndexOf(value: '{', startIndex: at + 1))
        {
            if (at + 1 < format.Length && format[at + 1] == '{')
            {
                at++;

                continue;
            }

            // The index and the width come before the format of the item.
            int end = format.IndexOfAny(anyOf: [':', '}'], startIndex: at);
            string head =
                end < 0 ? format.Substring(at + 1) : format.Substring(startIndex: at + 1, length: end - at - 1);
            int comma = head.IndexOf(',');

            if (comma >= 0
                && int.TryParse(
                    s: head.Substring(comma + 1).Trim(),
                    style: NumberStyles.AllowLeadingSign,
                    provider: CultureInfo.InvariantCulture,
                    result: out int width)
                && width > 0)
            {
                return true;
            }
        }

        return false;
    }

    // An argument that is null or default, or a local that only gets such a value. A callee that
    // gets it for a comparer has no comparer, and uses the default one, which calls the members of
    // the objects. A parameter is the same when the call gives it such a value, and the callee
    // does not change it.
    private bool IsNoValue(ExpressionSyntax written) =>
        IsNoValueLiteral(written)
        || (Unwrapped(written) is IdentifierNameSyntax name
            && this.region.SymbolOf(name) switch
            {
                ILocalSymbol local => this.OnlyGetsNoValue(local),
                IParameterSymbol parameter => this.GetsNoValue(parameter),
                _ => false
            });

    // A call that gives no argument for an optional parameter gives its default value. The walk of
    // a callee binds each argument that the call gives, thus a parameter with no binding got none.
    private bool GetsNoValue(IParameterSymbol parameter) =>
        (this.noValues.Contains(parameter)
         || (this.depth > 0
             && !this.bound.ContainsKey(parameter)
             && parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: null }))
        && !IsWrittenIn(parameter: parameter, region: this.region);

    private static bool IsNoValueLiteral(ExpressionSyntax written) =>
        Unwrapped(written)
            is LiteralExpressionSyntax
            {
                RawKind: (int)SyntaxKind.NullLiteralExpression or (int)SyntaxKind.DefaultLiteralExpression
            }
            or DefaultExpressionSyntax;

    // True for a local whose declaration and each assignment in its block give it null or default.
    // A ref or a deconstruction can give it a value that the code does not say here. The compound
    // assignment of a comparer is ??=, and with null or default it leaves the value as it was.
    private bool OnlyGetsNoValue(ISymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarator
        && (declarator.Initializer == null || IsNoValueLiteral(declarator.Initializer.Value))
        && NamesOf(local: local, declarator: declarator, region: this.region) is { } names
        && names.All(static name =>
            !IsWritten(name)
            || (name.Parent is AssignmentExpressionSyntax assignment
                && assignment.Left == name
                && IsNoValueLiteral(assignment.Right)));

    // A comparer, or a delegate of the type Comparison, which a sort can get.
    private static bool IsComparison(ITypeSymbol? type) =>
        SodaFlowSymbols.IsComparer(type)
        || type is INamedTypeSymbol { TypeKind: TypeKind.Delegate, Name: "Comparison" }
        // A type parameter for a comparer, as in Sort<T, TComparer>.
        || (type is ITypeParameterSymbol generic && generic.ConstraintTypes.Any(SodaFlowSymbols.IsComparer));

    // The elements that a callee gets from a sequence of this compilation, when the callee
    // enumerates what it gets for the parameter.
    private LoopState Enumerated(ExpressionSyntax? written, ITypeSymbol? parameter) =>
        written != null && SodaFlowSymbols.IsSequence(parameter)
            ? this.FollowEnumeration(written)?.State ?? LoopState.None
            : LoopState.None;
}
