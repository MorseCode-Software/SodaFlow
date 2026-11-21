using Microsoft.CodeAnalysis;

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
///     The members of the object, for a method of an object whose construction
///     the evaluator followed.
/// </param>
/// <param name="ofThis">
///     For a method of this object that the code names as a method group, the
///     evaluator of that code. The method reads the members of this through
///     the record of that evaluator, as a call of the method on this does.
///     Null for each other function.
/// </param>
internal sealed class FunctionValue(
    in SyntaxNode declaration,
    in LoopStateEvaluator? definer,
    in Construction? receiver,
    in LoopStateEvaluator? ofThis)
{
    public SyntaxNode Declaration { get; } = declaration;

    public LoopStateEvaluator? Definer { get; } = definer;

    public Construction? Receiver { get; } = receiver;

    public LoopStateEvaluator? OfThis { get; } = ofThis;
}
