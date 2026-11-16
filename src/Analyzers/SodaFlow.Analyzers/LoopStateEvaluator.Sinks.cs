using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state that a read in the code of the block gives for a hold that
    ///     the code of the block made. That state is the initial value of the
    ///     hold. Null for each other read.
    /// </summary>
    /// <remarks>
    ///     A hold gets the value of a firing at the end of the transaction. The
    ///     block and each send in it run in one transaction. Thus, a read in the
    ///     block gives the initial value, also after a send into the sink of the
    ///     hold. An accumulation and a cell sink are holds also. A read in a
    ///     function can run after the transaction, thus it gets each value.
    /// </remarks>
    /// <param name="read">The call that reads.</param>
    /// <param name="receiver">The cell that the call reads.</param>
    private LoopState? InitialOfAHoldOfTheBlock(SyntaxNode read, ExpressionSyntax? receiver)
    {
        if (receiver == null || this.quiet)
        {
            return null;
        }

        ExpressionSyntax cell = Unwrapped(receiver);
        (ILocalSymbol Local, VariableDeclaratorSyntax Declarator)? named = null;

        // A local that no code writes has the hold of its initializer.
        if (cell is IdentifierNameSyntax name
            && this.region.SymbolOf(name) is ILocalSymbol { DeclaringSyntaxReferences.Length: 1 } local
            && local.DeclaringSyntaxReferences[0].GetSyntax()
                is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator)
        {
            cell = Unwrapped(value);
            named = (local, declarator);
        }

        if (this.InitialValueOf(cell) is not { } initial
            || !this.IsCodeOfTheBlock(read)
            || (named is { Local: var only, Declarator: var declaredAt }
                && (!this.IsCodeOfTheBlock(declaredAt)
                    || NamesOf(local: only, declarator: declaredAt, region: this.region)?.Any(IsWritten) != false)))
        {
            return null;
        }

        return this.Eval(initial);
    }

    // The initial value of the hold, of the accumulation, or of the cell sink that a call makes.
    // Null for each other expression.
    private ExpressionSyntax? InitialValueOf(ExpressionSyntax made)
    {
        if (made is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Hold" or "Accum" or "CreateSink" }
            } creation
            || this.region.SymbolOf(creation) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            return null;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        bool isAHold =
            method.Name == "CreateSink"
                ? this.region.Symbols.IsCellOrBehaviorType(method.ReturnType)
                : definition is { IsExtensionMethod: true, Parameters.Length: > 1 }
                  && this.region.Symbols.IsStreamType(definition.Parameters[0].Type);

        // The first parameter of an extension method is the stream.
        int ordinal = definition.IsExtensionMethod ? 1 : 0;

        return isAHold
            ? creation.ArgumentList.Arguments
                .Where((argument, position) =>
                    ParameterFor(method: method, argument: argument, position: position)?.Ordinal == ordinal)
                .Select(static argument => argument.Expression)
                .FirstOrDefault()
            : null;
    }

    /// <summary>
    ///     Reads a function that runs after the transaction of the block, as the
    ///     function of <c>Transaction.Post</c> does. A send in it gives its value
    ///     to the sink after the close. Thus, each value that the block made from
    ///     the sink gets that value also.
    /// </summary>
    /// <remarks>
    ///     An evaluator that changes nothing reads the function, and the walk
    ///     reports nothing from it. A read after the close does not throw.
    /// </remarks>
    /// <param name="function">The expression of the function.</param>
    public void RunsAfterTheTransaction(ExpressionSyntax function)
    {
        if (this.quiet)
        {
            return;
        }

        ImmutableArray<FunctionValue> functions = this.Eval(function).Functions;

        if (functions.IsEmpty)
        {
            return;
        }

        LoopStateEvaluator later = this.Quiet();
        SinkWalker walker = later.ReadBy(new SinkWalker(region: this.region, evaluator: later, sink: static _ => { }));

        this.region.ReadAfterTheTransaction(() =>
        {
            foreach (FunctionValue each in functions)
            {
                switch (each.Declaration)
                {
                    case AnonymousFunctionExpressionSyntax lambda:
                        walker.Visit(lambda.Body);

                        break;
                    case LocalFunctionStatementSyntax local:
                        walker.Visit((SyntaxNode?)local.Body ?? local.ExpressionBody);

                        break;
                }
            }
        });
    }

    // True when a node is in the code of the block, and not in a function or a query. Such
    // code runs in the transaction of the block.
    private bool IsCodeOfTheBlock(SyntaxNode node)
    {
        for (SyntaxNode? each = node; each != null; each = each.Parent)
        {
            if (each == this.region.Root)
            {
                return true;
            }

            if (each is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or QueryExpressionSyntax
                or MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }
}
