namespace SodaFlow.Analyzers;

// The part of the evaluator for a function that a send gives to a sink.
internal sealed partial class LoopStateEvaluator
{
    // The content of a hold after the close. A function that the stream fires is a value of the hold also. A send
    // keeps its function for a second read also: see LoopState.ForASecondRead.
    private static LoopState HeldAfterTheClose(LoopState fired) =>
        OnlyTheFunctions(state: fired, seed: fired.AfterTheClose);
}
