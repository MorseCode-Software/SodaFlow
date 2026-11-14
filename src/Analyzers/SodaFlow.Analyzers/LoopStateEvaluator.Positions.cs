using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the positions of the elements of a collection.
internal sealed partial class LoopStateEvaluator
{
    // The expression of the element that a read by a constant index gives, for a collection that
    // keeps its positions. For an array with two dimensions, the first index gives a row, and the
    // row contains the element.
    private ExpressionSyntax? PositionOf(ElementAccessExpressionSyntax access) =>
        this.ElementsOf(access.Expression) is { } all
        && this.region.ModelFor(access).GetConstantValue(access.ArgumentList.Arguments[0].Expression)
            is { Value: int index and >= 0 }
        && index < all.Count
            ? all[index]
            : null;

    /// <summary>
    ///     The expression of each element of the collection in a local, in
    ///     order, when the collection keeps its positions. Null for each other
    ///     expression.
    /// </summary>
    /// <remarks>
    ///     A collection keeps its positions when a literal made it and no code
    ///     can change it. Each use of the local in its block must be a read. A
    ///     read is an index, <c>Count</c> or <c>Length</c>, or the collection of a
    ///     foreach. <c>First</c> or <c>Last</c> of the platform with no argument
    ///     is a read also. Any other use can change the collection or give it a
    ///     second name. Then, the local has one state for all its elements.
    ///     <para>
    ///         <c>Add</c> of a list of the platform puts a value after each element
    ///         that the list has, and moves none of them. Thus, a statement at the
    ///         first level of the block of the local that is such a call is no
    ///         change to a position. A read has the elements of each such statement
    ///         before it. A function can run after the next such statement, thus a
    ///         read in a function has no positions for such a list.
    ///     </para>
    /// </remarks>
    private IReadOnlyList<ExpressionSyntax>? ElementsOf(SyntaxNode collection)
    {
        if (this.region.SymbolOf(collection) is not ILocalSymbol local)
        {
            return null;
        }

        if (!this.Frame.Positions.TryGetValue(key: local, value: out IReadOnlyList<ExpressionSyntax>? known))
        {
            known = this.LiteralAndAddsOf(local);
            this.Frame.Positions[local] = known;
        }

        // Each element of the literal is in the declaration, which is before each read.
        if (known == null
            || known.Count == 0
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } declared
            || known[known.Count - 1].SpanStart < declared.Span.End)
        {
            return known;
        }

