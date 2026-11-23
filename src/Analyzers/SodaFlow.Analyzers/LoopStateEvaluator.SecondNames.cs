using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the second names that the two records of names use. One is the record of the writes
// through second names, from NameKeyOf. The other is the record of the names of one object, from NameOfAnObjectIn.
internal sealed partial class LoopStateEvaluator
{
    // The location that a local with ref names at a use. It is the location of the declaration of the local. The
    // local gets no other location in its function, and only an assignment gives it a value, which goes into the
    // location. No code between the declaration and the use gives a part of the location a value. Null for each
    // other expression.
    private ExpressionSyntax? TheDeclaredLocationOf(ExpressionSyntax written)
    {
        if (Unwrapped(written) is not IdentifierNameSyntax use
            || this.region.SymbolOf(use) is not ILocalSymbol { RefKind: not RefKind.None } local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax
                {
                    Initializer.Value: RefExpressionSyntax { Expression: var location }
                } declarator
            || FunctionOf(declarator) is not { } code
            || this.UsesOf(name: local, code: code)
                .Any(one => one.Gives && !IsAnAssignmentTo(code.FindToken(one.Position).Parent)))
        {
            return null;
        }

        ExpressionSyntax target = Unwrapped(location);

        return target.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(part => !(target is MemberAccessExpressionSyntax access && access.Name == part)
                         && this.region.SymbolOf(part)
                             is (ILocalSymbol or IParameterSymbol or IFieldSymbol) and { } symbol
                         && this.GetsAValue(
                             name: symbol,
                             code: code,
                             after: declarator.Span.End,
                             before: use.SpanStart,
                             orAFunctionUsesIt: false))
            ? null
            : location;
    }

    /// <summary>
    ///     The local that a ref field of a ref struct in a local refers to, as
    ///     <c>r.Target</c> after <c>R r = new(ref x)</c>. Null for each other
    ///     member access.
    /// </summary>
    /// <remarks>
    ///     The constructor puts a ref to one of its parameters into the field.
    ///     No code of the function gives the struct local a second value, or the
    ///     field a second ref. Thus, the field refers to the local of the
    ///     argument at each use.
    /// </remarks>
    public ExpressionSyntax? TheLocalOfARefField(MemberAccessExpressionSyntax access)
    {
        if (this.region.SymbolOf(access) is not IFieldSymbol { RefKind: not RefKind.None, IsStatic: false } field
            || Unwrapped(access.Expression) is not IdentifierNameSyntax holder
            || this.region.SymbolOf(holder) is not ILocalSymbol { RefKind: RefKind.None } local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax
                {
                    Initializer.Value: BaseObjectCreationExpressionSyntax { ArgumentList: { } arguments } creation
                } declarator
            || FunctionOf(declarator) is not { } code
            || FunctionOf(access) != code
            || this.region.SymbolOf(creation) is not IMethodSymbol constructor
            || TheParameterInARefField(constructor: constructor, field: field) is not { } parameter
            || this.UsesOf(name: local, code: code).Any(static one => one.Gives)
            || this.GivesARefTo(field: field, holder: local, code: code))
        {
            return null;
        }

        for (int position = 0; position < arguments.Arguments.Count; position++)
        {
            ArgumentSyntax argument = arguments.Arguments[position];

            if (SymbolEqualityComparer.Default.Equals(
                    x: SodaFlowSymbols.ParameterOf(callee: constructor, argument: argument, position: position),
                    y: parameter))
            {
                return NameByRef(argument) is { } name
                       && this.region.SymbolOf(name) is ILocalSymbol { RefKind: RefKind.None }
                    ? name
                    : null;
            }
        }

        return null;
    }

