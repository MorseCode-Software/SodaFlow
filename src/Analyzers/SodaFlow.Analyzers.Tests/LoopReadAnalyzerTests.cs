using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Runs <see cref="LoopReadAnalyzer" /> over the shapes file and checks
///     the rule it reports for each shape. Then runs each shape, and checks that
///     the library does what the rules say.
/// </summary>
/// <remarks>
///     Each shape is one method of <c>Shapes.cs</c>, and the table here gives
///     the rules expected in it. A shape with no entry expects nothing. The table
///     is the record of what the analyzer sees and what it does not. The shapes
///     with no entry include a read in a callback and a read after the block. A
///     shape with only SF1003 has a value that goes where the analyzer cannot
///     read. That is a delegate of an unknown source, or a chain of calls
///     that is deeper than the depth.
/// </remarks>
internal sealed class LoopReadAnalyzerTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Expected =
        new Dictionary<string, string[]>
        {
            ["DirectSample"] = ["SF1001"],
            ["MapSample"] = ["SF1001"],
            ["LiftSample"] = ["SF1001"],
            ["CalmSample"] = ["SF1001"],
            ["SwitchCSample"] = ["SF1001"],
            ["HoldLazySample"] = ["SF1001"],
            ["ConstantLazySample"] = ["SF1001"],
            ["ConstructorSamples"] = ["SF1004"],
            ["ConstructorSamplesAsserted"] = ["SF1004"],
            ["ConstructorStores"] = ["SF1002"],
            ["HelperSamples"] = ["SF1004"],
            ["DerivedIntoHelperSamples"] = ["SF1004"],
            ["CloseWithPlaceholder"] = ["SF1002"],
            ["CloseWithMap"] = ["SF1002"],
            ["CloseWithUpdatesHold"] = ["SF1002"],
            ["SwitchOverOwnOutput"] = ["SF1002"],
            ["ReadInNestedTransaction"] = ["SF1001"],
            ["ReadInLinqLambda"] = ["SF1001"],
            ["ReadInLocalFunction"] = ["SF1001"],
            ["ForwardReferenceRead"] = ["SF1001"],
            ["ListLiftSample"] = ["SF1001"],
            ["DelegateCall"] = ["SF1003"],
            ["BehaviorMapSample"] = ["SF1001"],
            ["BindableRead"] = ["SF1001"],
            ["RowPropertyReadInside"] = ["SF1001"],
            ["RowMethodReadInside"] = ["SF1005"],
            ["ExplicitRowReadInside"] = ["SF1001"],
            ["RowIntoListAndComputedProperty"] = ["SF1005"],
            ["CloseWithOwnLazySample"] = ["SF1006"],
            ["CloseThroughHeldRow"] = ["SF1002"],
            ["StoredLambdas"] = ["SF1001"],
            ["DeferredSampleRead"] = ["SF1001"],
            ["BlockHelpers"] = ["SF1001"],
            ["NestedLoop"] = ["SF1001", "SF1002"],
            ["HelperChains"] = ["SF1001", "SF1001", "SF1003"],
            ["RowMethodThroughSecondMethod"] = ["SF1005"],
            ["HelperSamplesThroughHelper"] = ["SF1003", "SF1004"],
            ["RecursiveHelper"] = ["SF1003"],
            ["ConstructorInitializerCallsHelper"] = ["SF1001"],
            ["CloseThroughSinkOfRow"] = ["SF1002"],
            ["AssertionAtTheArgument"] = ["SF1004"],
            ["CoreBlockSample"] = ["SF1001"],
            ["CoreBlockCloseWithPlaceholder"] = ["SF1002"],
            ["NamedArgumentsReorderedToTheLoop"] = ["SF1001"],
            ["GenericHelperSamples"] = ["SF1004"],
            ["GenericRowSamples"] = ["SF1004"],
            ["ViewFilteredByTheLoopRead"] = ["SF1001"],
            ["ViewFilteredByTheLoopAsTheClose"] = ["SF1002"],
            ["FilterOfPendingViewRead"] = ["SF1001"],
            ["ConditionalAccessSample"] = ["SF1001"],
            ["ConditionalAccessMapSample"] = ["SF1001"],
            ["ConditionalAccessBindableValue"] = ["SF1001"],
            ["ConditionalAccessRowMember"] = ["SF1001"],
            ["StaticFormSample"] = ["SF1001"],
            ["StaticFormMapSample"] = ["SF1001"],
            ["StaticFormNamedMapSample"] = ["SF1001"],
            ["LocalFunctionBlock"] = ["SF1001"],
            ["AnonymousMethodBlock"] = ["SF1001"],
            ["BlockThatReads"] = ["SF1001"],
            ["BlockThatClosesWithItself"] = ["SF1002"],
            ["RecordPositionalMember"] = ["SF1001"],
            ["RecordWithPendingMember"] = ["SF1001"],
            ["ObjectInitializerMember"] = ["SF1001"],
            ["ObjectInitializerNoArgumentList"] = ["SF1001"],
            ["ObjectInitializerReads"] = ["SF1001"],
            ["BaseConstructorMember"] = ["SF1001"],
            ["BaseConstructorSamples"] = ["SF1004"],
            ["WrittenConstructorChain"] = ["SF1001"],
            ["CollectionInitializerElement"] = ["SF1001"],
            ["HelperDerivesThenSamples"] = ["SF1004"],
            ["HelperLocalThenSamples"] = ["SF1004"],
            ["RowPassedToHelper"] = ["SF1004"],
            ["RowMethodDerivedRead"] = ["SF1005"],
            ["HelperBranchReturn"] = ["SF1001"],
            ["ComputedPropertyOfPendingMember"] = ["SF1001"],
            ["HelperPassesOn"] = ["SF1003"],
            ["LocalFunctionWithParameter"] = ["SF1004"],
            ["LocalFunctionReadsALocal"] = ["SF1001"],
            ["StoredLambdaThroughInvoke"] = ["SF1001"],
            ["StoredLambdaThroughConditionalInvoke"] = ["SF1001"],
            ["StoredLambdaWithParameter"] = ["SF1004"],
            ["PrimaryConstructorCaptureCalled"] = ["SF1005"],
            ["LambdaKeptByConstructorThenRun"] = ["SF1001"],
            ["LambdaRunByMethod"] = ["SF1001"],
            ["LambdaResultReadByMethod"] = ["SF1004"],
            ["LambdaGivenTheLoopByMethod"] = ["SF1004"],
            ["ContentFunctionGivesALocal"] = ["SF1002"],
            ["ForeachVariable"] = ["SF1001"],
            ["Deconstruction"] = ["SF1001"],
            ["PatternVariable"] = ["SF1001"],
            ["PatternVariableOfRowMember"] = ["SF1001"],
            ["SwitchPatternVariable"] = ["SF1001"],
            ["OutVariable"] = ["SF1001"],
            ["StaticField"] = ["SF1001"],
            ["StaticFieldReadByHelper"] = ["SF1005"],
            ["ListAddThenIndex"] = ["SF1001"],
            ["LinqLambdaParameter"] = ["SF1001"],
            ["FirstOfPendingList"] = ["SF1001"],
            ["DictionaryTryGetValue"] = ["SF1001"],
            ["MemberAssignedInTheBlock"] = ["SF1001"],
            ["MaybeMatchRunsItsFunction"] = ["SF1001"],
            ["MaybeOfPendingCellMatched"] = ["SF1001"],
            ["FoldSelectReads"] = ["SF1001"],
            ["FunctionWrapperCalled"] = ["SF1001"],
            ["LocalFunctionGivenToAQuery"] = ["SF1004"],
            ["QueryResultOfALambda"] = ["SF1001"],
            ["SampleOfPendingContent"] = ["SF1001"],
            ["RowFromSampleOfSink"] = ["SF1001"],
            ["BindableValueIsAPendingCell"] = ["SF1001"],
            ["ForeachOverASampledList"] = ["SF1001"],
            ["ReassignedLocalInABranch"] = ["SF1001"],
            ["ReassignedLocalToThePendingValue"] = ["SF1001"],
            ["MapFunctionReads"] = ["SF1001"],
            ["LiftFunctionReads"] = ["SF1001"],
            ["BehaviorMapFunctionReads"] = ["SF1001"],
            ["LazyMapFunctionReads"] = ["SF1001"],
            ["ViewPredicateReads"] = ["SF1001"],
            ["CloseWithMapWhoseFunctionReads"] = ["SF1006"],
            ["MapFunctionReadsThroughHelper"] = ["SF1001"],
            ["MapOfMapFunctionReads"] = ["SF1001"],
            ["PendingPartOfATupleFromAHelper"] = ["SF1001"],
            ["PendingElementOfATupleLocal"] = ["SF1001"],
            ["MemberThroughAnInterface"] = ["SF1001"],
            ["MethodThroughAnInterface"] = ["SF1005"],
            ["ObjectGivesItselfToAHelper"] = ["SF1004"],
            ["AssignedInAFunctionThatDoesNotRun"] = ["SF1001"],
            ["DelegateGetsAFunctionThenRuns"] = ["SF1001"],
            ["ValuesOfADictionary"] = ["SF1001"],
            ["FunctionKeptByAConstructorOfThisCode"] = ["SF1005"],
            ["FunctionFromAHelperCalled"] = ["SF1005"],
            ["LazyReadInTheBlock"] = ["SF1001"],
            ["LazyOfALocalFunctionRead"] = ["SF1001"],
            ["LazyReadInAHelper"] = ["SF1001"],
            ["LazyWhoseFunctionCallsAHelper"] = ["SF1004"],
            ["LazyConditionalRead"] = ["SF1001"],
            ["LazyMapRead"] = ["SF1001"],
            ["LazyMapSamplesAMadeCell"] = ["SF1004"],
            ["LazyLiftRead"] = ["SF1001"],
            ["LazyInAListRead"] = ["SF1001"],
            ["LazyOfAFunctionCalled"] = ["SF1001"],
            ["LazyOfAMadeFunctionCalled"] = ["SF1001"],
            ["LazyKeptByAConstructorOfThisCode"] = ["SF1005"],
            ["LazyFromAHelperRead"] = ["SF1005"],
            ["HeldInitialCellRead"] = ["SF1001"],
            ["SortByAnOrderThatReads"] = ["SF1001"],
            ["SortByAnOrderCellThatReads"] = ["SF1001"],
            ["ThenByThatReads"] = ["SF1001"],
            ["OrderByIdentityThatReads"] = ["SF1001"],
            ["OrderFromAHelper"] = ["SF1001"],
            ["OrderOfALocalFunction"] = ["SF1001"],
            ["OrderCellMappedToAnOrderThatReads"] = ["SF1001"],
            ["CloseWithAViewSortedByAnOrderCell"] = ["SF1006"],
            ["GetterInAPropertyPattern"] = ["SF1005"],
            ["LazyValueInAPropertyPattern"] = ["SF1001"],
            ["BindableValueInAPropertyPattern"] = ["SF1001"],
            ["GetterInASwitchStatement"] = ["SF1005"],
            ["GetterInASwitchExpression"] = ["SF1005"],
            ["GetterInANestedPattern"] = ["SF1005"],
            ["GetterInAnExtendedPattern"] = ["SF1005"],
            ["GetterInAPatternInAHelper"] = ["SF1004"],
            ["PatternOnAPendingRowInAMethodOfAPlainRow"] = ["SF1004"],
            ["GetterInANegatedPattern"] = ["SF1005"],
            ["GetterInATypePattern"] = ["SF1005"],
            ["GetterThroughAnInterfaceInAPattern"] = ["SF1005"],
            ["ComparerObjectInASort"] = ["SF1001"],
            ["ComparerObjectThroughTheInterface"] = ["SF1001"],
            ["KeyComparerObjectWithATie"] = ["SF1001"],
            ["SortByKeyWithAComparerObject"] = ["SF1001"],
            ["OrderByKeyWithAComparerObject"] = ["SF1001"],
            ["ThenByWithAComparerObjectAndATie"] = ["SF1001"],
            ["ComparerFromAFunctionInASort"] = ["SF1001"],
            ["ComparerFromAHelperInASort"] = ["SF1001"],
            ["ComparerObjectGivenToAQuery"] = ["SF1005"],
            ["ComparerObjectGivenToMinOrNone"] = ["SF1005"],
            ["CloseWithAViewSortedByAComparerObject"] = ["SF1006"],
            ["EqualityComparerObjectInACollection"] = ["SF1001"],
            ["KeySelectorOfACollectionThatReads"] = ["SF1001"],
            ["IndexerGetter"] = ["SF1005"],
            ["IndexerSetter"] = ["SF1005"],
            ["IndexerGetterThroughAConditionalAccess"] = ["SF1005"],
            ["OperatorWithTwoOperands"] = ["SF1004"],
            ["OperatorWithOneOperand"] = ["SF1004"],
            ["IncrementOperator"] = ["SF1004"],
            ["OperatorInACompoundAssignment"] = ["SF1004"],
            ["TruthOperator"] = ["SF1004"],
            ["OperatorInAHelper"] = ["SF1004"],
            ["ImplicitConversion"] = ["SF1004"],
            ["ImplicitConversionAtAnArgument"] = ["SF1004"],
            ["ExplicitConversion"] = ["SF1004"],
            ["ImplicitConversionFromAPendingCell"] = ["SF1004"],
            ["PropertySetter"] = ["SF1005"],
            ["PropertySetterInAnInitializer"] = ["SF1005"],
            ["PropertySetterThatSamplesItsValue"] = ["SF1004"],
            ["EventAccessor"] = ["SF1005"],
            ["DeconstructMethod"] = ["SF1005"],
            ["DeconstructMethodInAPattern"] = ["SF1005"],
            ["DeconstructMethodInAForEach"] = ["SF1005"],
            ["DeconstructMethodThatGivesAPendingCell"] = ["SF1001"],
            ["ToStringInAnInterpolation"] = ["SF1005"],
            ["ToStringInAConcatenation"] = ["SF1005"],
            ["DisposeInAUsingStatement"] = ["SF1005"],
            ["DisposeInAUsingDeclaration"] = ["SF1005"],
            ["ForEachOverAnEnumerableOfThisCode"] = ["SF1005"],
            ["ForEachOverAnEnumeratorOfThisCode"] = ["SF1005"],
            ["AddInACollectionInitializer"] = ["SF1005"],
            ["ForEachOverAnIteratorOfCells"] = ["SF1001"],
            ["AddInACollectionInitializerThatSamplesItsValue"] = ["SF1004"],
            ["DefaultComparerComparesObjects"] = ["SF1004", "SF1004", "SF1005"],
            ["MinOfObjects"] = ["SF1004", "SF1005"],
            ["OrderByAnObjectKey"] = ["SF1004", "SF1005"],
            ["ThenByAnObjectKey"] = ["SF1004", "SF1005"],
            ["OrderByAMethodThatGivesAnObjectKey"] = ["SF1004", "SF1005"],
            ["OrderByALocalFunctionThatGivesAnObjectKey"] = ["SF1004", "SF1005"],
            ["OrderByAFunctionInALocalThatGivesAnObjectKey"] = ["SF1004", "SF1005"],
            ["GroupByAMethodThatGivesAnObjectKey"] = ["SF1005"],
            ["MaxByAnObjectKey"] = ["SF1004", "SF1005"],
            ["OrderByATupleKeyWithAnObject"] = ["SF1004", "SF1005"],
            ["MaxOfObjectsFromAQuery"] = ["SF1004", "SF1005"],
            ["SortedSetInitializerOfObjects"] = ["SF1004", "SF1005"],
            ["SortedSetAddOfObjects"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["SortedDictionaryAddOfObjectKeys"] = ["SF1004", "SF1005"],
            ["SortedDictionaryIndexerOfObjectKeys"] = ["SF1004", "SF1005"],
            ["SortedSetContainsAnObject"] = ["SF1004", "SF1005"],
            ["ListContainsWithAPendingElement"] = ["SF1004", "SF1005"],
            ["ListContainsAPendingObject"] = ["SF1004", "SF1005"],
            ["ListContainsAnObjectThatEqualsReads"] = ["SF1004"],
            ["ListIndexOfAnObject"] = ["SF1004", "SF1005"],
            ["ListRemoveOfAnObject"] = ["SF1004", "SF1005"],
            ["ArrayIndexOfAnObject"] = ["SF1004", "SF1005"],
            ["ArrayContainsAnObject"] = ["SF1004", "SF1005"],
            ["QueryContainsAnObject"] = ["SF1004", "SF1005"],
            ["SequenceEqualOfObjects"] = ["SF1004", "SF1005"],
            ["StaticEqualsOfObjects"] = ["SF1004", "SF1005"],
            ["DefaultEqualityComparerOfObjects"] = ["SF1004", "SF1004", "SF1005"],
            ["ListContainsAnEquatableObject"] = ["SF1004", "SF1005"],
            ["TupleEqualsWithAnObject"] = ["SF1004", "SF1005"],
            ["DistinctOfObjects"] = ["SF1005"],
            ["GroupByAnObjectKey"] = ["SF1005"],
            ["ToDictionaryWithObjectKeys"] = ["SF1005"],
            ["ToHashSetOfObjects"] = ["SF1005"],
            ["ExceptOfObjects"] = ["SF1005"],
            ["JoinOnObjectKeys"] = ["SF1005", "SF1005"],
            ["HashSetInitializerOfObjects"] = ["SF1005"],
            ["HashSetExpressionOfObjects"] = ["SF1005"],
            ["HashSetAddOfAnObject"] = ["SF1005"],
            ["HashSetConstructorWithObjects"] = ["SF1005"],
            ["HashSetContainsAnObject"] = ["SF1005"],
            ["HashSetOfObjectsWithOneHashCode"] = ["SF1004", "SF1005"],
            ["DictionaryAddOfAnObjectKey"] = ["SF1005"],
            ["DictionaryIndexerSetOfAnObjectKey"] = ["SF1005"],
            ["DictionaryIndexerGetOfAnObjectKey"] = ["SF1005"],
            ["DictionaryIndexerInitializerOfAnObjectKey"] = ["SF1005"],
            ["DictionaryAddInitializerOfAnObjectKey"] = ["SF1005"],
            ["DictionaryContainsAnObjectKey"] = ["SF1005"],
            ["DictionaryTryGetValueOfAnObjectKey"] = ["SF1005"],
            ["JoinOfAListOfObjects"] = ["SF1005"],
            ["JoinOfObjects"] = ["SF1005"],
            ["ConcatOfAnObject"] = ["SF1005"],
            ["FormatOfAnObject"] = ["SF1005"],
            ["BuilderAppendOfAnObject"] = ["SF1005"],
            ["BuilderAppendJoinOfObjects"] = ["SF1005"],
            ["WriterWriteOfAnObject"] = ["SF1005"],
            ["ConvertToStringOfAnObject"] = ["SF1005"],
            ["TupleToStringWithAnObject"] = ["SF1005"],
            ["SumOfASequence"] = ["SF1005"],
            ["ToListOfASequence"] = ["SF1005"],
            ["ListConstructorWithASequence"] = ["SF1005"],
            ["AddRangeOfASequence"] = ["SF1005"],
            ["JoinOfASequence"] = ["SF1005"],
            ["SpreadOfASequence"] = ["SF1005"],
            ["SumOfASequenceThroughAnInterface"] = ["SF1005"],
            ["SumOfASequenceInAStaticCall"] = ["SF1005"],
            ["SumOfASequenceWithAnEnumeratorObject"] = ["SF1005"],
            ["QueryFunctionSamplesAnElementOfASequence"] = ["SF1001"],
            ["ForEachOverAListFromASequence"] = ["SF1001"],
            ["ElementOfAListOfObjects"] = ["SF1005"],
            ["ForEachOverAListOfObjects"] = ["SF1005"],
            ["ObjectInAnObjectLocal"] = ["SF1005"],
            ["ObjectToAGenericHelper"] = ["SF1004"],
            ["ObjectOrAListInAnObjectLocal"] = ["SF1005"],
            ["ListToAGenericHelperThatEnumerates"] = ["SF1004"],
            ["ForEachOverAnObjectLocalAsASequence"] = ["SF1005"],
            ["ElementOfAnObjectLocalAfterACast"] = ["SF1005"],
            ["StringJoinOfAnObjectLocalAsASequence"] = ["SF1005"],
            ["ValueOfADictionaryOfObjects"] = ["SF1005"],
            ["OutValueOfADictionaryOfObjects"] = ["SF1005"],
            ["ForEachMethodOfAListOfObjects"] = ["SF1005"],
            ["ElementOfAnArrayList"] = ["SF1005"],
            ["PatternGivesAnObjectLocalAListType"] = ["SF1005"],
            ["GenericHelperGivesAnElement"] = ["SF1005"],
            ["GenericHelperGivesTheFirstElement"] = ["SF1005"],
            ["AggregateOverAListOfObjects"] = ["SF1005"],
            ["ListPatternOverAListOfObjects"] = ["SF1005"],
            ["ForEachOverAQueryThatSamples"] = ["SF1001"],
            ["ToListOfAQueryThatSamples"] = ["SF1001"],
            ["FirstOfAQueryThatSamples"] = ["SF1001"],
            ["StringJoinOfAQueryThatSamples"] = ["SF1001"],
            ["ListFromAQueryThatSamples"] = ["SF1001"],
            ["SpreadOfAQueryThatSamples"] = ["SF1001"],
            ["AddRangeOfAQueryThatSamples"] = ["SF1001"],
            ["EnumeratorOfAQueryThatSamples"] = ["SF1001"],
            ["QueryOverAQueryToList"] = ["SF1001"],
            ["ChainOfQueriesEnumerated"] = ["SF1001"],
            ["QueryAsTheSecondSourceOfAQuery"] = ["SF1001"],
            ["QueryFromTheFunctionOfSelectMany"] = ["SF1001"],
            ["QueryThatAQueryFunctionReturnsEnumerated"] = ["SF1001"],
            ["QueryToALocalFunctionThatEnumerates"] = ["SF1001"],
            ["QueryToAHelperThatEnumerates"] = ["SF1001"],
            ["QueryToAnExtensionMethodThatEnumerates"] = ["SF1001"],
            ["QueryEnumeratedInALambdaInALocal"] = ["SF1001"],
            ["QueryEnumeratedInALambdaThatRunsInTheCall"] = ["SF1001"],
            ["QueryEnumeratedByAnObject"] = ["SF1001"],
            ["QueryToTheLibrary"] = ["SF1001"],
            ["QueryEnumeratedInALazy"] = ["SF1001"],
            ["QueryThroughALazy"] = ["SF1001"],
            ["QueryThroughATask"] = ["SF1001"],
            ["QueryThroughATupleOfThePlatform"] = ["SF1001"],
            ["QueryThroughATuple"] = ["SF1001"],
            ["QueryThroughAnAnonymousObject"] = ["SF1001"],
            ["CellThroughAnAnonymousObject"] = ["SF1001"],
            ["QueryThroughAnElementOfAList"] = ["SF1001"],
            ["QueryThroughAnElementOfAnArray"] = ["SF1001"],
            ["QueryThroughTheValuesOfADictionary"] = ["SF1001"],
            ["QueryThroughACell"] = ["SF1001"],
            ["QueryInAnObjectLocalThroughAPattern"] = ["SF1001"],
            ["QueryThroughAStaticField"] = ["SF1001"],
            ["QueryThroughAnOutParameter"] = ["SF1004"],
            ["QueryThroughAMethodGroup"] = ["SF1001"],
            ["QueryInAnObjectInitializerEnumerated"] = ["SF1001"],
            ["QueryThroughAnIterator"] = ["SF1001"],
            ["LocalIteratorOverAQueryEnumerated"] = ["SF1001"],
            ["QueryExpressionEnumerated"] = ["SF1001"],
            ["QueryExpressionWithClausesEnumerated"] = ["SF1001"],
            ["ForEachOverAQueryExpression"] = ["SF1001"],
            ["QueryExpressionOverAQuery"] = ["SF1001"],
            ["QueryExpressionWithASecondSource"] = ["SF1001"],
            ["QueryWithAFunctionInALocalEnumerated"] = ["SF1001"],
            ["QueryWithALocalFunctionEnumerated"] = ["SF1001"],
            ["QueryThatSamplesAnElementEnumerated"] = ["SF1001"],
            ["QueryFromAStaticCallEnumerated"] = ["SF1001"],
            ["QueryEnumeratedTwoTimes"] = ["SF1001"],
            ["QueryAssignedInALoopEnumerated"] = ["SF1001"],
            ["SortOfObjectsEnumeratedLater"] = ["SF1004", "SF1005"],
            ["DistinctOfObjectsToList"] = ["SF1005"],
            ["ForEachOverAnIteratorThatSamples"] = ["SF1004"],
            ["ToListOfAnIteratorThatSamples"] = ["SF1004"],
            ["SpreadOfAnIteratorThatSamples"] = ["SF1004"],
            ["LocalIteratorEnumerated"] = ["SF1001"],
            ["EnumeratorOfAnIteratorMoved"] = ["SF1005"],
            ["IteratorOfAnEnumeratorMoved"] = ["SF1004"],
            ["IteratorOfAnObjectEnumerated"] = ["SF1005"],
            ["QueryOverAnIteratorEnumerated"] = ["SF1005"],
            ["IteratorToAHelperThatEnumerates"] = ["SF1004"],
            ["QueryFromALocalFunctionEnumerated"] = ["SF1001"],
            ["QueryFromAHelperEnumerated"] = ["SF1004"],
            ["QueryFromAHelperOfAHelperEnumerated"] = ["SF1004"],
            ["QueryFromAHelperToAHelperThatEnumerates"] = ["SF1004"],
            ["IteratorFromAHelperEnumerated"] = ["SF1004"],
            ["QueryFromALambdaInALocalEnumerated"] = ["SF1001"],
            ["IteratorFromALambdaEnumerated"] = ["SF1004"],
            ["QueryOfAConstructorEnumeratedByAMethod"] = ["SF1004"],
            ["QueryOfAGetterEnumerated"] = ["SF1005"],
            ["QueryOfAReturnedLambdaEnumerated"] = ["SF1005"],
            ["QueryFromALocalFunctionOfSelectMany"] = ["SF1001"],
            ["QueryExpressionWithAJoin"] = ["SF1001"],
            ["InterpolationOfAGenericObject"] = ["SF1005"],
            ["InterpolationOfAnObjectInAGenericType"] = ["SF1005"],
            ["ForEachOverAGenericSequence"] = ["SF1005"],
            ["UsingOfAGenericObject"] = ["SF1005"],
            ["SumOfAGenericSequence"] = ["SF1005"],
            ["StringJoinOfGenericObjects"] = ["SF1005"],
            ["MinOfGenericObjects"] = ["SF1004", "SF1005"],
            ["InterfaceMethodOfAGenericObject"] = ["SF1005"],
            ["VirtualMethodOfAGenericObject"] = ["SF1005"],
            ["ToStringOfAGenericObject"] = ["SF1005"],
            ["PropertyOfAGenericObject"] = ["SF1001"],
            ["ValueOfAPairOfThePlatform"] = ["SF1005"],
            ["ItemOfATupleOfThePlatform"] = ["SF1005"],
            ["ItemOfACreatedTupleOfThePlatform"] = ["SF1005"],
            ["ResultOfATaskOfAnObject"] = ["SF1005"],
            ["CurrentOfAnEnumeratorOfObjects"] = ["SF1005"],
            ["FieldOfAStrongBox"] = ["SF1005"],
            ["ValueOfTheFirstPairOfADictionary"] = ["SF1005"],
            ["ValueOfAPairInAForEach"] = ["SF1005"],
            ["PropertyPatternOnAPair"] = ["SF1005"],
            ["PropertyPatternOnAPairOfAnObject"] = ["SF1005"],
            ["ValueOfAPairInAGenericHelper"] = ["SF1004"],
            ["MemberOfTheValueOfAPair"] = ["SF1001"],
            ["ValueOfAPairOfAGenericObject"] = ["SF1005"],
            ["CellOfAPair"] = ["SF1001"],
            ["FirstOfAListOfObjects"] = ["SF1005"],
            ["SingleOfAListOfObjects"] = ["SF1005"],
            ["FindInAListOfObjects"] = ["SF1005"],
            ["DequeueOfAQueueOfObjects"] = ["SF1005"],
            ["PeekOfAStackOfObjects"] = ["SF1005"],
            ["FirstValueOfADictionaryOfObjects"] = ["SF1005"],
            ["ElementOfAnArrayFromAListOfObjects"] = ["SF1005"],
            ["FirstOfARepeatedObject"] = ["SF1005"],
            ["ElementOfASpanOfObjects"] = ["SF1005"],
            ["ElementAtOfASequenceOfObjects"] = ["SF1005"],
            ["FirstAfterACastToObjects"] = ["SF1005"],
            ["LastOrDefaultOfAnArrayOfObjects"] = ["SF1005"],
            ["MaxByOfAnArrayOfObjects"] = ["SF1005"],
            ["GetValueOrDefaultOfADictionaryOfObjects"] = ["SF1005"],
            ["ObjectFromAMethodThatReturnsItsArgument"] = ["SF1005"],
            ["CurrentOfAnEnumeratorOfPlainObjects"] = ["SF1005"],
            ["ItemOfATupleOfAnObject"] = ["SF1005"],
            ["ValueOfAPairOfAnObject"] = ["SF1005"],
            ["ResultOfATaskOfAPlainObject"] = ["SF1005"],
            ["TargetOfAWeakReference"] = ["SF1005"],
            ["PopOfAStackThatIsNotGeneric"] = ["SF1005"],
            ["CurrentOfAnEnumeratorThatIsNotGeneric"] = ["SF1005"],
            ["ValueOfADictionaryEntry"] = ["SF1005"],
            ["CellFromAListOfObjects"] = ["SF1001"],
            ["ChooseEnumerated"] = ["SF1001"],
            ["ChooseWithAnIndexEnumerated"] = ["SF1001"],
            ["ForEachOverChoose"] = ["SF1001"],
            ["WhereSomeOverAQueryEnumerated"] = ["SF1001"],
            ["ChooseThenFirstOrNone"] = ["SF1001"],
            ["AllSomeOrNoneWithAFunctionThatSamples"] = ["SF1001"],
            ["FirstOrNoneWithAFunctionThatSamples"] = ["SF1001"],
            ["MaxOrNoneWithAFunctionThatSamples"] = ["SF1001"],
            ["ValueOfAPairOfATuple"] = ["SF1005"],
            ["ResultOfATaskOfATuple"] = ["SF1005"],
            ["TupleKeptAsObjectThenRead"] = ["SF1005"],
            ["TupleKeptAsAnInterfaceThenRead"] = ["SF1005"],
            ["ValueOfANullableTuple"] = ["SF1005"],
            ["ValueOfANullableStruct"] = ["SF1005"],
            ["NullableTupleThroughAPattern"] = ["SF1005"],
            ["HelperAddsToAList"] = ["SF1001"],
            ["LocalFunctionAddsToAList"] = ["SF1001"],
            ["HelperSetsAMemberOfAnObject"] = ["SF1001"],
            ["HelperPutsIntoADictionary"] = ["SF1001"],
            ["HelperSetsAnArrayElement"] = ["SF1001"],
            ["ExtensionMethodAddsToItsReceiver"] = ["SF1001"],
            ["ConstructorAddsToAList"] = ["SF1001"],
            ["HelperAddsThroughASecondHelper"] = ["SF1001"],
            ["ElementOfTheInnerList"] = ["SF1005"],
            ["InnerListAsObjectThenElement"] = ["SF1005"],
            ["ForeachInnerThenElement"] = ["SF1005"],
            ["PileInAListAsObject"] = ["SF1005"],
            ["ForeachPileAsObject"] = ["SF1005"],
            ["OldPendingValueOfAnExchange"] = ["SF1005"],
            ["SlotAfterAnExchange"] = ["SF1005"],
            ["OldPendingValueOfACompareExchange"] = ["SF1005"],
            ["SlotAfterACompareExchange"] = ["SF1005"],
            ["IteratorPropertyInAForeach"] = ["SF1005"],
            ["IteratorPropertyToList"] = ["SF1005"],
            ["IteratorPropertyInALocalThenCount"] = ["SF1005"],
            ["IteratorPropertyWithNoOwnerSum"] = ["SF1005"],
            ["IteratorPropertyThroughAConditionEnumerated"] = ["SF1005"],
            ["EnumeratorPropertyMoved"] = ["SF1005"],
            ["IteratorPropertySpread"] = ["SF1005"],
            ["IteratorPropertyKeptThenEnumerated"] = ["SF1005"],
            ["GetterWithCodeThatReturnsASequence"] = ["SF1005"],
            ["QuerySerialized"] = ["SF1001"],
            ["QuerySerializedAsAnObject"] = ["SF1001"],
            ["QueryInAnAnonymousObjectSerialized"] = ["SF1001"],
            ["ObjectWithAGetterThatReadsSerialized"] = ["SF1005"],
            ["ObjectWithAConditionOnJsonIgnoreSerialized"] = ["SF1005"],
            ["ObjectInAnObjectSerialized"] = ["SF1005"],
            ["ListOfObjectsSerialized"] = ["SF1005"],
            ["ObjectWithAGetterThatGivesAQuerySerialized"] = ["SF1001"],
            ["QuerySerializedByJsonConvert"] = ["SF1001"],
            ["ObjectSerializedByJsonConvert"] = ["SF1005"],
            ["TupleOfAQuerySerializedByJsonConvert"] = ["SF1001"],
            ["ObjectSerializedByXmlSerializer"] = ["SF1005"],
            ["ComparerKeptBySortedSetThenTwoAdds"] = ["SF1005", "SF1005"],
            ["ComparerKeptBySortedDictionary"] = ["SF1005", "SF1005"],
            ["EqualityKeptByDictionaryThenSet"] = ["SF1005"],
            ["ComparerAndItemsInTheConstructor"] = ["SF1005"],
            ["ComparerKeptThenACollectionInitializer"] = ["SF1005"],
            ["EqualityKeptThenAnInitializerWithAnIndex"] = ["SF1005"],
            ["ComparerKeptByAPriorityQueue"] = ["SF1005", "SF1005"],
            ["ComparerKeptByAPriorityQueueThenEnqueueDequeue"] = ["SF1005", "SF1005"],
            ["ComparerKeptByAPriorityQueueThenEnqueueRange"] = ["SF1005"],
            ["ComparerAndItemsInTheConstructorOfAPriorityQueue"] = ["SF1005"],
            ["PrioritiesOfAPriorityQueueCompared"] = ["SF1004", "SF1005"],
            ["ComparerKeptBySortedSetOfObjects"] = ["SF1005", "SF1005"],
            ["ToStringOfAnObjectFilledByAHelper"] = ["SF1005"],
            ["SlotAfterACompareExchangeThatDoesNotExchange"] = ["SF1005"],
            ["SequenceOfTheCompilationSerialized"] = ["SF1005"],
            ["PropertySetAfterTheConstructorReadThroughObject"] = ["SF1005"],
            ["PropertySetAfterTheConstructorReadThroughItsType"] = ["SF1005"],
            ["PropertySetByAHelperAfterTheConstructorReadThroughObject"] = ["SF1005"],
            ["PropertySetByAnInitializerReadThroughObject"] = ["SF1005"],
            ["OneOfTwoObjectsReadThroughObject"] = ["SF1005"],
            ["ToStringOfATupleThroughObject"] = ["SF1005"],
            ["ToStringOfATupleThroughValueType"] = ["SF1005"],
            ["ElementOfATupleThroughITuple"] = ["SF1005"],
            ["EqualsOfATupleThroughObject"] = ["SF1004", "SF1005"],
            ["GetHashCodeOfATuple"] = ["SF1005"],
            ["GetHashCodeOfATupleThroughObject"] = ["SF1005"],
            ["CompareToOfATuple"] = ["SF1004", "SF1005"],
            ["CompareToOfATupleThroughAnInterface"] = ["SF1004", "SF1005"],
            ["PendingElementOfATupleInAList"] = ["SF1005"]
        };

    // A shape whose block is a method, given by its name. The analyzer reports in that method.
    // Thus, the rules of the method say what the shape does at run time.
    private static readonly Dictionary<string, string> BlockMethods =
        new()
        {
            ["MethodBlock"] = "BlockThatReads",
            ["MethodBlockCloses"] = "BlockThatClosesWithItself",
            ["MethodBlockSnapshots"] = "BlockThatSnapshots"
        };

    // Core lets an assembly with this name use its internals. The Core block shapes must use them,
    // because only the library can call a Core block. The name is not one of the library, thus
    // the analyzer reads the shapes as code that uses the library.
    private const string ShapesAssembly = "SodaFlow.Tests.Internal";

    // The shapes that the run-time test does not call.
    private static readonly HashSet<string> NotRun =
    [
        // The shapes file must have an entry point.
        "Main",
        // The helper calls itself with no end, and a run overflows the stack.
        "RecursiveHelper"
    ];

    private static readonly Assembly[] ShapeLibraries =
    [
        typeof(Cell<>).Assembly,
        typeof(CellExtensionMethods).Assembly,
        typeof(Maybe<>).Assembly,
        typeof(IBindableFactory).Assembly,
        typeof(IOneWayBindableValue<>).Assembly,
        typeof(IIdentity<>).Assembly,
        typeof(CollectionExtensionMethods).Assembly,
        // The shapes of the serializer rule call it. The run of the shapes holds the rule to it.
        typeof(Newtonsoft.Json.JsonConvert).Assembly
    ];

    private static Task<string> ReadShapes() =>
        File.ReadAllTextAsync(Path.Combine(path1: AppContext.BaseDirectory, path2: "Shapes.cs"));

    [Test]
    public async Task EachShapeReportsTheExpectedRules()
    {
        string source = await ReadShapes();

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly));

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        SyntaxNode root = await tree.GetRootAsync();

        IReadOnlyList<string> shapes =
        [
            .. root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(static method => method.Parent is ClassDeclarationSyntax { Identifier.ValueText: "Shapes" })
                .Select(static method => method.Identifier.ValueText)
        ];

        Dictionary<string, List<string>> actual =
            shapes.ToDictionary(keySelector: static shape => shape, elementSelector: static _ => new List<string>());

        foreach (Diagnostic diagnostic in diagnostics)
        {
            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan);

            MethodDeclarationSyntax? shape =
                node.AncestorsAndSelf()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(static method =>
                        method.Parent is ClassDeclarationSyntax { Identifier.ValueText: "Shapes" });

            if (shape == null)
            {
                throw new InvalidOperationException($"{diagnostic} is outside every shape.");
            }

            actual[shape.Identifier.ValueText].Add(diagnostic.Id);
        }

        // All mismatches together, so that one run says what changed.
        IReadOnlyList<string> mismatches =
        [
            .. shapes
                .Select(shape =>
                {
                    string expected =
                        string.Join(
                            separator: " ",
                            value: Expected.TryGetValue(key: shape, value: out string[]? rules) ? rules : []);

                    string found =
                        string.Join(
                            separator: " ",
                            values: actual[shape]
                                .OrderBy(keySelector: static id => id, comparer: StringComparer.Ordinal));

                    return expected == found ? null : $"{shape}: expected [{expected}], found [{found}]";
                })
                .Where(static line => line != null)
                .Cast<string>()
        ];

        await NoMismatches(mismatches);

        await Assert.That(Expected.Keys.Except(shapes)).IsEmpty().Because("every expectation names a shape");
    }

    /// <summary>
    ///     Holds the table of expected rules to the library. A rule for a read
    ///     means that the shape throws the exception of a looped value sampled
    ///     before its loop closes. A rule for a close means that the shape throws
    ///     the exception of that close. No rule means that the shape returns.
    /// </summary>
    /// <remarks>
    ///     Without this test, the table is only what the analyzer says. A change
    ///     to the library can change what throws. Then, the analyzer is incorrect
    ///     and no test fails. SF1003 says that the analyzer cannot see the callee,
    ///     thus a shape with only that rule has no known result and does not run.
    /// </remarks>
    [Test]
    public async Task EachShapeDoesAtRunTimeWhatItsRulesSay()
    {
        Assembly assembly =
            AnalyzerHarness.Load(
                AnalyzerHarness.Compile(
                    source: await ReadShapes(),
                    libraries: ShapeLibraries,
                    assemblyName: ShapesAssembly));

        Type shapes =
            assembly.GetType("SodaFlow.Analyzers.Shapes.Shapes")
            ?? throw new InvalidOperationException("The shapes file has no class with the name Shapes.");

        List<string> mismatches = [];
        int count = 0;

        foreach (MethodInfo shape in shapes
                     .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(static method => !method.IsSpecialName && !NotRun.Contains(method.Name)))
        {
            string reportedIn =
                BlockMethods.TryGetValue(key: shape.Name, value: out string? blockMethod) ? blockMethod : shape.Name;

            string[] rules = Expected.TryGetValue(key: reportedIn, value: out string[]? listed) ? listed : [];

            IReadOnlyList<Outcome> permitted =
                rules.Length == 0
                    ? [Outcome.Returns]
                    : [.. rules.Where(static rule => rule != "SF1003").Select(OutcomeOf).Distinct()];

            if (permitted.Count == 0)
            {
                continue;
            }

            (Outcome outcome, string description) = Run(shape);
            count++;

            if (!permitted.Contains(outcome))
            {
                mismatches.Add(
                    $"{shape.Name}: the rules [{string.Join(separator: " ", value: rules)}] permit "
                    + $"[{string.Join(separator: ", ", values: permitted)}], but the shape {description}");
            }
        }

        await NoMismatches(mismatches);

        await Assert.That(count).IsGreaterThanOrEqualTo(1).Because("the test found shapes to run");
    }

    /// <summary>
    ///     The analyzer skips the search for a member that the platform calls
    ///     when the compilation declares no such member. A comparer is one of
    ///     them. The shapes file declares each kind, thus it cannot show that the
    ///     search runs for a compilation that declares only a comparer. The
    ///     search has one gate for a call and one for an indexer, and the code has
    ///     the two.
    /// </summary>
    [Test]
    public async Task AKeptComparerIsReadInACompilationThatDeclaresNoOtherMember()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();

                public static Cell<int> Kept() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        SortedSet<int> set = new(new ByOffset(l.Map(v => v)));
                        set.Add(1);
                        set.Add(2);

                        return S.Hold(set.Count);
                    });

                public static Cell<int> KeptByAnIndexer() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        SortedDictionary<int, int> all = new(new ByOffset(l.Map(v => v)));
                        all[1] = 1;
                        all[2] = 2;

                        return S.Hold(all.Count);
                    });

                public sealed class ByOffset(Cell<int> offset) : IComparer<int>
                {
                    public int Compare(int x, int y) => x.CompareTo(y + offset.Sample());
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyAComparer"));

        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1005", "SF1005", "SF1005", "SF1005"]);
    }

    // An assertion on a collection names ten of its items and counts the others. The reason names
    // each mismatch, so that one run says all that changed.
    private static async Task NoMismatches(IReadOnlyCollection<string> mismatches) =>
        await Assert.That(mismatches.Count)
            .IsEqualTo(0)
            .Because(string.Join(separator: Environment.NewLine, values: mismatches));

    private static Outcome OutcomeOf(string rule) =>
        rule switch
        {
            "SF1001" or "SF1004" or "SF1005" => Outcome.ReadsEarly,
            "SF1002" => Outcome.MakesCycle,
            "SF1006" => Outcome.ForcesItself,
            _ => throw new ArgumentOutOfRangeException(
                paramName: nameof(rule),
                actualValue: rule,
                message: "The test does not know what this rule means at run time.")
        };

    private static (Outcome Outcome, string Description) Run(MethodBase shape)
    {
        try
        {
            shape.Invoke(obj: null, parameters: [.. shape.GetParameters().Select(ArgumentFor)]);

            return (Outcome.Returns, "returns");
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is { } thrown)
        {
            // A serializer puts the exception of a getter into one of its own.
            Outcome outcome = Outcome.Other;

            for (Exception? cause = thrown; cause != null && outcome == Outcome.Other; cause = cause.InnerException)
            {
                outcome = OutcomeOf(cause);
            }

            return (outcome, $"throws {thrown.GetType().Name}: {thrown.Message}");
        }
    }

    private static Outcome OutcomeOf(Exception thrown) =>
        thrown switch
        {
            DependencyCycleException => Outcome.MakesCycle,
            InvalidOperationException when Says(exception: thrown, text: "sampled before it was looped") =>
                Outcome.ReadsEarly,
            InvalidOperationException when Says(exception: thrown, text: "lazy sample forces itself") =>
                Outcome.ForcesItself,
            _ => Outcome.Other
        };

    private static bool Says(Exception exception, string text) =>
        exception.Message.Contains(value: text, comparisonType: StringComparison.Ordinal);

    // A shape with a parameter takes a value that the analyzer cannot see into. The run gives it a
    // value that does nothing with a looped value.
    private static object ArgumentFor(ParameterInfo parameter) =>
        parameter.ParameterType switch
        {
            { } type when type == typeof(IBindableFactory) => new BindableFactory(BindingScheduler.Immediate),
            { } type when type == typeof(Func<Cell<int>, int>) => (Func<Cell<int>, int>)(static _ => 0),
            { } type when type == typeof(Action<Action>) => (Action<Action>)(static _ => { }),
            _ => throw new InvalidOperationException(
                $"The test has no argument for parameter {parameter.Name} of {parameter.Member.Name}.")
        };

    /// <summary>What a shape does when the test calls it.</summary>
    private enum Outcome
    {
        /// <summary>The shape returns.</summary>
        Returns,

        /// <summary>The shape samples a looped value before the loop closes.</summary>
        ReadsEarly,

        /// <summary>The shape closes a loop with a value that is linked to the loop.</summary>
        MakesCycle,

        /// <summary>
        ///     The shape closes a loop with a value whose initial value is a lazy
        ///     sample of the loop.
        /// </summary>
        ForcesItself,

        /// <summary>The shape throws an exception that no rule is about.</summary>
        Other
    }
}
