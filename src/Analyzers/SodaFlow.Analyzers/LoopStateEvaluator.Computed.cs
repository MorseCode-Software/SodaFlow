using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a function that calculates the value of a cell at the first read of the cell.
// The walk reads the function at that read, with the values that the function reads then.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of the result of an operator, with the functions that
    ///     calculate its value at the first read.
    /// </summary>
    /// <remarks>
    ///     A read of the result also reads each input that gives the result its
    ///     pending value. Thus, that read runs the functions of those inputs. The
    ///     result gets its own functions only where they can read a value that
    ///     the code can change after the call. Each other function reads at the
    ///     first read what it reads at the call, which the call checks.
    /// </remarks>
    private LoopState WithTheComputedValue(
        LoopState state,
        OperatorRule rule,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ITypeSymbol? receiverType,
        LoopState receiverState,
        LoopState firstArgument,
        LoopState inputs)
    {
        state = rule switch
        {
            OperatorRule.MapValue or OperatorRule.SampleLazy => state.Computing(receiverState.Computed),
            OperatorRule.LiftValue or OperatorRule.Combine or OperatorRule.SameValues =>
                state.Computing(inputs.Computed),
            OperatorRule.HoldLazy => state.Computing(firstArgument.Computed),
            // A read of the switch reads the cell that holds the selected cell, and then that cell.
            OperatorRule.SwitchValue =>
                state.Computing(receiverState.Computed).Computing(receiverState.Content.Computed),
            _ => state
        };

        if (!OperatorTable.ComputesValueWithFunction(
                symbols: this.region.Symbols,
                method: method,
                receiverType: receiverType))
        {
            return state;
        }

        LoopState given = inputs.Content;
        ImmutableArray<(ExpressionSyntax Written, LoopState Functions)>.Builder? functions = null;
        bool manyTimes = false;
        bool writes = false;
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            ExpressionSyntax written = arguments[i].Expression;
            IParameterSymbol? parameter = ParameterFor(method: method, argument: arguments[i], position: i);

            // A lambda that reads only its parameters reads at the first read what it reads at the
            // call. A lambda with no call can write a name also. A name can be a method of this
            // compilation. Such a method can read and write each field.
            if (!OperatorTable.ComputesValue(method: method, parameter: parameter)
                || (written is AnonymousFunctionExpressionSyntax lambda
                    && !((this.CanRead(lambda.Body) || Writes(lambda.Body))
                         && this.CanReadALaterValue(lambda: lambda, given: given))))
            {
                continue;
            }

            LoopState function = this.Eval(written);

            // A cell of functions gives the operator the function that it holds.
            if (function.Functions.IsEmpty && this.region.Symbols.IsCellOrBehaviorType(parameter?.Type))
            {
                function = function.Content;
            }

            if (function.Functions.IsEmpty)
            {
                continue;
            }

            functions ??= ImmutableArray.CreateBuilder<(ExpressionSyntax Written, LoopState Functions)>();
            functions.Add((written, function));
            writes |= written is AnonymousFunctionExpressionSyntax { Body: var body } && Writes(body);

            // The function of a cell runs one time at the first read. A view runs its functions for
            // each item.
            manyTimes |= parameter?.Name is not ("f" or "bf" or "cf");
        }

        return functions == null
            ? state
            : state.Computing(
                ImmutableArray.Create(
                    new ComputedValue(
                        build: invocation,
                        builder: this,
                        functions: functions.ToImmutable(),
                        given: given,
                        manyTimes: manyTimes,
                        writes: writes,
                        manyCells: this.CanBuildTwoOrMoreCells(invocation))));
    }

    // True for code with an assignment. An increment gives a number, which has no state.
    private static bool Writes(SyntaxNode body) =>
        body.DescendantNodesAndSelf().Any(static node => node is AssignmentExpressionSyntax);

    /// <summary>
    ///     True when a lambda can read a value that the code can change after
    ///     it makes the lambda. That is a name from the code around the lambda,
    ///     a member of an object, a static member, or a function of this
    ///     compilation. The check of the names comes first. Only the first name
    ///     of a member access needs the compiler, because it can be a type. Then,
    ///     the member after the type needs it also. A name of a nested part
    ///     needs it too, because it can be a capture.
    /// </summary>
    private bool CanReadALaterValue(AnonymousFunctionExpressionSyntax lambda, LoopState given)
    {
        if (!given.Objects.IsEmpty || given.HoldsAnObject)
        {
            return true;
        }

        DeclaredNames? own = null;

        foreach (SyntaxNode node in lambda.Body.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case ThisExpressionSyntax or BaseExpressionSyntax:
                    return true;
                case SimpleNameSyntax name when IsAValue(name):
                    own ??= NamesDeclaredIn(lambda);

                    if (!this.IsOwn(name: name, lambda: lambda, own: own)
                        && (name.Parent is not MemberAccessExpressionSyntax access
                            || access.Expression != name
                            || this.region.SymbolOf(name) is not (ITypeSymbol or INamespaceSymbol)
                            || this.CanHoldALaterValue(access)))
                    {
                        return true;
                    }

                    break;
            }
        }

        // A member of this compilation can read a static member, also when the lambda reaches it through
        // its parameter or with no name. This check needs the compiler, thus it comes last.
        return lambda.Body.DescendantNodesAndSelf().Any(this.RunsCodeOfThisCompilation);

        static bool IsAValue(ExpressionSyntax name) =>
            name.Parent switch
            {
                MemberAccessExpressionSyntax access => access.Name != name,
                MemberBindingExpressionSyntax or QualifiedNameSyntax or NameColonSyntax or NameEqualsSyntax => false,
                _ => !SyntaxFacts.IsInTypeOnlyContext(name)
            };
    }

    /// <summary>
    ///     True when a node of a lambda runs a member of this compilation. That
    ///     is a method, a getter, an indexer, an operator, a conversion, or a
    ///     constructor. The check of the syntax comes first, and then the
    ///     compiler gives the member.
    /// </summary>
    private bool RunsCodeOfThisCompilation(SyntaxNode node)
    {
        DeclaredCode declared = this.region.Symbols.DeclaredCode;

        ISymbol? member =
            node switch
            {
                MemberAccessExpressionSyntax { Name: var name } access when Calls(access) || declared.CanRunCode(name)
                    => this.MemberOf(access),
                MemberBindingExpressionSyntax { Name: var name } binding
                    when Calls(binding) || declared.CanRunCode(name) => this.MemberOf(binding),
                ElementAccessExpressionSyntax or ElementBindingExpressionSyntax
                    when this.region.Symbols.DeclaresIndexers => this.region.SymbolOf(node),
                BinaryExpressionSyntax
                    or PrefixUnaryExpressionSyntax
                    or PostfixUnaryExpressionSyntax
                    or AssignmentExpressionSyntax
                    or CastExpressionSyntax
                    or BaseObjectCreationExpressionSyntax when declared.CanRunCode(node) => this.region.SymbolOf(node),
                // No syntax shows an implicit conversion. This check reads it on a name only, as DeclaredCode does.
                IdentifierNameSyntax name when declared.CanRunCode(name) =>
                    this.region.ModelFor(name).GetConversion(name).MethodSymbol,
                _ => null
            };

        return member is IMethodSymbol or IPropertySymbol
               && SymbolEqualityComparer.Default.Equals(x: member.ContainingAssembly, y: this.region.Assembly);

        static bool Calls(SyntaxNode used) =>
            used.Parent is InvocationExpressionSyntax call && call.Expression == used;
    }

    // True when a name in a lambda names a parameter or a local of the lambda. A name of a nested part can
    // also be a capture, thus the compiler gives the symbol of such a name.
    private bool IsOwn(SimpleNameSyntax name, SyntaxNode lambda, DeclaredNames own)
    {
        string text = name.Identifier.ValueText;

        return own.Everywhere.Contains(text)
               || (own.InAPart.Contains(text)
                   && (this.region.SymbolOf(name) is not { } symbol
                       || symbol.DeclaringSyntaxReferences.Any(reference =>
                           reference.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(reference.Span))));
    }

    /// <summary>
    ///     True when a member access with a type or a namespace at its head names
    ///     a static member that can change after the build. A method or a getter
    ///     of this compilation can read each field. A field or a property can
    ///     hold a state when its type can. The access can also run a static
    ///     constructor of its type. A call such as <c>Math.Max</c> does none of
    ///     these.
    /// </summary>
    private bool CanHoldALaterValue(ExpressionSyntax access)
    {
        // A namespace or a nested type in the access names the type of the member after it.
        ExpressionSyntax used = access;
        ISymbol? member = this.MemberOf(used);

        while (member is INamespaceOrTypeSymbol
               && used.Parent is MemberAccessExpressionSyntax next
               && next.Expression == used)
        {
            used = next;
            member = this.MemberOf(used);
        }

        return member switch
            {
                IMethodSymbol { IsStatic: true } method => !method.DeclaringSyntaxReferences.IsEmpty,
                IFieldSymbol { IsStatic: true, IsConst: false } field => this.CanHoldState(field.Type),
                IPropertySymbol { IsStatic: true } property =>
                    GetterOf(property) != null || this.CanHoldState(property.Type),
                _ => false
            }
            || (member is { IsStatic: true, ContainingType: { DeclaringSyntaxReferences.IsEmpty: false } owner }
                && owner.OriginalDefinition.StaticConstructors.Any(static constructor =>
                    !constructor.IsImplicitlyDeclared));
    }

    // The member that a part of a member access names. The compiler keeps the symbol of a call, and not the symbol
    // of the name of its method. A call of a delegate gets the member that holds the delegate.
    private ISymbol? MemberOf(SyntaxNode used) =>
        used.Parent is InvocationExpressionSyntax call
        && call.Expression == used
        && this.region.SymbolOf(call) is IMethodSymbol { MethodKind: not MethodKind.DelegateInvoke } called
            ? called
            : this.region.SymbolOf(used);

    private bool CanHoldState(ITypeSymbol type) =>
        this.region.Symbols.CanHoldState(type: type, compilation: this.region.Assembly);

    // The names of the parameters and the locals that a lambda declares, with those of each function in it. A
    // parameter, or a local of the top block, hides a capture with its name in all of the lambda. Each other name
    // hides a capture only in a part of the lambda.
    private static DeclaredNames NamesDeclaredIn(AnonymousFunctionExpressionSyntax lambda)
    {
        HashSet<string> everywhere =
            [.. ParametersOf(lambda).Select(static parameter => parameter.Identifier.ValueText)];

        HashSet<string> names = [];

        foreach (StatementSyntax statement in lambda.Block?.Statements ?? default)
        {
            switch (statement)
            {
                case LocalDeclarationStatementSyntax local:
                    everywhere.UnionWith(local.Declaration.Variables.Select(static each => each.Identifier.ValueText));

                    break;
                case LocalFunctionStatementSyntax function:
                    everywhere.Add(function.Identifier.ValueText);

                    break;
            }
        }

        foreach (SyntaxNode node in lambda.Body.DescendantNodesAndSelf())
        {
            SyntaxToken declared =
                node switch
                {
                    ParameterSyntax parameter => parameter.Identifier,
                    VariableDeclaratorSyntax variable => variable.Identifier,
                    SingleVariableDesignationSyntax designation => designation.Identifier,
                    ForEachStatementSyntax loop => loop.Identifier,
                    CatchDeclarationSyntax caught => caught.Identifier,
                    LocalFunctionStatementSyntax local => local.Identifier,
                    QueryClauseSyntax or QueryContinuationSyntax => IdentifierOf(node),
                    _ => default
                };

            if (declared.IsKind(SyntaxKind.IdentifierToken) && !everywhere.Contains(declared.ValueText))
            {
                names.Add(declared.ValueText);
            }
        }

        return new DeclaredNames(everywhere: everywhere, inAPart: names);

        static SyntaxToken IdentifierOf(SyntaxNode clause) =>
            clause switch
            {
                FromClauseSyntax from => from.Identifier,
                LetClauseSyntax let => let.Identifier,
                JoinClauseSyntax join => join.Identifier,
                QueryContinuationSyntax into => into.Identifier,
                _ => default
            };
    }

    /// <summary>
    ///     The state of a cell at a read, with what the functions that the read
    ///     runs read. The read runs the functions of each cell in the state that
    ///     no read before ran, with the values that they read now. The walk
    ///     applies each write of those functions here.
    /// </summary>
    /// <remarks>
    ///     The state keeps the answer of the call that made the cell, and the
    ///     result joins the two. A read that the code can skip, or that a quiet
    ///     evaluator reads, leaves each cell for the next read. A value that
    ///     came into this code through a parameter or a member gives the read
    ///     its origin. Thus, the caller reports the read at its call.
    /// </remarks>
    /// <param name="read">The read, which is where the walk reports what the functions read.</param>
    /// <param name="state">The state of the cell.</param>
    /// <param name="marks">
    ///     What records the run of a cell, with the name of the read. The
    ///     position of the read tells the walk how. Null when the walk cannot
    ///     know that the read runs.
    /// </param>
    /// <param name="heldIn">
    ///     The expression of the cell at the read, for a cell in a member. Null
    ///     for a read with no such expression.
    /// </param>
    /// <param name="ofACaller">
    ///     What gets a read of a function whose code is in a caller of this code,
    ///     such as a lambda that the caller gave. That read is a finding of the
    ///     caller. Null where no frame of a caller can have the function.
    /// </param>
    public LoopState AtTheFirstRead(
        SyntaxNode read,
        LoopState state,
        Action<ComputedValue, ReadKey?>? marks,
        ExpressionSyntax? heldIn = null,
        Action<Finding>? ofACaller = null)
    {
        List<Finding> found = [];
        List<Finding>? ofCallers = null;

        // A build that makes two or more cells has a record for each name of a read.
        ReadKey? key = state.Computed.Any(static cell => cell.ManyCells) ? this.ReadKeyOf(heldIn) : null;

        foreach (ComputedValue cell in state.Computed.Where(cell => !cell.HasRunAt(key)))
        {
            // The mark comes before the walk of the functions. Thus, a function that reads its own
            // cell does not run again.
            if (!this.quiet)
            {
                marks?.Invoke(cell, key);
            }

            // An object of the store can get a pending member through a second name, which the
            // names of the code do not show. A write can put a plain value over a pending one.
            if (this.region.Objects.IsEmpty
                && !cell.Writes
                && !cell.Builder.CanUseState(invocation: cell.Build, given: cell.Given))
            {
                continue;
            }

            int from = found.Count;

            foreach ((ExpressionSyntax written, LoopState functions) in cell.Functions)
            {
                foreach (FunctionValue function in functions.Functions)
                {
                    found.AddRange(
                        this.FollowFunction(
                                call: read,
                                name: NameOf(written),
                                function: function,
                                target: functions,
                                arguments: default,
                                each: (written, cell.Given, cell.ManyTimes),
                                heldIn: heldIn)
                            ?.Findings
                            .Where(static finding => !finding.Escape)
                        ?? []);
                }
            }

            // The call that made this frame drops a finding of this frame with no origin. Thus, a finding of
            // a caller goes back to that caller as it is. A cell from the frame of that finding came here
            // through an argument, and the caller reports the read at that argument.
            if (ofACaller != null)
            {
                for (int i = from; i < found.Count; i++)
                {
                    if (found[i].Home != this.Frame
                        && found[i].Home != cell.Builder.Frame
                        && this.Frame.IsWithin(found[i].Home))
                    {
                        (ofCallers ??= []).Add(found[i]);
                    }
                }
            }
        }

        if (found.Count == 0)
        {
            return state;
        }

        foreach (Finding finding in ofCallers ?? [])
        {
            ofACaller?.Invoke(finding);
        }

        ISymbol? origin = found.FirstOrDefault(finding => finding.Home == this.Frame && finding.Origin != null)?.Origin;

        // A function that reads a parameter of this code makes the read a read of that parameter, also
        // for a cell in a member. Thus, the caller reports the read at the argument.
        return origin is IParameterSymbol parameter
            ? state.WithPending(true).From(parameter)
            : state.WithPending(true).FromIfUnknown(origin);
    }

    private sealed class DeclaredNames(in HashSet<string> everywhere, in HashSet<string> inAPart)
    {
        public HashSet<string> Everywhere { get; } = everywhere;

        public HashSet<string> InAPart { get; } = inAPart;
    }
}
