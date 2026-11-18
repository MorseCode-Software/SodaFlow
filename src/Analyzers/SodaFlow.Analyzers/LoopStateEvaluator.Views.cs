using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a view of a local array: a span, a memory, or a segment of it. A write through the
// view is a write into the elements of the array.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True for a view of the elements of an array: <c>Span</c>,
    ///     <c>ReadOnlySpan</c>, <c>Memory</c>, <c>ReadOnlyMemory</c>, or
    ///     <c>ArraySegment</c> of the platform.
    /// </summary>
    private static bool IsAView(ITypeSymbol? type) =>
        type is INamedTypeSymbol
        {
            Name: "Span" or "ReadOnlySpan" or "Memory" or "ReadOnlyMemory" or "ArraySegment",
            Arity: 1,
            ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
        };

    /// <summary>
    ///     The local array whose elements a view names, as the code wrote it
    ///     where it made the view. Null when the walk does not know such an
    ///     array.
    /// </summary>
    /// <remarks>
    ///     The code makes the view at the use from a local or a parameter that
    ///     is an array. Or, the view is a local that the same function made so.
    ///     The view and each local of the chain get no value after their
    ///     declarations. Thus, the array has the same object at the use as where
    ///     the code made the view. A view that a method gets as a parameter, or
    ///     that a call of the platform makes, such as
    ///     <c>CollectionsMarshal.AsSpan</c>, is no such view.
    /// </remarks>
    /// <param name="view">The expression of the view at the use.</param>
    private ExpressionSyntax? TheArrayOfAView(ExpressionSyntax view) =>
        FunctionOf(view) is { } code ? this.ArrayOf(made: view, code: code) : null;

    /// <summary>
    ///     The local array of a view whose element the code reads or sets, as in
    ///     <c>view[0]</c>. The view is a local, or the <c>Span</c> of a local.
    ///     Null for each other element.
    /// </summary>
    /// <remarks>
    ///     Each element access of the code comes here. Thus, the test asks only
    ///     for the symbol of a name, which the walk of the collection asked for
    ///     before.
    /// </remarks>
    public ExpressionSyntax? TheArrayOfAViewOf(ElementAccessExpressionSyntax element) =>
        Unwrapped(element.Expression) switch
        {
            IdentifierNameSyntax name => name,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Span", Expression: IdentifierNameSyntax name } =>
                name,
            _ => null
        } is { } local
        && this.region.SymbolOf(local) is ILocalSymbol { Type: var type }
        && IsAView(type)
            ? this.TheArrayOfAView(element.Expression)
            : null;

    /// <summary>
    ///     Records the puts of a call of the platform through a view of a local
    ///     array. <c>Fill</c> of a span puts its value into each element.
    ///     <c>CopyTo</c> and <c>TryCopyTo</c> put each element of their source
    ///     into a view or an array that they get.
    /// </summary>
    /// <remarks>
    ///     <c>Clear</c> puts the default value, which has no state. The elements
    ///     then keep the state that they had, which is the careful side.
    /// </remarks>
    /// <param name="method">The method.</param>
    /// <param name="receiver">The owner of the call. For an extension method, it is the first argument.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public void PutsThroughViews(
        IMethodSymbol method,
        ExpressionSyntax? receiver,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (receiver == null || this.quiet)
        {
            return;
        }

        // Only Fill of a span gets here with a receiver that is a view or an array. The count of
        // its arguments is a guard for the index.
        if (method.Name == "Fill")
        {
            if (arguments.Count == 1 && this.TheArrayOfAView(receiver) is { } filled)
            {
                ExpressionSyntax value = arguments[0].Expression;

                this.PutsThroughAView(array: filled, value: value, state: this.Eval(value));
            }

            return;
        }

        if (method.Name is not ("CopyTo" or "TryCopyTo"))
        {
            return;
        }

        foreach (ArgumentSyntax argument in arguments)
        {
            if (this.TheArrayOfAView(argument.Expression) is { } into)
            {
                this.PutsThroughAView(
                    array: into,
                    value: null,
                    state: this.Eval(this.TheArrayOfAView(receiver) ?? receiver).AsOneOfItsElements);
            }
        }
    }

    /// <summary>
    ///     Records a put of a value into the elements of an array through a
    ///     view. The view can be a slice, thus the walk does not know the
    ///     position. The array holds the value, as after <c>Array.Fill</c> with a
    ///     range that is not a constant. The put into the name of the array makes
    ///     it forget its positions.
    /// </summary>
    /// <param name="array">The array, from <see cref="TheArrayOfAView" />.</param>
    /// <param name="value">The expression of the value, when the code has one.</param>
    /// <param name="state">The state of the value.</param>
    public void PutsThroughAView(ExpressionSyntax array, ExpressionSyntax? value, LoopState state)
    {
        if (value != null)
        {
            this.Gets(collection: array, value: value);
        }
        else
        {
            this.GetsAState(collection: array, put: state);
        }

        this.Absorb(target: array, state: state);
        this.PutsIntoALocation(field: null, element: null, state: state, owner: array);
    }

    // The array of an expression that is the array, or a view of it. Each step goes one view back,
    // to an earlier declaration or to a part of the expression. Thus, the chain has an end.
    private ExpressionSyntax? ArrayOf(ExpressionSyntax made, SyntaxNode code)
    {
        for (ExpressionSyntax? at = made; at != null; at = this.OneViewBack(made: at, code: code))
        {
            if (Unwrapped(at) is IdentifierNameSyntax name
                && this.region.SymbolOf(name) is { } array
                    and (ILocalSymbol { RefKind: RefKind.None, Type: IArrayTypeSymbol { Rank: 1 } }
                    or IParameterSymbol { RefKind: RefKind.None, Type: IArrayTypeSymbol { Rank: 1 } })
                && this.HasOneValueIn(name: array, code: code))
            {
                return name;
            }
        }

        return null;
    }

    // What a view is made from. A local of a view gives its initializer. AsSpan, AsMemory, Slice, a
    // range, a constructor of a view, and the Span of a memory give what they get. Null for each
    // other expression.
    private ExpressionSyntax? OneViewBack(ExpressionSyntax made, SyntaxNode code) =>
        Unwrapped(made) switch
        {
            IdentifierNameSyntax name =>
                this.region.SymbolOf(name) is ILocalSymbol { RefKind: RefKind.None } local
                && IsAView(local.Type)
                && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is VariableDeclaratorSyntax { Initializer.Value: { } initial }
                && this.HasOneValueIn(name: local, code: code)
                    ? initial
                    : null,
            InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "AsSpan" or "AsMemory" or "Slice", Expression: var of
                    }
                } call
                when this.region.SymbolOf(call) is IMethodSymbol method && SodaFlowSymbols.IsPlatform(method) => of,
            BaseObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: > 0 } arguments } creation
                when IsAView(this.region.TypeOf(creation)) => arguments[0].Expression,
            // A range of an array is a new array, and a range of a view is a view.
            ElementAccessExpressionSyntax range
                when IsARange(range.ArgumentList.Arguments) && IsAView(this.region.TypeOf(range.Expression)) =>
                range.Expression,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Span", Expression: var memory }
                when IsAView(this.region.TypeOf(memory)) => memory,
            _ => null
        };

    private static bool IsARange(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        arguments.Count == 1 && arguments.First() is { Expression: RangeExpressionSyntax };

    // True for a local or a parameter of a function that gets no value in it, other than at its
    // declaration.
    private bool HasOneValueIn(ISymbol name, SyntaxNode code) =>
        name.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is { } declaration
        && FunctionOf(declaration) == code
        && !this.UsesOf(name: name, code: code).Any(static one => one.Gives);
}
