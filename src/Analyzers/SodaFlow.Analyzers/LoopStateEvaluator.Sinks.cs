using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state that a read in the transaction of the block gives for a hold
    ///     that the code of that transaction made. That state is the initial value
    ///     of the hold. Null for each other read.
    /// </summary>
    /// <remarks>
    ///     A hold gets the value of a firing at the end of the transaction. The
    ///     block and each send in it run in one transaction. Thus, a read in the
    ///     block gives the initial value, also after a send into the sink of the
    ///     hold. An accumulation and a cell sink are holds also. A function that
    ///     runs only in the transaction is a part of it. Each other function can
    ///     run after the transaction, thus a read in it gets each value.
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
            || !this.RunsInTheTransaction(read)
            || (named is { Local: var only, Declarator: var declaredAt }
                && (!this.RunsInTheTransaction(declaredAt)
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

    // True when a node runs only in the transaction of the block. That is the code of the block,
    // and a function in it that the library runs only in the call. The function of a map of a
    // constant cell is such a function also, because the transaction computes the first value of
    // the map. Each other function, a query, and a member can run after the transaction.
    private bool RunsInTheTransaction(SyntaxNode node)
    {
        for (SyntaxNode? each = node; each != null; each = each.Parent)
        {
            if (each == this.region.Root)
            {
                return true;
            }

            if ((each is AnonymousFunctionExpressionSyntax function && !this.RunsOnlyInTheCall(function))
                || each is LocalFunctionStatementSyntax or QueryExpressionSyntax or MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    // True when the library runs a function only in the call that gets it. A transaction and a
    // loop block run it one time. A map of a constant cell runs it for the first value, and has
    // no update that runs it again.
    private bool RunsOnlyInTheCall(AnonymousFunctionExpressionSyntax function)
    {
        if (function.Parent is not ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call }
            || this.region.SymbolOf(call) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            return false;
        }

        if (OperatorTable.RunsFunctionOneTime(method))
        {
            return true;
        }

        return method.Name == "Map"
               && ReceiverOf(symbols: this.region.Symbols, invocation: call, method: method) is { } receiver
               && this.region.Symbols.IsCellOrBehaviorType(this.region.TypeOf(receiver))
               && this.IsAConstant(receiver);
    }

    // True for a call of Constant of the library. The call can also be the initializer of a
    // readonly field, or of a local that no code writes.
    private bool IsAConstant(ExpressionSyntax cell)
    {
        ExpressionSyntax made = Unwrapped(cell);

        switch (this.region.SymbolOf(made))
        {
            case IFieldSymbol { IsReadOnly: true } field
                when this.region.InitializerOf(field.OriginalDefinition) is { } value:
                made = Unwrapped(value);

                break;
            case ILocalSymbol { DeclaringSyntaxReferences.Length: 1 } local
                when local.DeclaringSyntaxReferences[0].GetSyntax()
                         is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
                     && NamesOf(local: local, declarator: declarator, region: this.region)?.Any(IsWritten) == false:
                made = Unwrapped(value);

                break;
        }

        return made is InvocationExpressionSyntax
                   {
                       Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Constant" }
                   } constant
               && this.region.SymbolOf(constant) is IMethodSymbol method
               && this.region.Symbols.IsSodaFlow(method);
    }
}
