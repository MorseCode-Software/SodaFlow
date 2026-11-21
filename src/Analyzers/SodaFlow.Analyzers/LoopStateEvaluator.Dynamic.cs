using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for an operation on a value of the type dynamic. The binder finds the member at run time,
// from the type of the object. Where the walk saw the object made, the evaluator reads the member of that type that
// the operation names. Otherwise, it reads nothing.
internal sealed partial class LoopStateEvaluator
{
    // The operator that a binary or a unary operation on a dynamic value calls. Each operand of an
    // object that the walk saw made gives the operators of its type. The evaluator reads one only
    // when the types have one operator with that name and number of operands.
    private Followed? FollowDynamicOperator(CSharpSyntaxNode operation, IReadOnlyCollection<ExpressionSyntax> operands)
    {
        if (OperatorName(operation.Kind()) is not { } name)
        {
            return null;
        }

        LoopState[] states = [.. operands.Select(this.Self)];

        IMethodSymbol[] found =
        [
            .. states
                .SelectMany(state => state.Members?.OperatorsOf(name: name, parameters: operands.Count) ?? [])
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
        ];

        return found.Length == 1
            ? this.FollowImplied(
                call: operation,
                member: found[0],
                owner: null,
                arguments:
                [
                    .. operands.Zip(second: states, resultSelector: static (operand, state) => (operand, state))
                ])
            : null;
    }

    // The conversion operator that the binder calls where the code converts a dynamic value. The
    // binder classifies the conversion from the type of the object as the compiler does. Thus, a
    // standard conversion can come before or after the operator. The walk classifies it as for a
    // cast. Where the code has no cast, a conversion that only a cast permits fails to bind.
    private Followed? FollowDynamicConversion(SyntaxNode call, ExpressionSyntax expression, ITypeSymbol to)
    {
        LoopState state = this.Self(expression);

        IMethodSymbol[] found =
        [
            .. state.Members?.Types
                    .Select(type => this.region.ConversionOperator(from: type, to: to))
                    .OfType<IMethodSymbol>()
                    .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                ?? []
        ];

        return found.Length == 1
            ? this.FollowImplied(call: call, member: found[0], owner: null, arguments: [(expression, state)])
            : null;
    }

    /// <summary>
    ///     The method that the binder picks for a dynamic call, of the types of
    ///     an object that the walk saw made. The result also has the arguments
    ///     in the order of the parameters. Each argument must name a parameter
    ///     or be at the position of one. Each parameter with no argument must be
    ///     optional, and has null in the order. The type of an argument that the
    ///     code knows must have an implicit conversion to the type of its
    ///     parameter. Null when no method or more than one method agrees with
    ///     the arguments.
    /// </summary>
    /// <param name="members">The members of the object.</param>
    /// <param name="name">The name of the method.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public (IMethodSymbol Method, IReadOnlyList<ArgumentSyntax?> InOrder)? DynamicMethodOf(
        Construction members,
        string name,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        (IMethodSymbol Method, IReadOnlyList<ArgumentSyntax?> InOrder)[] found =
        [
            .. members.Types
                .SelectMany(type => type.GetMembers(name))
                .OfType<IMethodSymbol>()
                .Where(static method => method is { IsStatic: false, MethodKind: MethodKind.Ordinary })
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .SelectMany<IMethodSymbol, (IMethodSymbol Method, IReadOnlyList<ArgumentSyntax?> InOrder)>(method =>
                    this.InTheOrderOfTheParameters(method: method, arguments: arguments) is { } inOrder
                        ? [(method, inOrder)]
                        : [])
        ];

