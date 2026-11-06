using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Walks the code that runs while a loop is open and reports each read of a
///     pending value. A lambda given to a library operator runs after the close, and
///     the walk does not enter it. The walk enters all other lambdas.
/// </summary>
internal sealed class SinkWalker(in LoopRegion region, in LoopStateEvaluator evaluator) : CSharpSyntaxWalker
{
    private readonly LoopStateEvaluator evaluator = evaluator;
    private readonly LoopRegion region = region;

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        foreach (VariableDeclaratorSyntax declarator in node.Declaration.Variables)
        {
            if (declarator.Initializer == null)
            {
                continue;
            }

            this.Visit(declarator.Initializer.Value);

            if (model.GetDeclaredSymbol(declarator) is { } local)
            {
                this.evaluator.Declare(local, this.evaluator.Eval(declarator.Initializer.Value));
            }
        }
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        base.VisitAssignmentExpression(node);

        if (node.Left is IdentifierNameSyntax
            && this.region.ModelFor(node).GetSymbolInfo(node.Left).Symbol is ILocalSymbol local)
        {
            this.evaluator.Declare(local, this.evaluator.Eval(node.Right));
        }
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        SemanticModel model = this.region.ModelFor(node);

        if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
        {
            base.VisitInvocationExpression(node);

            return;
        }

        this.Visit(node.Expression);

        bool library = SodaFlowSymbols.IsSodaFlow(method);
        bool runsNow = LoopStateEvaluator.RunsNow(method);

        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            if (argument.Expression is AnonymousFunctionExpressionSyntax && library && !runsNow)
            {
                continue;
            }

