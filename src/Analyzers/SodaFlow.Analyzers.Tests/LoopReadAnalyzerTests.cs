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
            ["HelperChains"] = ["SF1001", "SF1001", "SF1001", "SF1003"],
            ["RowMethodThroughSecondMethod"] = ["SF1005", "SF1005"],
            ["HelperSamplesThroughHelper"] = ["SF1003", "SF1004", "SF1004"],
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
            ["DefaultComparerComparesObjects"] = ["SF1005"],
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
            ["SortedSetInitializerOfObjects"] = ["SF1004"],
            ["SortedSetAddOfObjects"] = ["SF1004"],
            ["SortedDictionaryAddOfObjectKeys"] = ["SF1004"],
            ["SortedDictionaryIndexerOfObjectKeys"] = ["SF1005"],
            ["SortedSetContainsAnObject"] = ["SF1005"],
            ["ListContainsWithAPendingElement"] = ["SF1005"],
            ["ListContainsAPendingObject"] = ["SF1004"],
            ["ListContainsAnObjectThatEqualsReads"] = ["SF1004"],
            ["ListIndexOfAnObject"] = ["SF1005"],
            ["ListRemoveOfAnObject"] = ["SF1005"],
            ["ArrayIndexOfAnObject"] = ["SF1005"],
            ["ArrayContainsAnObject"] = ["SF1005"],
            ["QueryContainsAnObject"] = ["SF1005"],
            ["SequenceEqualOfObjects"] = ["SF1005"],
            ["StaticEqualsOfObjects"] = ["SF1005"],
            ["DefaultEqualityComparerOfObjects"] = ["SF1005"],
            ["ListContainsAnEquatableObject"] = ["SF1005"],
            ["TupleEqualsWithAnObject"] = ["SF1005"],
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
            ["HashSetOfObjectsWithOneHashCode"] = ["SF1005"],
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
            ["ComparerKeptBySortedSetThenTwoAdds"] = ["SF1005"],
            ["ComparerKeptBySortedDictionary"] = ["SF1005"],
            ["EqualityKeptByDictionaryThenSet"] = ["SF1005"],
            ["ComparerAndItemsInTheConstructor"] = ["SF1005"],
            ["ComparerKeptThenACollectionInitializer"] = ["SF1005"],
            ["EqualityKeptThenAnInitializerWithAnIndex"] = ["SF1005"],
            ["ComparerKeptByAPriorityQueue"] = ["SF1005"],
            ["ComparerKeptByAPriorityQueueThenEnqueueDequeue"] = ["SF1005"],
            ["ComparerKeptByAPriorityQueueThenEnqueueRange"] = ["SF1005"],
            ["ComparerAndItemsInTheConstructorOfAPriorityQueue"] = ["SF1005"],
            ["PrioritiesOfAPriorityQueueCompared"] = ["SF1004"],
            ["ComparerKeptBySortedSetOfObjects"] = ["SF1005"],
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
            ["EqualsOfATupleThroughObject"] = ["SF1005"],
            ["GetHashCodeOfATuple"] = ["SF1005"],
            ["GetHashCodeOfATupleThroughObject"] = ["SF1005"],
            ["CompareToOfATuple"] = ["SF1005"],
            ["CompareToOfATupleThroughAnInterface"] = ["SF1005"],
            ["PendingElementOfATupleInAList"] = ["SF1005"],
            ["IteratorPropertyByAPatternThenCount"] = ["SF1005"],
            ["IteratorPropertyByAPatternInAForeach"] = ["SF1005"],
            ["IteratorPropertyByAPatternThenAnyInTheCondition"] = ["SF1005"],
            ["IteratorPropertyByAPatternKeptThenEnumerated"] = ["SF1005"],
            ["IteratorPropertyByASwitchExpressionThenCount"] = ["SF1005"],
            ["IteratorPropertyOfAMemberByAPatternThenCount"] = ["SF1005"],
            ["EnumeratorPropertyByAPatternMoved"] = ["SF1005"],
            ["GetterWithCodeByAPattern"] = ["SF1005"],
            ["IteratorPropertyByAPatternInAHelperThenCount"] = ["SF1004"],
            ["ElementOfATupleThroughITupleThenCast"] = ["SF1005"],
            ["ElementOfAListOfTwoKindsOfObjects"] = ["SF1005"],
            ["OneOfTwoPendingObjectsThroughObject"] = ["SF1005"],
            ["OneOfTwoPendingObjectsThroughAnInterface"] = ["SF1005"],
            ["ForeachOverAListOfTwoKindsOfObjects"] = ["SF1005"],
            ["JoinOfAListOfTwoKindsOfObjects"] = ["SF1005"],
            ["ListOfTwoKindsOfObjectsSerialized"] = ["SF1005"],
            ["GetterOfOneOfTwoObjectsByAPattern"] = ["SF1005"],
            ["ForeachOverOneOfTwoSequences"] = ["SF1005"],
            ["GetterOfAnInterfaceOnOneOfTwoObjects"] = ["SF1005"],
            ["GetterOfAnInterfaceOnOneOfTwoObjectsByAPattern"] = ["SF1005"],
            ["InterpolationOfOneOfTwoObjects"] = ["SF1005"],
            ["MethodThatIsAnIteratorInOnlyOneOfTwoObjects"] = ["SF1005"],
            ["CastOfOneOfTwoObjectsToTheSecondType"] = ["SF1005"],
            ["OneOfTwoSequencesGivenToThePlatform"] = ["SF1005"],
            ["MethodOfAnInterfaceThatOneOfTwoObjectsHasExplicitly"] = ["SF1005"],
            ["PropertyOfAnInterfaceOnOneOfTwoObjects"] = ["SF1001"],
            ["ConcatOfOneOfTwoObjectsThroughTheSecondType"] = ["SF1005"],
            ["LambdaGivenToAMethodThatOneOfTwoObjectsHasExplicitly"] = ["SF1001"],
            ["ExplicitMethodThroughTheInterface"] = ["SF1005"],
            ["ExplicitGetterThroughTheInterface"] = ["SF1005"],
            ["ExplicitGetterByAPattern"] = ["SF1005"],
            ["ExplicitMethodOnAnElementOfAList"] = ["SF1005"],
            ["MethodWithTheNameOfAnExplicitOne"] = ["SF1005"],
            ["ExplicitMethodOfAGenericInterface"] = ["SF1005"],
            ["ExplicitMethodOfABaseType"] = ["SF1005"],
            ["ExplicitDisposeAtAUsing"] = ["SF1005"],
            ["ForeachOverASequenceWithAnExplicitEnumerator"] = ["SF1005"],
            ["ExplicitOverloadOfAGenericInterface"] = ["SF1005"],
            ["InnerQueriesInTwoForeach"] = ["SF1001"],
            ["InnerQueryByIndexEnumerated"] = ["SF1001"],
            ["InnerQueryFirstThenCount"] = ["SF1001"],
            ["InnerQueriesCountOfEach"] = ["SF1001"],
            ["OuterFunctionOfAListOfQueriesReads"] = ["SF1001"],
            ["QueriesInADictionaryValueCounted"] = ["SF1001"],
            ["InnerQueryAsAnObjectEnumerated"] = ["SF1001"],
            ["InnerQueryAsDynamicEnumerated"] = ["SF1001"],
            ["QueryEnumeratedInAGenericMethod"] = ["SF1001"],
            ["QueryEnumeratedAsATypeParameter"] = ["SF1001"],
            ["QueryFromAGenericMethodEnumerated"] = ["SF1004"],
            ["EnumeratorOfAQueryMoved"] = ["SF1001"],
            ["PlainIteratorAsASequenceOfObjects"] = ["SF1004"],
            ["QueryOfTextsAsASequenceOfObjects"] = ["SF1001"],
            ["ChainOfTwoTypesEnumerated"] = ["SF1001"],
            ["QueryExpressionOverAQueryOfADifferentType"] = ["SF1001"],
            ["SecondSourceOfAZipEnumerated"] = ["SF1001"],
            ["ChainOfTwoTypesFromAMethod"] = ["SF1004"],
            ["ChainOfTwoTypesFromAMethodWithALocal"] = ["SF1004"],
            ["ChainOfThreeTypesFromTwoMethods"] = ["SF1004"],
            ["SelectManyWithAResultSelector"] = ["SF1001"],
            ["SelectManyWithABlockAndAResultSelector"] = ["SF1001"],
            ["SelectManyByNameWithAResultSelector"] = ["SF1001"],
            ["SelectManyOfAPlatformMethodByName"] = ["SF1001"],
            ["ToListByNameOverAListOfQueries"] = ["SF1001"],
            ["CountByNameInASumOfQueries"] = ["SF1001"],
            ["CountInALocalInASumOfQueries"] = ["SF1001"],
            ["SelectManyOfAnImmutableArrayOfQueries"] = ["SF1001"],
            ["SelectManyOfACastByName"] = ["SF1001"],
            ["ChainFromAMethodWhoseFunctionEnumeratesAQuery"] = ["SF1004"],
            ["QueryOfArraysEnumeratedInAGenericMethod"] = ["SF1001"],
            ["PlainIteratorOfAnEnumeratorMoved"] = ["SF1004"],
            ["PendingValueOfAPairOfOneType"] = ["SF1005"],
            ["PendingKeyOfAPairOfOneType"] = ["SF1005"],
            ["PendingValueOfAPairFromCreate"] = ["SF1005"],
            ["PendingValueOfAPairWithNamedArguments"] = ["SF1005"],
            ["PendingItemOfATupleOfOneType"] = ["SF1005"],
            ["PendingValueOfADictionaryEntry"] = ["SF1005"],
            ["PendingValueOfAPairByAPattern"] = ["SF1005"],
            ["PendingValueOfAPairFromADeconstruct"] = ["SF1005"],
            ["PendingValueOfAPairFromAVarDeconstruct"] = ["SF1005"],
            ["PendingItemOfATupleFromADeconstruct"] = ["SF1005"],
            ["PendingValueOfAPairByAPositionalPattern"] = ["SF1005"],
            ["PendingValueOfEachPairOfAList"] = ["SF1005"],
            ["PendingValueOfAPairInAList"] = ["SF1005"],
            ["KeyOfAPairThatCanBeFromADictionary"] = ["SF1005"],
            ["KeyOfAPairThatCanBeFromADictionaryAfter"] = ["SF1005"],
            ["KeyOfAPairFromADictionaryInAListOfTwo"] = ["SF1005"],
            ["KeyOfAPairOfCellsThatCanBeFromADictionary"] = ["SF1001"],
            ["KeyOfADictionaryEntrySetAfter"] = ["SF1005"],
            ["KeyOfADictionaryEntryWithPartsSetAfter"] = ["SF1005"],
            ["PendingValueOfAPairOfOneTypeInAHelper"] = ["SF1004"],
            ["PendingValueOfAPairThroughATask"] = ["SF1005"],
            ["PendingValueOfAPairByAVarPattern"] = ["SF1005"],
            ["KeyOfAPairFromADictionaryInAListWithOneMore"] = ["SF1005"],
            ["DeconstructThatCrossesItsParts"] = ["SF1001"],
            ["KeyOfAPairFromADictionaryByADeconstruct"] = ["SF1005"],
            ["OverriddenPropertyOfAValueThatIsOneOfTwo"] = ["SF1001"],
            ["OverriddenPropertyOfOneOfTwoByAPattern"] = ["SF1001"],
            ["PendingValueOfAPairDeconstructedInAHelper"] = ["SF1004"],
            ["PendingElementOfAMixedListByIndex"] = ["SF1001"],
            ["ElementOfAListAfterAnInsert"] = ["SF1001"],
            ["ElementOfAMixedListAfterAReverse"] = ["SF1001"],
            ["ElementOfAnArrayAfterAnAssignment"] = ["SF1001"],
            ["PendingLastOfAMixedListOfObjects"] = ["SF1005"],
            ["ElementOfAListGivenToAHelper"] = ["SF1001"],
            ["PendingElementOfAnArrayFromAnInitializer"] = ["SF1001"],
            ["PendingElementOfAListFromAnInitializer"] = ["SF1001"],
            ["ElementOfAListFromACollectionAndAnInitializer"] = ["SF1001"],
            ["ElementOfAListFromElementsWithBraces"] = ["SF1001"],
            ["FirstOfAConcurrentBag"] = ["SF1001"],
            ["FirstOfAConcurrentBagFromAnInitializer"] = ["SF1001"],
            ["ElementOfAnArraySetThroughARefArgument"] = ["SF1001"],
            ["ElementsOfAnArraySetByADeconstruction"] = ["SF1001"],
            ["ElementOfAMixedArraySetThroughARefLocal"] = ["SF1001"],
            ["ElementOfAMixedArraySetInParentheses"] = ["SF1001"],
            ["ElementOfAnArrayAfterAnIncrement"] = ["SF1001"],
            ["ElementOfAMixedListSetThroughASecondName"] = ["SF1001"],
            ["ReadingFunctionOfAMixedListOfFunctions"] = ["SF1001"],
            ["FirstOfThisCompilationChangesTheList"] = ["SF1001"],
            ["PendingLastOfAMixedSequence"] = ["SF1001"],
            ["ElementOfAListWithASpread"] = ["SF1001"],
            ["AddThroughAnElementOfAListOfLists"] = ["SF1001"],
            ["HelperAddsThroughAnElementOfAListOfLists"] = ["SF1001"],
            ["AddThroughTheFirstOfAListOfLists"] = ["SF1001"],
            ["ElementOfAnElementAfterAnAssignment"] = ["SF1001"],
            ["ElementOfAListFromItsTypeACollectionAndAnInitializer"] = ["SF1001"],
            ["CountOfThisCompilationChangesTheList"] = ["SF1001"],
            ["PropertySetThroughAnElement"] = ["SF1001"],
            ["PropertySetThroughTheFirstElement"] = ["SF1001"],
            ["PendingRowOfAGrid"] = ["SF1001"],
            ["ObjectOfAListFromElementsWithBraces"] = ["SF1005"],
            ["ElementOfAnArrayAfterAnIncrementBeforeIt"] = ["SF1001"],
            ["ElementOfAnArrayAfterADecrement"] = ["SF1001"],
            ["ElementOfAnArrayAfterADecrementBeforeIt"] = ["SF1001"],
            ["KeyOfADictionaryFromAnIndexInTheInitializer"] = ["SF1005"],
            ["KeyOfADictionaryFromAnAssignmentByIndex"] = ["SF1005"],
            ["KeyThroughKeysFromAnAssignmentByIndex"] = ["SF1005"],
            ["CellKeyFromAnAssignmentByIndex"] = ["SF1001"],
            ["KeyOfAHashtableFromAnAssignmentByIndex"] = ["SF1005"],
            ["PendingIndexOfAnIndexerOfThisCompilation"] = ["SF1004"],
            ["VarPatternCallsADeconstructThatReads"] = ["SF1005"],
            ["VarPatternInASwitchCallsADeconstructThatReads"] = ["SF1005"],
            ["PendingPartOfAVarPattern"] = ["SF1001"],
            ["PendingPartOfAPartOfAVarPattern"] = ["SF1001"],
            ["ChangeThroughFind"] = ["SF1001"],
            ["ChangeThroughLastOfAGrownList"] = ["SF1001"],
            ["ChangeThroughElementAt"] = ["SF1001"],
            ["ChangeThroughAForeachVariable"] = ["SF1001"],
            ["PropertySetThroughLast"] = ["SF1001"],
            ["PropertySetThroughAForeachVariable"] = ["SF1001"],
            ["ChangeThroughALocalOfAnElement"] = ["SF1001"],
            ["ChangeThroughALocalThatGetsAnElement"] = ["SF1001"],
            ["ChangeThroughAnElementInACast"] = ["SF1001"],
            ["ChangeThroughAnOutVariable"] = ["SF1001"],
            ["ChangeThroughALambdaParameter"] = ["SF1001"],
            ["ChangeThroughAParameterOfAQueryFunction"] = ["SF1001"],
            ["ChangeThroughValuesOfADictionary"] = ["SF1001"],
            ["HelperOnAForeachVariable"] = ["SF1001"],
            ["QueryInTheContentOfAnXElement"] = ["SF1001"],
            ["QueryInTheContentsOfAnXElement"] = ["SF1001"],
            ["QueryInAnArrayInTheContentOfAnXElement"] = ["SF1001"],
            ["QueryInAnXElementInAnXElement"] = ["SF1001"],
            ["QueryInTheContentOfAnXDocument"] = ["SF1001"],
            ["QueryGivenToAddOfAnXElement"] = ["SF1001"],
            ["QueryGivenToAddOfTheRootOfAnXDocument"] = ["SF1001"],
            ["QueryGivenToAddFirstOfAnXElement"] = ["SF1001"],
            ["QueryGivenToReplaceNodesOfAnXElement"] = ["SF1001"],
            ["QueryGivenToReplaceAllOfAnXElement"] = ["SF1001"],
            ["QueryGivenToReplaceAttributesOfAnXElement"] = ["SF1001"],
            ["QueryGivenToAddAfterSelf"] = ["SF1001"],
            ["QueryGivenToAddBeforeSelf"] = ["SF1001"],
            ["QueryGivenToReplaceWith"] = ["SF1001"],
            ["ObjectInTheContentOfAnXElement"] = ["SF1005"],
            ["ObjectsInTheContentOfAnXElement"] = ["SF1005"],
            ["ObjectGivenToAddOfAnXElement"] = ["SF1005"],
            ["ObjectAsTheValueOfAnXAttribute"] = ["SF1005"],
            ["ObjectGivenToSetValueOfAnXElement"] = ["SF1005"],
            ["ObjectGivenToSetElementValue"] = ["SF1005"],
            ["ObjectGivenToSetAttributeValue"] = ["SF1005"],
            ["QueryInAnXStreamingElementThatMakesText"] = ["SF1001"],
            ["QueryInAnXStreamingElementThatIsSaved"] = ["SF1001"],
            ["QueryAddedToAnXStreamingElementThatMakesText"] = ["SF1001"],
            ["XStreamingElementInTheContentOfAnXElement"] = ["SF1001"],
            ["SetOfOneWhoseHashCodeReads"] = ["SF1005"],
            ["SortOfAnArrayOfTwo"] = ["SF1004", "SF1005"],
            ["SortOfAListOfOneThenOneMore"] = ["SF1004", "SF1005"],
            ["SortOfTwoSpreadsOfAnArrayOfOne"] = ["SF1004", "SF1005"],
            ["SortOfAnArrayOfOneThatIsReplaced"] = ["SF1004", "SF1005"],
            ["SortOfAnArrayOfOneThatIsResized"] = ["SF1004", "SF1005"],
            ["SetOfOneThenOneMore"] = ["SF1005"],
            ["SetFromAnArrayAndAnInitializer"] = ["SF1005"],
            ["ContainsInAListOfOne"] = ["SF1005"],
            ["SetOfTwoFromACollectionExpression"] = ["SF1005"],
            ["KeyComparedWithAKeyOfTheDictionaryAtAnAssignment"] = ["SF1005"],
            ["KeyComparedWithAKeyOfTheDictionaryAtARead"] = ["SF1005"],
            ["SetFromASpreadAndOneElement"] = ["SF1005"],
            ["ListContainsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["ListIndexOfOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["ListRemoveOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["ArrayIndexOfOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["QueryContainsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SequenceEqualOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SetContainsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SetAddOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryContainsKeyOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryTryGetValueOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryAssignmentOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryAddOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["StaticEqualsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["TupleEqualsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["ListBinarySearchOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["ArrayBinarySearchOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SortedListAddOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DefaultComparerOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DefaultEqualityComparerOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SortedSetAddOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SortedSetContainsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SortedDictionaryAddOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SortedDictionaryAssignmentOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SpanContainsOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["PriorityQueueEnqueueOfAPendingPriority"] = ["SF1005"],
            ["FieldListContainsAPendingValueWhoseEqualsReadsTheOther"] = ["SF1004"],
            ["ObjectGivenToAddFirstOfAnXElement"] = ["SF1005"],
            ["ObjectGivenToReplaceNodesOfAnXElement"] = ["SF1005"],
            ["ObjectGivenToReplaceAllOfAnXElement"] = ["SF1005"],
            ["ObjectGivenToSetValueOfAnXAttribute"] = ["SF1005"],
            ["QueryInAnXStreamingElementThatIsWritten"] = ["SF1001"],
            ["SequenceOfThisCompilationInTheContentOfAnXElement"] = ["SF1005"],
            ["SetInitializerWhoseEqualsReadsTheOther"] = ["SF1004"],
            ["ListLastIndexOfOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["SetRemoveOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryRemoveOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryReadOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["TupleCompareToOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["DictionaryAddOfAKeyOwnStateWhereTheReceiverIsPending"] = ["SF1005"],
            ["ChangeAndReadThroughAForeachVariable"] = ["SF1001"],
            ["ChangeThroughALocalThatGetsItsOwnElement"] = ["SF1001"],
            ["SpanContainsAPlainValueWhereTheElementIsPending"] = ["SF1004"],
            ["QueryInAnArrayThatIsTheContentOfAnXDocument"] = ["SF1001"],
            ["PriorityQueueOfTwoComparesThePriorities"] = ["SF1005"],
            ["AssignmentByIndexThroughALocalThatGetsItsOwnElement"] = ["SF1001"],
            ["AssignmentByIndexThroughTwoLocalsThatOwnEachOther"] = ["SF1001"],
            ["PendingValueThatAnIndexerOfThisCompilationKeeps"] = ["SF1001"],
            ["SetFromACollectionExpressionOfTwoInTheCall"] = ["SF1004", "SF1005"],
            ["SetFromASpreadOfTwoInTheCall"] = ["SF1004", "SF1005"],
            ["SetFromASpreadAndOneElementInTheCall"] = ["SF1004", "SF1005"],
            ["HashSetFromACollectionExpressionOfTwoInTheCall"] = ["SF1004", "SF1005"],
            ["PriorityQueueFromTwoPairsInTheCall"] = ["SF1005"],
            ["JoinOfACollectionExpressionInTheCall"] = ["SF1005"],
            ["ConcatOfACollectionExpressionInTheCall"] = ["SF1005"],
            ["ContainsInACollectionExpressionInTheCall"] = ["SF1005"],
            ["OrderOfACollectionExpressionBehindACast"] = ["SF1004", "SF1005"],
            ["SetFromAConditionalOfCollectionExpressionsInTheCall"] = ["SF1004", "SF1005"],
            ["ConcatOfQueriesInACollectionExpression"] = ["SF1001"],
            ["SetFromASwitchOfCollectionExpressionsInTheCall"] = ["SF1004", "SF1005"],
            ["SetFromACollectionExpressionInParenthesesInTheCall"] = ["SF1004", "SF1005"],
            ["SetFromAConditionalOfOneOrTwoElementsInTheCall"] = ["SF1004", "SF1005"],
            ["SetFromASwitchOfOneOrTwoElementsInTheCall"] = ["SF1004", "SF1005"],
            ["SetFromASpreadOfAnArrayThatIsReplaced"] = ["SF1004", "SF1005"],
            ["JoinOfOnlyAQuery"] = ["SF1001"],
            ["JoinOfAnObjectAndOneMoreValue"] = ["SF1005"],
            ["JoinOfOneArrayOfObjects"] = ["SF1005"],
            ["FormatOfAnObjectAmongFourValues"] = ["SF1005"],
            ["QueryAsOneOfTwoContentsOfAnXElement"] = ["SF1001"],
            ["ConcatOfTwoQueriesThatIsEnumerated"] = ["SF1001"],
            ["SpanContainsOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanContainsOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanIndexOfOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanIndexOfOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanIndexOfASpanOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanIndexOfASpanOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanLastIndexOfOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanLastIndexOfOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyExceptOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyExceptOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyExceptOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyExceptOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanContainsAnyOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanContainsAnyOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanContainsAnyExceptOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanContainsAnyExceptOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanCountOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanCountOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanCountAnyOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanCountAnyOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanSequenceEqualOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanSequenceEqualOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanStartsWithOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanStartsWithOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanEndsWithOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanEndsWithOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanCommonPrefixLengthOfAnEquatableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanCommonPrefixLengthOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanReplaceOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanReplaceOfAnObjectWhereTheElementIsPending"] = ["SF1005"],
            ["SpanTrimOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanTrimStartOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanTrimEndOfAnEquatableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanBinarySearchOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanSequenceCompareToOfAComparableWhereTheElementIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyInRangeOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyInRangeOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyExceptInRangeOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyExceptInRangeOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanContainsAnyInRangeOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanContainsAnyExceptInRangeOfAComparableWhereTheValueIsPending"] = ["SF1005"],
            ["SpanSortOfTwo"] = ["SF1005"],
            ["SpanSortOfObjectKeysAndItems"] = ["SF1005"],
            ["SpanInALocalContainsWhereTheElementIsPending"] = ["SF1004"],
            ["ChangeThroughALocalThatAListLiteralHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAListGotWithAdd"] = ["SF1001"],
            ["ChangeThroughALocalThatAnElementGot"] = ["SF1001"],
            ["ChangeThroughALocalThatAnArrayInitializerHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAnArrayCreationHas"] = ["SF1001"],
            ["ChangeThroughALocalThatACollectionInitializerHas"] = ["SF1001"],
            ["ChangeThroughALocalThatADictionaryGotByIndex"] = ["SF1001"],
            ["ChangeThroughALocalThatADictionaryGotWithAdd"] = ["SF1001"],
            ["ChangeThroughALocalThatADictionaryInitializerHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAPairInADictionaryInitializerHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAnAssignedLiteralHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAListGotWithInsert"] = ["SF1001"],
            ["ChangeThroughALocalThatAStackGot"] = ["SF1001"],
            ["ChangeThroughALocalThatAQueueGot"] = ["SF1001"],
            ["ChangeThroughALocalThatAnInnerLiteralHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAnAddedLiteralHas"] = ["SF1001"],
            ["ChangeThroughALocalThatTwoListsHaveReadFromTheFirst"] = ["SF1001"],
            ["ChangeThroughALocalThatTwoListsHaveReadFromTheSecond"] = ["SF1001"],
            ["ChangeThroughALocalThatAListInAListHas"] = ["SF1001"],
            ["ListGetsAnObjectAfterTheChange"] = ["SF1001"],
            ["RowFilledAfterTheRowsGotIt"] = ["SF1001"],
            ["RowFilledAfterALiteralOfRowsGotIt"] = ["SF1001"],
            ["HelperChangesALocalThatAListHas"] = ["SF1001"],
            ["ChangeThroughAParameterThatAListHas"] = ["SF1001"],
            ["ChangeThroughALocalThatALiteralInALiteralOfLocalsHas"] = ["SF1001"],
            ["ListsThatHaveEachOtherGetAValue"] = ["SF1001"],
            ["ChangeThroughALocalThatAListTypeOfThisCompilationGot"] = ["SF1001"],
            ["SetFromAConditionalWhereTheSecondResultHasTwoElements"] = ["SF1004", "SF1005"],
            ["ChangeThroughALocalThatAPriorityQueueGot"] = ["SF1001"],
            ["ChangeThroughALocalThatAnArrayCreationWithATypeHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAnObjectCreationWithATypeHas"] = ["SF1001"],
            ["SpanSequenceCompareToOfAPlainComparableWhereTheElementIsPending"] = ["SF1005"],
            ["ListSortWithANullComparer"] = ["SF1004", "SF1005"],
            ["ArraySortWithANullComparer"] = ["SF1004", "SF1005"],
            ["ArraySortWithADefaultComparer"] = ["SF1004", "SF1005"],
            ["ArraySortWithTheDefaultLiteral"] = ["SF1004", "SF1005"],
            ["SortedSetWithANullComparer"] = ["SF1004", "SF1005"],
            ["HashSetWithANullComparer"] = ["SF1004", "SF1005"],
            ["OrderByWithANullComparer"] = ["SF1004", "SF1005"],
            ["ContainsWithANullComparer"] = ["SF1005"],
            ["SpanContainsWithANullComparer"] = ["SF1005"],
            ["ListSortWithANullComparerThatSaysNotNull"] = ["SF1004", "SF1005"],
            ["ListInAPropertyOfALocalObjectGetsAPendingCell"] = ["SF1001"],
            ["ListInAPropertyOfALocalObjectGetsAPendingObject"] = ["SF1001"],
            ["ListInAFieldOfALocalObjectGetsAPendingCell"] = ["SF1001"],
            ["ListInASetPropertyOfALocalObjectGetsAPendingCell"] = ["SF1001"],
            ["ListInAPropertyOfAPropertyGetsAPendingCell"] = ["SF1001"],
            ["ElementOfAListInAPropertyGetsAPendingCell"] = ["SF1001"],
            ["DictionaryInAPropertyGetsAPendingCell"] = ["SF1001"],
            ["MethodOfTheObjectReadsTheListThatTheCodeFilled"] = ["SF1005"],
            ["ChangeThroughALocalThatAListOfAnObjectHas"] = ["SF1001"],
            ["ListInAPropertyOfAnElementGetsAPendingCell"] = ["SF1001"],
            ["HashSetExceptWith"] = ["SF1005"],
            ["SortedSetExceptWith"] = ["SF1004"],
            ["HashSetIntersectWith"] = ["SF1004", "SF1005"],
            ["SortedSetIntersectWith"] = ["SF1004"],
            ["HashSetIsProperSubsetOf"] = ["SF1005"],
            ["SortedSetIsProperSubsetOf"] = ["SF1004"],
            ["HashSetIsProperSupersetOf"] = ["SF1005"],
            ["SortedSetIsProperSupersetOf"] = ["SF1004"],
            ["HashSetIsSubsetOf"] = ["SF1005"],
            ["SortedSetIsSubsetOf"] = ["SF1004"],
            ["HashSetIsSupersetOf"] = ["SF1005"],
            ["SortedSetIsSupersetOf"] = ["SF1004"],
            ["HashSetOverlaps"] = ["SF1005"],
            ["SortedSetOverlaps"] = ["SF1004"],
            ["HashSetSetEquals"] = ["SF1005"],
            ["SortedSetSetEquals"] = ["SF1004"],
            ["HashSetSymmetricExceptWith"] = ["SF1004", "SF1005"],
            ["SortedSetSymmetricExceptWith"] = ["SF1004"],
            ["HashSetUnionWith"] = ["SF1004", "SF1005"],
            ["SortedSetUnionWith"] = ["SF1004"],
            ["HashSetTryGetValue"] = ["SF1005"],
            ["SortedSetRemove"] = ["SF1004"],
            ["SortedSetTryGetValue"] = ["SF1004"],
            ["SortedSetGetViewBetween"] = ["SF1004", "SF1004"],
            ["DictionaryConstructor"] = ["SF1004", "SF1005"],
            ["DictionaryContainsValue"] = ["SF1005"],
            ["SortedDictionaryConstructor"] = ["SF1004", "SF1005"],
            ["SortedDictionaryContainsKey"] = ["SF1004"],
            ["SortedListContainsKey"] = ["SF1005"],
            ["SortedDictionaryRemove"] = ["SF1004"],
            ["SortedListRemove"] = ["SF1005"],
            ["SortedDictionaryTryGetValue"] = ["SF1004"],
            ["SortedDictionaryRead"] = ["SF1004"],
            ["SortedDictionaryContainsValue"] = ["SF1005"],
            ["SortedListConstructor"] = ["SF1004", "SF1005"],
            ["SortedListTryGetValue"] = ["SF1005"],
            ["SortedListIndexOfKey"] = ["SF1005"],
            ["SortedListRead"] = ["SF1005"],
            ["SortedListAssignment"] = ["SF1005"],
            ["SortedListContainsValue"] = ["SF1005"],
            ["SortedListIndexOfValue"] = ["SF1005"],
            ["LinkedListContains"] = ["SF1005"],
            ["LinkedListFind"] = ["SF1005"],
            ["LinkedListFindLast"] = ["SF1005"],
            ["LinkedListRemove"] = ["SF1005"],
            ["QueueContains"] = ["SF1005"],
            ["StackContains"] = ["SF1005"],
            ["PriorityQueueDequeueEnqueue"] = ["SF1004"],
            ["ArrayLastIndexOf"] = ["SF1005"],
            ["OrderedDictionaryConstructor"] = ["SF1004", "SF1005"],
            ["OrderedDictionaryAdd"] = ["SF1005"],
            ["OrderedDictionaryContainsKey"] = ["SF1005"],
            ["OrderedDictionaryIndexOf"] = ["SF1005"],
            ["OrderedDictionaryRemove"] = ["SF1005"],
            ["OrderedDictionaryInsert"] = ["SF1005"],
            ["OrderedDictionarySetAt"] = ["SF1004", "SF1005"],
            ["OrderedDictionaryTryAdd"] = ["SF1005"],
            ["OrderedDictionaryTryGetValue"] = ["SF1005"],
            ["OrderedDictionaryRead"] = ["SF1005"],
            ["OrderedDictionaryAssignment"] = ["SF1005"],
            ["OrderedDictionaryContainsValue"] = ["SF1004"],
            ["ConcurrentDictionaryConstructor"] = ["SF1004", "SF1005"],
            ["ConcurrentDictionaryAddOrUpdate"] = ["SF1005"],
            ["ConcurrentDictionaryContainsKey"] = ["SF1005"],
            ["ConcurrentDictionaryGetOrAdd"] = ["SF1005"],
            ["ConcurrentDictionaryTryAdd"] = ["SF1005"],
            ["ConcurrentDictionaryTryGetValue"] = ["SF1005"],
            ["ConcurrentDictionaryTryRemove"] = ["SF1005"],
            ["ConcurrentDictionaryTryUpdate"] = ["SF1005"],
            ["ConcurrentDictionaryRead"] = ["SF1005"],
            ["ConcurrentDictionaryAssignment"] = ["SF1005"],
            ["CollectionContains"] = ["SF1005"],
            ["CollectionIndexOf"] = ["SF1005"],
            ["CollectionRemove"] = ["SF1005"],
            ["ReadOnlyCollectionContains"] = ["SF1005"],
            ["ReadOnlyCollectionIndexOf"] = ["SF1005"],
            ["ReadOnlyDictionaryContainsKey"] = ["SF1005"],
            ["ReadOnlyDictionaryTryGetValue"] = ["SF1005"],
            ["ReadOnlyDictionaryRead"] = ["SF1005"],
            ["CollectionExtensionsGetValueOrDefault"] = ["SF1005"],
            ["CollectionExtensionsRemove"] = ["SF1005"],
            ["CollectionExtensionsTryAdd"] = ["SF1005"],
            ["EnumerableExceptBy"] = ["SF1004", "SF1005"],
            ["EnumerableIntersectBy"] = ["SF1004", "SF1005"],
            ["EnumerableUnionBy"] = ["SF1004", "SF1005"],
            ["EnumerableGroupJoin"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["EnumerableLeftJoin"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["EnumerableRightJoin"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["EnumerableCountBy"] = ["SF1004", "SF1005"],
            ["EnumerableAggregateBy"] = ["SF1004", "SF1005"],
            ["ImmutableHashSetAdd"] = ["SF1004"],
            ["ImmutableSortedSetAdd"] = ["SF1004"],
            ["ImmutableHashSetContains"] = ["SF1004"],
            ["ImmutableSortedSetContains"] = ["SF1004"],
            ["ImmutableHashSetRemove"] = ["SF1004", "SF1005"],
            ["ImmutableSortedSetRemove"] = ["SF1004"],
            ["ImmutableHashSetTryGetValue"] = ["SF1004"],
            ["ImmutableSortedSetTryGetValue"] = ["SF1004"],
            ["ImmutableSortedSetIndexOf"] = ["SF1004"],
            ["ImmutableHashSetExcept"] = ["SF1004", "SF1005"],
            ["ImmutableSortedSetExcept"] = ["SF1004"],
            ["ImmutableHashSetIntersect"] = ["SF1004"],
            ["ImmutableSortedSetIntersect"] = ["SF1004"],
            ["ImmutableHashSetIsProperSubsetOf"] = ["SF1004"],
            ["ImmutableSortedSetIsProperSubsetOf"] = ["SF1004"],
            ["ImmutableHashSetIsProperSupersetOf"] = ["SF1004"],
            ["ImmutableSortedSetIsProperSupersetOf"] = ["SF1004"],
            ["ImmutableHashSetIsSubsetOf"] = ["SF1004"],
            ["ImmutableSortedSetIsSubsetOf"] = ["SF1004"],
            ["ImmutableHashSetIsSupersetOf"] = ["SF1004"],
            ["ImmutableSortedSetIsSupersetOf"] = ["SF1004"],
            ["ImmutableHashSetOverlaps"] = ["SF1004"],
            ["ImmutableSortedSetOverlaps"] = ["SF1004"],
            ["ImmutableHashSetSetEquals"] = ["SF1004"],
            ["ImmutableSortedSetSetEquals"] = ["SF1004", "SF1005"],
            ["ImmutableHashSetSymmetricExcept"] = ["SF1004", "SF1005"],
            ["ImmutableSortedSetSymmetricExcept"] = ["SF1004", "SF1005"],
            ["ImmutableHashSetUnion"] = ["SF1004"],
            ["ImmutableSortedSetUnion"] = ["SF1004"],
            ["ImmutableHashSetCreate"] = ["SF1004", "SF1005"],
            ["ImmutableHashSetCreateRange"] = ["SF1004", "SF1005"],
            ["ImmutableHashSetToImmutableHashSet"] = ["SF1004", "SF1005"],
            ["ImmutableSortedSetCreate"] = ["SF1004", "SF1005"],
            ["ImmutableSortedSetCreateRange"] = ["SF1004", "SF1005"],
            ["ImmutableSortedSetToImmutableSortedSet"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryAdd"] = ["SF1004"],
            ["ImmutableDictionaryAddRange"] = ["SF1004", "SF1004", "SF1005"],
            ["ImmutableDictionaryContains"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryContainsKey"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryRemove"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryRemoveRange"] = ["SF1004", "SF1005"],
            ["ImmutableDictionarySetItem"] = ["SF1004"],
            ["ImmutableDictionarySetItems"] = ["SF1004", "SF1004", "SF1005"],
            ["ImmutableDictionaryTryGetKey"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryTryGetValue"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryRead"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryContainsValue"] = ["SF1004"],
            ["ImmutableDictionaryCreateRange"] = ["SF1004", "SF1005"],
            ["ImmutableDictionaryToImmutableDictionary"] = ["SF1004", "SF1005"],
            ["ImmutableSortedDictionaryAdd"] = ["SF1004"],
            ["ImmutableSortedDictionaryAddRange"] = ["SF1004"],
            ["ImmutableSortedDictionaryContains"] = ["SF1004"],
            ["ImmutableSortedDictionaryContainsKey"] = ["SF1004"],
            ["ImmutableSortedDictionaryRemove"] = ["SF1004"],
            ["ImmutableSortedDictionaryRemoveRange"] = ["SF1004"],
            ["ImmutableSortedDictionarySetItem"] = ["SF1004"],
            ["ImmutableSortedDictionarySetItems"] = ["SF1004"],
            ["ImmutableSortedDictionaryTryGetKey"] = ["SF1004"],
            ["ImmutableSortedDictionaryTryGetValue"] = ["SF1004"],
            ["ImmutableSortedDictionaryRead"] = ["SF1004"],
            ["ImmutableSortedDictionaryContainsValue"] = ["SF1004"],
            ["ImmutableSortedDictionaryCreateRange"] = ["SF1004", "SF1005"],
            ["ImmutableSortedDictionaryToImmutableSortedDictionary"] = ["SF1004", "SF1005"],
            ["ImmutableSortedDictionaryValueRef"] = ["SF1004"],
            ["ImmutableListContains"] = ["SF1004"],
            ["ImmutableListIndexOf"] = ["SF1004"],
            ["ImmutableListRemove"] = ["SF1004"],
            ["ImmutableListRemoveRange"] = ["SF1004"],
            ["ImmutableListReplace"] = ["SF1004"],
            ["ImmutableListSort"] = ["SF1004", "SF1005"],
            ["ImmutableArrayContains"] = ["SF1005"],
            ["ImmutableArrayIndexOf"] = ["SF1005"],
            ["ImmutableArrayRemove"] = ["SF1005"],
            ["ImmutableArrayRemoveRange"] = ["SF1004", "SF1005"],
            ["ImmutableArrayReplace"] = ["SF1005"],
            ["ImmutableArraySort"] = ["SF1004", "SF1005"],
            ["ImmutableArrayLastIndexOf"] = ["SF1005"],
            ["ImmutableListBinarySearch"] = ["SF1004"],
            ["FrozenSetContains"] = ["SF1004"],
            ["FrozenSetIsProperSubsetOf"] = ["SF1004"],
            ["FrozenSetIsProperSupersetOf"] = ["SF1004"],
            ["FrozenSetIsSubsetOf"] = ["SF1004"],
            ["FrozenSetIsSupersetOf"] = ["SF1004"],
            ["FrozenSetOverlaps"] = ["SF1004"],
            ["FrozenSetSetEquals"] = ["SF1004"],
            ["FrozenSetTryGetValue"] = ["SF1004"],
            ["FrozenSetToFrozenSet"] = ["SF1004", "SF1005"],
            ["FrozenDictionaryContainsKey"] = ["SF1005"],
            ["FrozenDictionaryTryGetValue"] = ["SF1005"],
            ["FrozenDictionaryRead"] = ["SF1005"],
            ["FrozenDictionaryGetValueRefOrNullRef"] = ["SF1005"],
            ["FrozenDictionaryToFrozenDictionary"] = ["SF1004", "SF1005"],
            ["ArrayListContains"] = ["SF1005"],
            ["ArrayListIndexOf"] = ["SF1005"],
            ["ArrayListLastIndexOf"] = ["SF1005"],
            ["ArrayListRemove"] = ["SF1005"],
            ["HashtableAdd"] = ["SF1005"],
            ["HashtableContains"] = ["SF1005"],
            ["HashtableContainsKey"] = ["SF1005"],
            ["HashtableRemove"] = ["SF1005"],
            ["HashtableRead"] = ["SF1005"],
            ["HashtableAssignment"] = ["SF1005"],
            ["HashtableContainsValue"] = ["SF1005"],
            ["EqualityComparerGetHashCode"] = ["SF1005"],
            ["HashCodeCombine"] = ["SF1005"],
            ["SpanIndexOfAnyOfThreeValuesWhereTheValueIsPending"] = ["SF1005"],
            ["SpanIndexOfAnyOfASpanOfValuesWhereTheElementIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyOfThreeValuesWhereTheValueIsPending"] = ["SF1005"],
            ["SpanLastIndexOfAnyOfASpanOfValuesWhereTheElementIsPending"] = ["SF1005"],
            ["SpanContainsAnyOfThreeValuesWhereTheValueIsPending"] = ["SF1005"],
            ["SpanContainsAnyOfASpanOfValuesWhereTheElementIsPending"] = ["SF1005"],
            ["SpanReplaceOfAnEquatableWhereTheOldValueIsPending"] = ["SF1005"],
            ["ListInAnElementOfATupleGetsAPendingCell"] = ["SF1001"],
            ["ListInItem1OfATupleGetsAPendingCell"] = ["SF1001"],
            ["ListInTheValueOfAPairGetsAPendingCell"] = ["SF1001"],
            ["ListSortWithANullComparerInALocal"] = ["SF1004", "SF1005"],
            ["ArraySortWithADefaultComparerInALocal"] = ["SF1004", "SF1005"],
            ["ListSortWithANullComparerAssignedLater"] = ["SF1004", "SF1005"],
            ["ListSortWithANullComparerInALocalThatSaysNotNull"] = ["SF1004", "SF1005"],
            ["HashSetWithANullComparerInALocal"] = ["SF1004", "SF1005"],
            ["OrderByWithANullComparerInALocal"] = ["SF1004", "SF1005"],
            ["ListSortWithAConstantNullComparer"] = ["SF1004", "SF1005"],
            ["SortedSetWithANullComparerInALocalThenAdd"] = ["SF1004"],
            ["AddThroughISetOfAHashSet"] = ["SF1005"],
            ["AddThroughISetOfASortedSet"] = ["SF1004"],
            ["AddThroughIDictionaryOfADictionary"] = ["SF1005"],
            ["IndexerThroughIDictionaryOfADictionary"] = ["SF1005"],
            ["AddThroughICollectionOfAHashSet"] = ["SF1004", "SF1005"],
            ["ContainsThroughICollectionOfAList"] = ["SF1005"],
            ["IndexOfThroughIListOfAList"] = ["SF1005"],
            ["IndexOfThroughIListOfAnArray"] = ["SF1004", "SF1005"],
            ["ContainsKeyThroughIReadOnlyDictionaryOfADictionary"] = ["SF1005"],
            ["ContainsThroughIListOfAnArrayList"] = ["SF1005"],
            ["AddThroughIDictionaryOfAHashtable"] = ["SF1005"],
            ["AddThroughACastToISet"] = ["SF1005"],
            ["ContainsThroughILookup"] = ["SF1004", "SF1005"],
            ["AddThroughISetFromAMethod"] = ["SF1004", "SF1005"],
            ["ContainsThroughICollectionInALocalFunction"] = ["SF1004"],
            ["TryGetValueThroughIDictionaryFromAMethod"] = ["SF1004", "SF1005"],
            ["RemoveThroughICollectionFromAMethod"] = ["SF1004", "SF1005"],
            ["OverlapsThroughIReadOnlySetFromAMethod"] = ["SF1005"],
            ["RemoveWhereOfAHashSet"] = ["SF1004", "SF1005"],
            ["RemoveWhereOfASortedSet"] = ["SF1004", "SF1005"],
            ["RemoveOfAPriorityQueue"] = ["SF1004"],
            ["EqualsOfATupleObject"] = ["SF1005"],
            ["GetHashCodeOfATupleObject"] = ["SF1005"],
            ["CompareToOfATupleObjectThroughAnInterface"] = ["SF1005"],
            ["HashSetOfTupleObjects"] = ["SF1005"],
            ["TryUpdateComparesTheValues"] = ["SF1005"],
            ["AddOfAHashCode"] = ["SF1005"],
            ["SortOfAnArrayList"] = ["SF1004", "SF1005"],
            ["BinarySearchOfAnArrayList"] = ["SF1005"],
            ["HashtableFromADictionary"] = ["SF1005"],
            ["LinkedListContainsWhereTheElementIsPending"] = ["SF1005"],
            ["ImmutableListContainsWhereTheValueIsPending"] = ["SF1005"],
            ["PriorityQueueEnqueueDequeueReadsThePriorityInAPair"] = ["SF1004"],
            ["ContainsThroughIDictionaryOfADictionary"] = ["SF1004", "SF1005"],
            ["AddThroughIImmutableSetFromAMethod"] = ["SF1004"],
            ["AddThroughIImmutableDictionaryFromAMethod"] = ["SF1004"],
            ["ContainsKeyThroughIReadOnlyDictionaryFromAMethod"] = ["SF1005"],
            ["AddThroughICollectionOfASetFromAHelper"] = ["SF1004", "SF1005"],
            ["AddThroughICollectionOfASetFromAHelperWithALocal"] = ["SF1004", "SF1005"],
            ["AddThroughICollectionOfASetFromAHelperWithTwoReturns"] = ["SF1004", "SF1005"],
            ["AddThroughICollectionOfASetThroughAHelperThatReturnsItsParameter"] = ["SF1004", "SF1005"],
            ["AddThroughICollectionOfASetFromALocalFunction"] = ["SF1004", "SF1005"],
            ["ContainsThroughICollectionOfAListFromAHelperWhereTheElementIsPending"] = ["SF1005"],
            ["AddThroughISetOfAnUnknownType"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["TryGetValueThroughIDictionaryOfAnUnknownType"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["OverlapsThroughIReadOnlySetOfAnUnknownType"] = ["SF1004", "SF1005"],
            ["AddThroughIImmutableSetOfAnUnknownType"] = ["SF1004", "SF1005"],
            ["AddThroughIImmutableDictionaryOfAnUnknownType"] = ["SF1004", "SF1004", "SF1005"],
            ["ContainsKeyThroughIReadOnlyDictionaryOfAnUnknownType"] = ["SF1004", "SF1005"],
            ["AddThroughISetOfARecursiveHelper"] = ["SF1004", "SF1004", "SF1005", "SF1005"],
            ["ListSortWithANullComparerParameter"] = ["SF1004"],
            ["ListSortWithADefaultComparerParameter"] = ["SF1004"],
            ["ListSortWithAnOptionalComparerParameter"] = ["SF1004"],
            ["ListSortWithANullComparerParameterByName"] = ["SF1004"],
            ["ListSortWithANullComparerThroughTwoHelpers"] = ["SF1004"],
            ["ListSortWithANullComparerInALocalGivenToAParameter"] = ["SF1004"],
            ["ListSortWithANullComparerParameterInALocalFunction"] = ["SF1004"],
            ["RemoveOfAQueueWhoseElementAndPriorityHaveOneTypeWithAPendingElement"] = ["SF1004"],
            ["EnqueueOfAQueueWhoseElementAndPriorityHaveOneTypeWithAPendingPriority"] = ["SF1004"],
            ["ContainsOfAPairThroughICollectionOfADictionary"] = ["SF1004", "SF1005"],
            ["RemoveOfAPairThroughICollectionOfADictionary"] = ["SF1005"],
            ["ContainsOfAPairThroughICollectionOfADictionaryWhereTheValueIsPending"] = ["SF1004", "SF1005"],
            ["AddOfAPairThroughICollectionOfADictionary"] = ["SF1005"],
            ["ContainsOfAPairThroughICollectionOfASortedDictionary"] = ["SF1004", "SF1005"],
            ["ContainsOfAPairInAList"] = ["SF1005"],
            ["ContainsOfAPairWithAPendingKeyThroughICollectionOfADictionary"] = ["SF1004", "SF1005"],
            ["ContainsOfAPairWithAPendingValueThroughICollectionOfADictionary"] = ["SF1004", "SF1005"],
            ["CompareToTwoHelpersDown"] = ["SF1004"],
            ["SortTwoHelpersDown"] = ["SF1004"],
            ["SortWithANullComparerTwoHelpersDown"] = ["SF1004"],
            ["SortWithANullComparerInALocalFunctionOfAHelper"] = ["SF1004"],
            ["SortThreeHelpersDown"] = ["SF1003"],
            ["CompareToThreeHelpersDown"] = ["SF1003"],
            ["GetterTwoHelpersDown"] = ["SF1004"],
            ["GetterThreeHelpersDown"] = ["SF1003"],
            ["GetterInAConstructorTwoHelpersDown"] = ["SF1003"],
            ["GetOrAddWithAFunctionForTheValue"] = ["SF1005"],
            ["GetOrAddWithAFunctionAndItsArgument"] = ["SF1005"],
            ["AddOrUpdateWithTwoFunctions"] = ["SF1005"],
            ["GetOrAddWithAFunctionWhereTheGivenKeyIsPending"] = ["SF1004"],
            ["ExceptByComparesTheKeysWithTheSecondSequence"] = ["SF1004", "SF1005"],
            ["IntersectByComparesTheKeysWithTheSecondSequence"] = ["SF1004", "SF1005"],
            ["SetExpressionWhereTheFirstElementIsPending"] = ["SF1005"],
            ["SetExpressionOfThreeWhereTheSecondElementIsPending"] = ["SF1005"],
            ["SetInitializerWhereTheFirstElementIsPending"] = ["SF1005"],
            ["DictionaryInitializerWhereTheFirstKeyIsPending"] = ["SF1005"],
            ["DictionaryIndexInitializerWhereTheFirstKeyIsPending"] = ["SF1005"],
            ["SortedSetExpressionWhereTheSecondElementIsPending"] = ["SF1005"],
            ["IsSubsetOfAHashSetArgument"] = ["SF1005"],
            ["IsProperSubsetOfWhereTheElementIsPending"] = ["SF1005"],
            ["IsSubsetOfAnArgumentOfAnUnknownTypeThatIsASet"] = ["SF1005"],
            ["IsSubsetOfAnArgumentOfAnUnknownTypeThatIsAnArray"] = ["SF1005"],
            ["IsSubsetOfAHashSetArgumentThatGotAComparer"] = ["SF1005"],
            ["OverlapsOfASortedSetArgumentWhereTheElementIsPending"] = ["SF1005"],
            ["UnionOfAnImmutableHashSetComparesTheGivenValues"] = ["SF1004"],
            ["UnionWithOfASortedSetComparesTheGivenValues"] = ["SF1004"],
            ["IntersectWithOfAHashSetComparesItsElements"] = ["SF1004"],
            ["ChangeThroughASecondLocal"] = ["SF1001"],
            ["ChangeThroughTheFirstLocalReadThroughTheSecond"] = ["SF1001"],
            ["ChangeThroughAnElementReadThroughTheLocalThatTheListGot"] = ["SF1001"],
            ["ChangeThroughAnElementReadThroughALocalOfThatElement"] = ["SF1001"],
            ["ChangeThroughAForeachVariableReadThroughTheLocal"] = ["SF1001"],
            ["ChangeThroughALocalThatATupleHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAKeyValuePairHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAnObjectHas"] = ["SF1001"],
            ["ChangeThroughALocalThatAHelperPutIntoAList"] = ["SF1001"],
            ["ChangeThroughALocalThatACopyOfTheListHas"] = ["SF1001"],
            ["ChangeThroughALocalThatASpreadOfTheListHas"] = ["SF1001"],
            ["ListInAPropertyThroughASecondLocal"] = ["SF1001"],
            ["ListThatAConstructorGotChangesLater"] = ["SF1001"],
            ["ListGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["ListInAListGetsAPendingCellThroughItsLocal"] = ["SF1001"],
            ["ListGetsAPendingCellThroughAnElementOfAList"] = ["SF1001"],
            ["DictionaryGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["ArrayGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["MemberSetUnderAConditionThatDoesNotRun"] = ["SF1001"],
            ["MemberSetThroughASecondNameUnderACondition"] = ["SF1001"],
            ["HelperUnderAConditionThatDoesNotRunSetsAPlainCell"] = ["SF1001"],
            ["HelperSetsAPendingCellThroughASecondName"] = ["SF1001"],
            ["ObjectThatAHelperMadeUnderTwoNames"] = ["SF1001"],
            ["ObjectThatThePlatformMadeUnderTwoNames"] = ["SF1001"],
            ["CopyOfAListUnderTwoNames"] = ["SF1001"],
            ["CastOfAListInAMember"] = ["SF1001"],
            ["FirstObjectOfTwoCallsOfOneHelper"] = ["SF1001"],
            ["ObjectThatAHelperMadeAndPutIntoAList"] = ["SF1001"],
            ["GetterInAConstructorThreeHelpersDown"] = ["SF1003"],
            ["IsSubsetOfAHashSetArgumentWithADifferentComparer"] = ["SF1005"],
            ["IsSubsetOfAHashSetArgumentThatGotADifferentSet"] = ["SF1005"],
            ["IsSubsetOfASetFromAFactoryWithADifferentComparer"] = ["SF1005"],
            ["IsSubsetOfAHashSetWhereTheArgumentIsAnImmutableHashSet"] = ["SF1005"],
            ["IsSubsetOfAnArgumentWhoseComparerIsInALocal"] = ["SF1005"],
            ["IsSubsetOfASetFromAHelperWithADifferentComparer"] = ["SF1005"],
            ["OverlapsOfASortedSetWhereTheArgumentIsAViewOfASetWithAComparer"] = ["SF1004"],
            ["ElementThatIsNoObjectOfTheStoreBesideOneThatIs"] = ["SF1001"],
            ["NameOfAnObjectOfTheStoreOrOfAnObjectOfALoop"] = ["SF1001"],
            ["PlainThroughANameOfTwoObjectsWhereTheOtherIsPending"] = ["SF1001"],
            ["ArrayCreationGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["ArrayCreationWithNoTypeGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["ArrayCreationWithALengthGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["ChangeThroughAnElementFromACallReadThroughTheLocal"] = ["SF1001"],
            ["ListThatARecordGotChangesLater"] = ["SF1001"],
            ["ListThatAPrimaryConstructorGotChangesLater"] = ["SF1005"],
            ["ListInAMemberOfAnObjectReadInAHelper"] = ["SF1004"],
            ["ChangeThroughALocalReadThroughAPairOfADictionary"] = ["SF1001"],
            ["ListOfCellsOfCellsGetsAPendingContentThroughASecondLocal"] = ["SF1001"],
            ["ListGetsAPendingCellThroughALocalOfAnInterface"] = ["SF1001"],
            ["ChangeThroughTheResultOfAGenericHelper"] = ["SF1001"],
            ["HelperWithABodyInAnIfStatementSetsAPlainCell"] = ["SF1001"],
            ["ObjectOfAHelperInALoopWhereTheHelperChangesTheObjectBeforeIt"] = ["SF1001"],
            ["TwoObjectsOfALocalFunction"] = ["SF1001"],
            ["TwoObjectsOfALambda"] = ["SF1001"],
            ["ListFromAHelperThatReturnsAnInterfaceUnderTwoNames"] = ["SF1001"],
            ["ListInAnInterfaceMemberUnderTwoNames"] = ["SF1001"],
            ["IsSubsetOfAnArgumentThatGotItsComparerByName"] = ["SF1005"],
            ["KeyOfAPairOfADictionaryThatHoldsAnObjectOfTheStore"] = ["SF1001"],
            ["ObjectOfALocalFunctionInALoopThatChangesTheObjectBeforeIt"] = ["SF1001"],
            ["KeyOfAPairOfADictionaryThatHoldsAnObjectFromAHelper"] = ["SF1001"],
            ["SecondElementOfAnInitializerOnASetThatGotOnlyAComparer"] = ["SF1005"],
            ["SecondElementOfAnInitializerOnASetThatGotOnlyACapacity"] = ["SF1005"],
            ["InitializerOnASetThatGotASequenceAndAComparer"] = ["SF1005"],
            ["QueryExpressionSelectsAPendingCell"] = ["SF1001"],
            ["QueryExpressionSelectsAnObjectWithAPendingCell"] = ["SF1001"],
            ["ForeachOverAQueryExpressionThatSelectsAnObject"] = ["SF1001"],
            ["QueryExpressionSelectsAnAnonymousObject"] = ["SF1001"],
            ["SelectClauseSamplesItsRangeVariable"] = ["SF1001"],
            ["WhereClauseSamplesItsRangeVariable"] = ["SF1001"],
            ["OrderByClauseSamplesItsRangeVariable"] = ["SF1001"],
            ["LetClauseGivesItsVariableTheRangeVariable"] = ["SF1001"],
            ["SecondFromClauseSamplesItsRangeVariable"] = ["SF1001"],
            ["JoinClauseSamplesItsRangeVariable"] = ["SF1001"],
            ["JoinIntoClauseSamplesAnElementOfItsGroup"] = ["SF1001"],
            ["ContinuationSamplesWhatTheSelectBeforeItMakes"] = ["SF1001"],
            ["GroupClauseKeepsItsElements"] = ["SF1001"],
            ["GroupClauseKeepsItsKey"] = ["SF1001"],
            ["ContinuationOfAGroupSamplesAnElement"] = ["SF1001"],
            ["QueryExpressionSelectsItsRangeVariable"] = ["SF1001"],
            ["ImmutableDictionaryAddWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionaryAddWhereEqualsReadsTheGivenValue"] = ["SF1004"],
            ["ImmutableDictionarySetItemWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionarySetItemsWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionaryAddRangeWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionaryAddRangeComparesTheValueThatItHasWithItself"] = ["SF1004"],
            ["ImmutableDictionaryBuilderAddOfAPairWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionaryBuilderAddWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionaryBuilderIndexerWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableDictionaryBuilderAddRangeWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableSortedDictionaryAddWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableSortedDictionarySetItemWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableSortedDictionaryAddRangeWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ImmutableSortedDictionaryBuilderAddWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ToImmutableDictionaryWhereAKeyRepeats"] = ["SF1005"],
            ["CreateRangeOfAnImmutableSortedDictionaryWhereAKeyRepeats"] = ["SF1005"],
            ["ImmutableDictionaryContainsOfAPairWhereTheValueThatItHasIsPending"] = ["SF1005"],
            ["ToImmutableDictionaryWithAKeyComparerWhereAKeyRepeats"] = ["SF1005"],
            ["ToImmutableDictionaryWithAKeyFunctionWhereAKeyRepeats"] = ["SF1005"],
            ["QueryExpressionWithAContinuationSelectsAPendingCell"] = ["SF1001"],
            ["InitializerOnASetOfThisCodeThatItsConstructorFills"] = ["SF1004"],
            ["RangeVariableThroughObjectIsAnElement"] = ["SF1005"],
            ["ImmutableDictionaryThatGotAKeyComparerOnly"] = ["SF1005"],
            ["ImmutableDictionaryThatGotNullForItsValueComparer"] = ["SF1005"],
            ["TurnFirstObjectKeptInALocal"] = ["SF1001"],
            ["TurnPlainForTheSecondObjectOnly"] = ["SF1001"],
            ["TurnChangeThroughASecondLocal"] = ["SF1001"],
            ["TurnChangeThroughAnElementOfAListOfTheTurn"] = ["SF1001"],
            ["TurnChangeThroughALocalThatAnObjectOfTheTurnHas"] = ["SF1001"],
            ["TurnChangeThroughALocalThatATupleHas"] = ["SF1001"],
            ["TurnChangeAfterTheListGotTheObject"] = ["SF1001"],
            ["TurnForeachChangeThroughASecondLocal"] = ["SF1001"],
            ["TurnWhileChangeThroughASecondLocal"] = ["SF1001"],
            ["TurnDoChangeThroughASecondLocal"] = ["SF1001"],
            ["TurnOlderObjectGetsAPendingCellThroughALocalOfTheBlock"] = ["SF1001"],
            ["TurnOlderElementGetsAPendingCell"] = ["SF1001"],
            ["TurnWriteAfterTheLoopThroughOneOfTwoLocals"] = ["SF1001"],
            ["TurnWriteInTheOuterTurnAfterTheInnerLoop"] = ["SF1001"],
            ["TurnObjectInAMemberOfAnObjectOfTheBlock"] = ["SF1001"],
            ["TurnObjectInAMemberOfAnObjectOfTheOuterTurn"] = ["SF1001"],
            ["TurnObjectOfTheInitializerOfAForWhenNoTurnRuns"] = ["SF1001"],
            ["TurnPlainUnderAConditionAfterPending"] = ["SF1001"],
            ["TurnWriteInAnInnerLoop"] = ["SF1001"],
            ["TurnContinueBeforeTheWrite"] = ["SF1001"],
            ["TurnBreakBeforeTheWrite"] = ["SF1001"],
            ["TurnThrowBeforeTheWrite"] = ["SF1001"],
            ["TurnResultOfACallChangedThroughASecondLocal"] = ["SF1001"],
            ["TurnMemberOfTheResultOfACall"] = ["SF1001"],
            ["TurnReturnBeforeTheWrite"] = ["SF1001"],
            ["TurnGotoBeforeTheWrite"] = ["SF1001"],
            ["TurnThrowExpressionBeforeTheWrite"] = ["SF1001"],
            ["TurnWriteInALambdaThatReturnsEarly"] = ["SF1001"],
            ["TurnMemberThatTheTurnDidNotSet"] = ["SF1001"],
            ["TurnForeachMemberThatTheTurnDidNotSet"] = ["SF1001"],
            ["TurnForeachOfPartsMemberThatTheTurnDidNotSet"] = ["SF1001"],
            ["TurnWhileMemberThatTheTurnDidNotSet"] = ["SF1001"],
            ["TurnDoMemberThatTheTurnDidNotSet"] = ["SF1001"],
            ["TurnObjectInAMemberOfThisObject"] = ["SF1001"],
            ["TurnObjectInAMemberOfAStaticObject"] = ["SF1001"],
            ["TurnObjectInAMemberOfTheResultOfACall"] = ["SF1001"],
            ["TurnLocalOfACallThatReturnedKeepsTheFirstObject"] = ["SF1001"],
            ["TurnInnerObjectInAListOfTheOuterTurn"] = ["SF1001"],
            ["TurnObjectOfTheInitializerOfAForChangedThroughALocalOfTheBody"] = ["SF1001"],
            ["TurnYieldBreakBeforeTheWrite"] = ["SF1001"],
            ["TurnOlderObjectThroughALocalOfTheBlock"] = ["SF1001"],
            ["TurnOlderObjectThroughALocalThatALocalFunctionAssigns"] = ["SF1001"],
            ["TurnOlderObjectThroughAMemberOfAnObjectOfTheBlock"] = ["SF1001"],
            ["TurnOlderElementReadThroughALocalOfTheBlock"] = ["SF1001"],
            ["TurnOlderPartOfATupleReadThroughALocalOfTheBlock"] = ["SF1001"],
            ["TurnOlderObjectThroughAMemberOfAnOlderHolder"] = ["SF1001"],
            ["TurnOlderObjectThroughAnElementOfAnOlderList"] = ["SF1001"],
            ["TurnOlderObjectThroughAForeachOverAnOlderList"] = ["SF1001"],
            ["TurnOlderObjectThroughAMemberOfTheReceiverOfAMethod"] = ["SF1001"],
            ["TurnOlderObjectThroughTheValueOfACell"] = ["SF1001"],
            ["TurnObjectInAMemberThatTheLoopOfAConstructorSets"] = ["SF1001"],
            ["TurnObjectsOfALambdaThatAQueryRuns"] = ["SF1001"],
            ["TurnObjectsOfALocalFunctionThatAQueryRuns"] = ["SF1001"],
            ["TurnObjectsOfAQueryExpression"] = ["SF1001"],
            ["TurnPlainInALocalFunctionThatSomeTurnsCall"] = ["SF1001"],
            ["TurnOlderObjectThroughAMemberOfAnObjectOfTheOuterTurn"] = ["SF1001"],
            ["TurnLambdaOfTheFirstTurnGivesItsObject"] = ["SF1001"],
            ["TurnLambdaOfTheFirstTurnChangesItsObject"] = ["SF1001"],
            ["TurnLocalFunctionOfTheBodyGivesItsObject"] = ["SF1001"],
            ["TurnOlderObjectThroughAMemberOfAnObjectThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["TurnOlderPartOfATupleInAListThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["TurnPlainThroughTheFieldOfAnObjectThatTheWalkDoesNotKnow"] = ["SF1001"],
            ["TurnLocalFunctionOfTheFirstTurnGivesItsObject"] = ["SF1001"],
            ["TurnLambdaOfACallOfTheFirstTurnGivesItsArgument"] = ["SF1001"],
            ["TurnLambdaOfTheFirstTurnChangesItsObjectInALambdaOfAList"] = ["SF1001"],
            ["TurnAssignmentToALocalOfTheBlockWhenNoTurnRuns"] = ["SF1001"],
            ["TurnObjectsOfALambdaThatAListRuns"] = ["SF1001"],
            ["TurnObjectsOfALocalFunctionThatAListRuns"] = ["SF1001"],
            ["ElementBesideAnObjectThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["ElementOfALiteralWithAnObjectThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["ElementThatGetsAPendingCellAfterTheListGotIt"] = ["SF1001"],
            ["WriteThroughAnElementIsAChangeToItsObject"] = ["SF1001"],
            ["ElementThatAddRangePut"] = ["SF1001"],
            ["ElementThatAddRangePutAndThatGetsAPendingCellAfterIt"] = ["SF1001"],
            ["ElementOfACopyOfAList"] = ["SF1001"],
            ["ElementWithNoStateBesideASecondObject"] = ["SF1001"],
            ["ElementWithNoStateThatAddRangePut"] = ["SF1001"],
            ["BuilderOfAnImmutableHashSetAdd"] = ["SF1005"],
            ["BuilderOfAnImmutableHashSetContains"] = ["SF1005"],
            ["BuilderOfAnImmutableHashSetTryGetValue"] = ["SF1005"],
            ["BuilderOfAnImmutableHashSetUnionWith"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedSetAdd"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedSetContains"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedSetRemove"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedSetIntersectWith"] = ["SF1005"],
            ["BuilderOfAnImmutableDictionaryAdd"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedDictionaryAdd"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedDictionaryContainsKey"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedDictionaryIndexer"] = ["SF1005"],
            ["BuilderOfAnImmutableListContains"] = ["SF1005"],
            ["BuilderOfAnImmutableListIndexOf"] = ["SF1005"],
            ["BuilderOfAnImmutableListRemove"] = ["SF1005"],
            ["BuilderOfAnImmutableArrayContains"] = ["SF1005"],
            ["BuilderOfAnImmutableArrayIndexOf"] = ["SF1005"],
            ["BuilderOfAnImmutableHashSetRemoveOfTheObjectThatItHas"] = ["SF1005"],
            ["BuilderOfAnImmutableDictionaryContainsKeyOfTheKeyThatItHas"] = ["SF1005"],
            ["TurnBeforeGaveAPendingCell"] = ["SF1001"],
            ["TurnBeforeGaveAnObjectWithAPendingCell"] = ["SF1001"],
            ["TurnBeforeOfAWhileGaveAPendingCell"] = ["SF1001"],
            ["TurnBeforeOfADoGaveAPendingCell"] = ["SF1001"],
            ["TurnBeforeOfAForeachGaveAPendingCell"] = ["SF1001"],
            ["TurnBeforeOfAForeachOfPartsGaveAPendingCell"] = ["SF1001"],
            ["ConditionReadsWhatTheTurnBeforeGave"] = ["SF1001"],
            ["ConditionOfAForReadsWhatTheTurnBeforeGave"] = ["SF1001"],
            ["TurnBeforeGaveAMemberItsObject"] = ["SF1001"],
            ["TurnBeforePutAPendingCellIntoAList"] = ["SF1001"],
            ["LambdaReadsWhatTheTurnBeforeGave"] = ["SF1001"],
            ["CalleeReadsWhatTheTurnBeforeGave"] = ["SF1004"],
            ["InnerLoopReadsWhatTheOuterTurnBeforeGave"] = ["SF1001"],
            ["InnerLoopReadsWhatItsTurnBeforeGave"] = ["SF1001"],
            ["ElementFromAListThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["BuilderOfAnImmutableDictionaryContainsValue"] = ["SF1005"],
            ["BuilderOfAnImmutableSortedDictionaryContainsValue"] = ["SF1005"],
            ["BuilderOfAnImmutableListBinarySearch"] = ["SF1005"],
            ["BuilderOfAnImmutableListSort"] = ["SF1005"],
            ["BuilderOfAnImmutableArraySort"] = ["SF1005"],
            ["ConditionOfADoReadsWhatTheTurnBeforeGave"] = ["SF1001"],
            ["TurnAssignmentOfACellWhenNoTurnRuns"] = ["SF1001"],
            ["TurnEndsAfterItsAssignment"] = ["SF1001"],
            ["ElementFromAFieldThatGetsAPendingCellAfterTheListGotIt"] = ["SF1001"],
            ["ElementOfAListThatTheWalkDidNotSeeMadeInAListOfLists"] = ["SF1001"],
            ["ElementWithNoObjectThatGetsAPendingCellThroughTheIndexer"] = ["SF1001"],
            ["ElementWithNoStateBesideAnObjectOfTheStore"] = ["SF1001"],
            ["ElementWithNoStateThatGetsAPendingCellThroughASecondLocal"] = ["SF1001"],
            ["ElementWithNoStateInACollectionExpression"] = ["SF1001"],
            ["ElementWithNoStateInAnArrayWithNoType"] = ["SF1001"],
            ["ElementWithNoStateInAnArrayOfAType"] = ["SF1001"],
            ["ElementWithNoStateInACollectionInitializer"] = ["SF1001"],
            ["ElementWithNoStateInAnInitializerOfADictionary"] = ["SF1001"],
            ["ElementWithNoStateThatAnIndexAssignmentPut"] = ["SF1001"],
            ["ElementOfACopyOfAListThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["FunctionForACalleeReadsWhatTheTurnBeforeGave"] = ["SF1001"],
            ["MemberOfTheHolderOfTheTurnBefore"] = ["SF1001"],
            ["ConversionReadsWhatTheTurnBeforeGave"] = ["SF1004"],
            ["LoopOfOneTurnWhoseSecondReadGivesAPlainCell"] = ["SF1001"],
            ["IncrementorReadsWhatTheTurnGave"] = ["SF1001"],
            ["LocalOfTheBlockThatEachTurnAssignsAndTheNextTurnReads"] = ["SF1001"],
            ["LocalOfTheBlockThatEachTurnAssignsAndAListKeeps"] = ["SF1001"],
            ["ElementFromACallWithNoBodyInACollectionExpression"] = ["SF1001"],
            ["MemberOfTheHolderOfTheTurnBeforeThroughASecondName"] = ["SF1001"],
            ["ElementOfTheResultOfACallOnAListThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["ElementOfAListFromAConstructorWithAListThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["ElementOfAListThatAHelperReturns"] = ["SF1001"],
            ["ElementOfAListThatAMethodWithNoBodyFilled"] = ["SF1001"],
            ["ElementOfAListInAMemberThatTheWalkDidNotSeeSet"] = ["SF1001"],
            ["ElementWithAPendingCellBesideAnObjectOfTheStoreInACollectionExpression"] = ["SF1001"],
            ["ElementWithAPendingCellBesideAnObjectOfTheStoreInACollectionInitializer"] = ["SF1001"],
            ["PlainThroughALocalOfTheBlockAfterTheLoop"] = ["SF1001"],
            ["PlainThroughALocalOfTheBlockThatOneTurnAssigns"] = ["SF1001"],
            ["CoalescingAssignmentOfACell"] = ["SF1001"],
            ["CoalescingAssignmentOfAnObject"] = ["SF1001"],
            ["CoalescingAssignmentKeepsWhatItsTargetHad"] = ["SF1001"],
            ["CoalescingAssignmentToAMember"] = ["SF1001"],
            ["ValueOfACoalescingAssignment"] = ["SF1001"],
            ["CellInAVariableOfAFor"] = ["SF1001"],
            ["ObjectInAVariableOfAFor"] = ["SF1001"],
            ["CellInTheSecondVariableOfAFor"] = ["SF1001"],
            ["ObjectInAVariableOfAForThatTheTurnBeforeChanged"] = ["SF1001"],
            ["ConditionOfAForReadsBeforeTheBody"] = ["SF1001"],
            ["ChainOfAssignments"] = ["SF1001"],
            ["ChainOfAssignmentsOfOneObject"] = ["SF1001"],
            ["SampleOfTheValueOfAnAssignment"] = ["SF1001"],
            ["AssignmentInTheInitializerOfALocal"] = ["SF1001"],
            ["HelperThatReturnsBeforeItsPlainWrite"] = ["SF1001"],
            ["UnsetMemberThroughAnElement"] = ["SF1001"],
            ["UnsetMemberThroughAnElementInTheTurn"] = ["SF1001"],
            ["UnsetMemberThroughOneOfTwoObjects"] = ["SF1001"],
            ["WriteThroughTheUnsetMemberOfOneOfTwoObjects"] = ["SF1001"],
            ["UnsetMemberThroughTheVariableOfAForeach"] = ["SF1001"],
            ["FieldOfTwoObjectsThatTheWalkDoesNotKnow"] = ["SF1001"],
            ["PendingCellThroughAFieldOfAnObjectThatTheWalkDoesNotKnow"] = ["SF1001"],
            ["ResultOfALambdaOfAHelper"] = ["SF1001"],
            ["ResultOfALambdaOfAHelperThatKeeps"] = ["SF1001"],
            ["LambdaOfAHelperThatSamples"] = ["SF1004"],
            ["TurnResultOfALambdaOfAHelperThatKeeps"] = ["SF1001"],
            ["PlainUnderAConditionInALambda"] = ["SF1001"],
            ["PlainUnderAConditionInALocalFunction"] = ["SF1001"],
            ["PlainUnderAConditionInAHelperOfALoop"] = ["SF1004"],
            ["LambdaThatReturnsBeforeItsPlainWrite"] = ["SF1001"],
            ["PlainThroughALambdaThatTheTurnDoesNotCall"] = ["SF1001"],
            ["TwoNamesInAHelperOfALoop"] = ["SF1004"],
            ["TwoNamesInALambdaOfALoop"] = ["SF1004"],
            ["TwoNamesInALambda"] = ["SF1004"],
            ["TwoNamesInALocalFunction"] = ["SF1004"],
            ["ObjectOfAHelperThatTheTurnBeforeKept"] = ["SF1001"],
            ["ValueOfACoalescingAssignmentWhoseTargetHadAPendingCell"] = ["SF1001"],
            ["IncrementorReadsWhatTheBodyOfTheSecondTurnGave"] = ["SF1001"],
            ["IncrementorOfALoopOfOneTurn"] = ["SF1001"],
            ["PlainAfterAReturnInALambdaOfATransaction"] = ["SF1001"],
            ["HelperThatGoesToItsEndBeforeItsPlainWrite"] = ["SF1001"],
            ["IteratorThatEndsBeforeItsPlainWrite"] = ["SF1001"],
            ["UnsetMemberThroughOneOfAnObjectThatTheWalkDoesNotKnow"] = ["SF1001"],
            ["TwoNamesInAHelperOfAHelperOfALoop"] = ["SF1004"],
            ["TurnObjectsOfALambdaInALocalThatAListRuns"] = ["SF1001"],
            ["TwoNamesInALocalFunctionOfALambda"] = ["SF1004"],
            ["FunctionOfTheFirstCallOfALambda"] = ["SF1001"],
            ["LocalOfALocalFunctionThatCallsItself"] = ["SF1004", "SF1004"],
            ["ParameterOfALambdaThatTheCallBeforeAssigned"] = ["SF1004"],
            ["PlainThroughALambdaThatABranchCalls"] = ["SF1001"],
            ["PlainThroughALocalFunctionThatABranchCalls"] = ["SF1001"],
            ["PlainThroughALambdaInAnArmThatDoesNotRun"] = ["SF1001"],
            ["PlainThroughALambdaAfterAnOperatorThatStops"] = ["SF1001"],
            ["PlainThroughALambdaAfterACoalescingOperator"] = ["SF1001"],
            ["LocalThatIsOneOfAnObjectOfTheStoreAndAnObjectWithNoState"] = ["SF1001"],
            ["LocalThatABranchGivesAnObjectWithNoState"] = ["SF1001"],
            ["LocalFromACoalescingOperatorWithAnObjectWithNoState"] = ["SF1001"],
            ["ElementOfAnObjectThatKeepsThroughAnInterface"] = ["SF1001"],
            ["ElementOfAnObjectThatKeepsThroughAnInterfaceAndGetsAPendingCell"] = ["SF1001"],
            ["LambdaOfTheTurnBeforeReadsALocalOfItsTurn"] = ["SF1001"],
            ["LocalFunctionOfTheTurnBeforeReadsALocalOfItsTurn"] = ["SF1001"],
            ["FirstOfAListReadsAPendingCell"] = ["SF1001"],
            ["FirstOfAListWithAnObjectThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["LastOfAListAfterAChangeThroughAnElement"] = ["SF1001"],
            ["ElementOfAToListThatGetsAPendingCell"] = ["SF1001"],
            ["ElementsOfAQueryOverAnObjectThatTheWalkDidNotSeeMade"] = ["SF1001"],
            ["KeyThroughACopyOfKeys"] = ["SF1005"],
            ["KeyThroughALoopOverKeys"] = ["SF1005"],
            ["FirstOrDefaultGivesItsArgument"] = ["SF1001"],
            ["FirstOrDefaultOfAQueryGivesItsArgument"] = ["SF1001"],
            ["FirstAddToAnEmptyHashSetMakesAHashCode"] = ["SF1005"],
            ["SecondAddToASortedSet"] = ["SF1005"],
            ["FirstAddAfterAHelperGotTheSortedSet"] = ["SF1005"],
            ["FirstAddToASortedSetInALoop"] = ["SF1005"],
            ["FirstAddToASortedSetFromASequence"] = ["SF1005"],
            ["FirstAddInALambdaThatRunsAfterASecondAdd"] = ["SF1005"],
            ["CellFromTwoTurnsBefore"] = ["SF1001"],
            ["CellFromThreeTurnsBefore"] = ["SF1001"],
            ["ObjectFromTwoTurnsBefore"] = ["SF1001"],
            ["TwoNamesInAFunctionOfAQuery"] = ["SF1001"],
            ["TwoNamesInALambdaThatAListRuns"] = ["SF1001"],
            ["PlainInABranchOfALambdaThatAListRuns"] = ["SF1001"],
            ["TwoNamesInALocalFunctionThatAListRuns"] = ["SF1001"],
            ["RunBeforeOfALambdaThatAListRunsGaveAPendingCell"] = ["SF1001"],
            ["PlainThroughALambdaOfALambdaThatABranchCalls"] = ["SF1001"],
            ["PlainThroughALambdaAfterAnOrOperator"] = ["SF1001"],
            ["PlainThroughALambdaInACoalescingAssignment"] = ["SF1001"],
            ["PlainThroughALambdaInACallOnNull"] = ["SF1001"],
            ["PlainThroughALambdaInAnArmOfASwitchExpression"] = ["SF1001"],
            ["LocalFromASwitchExpressionWithAnObjectWithNoState"] = ["SF1001"],
            ["FirstAddToASortedSetOfThisCompilation"] = ["SF1005"],
            ["CellFromFourTurnsBefore"] = ["SF1001"],
            ["MemberFromTwoTurnsBefore"] = ["SF1001"],
            ["ObjectsOfAnExpressionLambdaOfAQuery"] = ["SF1001"],
            ["PendingThroughFirstOfAListWhoseObjectHasNoMember"] = ["SF1001"],
            ["SecondTryAddOfADictionaryWithAComparer"] = ["SF1005"],
            ["SecondGetOrAddOfAConcurrentDictionaryWithAComparer"] = ["SF1005"],
            ["PendingCellThroughThreeLocalsThatStartPlain"] = ["SF1001"],
            ["MemberOfAnOutsideObjectThroughThreeLocals"] = ["SF1001"],
            ["MemberOfAnObjectFromACellTwoTurnsBefore"] = ["SF1001"],
            ["PlainThroughTheLastObjectOfAnExpressionLambdaThatAListRuns"] = ["SF1001"],
            ["PlainThroughTheLastObjectOfALocalFunctionThatAListRuns"] = ["SF1001"],
            ["FirstAddToAnEmptyHashSetWithAComparerThatMakesAHashCode"] = ["SF1005"],
            ["SecondAddToAHashSetWithAComparer"] = ["SF1005"],
            ["FirstAddWhoseArgumentPutsAnObjectFirst"] = ["SF1005"],
            ["SecondKeyThroughTheIndexerOfASortedDictionary"] = ["SF1005"],
            ["KeyThroughTheIndexerAfterTheValuePutsAKey"] = ["SF1005"],
            ["SecondValueThroughTheIndexerOfABuilder"] = ["SF1005"],
            ["FirstAddToAnEmptyHashSetWithAHashCodeOfTheInterface"] = ["SF1005"],
            ["PendingCellThroughThreeLocalsThatStartLinked"] = ["SF1001"],
            ["MemberOfAnObjectInAMemberFromACellTwoTurnsBefore"] = ["SF1001"],
            ["PlainThroughTheFirstElementInAFunctionThatAQueryRunsAgain"] = ["SF1001"]
        };

    // A shape whose block is a method, given by its name. The analyzer reports in that method.
    // Thus, the rules of the method say what the shape does at run time.
    private static readonly Dictionary<string, string> BlockMethods =
        new()
        {
            ["MethodBlock"] = "BlockThatReads",
            ["MethodBlockCloses"] = "BlockThatClosesWithItself",
            ["MethodBlockSnapshots"] = "BlockThatSnapshots",
            ["ListSortWithAnOptionalComparerOfTheMethodOfTheBlock"] = "SortInABlock"
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
    ///     the two. The first put into each empty collection calls nothing, thus
    ///     each block has one report, for its second put.
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
            .IsEquivalentTo(["SF1005", "SF1005"]);

        // The lines of the second Add and of the second assignment through the indexer.
        await Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line))
            .IsEquivalentTo([12, 22]);
    }

    /// <summary>
    ///     The analyzer asks the compiler for a <c>Deconstruct</c> method in two
    ///     cases. The compilation declares one, or the value is a pair with a
    ///     state for each part. The shapes file declares one, thus it cannot show
    ///     the second case. An assignment, a foreach, and a positional pattern
    ///     each have that condition. A state must keep the answer for the second
    ///     case in three more places. The state goes to a method, the code sets a
    ///     member of the collection that has the pair, or a join fills a part.
    /// </summary>
    [Test]
    public async Task APairGivesItsPartsInACompilationThatDeclaresNoDeconstruct()
    {
        const string source =
            """
            using System.Collections.Generic;
            using System.Linq;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);
                private static readonly List<object> Kept = [];

                public static Cell<int> PlainKeyByAnAssignment() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        (Shown k, Shown _) = p;
                        Kept.Add(k.ToString());

                        return S.Hold(0);
                    });

                public static Cell<int> PendingValueByAnAssignment() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        (Shown _, Shown v) = p;
                        Kept.Add(v.ToString());

                        return S.Hold(0);
                    });

                public static Cell<int> PlainKeyByAForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<KeyValuePair<Shown, Shown>> all = [new(new Shown(Plain), new Shown(l.Map(v => v)))];

                        foreach ((Shown k, Shown _) in all)
                        {
                            Kept.Add(k.ToString());
                        }

                        return S.Hold(0);
                    });

                public static Cell<int> PendingValueByAForeach() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<KeyValuePair<Shown, Shown>> all = [new(new Shown(Plain), new Shown(l.Map(v => v)))];

                        foreach ((Shown _, Shown v) in all)
                        {
                            Kept.Add(v.ToString());
                        }

                        return S.Hold(0);
                    });

                public static Cell<int> PlainKeyByAPattern() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));

                        if (p is (var k, _))
                        {
                            Kept.Add(k.ToString());
                        }

                        return S.Hold(0);
                    });

                public static Cell<int> PendingValueByAPattern() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));

                        if (p is (_, var v))
                        {
                            Kept.Add(v.ToString());
                        }

                        return S.Hold(0);
                    });

                // The pair goes to a method, which gives each of its states an origin.
                public static Cell<int> PlainKeyInAMethod() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        KeyValuePair<Shown, Shown> p = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        Kept.Add(KeyTextOf(p));

                        return S.Hold(0);
                    });

                // The code sets a member of the list that has the pair.
                public static Cell<int> PlainKeyAfterAMemberOfTheList() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<KeyValuePair<Shown, Shown>> all = [new(new Shown(Plain), new Shown(l.Map(v => v)))];
                        all.Capacity = 4;

                        foreach ((Shown k, Shown _) in all)
                        {
                            Kept.Add(k.ToString());
                        }

                        return S.Hold(0);
                    });

                // The value is one of two pairs, and the one from the dictionary has no state for
                // its parts.
                public static Cell<int> PlainKeyOfOneOfTwoPairs() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Dictionary<Shown, Shown> d = new() { { new Shown(Plain), new Shown(Plain) } };
                        KeyValuePair<Shown, Shown> made = new(new Shown(Plain), new Shown(l.Map(v => v)));
                        KeyValuePair<Shown, Shown> p = d.Count > 5 ? made : d.First();
                        (Shown k, Shown _) = p;
                        Kept.Add(k.ToString());

                        return S.Hold(0);
                    });

                private static string KeyTextOf(KeyValuePair<Shown, Shown> pair)
                {
                    (Shown k, Shown _) = pair;

                    return k.ToString();
                }

                public sealed class Shown(Cell<int> share)
                {
                    public override string ToString() => share.Sample().ToString();
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyAPair"));

        // One report for each read of the pending value, and none for a read of the plain key.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1005", "SF1005", "SF1005"]);
    }

    /// <summary>
    ///     A literal that no code changes keeps its positions. An index that the
    ///     literal has no element for, and <c>First</c> of a literal with no
    ///     element, give no position. The analyzer then reads the collection as one
    ///     state. At run time, each of these reads throws an exception that is not
    ///     the one of a loop, thus the shapes file cannot have them.
    /// </summary>
    [Test]
    public async Task AnIndexOutsideALiteralGivesNoPosition()
    {
        const string source =
            """
            using System.Collections.Generic;
            using System.Linq;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly Cell<int> Plain = Cell.Constant(3);

                public static Cell<int> AfterTheLastElement() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [Plain, l.Map(v => v)];

                        return S.Hold(cs[2].Sample());
                    });

                public static Cell<int> BeforeTheFirstElement() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [Plain, l.Map(v => v)];

                        return S.Hold(cs[-1].Sample());
                    });

                public static Cell<int> FirstOfNoElement() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> cs = [];

                        return S.Hold(cs.First().Sample() + l.Sample());
                    });
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyALiteral"));

        // The two reads by an index have the state of all the elements, and one of those is
        // pending. The third method reads the looped cell itself.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id))
            .IsEquivalentTo(["SF1001", "SF1001", "SF1001"]);
    }

    /// <summary>
    ///     The walk has a limit for the number of callee bodies that it reads for
    ///     one block. A member that the block calls after that limit is code that
    ///     the analyzer does not read. The object of the member has a pending cell, thus the call
    ///     gets the report for such code.
    /// </summary>
    /// <remarks>
    ///     The helpers call each other eight times at each of three levels, which
    ///     is more bodies than the limit. The shapes file cannot have this block:
    ///     its calls after the limit get the same rule, and a shape has only a set
    ///     of rules.
    /// </remarks>
    [Test]
    public async Task AMemberThatTheBlockCallsPastTheLimitOfBodiesGetsAReport()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> PastTheLimit() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        Reader reader = new(l.Map(v => v));
                        Kept.Add(Wide3(l.Map(v => v)));
                        Kept.Add(reader.Now);

                        return S.Hold(0);
                    });

                private static int Wide0(Cell<int> c) => c == null ? 1 : 0;

                private static int Wide1(Cell<int> c) =>
                    Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c) + Wide0(c);

                private static int Wide2(Cell<int> c) =>
                    Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c) + Wide1(c);

                private static int Wide3(Cell<int> c) =>
                    Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c) + Wide2(c);

                public sealed class Reader(Cell<int> share)
                {
                    public int Now => share.Sample();
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyPastTheLimit"));

        string[] notRead =
        [
            .. diagnostics
                .Where(static diagnostic => diagnostic.Id == "SF1003")
                .Select(static diagnostic =>
                    diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? string.Empty)
        ];

        await Assert.That(notRead).Contains("reader.Now");
    }

    /// <summary>
    ///     A callee that gets only an object of the store, and no state from the
    ///     loop, has its own limit of bodies. A block that calls many such callees
    ///     has each body that a pending value needs.
    /// </summary>
    /// <remarks>
    ///     The helpers get an empty list, and call each other eight times at each
    ///     of three levels. That is more bodies than the limit. The helper after
    ///     them samples a pending cell, and the walk reads it.
    /// </remarks>
    [Test]
    public async Task CalleesThatGetOnlyObjectsDoNotUseTheBodiesOfAPendingValue()
    {
        const string source =
            """
            using System.Collections.Generic;
            using SodaFlow;

            public static class Only
            {
                private static readonly StreamSink<int> S = Stream.CreateSink<int>();
                private static readonly List<object> Kept = [];

                public static Cell<int> ManyCalleesWithAnObject() =>
                    Cell.Loop<int>().WithoutCaptures(l =>
                    {
                        List<Cell<int>> list = [];
                        Kept.Add(Wide3(list));
                        Kept.Add(NowOf(l.Map(v => v)));

                        return S.Hold(0);
                    });

                private static int NowOf(Cell<int> c) => c.Sample();

                private static int Wide0(List<Cell<int>> xs) => xs.Count;

                private static int Wide1(List<Cell<int>> xs) =>
                    Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs) + Wide0(xs);

                private static int Wide2(List<Cell<int>> xs) =>
                    Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs) + Wide1(xs);

                private static int Wide3(List<Cell<int>> xs) =>
                    Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs) + Wide2(xs);
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics =
            await AnalyzerHarness.Analyze(
                analyzer: new LoopReadAnalyzer(),
                compilation: AnalyzerHarness.Compile(
                    source: source,
                    libraries: ShapeLibraries,
                    assemblyName: "OnlyManyCallees"));

        // The helper samples its parameter, and the walk says so. A helper that the walk does not
        // read gets the report for such code and no more.
        await Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id)).IsEquivalentTo(["SF1004"]);
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