        return found.Length == 1 ? found[0] : null;
    }

    private ArgumentSyntax?[]? InTheOrderOfTheParameters(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        ArgumentSyntax?[] placed = new ArgumentSyntax?[parameters.Length];

        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax argument = arguments[i];

            int at = argument.NameColon is { } named ? IndexOf(parameters: parameters, name: named.Name) : i;

            if (at < 0
                || at >= parameters.Length
                || (this.region.TypeOf(argument.Expression) is { } type
                    && !this.region.Converts(from: type, to: parameters[at].Type)))
            {
                return null;
            }

            placed[at] = argument;
        }

        return parameters.Where((parameter, position) => placed[position] == null && !parameter.IsOptional).Any()
            ? null
            : placed;
    }

    private static int IndexOf(ImmutableArray<IParameterSymbol> parameters, SimpleNameSyntax name)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == name.Identifier.ValueText)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     The calls that an await of a dynamic value makes. The binder finds
    ///     <c>GetAwaiter</c> and <c>IsCompleted</c> by name on the type of each
    ///     object.
    /// </summary>
    /// <remarks>
    ///     The await calls <c>GetResult</c> in the call only when the task is
    ///     complete, and the walk knows no complete task of a dynamic value.
    /// </remarks>
    /// <param name="awaited">The await.</param>
    private Followed? FollowDynamicAwaiter(AwaitExpressionSyntax awaited)
    {
        LoopState value = this.Self(awaited.Expression);

        if (OneMethodNamed(owner: value, name: "GetAwaiter") is not { } start)
        {
            return null;
        }

        Followed? awaiter = this.FollowImplied(call: awaited, member: start, owner: value, arguments: []);
        LoopState state = awaiter?.State ?? LoopState.None;

        return Both(
            first: awaiter,
            second: GetterNamed(owner: state, name: "IsCompleted") is { } test
                ? this.FollowImplied(call: awaited, member: test, owner: state, arguments: [])
                : null);
    }

    /// <summary>
    ///     The one instance method with the name and no parameter of the types
    ///     of an object that the walk saw made. The binder finds a member of a
    ///     dynamic value by name. Null when the types have no such method or
    ///     more than one.
    /// </summary>
    private static IMethodSymbol? OneMethodNamed(LoopState owner, string name) =>
        owner.Members?.MethodsOf(name: name, parameters: 0).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .ToArray()
            is { Length: 1 } found
            ? found[0]
            : null;

    /// <summary>
    ///     The getter of the property with the name, for an object that the walk
    ///     saw made. Null when the walk knows no such property.
    /// </summary>
    private static IMethodSymbol? GetterNamed(LoopState owner, string name) =>
        owner.Members?.PropertiesNamed(name).FirstOrDefault()?.GetMethod;

    // The operator true or operator false that the binder calls where a dynamic value is a condition.
    private Followed? FollowDynamicTruth(ExpressionSyntax condition, string name)
    {
        LoopState state = this.Self(condition);

        return state.Members?.OperatorsOf(name: name, parameters: 1).FirstOrDefault() is { } truth
            ? this.FollowImplied(call: condition, member: truth, owner: null, arguments: [(condition, state)])
            : null;
    }

    /// <summary>
    ///     The setter that an assignment to a member or an element of a dynamic
    ///     value calls. It is the setter of the property with that name, or of
    ///     the indexer with that number of indexes.
    /// </summary>
    /// <param name="assignment">The assignment.</param>
    public Followed? FollowDynamicWrite(AssignmentExpressionSyntax assignment)
    {
        // A += or a -= of a handler to an event calls the add or the remove accessor of the event.
        if (assignment is
            {
                RawKind: (int)SyntaxKind.AddAssignmentExpression or (int)SyntaxKind.SubtractAssignmentExpression,
                Left: MemberAccessExpressionSyntax target
            }
            && this.Self(target.Expression) is { Members: { } owners } eventOwner
            && EventsNamed(members: owners, name: target.Name.Identifier.ValueText) is { Length: 1 } named
            && (assignment.IsKind(SyntaxKind.AddAssignmentExpression) ? named[0].AddMethod : named[0].RemoveMethod)
                is { } accessor)
        {
            return this.FollowImplied(
                call: assignment,
                member: accessor,
                owner: eventOwner,
                arguments: [(assignment.Right, this.Eval(assignment.Right))]);
        }

        (ExpressionSyntax? owner, IPropertySymbol? property, IEnumerable<ArgumentSyntax> indexes) =
            assignment.Left switch
            {
                MemberAccessExpressionSyntax access when this.Self(access.Expression).Members is { } members =>
                    (access.Expression, members.PropertiesNamed(access.Name.Identifier.ValueText).FirstOrDefault(),
                        []),
                ElementAccessExpressionSyntax element => (element.Expression,
                    this.DynamicIndexerOf(owner: element.Expression, indexes: element.ArgumentList.Arguments.Count),
                    element.ArgumentList.Arguments),
                _ => (null, null, [])
            };

        return owner != null && property?.SetMethod is { } setter
            ? this.FollowImplied(
                call: assignment,
                member: setter,
                owner: this.Self(owner),
                arguments:
                [
                    .. indexes.Select(index => (index.Expression, this.Eval(index.Expression))),
                    (assignment.Right, this.Eval(assignment.Right))
                ])
            : null;
    }

    // The event with the name of each type of an object, from the type itself or its nearest base type
    // that declares one.
    private static IEventSymbol[] EventsNamed(Construction members, string name) =>
    [
        .. members.Types
            .Select(type =>
            {
                for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
                {
                    if (current.GetMembers(name).OfType<IEventSymbol>().FirstOrDefault() is { } found)
                    {
                        return found;
                    }
                }

                return null;
            })
            .OfType<IEventSymbol>()
            .Distinct<IEventSymbol>(SymbolEqualityComparer.Default)
    ];

    // The indexer with the number of indexes that the type of the object has. Null when the walk did
    // not see the object made.
    private IPropertySymbol? DynamicIndexerOf(ExpressionSyntax owner, int indexes) =>
        this.Self(owner)
            .Members?.PropertiesNamed("this[]")
            .FirstOrDefault(indexer => indexer.Parameters.Length == indexes);

    // The metadata name of the operator that an operation of the language calls.
    private static string? OperatorName(SyntaxKind operation) =>
        operation switch
        {
            SyntaxKind.AddExpression => "op_Addition",
            SyntaxKind.SubtractExpression => "op_Subtraction",
            SyntaxKind.MultiplyExpression => "op_Multiply",
            SyntaxKind.DivideExpression => "op_Division",
            SyntaxKind.ModuloExpression => "op_Modulus",
            SyntaxKind.BitwiseAndExpression => "op_BitwiseAnd",
            SyntaxKind.BitwiseOrExpression => "op_BitwiseOr",
            SyntaxKind.ExclusiveOrExpression => "op_ExclusiveOr",
            SyntaxKind.LeftShiftExpression => "op_LeftShift",
            SyntaxKind.RightShiftExpression => "op_RightShift",
            SyntaxKind.EqualsExpression => "op_Equality",
            SyntaxKind.NotEqualsExpression => "op_Inequality",
            SyntaxKind.LessThanExpression => "op_LessThan",
            SyntaxKind.LessThanOrEqualExpression => "op_LessThanOrEqual",
            SyntaxKind.GreaterThanExpression => "op_GreaterThan",
            SyntaxKind.GreaterThanOrEqualExpression => "op_GreaterThanOrEqual",
            SyntaxKind.UnaryMinusExpression => "op_UnaryNegation",
            SyntaxKind.UnaryPlusExpression => "op_UnaryPlus",
            SyntaxKind.LogicalNotExpression => "op_LogicalNot",
            SyntaxKind.BitwiseNotExpression => "op_OnesComplement",
            SyntaxKind.PreIncrementExpression or SyntaxKind.PostIncrementExpression => "op_Increment",
            SyntaxKind.PreDecrementExpression or SyntaxKind.PostDecrementExpression => "op_Decrement",
            SyntaxKind.AddAssignmentExpression => "op_Addition",
            SyntaxKind.SubtractAssignmentExpression => "op_Subtraction",
            SyntaxKind.MultiplyAssignmentExpression => "op_Multiply",
            SyntaxKind.DivideAssignmentExpression => "op_Division",
            SyntaxKind.ModuloAssignmentExpression => "op_Modulus",
            SyntaxKind.AndAssignmentExpression => "op_BitwiseAnd",
            SyntaxKind.OrAssignmentExpression => "op_BitwiseOr",
            SyntaxKind.ExclusiveOrAssignmentExpression => "op_ExclusiveOr",
            SyntaxKind.LeftShiftAssignmentExpression => "op_LeftShift",
            SyntaxKind.RightShiftAssignmentExpression => "op_RightShift",
            _ => null
        };
}