            this.Visit(argument);
        }

        ExpressionSyntax? receiver = LoopStateEvaluator.ReceiverOf(invocation: node, method: method);

        // CanExecute is an ICommand member, which a bindable action implements. Thus, the read
        // members count on a library receiver too, and not only on a library method.
        bool libraryReceiver = receiver != null && SodaFlowSymbols.IsCarrierType(model.GetTypeInfo(receiver).Type);

        if (library || libraryReceiver)
        {
            if (SodaFlowSymbols.ReadMembers.Contains(LoopStateEvaluator.OperatorName(method))
                && receiver != null
                && this.evaluator.Eval(receiver).Pending)
            {
                this.region.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.ReadBeforeClose,
                        location: node.GetLocation(),
                        method.Name,
                        this.region.PlaceholderName));
            }

            if (library)
            {
                return;
            }
        }

        IMethodSymbol definition = method.ReducedFrom ?? method;

        if (method.ReducedFrom != null && receiver != null && definition.Parameters.Length > 0)
        {
            this.CheckEscape(callee: definition, parameter: definition.Parameters[0], argument: receiver);
        }

        this.CheckEscapes(callee: definition, arguments: node.ArgumentList.Arguments, offset: method.ReducedFrom != null ? 1 : 0);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        base.VisitObjectCreationExpression(node);
        this.CheckCreation(node, node.ArgumentList);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        base.VisitImplicitObjectCreationExpression(node);
        this.CheckCreation(node, node.ArgumentList);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        base.VisitMemberAccessExpression(node);

        if (this.region.ModelFor(node).GetSymbolInfo(node).Symbol is IPropertySymbol property
            && SodaFlowSymbols.IsSodaFlow(property)
            && SodaFlowSymbols.ReadMembers.Contains(property.Name)
            && this.evaluator.Eval(node.Expression).Pending)
        {
            this.region.Report(
                Diagnostic.Create(
                    descriptor: Diagnostics.ReadBeforeClose,
                    location: node.GetLocation(),
                    property.Name,
                    this.region.PlaceholderName));
        }
    }

    private void CheckCreation(ExpressionSyntax node, ArgumentListSyntax? arguments)
    {
        if (arguments == null
            || this.region.ModelFor(node).GetSymbolInfo(node).Symbol is not IMethodSymbol constructor
            || SodaFlowSymbols.IsSodaFlow(constructor))
        {
            return;
        }

        this.CheckEscapes(callee: constructor, arguments: arguments.Arguments, offset: 0);
    }

    private void CheckEscapes(IMethodSymbol callee, SeparatedSyntaxList<ArgumentSyntax> arguments, int offset)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax argument = arguments[i];
            IParameterSymbol? parameter = ParameterOf(callee: callee, argument: argument, position: i + offset);

            if (parameter != null)
            {
                this.CheckEscape(callee: callee, parameter: parameter, argument: argument.Expression);
            }
        }
    }

    /// <summary>
    ///     A pending value given to code that is not in the library. The body of the
    ///     callee answers the question when it is in this compilation. An
    ///     <c>AsCell()</c> at the argument is the assertion of the caller and reports
    ///     nothing.
    /// </summary>
    private void CheckEscape(IMethodSymbol callee, IParameterSymbol parameter, ExpressionSyntax argument)
    {
        if (!this.evaluator.Eval(argument).Pending)
        {
            return;
        }

        if (IsAsserted(argument))
        {
            return;
        }

        if (this.SamplesParameter(callee: callee, parameter: parameter))
        {
            this.region.Report(
                Diagnostic.Create(
                    descriptor: Diagnostics.ReadInCallee,
                    location: argument.GetLocation(),
                    callee.MethodKind == MethodKind.Constructor ? callee.ContainingType.Name : callee.Name,
                    parameter.Name,
                    this.region.PlaceholderName));

            return;
        }

        this.region.Report(
            Diagnostic.Create(
                descriptor: Diagnostics.Escape,
                location: argument.GetLocation(),
                this.region.PlaceholderName,
                callee.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    ///     An <c>AsCell()</c> or <c>AsBehavior()</c> on the argument, or on a value it
    ///     is derived from, is the assertion that nothing reads it before the close.
    /// </summary>
    private static bool IsAsserted(ExpressionSyntax argument)
    {
        ExpressionSyntax? current = argument;

        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
        {
            if (access.Name.Identifier.ValueText is "AsCell" or "AsBehavior")
            {
                return true;
            }

            current = access.Expression;
        }

        return false;
    }

    private bool SamplesParameter(IMethodSymbol callee, IParameterSymbol parameter)
    {
        SyntaxNode? declaration = callee.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        if (declaration == null)
        {
            return false;
        }

        // A primary constructor declares on its type. Then, the scan covers the type: the
        // initializers of its members and the arguments to its base.
        SemanticModel model = this.region.ModelFor(declaration);

        IEnumerable<InvocationExpressionSyntax> reads =
            declaration.DescendantNodes(descendIntoChildren: static n => n is not AnonymousFunctionExpressionSyntax)
                .OfType<InvocationExpressionSyntax>()
                .Where(static i => i.Expression is MemberAccessExpressionSyntax access
                    && SodaFlowSymbols.ReadMembers.Contains(access.Name.Identifier.ValueText));

        foreach (InvocationExpressionSyntax read in reads)
        {
            ExpressionSyntax target = ((MemberAccessExpressionSyntax)read.Expression).Expression;

            if (model.GetSymbolInfo(read).Symbol is IMethodSymbol readMethod
                && SodaFlowSymbols.IsSodaFlow(readMethod)
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target).Symbol, parameter))
            {
                return true;
            }
        }

        return false;
    }

    private static IParameterSymbol? ParameterOf(IMethodSymbol callee, ArgumentSyntax argument, int position)
    {
        if (argument.NameColon != null)
        {
            string name = argument.NameColon.Name.Identifier.ValueText;

            return callee.Parameters.FirstOrDefault(p => p.Name == name);
        }

        if (position < callee.Parameters.Length)
        {
            return callee.Parameters[position];
        }

        return callee.Parameters.Length > 0 && callee.Parameters[callee.Parameters.Length - 1].IsParams
            ? callee.Parameters[callee.Parameters.Length - 1]
            : null;
    }
}