        return FunctionOf(collection) == FunctionOf(declared)
            ? [.. known.TakeWhile(element => element.SpanStart < collection.SpanStart)]
            : null;
    }

    // The elements of the literal that made a local, and then the value of each Add of the local.
    // Each such Add is a statement at the first level of the block of the local. Null when a use of
    // the local is not such an Add and not a read.
    private IReadOnlyList<ExpressionSyntax>? LiteralAndAddsOf(ILocalSymbol local)
    {
        if (this.LiteralOf(local) is not (var written, var uses)
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().FirstAncestorOrSelf<BlockSyntax>()
                is not { } scope)
        {
            return null;
        }

        List<ExpressionSyntax> all = [.. written];

        foreach (IdentifierNameSyntax use in uses)
        {
            if (this.AddedBy(use: use, scope: scope) is { } added)
            {
                all.Add(added);
            }
            else if (!this.IsARead(use))
            {
                return null;
            }
        }

        return all;
    }

    // The value that a statement at the first level of a block adds to a list of the platform. The
    // name is the name of the list in that statement. Null for each other use of the name.
    private ExpressionSyntax? AddedBy(SyntaxNode use, BlockSyntax scope) =>
        use.Parent is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "Add",
            Parent: InvocationExpressionSyntax
            {
                ArgumentList.Arguments: { Count: 1 } arguments, Parent: ExpressionStatementSyntax statement
            } call
        } access
        && access.Expression == use
        && statement.Parent == scope
        && this.region.SymbolOf(call) is IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" } owner }
        && SodaFlowSymbols.IsPlatform(owner)
            ? arguments[0].Expression
            : null;

    // The elements of the literal that made a local, and each name of the local in its block.
    private (IReadOnlyList<ExpressionSyntax> Elements, IEnumerable<IdentifierNameSyntax> Uses)? LiteralOf(
        ILocalSymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
        && this.LiteralElements(made: made, type: local.Type) is { } written
        && declarator.FirstAncestorOrSelf<BlockSyntax>() is { } scope
            ? (written,
                scope.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(name => name.Identifier.ValueText == local.Name
                                   && SymbolEqualityComparer.Default.Equals(
                                       x: this.region.SymbolOf(name),
                                       y: local)))
            : null;

    // The methods of the platform that put one value into their collection, and that a run on an
    // empty collection has a result for.
    private static readonly ImmutableHashSet<string> PutsOfOneValue =
        ImmutableHashSet.Create("Add", "TryAdd", "SetItem", "GetOrAdd", "AddOrUpdate", "Enqueue");

    /// <summary>
    ///     True for a collection that has no element when a put into it runs. The
    ///     expression makes an empty collection, or it is the first use of a local
    ///     that such an expression made.
    /// </summary>
    /// <remarks>
    ///     No code before the first use can put an object into the collection. A
    ///     use in a loop or in a function of the block can run after a second use.
    ///     An argument of the put runs before the put. Thus, the put has no second
    ///     use of the local.
    /// </remarks>
    /// <param name="collection">The expression for the collection.</param>
    /// <param name="put">The call or the assignment that puts a value into the collection.</param>
    public bool IsEmptyHere(ExpressionSyntax collection, SyntaxNode put)
    {
        ExpressionSyntax written = Unwrapped(collection);

        if (this.MakesAnEmptyCollection(written))
        {
            return true;
        }

        if (written is not IdentifierNameSyntax name
            || this.region.SymbolOf(name) is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: { } made } declarator
            || !this.MakesAnEmptyCollection(made)
            || declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } scope)
        {
            return false;
        }

        IdentifierNameSyntax[] firstTwo =
        [
            .. scope.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(use =>
                    use.Identifier.ValueText == local.Name
                    && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: local))
                .Take(2)
        ];

        return firstTwo[0] == name
               && (firstTwo.Length == 1 || !put.Span.Contains(firstTwo[1].Span))
               && !name.Ancestors()
                   .TakeWhile(ancestor => ancestor != scope)
                   .Any(static ancestor =>
                       LoopBodyOf(ancestor) != null
                       || ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }

    // True for an expression that makes a collection of the platform with no element. That is an
    // empty collection expression, or the Empty of an immutable collection. It is also a
    // constructor or a CreateBuilder that gets only comparers and numbers.
    private bool MakesAnEmptyCollection(ExpressionSyntax made) =>
        Unwrapped(made) switch
        {
            CollectionExpressionSyntax { Elements.Count: 0 } => true,
            BaseObjectCreationExpressionSyntax { Initializer: null or { Expressions.Count: 0 } } creation =>
                this.GetsOnlyComparersAndNumbers(creation),
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateBuilder" }
            } call => this.GetsOnlyComparersAndNumbers(call),
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Empty" } access =>
                this.region.SymbolOf(access) is IFieldSymbol { IsStatic: true, ContainingType: { } owner }
                && SodaFlowSymbols.IsPlatform(owner),
            _ => false
        };

    private bool GetsOnlyComparersAndNumbers(SyntaxNode call) =>
        this.region.SymbolOf(call) is IMethodSymbol callee
        && SodaFlowSymbols.IsPlatform(callee.ContainingType)
        && callee.Parameters.All(static parameter =>
            SodaFlowSymbols.IsComparer(parameter.Type) || parameter.Type.SpecialType == SpecialType.System_Int32);

    /// <summary>
    ///     True for a collection that has one element, as far as the code says.
    ///     The expression is a literal with one element, or a local that such a
    ///     literal made and whose number of elements no code can change.
    /// </summary>
    /// <remarks>
    ///     An array has the number of elements that it got. Thus, each use of an
    ///     array in a local keeps it, but an assignment to the local and a
    ///     <c>ref</c> to it. A list keeps it while each use is a read, a
    ///     <c>Sort</c>, or a <c>Reverse</c>.
    /// </remarks>
    private bool HasOneElement(ExpressionSyntax collection)
    {
        while (collection is ParenthesizedExpressionSyntax inner)
        {
            collection = inner.Expression;
        }

        switch (collection)
        {
            // A collection expression in a call has one element for each type that the compiler
            // makes from it. A spread has the number of elements of its collection.
            case CollectionExpressionSyntax { Elements.Count: 1 } written:
                return written.Elements[0] is not SpreadElementSyntax spread || this.HasOneElement(spread.Expression);
            case ConditionalExpressionSyntax either:
                return this.HasOneElement(either.WhenTrue) && this.HasOneElement(either.WhenFalse);
            case SwitchExpressionSyntax { Arms.Count: > 0 } chosen:
                return chosen.Arms.All(arm => this.HasOneElement(arm.Expression));
        }

        if (this.region.SymbolOf(collection) is not ILocalSymbol local)
        {
            return this.LiteralElements(made: collection, type: this.region.TypeOf(collection)) is { Count: 1 };
        }

        if (!this.Frame.HasOneElement.TryGetValue(key: local, value: out bool known))
        {
            known =
                this.LiteralOf(local) is ({ Count: 1 }, var uses)
                && uses.All(local.Type is IArrayTypeSymbol ? static name => !IsWritten(name) : this.KeepsTheNumber);

            this.Frame.HasOneElement[local] = known;
        }

        return known;
    }

    // A read of a list, or a call of its Sort or its Reverse, which change only the order.
    private bool KeepsTheNumber(IdentifierNameSyntax name) =>
        this.IsARead(name)
        || (name.Parent is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Sort" or "Reverse", Parent: InvocationExpressionSyntax call
            }
            && this.region.SymbolOf(call)
                is IMethodSymbol { ReducedFrom: null, ContainingType: { Name: "List" or "ArrayList" } owner }
            && SodaFlowSymbols.IsPlatform(owner));

    // The elements of a literal whose object has them in the order of the code. A spread has a
    // number of elements that the code does not say.
    private IReadOnlyList<ExpressionSyntax>? LiteralElements(ExpressionSyntax made, ITypeSymbol? type) =>
        made switch
        {
            CollectionExpressionSyntax collection
                when SodaFlowSymbols.KeepsTheOrder(type)
                     && collection.Elements.All(static element => element is ExpressionElementSyntax) =>
                [.. collection.Elements.Cast<ExpressionElementSyntax>().Select(static element => element.Expression)],
            InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ArrayInitializerExpression } initializer =>
                [.. initializer.Expressions],
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => [.. initializer.Expressions],
            ImplicitArrayCreationExpressionSyntax implied => [.. implied.Initializer.Expressions],
            // A List with no argument for its constructor, and an Add for each element.
            ObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 }, Initializer: { } added }
                when this.IsAListOf(creation: made, added: added) => [.. added.Expressions],
            ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0, Initializer: { } added }
                when this.IsAListOf(creation: made, added: added) => [.. added.Expressions],
            // The same with no initializer: the list has no element.
            BaseObjectCreationExpressionSyntax { ArgumentList: null or { Arguments.Count: 0 }, Initializer: null }
                when SodaFlowSymbols.KeepsTheOrder(this.region.TypeOf(made)) => [],
            _ => null
        };

    // The initializer calls Add one time for each element. An element with braces gives the
    // arguments of one Add, and is not the expression of an element.
    private bool IsAListOf(ExpressionSyntax creation, InitializerExpressionSyntax added) =>
        added.Expressions.All(static element => element is not InitializerExpressionSyntax)
        && SodaFlowSymbols.KeepsTheOrder(this.region.TypeOf(creation));

    // The name is a name of the local, thus it is the owner in an index or in a member access.
    private bool IsARead(SyntaxNode name) =>
        name.Parent switch
        {
            ElementAccessExpressionSyntax access => !IsWritten(access),
            // The property. A method with the name Count can be one of this compilation.
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "Length" } member =>
                member.Parent is not InvocationExpressionSyntax,
            // A method of this compilation with the name First can change the collection.
            MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "First" or "Last",
                Parent: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } call
            } =>
                this.region.SymbolOf(call) is IMethodSymbol { ReducedFrom.ContainingType: { } owner }
                && SodaFlowSymbols.IsLinqEnumerable(owner),
            ForEachStatementSyntax => true,
            _ => false
        };

    // True when the code can change the element that an element access names. The code changes
    // it with an assignment, an increment, a ref or out argument, or a ref to the element.
    private static bool IsWritten(SyntaxNode element)
    {
        // A target of a deconstruction is in a tuple on the left of an assignment.
        while (true)
        {
            switch (element.Parent)
            {
                case ParenthesizedExpressionSyntax around:
                    element = around;
                    continue;
                case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
                    element = tuple;
                    continue;
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left == element;
                case ArgumentSyntax argument:
                    return !argument.RefKindKeyword.IsKind(SyntaxKind.None);
                case RefExpressionSyntax:
                    return true;
                case PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax:
                    return element.Parent.Kind()
                        is SyntaxKind.PreIncrementExpression
                        or SyntaxKind.PreDecrementExpression
                        or SyntaxKind.PostIncrementExpression
                        or SyntaxKind.PostDecrementExpression;
                default:
                    return false;
            }
        }
    }
}