    // The parameter with ref whose ref a constructor of this compilation puts into a ref field, as in
    // this.Target = ref target. The constructor calls no other constructor and gives the field no
    // other ref. Null for each other constructor.
    private static IParameterSymbol? TheParameterInARefField(IMethodSymbol constructor, IFieldSymbol field)
    {
        if (constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is not ConstructorDeclarationSyntax { Initializer: null } declaration)
        {
            return null;
        }

        IParameterSymbol? found = null;

        foreach (AssignmentExpressionSyntax assignment in declaration.DescendantNodes()
                     .OfType<AssignmentExpressionSyntax>())
        {
            string? target =
                assignment.Left switch
                {
                    IdentifierNameSyntax name => name.Identifier.ValueText,
                    MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } own =>
                        own.Name.Identifier.ValueText,
                    _ => null
                };

            if (target != field.Name)
            {
                continue;
            }

            if (found != null
                || assignment.Right is not RefExpressionSyntax { Expression: IdentifierNameSyntax given }
                || constructor.Parameters.FirstOrDefault(one => one.Name == given.Identifier.ValueText)
                    is not { RefKind: RefKind.Ref } parameter)
            {
                return null;
            }

            found = parameter;
        }

        return found;
    }

    // True when the code gives a ref field of a local a second ref, as in r.Target = ref y.
    private bool GivesARefTo(IFieldSymbol field, ISymbol holder, SyntaxNode code) =>
        code.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment is
                {
                    Right: RefExpressionSyntax,
                    Left: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } target
                }
                && target.Name.Identifier.ValueText == field.Name
                && owner.Identifier.ValueText == holder.Name
                && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(owner), y: holder));

    // True for the name that an assignment of a value with no ref gives a value.
    private static bool IsAnAssignmentTo(SyntaxNode? name) =>
        name?.Parent is AssignmentExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
            Right: not RefExpressionSyntax
        } assignment
        && assignment.Left == name;

    // The local of a collection that a second local got in its declaration. No code gives either of them a value
    // after that, thus the two are one collection. Each other name is its own collection.
    private ISymbol TheCollectionIn(ISymbol named) =>
        named is ILocalSymbol second
        && second.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: IdentifierNameSyntax value } declarator
        && this.region.SymbolOf(value) is ILocalSymbol first
        && FunctionOf(declarator) is { } code
        && !this.UsesOf(name: second, code: code).Any(static one => one.Gives)
        && !this.UsesOf(name: first, code: code)
            .Any(one => one.Gives && (one.InAFunction || one.Position > declarator.Span.End))
            ? first
            : named;

    // The key of a location for the names of this: see NotesANameOfThis. It is the key of LocationKeyOf, or the
    // name of a second location of one object, from NameOfAnObjectIn. An element at an index in a local is no such
    // name, because its key here has the local of the index.
    private NameKey? ThisKeyOf(ExpressionSyntax location) =>
        this.LocationKeyOf(location) is { } key
            ? new NameKey(of: key.Of, owner: key.Owner, index: key.Index)
            : this.NameOfAnObjectIn(location) switch
            {
                { Index: null } named => new NameKey(of: named.Of, owner: named.Owner, index: -1),
                { Index: not ISymbol } named =>
                    new NameKey(of: named.Of, owner: named.Owner, index: -3, key: named.Index),
                _ => null
            };

    // Records that a sure Add of a list in a local puts the object of the item at an index. The element there is
    // then a second name of that object, as after an assignment to it.
    private void NotesAnAdd(ExpressionSyntax collection, ExpressionSyntax item, int index)
    {
        if (this.quiet
            || Unwrapped(collection) is not IdentifierNameSyntax list
            || this.region.SymbolOf(list) is not ILocalSymbol local
            || collection.Parent?.Parent is not InvocationExpressionSyntax call
            || FunctionOf(call) is not { } code)
        {
            return;
        }

        NameKey name = new(of: local, owner: null, index: index);
        this.NotesALink(name: name, value: item, replaces: true);
        this.NotesNamesOfOneObject(target: (local, null, (decimal)index), at: call, value: item, replaces: true);

        if (Unwrapped(item) is ThisExpressionSyntax || this.IsANameOfThis(item))
        {
            this.Frame.NamesOfThis.Add((null, name, code, call.Span.End));
        }
    }

    // The field of this object that a property of this compilation reads and writes. Its get accessor only reads
    // the field, and a set accessor, where it has one, only puts the value into the field. Thus, a name through the
    // property is a name through the field. Null for each other member.
    private IFieldSymbol? TheFieldBehind(ISymbol member)
    {
        if (member is not IPropertySymbol { IsVirtual: false, IsOverride: false } property
            || property.DeclaringSyntaxReferences.IsEmpty)
        {
            return null;
        }

        if (!this.region.FieldsBehind.TryGetValue(key: property, value: out IFieldSymbol? field))
        {
            field = this.FieldBehind(property);
            this.region.FieldsBehind[property] = field;
        }

        return field;
    }

    // The same, read from the declaration of the property.
    private IFieldSymbol? FieldBehind(ISymbol property)
    {
        if (property.DeclaringSyntaxReferences[0].GetSyntax() is not PropertyDeclarationSyntax declaration)
        {
            return null;
        }

        if (declaration.ExpressionBody is { Expression: var only })
        {
            return this.AFieldOfThis(only);
        }

        if (declaration.AccessorList is not { Accessors: var accessors })
        {
            return null;
        }

        IFieldSymbol? read = null;
        IFieldSymbol? written = null;

        foreach (AccessorDeclarationSyntax accessor in accessors)
        {
            ExpressionSyntax? body =
                accessor.ExpressionBody?.Expression
                ?? (accessor.Body?.Statements is { Count: 1 } statements
                    ? statements[0] switch
                    {
                        ReturnStatementSyntax { Expression: { } returned } => returned,
                        ExpressionStatementSyntax { Expression: { } done } => done,
                        _ => null
                    }
                    : null);

            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                read = this.AFieldOfThis(body);
            }
            else if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                     && body is AssignmentExpressionSyntax
                     {
                         RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                         Right: IdentifierNameSyntax value
                     } put
                     && this.region.SymbolOf(value) is IParameterSymbol)
            {
                written = this.AFieldOfThis(put.Left);
            }
        }

        return accessors.All(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
               || SymbolEqualityComparer.Default.Equals(x: read, y: written)
            ? read
            : null;
    }

    // The field that a method of this compilation returns. The method gets no argument, and its body only returns a
    // field that it names alone or through this. Null for each other method.
    private IFieldSymbol? FieldReturnedBy(IMethodSymbol method)
    {
        if (method is not
            {
                MethodKind: MethodKind.Ordinary,
                IsVirtual: false,
                IsAbstract: false,
                IsOverride: false,
                IsAsync: false,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false,
                Parameters.IsEmpty: true,
                DeclaringSyntaxReferences.IsEmpty: false
            })
        {
            return null;
        }

        if (!this.region.FieldsBehind.TryGetValue(key: method.OriginalDefinition, value: out IFieldSymbol? field))
        {
            field =
                method.DeclaringSyntaxReferences[0].GetSyntax() is MethodDeclarationSyntax declaration
                    ? this.AFieldOfThis(ReturnedBy(arrow: declaration.ExpressionBody, body: declaration.Body))
                    : null;

            this.region.FieldsBehind[method.OriginalDefinition] = field;
        }

        return field;
    }

    // The name that a static property of this compilation gives. Its get accessor only returns an element at a
    // constant index of an array in a static field. A property that returns a field is a name through
    // TheFieldBehind. Null for each other member.
    private (ISymbol Of, ISymbol? Owner, object? Index)? TheStaticElementBehind(IPropertySymbol property)
    {
        if (property is not { IsStatic: true, SetMethod: null, DeclaringSyntaxReferences: { Length: 1 } references }
            || references[0].GetSyntax() is not PropertyDeclarationSyntax declaration)
        {
            return null;
        }

        AccessorDeclarationSyntax? getter =
            declaration.AccessorList?.Accessors is { Count: 1 } accessors ? accessors[0] : null;

        return ReturnedBy(arrow: declaration.ExpressionBody ?? getter?.ExpressionBody, body: getter?.Body)
                   is { } returned
               && Unwrapped(returned) is ElementAccessExpressionSyntax element
               && this.NameOfAnObjectIn(element) is { Owner: null, Index: not (null or ISymbol) } named
            ? named
            : null;
    }

    // The expression that a body with an arrow or with one return statement returns. Null for each other body.
    private static ExpressionSyntax? ReturnedBy(ArrowExpressionClauseSyntax? arrow, BlockSyntax? body) =>
        arrow?.Expression
        ?? (body?.Statements is { Count: 1 } statements
            ? (statements[0] as ReturnStatementSyntax)?.Expression
            : null);

    // The field that a method of this compilation gives an out parameter. Each statement of the method only gives an
    // out parameter a literal or a field, which it names alone or through this. Null for each other parameter.
    private IFieldSymbol? FieldGivenTo(IMethodSymbol method, IParameterSymbol parameter)
    {
        if (method is not { MethodKind: MethodKind.Ordinary, IsVirtual: false, IsAbstract: false, IsOverride: false }
            || parameter.RefKind != RefKind.Out
            || method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not MethodDeclarationSyntax declaration)
        {
            return null;
        }

        IReadOnlyList<ExpressionSyntax> each =
            declaration.ExpressionBody is { Expression: var only }
                ? [only]
                : declaration.Body is { } body
                  && body.Statements.All(static statement => statement is ExpressionStatementSyntax)
                    ? [.. body.Statements.Cast<ExpressionStatementSyntax>().Select(static one => one.Expression)]
                    : [];

        IFieldSymbol? given = null;

        foreach (ExpressionSyntax one in each)
        {
            if (one is not AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                    Left: IdentifierNameSyntax target,
                    Right: var value
                }
                || this.region.SymbolOf(target) is not IParameterSymbol { RefKind: RefKind.Out } assigned)
            {
                return null;
            }

            bool isTheParameter = SymbolEqualityComparer.Default.Equals(x: assigned, y: parameter);

            if (value is LiteralExpressionSyntax && !isTheParameter)
            {
                continue;
            }

            if (this.AFieldOfThis(value) is not { } field)
            {
                return null;
            }

            if (isTheParameter)
            {
                given = field;
            }
        }

        return given;
    }

    // True for a method whose parameters are all out parameters, and whose code only gives each of them
    // a field or a literal: see FieldGivenTo. Its call changes no member and no name.
    private bool GivesOnlyFields(IMethodSymbol method) =>
        method.Parameters is { IsEmpty: false } parameters
        && parameters.All(static parameter => parameter.RefKind == RefKind.Out)
        && this.FieldGivenTo(method: method.OriginalDefinition, parameter: parameters[0].OriginalDefinition) != null;

    /// <summary>
    ///     Records that a local holds the object of a field after a call that
    ///     gives the local the field through an out parameter. A Deconstruct
    ///     method gives each part as such a call does.
    /// </summary>
    /// <param name="target">The argument, or the designation of a part.</param>
    /// <param name="parameter">The out parameter.</param>
    /// <param name="method">The method that the call runs.</param>
    /// <param name="receiver">The object of the call, when the call has one.</param>
    /// <param name="call">The call, or the deconstruction.</param>
    /// <param name="replaces">True when the call always gives the local the field.</param>
    public void NotesAFieldGivenOut(
        SyntaxNode target,
        IParameterSymbol parameter,
        IMethodSymbol method,
        ExpressionSyntax? receiver,
        SyntaxNode call,
        bool replaces)
    {
        if (this.quiet
            || this.function != null
            || !replaces
            || target switch
                {
                    DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single } =>
                        this.region.ModelFor(single).GetDeclaredSymbol(single),
                    SingleVariableDesignationSyntax single => this.region.ModelFor(single).GetDeclaredSymbol(single),
                    IdentifierNameSyntax name => this.region.SymbolOf(name),
                    _ => null
                } is not ILocalSymbol { RefKind: RefKind.None } local
            || this.FieldGivenTo(method: method.OriginalDefinition, parameter: parameter.OriginalDefinition)
                is not { } field
            || !SymbolEqualityComparer.Default.Equals(x: field.Type, y: local.Type)
            || FunctionOf(call) is not { } code)
        {
            return;
        }

        this.LeavesItsRecords((local, null, null));

        if (field.IsStatic)
        {
            this.JoinsTheRecordOf(
                target: (local, null, null),
                from: (field, null, null),
                use: call,
                code: code,
                at: call);
        }
        else if (receiver != null
                 && Unwrapped(receiver) is IdentifierNameSyntax holder
                 && this.region.SymbolOf(holder) is { } owner
                 && KeepsItsObject(owner))
        {
            this.JoinsTheRecordOf(
                target: (local, null, null),
                from: (field, owner, null),
                use: holder,
                code: code,
                at: call);
        }
    }

    // The field that an expression reads, by its name alone or through this. Null for each other expression.
    private IFieldSymbol? AFieldOfThis(SyntaxNode? expression) =>
        expression is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
            ? this.region.SymbolOf(expression) as IFieldSymbol
            : null;
}
