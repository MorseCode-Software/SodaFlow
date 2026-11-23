using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     A function that a delegate can be, with what a call of it needs.
/// </summary>
/// <param name="declaration">
///     The lambda, the anonymous method, the local function,
///     or the method.
/// </param>
/// <param name="definer">
///     For a lambda or a local function, the evaluator of the code that
///     contains the function. The function can read the locals of that code.
///     Null for a method.
/// </param>
/// <param name="receiver">
///     The state of the object, for a method of a different object. It has
///     the members of the object when the evaluator followed its construction.
/// </param>
/// <param name="ofThis">
///     For a method of this object that the code names as a method group, the
///     evaluator of that code. The method reads the members of this through
///     the record of that evaluator, as a call of the method on this does.
///     Null for each other function.
/// </param>
/// <param name="owner">
///     For a method group whose owner is not this, the expression of the
///     owner and the evaluator of the code that names the group. Null for each
///     other function.
/// </param>
/// <param name="extended">
///     For a method group of an extension method, the expression of the
///     receiver. The first parameter of the method gets it, with the state of
///     <paramref name="receiver" />. Null for each other function.
/// </param>
internal sealed class FunctionValue(
    in SyntaxNode declaration,
    in LoopStateEvaluator? definer,
    in LoopState? receiver,
    in LoopStateEvaluator? ofThis,
    in (LoopStateEvaluator Namer, ExpressionSyntax Written)? owner,
    in ExpressionSyntax? extended = null)
{
    public SyntaxNode Declaration { get; } = declaration;

    public LoopStateEvaluator? Definer { get; } = definer;

    public LoopState? Receiver { get; } = receiver;

    public LoopStateEvaluator? OfThis { get; } = ofThis;

    public (LoopStateEvaluator Namer, ExpressionSyntax Written)? Owner { get; } = owner;

    public ExpressionSyntax? Extended { get; } = extended;
}
