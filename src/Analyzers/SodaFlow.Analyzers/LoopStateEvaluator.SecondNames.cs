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
    // local gets no value and no other location in its function. No code between the declaration and the use gives
    // a part of the location a value. Null for each other expression.
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
            || this.UsesOf(name: local, code: code).Any(static one => one.Gives))
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
    // the field, and its set accessor only puts the value into the field. Thus, a name through the property is a
    // name through the field. Null for each other member.
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
        if (property.DeclaringSyntaxReferences[0].GetSyntax()
            is not PropertyDeclarationSyntax { AccessorList.Accessors: var accessors })
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

        return SymbolEqualityComparer.Default.Equals(x: read, y: written) ? read : null;
    }

    // The field that an expression reads, by its name alone or through this. Null for each other expression.
    private IFieldSymbol? AFieldOfThis(SyntaxNode? expression) =>
        expression is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
            ? this.region.SymbolOf(expression) as IFieldSymbol
            : null;
}
