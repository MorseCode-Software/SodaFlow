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
            ["SortedSetInitializerOfObjects"] = ["SF1004", "SF1005"],
            ["SortedSetAddOfObjects"] = ["SF1004", "SF1005"],
            ["SortedDictionaryAddOfObjectKeys"] = ["SF1004", "SF1005"],
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
            ["EqualsOfATupleThroughObject"] = ["SF1005"],
            ["GetHashCodeOfATuple"] = ["SF1005"],
            ["GetHashCodeOfATupleThroughObject"] = ["SF1005"],
            ["CompareToOfATuple"] = ["SF1005"],
            ["CompareToOfATupleThroughAnInterface"] = ["SF1004", "SF1005"],
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
            ["SetOfTwoFromACollectionExpression"] = ["SF1004", "SF1005"],
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
            ["SpanInALocalContainsWhereTheElementIsPending"] = ["SF1004"]
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
