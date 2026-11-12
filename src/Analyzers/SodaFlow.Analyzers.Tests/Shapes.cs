using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
using SodaFlow;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Analyzers.Shapes;

// Each method is one shape, and LoopReadAnalyzerTests gives the rules expected in each. The
// comment on a shape says what the shape is. This file is test data: the harness compiles it and
// the analyzer reads it, and the project does not compile it.
public static class Shapes
{
    private static readonly StreamSink<int> S = Stream.CreateSink<int>();
    private static readonly Cell<int> Plain = Cell.Constant(3);

    public static void Main() { }

    // S01. Direct read.
    public static Cell<int> DirectSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Sample(); return S.Hold(x); });

    // S02. Read through Map.
    public static Cell<int> MapSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Map(v => v + 1).Sample(); return S.Hold(x); });

    // S03. Read through Lift, placeholder as argument.
    public static Cell<int> LiftSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Plain.Lift(l, (a, b) => a + b).Sample(); return S.Hold(x); });

    // S04. Read through Calm.
    public static Cell<int> CalmSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Calm().Sample(); return S.Hold(x); });

    // S05. Read through SwitchC on a looped cell of cells.
    public static Cell<Cell<int>> SwitchCSample() =>
        Cell.Loop<Cell<int>>().WithoutCaptures(l => { int x = l.SwitchC().Sample(); return Cell.Constant(Cell.Constant(x)); });

    // S06. Read through HoldLazy of a lazy sample of the placeholder.
    public static Cell<int> HoldLazySample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Stream.Never<int>().HoldLazy(l.SampleLazy()).Sample(); return S.Hold(x); });

    // S07. Read through ConstantLazy and a lazy Map.
    public static Cell<int> ConstantLazySample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Cell.ConstantLazy(l.SampleLazy().Map(v => v + 1)).Sample(); return S.Hold(x); });

    // S08. A hold with a given value is not pending.
    public static Cell<int> HoldWithValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Updates().Hold(7).Sample(); return S.Hold(x); });

    // S09. A snapshot samples at firing time.
    public static Cell<int> SnapshotHold() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = S.Snapshot(l, (a, b) => a + b).Hold(7).Sample(); return S.Hold(x); });

    // S10. A read in a Map callback runs after the close.
    public static Cell<int> ReadInCallback() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Map(v => v + l.Sample()).Hold(0));

    // S11. A constructor that samples its parameter.
    public static Cell<int> ConstructorSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { SamplingRow row = new(l); return S.Hold(row.Value); });

    // S12. The same constructor with AsCell at the argument.
    public static Cell<int> ConstructorSamplesAsserted() =>
        Cell.Loop<int>().WithoutCaptures(l => { SamplingRow row = new(l.AsCell()); return S.Hold(row.Value); });

    // S13. A constructor that stores its parameter.
    public static Cell<int> ConstructorStores() =>
        Cell.Loop<int>().WithoutCaptures(l => { StoringRow row = new(l); return S.Hold(0).Lift(row.Cell, (a, b) => a + b); });

    // S14. A static helper that samples its parameter.
    public static Cell<int> HelperSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadIt(l)));

    // S15. A derived value into a helper that samples.
    public static Cell<int> DerivedIntoHelperSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadIt(l.Map(v => v * 2))));

    // S16. Close with the placeholder.
    public static Cell<int> CloseWithPlaceholder() =>
        Cell.Loop<int>().WithoutCaptures(l => l);

    // S17. Close with a Map of the placeholder.
    public static Cell<int> CloseWithMap() =>
        Cell.Loop<int>().WithoutCaptures(l => l.Map(v => v + 1));

    // S18. Close with a hold of the placeholder's own updates.
    public static Cell<int> CloseWithUpdatesHold() =>
        Cell.Loop<int>().WithoutCaptures(l => l.Updates().Filter(v => v % 2 == 0).Map(v => v + 1).Merge(S, (_, r) => r).Hold(1));

    // S19. Close with a snapshot of the placeholder.
    public static Cell<int> CloseWithSnapshot() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Snapshot(l, (a, b) => a + b).Hold(0));

    // S20. Close through Operational.Defer.
    public static Cell<int> CloseWithDefer() =>
        Cell.Loop<int>().WithoutCaptures(l => Operational.Defer(l.Updates()).Merge(S, (_, r) => r).Hold(0));

    // S21. A stream loop switched over its own output.
    public static Stream<int> SwitchOverOwnOutput() =>
        Transaction.Run(() => Stream.Loop<int>().WithoutCaptures(loop => Cell.Constant<Stream<int>>(loop).SwitchS()));

    // S22. A selector that holds the switch's own output.
    public static Stream<int> SelectorHoldsOwnOutput()
    {
        StreamSink<int> evens = Stream.CreateSink<int>();
        StreamSink<int> odds = Stream.CreateSink<int>();

        return Transaction.Run(() =>
            Cell.Loop<Stream<int>>()
                .WithCaptures(loop =>
                {
                    Stream<int> switched = loop.SwitchS();
                    Cell<Stream<int>> selector = switched.Map(v => v % 2 == 0 ? (Stream<int>)odds : evens).Hold(evens);
                    return (selector, switched);
                }).Captures);
    }

    // S23. A read in Transaction.Run in the block runs now.
    public static Cell<int> ReadInNestedTransaction() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Transaction.Run(() => l.Sample()); return S.Hold(x); });

    // S24 was an explicit loop, which the analyzer does not check.

    // S25. A read in a LINQ lambda runs now.
    public static Cell<int> ReadInLinqLambda() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<int> xs = Enumerable.Range(0, 1).Select(_ => l.Sample()).ToList(); return S.Hold(xs[0]); });

    // S26. A read in a local function called in the block.
    public static Cell<int> ReadInLocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            return S.Hold(Read());

            int Read() => l.Sample();
        });

    // S27. A user helper whose body defers, seen through one level of inlining.
    public static Cell<IReadOnlyList<int>> HelperDefers() =>
        Cell.Loop<IReadOnlyList<int>>().WithoutCaptures(l =>
            l.Map(xs => Cell.Constant(xs.Count)).SwitchCWithDeferredValues().Filter(v => v < 50)
                .Snapshot(l, (_, items) => (IReadOnlyList<int>)[.. items, 1]).Hold([1, 2, 3]));

    // S28. A capture derived from the placeholder, read after the block.
    public static int CaptureReadAfter()
    {
        (Cell<int> _, Cell<int> derived) = Cell.Loop<int>().WithCaptures(l => (Cell: S.Hold(1), Captures: l.Lift(Plain, (a, b) => a + b)));
        return derived.Sample();
    }

    // S29. Forward reference read during construction.
    public static int ForwardReferenceRead() =>
        ForwardReference<int>.WithoutCaptures(reference => reference.Sample());

    // S30. Forward reference into a constructor with AsCell.
    public static StoringRow ForwardReferenceIntoConstructor() =>
        ForwardReference<StoringRow>.WithoutCaptures(reference => new StoringRow(reference.AsCell().Map(r => r.Value)));

    // S31. Forward reference into a constructor without AsCell.
    public static StoringRow ForwardReferenceIntoConstructorBare() =>
        ForwardReference<StoringRow>.WithoutCaptures(reference => new StoringRow(reference.Map(r => r.Value)));

    // S32. A list Lift over a list that holds the placeholder, sampled in the block.
    public static Cell<int> ListLiftSample() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            IReadOnlyList<Cell<int>> cells = [l, Plain];
            int now = cells.Lift().Map(v => v.Sum()).Sample();
            return S.Hold(now);
        });

    // S33. A delegate called with the placeholder, its result sampled.
    public static Cell<int> DelegateCall(Func<Cell<int>, Cell<int>> f) =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int> r = f(l); int x = r.Sample(); return S.Snapshot(r, (a, b) => a + b + x).Hold(0); });

    // S34. A Behavior loop read through Map.
    public static Behavior<int> BehaviorMapSample() =>
        Behavior.Loop<int>().WithoutCaptures(l => { int x = l.Map(v => v).Sample(); return Behavior.Constant(x); });

    // S35. A bindable built from the placeholder, read in the block.
    public static Cell<int> BindableRead(SodaFlow.Bindable.ObjectModel.IBindableFactory factory) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            SodaFlow.Bindable.ObjectModel.IOneWayBindableValue<int> b = factory.CreateOneWay(l);
            int x = b.Value;
            return S.Hold(x);
        });

    // S36. A bindable built from the placeholder, read after the block.
    public static int BindableReadAfter(SodaFlow.Bindable.ObjectModel.IBindableFactory factory)
    {
        (Cell<int> _, SodaFlow.Bindable.ObjectModel.IOneWayBindableValue<int> b) =
            Cell.Loop<int>().WithCaptures(l => (Cell: S.Hold(1), Captures: factory.CreateOneWay(l)));
        return b.Value;
    }

    // S37. A row built in the block from a pending cell, its property sampled in the block.
    public static Cell<decimal> RowPropertyReadInside() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row.Share.Sample();
            return Cell.Constant(2m + now);
        });

    // S38. The same row, its property sampled after the block.
    public static decimal RowPropertyReadAfter()
    {
        (Cell<decimal> _, Row row) =
            Cell.Loop<decimal>().WithCaptures(total =>
                (Cell: Cell.Constant(2m), Captures: new Row(share: Cell.Constant(1m).Lift(total, (a, b) => a / b))));
        return row.Share.Sample();
    }

    // S39. The row's property sampled in the block through a method on the row.
    public static Cell<decimal> RowMethodReadInside() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Cell.Constant(row.ShareNow());
        });

    // S40. A member of the tainted row built from a plain cell, read in the block.
    public static Cell<decimal> RowPlainMemberReadInside() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row.Plain.Sample();
            return Cell.Constant(2m + now);
        });

    // S41. A row with an explicit constructor that assigns this.Share from a Map of the parameter.
    public static Cell<decimal> ExplicitRowReadInside() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            ExplicitRow row = new(Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row.Share.Sample();
            return Cell.Constant(2m + now);
        });

    // S42. The row added to a list and the row's computed property read in.
    public static Cell<decimal> RowIntoListAndComputedProperty() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            List<Row> rows = [];
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            rows.Add(row);
            return Cell.Constant(row.ShareValue);
        });

    // S43. A close whose initial value is a lazy sample of the loop, with no link.
    public static Cell<int> CloseWithOwnLazySample() =>
        Cell.Loop<int>().WithoutCaptures(l => Stream.Never<int>().HoldLazy(l.SampleLazy()));

    // S44. A hold of the row, mapped to its lifted member and switched, as the close.
    public static Cell<decimal> CloseThroughHeldRow() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Stream.Never<Row>().Hold(row).Map(r => r.Share).SwitchC();
        });

    // S45. A lambda stored in a local and returned as a capture.
    public static int StoredLambdas()
    {
        (Cell<int> _, Func<int> f) =
            Cell.Loop<int>().WithCaptures(l =>
            {
                Func<int> later = () => l.Sample();
                Func<int> now = () => l.Sample();
                int x = now();
                return (Cell: S.Hold(x), Captures: later);
            });
        return f();
    }

    // S46. A lambda given to Task.Run, and an event subscription.
    public static Cell<int> ThreadAndEvent(Action<Action> subscribe) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            System.Threading.Tasks.Task.Run(() => l.Sample());
            Changed += () => l.Sample();
            return S.Hold(0);
        });

    public static event Action? Changed;

    // S47. LazySample.Defer over a lambda that samples the placeholder, held and sampled.
    public static Cell<int> DeferredSampleRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Stream.Never<int>().HoldLazy(LazySample.Defer(() => l.Sample() + 1)).Sample(); return S.Hold(x); });

    // S48. A block-bodied helper that maps its argument, sampled.
    public static Cell<int> BlockHelpers() =>
        Cell.Loop<int>().WithoutCaptures(l => { int a = Doubled(l).Sample(); int b = Fresh(l).Sample(); return S.Hold(a + b); });

    private static Cell<int> Doubled(Cell<int> c)
    {
        Cell<int> d = c.Map(v => v * 2);
        return d;
    }

    private static Cell<int> Fresh(Cell<int> c)
    {
        Cell<int> d = Cell.Constant(c.GetHashCode());
        return d;
    }

    // S49. A hold of the row, mapped to its plain member and switched, as the close. The loop is
    // then the plain member, which is zero, thus this share adds and does not divide.
    public static Cell<decimal> CloseThroughHeldRowPlainMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a + b));
            return Stream.Never<Row>().Hold(row).Map(r => r.Plain).SwitchC();
        });

    // S50. A nested loop block whose inner close maps the outer placeholder, then sampled in.
    public static Cell<int> NestedLoop() =>
        Cell.Loop<int>().WithoutCaptures(outer =>
        {
            Cell<int> inner = Cell.Loop<int>().WithoutCaptures(i => outer.Map(v => v + 1));
            int x = inner.Sample();
            return inner.Map(v => v + x);
        });

    // S51. A row held, mapped to its pending member and switched, used after the block.
    public static int HeldRowReadAfter()
    {
        (Cell<decimal> _, Cell<decimal> share) =
            Cell.Loop<decimal>().WithCaptures(total =>
            {
                Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
                return (Cell: Cell.Constant(2m), Captures: Stream.Never<Row>().Hold(row).Map(r => r.Share).SwitchC());
            });
        return (int)share.Sample();
    }

    // S52. Helper chains.
    public static Cell<int> HelperChains() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int two = Two(l).Sample();
            int three = Three(l).Sample();
            int four = Four(l).Sample();
            return S.Hold(two + three + four);
        });

    private static Cell<int> One(Cell<int> c) => c.Map(v => v + 1);
    private static Cell<int> Two(Cell<int> c) => One(c);
    private static Cell<int> Three(Cell<int> c) => Two(c);
    private static Cell<int> Four(Cell<int> c) => Three(c);

    // S53. A row method that reads a pending member through a second method and a computed property.
    public static Cell<decimal> RowMethodThroughSecondMethod() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Cell.Constant(row.Total() + row.Report.Length);
        });

    // S54. A helper that samples its parameter through a second helper.
    public static Cell<int> HelperSamplesThroughHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadViaOne(l) + ReadViaThree(l)));

    private static int ReadViaOne(Cell<int> c) => ReadIt(c);
    private static int ReadViaTwo(Cell<int> c) => ReadViaOne(c);
    private static int ReadViaThree(Cell<int> c) => ReadViaTwo(c);

    // S55. A constructor whose initializer calls a helper on the parameter.
    public static Cell<decimal> ConstructorInitializerCallsHelper() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            HalvingRow row = new(Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row.Half.Sample();
            return Cell.Constant(now);
        });

    // S56. A self-recursive helper.
    public static Cell<int> RecursiveHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Forever(l).Sample(); return S.Hold(x); });

    private static Cell<int> Forever(Cell<int> c) => Forever(c);

    // S57. The Bounce shape.
    public static Stream<int> ObjectsInASink() =>
        Stream.Loop<int>().WithoutCaptures(activated =>
        {
            Scene first = new(activated.Filter(i => i == 0));
            Scene second = new(activated.Filter(i => i == 1));
            Scene[] scenes = [first, second];
            CellSink<Scene> selected = Cell.CreateSink(scenes[0]);
            return selected.Updates().Map(scene => Array.IndexOf(scenes, scene));
        });

    // S58. A sink of the row, mapped to its lifted member and switched, as the close.
    public static Cell<decimal> CloseThroughSinkOfRow() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Cell.CreateSink(row).Map(r => r.Share).SwitchC();
        });

    // S59. The assertion at the argument on a derived value.
    public static Cell<int> AssertionAtTheArgument(Func<Cell<int>, int> unknown) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int stored = unknown(l.Map(v => v + 1).AsCell());
            SamplingRow row = new(l.Map(v => v + 2).AsCell());
            return S.Hold(stored + row.Value);
        });

    // S60. A Core block with captures, read before the close. Only the library can write one, as here.
    public static Cell<int> CoreBlockSample() =>
        TransactionInternal.Apply(code: (trans, isNew) =>
            LoopedCell<int>.WithCapturesImpl(
                    trans: trans,
                    state: Plain,
                    f: static (l, plain) => { int x = l.Sample(); return (Cell: S.Hold(x), Captures: plain); })
                .Cell);

    // S61. A Core block without captures, closed with its own placeholder.
    public static Cell<int> CoreBlockCloseWithPlaceholder() =>
        TransactionInternal.Apply(code: (trans, isNew) =>
            LoopedCell<int>.WithoutCapturesImpl(trans: trans, state: 0, f: static (l, _) => l));

    // S62. Named arguments in a sequence that is not the sequence of the parameters. The helper
    // returns its first parameter, which gets the loop.
    public static Cell<int> NamedArgumentsReorderedToTheLoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = PickFirst(b: Plain, a: l).Sample(); return S.Hold(x); });

    // S63. The same call with the two values exchanged. The helper returns the plain cell.
    public static Cell<int> NamedArgumentsReorderedToThePlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = PickFirst(b: l, a: Plain).Sample(); return S.Hold(x); });

    private static Cell<int> PickFirst(Cell<int> a, Cell<int> b) => a;

    // S64. A generic helper that samples its parameter.
    public static Cell<int> GenericHelperSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadGeneric(l)));

    private static T ReadGeneric<T>(Cell<T> c) => c.Sample();

    // S65. A generic row whose constructor samples its parameter.
    public static Cell<int> GenericRowSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericSamplingRow<int> row = new(l); return S.Hold(row.Value); });

    // S66. A generic helper that only derives from its parameter.
    public static Cell<int> GenericHelperDerives() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Snapshot(Same(l), (a, b) => a + b).Hold(0));

    private static Cell<T> Same<T>(Cell<T> c) => c.Map(v => v);

    // S67. A generic row that only stores its parameter.
    public static Cell<int> GenericRowStores() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            GenericStoringRow<int> row = new(l);
            return S.Snapshot(row.Cell, (a, b) => a + b).Hold(0);
        });

    // S68. A collection view filtered by a looped criteria cell, read in the block.
    public static Cell<bool> ViewFilteredByTheLoopRead() =>
        Cell.Loop<bool>().WithoutCaptures(l =>
        {
            ReactiveCollection<int, ItemId, int> view =
                Items().Filter(criteriaCell: l, predicate: (show, _, state) => show || state > 10);
            int count = view.KeysCell.Sample().Count;
            return Cell.Constant(count > 0);
        });

    // S69. The same view, with its keys as the close. The keys are linked to the loop.
    public static Cell<bool> ViewFilteredByTheLoopAsTheClose() =>
        Cell.Loop<bool>().WithoutCaptures(l =>
        {
            ReactiveCollection<int, ItemId, int> view =
                Items().Filter(criteriaCell: l, predicate: (show, _, state) => show || state > 10);
            return view.KeysCell.Map(keys => keys.Count > 0);
        });

    // S70. A view that gets its limit from the loop, then a plain predicate, read in the block.
    public static Cell<int> FilterOfPendingViewRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = Items().Take(limitCell: l).Filter(predicate: (_, state) => state > 10).KeysCell.Sample().Count;
            return Cell.Constant(count);
        });

    // S71. The view filtered by the loop, kept as a capture and read after the block.
    public static int ViewFilteredByTheLoopReadAfter()
    {
        (Cell<bool> _, ReactiveCollection<int, ItemId, int> view) =
            Cell.Loop<bool>().WithCaptures(l =>
                (Cell: Cell.Constant(true),
                    Captures: Items().Filter(criteriaCell: l, predicate: (show, _, state) => show || state > 10)));
        return view.KeysCell.Sample().Count;
    }

    private static ReactiveCollection<int, ItemId, int> Items() =>
        ReactiveCollection.Create<int, ItemId, int>(
            initialEntries: [new Item<ItemId, int>(identity: new ItemId(1), state: 10)],
            Stream.Never<CollectionEdit<int, ItemId, int>>());

    // S72. A read through a conditional access.
    public static Cell<int> ConditionalAccessSample() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int>? c = l.Map(v => v);
            int x = c?.Sample() ?? 0;
            return S.Hold(x);
        });

    // S73. A Map through a conditional access, then a read.
    public static Cell<int> ConditionalAccessMapSample() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int>? c = l;
            int x = c?.Map(v => v + 1).Sample() ?? 0;
            return S.Hold(x);
        });

    // S74. The value of a bindable through a conditional access.
    public static Cell<int> ConditionalAccessBindableValue(SodaFlow.Bindable.ObjectModel.IBindableFactory factory) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            SodaFlow.Bindable.ObjectModel.IOneWayBindableValue<int>? b = factory.CreateOneWay(l);
            int x = b?.Value ?? 0;
            return S.Hold(x);
        });

    // S75. The pending member of a row through a conditional access.
    public static Cell<decimal> ConditionalAccessRowMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row? row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row?.Share.Sample() ?? 0m;
            return Cell.Constant(2m + now);
        });

    // S76. A static call of the extension method that reads.
    public static Cell<int> StaticFormSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = CellExtensionMethods.Sample(l); return S.Hold(x); });

    // S77. A static call of Map, then a read.
    public static Cell<int> StaticFormMapSample() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = CellExtensionMethods.Map(l, v => v + 1).Sample();
            return S.Hold(x);
        });

    // S78. A static call of Map with named arguments and the function first, then a read.
    public static Cell<int> StaticFormNamedMapSample() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = CellExtensionMethods.Map(f: v => v + 1, c: l).Sample();
            return S.Hold(x);
        });

    // S79. A static call of Hold with a given value, which is not pending.
    public static Cell<int> StaticFormHoldWithValue() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = StreamExtensionMethods.Hold(l.Updates(), 7).Sample();
            return S.Hold(x);
        });

    // S80. A local function as the block, by its name.
    public static Cell<int> LocalFunctionBlock()
    {
        return Cell.Loop<int>().WithoutCaptures(Build);

        static Cell<int> Build(LoopedCell<int> l) { int x = l.Sample(); return S.Hold(x); }
    }

    // S81. An anonymous method as the block.
    public static Cell<int> AnonymousMethodBlock() =>
        Cell.Loop<int>().WithoutCaptures(delegate (LoopedCell<int> l) { int x = l.Sample(); return S.Hold(x); });

    // S82. A method as the block, by its name. The read is in the method, and the analyzer reports
    // there.
    public static Cell<int> MethodBlock() => Cell.Loop<int>().WithoutCaptures(BlockThatReads);

    private static Cell<int> BlockThatReads(LoopedCell<int> l) { int x = l.Sample(); return S.Hold(x); }

    // S83. A method with an expression body as the block. It closes the loop with the placeholder.
    public static Cell<int> MethodBlockCloses() => Cell.Loop<int>().WithoutCaptures(BlockThatClosesWithItself);

    private static Cell<int> BlockThatClosesWithItself(LoopedCell<int> l) => l;

    // S84. A method as the block that reads only in a snapshot.
    public static Cell<int> MethodBlockSnapshots() => Cell.Loop<int>().WithoutCaptures(BlockThatSnapshots);

    private static Cell<int> BlockThatSnapshots(LoopedCell<int> l) => S.Snapshot(l, (a, b) => a + b).Hold(0);

    // S85. A positional record built from a pending cell, its property sampled in the block.
    public static Cell<int> RecordPositionalMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            RecordRow row = new(l.Map(v => v));
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S86. A positional record with a pending member and a plain one. The block samples the plain one.
    public static Cell<int> RecordPlainMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            RecordPair pair = new(Pending: l.Map(v => v), Plain: Plain);
            int x = pair.Plain.Sample();
            return S.Hold(x);
        });

    // S87. A with expression that sets a pending member, sampled in the block.
    public static Cell<int> RecordWithPendingMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            RecordRow row = new RecordRow(Plain) with { Share = l.Map(v => v) };
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S88. A with expression that replaces a pending member with a plain one, sampled.
    public static Cell<int> RecordWithPlainMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            RecordRow row = new RecordRow(l.Map(v => v)) with { Share = Plain };
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S89. An object initializer that sets a pending member, sampled in the block.
    public static Cell<int> ObjectInitializerMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            InitRow row = new() { Share = l.Map(v => v) };
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S90. The same initializer with the type named and no argument list.
    public static Cell<int> ObjectInitializerNoArgumentList() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            InitRow row = new InitRow { Share = l.Map(v => v) };
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S91. A read of the placeholder in an object initializer.
    public static Cell<int> ObjectInitializerReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            ValueRow row = new() { Value = l.Sample() };
            return S.Hold(row.Value);
        });

    // S92. A member that the base constructor sets from a pending cell, sampled in the block.
    public static Cell<int> BaseConstructorMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            DerivedRow row = new(l.Map(v => v));
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S93. The same row, with the member read only in a snapshot.
    public static Cell<int> BaseConstructorMemberSnapshot() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            DerivedRow row = new(l.Map(v => v));
            return S.Snapshot(row.Share, (a, b) => a + b).Hold(0);
        });

    // S94. A base constructor that samples the parameter that the derived constructor passes on.
    public static Cell<int> BaseConstructorSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { SamplingDerivedRow row = new(l); return S.Hold(row.Value); });

    // S95. Written constructors: one calls the base, and the base calls a second constructor.
    public static Cell<int> WrittenConstructorChain() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            WrittenDerivedRow row = new(l.Map(v => v));
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S96. A collection initializer with a pending cell, an element sampled in the block.
    public static Cell<int> CollectionInitializerElement() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            List<Cell<int>> cells = new() { l.Map(v => v) };
            int x = cells[0].Sample();
            return S.Hold(x);
        });

    // S97. A helper that derives from its parameter and samples the derived value.
    public static Cell<int> HelperDerivesThenSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadDerived(l)));

    private static int ReadDerived(Cell<int> c) { int x = c.Map(v => v).Sample(); return x; }

    // S98. A helper that copies its parameter to a local and samples the local.
    public static Cell<int> HelperLocalThenSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadLocal(l)));

    private static int ReadLocal(Cell<int> c) { Cell<int> d = c; return d.Sample(); }

    // S99. A row passed to a helper that samples the pending member of the row.
    public static Cell<decimal> RowPassedToHelper() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Cell.Constant(ReadRow(row));
        });

    private static decimal ReadRow(Row r) => r.Share.Sample();

    // S100. The same row passed to a helper that samples its plain member.
    public static Cell<decimal> RowPassedToHelperPlainMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a + b));
            return Cell.Constant(ReadPlainOfRow(row));
        });

    private static decimal ReadPlainOfRow(Row r) => r.Plain.Sample();

    // S101. A method of the row that derives from the pending member and samples.
    public static Cell<decimal> RowMethodDerivedRead() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Cell.Constant(row.ShareMappedNow());
        });

    // S102. A helper that returns a local from a branch. The block samples the result.
    public static Cell<int> HelperBranchReturn() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Branch(l, true).Sample(); return S.Hold(x); });

    // S103. The same helper, with the result read only in a snapshot.
    public static Cell<int> HelperBranchReturnSnapshot() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Snapshot(Branch(l, true), (a, b) => a + b).Hold(0));

    private static Cell<int> Branch(Cell<int> c, bool flag)
    {
        if (flag)
        {
            Cell<int> inner = c.Map(v => v);
            return inner;
        }

        return Plain;
    }

    // S104. A property with a getter that derives from the pending member, sampled in the block.
    public static Cell<decimal> ComputedPropertyOfPendingMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row.ShareDoubled.Sample();
            return Cell.Constant(2m + now);
        });

    // S105. A property with a getter that derives from the plain member, sampled in the block.
    public static Cell<decimal> ComputedPropertyOfPlainMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a + b));
            decimal now = row.PlainDoubled.Sample();
            return Cell.Constant(2m + now);
        });

    // S106. A helper that gives its parameter to code the analyzer cannot read.
    public static Cell<int> HelperPassesOn(Func<Cell<int>, int> unknown) =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(PassOn(l, unknown)));

    // S107. The same call with the assertion at the argument of the block.
    public static Cell<int> HelperPassesOnAsserted(Func<Cell<int>, int> unknown) =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(PassOn(l.AsCell(), unknown)));

    private static int PassOn(Cell<int> c, Func<Cell<int>, int> unknown) => unknown(c);

    // S108. A local function that reads, called only from a callback.
    public static Cell<int> LocalFunctionOnlyInCallback() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            return S.Map(v => v + Read()).Hold(0);

            int Read() => l.Sample();
        });

    // S109. A local function that reads, given to an operator as the callback.
    public static Cell<int> LocalFunctionAsCallback() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            return S.Map(Later).Hold(0);

            int Later(int v) => v + l.Sample();
        });

    // S110. A local function that reads, which nothing calls.
    public static Cell<int> LocalFunctionNeverCalled() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            return S.Hold(0);

            int Unused() => l.Sample();
        });

    // S111. A local function that samples its parameter, called with the placeholder.
    public static Cell<int> LocalFunctionWithParameter() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Read(l);
            return S.Hold(x);

            int Read(Cell<int> c) => c.Sample();
        });

    // S112. A local function that samples a local of the block, derived from the placeholder.
    public static Cell<int> LocalFunctionReadsALocal() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> d = l.Map(v => v);
            return S.Hold(Read());

            int Read() => d.Sample();
        });

    // S113. A lambda in a local, run through Invoke.
    public static Cell<int> StoredLambdaThroughInvoke() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Func<int> f = () => l.Sample();
            int x = f.Invoke();
            return S.Hold(x);
        });

    // S114. A lambda in a local, run through a conditional Invoke.
    public static Cell<int> StoredLambdaThroughConditionalInvoke() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Func<int>? f = () => l.Sample();
            int x = f?.Invoke() ?? 0;
            return S.Hold(x);
        });

    // S115. A lambda in a local that samples its parameter, called with the placeholder.
    public static Cell<int> StoredLambdaWithParameter() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Func<Cell<int>, int> read = c => c.Sample();
            int x = read(l);
            return S.Hold(x);
        });

    // S116. The same lambda, called with a plain cell.
    public static Cell<int> StoredLambdaWithPlainArgument() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Func<Cell<int>, int> read = c => c.Sample();
            int x = read(Plain);
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S117. A method of a row reads a parameter of the primary constructor. Only a callback calls it.
    public static Cell<int> PrimaryConstructorCaptureInCallback() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            CapturingRow row = new(l);
            return S.Map(v => v + row.Now()).Hold(0);
        });

    // S118. The same method, called in the block.
    public static Cell<int> PrimaryConstructorCaptureCalled() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            CapturingRow row = new(l);
            return S.Hold(row.Now());
        });

    // S119. A lambda given to a constructor that keeps it. Only a callback runs it.
    public static Cell<int> LambdaKeptByConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Command command = new(() => l.Sample());
            return S.Map(v => v + command.Run()).Hold(0);
        });

    // S120. The same object, with the lambda run in the block.
    public static Cell<int> LambdaKeptByConstructorThenRun() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Command command = new(() => l.Sample());
            return S.Hold(command.Run());
        });

    // S121. A lambda given to a method that keeps it in a list.
    public static Cell<int> LambdaKeptByMethod() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Register(() => l.Sample());
            return S.Hold(0);
        });

    private static readonly List<Func<int>> Handlers = [];

    private static void Register(Func<int> handler) => Handlers.Add(handler);

    // S122. A lambda given to a method that runs it.
    public static Cell<int> LambdaRunByMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(RunIt(() => l.Sample())));

    private static int RunIt(Func<int> f) => f();

    // S123. A lambda that gives a pending cell to a method, which samples that cell.
    public static Cell<int> LambdaResultReadByMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadResult(() => l.Map(v => v))));

    private static int ReadResult(Func<Cell<int>> f) => f().Sample();

    // S124. A method that calls its function with its second argument, which is the placeholder.
    public static Cell<int> LambdaGivenTheLoopByMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ApplyTo(c => c.Sample(), l)));

    private static int ApplyTo(Func<Cell<int>, int> f, Cell<int> c) => f(c);

    // S125. A function in a cell of cells that gives a local of the block, switched, as the close.
    public static Cell<int> ContentFunctionGivesALocal() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> d = l.Map(v => v);
            return Cell.Constant(1).Map(_ => d).SwitchC();
        });

    // S126. The variable of a foreach over cells derived from the placeholder.
    public static Cell<int> ForeachVariable() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = 0;
            foreach (Cell<int> c in new[] { l.Map(v => v) }) { x += c.Sample(); }
            return S.Hold(x);
        });

    // S127. The variable of a foreach over plain cells.
    public static Cell<int> ForeachOverPlainCells() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = 0;
            foreach (Cell<int> c in new[] { Plain }) { x += c.Sample(); }
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S128. A deconstruction into locals. The first part is derived from the placeholder.
    public static Cell<int> Deconstruction() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            (Cell<int> a, int b) = (l.Map(v => v), 1);
            int x = a.Sample() + b;
            return S.Hold(x);
        });

    // S129. The same deconstruction, with a plain second part that the block samples.
    public static Cell<int> DeconstructionPlainPart() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            (Cell<int> a, Cell<int> b) = (l.Map(v => v), Plain);
            int x = b.Sample();
            return S.Snapshot(a, (u, v) => u + v + x).Hold(0);
        });

    // S130. A variable of a pattern.
    public static Cell<int> PatternVariable() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = 0;
            if (l.Map(v => v) is { } m) { x = m.Sample(); }
            return S.Hold(x);
        });

    // S131. A variable of a pattern for the pending member of a row.
    public static Cell<decimal> PatternVariableOfRowMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = row is { Share: var share } ? share.Sample() : 0m;
            return Cell.Constant(2m + now);
        });

    // S132. A variable of a pattern for the plain member of the same row.
    public static Cell<decimal> PatternVariableOfPlainRowMember() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a + b));
            decimal now = row is { Plain: var plain } ? plain.Sample() : 0m;
            return Cell.Constant(2m + now);
        });

    // S133. A variable of a pattern in a switch statement.
    public static Cell<int> SwitchPatternVariable() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            object o = l.Map(v => v);
            int x = 0;

            switch (o)
            {
                case Cell<int> c:
                    x = c.Sample();
                    break;
            }

            return S.Hold(x);
        });

    // S134. An out variable that a helper sets from its pending parameter.
    public static Cell<int> OutVariable() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Derive(l, out Cell<int> d);
            int x = d.Sample();
            return S.Hold(x);
        });

    private static void Derive(Cell<int> c, out Cell<int> d) => d = c.Map(v => v);

    // S135. An out variable that a helper sets to a plain cell.
    public static Cell<int> OutVariablePlain() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            GivePlain(l, out Cell<int> d);
            int x = d.Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    private static void GivePlain(Cell<int> c, out Cell<int> d) => d = Plain;

    private static Cell<int>? held;

    // S136. A static field that the block sets, then samples.
    public static Cell<int> StaticField() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            held = l.Map(v => v);
            int x = held.Sample();
            return S.Hold(x);
        });

    // S137. A static field that the block sets, sampled in a helper.
    public static Cell<int> StaticFieldReadByHelper() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            held = l.Map(v => v);
            return S.Hold(ReadHeld());
        });

    private static int ReadHeld() => held!.Sample();

    // S138. A list that the block fills with Add, then reads by index.
    public static Cell<int> ListAddThenIndex() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            List<Cell<int>> cs = [];
            cs.Add(l.Map(v => v));
            int x = cs[0].Sample();
            return S.Hold(x);
        });

    // S139. The same list, filled with a plain cell.
    public static Cell<int> ListAddOfPlainThenIndex() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            List<Cell<int>> cs = [];
            cs.Add(Plain);
            int x = cs[0].Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S140. A list that only keeps a pending cell. Nothing reads it in the block.
    public static Cell<int> ListOnlyKeeps() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(l.Map(v => v));
            return S.Hold(0);
        });

    private static readonly List<object> Kept = [];

    // S141. A LINQ lambda that samples its own parameter, which is an element derived from the loop.
    public static Cell<int> LinqLambdaParameter() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = new[] { l.Map(v => v) }.Select(c => c.Sample()).First();
            return S.Hold(x);
        });

    // S142. The first element of a list of pending cells, sampled.
    public static Cell<int> FirstOfPendingList() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            List<Cell<int>> cs = [l.Map(v => v)];
            int x = cs.First().Sample();
            return S.Hold(x);
        });

    // S143. A dictionary filled with Add, read through TryGetValue.
    public static Cell<int> DictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Dictionary<int, Cell<int>> d = new();
            d.Add(1, l.Map(v => v));
            int x = d.TryGetValue(1, out Cell<int>? c) ? c.Sample() : 0;
            return S.Hold(x);
        });

    // S144. A member that the block sets on an object, then samples.
    public static Cell<int> MemberAssignedInTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            SettableRow row = new();
            row.Share = l.Map(v => v);
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S145. A second member of the same object, set to a plain cell and sampled.
    public static Cell<int> PlainMemberAssignedInTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            SettableRow row = new();
            row.Share = l.Map(v => v);
            row.Other = Plain;
            int x = row.Other.Sample();
            return S.Snapshot(row.Share, (a, b) => a + b + x).Hold(0);
        });

    // S146. Match of a Maybe runs its function in the call.
    public static Cell<int> MaybeMatchRunsItsFunction() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Maybe.Some(1).Match(onSome: v => l.Sample() + v, onNone: () => 0);
            return S.Hold(x);
        });

    // S147. A Maybe of a pending cell. The function of Match gets that cell and samples it.
    public static Cell<int> MaybeOfPendingCellMatched() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Maybe.Some(l.Map(v => v)).Match(onSome: c => c.Sample(), onNone: () => 0);
            return S.Hold(x);
        });

    // S148. A Maybe of a plain cell, with the same Match.
    public static Cell<int> MaybeOfPlainCellMatched() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Maybe.Some(Plain).Match(onSome: c => c.Sample(), onNone: () => 0);
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S149. Fold of a collection runs select over the items that it has at construction.
    public static Cell<int> FoldSelectReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
            Items().Fold(select: (_, state) => state + l.Sample(), zero: 0, add: (a, b) => a + b, subtract: (a, b) => a - b));

    // S150. The same Fold keeps subtract for an edit after the close.
    public static Cell<int> FoldSubtractReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
            Items().Fold(select: (_, state) => state, zero: 0, add: (a, b) => a + b, subtract: (a, b) => a - b - l.Sample()));

    // S151. A Lazy keeps its function. The code forces it after the block.
    public static int LazyForcedAfter()
    {
        (Cell<int> _, Lazy<int> lazy) =
            Cell.Loop<int>().WithCaptures(l =>
            {
                Lazy<int> later = new(() => l.Sample());
                return (Cell: S.Hold(1), Captures: later);
            });

        return lazy.Value;
    }

    // S152. ToFunc gives back the function that it gets. The block calls the result.
    public static Cell<int> FunctionWrapperCalled() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Action read = () => { int x = l.Sample(); };
            read.ToFunc()();
            return S.Hold(0);
        });

    // S153. The same wrapper, which the block only keeps.
    public static Cell<int> FunctionWrapperKept() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Action read = () => { int x = l.Sample(); };
            Kept.Add(read.ToFunc());
            return S.Hold(0);
        });

    // S154. A local function given to a query, which runs it with an element derived from the loop.
    public static Cell<int> LocalFunctionGivenToAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = new[] { l.Map(v => v) }.Select(Read).First();
            return S.Hold(x);

            int Read(Cell<int> c) => c.Sample();
        });

    // S155. A query whose lambda gives a pending cell. The block samples the first result.
    public static Cell<int> QueryResultOfALambda() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = new[] { 1 }.Select(_ => l.Map(v => v)).First().Sample();
            return S.Hold(x);
        });

    // S156. A sample gives a pending cell, which the block samples.
    public static Cell<int> SampleOfPendingContent() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Cell.Constant(l.Map(v => v)).Sample().Sample();
            return S.Hold(x);
        });

    // S157. A sample gives a plain cell, which the block samples.
    public static Cell<int> SampleOfPlainContent() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Cell.Constant(Plain).Sample().Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S158. A sample of a sink gives a row, and the block samples the pending member of the row.
    public static Cell<decimal> RowFromSampleOfSink() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            decimal now = Cell.CreateSink(row).Sample().Share.Sample();
            return Cell.Constant(2m + now);
        });

    // S159. The value of a bindable is a pending cell, which the block samples.
    public static Cell<int> BindableValueIsAPendingCell(SodaFlow.Bindable.ObjectModel.IBindableFactory factory) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = factory.CreateOneWay(Cell.Constant(l.Map(v => v))).Value.Sample();
            return S.Hold(x);
        });

    // S160. A sample gives a list of pending cells. The block samples each one.
    public static Cell<int> ForeachOverASampledList() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<IReadOnlyList<Cell<int>>> cells = Cell.Constant<IReadOnlyList<Cell<int>>>([l.Map(v => v)]);
            int x = 0;
            foreach (Cell<int> c in cells.Sample()) { x += c.Sample(); }
            return S.Hold(x);
        });

    // S161. A local that held the placeholder gets a plain cell before the read.
    public static Cell<int> ReassignedLocal() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> c = l;
            c = Plain;
            int x = c.Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S162. The same local, with the plain cell given in a branch that does not run.
    public static Cell<int> ReassignedLocalInABranch() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> c = l;

            if (Never)
            {
                c = Plain;
            }

            int x = c.Sample();
            return S.Hold(x);
        });

    private static readonly bool Never = Plain == null;

    // S163. A local that held a plain cell gets a value derived from the placeholder before the read.
    public static Cell<int> ReassignedLocalToThePendingValue() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> c = Plain;
            c = l.Map(v => v);
            int x = c.Sample();
            return S.Hold(x);
        });

    // S164. The function of a Map samples the loop. The block samples the result.
    public static Cell<int> MapFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Plain.Map(v => v + l.Sample()).Sample(); return S.Hold(x); });

    // S165. The same Map, which the block only keeps. Its function runs at the first read.
    public static Cell<int> MapFunctionReadsNotSampled() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Plain.Map(v => v + l.Sample())); return S.Hold(0); });

    // S166. The function of a Lift samples the loop.
    public static Cell<int> LiftFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Plain.Lift(Plain, (a, b) => a + b + l.Sample()).Sample();
            return S.Hold(x);
        });

    // S167. The function of a Map of a behavior samples the loop.
    public static Cell<int> BehaviorMapFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Behavior.Constant(1).Map(v => v + l.Sample()).Sample();
            return S.Hold(x);
        });

    // S168. The function of a Map of a lazy sample samples the loop.
    public static Cell<int> LazyMapFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Cell.ConstantLazy(Plain.SampleLazy().Map(v => v + l.Sample())).Sample();
            return S.Hold(x);
        });

    // S169. The predicate of a view samples the loop. The block samples the keys of the view.
    public static Cell<int> ViewPredicateReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = Items().Filter(predicate: (_, state) => state > l.Sample()).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S170. The same view, which the block only keeps.
    public static Cell<int> ViewPredicateReadsNotSampled() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(Items().Filter(predicate: (_, state) => state > l.Sample()));
            return S.Hold(0);
        });

    // S171. A close with a Map whose function samples the loop. The value of the loop reads itself.
    public static Cell<int> CloseWithMapWhoseFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l => Plain.Map(v => v + l.Sample()));

    // S172. The function of a Calm samples the loop. Calm does not run it for the first value.
    public static Cell<int> CalmFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Plain.Calm((a, b) => l.Sample() == 0 && a == b).Sample();
            return S.Hold(x);
        });

    // S173. The function of an Accum samples the loop. It runs at a firing.
    public static Cell<int> AccumFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = S.Accum(0, (v, a) => a + v + l.Sample()).Sample();
            return S.Hold(x);
        });

    // S174. The function of a Map samples the loop through a helper.
    public static Cell<int> MapFunctionReadsThroughHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Plain.Map(v => v + ReadIt(l)).Sample(); return S.Hold(x); });

    // S175. A Map of a Map whose function samples the loop.
    public static Cell<int> MapOfMapFunctionReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Plain.Map(v => v + l.Sample()).Map(v => v + 1).Sample();
            return S.Hold(x);
        });

    // S176. A helper gives a tuple with a pending part and a plain part. The block samples the plain one.
    public static Cell<int> PlainPartOfATupleFromAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            (Cell<int> a, Cell<int> b) = PairOf(l);
            int x = b.Sample();
            return S.Snapshot(a, (u, v) => u + v + x).Hold(0);
        });

    // S177. The same tuple, with the pending part sampled.
    public static Cell<int> PendingPartOfATupleFromAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            (Cell<int> a, Cell<int> b) = PairOf(l);
            int x = a.Sample();
            return S.Snapshot(b, (u, v) => u + v + x).Hold(0);
        });

    private static (Cell<int> Derived, Cell<int> Plain) PairOf(Cell<int> c) => (c.Map(v => v), Plain);

    // S178. A tuple in a local, with the plain element sampled.
    public static Cell<int> PlainElementOfATupleLocal() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            (Cell<int> A, Cell<int> B) t = (l.Map(v => v), Plain);
            int x = t.B.Sample();
            return S.Snapshot(t.A, (u, v) => u + v + x).Hold(0);
        });

    // S179. The same tuple, with the pending element sampled.
    public static Cell<int> PendingElementOfATupleLocal() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            (Cell<int> A, Cell<int> B) t = (l.Map(v => v), Plain);
            int x = t.A.Sample();
            return S.Hold(x);
        });

    // S180. A deconstruction with var of the tuple from the helper, with the plain part sampled.
    public static Cell<int> VarDeconstructionPlainPart() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            var (a, b) = PairOf(l);
            int x = b.Sample();
            return S.Snapshot(a, (u, v) => u + v + x).Hold(0);
        });

    // S181. A row in a local of its interface type. The block samples the pending member.
    public static Cell<int> MemberThroughAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            IShared row = new SharedRow(l.Map(v => v));
            int x = row.Share.Sample();
            return S.Hold(x);
        });

    // S182. The same row. The block calls a method of the interface, which samples the member.
    public static Cell<int> MethodThroughAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            IShared row = new SharedRow(l.Map(v => v));
            return S.Hold(row.Now());
        });

    // S183. The same method, which only a callback calls.
    public static Cell<int> MethodThroughAnInterfaceInCallback() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            IShared row = new SharedRow(l.Map(v => v));
            return S.Map(v => v + row.Now()).Hold(0);
        });

    // S184. A constructor gives its object to a helper, which samples a member that the constructor set.
    public static Cell<int> ObjectGivesItselfToAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { SelfReader reader = new(l.Map(v => v)); return S.Hold(reader.Value); });

    // S185. A local gets a plain cell in a function that a list runs for each element. The list is
    // empty, thus the function does not run.
    public static Cell<int> AssignedInAFunctionThatDoesNotRun() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> c = l;
            new List<int>().ForEach(_ => { c = Plain; });
            int x = c.Sample();
            return S.Hold(x);
        });

    // S186. A local gets a plain cell in a transaction, which runs its function one time.
    public static Cell<int> AssignedInATransaction() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> c = l;
            Transaction.RunVoid(() => { c = Plain; });
            int x = c.Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S187. A local gets a plain cell in a local function that the block calls.
    public static Cell<int> AssignedByALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<int> c = l;
            Reset();
            int x = c.Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);

            void Reset() { c = Plain; }
        });

    // S188. A delegate gets a function with +=, and the block runs the delegate.
    public static Cell<int> DelegateGetsAFunctionThenRuns() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Action? all = null;
            all += () => { int x = l.Sample(); };
            all?.Invoke();
            return S.Hold(0);
        });

    // S189. The values of a dictionary that holds a pending cell.
    public static Cell<int> ValuesOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Dictionary<int, Cell<int>> d = new() { [1] = l.Map(v => v) };
            int x = d.Values.First().Sample();
            return S.Hold(x);
        });

    // S190. A constructor keeps a lambda that samples its parameter. A getter of the object runs
    // the lambda.
    public static Cell<int> FunctionKeptByAConstructorOfThisCode() =>
        Cell.Loop<int>().WithoutCaptures(l => { ReadingRow row = new(l.Map(v => v)); return S.Hold(row.Now); });

    // S191. The same row with a plain cell.
    public static Cell<int> FunctionKeptByAConstructorOfThisCodePlain() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            ReadingRow row = new(Plain);
            return S.Snapshot(l, (a, b) => a + b).Hold(row.Now);
        });

    // S192. A helper returns a lambda that samples the parameter of the helper. The block runs
    // the lambda.
    public static Cell<int> FunctionFromAHelperCalled() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<int> read = ReaderOf(l.Map(v => v)); return S.Hold(read()); });

    // S193. The same lambda, which the block only keeps.
    public static Cell<int> FunctionFromAHelperKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ReaderOf(l.Map(v => v))); return S.Hold(0); });

    private static Func<int> ReaderOf(Cell<int> c) => () => c.Sample();

    // S194. A Lazy keeps a function that samples the placeholder. The block reads its value, which
    // runs the function.
    public static Cell<int> LazyReadInTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int> lazy = new(() => l.Sample()); int x = lazy.Value; return S.Hold(x); });

    // S195. The same Lazy, which the block only keeps.
    public static Cell<int> LazyKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int> lazy = new(() => l.Sample()); Kept.Add(lazy); return S.Hold(0); });

    // S196. A Lazy whose function samples a plain cell.
    public static Cell<int> LazyOfAPlainCellRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<int> lazy = new(() => Plain.Sample());
            int x = lazy.Value;
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S197. Only a callback reads the value of the Lazy.
    public static Cell<int> LazyReadInACallback() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int> lazy = new(() => l.Sample()); return S.Map(v => v + lazy.Value).Hold(0); });

    // S198. A local function is the function of the Lazy.
    public static Cell<int> LazyOfALocalFunctionRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<int> lazy = new(Read);
            int x = lazy.Value;
            return S.Hold(x);

            int Read() => l.Sample();
        });

    // S199. A helper reads the value of the Lazy.
    public static Cell<int> LazyReadInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int> lazy = new(() => l.Sample()); return S.Hold(ValueOf(lazy)); });

    private static int ValueOf(Lazy<int> lazy) => lazy.Value;

    // S200. The function of the Lazy gives the placeholder to a helper that samples it.
    public static Cell<int> LazyWhoseFunctionCallsAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int> lazy = new(() => ReadIt(l)); int x = lazy.Value; return S.Hold(x); });

    // S201. A read of the value through a conditional access.
    public static Cell<int> LazyConditionalRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int>? lazy = new(() => l.Sample()); int x = lazy?.Value ?? 0; return S.Hold(x); });

    // S202. A Map of a Lazy keeps its function also. That function samples the placeholder.
    public static Cell<int> LazyMapRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = new Lazy<int>(() => 1).Map(v => v + l.Sample()).Value; return S.Hold(x); });

    // S203. A Map of a Lazy whose function samples the placeholder. The block only keeps it.
    public static Cell<int> LazyMapKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new Lazy<int>(() => l.Sample()).Map(v => v + 1)); return S.Hold(0); });

    // S204. The first function makes a pending cell, and the function of the Map samples it.
    public static Cell<int> LazyMapSamplesAMadeCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<Cell<int>> made = new(() => l.Map(v => v));
            int x = made.Map(c => c.Sample()).Value;
            return S.Hold(x);
        });

    // S205. The same Map over a Lazy that makes a plain cell.
    public static Cell<int> LazyMapSamplesAMadePlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<Cell<int>> made = new(() => Plain);
            int x = made.Map(c => c.Sample()).Value;
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S206. A Lazy holds a pending cell, and the function of the Map gives a plain one.
    public static Cell<int> LazyMapToAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<Cell<int>> first = new(l.Map(v => v));
            int x = first.Map(_ => Plain).Value.Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S207. A Lift of two Lazy values. The function of the second one samples the placeholder.
    public static Cell<int> LazyLiftRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = new Lazy<int>(() => 1).Lift(new Lazy<int>(() => l.Sample()), (a, b) => a + b).Value;
            return S.Hold(x);
        });

    // S208. A Lazy in a list.
    public static Cell<int> LazyInAListRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            List<Lazy<int>> list = [new Lazy<int>(() => l.Sample())];
            int x = list[0].Value;
            return S.Hold(x);
        });

    // S209. The local gets a second Lazy before the read.
    public static Cell<int> LazyReplacedThenRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<int> lazy = new(() => l.Sample());
            lazy = new Lazy<int>(() => 1);
            int x = lazy.Value;
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S210. A Lazy of a function holds a lambda as its value. The read of the value does not run
    // the lambda.
    public static Cell<int> LazyOfAFunctionRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Lazy<Func<int>> lazy = new(() => l.Sample());
            Kept.Add(lazy.Value);
            return S.Hold(0);
        });

    // S211. The same Lazy. The block calls the function that the value is.
    public static Cell<int> LazyOfAFunctionCalled() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<Func<int>> lazy = new(() => l.Sample()); int x = lazy.Value(); return S.Hold(x); });

    // S212. The function of a Lazy makes a lambda. The block calls that lambda.
    public static Cell<int> LazyOfAMadeFunctionCalled() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<Func<int>> lazy = new(() => () => l.Sample()); int x = lazy.Value(); return S.Hold(x); });

    // S213. The same Lazy. The block reads the value and does not call it.
    public static Cell<int> LazyOfAMadeFunctionRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<Func<int>> lazy = new(() => () => l.Sample()); Kept.Add(lazy.Value); return S.Hold(0); });

    // S214. A constructor keeps a Lazy whose function samples its parameter. A getter of the object
    // reads the value.
    public static Cell<int> LazyKeptByAConstructorOfThisCode() =>
        Cell.Loop<int>().WithoutCaptures(l => { LazyRow row = new(l.Map(v => v)); return S.Hold(row.Now); });

    // S215. A helper returns a Lazy whose function samples the parameter of the helper.
    public static Cell<int> LazyFromAHelperRead() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(LazyOf(l.Map(v => v)).Value));

    private static Lazy<int> LazyOf(Cell<int> c) => new(() => c.Sample());

    // S216. A hold of cells. The initial cell is plain, and a firing gives a pending one. A read in
    // the block gives the initial cell.
    public static Cell<int> HeldCellOfAFiringRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = S.Map(_ => l.Map(v => v)).Hold(Plain).Sample().Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S217. The same hold with the two cells exchanged. The initial cell is the pending one.
    public static Cell<int> HeldInitialCellRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = S.Map(_ => Plain).Hold(l.Map(v => v)).Sample().Sample();
            return S.Hold(x);
        });

    // S218. A hold of rows. Only the row of a firing has a pending member.
    public static Cell<int> HeldRowOfAFiringRead() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<SharedRow> rows = S.Map(_ => new SharedRow(l.Map(v => v))).Hold(new SharedRow(Plain));
            int x = rows.Sample().Now();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S219. A sort order whose function samples the placeholder. The block reads the keys of a
    // view that sorts by that order.
    public static Cell<int> SortByAnOrderThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample());
            int count = Items().SortBy(order: order).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S220. The same view, which the block only keeps.
    public static Cell<int> SortByAnOrderThatReadsKept() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample());
            Kept.Add(Items().SortBy(order: order));
            return S.Hold(0);
        });

    // S221. The same order in a cell. The view sorts by the order that the cell holds.
    public static Cell<int> SortByAnOrderCellThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample());
            int count = Items().SortBy(orderCell: Cell.Constant(order)).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S222. A cell of an order whose function reads no cell.
    public static Cell<int> SortByAPlainOrderCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By((_, state) => state);
            int count = Items().SortBy(orderCell: Cell.Constant(order)).KeysCell.Sample().Count;
            return S.Snapshot(l, (a, b) => a + b + count).Hold(0);
        });

    // S223. The second function of an order samples the placeholder.
    public static Cell<int> ThenByThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order =
                KeyOrder<int, ItemId, int>.By((_, state) => state).ThenBy((_, state) => state + l.Sample());

            int count = Items().SortBy(orderCell: Cell.Constant(order)).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S224. An order by identity whose function samples the placeholder.
    public static Cell<int> OrderByIdentityThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.ByIdentity(id => id.Number + l.Sample());
            int count = Items().SortBy(orderCell: Cell.Constant(order)).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S225. A helper makes the order, and the function of the order samples the parameter of the
    // helper.
    public static Cell<int> OrderFromAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = Items().SortBy(orderCell: Cell.Constant(OrderThatReads(l))).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    private static KeyOrder<int, ItemId, int> OrderThatReads(Cell<int> c) =>
        KeyOrder<int, ItemId, int>.By((_, state) => state + c.Sample());

    // S226. A local function is the function of the order.
    public static Cell<int> OrderOfALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By<int>(Selector);
            int count = Items().SortBy(orderCell: Cell.Constant(order)).KeysCell.Sample().Count;
            return S.Hold(count);

            int Selector(ItemId id, int state) => state + l.Sample();
        });

    // S227. The block makes the order and no view.
    public static Cell<int> OrderOnlyMade() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample()));
            return S.Hold(0);
        });

    // S228. Only a callback makes the view and reads its keys.
    public static Cell<int> OrderUsedInACallback() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample());
            return S.Snapshot(l, (a, b) => a + b + Items().SortBy(order: order).KeysCell.Sample().Count).Hold(0);
        });

    // S229. A map of a cell of orders adds a function that samples the placeholder.
    public static Cell<int> OrderCellMappedToAnOrderThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Cell<KeyOrder<int, ItemId, int>> orders =
                Cell.Constant(KeyOrder<int, ItemId, int>.By((_, state) => state))
                    .Map(order => order.ThenBy((_, state) => state + l.Sample()));

            int count = Items().SortBy(orderCell: orders).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S230. A cell of orders starts with a plain order. A firing gives the order that reads, which
    // is after the close.
    public static Cell<int> OrderCellThatStartsPlain() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> plain = KeyOrder<int, ItemId, int>.By((_, state) => state);
            KeyOrder<int, ItemId, int> reading = KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample());
            int count = Items().SortBy(orderCell: S.Map(_ => reading).Hold(plain)).KeysCell.Sample().Count;
            return S.Snapshot(l, (a, b) => a + b + count).Hold(0);
        });

    // S231. The loop closes with the keys of a view. The view sorts by an order cell, and the
    // function of the order samples the placeholder.
    public static Cell<int> CloseWithAViewSortedByAnOrderCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order = KeyOrder<int, ItemId, int>.By((_, state) => state + l.Sample());
            return Items().SortBy(orderCell: Cell.Constant(order)).KeysCell.Map(keys => keys.Count);
        });

    // S232. A property pattern reads a getter that samples a pending member.
    public static Cell<int> GetterInAPropertyPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { PatternRow row = new(l.Map(v => v)); return S.Hold(row is { Now: > 0 } ? 1 : 0); });

    // S233. The same pattern on a row with a plain member.
    public static Cell<int> GetterOfAPlainRowInAPropertyPattern() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternRow row = new(Plain);
            return S.Snapshot(l, (a, b) => a + b).Hold(row is { Now: > 0 } ? 1 : 0);
        });

    // S234. The same pattern in a callback.
    public static Cell<int> GetterInAPropertyPatternInACallback() =>
        Cell.Loop<int>().WithoutCaptures(l => { PatternRow row = new(l.Map(v => v)); return S.Map(v => row is { Now: > 0 } ? v : 0).Hold(0); });

    // S235. A property pattern reads the value of a Lazy whose function samples the placeholder.
    public static Cell<int> LazyValueInAPropertyPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { Lazy<int> lazy = new(() => l.Sample()); return S.Hold(lazy is { Value: > 0 } ? 1 : 0); });

    // S236. A property pattern reads the value of a bindable built from the placeholder.
    public static Cell<int> BindableValueInAPropertyPattern(SodaFlow.Bindable.ObjectModel.IBindableFactory factory) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            SodaFlow.Bindable.ObjectModel.IOneWayBindableValue<int> b = factory.CreateOneWay(l);
            return S.Hold(b is { Value: > 0 } ? 1 : 0);
        });

    // S237. The pattern is a label of a switch statement.
    public static Cell<int> GetterInASwitchStatement() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternRow row = new(l.Map(v => v));
            int x;

            switch (row)
            {
                case { Now: > 0 }:
                    x = 1;
                    break;
                default:
                    x = 0;
                    break;
            }

            return S.Hold(x);
        });

    // S238. The pattern is an arm of a switch expression.
    public static Cell<int> GetterInASwitchExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { PatternRow row = new(l.Map(v => v)); return S.Hold(row switch { { Now: > 0 } => 1, _ => 0 }); });

    // S239. The pattern reads a getter of a member of the object.
    public static Cell<int> GetterInANestedPattern() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternOuter outer = new(new PatternRow(l.Map(v => v)));
            return S.Hold(outer is { Inner: { Now: > 0 } } ? 1 : 0);
        });

    // S240. The same read, with the two names in one part of the pattern.
    public static Cell<int> GetterInAnExtendedPattern() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternOuter outer = new(new PatternRow(l.Map(v => v)));
            return S.Hold(outer is { Inner.Now: > 0 } ? 1 : 0);
        });

    // S241. The nested pattern on an object whose member has a plain cell.
    public static Cell<int> GetterInANestedPatternOfAPlainRow() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternOuter outer = new(new PatternRow(Plain));
            return S.Snapshot(l, (a, b) => a + b).Hold(outer is { Inner: { Now: > 0 } } ? 1 : 0);
        });

    // S242. A helper has the pattern.
    public static Cell<int> GetterInAPatternInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { PatternRow row = new(l.Map(v => v)); return S.Hold(NowIsBig(row) ? 1 : 0); });

    private static bool NowIsBig(PatternRow row) => row is { Now: > 0 };

    // S243. A method of a row with a pending member has a pattern on a different row, which is
    // plain. The name in the pattern is not a member of the row that the method is in.
    public static Cell<int> PatternOnAPlainRowInAMethodOfAPendingRow() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternRow mine = new(l.Map(v => v));
            bool big = mine.OtherIsBig(new PatternRow(Plain));
            return S.Snapshot(l, (a, b) => a + b).Hold(big ? 1 : 0);
        });

    // S244. The same method with the two rows exchanged.
    public static Cell<int> PatternOnAPendingRowInAMethodOfAPlainRow() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            PatternRow mine = new(Plain);
            bool big = mine.OtherIsBig(new PatternRow(l.Map(v => v)));
            return S.Hold(big ? 1 : 0);
        });

    // S245. The pattern has a not.
    public static Cell<int> GetterInANegatedPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { PatternRow row = new(l.Map(v => v)); return S.Hold(row is not { Now: 0 } ? 1 : 0); });

    // S246. The pattern has a type.
    public static Cell<int> GetterInATypePattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { object row = new PatternRow(l.Map(v => v)); return S.Hold(row is PatternRow { Now: > 0 } ? 1 : 0); });

    // S247. The pattern reads the getter through an interface.
    public static Cell<int> GetterThroughAnInterfaceInAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { IPatternRow row = new PatternRow(l.Map(v => v)); return S.Hold(row is { Now: > 0 } ? 1 : 0); });

    // S248. The pattern reads a member that holds a pending cell, and nothing samples the cell.
    public static Cell<int> MemberInAPatternNotSampled() =>
        Cell.Loop<int>().WithoutCaptures(l => { PatternRow row = new(l.Map(v => v)); return S.Hold(row is { Share: not null } ? 1 : 0); });

    private static ReactiveCollection<int, ItemId, int> TwoItems() =>
        ReactiveCollection.Create<int, ItemId, int>(
            initialEntries:
            [
                new Item<ItemId, int>(identity: new ItemId(1), state: 10),
                new Item<ItemId, int>(identity: new ItemId(2), state: 20)
            ],
            Stream.Never<CollectionEdit<int, ItemId, int>>());

    // S249. A sort gets a comparer object whose Compare samples a pending member. The block reads
    // the keys of the view, which runs the comparer.
    public static Cell<int> ComparerObjectInASort() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: new ReadingComparer(l.Map(v => v)),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Hold(count);
        });

    // S250. The same view, which the block only keeps.
    public static Cell<int> ComparerObjectInASortKept() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(
                TwoItems()
                    .SortBy(
                        selector: (_, state) => state,
                        sortComparer: new ReadingComparer(l.Map(v => v)),
                        keyComparer: Comparer<int>.Default,
                        isDescending: false));

            return S.Hold(0);
        });

    // S251. The same sort with a comparer object over a plain cell.
    public static Cell<int> ComparerObjectOfAPlainCellInASort() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: new ReadingComparer(Plain),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Snapshot(l, (a, b) => a + b + count).Hold(0);
        });

    // S252. The comparer object has a pending member and a plain one, and Compare samples only the
    // plain one.
    public static Cell<int> ComparerObjectThatSamplesItsPlainMember() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: new MixedComparer(l.Map(v => v), Plain),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Snapshot(l, (a, b) => a + b + count).Hold(0);
        });

    // S253. The comparer object is in a local of the interface type.
    public static Cell<int> ComparerObjectThroughTheInterface() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            IComparer<int> comparer = new ReadingComparer(l.Map(v => v));

            int count = TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: comparer,
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Hold(count);
        });

    // S254. The comparer object is the comparer of the keys. The two sort values are equal, thus
    // the sort compares the keys.
    public static Cell<int> KeyComparerObjectWithATie() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortBy(
                    selector: (_, _) => 0,
                    sortComparer: Comparer<int>.Default,
                    keyComparer: new ReadingComparer(l.Map(v => v)),
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Hold(count);
        });

    // S255. A sort by key with a comparer object.
    public static Cell<int> SortByKeyWithAComparerObject() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortByKey(keyComparer: new ReadingComparer(l.Map(v => v)), isDescending: false)
                .KeysCell.Sample().Count;

            return S.Hold(count);
        });

    // S256. An order by key with a comparer object, in a cell.
    public static Cell<int> OrderByKeyWithAComparerObject() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order =
                KeyOrder<int, ItemId, int>.ByKey(keyComparer: new ReadingComparer(l.Map(v => v)), isDescending: false);

            int count = TwoItems().SortBy(orderCell: Cell.Constant(order)).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S257. The second part of an order has the comparer object. The first parts are equal, thus
    // the sort compares the second parts.
    public static Cell<int> ThenByWithAComparerObjectAndATie() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            KeyOrder<int, ItemId, int> order =
                KeyOrder<int, ItemId, int>.By((_, _) => 0)
                    .ThenBy(
                        selector: (_, state) => state,
                        sortComparer: new ReadingComparer(l.Map(v => v)),
                        isDescending: false);

            int count = TwoItems().SortBy(order: order).KeysCell.Sample().Count;
            return S.Hold(count);
        });

    // S258. The block makes an order with the comparer object, and no view.
    public static Cell<int> ComparerObjectInAnOrderOnlyMade() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(
                KeyOrder<int, ItemId, int>.By(
                    selector: (_, state) => state,
                    sortComparer: new ReadingComparer(l.Map(v => v)),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false));

            return S.Hold(0);
        });

    // S259. A comparer from a function that samples the placeholder, in a sort that the block
    // reads.
    public static Cell<int> ComparerFromAFunctionInASort() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: Comparer<int>.Create((a, b) => a.CompareTo(b + l.Sample())),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Hold(count);
        });

    // S260. The same comparer, which the block only makes.
    public static Cell<int> ComparerFromAFunctionOnlyMade() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(Comparer<int>.Create((a, b) => a.CompareTo(b + l.Sample())));
            return S.Hold(0);
        });

    // S261. A helper makes the comparer from a function that samples the parameter of the helper.
    public static Cell<int> ComparerFromAHelperInASort() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int count = TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: ComparerThatReads(l.Map(v => v)),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Sample().Count;

            return S.Hold(count);
        });

    private static IComparer<int> ComparerThatReads(Cell<int> c) =>
        Comparer<int>.Create((a, b) => a.CompareTo(b + c.Sample()));

    // S262. A query of the platform gets the comparer object, and the block takes the first
    // element.
    public static Cell<int> ComparerObjectGivenToAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int first = new[] { 2, 1 }.OrderBy(keySelector: x => x, comparer: new ReadingComparer(l.Map(v => v))).First();
            return S.Hold(first);
        });

    // S263. MinOrNone gets the comparer object.
    public static Cell<int> ComparerObjectGivenToMinOrNone() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Maybe<int> min = new[] { 2, 1 }.MinOrNone(new ReadingComparer(l.Map(v => v)));
            return S.Hold(min.Match(v => v, () => 0));
        });

    // S264. Only a callback makes the view with the comparer object and reads its keys.
    public static Cell<int> ComparerObjectInASortInACallback() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            ReadingComparer comparer = new(l.Map(v => v));

            return S.Map(v =>
                    v
                    + TwoItems()
                        .SortBy(
                            selector: (_, state) => state,
                            sortComparer: comparer,
                            keyComparer: Comparer<int>.Default,
                            isDescending: false)
                        .KeysCell.Sample().Count)
                .Hold(0);
        });

    // S265. The loop closes with the keys of a view that sorts with a comparer object, and Compare
    // samples the placeholder.
    public static Cell<int> CloseWithAViewSortedByAComparerObject() =>
        Cell.Loop<int>().WithoutCaptures(l =>
            TwoItems()
                .SortBy(
                    selector: (_, state) => state,
                    sortComparer: new ReadingComparer(l),
                    keyComparer: Comparer<int>.Default,
                    isDescending: false)
                .KeysCell.Map(keys => keys.Count));

    // S266. A collection gets an equality comparer object for its keys. The methods of the
    // comparer sample a pending member, and the block reads the keys.
    public static Cell<int> EqualityComparerObjectInACollection() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            ReactiveCollection<int, ItemId, int> items =
                ReactiveCollection.Create<int, ItemId, int>(
                    keyEqualityComparer: new ReadingEquality(l.Map(v => v)),
                    initialEntries: [new Item<ItemId, int>(identity: new ItemId(1), state: 10)],
                    Stream.Never<CollectionEdit<int, ItemId, int>>());

            return S.Hold(items.KeysCell.Sample().Count);
        });

    // S267. The same collection, which the block only keeps.
    public static Cell<int> EqualityComparerObjectInACollectionKept() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(
                ReactiveCollection.Create<int, ItemId, int>(
                    keyEqualityComparer: new ReadingEquality(l.Map(v => v)),
                    initialEntries: [new Item<ItemId, int>(identity: new ItemId(1), state: 10)],
                    Stream.Never<CollectionEdit<int, ItemId, int>>()));

            return S.Hold(0);
        });

    // S268. Calm gets the equality comparer object, and compares at an update, which is after the
    // close.
    public static Cell<int> EqualityComparerObjectInCalm() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            int x = Plain.Calm(new ReadingEquality(l.Map(v => v))).Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S269. The key selector of a collection samples the placeholder, and the block reads the keys.
    public static Cell<int> KeySelectorOfACollectionThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            ReactiveCollection<int, ItemId, int> items =
                ReactiveCollection<int, ItemId, int>.Create(
                    keySelector: id => id.Number + l.Sample(),
                    initialItems: [new Item<ItemId, int>(identity: new ItemId(1), state: 10)],
                    Stream.Never<CollectionEdit<int, ItemId, int>>());

            return S.Hold(items.KeysCell.Sample().Count);
        });

    // S270. The same collection, which the block only keeps.
    public static Cell<int> KeySelectorOfACollectionThatReadsKept() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Kept.Add(
                ReactiveCollection<int, ItemId, int>.Create(
                    keySelector: id => id.Number + l.Sample(),
                    initialItems: [new Item<ItemId, int>(identity: new ItemId(1), state: 10)],
                    Stream.Never<CollectionEdit<int, ItemId, int>>()));

            return S.Hold(0);
        });

    // S271. The getter of an indexer samples a pending member.
    public static Cell<int> IndexerGetter() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); int x = t[0]; return S.Hold(x); });

    // S272. The same indexer on an object with a plain cell.
    public static Cell<int> IndexerGetterOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(Plain); int x = t[0]; return S.Snapshot(l, (a, b) => a + b + x).Hold(0); });

    // S273. The same indexer, which only a callback reads.
    public static Cell<int> IndexerGetterInACallback() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); return S.Map(v => v + t[0]).Hold(0); });

    // S274. The setter of the indexer samples the pending member.
    public static Cell<int> IndexerSetter() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); t[0] = 1; return S.Hold(0); });

    // S275. The getter of the indexer through a conditional access.
    public static Cell<int> IndexerGetterThroughAConditionalAccess() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally? t = new(l.Map(v => v)); int x = t?[0] ?? 0; return S.Hold(x); });

    // S276. An operator of the type samples a pending member of its operand.
    public static Cell<int> OperatorWithTwoOperands() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); int x = t + 1; return S.Hold(x); });

    // S277. The same operator on an object with a plain cell.
    public static Cell<int> OperatorWithTwoOperandsOnAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(Plain); int x = t + 1; return S.Snapshot(l, (a, b) => a + b + x).Hold(0); });

    // S278. An operator with one operand.
    public static Cell<int> OperatorWithOneOperand() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); int x = -t; return S.Hold(x); });

    // S279. The increment operator.
    public static Cell<int> IncrementOperator() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); t++; return S.Hold(0); });

    // S280. An assignment with += whose right side has the operator.
    public static Cell<int> OperatorInACompoundAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); int x = 0; x += t + 1; return S.Hold(x); });

    // S281. A condition on a value of a type with the true operator.
    public static Cell<int> TruthOperator() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); int x = t ? 1 : 0; return S.Hold(x); });

    // S282. A helper has the operator.
    public static Cell<int> OperatorInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); return S.Hold(TallyPlusOne(t)); });

    private static int TallyPlusOne(Tally t) => t + 1;

    // S283. An implicit conversion of the type samples a pending member.
    public static Cell<int> ImplicitConversion() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); long x = t; return S.Hold((int)x); });

    // S284. The same conversion at an argument of a method of the platform.
    public static Cell<int> ImplicitConversionAtAnArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); long x = Math.Abs(t); return S.Hold((int)x); });

    // S285. The same conversion on an object with a plain cell.
    public static Cell<int> ImplicitConversionOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(Plain); long x = t; return S.Snapshot(l, (a, b) => a + b + (int)x).Hold(0); });

    // S286. A conversion that the code writes as a cast.
    public static Cell<int> ExplicitConversion() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); short x = (short)t; return S.Hold(x); });

    // S287. An implicit conversion from a cell samples the cell, which is pending.
    public static Cell<int> ImplicitConversionFromAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyReading r = l.Map(v => v); return S.Hold(r.Value); });

    // S288. The same conversion from a plain cell.
    public static Cell<int> ImplicitConversionFromAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyReading r = Plain; return S.Snapshot(l, (a, b) => a + b + r.Value).Hold(0); });

    // S289. The setter of a property samples a pending member.
    public static Cell<int> PropertySetter() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); t.Level = 1; return S.Hold(0); });

    // S290. The same setter on an object with a plain cell.
    public static Cell<int> PropertySetterOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(Plain); t.Level = 1; return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S291. The same setter in an object initializer.
    public static Cell<int> PropertySetterInAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)) { Level = 1 }; Kept.Add(t); return S.Hold(0); });

    // S292. A setter samples the value that it gets, which is a pending cell.
    public static Cell<int> PropertySetterThatSamplesItsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyHolder h = new(); h.Source = l.Map(v => v); return S.Hold(0); });

    // S293. The same setter with a plain cell.
    public static Cell<int> PropertySetterThatSamplesAPlainValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyHolder h = new(); h.Source = Plain; return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S294. The add accessor of an event samples a pending member.
    public static Cell<int> EventAccessor() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); t.Changed += () => { }; return S.Hold(0); });

    // S295. A deconstruction calls a Deconstruct method that samples a pending member.
    public static Cell<int> DeconstructMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); (int x, int y) = t; return S.Hold(x + y); });

    // S296. The same deconstruction of an object with a plain cell.
    public static Cell<int> DeconstructMethodOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(Plain); (int x, int y) = t; return S.Snapshot(l, (a, b) => a + b + x + y).Hold(0); });

    // S297. A positional pattern calls the same method.
    public static Cell<int> DeconstructMethodInAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); return S.Hold(t is (> 0, _) ? 1 : 0); });

    // S298. A foreach calls the same method for each element.
    public static Cell<int> DeconstructMethodInAForEach() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            Tally[] all = [new Tally(l.Map(v => v))];
            int total = 0;

            foreach ((int x, int y) in all)
            {
                total += x + y;
            }

            return S.Hold(total);
        });

    // S299. A Deconstruct method gives a pending cell and a plain one. The block samples the
    // plain one.
    public static Cell<int> DeconstructMethodThatGivesAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            TallyPair pair = new(l.Map(v => v), Plain);
            (Cell<int> first, Cell<int> second) = pair;
            Kept.Add(first);
            int x = second.Sample();
            return S.Snapshot(l, (a, b) => a + b + x).Hold(0);
        });

    // S300. The same deconstruction. The block samples the pending one.
    public static Cell<int> DeconstructMethodThatGivesAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            TallyPair pair = new(l.Map(v => v), Plain);
            (Cell<int> first, Cell<int> second) = pair;
            Kept.Add(second);
            int x = first.Sample();
            return S.Hold(x);
        });

    // S301. An interpolation calls a ToString that samples a pending member.
    public static Cell<int> ToStringInAnInterpolation() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); string s = $"{t}"; return S.Hold(s.Length); });

    // S302. The same interpolation of an object with a plain cell.
    public static Cell<int> ToStringInAnInterpolationOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(Plain); string s = $"{t}"; return S.Snapshot(l, (a, b) => a + b + s.Length).Hold(0); });

    // S303. A concatenation calls the same ToString.
    public static Cell<int> ToStringInAConcatenation() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); string s = "n" + t; return S.Hold(s.Length); });

    // S304. A using statement calls a Dispose that samples a pending member.
    public static Cell<int> DisposeInAUsingStatement() =>
        Cell.Loop<int>().WithoutCaptures(l => { using (new Tally(l.Map(v => v))) { } return S.Hold(0); });

    // S305. A using declaration calls the same Dispose at the end of the block.
    public static Cell<int> DisposeInAUsingDeclaration() =>
        Cell.Loop<int>().WithoutCaptures(l => { using Tally t = new(l.Map(v => v)); return S.Hold(0); });

    // S306. The same using statement on an object with a plain cell.
    public static Cell<int> DisposeInAUsingStatementOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { using (new Tally(Plain)) { } return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S307. A foreach calls the GetEnumerator of a type of this file, which samples a pending member.
    public static Cell<int> ForEachOverAnEnumerableOfThisCode() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            TallyBag bag = new(l.Map(v => v));
            int total = 0;

            foreach (int v in bag)
            {
                total += v;
            }

            return S.Hold(total);
        });

    // S308. The same type, which the block only makes.
    public static Cell<int> EnumerableOfThisCodeOnlyMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyBag bag = new(l.Map(v => v)); Kept.Add(bag); return S.Hold(0); });

    // S309. A foreach over a type whose enumerator is an object of this file. Its Current samples a
    // pending member.
    public static Cell<int> ForEachOverAnEnumeratorOfThisCode() =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            TallyWalk walk = new(l.Map(v => v));
            int total = 0;

            foreach (int v in walk)
            {
                total += v;
            }

            return S.Hold(total);
        });

    // S310. A collection initializer calls an Add that samples a pending member.
    public static Cell<int> AddInACollectionInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyBag bag = new(l.Map(v => v)) { 1 }; Kept.Add(bag); return S.Hold(0); });

    // S311. The same initializer on an object with a plain cell.
    public static Cell<int> AddInACollectionInitializerOfAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyBag bag = new(Plain) { 1 }; Kept.Add(bag); return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S312. A list has the members of its elements in its state. ToString of the list is not ToString
    // of an element, which samples a pending member.
    public static Cell<int> ToStringOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; Kept.Add(all.ToString()!); return S.Hold(0); });

    // S313. The same for Equals of the list.
    public static Cell<int> EqualsOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> all = [new(l.Map(v => v))]; bool e = all.Equals(all); return S.Hold(e ? 1 : 0); });

    // S314. The same for GetHashCode of the list.
    public static Cell<int> GetHashCodeOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Hashed> all = [new(l.Map(v => v))]; int h = all.GetHashCode(); return S.Hold(h); });

    // S315. An interpolation calls ToString of the list, and not of an element.
    public static Cell<int> InterpolationOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; string s = $"{all}"; return S.Hold(s.Length); });

    // S316. A cast to object does not make the list one of its elements.
    public static Cell<int> ToStringOfAListCastToObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; Kept.Add(((object)all).ToString()!); return S.Hold(0); });

    // S317. A foreach over a list of sequences calls GetEnumerator of the list. It does not enumerate an
    // element, whose iterator samples a pending member.
    public static Cell<int> ForEachOverAListOfSequences() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<Sack> sacks = new List<Sack> { new(l.Map(v => v)) }; int n = 0; foreach (Sack sack in sacks) { Kept.Add(sack); n++; } return S.Hold(n); });

    // S318. A foreach over a sequence of this file whose iterator gives a pending cell. The body samples
    // the cell.
    public static Cell<int> ForEachOverAnIteratorOfCells() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellSack cells = new(l.Map(v => v)); int t = 0; foreach (Cell<int> c in cells) { t += c.Sample(); } return S.Hold(t); });

    // S319. The same with a plain cell.
    public static Cell<int> ForEachOverAnIteratorOfPlainCells() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellSack cells = new(Plain); int t = 0; foreach (Cell<int> c in cells) { t += c.Sample(); } return S.Snapshot(l, (a, b) => a + b + t).Hold(0); });

    // S320. A collection initializer calls an Add that samples the cell that it gets.
    public static Cell<int> AddInACollectionInitializerThatSamplesItsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Bin bin = new() { l.Map(v => v) }; Kept.Add(bin); return S.Hold(0); });

    // S321. The same with a plain cell.
    public static Cell<int> AddInACollectionInitializerThatSamplesAPlainValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Bin bin = new() { Plain }; Kept.Add(bin); return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S322. Code of the platform calls a member of an object of this file. The default comparer calls
    // CompareTo, which samples a pending member.
    public static Cell<int> DefaultComparerComparesObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { int c = Comparer<Ranked>.Default.Compare(new Ranked(l.Map(v => v)), new Ranked(Plain)); return S.Hold(c); });

    // S323. Min of a query calls CompareTo.
    public static Cell<int> MinOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.Min()!); return S.Hold(0); });

    // S324. A sort by a key calls CompareTo of the keys.
    public static Cell<int> OrderByAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Ranked first = all.OrderBy(a => a).First(); Kept.Add(first); return S.Hold(0); });

    // S325. The same for the second key of a sort.
    public static Cell<int> ThenByAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.OrderBy(a => 0).ThenBy(a => a).First()); return S.Hold(0); });

    // S326. The key function is a method, and not a lambda at the call.
    public static Cell<int> OrderByAMethodThatGivesAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.OrderBy(Itself).First()); return S.Hold(0); });

    // S327. The key function is a local function.
    public static Cell<int> OrderByALocalFunctionThatGivesAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Ranked Key(Ranked r) => r; Kept.Add(all.OrderBy(Key).First()); return S.Hold(0); });

    // S328. The key function is a lambda in a local.
    public static Cell<int> OrderByAFunctionInALocalThatGivesAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Func<Ranked, Ranked> key = r => r; Kept.Add(all.OrderBy(key).First()); return S.Hold(0); });

    // S329. A method that gives a plain key.
    public static Cell<int> OrderByAMethodThatGivesAPlainKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.OrderBy(Zero).First()); return S.Hold(0); });

    // S330. GroupBy with a method for the key function calls GetHashCode of the keys.
    public static Cell<int> GroupByAMethodThatGivesAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; int n = all.GroupBy(SameHashed).Count(); return S.Hold(n); });

    // S331. MaxBy calls CompareTo of the keys.
    public static Cell<int> MaxByAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.MaxBy(a => a)!); return S.Hold(0); });

    // S332. A tuple key compares its elements.
    public static Cell<int> OrderByATupleKeyWithAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.OrderBy(a => (a, 1)).First()); return S.Hold(0); });

    // S333. The objects are the result of a query of the platform.
    public static Cell<int> MaxOfObjectsFromAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.Select(a => a).Max()!); return S.Hold(0); });

    // S334. A sorted set calls CompareTo at each Add of its initializer.
    public static Cell<int> SortedSetInitializerOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> set = new() { new Ranked(l.Map(v => v)), new Ranked(Plain) }; Kept.Add(set); return S.Hold(0); });

    // S335. The same with a call of Add.
    public static Cell<int> SortedSetAddOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> set = new(); set.Add(new Ranked(l.Map(v => v))); set.Add(new Ranked(Plain)); return S.Hold(set.Count); });

    // S336. A sorted dictionary compares its keys.
    public static Cell<int> SortedDictionaryAddOfObjectKeys() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<Ranked, int> d = new(); d.Add(new Ranked(l.Map(v => v)), 1); d.Add(new Ranked(Plain), 2); return S.Hold(d.Count); });

    // S337. The same through the indexer.
    public static Cell<int> SortedDictionaryIndexerOfObjectKeys() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<Ranked, int> d = new() { [new Ranked(Plain)] = 0 }; d[new Ranked(l.Map(v => v))] = 1; return S.Hold(d.Count); });

    // S338. A search in a sorted set calls CompareTo.
    public static Cell<int> SortedSetContainsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> set = new() { new Ranked(Plain) }; bool has = set.Contains(new Ranked(l.Map(v => v))); return S.Hold(has ? 1 : 0); });

    // S339. A sort by a plain key does not call CompareTo of the objects.
    public static Cell<int> OrderByAPlainKeyOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.OrderBy(a => 1).First()); return S.Hold(0); });

    // S340. A sort that gets a comparer calls the comparer, and not CompareTo.
    public static Cell<int> OrderByAnObjectKeyWithAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.OrderBy(a => a, Comparer<Ranked>.Create((x, y) => 0)).First()); return S.Hold(0); });

    // S341. The same for a sort that gets a function that compares.
    public static Cell<int> SortOfObjectsWithAComparison() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; all.Sort((x, y) => 0); return S.Hold(0); });

    // S342. CompareTo of objects with plain cells reads nothing that is pending.
    public static Cell<int> MinOfPlainObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(Plain), new(Plain)]; Kept.Add(all.Min()!); return S.Snapshot(l, (p, q) => p + q).Hold(0); });

    // S343. A search in a list calls Equals.
    public static Cell<int> ListContainsWithAPendingElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> all = [new(l.Map(v => v))]; bool has = all.Contains(new Alike(Plain)); return S.Hold(has ? 1 : 0); });

    // S344. The same when the object that the search is for is the pending one.
    public static Cell<int> ListContainsAPendingObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> all = [new(Plain)]; bool has = all.Contains(new Alike(l.Map(v => v))); return S.Hold(has ? 1 : 0); });

    // S345. Equals of an element of the list samples a member of the object that it gets.
    public static Cell<int> ListContainsAnObjectThatEqualsReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikePeer> all = [new(Plain)]; bool has = all.Contains(new AlikePeer(l.Map(v => v))); return S.Hold(has ? 1 : 0); });

    // S346. IndexOf calls Equals.
    public static Cell<int> ListIndexOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> all = [new(l.Map(v => v))]; int i = all.IndexOf(new Alike(Plain)); return S.Hold(i); });

    // S347. Remove calls Equals.
    public static Cell<int> ListRemoveOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> all = [new(l.Map(v => v))]; bool gone = all.Remove(new Alike(Plain)); return S.Hold(gone ? 1 : 0); });

    // S348. Array.IndexOf calls Equals.
    public static Cell<int> ArrayIndexOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] all = [new(l.Map(v => v))]; int i = Array.IndexOf(all, new Alike(Plain)); return S.Hold(i); });

    // S349. Contains on an array calls Equals.
    public static Cell<int> ArrayContainsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] all = [new(l.Map(v => v))]; bool has = all.Contains(new Alike(Plain)); return S.Hold(has ? 1 : 0); });

    // S350. Contains of a query calls Equals.
    public static Cell<int> QueryContainsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<Alike> all = new List<Alike> { new(l.Map(v => v)) }.Where(a => true); bool has = all.Contains(new Alike(Plain)); return S.Hold(has ? 1 : 0); });

    // S351. SequenceEqual calls Equals.
    public static Cell<int> SequenceEqualOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> a = [new(l.Map(v => v))]; List<Alike> b = [new(Plain)]; bool e = a.SequenceEqual(b); return S.Hold(e ? 1 : 0); });

    // S352. Equals of object with two arguments calls Equals of the first one.
    public static Cell<int> StaticEqualsOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { bool e = Equals(new Alike(l.Map(v => v)), new Alike(Plain)); return S.Hold(e ? 1 : 0); });

    // S353. The default equality comparer calls Equals.
    public static Cell<int> DefaultEqualityComparerOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { bool e = EqualityComparer<Alike>.Default.Equals(new Alike(l.Map(v => v)), new Alike(Plain)); return S.Hold(e ? 1 : 0); });

    // S354. A search calls Equals of IEquatable when the type has it.
    public static Cell<int> ListContainsAnEquatableObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Equatable> all = [new(l.Map(v => v))]; bool has = all.Contains(new Equatable(Plain)); return S.Hold(has ? 1 : 0); });

    // S355. Equals of a tuple calls Equals of its elements.
    public static Cell<int> TupleEqualsWithAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Alike, int) a = (new Alike(l.Map(v => v)), 1); bool e = a.Equals((new Alike(Plain), 1)); return S.Hold(e ? 1 : 0); });

    // S356. Distinct calls GetHashCode.
    public static Cell<int> DistinctOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; int n = all.Distinct().Count(); return S.Hold(n); });

    // S357. GroupBy calls GetHashCode of the keys.
    public static Cell<int> GroupByAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; int n = all.GroupBy(a => a).Count(); return S.Hold(n); });

    // S358. ToDictionary calls GetHashCode of the keys.
    public static Cell<int> ToDictionaryWithObjectKeys() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; Kept.Add(all.ToDictionary(a => a, a => 1)); return S.Hold(0); });

    // S359. ToHashSet calls GetHashCode.
    public static Cell<int> ToHashSetOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; Kept.Add(all.ToHashSet()); return S.Hold(0); });

    // S360. Except calls GetHashCode.
    public static Cell<int> ExceptOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] a = [new(l.Map(v => v))]; Hashed[] b = [new(Plain)]; int n = a.Except(b).Count(); return S.Hold(n); });

    // S361. Join calls GetHashCode of the keys.
    public static Cell<int> JoinOnObjectKeys() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; Hashed[] more = [new(Plain)]; int n = all.Join(more, x => x, y => y, (x, y) => 1).Count(); return S.Hold(n); });

    // S362. A set calls GetHashCode at each Add of its initializer.
    public static Cell<int> HashSetInitializerOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Hashed> set = new() { new Hashed(l.Map(v => v)) }; return S.Hold(set.Count); });

    // S363. The same with a collection expression.
    public static Cell<int> HashSetExpressionOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Hashed> set = [new Hashed(l.Map(v => v))]; return S.Hold(set.Count); });

    // S364. The same with a call of Add.
    public static Cell<int> HashSetAddOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Hashed> set = new(); set.Add(new Hashed(l.Map(v => v))); return S.Hold(set.Count); });

    // S365. The constructor of a set calls GetHashCode of each element that it gets.
    public static Cell<int> HashSetConstructorWithObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; HashSet<Hashed> set = new(all); return S.Hold(set.Count); });

    // S366. A search in a set calls GetHashCode.
    public static Cell<int> HashSetContainsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Hashed> set = [new Hashed(Plain)]; bool has = set.Contains(new Hashed(l.Map(v => v))); return S.Hold(has ? 1 : 0); });

    // S367. A set calls Equals for two objects with one hash code.
    public static Cell<int> HashSetOfObjectsWithOneHashCode() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = [new Alike(l.Map(v => v)), new Alike(Plain)]; return S.Hold(set.Count); });

    // S368. A dictionary calls GetHashCode of a key.
    public static Cell<int> DictionaryAddOfAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new(); d.Add(new Hashed(l.Map(v => v)), 1); return S.Hold(d.Count); });

    // S369. The same through the setter of the indexer.
    public static Cell<int> DictionaryIndexerSetOfAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new(); d[new Hashed(l.Map(v => v))] = 1; return S.Hold(d.Count); });

    // S370. The same through the getter of the indexer.
    public static Cell<int> DictionaryIndexerGetOfAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new() { [new Hashed(Plain)] = 0 }; int found = d.TryGetValue(new Hashed(Plain), out int f) ? f : d[new Hashed(l.Map(v => v))]; return S.Hold(found); });

    // S371. The same in an initializer with an index.
    public static Cell<int> DictionaryIndexerInitializerOfAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new() { [new Hashed(l.Map(v => v))] = 1 }; return S.Hold(d.Count); });

    // S372. The same in an initializer that calls Add.
    public static Cell<int> DictionaryAddInitializerOfAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new() { { new Hashed(l.Map(v => v)), 1 } }; return S.Hold(d.Count); });

    // S373. ContainsKey calls GetHashCode.
    public static Cell<int> DictionaryContainsAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new() { [new Hashed(Plain)] = 0 }; bool has = d.ContainsKey(new Hashed(l.Map(v => v))); return S.Hold(has ? 1 : 0); });

    // S374. TryGetValue calls GetHashCode.
    public static Cell<int> DictionaryTryGetValueOfAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Hashed, int> d = new() { [new Hashed(Plain)] = 0 }; d.TryGetValue(new Hashed(l.Map(v => v)), out int found); return S.Hold(found); });

    // S375. The == operator of a class with no operator compares references, and calls nothing.
    public static Cell<int> ReferenceEqualityOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike a = new(l.Map(v => v)); Alike b = new(Plain); bool e = a == b; return S.Hold(e ? 1 : 0); });

    // S376. A search in a list does not call GetHashCode, and the type has no Equals.
    public static Cell<int> ListContainsAnObjectWithoutEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Hashed> all = [new(l.Map(v => v))]; bool has = all.Contains(new Hashed(Plain)); return S.Hold(has ? 1 : 0); });

    // S377. A search that gets a comparer calls the comparer, and not Equals.
    public static Cell<int> QueryContainsAnObjectWithAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> all = [new(l.Map(v => v))]; bool has = all.Contains(new Alike(Plain), EqualityComparer<Alike>.Create((x, y) => true)); return S.Hold(has ? 1 : 0); });

    // S378. Equals of a record compares the cells, and samples none.
    public static Cell<int> ListContainsARecord() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Tagged> all = [new(l.Map(v => v))]; bool has = all.Contains(new Tagged(Plain)); return S.Hold(has ? 1 : 0); });

    // S379. DistinctBy with a plain key does not call GetHashCode of the objects.
    public static Cell<int> DistinctByAPlainKeyOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v))]; int n = all.DistinctBy(a => 1).Count(); return S.Hold(n); });

    // S380. A dictionary calls no member of its values.
    public static Cell<int> DictionaryWithObjectValues() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<int, Alike> d = new() { [1] = new Alike(l.Map(v => v)) }; bool has = d.ContainsKey(1); return S.Hold(has ? 1 : 0); });

    // S381. string.Join calls ToString of each element.
    public static Cell<int> JoinOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; string s = string.Join(",", all); return S.Hold(s.Length); });

    // S382. The same for objects that are arguments.
    public static Cell<int> JoinOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { string s = string.Join(",", new Titled(l.Map(v => v)), new Titled(Plain)); return S.Hold(s.Length); });

    // S383. string.Concat calls ToString.
    public static Cell<int> ConcatOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { string s = string.Concat(new Titled(l.Map(v => v)), "x"); return S.Hold(s.Length); });

    // S384. string.Format calls ToString.
    public static Cell<int> FormatOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { string s = string.Format("{0}", new Titled(l.Map(v => v))); return S.Hold(s.Length); });

    // S385. Append of a StringBuilder calls ToString.
    public static Cell<int> BuilderAppendOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { StringBuilder b = new(); b.Append(new Titled(l.Map(v => v))); return S.Hold(b.Length); });

    // S386. AppendJoin calls ToString of each element.
    public static Cell<int> BuilderAppendJoinOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; StringBuilder b = new(); b.AppendJoin(",", all); return S.Hold(b.Length); });

    // S387. Write of a TextWriter calls ToString.
    public static Cell<int> WriterWriteOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { StringWriter w = new(); w.Write(new Titled(l.Map(v => v))); return S.Hold(w.ToString().Length); });

    // S388. Convert.ToString calls ToString.
    public static Cell<int> ConvertToStringOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { string? s = Convert.ToString(new Titled(l.Map(v => v))); return S.Hold(s?.Length ?? 0); });

    // S389. ToString of a tuple calls ToString of its elements.
    public static Cell<int> TupleToStringWithAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { string s = (new Titled(l.Map(v => v)), 1).ToString(); return S.Hold(s.Length); });

    // S390. string.Join of strings calls no member of an object that is only kept.
    public static Cell<int> JoinOfStringsBesideAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { string[] names = ["a"]; Titled n = new(l.Map(v => v)); Kept.Add(n); string s = string.Join(",", names); return S.Hold(s.Length); });

    // S391. A list of the platform calls no member of an object that it keeps.
    public static Cell<int> ObjectOnlyKeptInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S392. ToString of an object with a plain cell reads nothing that is pending.
    public static Cell<int> FormatOfAPlainObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { string s = string.Format("{0}", new Titled(Plain)); return S.Snapshot(l, (p, q) => p + q + s.Length).Hold(0); });

    // S393. The query gives strings, and string.Join does not get the objects.
    public static Cell<int> JoinOfStringsFromObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; string s = string.Join(",", all.Select(a => "x")); return S.Hold(s.Length); });

    // S394. A second Append does not call ToString of what the builder got before.
    public static Cell<int> BuilderAppendAfterAPlainObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { StringBuilder b = new(); Titled plain = new(Plain); b.Append(plain); Kept.Add(new Titled(l.Map(v => v))); b.Append("x"); return S.Hold(b.Length); });

    // S395. ToString of a writer is not ToString of an object that it wrote.
    public static Cell<int> WriterToStringAfterAPlainWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { StringWriter w = new(); w.Write(new Titled(Plain)); Kept.Add(new Titled(l.Map(v => v))); return S.Hold(w.ToString().Length); });

    // S396. A query of the platform enumerates a sequence of this file, whose iterator samples a pending member.
    public static Cell<int> SumOfASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); int t = bag.Sum(); return S.Hold(t); });

    // S397. ToList enumerates the sequence.
    public static Cell<int> ToListOfASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); Kept.Add(bag.ToList()); return S.Hold(0); });

    // S398. The constructor of a list enumerates the sequence that it gets.
    public static Cell<int> ListConstructorWithASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); List<int> all = new(bag); return S.Hold(all.Count); });

    // S399. AddRange enumerates the sequence.
    public static Cell<int> AddRangeOfASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); List<int> all = new(); all.AddRange(bag); return S.Hold(all.Count); });

    // S400. string.Join enumerates the sequence.
    public static Cell<int> JoinOfASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); string s = string.Join(",", bag); return S.Hold(s.Length); });

    // S401. A spread in a collection expression enumerates the sequence.
    public static Cell<int> SpreadOfASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); int[] all = [.. bag]; return S.Hold(all.Length); });

    // S402. The sequence is in a local with the type of the interface.
    public static Cell<int> SumOfASequenceThroughAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> seq = new Sack(l.Map(v => v)); int t = seq.Sum(); return S.Hold(t); });

    // S403. The same query as a static call.
    public static Cell<int> SumOfASequenceInAStaticCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); int t = Enumerable.Sum(bag); return S.Hold(t); });

    // S404. The enumerator is an object of this file, and its Current samples a pending member.
    public static Cell<int> SumOfASequenceWithAnEnumeratorObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stroll walk = new(l.Map(v => v)); int t = walk.Sum(); return S.Hold(t); });

    // S405. The sequence gives a pending cell to the function of the query, which samples it.
    public static Cell<int> QueryFunctionSamplesAnElementOfASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellSack cells = new(l.Map(v => v)); int t = cells.Select(c => c.Sample()).Sum(); return S.Hold(t); });

    // S406. ToList makes a list from the sequence, and the body of the foreach samples an element.
    public static Cell<int> ForEachOverAListFromASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellSack cells = new(l.Map(v => v)); int t = 0; foreach (Cell<int> c in cells.ToList()) { t += c.Sample(); } return S.Hold(t); });

    // S407. The iterator gives the cell and samples nothing.
    public static Cell<int> ToListOfASequenceOfCells() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellSack cells = new(l.Map(v => v)); Kept.Add(cells.ToList()); return S.Hold(0); });

    // S408. The iterator samples a plain cell.
    public static Cell<int> SumOfAPlainSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(Plain); int t = bag.Sum(); return S.Snapshot(l, (p, q) => p + q + t).Hold(0); });

    // S409. A query that calls no member of the objects.
    public static Cell<int> QueryOverObjectsThatReadsNoMember() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; int t = all.Select(a => 1).Sum(); return S.Hold(t); });

    // S410. The constructor of a list calls no member of the elements.
    public static Cell<int> ListConstructorWithObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(new List<Ranked>(all)); return S.Hold(0); });

    // S411. A search in a list does not call CompareTo.
    public static Cell<int> ListContainsAComparableObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v))]; bool has = all.Contains(new Ranked(Plain)); return S.Hold(has ? 1 : 0); });

    // S412. Distinct does not call CompareTo.
    public static Cell<int> DistinctOfComparableObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; int n = all.Distinct().Count(); return S.Hold(n); });

    // S413. A collection contains the objects that have the members, and is not one of them. The type
    // object does not tell the two apart, thus the state of the value does. ToString of a list in a
    // local of the type object is the ToString of the list.
    public static Cell<int> ListInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S414. The same for Equals.
    public static Cell<int> ListInAnObjectLocalEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Alike> { new(l.Map(v => v)) }; return S.Hold(o.Equals(o) ? 1 : 0); });

    // S415. The same for an array.
    public static Cell<int> ArrayInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new Titled[] { new(l.Map(v => v)) }; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S416. The same for a dictionary with an initializer.
    public static Cell<int> DictionaryInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new Dictionary<string, Titled> { ["a"] = new(l.Map(v => v)) }; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S417. The list got its element from Add.
    public static Cell<int> ListAfterAnAddInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = new(); all.Add(new Titled(l.Map(v => v))); object o = all; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S418. A query over the list contains the elements also.
    public static Cell<int> QueryInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; object o = all.Select(x => x); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S419. A sequence that a method of the platform makes from one object.
    public static Cell<int> RepeatedObjectInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Enumerable.Repeat(new Titled(l.Map(v => v)), 2); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S420. The second arm has no members, thus the value stays a container.
    public static Cell<int> ListOrAStringInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; object o = Kept.Count < 0 ? "x" : all; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S421. A type parameter tells no more than object does.
    public static Cell<int> ListToAGenericHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; Kept.Add(TextOf(all)); return S.Hold(0); });

    // S422. The same through a parameter of the type object.
    public static Cell<int> ListToAnObjectParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; Kept.Add(TextOfAnObject(all)); return S.Hold(0); });

    // S423. An interpolation calls ToString of the list.
    public static Cell<int> ListInAnInterpolation() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; Kept.Add($"{o}"); return S.Hold(0); });

    // S424. Append calls ToString of the list.
    public static Cell<int> ListAppendedToABuilder() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; return S.Hold(new StringBuilder().Append(o).Length); });

    // S425. The set calls GetHashCode of the list.
    public static Cell<int> ListInASetOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Hashed> { new(l.Map(v => v)) }; HashSet<object> set = [o]; return S.Hold(set.Count); });

    // S426. An element of a container is not a container. Each shape from here to the end of the group
    // throws.
    public static Cell<int> ElementOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; Kept.Add(all[0].ToString()!); return S.Hold(0); });

    // S427. The same for the variable of a foreach.
    public static Cell<int> ForEachOverAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; foreach (object x in all) { Kept.Add(x.ToString()!); } return S.Hold(0); });

    // S428. An object in a local of the type object is the object.
    public static Cell<int> ObjectInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new Titled(l.Map(v => v)); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S429. The same through a type parameter.
    public static Cell<int> ObjectToAGenericHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(TextOf(new Titled(l.Map(v => v)))); return S.Hold(0); });

    // S430. A local that is an object on one path and a list on a second path can be the object.
    public static Cell<int> ObjectOrAListInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Kept.Count >= 0 ? new Titled(l.Map(v => v)) : new List<Titled> { new(l.Map(v => v)) }; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S431. The helper gets the list, and its foreach gets an element.
    public static Cell<int> ListToAGenericHelperThatEnumerates() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; Kept.Add(TextOfAll(all)); return S.Hold(0); });

    // S432. The cast gives the list a type, and the foreach gives an element.
    public static Cell<int> ForEachOverAnObjectLocalAsASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; foreach (object x in (System.Collections.IEnumerable)o) { Kept.Add(x.ToString()!); } return S.Hold(0); });

    // S433. The element goes to a second local of the type object.
    public static Cell<int> ElementOfAnObjectLocalAfterACast() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; object first = ((List<Titled>)o)[0]; Kept.Add(first.ToString()!); return S.Hold(0); });

    // S434. Join reads the elements of the sequence.
    public static Cell<int> StringJoinOfAnObjectLocalAsASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; return S.Hold(string.Join(",", (IEnumerable<object>)o).Length); });

    // S435. The indexer of a dictionary gives one value.
    public static Cell<int> ValueOfADictionaryOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, object> d = new() { ["a"] = new Titled(l.Map(v => v)) }; Kept.Add(d["a"].ToString()!); return S.Hold(0); });

    // S436. An out parameter of the platform gets one value.
    public static Cell<int> OutValueOfADictionaryOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, object> d = new() { ["a"] = new Titled(l.Map(v => v)) }; d.TryGetValue("a", out object? v); Kept.Add(v!.ToString()!); return S.Hold(0); });

    // S437. The parameter of the lambda is one element.
    public static Cell<int> ForEachMethodOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; all.ForEach(x => Kept.Add(x.ToString()!)); return S.Hold(0); });

    // S438. A collection that is not generic gives its element as object.
    public static Cell<int> ElementOfAnArrayList() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.ArrayList all = new() { new Titled(l.Map(v => v)) }; Kept.Add(all[0]!.ToString()!); return S.Hold(0); });

    // S439. A pattern gives the list its type again.
    public static Cell<int> PatternGivesAnObjectLocalAListType() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = new List<Titled> { new(l.Map(v => v)) }; if (o is List<Titled> typed) { Kept.Add(typed[0].ToString()); } return S.Hold(0); });

    // S440. The helper reads one element of the list that it gets.
    public static Cell<int> GenericHelperGivesAnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; Kept.Add(FirstOf(all).ToString()!); return S.Hold(0); });

    // S441. First gives one element, and its result has the type parameter of the helper.
    public static Cell<int> GenericHelperGivesTheFirstElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; Kept.Add(FirstBy(all).ToString()!); return S.Hold(0); });

    // S442. The function of Aggregate gets one element.
    public static Cell<int> AggregateOverAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; Kept.Add(all.Aggregate("", (acc, x) => acc + x.ToString())); return S.Hold(0); });

    // S443. A list pattern gives one element to its variable.
    public static Cell<int> ListPatternOverAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; if (all is [var first, ..]) { Kept.Add(first.ToString()!); } return S.Hold(0); });

    // S444. A query of the platform runs its functions when something enumerates it, and not when the
    // code makes it. An iterator is the same. The walk reads each one where the code enumerates it.
    // The block only keeps this query, and the function runs after the close.
    public static Cell<int> QueryKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(q); return S.Hold(0); });

    // S445. The same for the key function of a sort.
    public static Cell<int> SortByAKeyFunctionKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.OrderBy(x => x + l.Sample()); Kept.Add(q); return S.Hold(0); });

    // S446. The same for a query over a query.
    public static Cell<int> ChainOfQueriesKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Where(x => x > l.Sample()).Select(x => x + 1); Kept.Add(q); return S.Hold(0); });

    // S447. The same for a query that an object keeps.
    public static Cell<int> QueryKeptInAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { QueryHolder h = new(Numbers.Select(x => x + l.Sample())); Kept.Add(h); return S.Hold(0); });

    // S448. The same for a query that is the value of a cell.
    public static Cell<int> QueryKeptInACell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<IEnumerable<int>> c = Cell.Constant(Numbers.Select(x => x + l.Sample())); Kept.Add(c); return S.Hold(0); });

    // S449. A callee with no body to read that gets the query as object keeps it, and enumerates
    // nothing.
    public static Cell<int> QueryGivenAsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(string.Format("{0}", q)); return S.Hold(0); });

    // S450. A query expression is a query also.
    public static Cell<int> QueryExpressionKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = from x in Numbers select x + l.Sample(); Kept.Add(q); return S.Hold(0); });

    // S451. The same with a filter and a sort.
    public static Cell<int> QueryExpressionWithClausesKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = from x in Numbers where x > l.Sample() orderby x select x; Kept.Add(q); return S.Hold(0); });

    // S452. The function is a lambda in a local, and not a lambda at the call.
    public static Cell<int> QueryWithAFunctionInALocalKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<int, int> f = x => x + l.Sample(); IEnumerable<int> q = Numbers.Select(f); Kept.Add(q); return S.Hold(0); });

    // S453. The function is a local function.
    public static Cell<int> QueryWithALocalFunctionKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Numbers.Select(F)); return S.Hold(0); int F(int x) => x + l.Sample(); });

    // S454. The function samples an element of the source.
    public static Cell<int> QueryThatSamplesAnElementKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cells = [l.Map(v => v), Plain]; IEnumerable<int> q = cells.Select(c => c.Sample()); Kept.Add(q); return S.Hold(0); });

    // S455. The function of a Map enumerates the query, and that function runs at a firing after the
    // close.
    public static Cell<int> QueryEnumeratedInAFunctionAfterTheClose() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Map(v => v + q.Sum()).Hold(0); });

    // S456. A sort calls CompareTo of the objects when something enumerates it.
    public static Cell<int> SortOfObjectsKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; IEnumerable<Ranked> q = all.OrderBy(a => a); Kept.Add(q); return S.Hold(0); });

    // S457. The same for GetHashCode in Distinct.
    public static Cell<int> DistinctOfObjectsKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.Distinct()); return S.Hold(0); });

    // S458. The call of the query method names the class.
    public static Cell<int> QueryFromAStaticCallKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Enumerable.Select(Numbers, x => x + l.Sample())); return S.Hold(0); });

    // S459. A second query over the first one enumerates nothing.
    public static Cell<int> QueryOverAKeptQueryKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); IEnumerable<int> r = q.Where(x => x > 0); Kept.Add(r); return S.Hold(0); });

    // S460. An object initializer gives the query to a property.
    public static Cell<int> QueryInAnObjectInitializerKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { QuerySlot slot = new() { Values = Numbers.Select(x => x + l.Sample()) }; Kept.Add(slot); return S.Hold(0); });

    // S461. The function of a stream makes the query at a firing, which comes after the close. The
    // sample in the block gives the first value of the hold.
    public static Cell<int> QueryOfAFiringHeldAndSampled() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<IEnumerable<int>> held = S.Map(v => Numbers.Select(x => x + v + l.Sample())).Hold(Numbers); return S.Hold(held.Sample().Sum()); });

    // S462. The body of an iterator runs at the first MoveNext. The block only keeps the sequence.
    public static Cell<int> IteratorKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = SamplesOf(l); Kept.Add(q); return S.Hold(0); });

    // S463. The same for an iterator that is a local function.
    public static Cell<int> LocalIteratorKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Each()); return S.Hold(0); IEnumerable<int> Each() { yield return l.Sample(); } });

    // S464. The same for the enumerator of an object whose GetEnumerator is an iterator.
    public static Cell<int> EnumeratorOfAnIteratorKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); IEnumerator<int> e = bag.GetEnumerator(); Kept.Add(e); return S.Hold(0); });

    // S465. The same for an iterator that returns an enumerator.
    public static Cell<int> IteratorOfAnEnumeratorKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(StepsOf(l)); return S.Hold(0); });

    // S466. The same for an iterator that reads a member of its object.
    public static Cell<int> IteratorOfAnObjectKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IteratorOwner r = new(l.Map(v => v)); Kept.Add(r.Each()); return S.Hold(0); });

    // S467. A query does not enumerate its source when the code makes it.
    public static Cell<int> QueryOverAnIteratorKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); IEnumerable<int> q = bag.Select(x => x); Kept.Add(q); return S.Hold(0); });

    // S468. The foreach is in the body of an iterator that nothing enumerates.
    public static Cell<int> IteratorOverAQueryKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(EachOf(q)); return S.Hold(0); });

    // S469. A local function makes the query and the block keeps it.
    public static Cell<int> QueryFromALocalFunctionKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Make(l)); return S.Hold(0); static IEnumerable<int> Make(Cell<int> c) => Numbers.Select(x => x + c.Sample()); });

    // S470. A method makes the query. The query keeps what the walk of the method found, for the code
    // that enumerates it.
    public static Cell<int> QueryFromAHelperKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(QueryOf(l)); return S.Hold(0); });

    // S471. The same through two methods.
    public static Cell<int> QueryFromAHelperOfAHelperKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(QueryOfAgain(l)); return S.Hold(0); });

    // S472. The same for an iterator that a method calls and returns.
    public static Cell<int> IteratorFromAHelperKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(SamplesFrom(l)); return S.Hold(0); });

    // S473. The same through a lambda in a local.
    public static Cell<int> IteratorFromALambdaKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<IEnumerable<int>> f = () => SamplesOf(l); Kept.Add(f()); return S.Hold(0); });

    // S474. A field initializer makes the query, and no member enumerates it in the block.
    public static Cell<int> QueryOfAConstructorKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new Summer(l.Map(v => v))); return S.Hold(0); });

    // S475. A getter makes the query.
    public static Cell<int> QueryOfAGetterKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new Summer(l.Map(v => v)).Items); return S.Hold(0); });

    // S476. A method returns a lambda that makes the query. The block calls the lambda and keeps the
    // query.
    public static Cell<int> QueryOfAReturnedLambdaKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<IEnumerable<int>> make = QueryMakerOf(l); Kept.Add(make()); return S.Hold(0); });

    // S477. A foreach enumerates the query. Each shape from here to the end of the group throws.
    public static Cell<int> ForEachOverAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); int n = 0; foreach (int v in q) { n += v; } return S.Hold(n); });

    // S478. ToList enumerates its source.
    public static Cell<int> ToListOfAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(q.ToList().Count); });

    // S479. First enumerates its source.
    public static Cell<int> FirstOfAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(q.First()); });

    // S480. A method of the platform that is not a query method enumerates a sequence that it gets.
    public static Cell<int> StringJoinOfAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(string.Join(",", q).Length); });

    // S481. The same for a constructor.
    public static Cell<int> ListFromAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); List<int> all = new(q); return S.Hold(all.Count); });

    // S482. A spread enumerates its value.
    public static Cell<int> SpreadOfAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); int[] all = [.. q]; return S.Hold(all.Length); });

    // S483. AddRange enumerates its argument.
    public static Cell<int> AddRangeOfAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); List<int> all = []; all.AddRange(q); return S.Hold(all.Count); });

    // S484. GetEnumerator on a query is the start of an enumeration.
    public static Cell<int> EnumeratorOfAQueryThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); bool any = q.GetEnumerator().MoveNext(); return S.Hold(any ? 1 : 0); });

    // S485. The second query has the first one as its source, and ToList enumerates the two.
    public static Cell<int> QueryOverAQueryToList() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(q.Where(x => x > 0).ToList().Count); });

    // S486. The same in one expression.
    public static Cell<int> ChainOfQueriesEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(Numbers.Where(x => x > l.Sample()).Select(x => x + 1).ToList().Count));

    // S487. The query is the second sequence of a Concat.
    public static Cell<int> QueryAsTheSecondSourceOfAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Numbers.Concat(q).ToList().Count); });

    // S488. SelectMany enumerates the sequence that its function returns.
    public static Cell<int> QueryFromTheFunctionOfSelectMany() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Numbers.SelectMany(x => q).ToList().Count); });

    // S489. The function of a query returns a query, and the block enumerates one of them.
    public static Cell<int> QueryThatAQueryFunctionReturnsEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists[0].Count()); });

    // S490. A local function enumerates the query that it gets.
    public static Cell<int> QueryToALocalFunctionThatEnumerates() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Run(q)); static int Run(IEnumerable<int> s) { int n = 0; foreach (int v in s) { n += v; } return n; } });

    // S491. A method enumerates the query that it gets.
    public static Cell<int> QueryToAHelperThatEnumerates() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(SumOf(q)); });

    // S492. The same for an extension method of this file.
    public static Cell<int> QueryToAnExtensionMethodThatEnumerates() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(q.TotalOf()); });

    // S493. A lambda that the block calls enumerates the query.
    public static Cell<int> QueryEnumeratedInALambdaInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Func<int> run = () => q.Sum(); return S.Hold(run()); });

    // S494. A lambda that a method of the platform runs in the call enumerates the query.
    public static Cell<int> QueryEnumeratedInALambdaThatRunsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Numbers.ToList().ForEach(x => Kept.Add(q.Sum())); return S.Hold(0); });

    // S495. A method of an object enumerates the query that the object keeps.
    public static Cell<int> QueryEnumeratedByAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { QueryHolder h = new(Numbers.Select(x => x + l.Sample())); return S.Hold(h.Total()); });

    // S496. A method of the library that gets a sequence enumerates it.
    public static Cell<int> QueryToTheLibrary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stream<int>[] ss = [S]; IEnumerable<Stream<int>> q = ss.Where(s => l.Sample() >= 0); return q.OrElse().Hold(0); });

    // S497. The function of a Lazy enumerates the query, and the read of Value runs that function.
    public static Cell<int> QueryEnumeratedInALazy() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Lazy<List<int>> z = new(() => q.ToList()); return S.Hold(z.Value.Count); });

    // S498. The Lazy gives the query, and the block enumerates it.
    public static Cell<int> QueryThroughALazy() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Lazy<IEnumerable<int>> z = new(() => q); return S.Hold(z.Value.Sum()); });

    // S499. A task of the platform gives the query back.
    public static Cell<int> QueryThroughATask() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Task.FromResult(q).Result.Sum()); });

    // S500. The same for a Tuple.
    public static Cell<int> QueryThroughATupleOfThePlatform() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Tuple.Create(q, 1).Item1.Sum()); });

    // S501. The query is an element of a tuple.
    public static Cell<int> QueryThroughATuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { (IEnumerable<int> Q, int N) t = (Numbers.Select(x => x + l.Sample()), 1); return S.Hold(t.Q.Sum()); });

    // S502. The query is a property of an anonymous object.
    public static Cell<int> QueryThroughAnAnonymousObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { var a = new { Q = Numbers.Select(x => x + l.Sample()) }; return S.Hold(a.Q.Sum()); });

    // S503. An anonymous object gives each property the state of its value.
    public static Cell<int> CellThroughAnAnonymousObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { var a = new { C = l.Map(v => v) }; return S.Hold(a.C.Sample()); });

    // S504. The query is an element of a list.
    public static Cell<int> QueryThroughAnElementOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> qs = [Numbers.Select(x => x + l.Sample())]; return S.Hold(qs[0].Sum()); });

    // S505. An assignment puts the query into an array.
    public static Cell<int> QueryThroughAnElementOfAnArray() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int>[] qs = new IEnumerable<int>[1]; qs[0] = Numbers.Select(x => x + l.Sample()); return S.Hold(qs[0].Sum()); });

    // S506. The query is a value of a dictionary, and SelectMany enumerates each value.
    public static Cell<int> QueryThroughTheValuesOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, IEnumerable<int>> d = new(); d["a"] = Numbers.Select(x => x + l.Sample()); return S.Hold(d.Values.SelectMany(v => v).Sum()); });

    // S507. The query is the value of a constant cell.
    public static Cell<int> QueryThroughACell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<IEnumerable<int>> c = Cell.Constant(Numbers.Select(x => x + l.Sample())); return S.Hold(c.Sample().Sum()); });

    // S508. A pattern gives the query its type again.
    public static Cell<int> QueryInAnObjectLocalThroughAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { object q = Numbers.Select(x => x + l.Sample()); return S.Hold(q is IEnumerable<int> again ? again.Sum() : 0); });

    // S509. The query goes through a static field.
    public static Cell<int> QueryThroughAStaticField() =>
        Cell.Loop<int>().WithoutCaptures(l => { StoredQuery = Numbers.Select(x => x + l.Sample()); return S.Hold(StoredQuery.Sum()); });

    // S510. A method gives the query to an out parameter.
    public static Cell<int> QueryThroughAnOutParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { QueryOut(l, out IEnumerable<int> q); return S.Hold(q.Sum()); });

    // S511. The name of ToList on the query is a delegate, and the call of it enumerates.
    public static Cell<int> QueryThroughAMethodGroup() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Func<List<int>> f = q.ToList; return S.Hold(f().Count); });

    // S512. An object initializer gives the query to a property, and the block enumerates the
    // property.
    public static Cell<int> QueryInAnObjectInitializerEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { QuerySlot slot = new() { Values = Numbers.Select(x => x + l.Sample()) }; return S.Hold(slot.Values.Sum()); });

    // S513. An iterator enumerates the query, and ToList enumerates the iterator.
    public static Cell<int> QueryThroughAnIterator() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(EachOf(q).ToList().Count); });

    // S514. The same for an iterator that is a local function.
    public static Cell<int> LocalIteratorOverAQueryEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Each().Sum()); IEnumerable<int> Each() { foreach (int v in q) { yield return v; } } });

    // S515. A query expression that something enumerates.
    public static Cell<int> QueryExpressionEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = from x in Numbers select x + l.Sample(); return S.Hold(q.Sum()); });

    // S516. The same with a filter and a sort.
    public static Cell<int> QueryExpressionWithClausesEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = from x in Numbers where x > l.Sample() orderby x select x; return S.Hold(q.ToList().Count); });

    // S517. The query expression is the source of a foreach.
    public static Cell<int> ForEachOverAQueryExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = 0; foreach (int v in from x in Numbers select x + l.Sample()) { n += v; } return S.Hold(n); });

    // S518. The first source of a query expression is a query.
    public static Cell<int> QueryExpressionOverAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); IEnumerable<int> again = from x in q select x; return S.Hold(again.Sum()); });

    // S519. The second source of a query expression is a query.
    public static Cell<int> QueryExpressionWithASecondSource() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); IEnumerable<int> both = from x in Numbers from y in q select x + y; return S.Hold(both.Sum()); });

    // S520. The function is a lambda in a local.
    public static Cell<int> QueryWithAFunctionInALocalEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<int, int> f = x => x + l.Sample(); IEnumerable<int> q = Numbers.Select(f); return S.Hold(q.Sum()); });

    // S521. The function is a local function.
    public static Cell<int> QueryWithALocalFunctionEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(Numbers.Select(F).Sum()); int F(int x) => x + l.Sample(); });

    // S522. The function samples an element of the source.
    public static Cell<int> QueryThatSamplesAnElementEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cells = [l.Map(v => v), Plain]; IEnumerable<int> q = cells.Select(c => c.Sample()); return S.Hold(q.Sum()); });

    // S523. The call of the query method names the class.
    public static Cell<int> QueryFromAStaticCallEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(Enumerable.Select(Numbers, x => x + l.Sample()).Sum()));

    // S524. The walk reads the function at the first enumeration only.
    public static Cell<int> QueryEnumeratedTwoTimes() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(q.Count() + q.Sum()); });

    // S525. A loop makes a query over the query that it made before.
    public static Cell<int> QueryAssignedInALoopEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers; for (int i = 0; i < 2; i++) { q = q.Select(x => x + l.Sample()); } return S.Hold(q.Sum()); });

    // S526. The sort is in a local, and First in the statement after it calls CompareTo.
    public static Cell<int> SortOfObjectsEnumeratedLater() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; IEnumerable<Ranked> q = all.OrderBy(a => a); Kept.Add(q.First()); return S.Hold(0); });

    // S527. ToList makes Distinct call GetHashCode.
    public static Cell<int> DistinctOfObjectsToList() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashed[] all = [new(l.Map(v => v)), new(Plain)]; return S.Hold(all.Distinct().ToList().Count); });

    // S528. A foreach runs the body of the iterator.
    public static Cell<int> ForEachOverAnIteratorThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = 0; foreach (int v in SamplesOf(l)) { n += v; } return S.Hold(n); });

    // S529. ToList runs the body of the iterator.
    public static Cell<int> ToListOfAnIteratorThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = SamplesOf(l); return S.Hold(q.ToList().Count); });

    // S530. A spread runs the body of the iterator.
    public static Cell<int> SpreadOfAnIteratorThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { int[] all = [.. SamplesOf(l)]; return S.Hold(all.Length); });

    // S531. The iterator is a local function.
    public static Cell<int> LocalIteratorEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(Each().Sum()); IEnumerable<int> Each() { yield return l.Sample(); } });

    // S532. MoveNext runs the body of a GetEnumerator that is an iterator.
    public static Cell<int> EnumeratorOfAnIteratorMoved() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); IEnumerator<int> e = bag.GetEnumerator(); return S.Hold(e.MoveNext() ? 1 : 0); });

    // S533. The same for an iterator that returns an enumerator.
    public static Cell<int> IteratorOfAnEnumeratorMoved() =>
        Cell.Loop<int>().WithoutCaptures(l => { using IEnumerator<int> e = StepsOf(l); int n = 0; while (e.MoveNext()) { n += e.Current; } return S.Hold(n); });

    // S534. The iterator reads a member of its object.
    public static Cell<int> IteratorOfAnObjectEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IteratorOwner r = new(l.Map(v => v)); return S.Hold(r.Each().Sum()); });

    // S535. The query enumerates its source when something enumerates the query.
    public static Cell<int> QueryOverAnIteratorEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack bag = new(l.Map(v => v)); IEnumerable<int> q = bag.Select(x => x); return S.Hold(q.Sum()); });

    // S536. A method enumerates the iterator that it gets.
    public static Cell<int> IteratorToAHelperThatEnumerates() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(SumOf(SamplesOf(l))));

    // S537. A local function makes the query and the block enumerates it.
    public static Cell<int> QueryFromALocalFunctionEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(Make(l).Sum()); static IEnumerable<int> Make(Cell<int> c) => Numbers.Select(x => x + c.Sample()); });

    // S538. A method makes the query and the block enumerates it.
    public static Cell<int> QueryFromAHelperEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(QueryOf(l).Sum()));

    // S539. The same through two methods.
    public static Cell<int> QueryFromAHelperOfAHelperEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(QueryOfAgain(l).Sum()));

    // S540. One method makes the query and a second method enumerates it.
    public static Cell<int> QueryFromAHelperToAHelperThatEnumerates() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(SumOf(QueryOf(l))));

    // S541. A method calls an iterator and returns it, and the block enumerates it.
    public static Cell<int> IteratorFromAHelperEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(SamplesFrom(l).Sum()));

    // S542. A lambda in a local makes the query.
    public static Cell<int> QueryFromALambdaInALocalEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<IEnumerable<int>> make = () => Numbers.Select(x => x + l.Sample()); return S.Hold(make().Sum()); });

    // S543. A lambda in a local calls the iterator.
    public static Cell<int> IteratorFromALambdaEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<IEnumerable<int>> f = () => SamplesOf(l); return S.Hold(f().Sum()); });

    // S544. A field initializer makes the query, and a method of the object enumerates it.
    public static Cell<int> QueryOfAConstructorEnumeratedByAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(new Summer(l.Map(v => v)).Total()));

    // S545. A getter makes the query, and the block enumerates it.
    public static Cell<int> QueryOfAGetterEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(new Summer(l.Map(v => v)).Items.Sum()));

    // S546. A method returns a lambda that makes the query, and the block enumerates the query.
    public static Cell<int> QueryOfAReturnedLambdaEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<IEnumerable<int>> make = QueryMakerOf(l); return S.Hold(make().Sum()); });

    // S547. The function of SelectMany is a local function that returns the query.
    public static Cell<int> QueryFromALocalFunctionOfSelectMany() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Numbers.SelectMany(Give).Sum()); IEnumerable<int> Give(int x) => q; });

    // S548. The source of a join clause is a query.
    public static Cell<int> QueryExpressionWithAJoin() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); IEnumerable<int> both = from x in Numbers join y in q on x equals y select x; return S.Hold(both.Count()); });

    // S549. The evaluator knows an object of a generic type by the definition of that type. The code
    // names the type with its type arguments, and the two are the same type for the object. An
    // interpolation calls ToString of the object.
    public static Cell<int> InterpolationOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericLabel<int> x = new(l.Map(v => v)); Kept.Add($"{x}"); return S.Hold(0); });

    // S550. The type of the object is in a generic type, and has the type parameters of that type.
    public static Cell<int> InterpolationOfAnObjectInAGenericType() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericOuter<int>.Label label = new(l.Map(v => v)); Kept.Add($"{label}"); return S.Hold(0); });

    // S551. A foreach calls GetEnumerator of the object.
    public static Cell<int> ForEachOverAGenericSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericPile<int> pile = new(l.Map(v => v)); int n = 0; foreach (int v in pile) { n += v; } return S.Hold(n); });

    // S552. A using calls Dispose of the object.
    public static Cell<int> UsingOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { using (new GenericCloser<int>(l.Map(v => v))) { } return S.Hold(0); });

    // S553. A method of the platform enumerates the object.
    public static Cell<int> SumOfAGenericSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericPile<int> pile = new(l.Map(v => v)); return S.Hold(pile.Sum()); });

    // S554. Join calls ToString of each element.
    public static Cell<int> StringJoinOfGenericObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericLabel<int>[] all = [new(l.Map(v => v))]; return S.Hold(string.Join(",", (IEnumerable<GenericLabel<int>>)all).Length); });

    // S555. Min calls CompareTo of the elements.
    public static Cell<int> MinOfGenericObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericGrade<int>[] all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(all.Min()!); return S.Hold(0); });

    // S556. A call through an interface runs the method of the object.
    public static Cell<int> InterfaceMethodOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = new GenericLabel<int>(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S557. The same for a method of a base type.
    public static Cell<int> VirtualMethodOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { ShownBase s = new GenericShower<int>(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S558. A call of ToString that the code writes.
    public static Cell<int> ToStringOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericLabel<int> x = new(l.Map(v => v)); Kept.Add(x.ToString()); return S.Hold(0); });

    // S559. A property of the object has the state that the constructor gave it.
    public static Cell<int> PropertyOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { GenericBox<Cell<int>> box = new(l.Map(v => v)); return S.Hold(box.Value.Sample()); });

    // S560. A list of such objects is not one of them. This shape and the one after it return.
    public static Cell<int> ListOfGenericObjectsToString() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<GenericLabel<int>> all = [new(l.Map(v => v))]; Kept.Add(all.ToString()!); return S.Hold(0); });

    // S561. The sequence is not generic, thus the type arguments of its interface are known. A sequence of
    // strings cannot be that object.
    public static Cell<int> QueryOfTextOverASequenceWithToString() =>
        Cell.Loop<int>().WithoutCaptures(l => { TitledPile pile = new(l.Map(v => v)); IEnumerable<string> names = pile.Select(x => "a"); Kept.Add(names.ToString()!); return S.Hold(0); });

    // S562. A pair, a tuple, a task, or an enumerator of the platform has the members of the object
    // that it contains in its state. Its own property is not one of those members. Value of a pair
    // gives the object, and the read through it throws.
    public static Cell<int> ValueOfAPairOfThePlatform() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Titled> p = new("a", new Titled(l.Map(v => v))); Kept.Add(p.Value.ToString()); return S.Hold(0); });

    // S563. The same for Item1 of a Tuple from Tuple.Create.
    public static Cell<int> ItemOfATupleOfThePlatform() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, int> t = Tuple.Create(new Titled(l.Map(v => v)), 1); Kept.Add(t.Item1.ToString()); return S.Hold(0); });

    // S564. The same for a Tuple from its constructor.
    public static Cell<int> ItemOfACreatedTupleOfThePlatform() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, int> t = new(new Titled(l.Map(v => v)), 1); Kept.Add(t.Item1.ToString()); return S.Hold(0); });

    // S565. The same for Result of a task.
    public static Cell<int> ResultOfATaskOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Task<Titled> t = Task.FromResult(new Titled(l.Map(v => v))); Kept.Add(t.Result.ToString()); return S.Hold(0); });

    // S566. The same for Current of an enumerator.
    public static Cell<int> CurrentOfAnEnumeratorOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; using IEnumerator<Titled> e = all.GetEnumerator(); e.MoveNext(); Kept.Add(e.Current.ToString()); return S.Hold(0); });

    // S567. The same for a field of an object of the platform.
    public static Cell<int> FieldOfAStrongBox() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Runtime.CompilerServices.StrongBox<Titled> box = new(new Titled(l.Map(v => v))); Kept.Add(box.Value!.ToString()); return S.Hold(0); });

    // S568. The pair comes from a dictionary.
    public static Cell<int> ValueOfTheFirstPairOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Titled> d = new() { ["a"] = new Titled(l.Map(v => v)) }; Kept.Add(d.First().Value.ToString()); return S.Hold(0); });

    // S569. The pair is the variable of a foreach.
    public static Cell<int> ValueOfAPairInAForEach() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Titled> d = new() { ["a"] = new Titled(l.Map(v => v)) }; foreach (KeyValuePair<string, Titled> kv in d) { Kept.Add(kv.Value.ToString()); } return S.Hold(0); });

    // S570. A property pattern reads the property as a member access does.
    public static Cell<int> PropertyPatternOnAPair() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Titled> p = new("a", new Titled(l.Map(v => v))); if (p is { Value: var v }) { Kept.Add(v.ToString()); } return S.Hold(0); });

    // S571. The variable of the pattern has the type object, and is one value that the pair contains.
    public static Cell<int> PropertyPatternOnAPairOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, object> p = new("a", new Titled(l.Map(v => v))); if (p is { Value: var v }) { Kept.Add(v.ToString()!); } return S.Hold(0); });

    // S572. The value has the type parameter of a helper, and is one value that the pair contains.
    public static Cell<int> ValueOfAPairInAGenericHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Titled> p = new("a", new Titled(l.Map(v => v))); Kept.Add(ValueTextOf(p)); return S.Hold(0); });

    // S573. The value has its own members, and one of them is the pending cell.
    public static Cell<int> MemberOfTheValueOfAPair() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, SharedRow> p = new("a", new SharedRow(l.Map(v => v))); return S.Hold(p.Value.Share.Sample()); });

    // S574. The object in the pair has a generic type.
    public static Cell<int> ValueOfAPairOfAGenericObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, GenericLabel<int>> p = new("a", new GenericLabel<int>(l.Map(v => v))); Kept.Add(p.Value.ToString()); return S.Hold(0); });

    // S575. A cell in a pair was found before also: a cell has no members.
    public static Cell<int> CellOfAPair() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Cell<int>> p = new("a", l.Map(v => v)); return S.Hold(p.Value.Sample()); });

    // S576. The key is a string, which has no state. Each shape from here to the end of the group
    // returns.
    public static Cell<int> PlainKeyOfAPair() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Titled> p = new("a", new Titled(l.Map(v => v))); Kept.Add(p.Key.Length); return S.Hold(0); });

    // S577. The same for a value that is a string.
    public static Cell<int> PlainValueOfAPairWithAnObjectKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, string> p = new(new Titled(l.Map(v => v)), "x"); Kept.Add(p.Value.Length); return S.Hold(0); });

    // S578. The key has a type that the object with the members cannot have.
    public static Cell<int> KeyOfADifferentTypeThanTheValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Plainly, Titled> p = new(new Plainly(), new Titled(l.Map(v => v))); Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S579. The object in the pair has a plain cell.
    public static Cell<int> ValueOfAPairOfPlainObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Titled> p = new("a", new Titled(Plain)); Kept.Add(p.Value.ToString()); return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S580. Count is a number.
    public static Cell<int> CountOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; return S.Hold(all.Count); });

    // S581. IsCompleted is a boolean.
    public static Cell<int> IsCompletedOfATask() =>
        Cell.Loop<int>().WithoutCaptures(l => { Task<Titled> t = Task.FromResult(new Titled(l.Map(v => v))); return S.Hold(t.IsCompleted ? 1 : 0); });

    // S582. The same in a property pattern.
    public static Cell<int> PropertyPatternOnTheCountOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; return S.Hold(all is { Count: var c } ? c : 0); });

    // S583. A member that is a sequence contains the values, and is not one of them.
    public static Cell<int> ValuesOfADictionaryInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Titled> d = new() { ["a"] = new Titled(l.Map(v => v)) }; object o = d.Values; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S584. The same in a property pattern.
    public static Cell<int> ValuesOfADictionaryInAPatternInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Titled> d = new() { ["a"] = new Titled(l.Map(v => v)) }; if (d is { Values: var vs }) { object o = vs; Kept.Add(o.ToString()!); } return S.Hold(0); });

    // S585. A value of the type object can be an object that has members, or a cell. A method of the
    // platform that returns object gives its result the state of what it got. First gives one element
    // as object.
    public static Cell<int> FirstOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; Kept.Add(all.First().ToString()!); return S.Hold(0); });

    // S586. The element goes to a local of the type object.
    public static Cell<int> SingleOfAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; object back = all.Single(); Kept.Add(back.ToString()!); return S.Hold(0); });

    // S587. Find gives one element.
    public static Cell<int> FindInAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; Kept.Add(all.Find(x => true)!.ToString()!); return S.Hold(0); });

    // S588. The queue got the object from Enqueue.
    public static Cell<int> DequeueOfAQueueOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Queue<object> q = new(); q.Enqueue(new Titled(l.Map(v => v))); Kept.Add(q.Dequeue().ToString()!); return S.Hold(0); });

    // S589. The same for a stack.
    public static Cell<int> PeekOfAStackOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stack<object> st = new(); st.Push(new Titled(l.Map(v => v))); Kept.Add(st.Peek().ToString()!); return S.Hold(0); });

    // S590. The values of a dictionary are a sequence of objects.
    public static Cell<int> FirstValueOfADictionaryOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, object> d = new() { ["a"] = new Titled(l.Map(v => v)) }; Kept.Add(d.Values.First().ToString()!); return S.Hold(0); });

    // S591. An array of objects can hold a state also.
    public static Cell<int> ElementOfAnArrayFromAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; object[] copy = all.ToArray(); Kept.Add(copy[0].ToString()!); return S.Hold(0); });

    // S592. A sequence of objects that a method of the platform makes.
    public static Cell<int> FirstOfARepeatedObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Enumerable.Repeat<object>(new Titled(l.Map(v => v)), 1).First().ToString()!); return S.Hold(0); });

    // S593. A span of objects.
    public static Cell<int> ElementOfASpanOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { object[] all = [new Titled(l.Map(v => v))]; Kept.Add(all.AsSpan()[0].ToString()!); return S.Hold(0); });

    // S594. ElementAt gives one element.
    public static Cell<int> ElementAtOfASequenceOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<object> all = new List<object> { new Titled(l.Map(v => v)) }; Kept.Add(all.ElementAt(0).ToString()!); return S.Hold(0); });

    // S595. Cast makes a sequence of objects from a list with a type.
    public static Cell<int> FirstAfterACastToObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; object first = all.Cast<object>().First(); Kept.Add(first.ToString()!); return S.Hold(0); });

    // S596. LastOrDefault gives one element.
    public static Cell<int> LastOrDefaultOfAnArrayOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { object[] all = [new Titled(l.Map(v => v))]; Kept.Add(all.LastOrDefault()!.ToString()!); return S.Hold(0); });

    // S597. MaxBy gives one element.
    public static Cell<int> MaxByOfAnArrayOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { object[] all = [new Titled(l.Map(v => v))]; Kept.Add(all.MaxBy(x => 1)!.ToString()!); return S.Hold(0); });

    // S598. A method of the platform gives the value for a key.
    public static Cell<int> GetValueOrDefaultOfADictionaryOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, object> d = new() { ["a"] = new Titled(l.Map(v => v)) }; Kept.Add(d.GetValueOrDefault("a")!.ToString()!); return S.Hold(0); });

    // S599. A method that returns object gives back the object that it got.
    public static Cell<int> ObjectFromAMethodThatReturnsItsArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(new Titled(l.Map(v => v)))!; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S600. A property of the type object, of an enumerator of objects.
    public static Cell<int> CurrentOfAnEnumeratorOfPlainObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; using IEnumerator<object> e = all.GetEnumerator(); e.MoveNext(); Kept.Add(e.Current.ToString()!); return S.Hold(0); });

    // S601. The same for a Tuple.
    public static Cell<int> ItemOfATupleOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<object, int> t = Tuple.Create<object, int>(new Titled(l.Map(v => v)), 1); Kept.Add(t.Item1.ToString()!); return S.Hold(0); });

    // S602. The same for a pair.
    public static Cell<int> ValueOfAPairOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, object> p = new("a", new Titled(l.Map(v => v))); Kept.Add(p.Value.ToString()!); return S.Hold(0); });

    // S603. The same for a task.
    public static Cell<int> ResultOfATaskOfAPlainObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Task<object> t = Task.FromResult<object>(new Titled(l.Map(v => v))); Kept.Add(t.Result.ToString()!); return S.Hold(0); });

    // S604. Target of a WeakReference has the type object.
    public static Cell<int> TargetOfAWeakReference() =>
        Cell.Loop<int>().WithoutCaptures(l => { Titled n = new(l.Map(v => v)); WeakReference w = new(n); Kept.Add(w.Target!.ToString()!); GC.KeepAlive(n); return S.Hold(0); });

    // S605. A collection that is not generic has objects as its elements.
    public static Cell<int> PopOfAStackThatIsNotGeneric() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.Stack st = new(); st.Push(new Titled(l.Map(v => v))); Kept.Add(st.Pop()!.ToString()!); return S.Hold(0); });

    // S606. The same for its enumerator.
    public static Cell<int> CurrentOfAnEnumeratorThatIsNotGeneric() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.ArrayList all = new() { new Titled(l.Map(v => v)) }; System.Collections.IEnumerator e = all.GetEnumerator(); e.MoveNext(); Kept.Add(e.Current!.ToString()!); return S.Hold(0); });

    // S607. The same for an entry of a Hashtable.
    public static Cell<int> ValueOfADictionaryEntry() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.Hashtable h = new() { ["a"] = new Titled(l.Map(v => v)) }; foreach (System.Collections.DictionaryEntry entry in h) { Kept.Add(entry.Value!.ToString()!); } return S.Hold(0); });

    // S608. The element is a cell, and a cast gives it its type again.
    public static Cell<int> CellFromAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> cells = [l.Map(v => v)]; return S.Hold(((Cell<int>)cells.First()).Sample()); });

    // S609. The object has a plain cell. Each shape from here to the end of the group returns.
    public static Cell<int> FirstOfAListOfPlainObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(Plain)]; Kept.Add(all.First().ToString()!); return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S610. The method gives back a list, and ToString of a list calls nothing.
    public static Cell<int> ListFromAMethodThatReturnsItsArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; object o = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(all)!; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S611. Reflection makes a new object, which is not the object that it got.
    public static Cell<int> ObjectMadeByReflectionFromAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Activator.CreateInstance(typeof(ObjectWrapper), new Titled(l.Map(v => v)))!; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S612. The result of a method that reflection calls is not the object of the call.
    public static Cell<int> ResultOfAMethodCalledByReflection() =>
        Cell.Loop<int>().WithoutCaptures(l => { Titled n = new(l.Map(v => v)); object r = typeof(object).GetMethod("GetHashCode")!.Invoke(n, null)!; Kept.Add(r.ToString()!); return S.Hold(0); });

    // S613. SyncRoot is an object to lock, and not an element.
    public static Cell<int> SyncRootOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; object sync = ((System.Collections.ICollection)all).SyncRoot; Kept.Add(sync.ToString()!); return S.Hold(0); });

    // S614. A number in a local of the type object has no state.
    public static Cell<int> CountOfAListOfObjectsInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; object n = all.Count; Kept.Add(n.ToString()!); return S.Hold(0); });

    // S615. The same for a collection that is not generic.
    public static Cell<int> CountOfAnArrayListInAnObjectLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.ArrayList all = new() { new Titled(l.Map(v => v)) }; object n = all.Count; Kept.Add(n.ToString()!); return S.Hold(0); });

    // S616. GetType calls no member of the object.
    public static Cell<int> TypeOfAnObjectFromAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; object type = all.First().GetType(); Kept.Add(type.ToString()!); return S.Hold(0); });

    // S617. The object has no Equals of its own.
    public static Cell<int> EqualsOnAnObjectFromAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Titled(l.Map(v => v))]; return S.Hold(all.First().Equals(null) ? 1 : 0); });

    // S618. Choose and WhereSome of SodaFlow.Functional make a query, as the query methods of the
    // platform do. The walk reads the function where something enumerates the result. The block only
    // keeps the result of Choose.
    public static Cell<int> ChooseKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Choose(x => Maybe.Some(x + l.Sample())); Kept.Add(q); return S.Hold(0); });

    // S619. The same for the function that gets the index.
    public static Cell<int> ChooseWithAnIndexKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Choose((x, i) => Maybe.Some(x + i + l.Sample())); Kept.Add(q); return S.Hold(0); });

    // S620. WhereSome does not enumerate its source in the call.
    public static Cell<int> WhereSomeOverAQueryKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<Maybe<int>> source = Numbers.Select(x => Maybe.Some(x + l.Sample())); Kept.Add(source.WhereSome()); return S.Hold(0); });

    // S621. Nothing reads the loop here.
    public static Cell<int> WhereSomeOfPlainValuesKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Maybe<int>[] source = [Maybe.Some(1)]; Kept.Add(source.WhereSome()); return S.Snapshot(l, (a, b) => a + b).Hold(0); });

    // S622. ToList enumerates the result of Choose. Each shape from here to the end of the group
    // throws.
    public static Cell<int> ChooseEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Choose(x => Maybe.Some(x + l.Sample())); return S.Hold(q.ToList().Count); });

    // S623. The same for the function that gets the index.
    public static Cell<int> ChooseWithAnIndexEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Choose((x, i) => Maybe.Some(x + i + l.Sample())); return S.Hold(q.Sum()); });

    // S624. A foreach enumerates it.
    public static Cell<int> ForEachOverChoose() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = 0; foreach (int v in Numbers.Choose(x => Maybe.Some(x + l.Sample()))) { n += v; } return S.Hold(n); });

    // S625. Sum enumerates WhereSome, which enumerates its source.
    public static Cell<int> WhereSomeOverAQueryEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<Maybe<int>> source = Numbers.Select(x => Maybe.Some(x + l.Sample())); return S.Hold(source.WhereSome().Sum()); });

    // S626. FirstOrNone of the same package enumerates in the call.
    public static Cell<int> ChooseThenFirstOrNone() =>
        Cell.Loop<int>().WithoutCaptures(l => { Maybe<int> first = Numbers.Choose(x => Maybe.Some(x + l.Sample())).FirstOrNone(); Kept.Add(first); return S.Hold(0); });

    // S627. AllSomeOrNone runs its function in the call.
    public static Cell<int> AllSomeOrNoneWithAFunctionThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { Maybe<IEnumerable<int>> all = Numbers.AllSomeOrNone(x => Maybe.Some(x + l.Sample())); Kept.Add(all); return S.Hold(0); });

    // S628. The same for the predicate of FirstOrNone.
    public static Cell<int> FirstOrNoneWithAFunctionThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { Maybe<int> first = Numbers.FirstOrNone(x => x > l.Sample()); Kept.Add(first); return S.Hold(0); });

    // S629. The same for the function of MaxOrNone.
    public static Cell<int> MaxOrNoneWithAFunctionThatSamples() =>
        Cell.Loop<int>().WithoutCaptures(l => { Maybe<int> max = Numbers.MaxOrNone(x => x + l.Sample()); Kept.Add(max); return S.Hold(0); });

    // S630. A tuple in a pair is not the pair. Value gives the tuple, and the tuple has the object.
    public static Cell<int> ValueOfAPairOfATuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, (Titled N, int I)> p = new("a", (new Titled(l.Map(v => v)), 1)); Kept.Add(p.Value.N.ToString()); return S.Hold(0); });

    // S631. The same for a tuple in a task.
    public static Cell<int> ResultOfATaskOfATuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { Task<(Titled N, int I)> t = Task.FromResult((new Titled(l.Map(v => v)), 1)); Kept.Add(t.Result.N.ToString()); return S.Hold(0); });

    // S632. The other element of that tuple is a number.
    public static Cell<int> PlainElementOfATupleInAPair() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, (Titled N, int I)> p = new("a", (new Titled(l.Map(v => v)), 1)); Kept.Add(p.Value.I.ToString()); return S.Hold(0); });

    // S633. A tuple is a value of object and of ValueType. A local of such a type keeps the elements.
    public static Cell<int> TupleKeptAsObjectThenRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = (new Titled(l.Map(v => v)), 1); ValueType t = (ValueTuple<Titled, int>)o; Kept.Add(((ValueTuple<Titled, int>)t).Item1.ToString()); return S.Hold(0); });

    // S634. A tuple is a value of each interface of its struct.
    public static Cell<int> TupleKeptAsAnInterfaceThenRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { IComparable t = (new Titled(l.Map(v => v)), 1); Kept.Add(((ValueTuple<Titled, int>)t).Item1.ToString()); return S.Hold(0); });

    // S635. A nullable tuple holds the tuple. Value is a member of the nullable.
    public static Cell<int> ValueOfANullableTuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Titled N, int I)? t = (new Titled(l.Map(v => v)), 1); Kept.Add(t.Value.N.ToString()); return S.Hold(0); });

    // S636. The same for a struct of the compilation.
    public static Cell<int> ValueOfANullableStruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { TitledValue? t = new TitledValue(l.Map(v => v)); Kept.Add(t.Value.ToString()); return S.Hold(0); });

    // S637. HasValue of that nullable reads nothing.
    public static Cell<int> HasValueOfANullableStruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { TitledValue? t = new TitledValue(l.Map(v => v)); Kept.Add(t.HasValue); return S.Hold(0); });

    // S638. A pattern reads the tuple in the nullable.
    public static Cell<int> NullableTupleThroughAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Titled N, int I)? t = (new Titled(l.Map(v => v)), 1); if (t is { N: var n }) { Kept.Add(n.ToString()); } return S.Hold(0); });

    // S639. A method of the compilation adds a value to a list that it gets. The list of the caller has the value.
    public static Cell<int> HelperAddsToAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; AddTo(cs, l.Map(v => v)); int x = cs[1].Sample(); Kept.Add(x); return S.Hold(0); });

    // S640. The same for a local function.
    public static Cell<int> LocalFunctionAddsToAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; Put(cs, l.Map(v => v)); int x = cs[1].Sample(); Kept.Add(x); static void Put(List<Cell<int>> cells, Cell<int> one) => cells.Add(one); return S.Hold(0); });

    // S641. The same, and nothing reads the list.
    public static Cell<int> HelperAddsThenNothingReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; AddTo(cs, l.Map(v => v)); Kept.Add(cs); return S.Hold(0); });

    // S642. A method sets a property of an object that it gets.
    public static Cell<int> HelperSetsAMemberOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new(); FillSlot(slot, l.Map(v => v)); int x = slot.Share!.Sample(); Kept.Add(x); return S.Hold(0); });

    // S643. A method puts a value into a dictionary that it gets.
    public static Cell<int> HelperPutsIntoADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Cell<int>> cs = []; PutIn(cs, l.Map(v => v)); int x = cs["a"].Sample(); Kept.Add(x); return S.Hold(0); });

    // S644. A method sets an element of an array that it gets.
    public static Cell<int> HelperSetsAnArrayElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain]; SetFirst(cs, l.Map(v => v)); int x = cs[0].Sample(); Kept.Add(x); return S.Hold(0); });

    // S645. An extension method adds to its first parameter.
    public static Cell<int> ExtensionMethodAddsToItsReceiver() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; cs.AddOne(l.Map(v => v)); int x = cs[1].Sample(); Kept.Add(x); return S.Hold(0); });

    // S646. A constructor adds to a list that it gets.
    public static Cell<int> ConstructorAddsToAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; Kept.Add(new Registrar(cs, l.Map(v => v))); int x = cs[1].Sample(); Kept.Add(x); return S.Hold(0); });

    // S647. A method that calls the method that adds.
    public static Cell<int> HelperAddsThroughASecondHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; AddToThroughOne(cs, l.Map(v => v)); int x = cs[1].Sample(); Kept.Add(x); return S.Hold(0); });

    // S648. A method assigns a new list to its parameter, and adds to that list. The list of the caller is the same.
    public static Cell<int> HelperAddsToAListOfItsOwn() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain]; AddToANewList(cs, l.Map(v => v)); int x = cs[0].Sample(); Kept.Add(x); return S.Hold(0); });

    // S649. A struct is a copy. A method that sets a property of its copy changes nothing for the caller.
    public static Cell<int> HelperSetsAMemberOfAStructCopy() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotValue slot = new() { Share = Plain }; FillCopy(slot, l.Map(v => v)); int x = slot.Share!.Sample(); Kept.Add(x); return S.Hold(0); });

    // S650. An element of a list of lists is a list. ToString of it is the one of the platform.
    public static Cell<int> ListInAListAsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Titled>> outer = [[new(l.Map(v => v))]]; object o = outer[0]; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S651. The same for the variable of a foreach.
    public static Cell<int> ForeachInnerListAsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Titled>> outer = [[new(l.Map(v => v))]]; foreach (List<Titled> inner in outer) { object o = inner; Kept.Add(o.ToString()!); } return S.Hold(0); });

    // S652. The same for an array of arrays.
    public static Cell<int> ArrayInAnArrayAsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Titled[][] outer = [[new(l.Map(v => v))]]; object o = outer[0]; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S653. An element of the inner list is one of the objects.
    public static Cell<int> ElementOfTheInnerList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Titled>> outer = [[new(l.Map(v => v))]]; Kept.Add(outer[0][0].ToString()); return S.Hold(0); });

    // S654. The same after the inner list was a local of the type object.
    public static Cell<int> InnerListAsObjectThenElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Titled>> outer = [[new(l.Map(v => v))]]; object o = outer[0]; Kept.Add(((List<Titled>)o)[0].ToString()); return S.Hold(0); });

    // S655. The same through two foreach statements.
    public static Cell<int> ForeachInnerThenElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Titled>> outer = [[new(l.Map(v => v))]]; foreach (List<Titled> inner in outer) { foreach (Titled n in inner) { Kept.Add(n.ToString()); } } return S.Hold(0); });

    // S656. A sequence of the compilation in a list is one of the objects, and it has its own ToString.
    public static Cell<int> PileInAListAsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> piles = [new TitledPile(l.Map(v => v))]; object o = piles[0]; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S657. The same for the variable of a foreach.
    public static Cell<int> ForeachPileAsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> piles = [new TitledPile(l.Map(v => v))]; foreach (IEnumerable<int> pile in piles) { object o = pile; Kept.Add(o.ToString()!); } return S.Hold(0); });

    // S658. Exchange gives back the value that the location had. That value is a plain one here.
    public static Cell<int> OldValueOfAnExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Plainly(); object old = Interlocked.Exchange(ref slot, new Titled(l.Map(v => v))); Kept.Add(old.ToString()!); return S.Hold(0); });

    // S659. The value that the location had is the pending one.
    public static Cell<int> OldPendingValueOfAnExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Titled(l.Map(v => v)); object old = Interlocked.Exchange(ref slot, new Plainly()); Kept.Add(old.ToString()!); return S.Hold(0); });

    // S660. The location has the new value after the exchange.
    public static Cell<int> SlotAfterAnExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Plainly(); Interlocked.Exchange(ref slot, new Titled(l.Map(v => v))); Kept.Add(slot.ToString()!); return S.Hold(0); });

    // S661. The same as the first, for a location with a type of the compilation.
    public static Cell<int> TypedOldValueOfAnExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { Titled slot = new(Plain); Titled old = Interlocked.Exchange(ref slot, new Titled(l.Map(v => v))); Kept.Add(old.ToString()); return S.Hold(0); });

    // S662. CompareExchange gives back the value that the location had also.
    public static Cell<int> OldValueOfACompareExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Plainly(); object same = slot; object old = Interlocked.CompareExchange(ref slot, new Titled(l.Map(v => v)), same); Kept.Add(old.ToString()!); return S.Hold(0); });

    // S663. The same, and that value is the pending one.
    public static Cell<int> OldPendingValueOfACompareExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Titled(l.Map(v => v)); object same = slot; object old = Interlocked.CompareExchange(ref slot, new Plainly(), same); Kept.Add(old.ToString()!); return S.Hold(0); });

    // S664. After CompareExchange, the location can have the new value.
    public static Cell<int> SlotAfterACompareExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Plainly(); object same = slot; Interlocked.CompareExchange(ref slot, new Titled(l.Map(v => v)), same); Kept.Add(slot.ToString()!); return S.Hold(0); });

    // S665. After Exchange, the location does not have the old value.
    public static Cell<int> SlotAfterAnExchangeOfAPendingValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Titled(l.Map(v => v)); Interlocked.Exchange(ref slot, new Plainly()); Kept.Add(slot.ToString()!); return S.Hold(0); });

    // S666. The arguments of an exchange by name, in a different sequence.
    public static Cell<int> OldValueOfAnExchangeWithNamedArguments() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Plainly(); object old = Interlocked.Exchange(value: new Titled(l.Map(v => v)), location1: ref slot); Kept.Add(old.ToString()!); Kept.Add(slot); return S.Hold(0); });

    // S667. A property whose getter is an iterator runs nothing at the read. The block keeps the result.
    public static Cell<int> IteratorPropertyKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); Kept.Add(r.Values); return S.Hold(0); });

    // S668. A foreach enumerates the property, and the getter runs.
    public static Cell<int> IteratorPropertyInAForeach() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); int n = 0; foreach (int v in r.Values) { n += v; } return S.Hold(n); });

    // S669. ToList enumerates it.
    public static Cell<int> IteratorPropertyToList() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); return S.Hold(r.Values.ToList().Count); });

    // S670. A query on the property waits also.
    public static Cell<int> IteratorPropertyThenSelectKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); Kept.Add(r.Values.Select(x => x + 1)); return S.Hold(0); });

    // S671. The property in a local, and then Count.
    public static Cell<int> IteratorPropertyInALocalThenCount() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); IEnumerable<int> v = r.Values; return S.Hold(v.Count()); });

    // S672. A method of the object reads the property with no owner, and gives it back.
    public static Cell<int> IteratorPropertyWithNoOwnerKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); Kept.Add(r.Held()); return S.Hold(0); });

    // S673. A method of the object reads the property with no owner, and enumerates it.
    public static Cell<int> IteratorPropertyWithNoOwnerSum() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); return S.Hold(r.Sum()); });

    // S674. The property through a conditional access, kept.
    public static Cell<int> IteratorPropertyThroughAConditionKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder? r = new(l.Map(v => v)); Kept.Add(r?.Values!); return S.Hold(0); });

    // S675. The property through a conditional access, enumerated.
    public static Cell<int> IteratorPropertyThroughAConditionEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder? r = new(l.Map(v => v)); return S.Hold(r?.Values.Count() ?? 0); });

    // S676. A property that is an enumerator, kept.
    public static Cell<int> EnumeratorPropertyKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); Kept.Add(r.Steps); return S.Hold(0); });

    // S677. MoveNext of that enumerator runs the getter.
    public static Cell<int> EnumeratorPropertyMoved() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); IEnumerator<int> e = r.Steps; e.MoveNext(); return S.Hold(e.Current); });

    // S678. A spread enumerates the property.
    public static Cell<int> IteratorPropertySpread() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); int[] all = [.. r.Values]; return S.Hold(all.Length); });

    // S679. The property kept, and then enumerated.
    public static Cell<int> IteratorPropertyKeptThenEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); IEnumerable<int> v = r.Values; Kept.Add(v); return S.Hold(string.Join(",", v).Length); });

    // S680. A getter with code and no yield runs at the read.
    public static Cell<int> GetterWithCodeThatReturnsASequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); Kept.Add(r.Now); return S.Hold(0); });

    // S681. A serializer enumerates a query that it gets.
    public static Cell<int> QuerySerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(JsonSerializer.Serialize(q)); return S.Hold(0); });

    // S682. The same for a query given as an object.
    public static Cell<int> QuerySerializedAsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(JsonSerializer.Serialize<object>(q)); return S.Hold(0); });

    // S683. The same for a query in an anonymous object.
    public static Cell<int> QueryInAnAnonymousObjectSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(JsonSerializer.Serialize(new { Values = q })); return S.Hold(0); });

    // S684. A serializer calls each public getter of an object.
    public static Cell<int> ObjectWithAGetterThatReadsSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialRow row = new(l.Map(v => v)); Kept.Add(JsonSerializer.Serialize(row)); return S.Hold(0); });

    // S685. The getters of this object read nothing.
    public static Cell<int> ObjectWithAPlainGetterSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialPlainRow row = new(l.Map(v => v)); Kept.Add(JsonSerializer.Serialize(row)); return S.Hold(0); });

    // S686. A serializer does not call a getter that has JsonIgnore, or one that is not public.
    public static Cell<int> ObjectWithIgnoredGettersSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialIgnoredRow row = new(l.Map(v => v)); Kept.Add(JsonSerializer.Serialize(row)); return S.Hold(0); });

    // S687. JsonIgnore with a condition reads the value before it decides.
    public static Cell<int> ObjectWithAConditionOnJsonIgnoreSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialConditionRow row = new(l.Map(v => v)); Kept.Add(JsonSerializer.Serialize(row)); return S.Hold(0); });

    // S688. A serializer writes an object that a getter gives, and calls the getters of that object.
    public static Cell<int> ObjectInAnObjectSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialOuter outer = new(new SerialRow(l.Map(v => v))); Kept.Add(JsonSerializer.Serialize(outer)); return S.Hold(0); });

    // S689. A serializer writes each element of a list.
    public static Cell<int> ListOfObjectsSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SerialRow> rows = [new(l.Map(v => v))]; Kept.Add(JsonSerializer.Serialize(rows)); return S.Hold(0); });

    // S690. A getter gives a query, and the serializer enumerates it.
    public static Cell<int> ObjectWithAGetterThatGivesAQuerySerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); SerialQueryRow row = new(q); Kept.Add(JsonSerializer.Serialize(row)); return S.Hold(0); });

    // S691. An object keeps a query that no property gives. The serializer does not get it.
    public static Cell<int> ObjectWithAQueryThatNoGetterGivesSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); QueryHolder holder = new(q); Kept.Add(JsonSerializer.Serialize(holder)); return S.Hold(0); });

    // S692. System.Text.Json does not write the fields of a tuple.
    public static Cell<int> TupleOfAQuerySerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(JsonSerializer.Serialize((q, 1))); return S.Hold(0); });

    // S693. JsonConvert of Newtonsoft.Json enumerates a query.
    public static Cell<int> QuerySerializedByJsonConvert() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(Newtonsoft.Json.JsonConvert.SerializeObject(q)); return S.Hold(0); });

    // S694. JsonConvert calls each public getter of an object.
    public static Cell<int> ObjectSerializedByJsonConvert() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialRow row = new(l.Map(v => v)); Kept.Add(Newtonsoft.Json.JsonConvert.SerializeObject(row)); return S.Hold(0); });

    // S695. JsonConvert writes the fields of a tuple.
    public static Cell<int> TupleOfAQuerySerializedByJsonConvert() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); Kept.Add(Newtonsoft.Json.JsonConvert.SerializeObject((q, 1))); return S.Hold(0); });

    // S696. XmlSerializer calls the getter of a property that it can set.
    public static Cell<int> ObjectSerializedByXmlSerializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { XmlRow row = new(l.Map(v => v)); new XmlSerializer(typeof(XmlRow)).Serialize(TextWriter.Null, row); return S.Hold(0); });

    // S697. XmlSerializer does not write a property with no setter.
    public static Cell<int> ObjectWithNoSetterSerializedByXmlSerializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { XmlFixedRow row = new(l.Map(v => v)); new XmlSerializer(typeof(XmlFixedRow)).Serialize(TextWriter.Null, row); return S.Hold(0); });

    // S698. A sorted set keeps the comparer that its constructor gets. The second Add calls it.
    public static Cell<int> ComparerKeptBySortedSetThenTwoAdds() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<int> set = new(new ReadingComparer(l.Map(v => v))); set.Add(1); set.Add(2); return S.Hold(set.Count); });

    // S699. The constructor keeps the comparer and calls nothing.
    public static Cell<int> ComparerKeptBySortedSetOnly() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<int> set = new(new ReadingComparer(l.Map(v => v))); Kept.Add(set); return S.Hold(set.Count); });

    // S700. The same for the indexer of a sorted dictionary.
    public static Cell<int> ComparerKeptBySortedDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<int, int> d = new(new ReadingComparer(l.Map(v => v))); d[1] = 1; d[2] = 2; return S.Hold(d.Count); });

    // S701. A dictionary keeps an equality comparer. The indexer calls GetHashCode of it.
    public static Cell<int> EqualityKeptByDictionaryThenSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<int, int> d = new(new ReadingEquality(l.Map(v => v))); d[1] = 1; return S.Hold(d.Count); });

    // S702. The constructor of a hash set keeps the equality comparer and calls nothing.
    public static Cell<int> EqualityKeptByHashSetOnly() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<int> set = new(new ReadingEquality(l.Map(v => v))); return S.Hold(set.Count); });

    // S703. A constructor that gets items and a comparer calls the comparer.
    public static Cell<int> ComparerAndItemsInTheConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<int> set = new(Numbers, new ReadingComparer(l.Map(v => v))); return S.Hold(set.Count); });

    // S704. Clear and Max of the set call nothing.
    public static Cell<int> ComparerKeptThenClearAndMax() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<int> set = new(new ReadingComparer(l.Map(v => v))); set.Clear(); return S.Hold(set.Max); });

    // S705. A collection initializer calls Add, and Add calls the comparer.
    public static Cell<int> ComparerKeptThenACollectionInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<int> set = new(new ReadingComparer(l.Map(v => v))) { 1, 2 }; return S.Hold(set.Count); });

    // S706. An initializer with an index calls the indexer.
    public static Cell<int> EqualityKeptThenAnInitializerWithAnIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<int, int> d = new(new ReadingEquality(l.Map(v => v))) { [1] = 1 }; return S.Hold(d.Count); });

    // S707. A priority queue keeps a comparer for its priorities. The second Enqueue calls it.
    public static Cell<int> ComparerKeptByAPriorityQueue() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, int> queue = new(new ReadingComparer(l.Map(v => v))); queue.Enqueue(1, 1); queue.Enqueue(2, 2); return S.Hold(queue.Count); });

    // S708. EnqueueDequeue on a queue with one element calls the comparer.
    public static Cell<int> ComparerKeptByAPriorityQueueThenEnqueueDequeue() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, int> queue = new(new ReadingComparer(l.Map(v => v))); queue.Enqueue(1, 1); return S.Hold(queue.EnqueueDequeue(2, 2)); });

    // S709. EnqueueRange calls the comparer.
    public static Cell<int> ComparerKeptByAPriorityQueueThenEnqueueRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, int> queue = new(new ReadingComparer(l.Map(v => v))); queue.EnqueueRange([(1, 1), (2, 2)]); return S.Hold(queue.Count); });

    // S710. The constructor of a priority queue that gets items and a comparer calls the comparer.
    public static Cell<int> ComparerAndItemsInTheConstructorOfAPriorityQueue() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, int> queue = new([(1, 1), (2, 2)], new ReadingComparer(l.Map(v => v))); return S.Hold(queue.Count); });

    // S711. A priority queue with no comparer calls CompareTo of its priorities.
    public static Cell<int> PrioritiesOfAPriorityQueueCompared() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, Ranked> queue = new(); queue.Enqueue(1, new Ranked(l.Map(v => v))); queue.Enqueue(2, new Ranked(Plain)); return S.Hold(queue.Count); });

    // S712. A priority queue does not compare its elements.
    public static Cell<int> ElementsOfAPriorityQueueNotCompared() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<Ranked, int> queue = new(); queue.Enqueue(new Ranked(l.Map(v => v)), 1); queue.Enqueue(new Ranked(Plain), 2); return S.Hold(queue.Count); });

    // S713. A sorted set of objects with a comparer calls the comparer, and not CompareTo of the objects.
    public static Cell<int> ComparerKeptBySortedSetOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> set = new(new RankedByOffset(l.Map(v => v))); set.Add(new Ranked(Plain)); set.Add(new Ranked(Plain)); return S.Hold(set.Count); });

    // S714. An object that a method filled is not a container. In a local of the type object, it is the object.
    public static Cell<int> ToStringOfAnObjectFilledByAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { TitledSlot slot = new(l.Map(v => v)); FillTitled(slot, l.Map(v => v)); object o = slot; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S715. CompareExchange with a comparand that is a different object leaves the old value in the location.
    public static Cell<int> SlotAfterACompareExchangeThatDoesNotExchange() =>
        Cell.Loop<int>().WithoutCaptures(l => { object slot = new Titled(l.Map(v => v)); object other = new Plainly(); Interlocked.CompareExchange(ref slot, new Plainly(), other); Kept.Add(slot.ToString()!); return S.Hold(0); });

    // S716. A serializer does not write a static property.
    public static Cell<int> ObjectWithAStaticGetterSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { SerialStaticRow.Shared = l.Map(v => v); Kept.Add(JsonSerializer.Serialize(new SerialStaticRow())); return S.Hold(0); });

    // S717. A serializer enumerates a sequence of the compilation.
    public static Cell<int> SequenceOfTheCompilationSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { Sack sack = new(l.Map(v => v)); Kept.Add(JsonSerializer.Serialize(sack)); return S.Hold(0); });

    // S718. An object whose constructor gets nothing keeps its type. The block sets a property, and ToString reads it through object.
    public static Cell<int> PropertySetAfterTheConstructorReadThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { LateSlot slot = new(); slot.Share = l.Map(v => v); object o = slot; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S719. The same read through the type of the object.
    public static Cell<int> PropertySetAfterTheConstructorReadThroughItsType() =>
        Cell.Loop<int>().WithoutCaptures(l => { LateSlot slot = new(); slot.Share = l.Map(v => v); Kept.Add(slot.ToString()); return S.Hold(0); });

    // S720. The same, and a method sets the property.
    public static Cell<int> PropertySetByAHelperAfterTheConstructorReadThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { LateSlot slot = new(); FillLate(slot, l.Map(v => v)); object o = slot; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S721. The same, and an object initializer sets the property.
    public static Cell<int> PropertySetByAnInitializerReadThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { LateSlot slot = new() { Share = l.Map(v => v) }; object o = slot; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S722. The property gets a plain cell.
    public static Cell<int> PlainPropertySetAfterTheConstructorReadThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { LateSlot slot = new(); slot.Share = Plain; object o = slot; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S723. A value that is one of two objects. The one with no state does not take the type from the other.
    public static Cell<int> OneOfTwoObjectsReadThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new Plainly() : new Titled(l.Map(v => v)); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S724. A tuple in a local of the type object. ToString of a tuple calls the one of each element.
    public static Cell<int> ToStringOfATupleThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = (new Titled(l.Map(v => v)), 1); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S725. The same through ValueType.
    public static Cell<int> ToStringOfATupleThroughValueType() =>
        Cell.Loop<int>().WithoutCaptures(l => { ValueType t = (new Titled(l.Map(v => v)), 1); Kept.Add(t.ToString()!); return S.Hold(0); });

    // S726. The elements of this tuple read nothing.
    public static Cell<int> ToStringOfAPlainTupleThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = (new Titled(Plain), 1); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S727. The indexer of ITuple gives one element of the tuple.
    public static Cell<int> ElementOfATupleThroughITuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Runtime.CompilerServices.ITuple t = (new Titled(l.Map(v => v)), 1); Kept.Add(t[0]!.ToString()!); return S.Hold(0); });

    // S728. Equals of a tuple through object calls Equals of each element.
    public static Cell<int> EqualsOfATupleThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = (new Alike(l.Map(v => v)), 1); object p = (new Alike(Plain), 1); Kept.Add(o.Equals(p)); return S.Hold(0); });

    // S729. GetHashCode of a tuple calls the one of each element.
    public static Cell<int> GetHashCodeOfATuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Hashed H, int I) t = (new Hashed(l.Map(v => v)), 1); Kept.Add(t.GetHashCode()); return S.Hold(0); });

    // S730. The same through object.
    public static Cell<int> GetHashCodeOfATupleThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = (new Hashed(l.Map(v => v)), 1); Kept.Add(o.GetHashCode()); return S.Hold(0); });

    // S731. CompareTo of a tuple calls the one of each element.
    public static Cell<int> CompareToOfATuple() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Ranked R, int I) t = (new Ranked(l.Map(v => v)), 1); Kept.Add(t.CompareTo((new Ranked(Plain), 1))); return S.Hold(0); });

    // S732. The same through IComparable.
    public static Cell<int> CompareToOfATupleThroughAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IComparable c = (new Ranked(l.Map(v => v)), 1); Kept.Add(c.CompareTo((new Ranked(Plain), 1))); return S.Hold(0); });

    // S733. An element of a list of tuples is a tuple, and the code reads one element of it.
    public static Cell<int> PendingElementOfATupleInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<(Titled N, int I)> all = [(new Titled(l.Map(v => v)), 1)]; Kept.Add(all[0].N.ToString()); return S.Hold(0); });

    // S734. The other element of that tuple is a number.
    public static Cell<int> PlainElementOfATupleInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<(Titled N, int I)> all = [(new Titled(l.Map(v => v)), 1)]; Kept.Add(all[0].I.ToString()); return S.Hold(0); });

    // S735. A property pattern reads a property whose getter is an iterator. The block keeps the result.
    public static Cell<int> IteratorPropertyByAPatternKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Values: var v }) { Kept.Add(v); } return S.Hold(0); });

    // S736. The block enumerates the variable of the pattern.
    public static Cell<int> IteratorPropertyByAPatternThenCount() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Values: var v }) { return S.Hold(v.Count()); } return S.Hold(0); });

    // S737. A foreach enumerates the variable of the pattern.
    public static Cell<int> IteratorPropertyByAPatternInAForeach() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Values: var v }) { foreach (int x in v) { Kept.Add(x); } } return S.Hold(0); });

    // S738. The condition of the same statement enumerates the variable.
    public static Cell<int> IteratorPropertyByAPatternThenAnyInTheCondition() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Values: var v } && v.Any()) { Kept.Add(1); } return S.Hold(0); });

    // S739. The variable kept, and then enumerated.
    public static Cell<int> IteratorPropertyByAPatternKeptThenEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Values: var v }) { Kept.Add(v); return S.Hold(string.Join(",", v).Length); } return S.Hold(0); });

    // S740. A pattern that only tests the property for null enumerates nothing.
    public static Cell<int> IteratorPropertyByAPatternThatTestsForNull() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Values: not null }) { Kept.Add(1); } return S.Hold(0); });

    // S741. The pattern of a switch expression, and the block keeps the result.
    public static Cell<int> IteratorPropertyByASwitchExpressionKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); IEnumerable<int> v = r switch { { Values: var x } => x }; Kept.Add(v); return S.Hold(0); });

    // S742. The same, and the block enumerates the result.
    public static Cell<int> IteratorPropertyByASwitchExpressionThenCount() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); IEnumerable<int> v = r switch { { Values: var x } => x }; return S.Hold(v.Count()); return S.Hold(0); });

    // S743. The property of a member of the object, kept.
    public static Cell<int> IteratorPropertyOfAMemberByAPatternKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { YielderHolder h = new(new Yielder(l.Map(v => v))); if (h is { Inner.Values: var v }) { Kept.Add(v); } return S.Hold(0); });

    // S744. The same, enumerated.
    public static Cell<int> IteratorPropertyOfAMemberByAPatternThenCount() =>
        Cell.Loop<int>().WithoutCaptures(l => { YielderHolder h = new(new Yielder(l.Map(v => v))); if (h is { Inner.Values: var v }) { return S.Hold(v.Count()); } return S.Hold(0); });

    // S745. A property that is an enumerator, read by a pattern and kept.
    public static Cell<int> EnumeratorPropertyByAPatternKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Steps: var e }) { Kept.Add(e); } return S.Hold(0); });

    // S746. MoveNext of that enumerator runs the getter.
    public static Cell<int> EnumeratorPropertyByAPatternMoved() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Steps: var e }) { e.MoveNext(); return S.Hold(e.Current); } return S.Hold(0); });

    // S747. A getter with code and no yield runs at the pattern.
    public static Cell<int> GetterWithCodeByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); if (r is { Now: var n }) { Kept.Add(n); } return S.Hold(0); });

    // S748. A method reads the property by a pattern and gives the result back. The block keeps it.
    public static Cell<int> IteratorPropertyByAPatternInAHelperKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); Kept.Add(ValuesByAPattern(r)); return S.Hold(0); });

    // S749. The same, and the block enumerates it.
    public static Cell<int> IteratorPropertyByAPatternInAHelperThenCount() =>
        Cell.Loop<int>().WithoutCaptures(l => { Yielder r = new(l.Map(v => v)); return S.Hold(ValuesByAPattern(r).Count()); return S.Hold(0); });

    // S750. An element of a tuple through ITuple, and a cast to its type. The element is one of the elements.
    public static Cell<int> ElementOfATupleThroughITupleThenCast() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Runtime.CompilerServices.ITuple t = (new Titled(l.Map(v => v)), 1); Titled x = (Titled)t[0]!; Kept.Add(x.ToString()); return S.Hold(0); });

    // S751. An element of a list of objects is the tuple, and the code reads the plain element of it.
    public static Cell<int> PlainElementOfATupleInAListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [(new Titled(l.Map(v => v)), new Titled(Plain))]; Kept.Add(((ValueTuple<Titled, Titled>)all[0]).Item2.ToString()); return S.Hold(0); });

    // S752. An array of tuples from a method of the platform. An element of it is a tuple.
    public static Cell<int> PlainElementOfATupleInAnArrayFromThePlatform() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Titled N, Titled M)[] all = Enumerable.Repeat((new Titled(l.Map(v => v)), new Titled(Plain)), 1).ToArray(); Kept.Add(all[0].M.ToString()); return S.Hold(0); });

    // S753. A list with two kinds of objects. An element is one of the two, and the code calls ToString of it.
    public static Cell<int> ElementOfAListOfTwoKindsOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new TitledPile(l.Map(v => v)), new Titled(l.Map(v => v))]; Kept.Add(all[1].ToString()!); return S.Hold(0); });

    // S754. A value that is one of two objects, through object.
    public static Cell<int> OneOfTwoPendingObjectsThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new TitledPile(l.Map(v => v)) : new Titled(l.Map(v => v)); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S755. A value that is one of two objects, through an interface that the two have.
    public static Cell<int> OneOfTwoPendingObjectsThroughAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = Numbers.Length > 100 ? new GenericLabel<int>(l.Map(v => v)) : new ShownReader(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S756. A foreach over a list of two kinds of objects, and a method of the interface.
    public static Cell<int> ForeachOverAListOfTwoKindsOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IShown> all = [new ShownPlain(l.Map(v => v)), new ShownReader(l.Map(v => v))]; foreach (IShown s in all) { Kept.Add(s.Show()); } return S.Hold(0); });

    // S757. A method of the platform calls ToString of each element of a list of two kinds of objects.
    public static Cell<int> JoinOfAListOfTwoKindsOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new SerialPlainRow(l.Map(v => v)), new Titled(l.Map(v => v))]; Kept.Add(string.Join(",", all)); return S.Hold(0); });

    // S758. A serializer writes each element of a list of two kinds of objects.
    public static Cell<int> ListOfTwoKindsOfObjectsSerialized() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> rows = [new SerialPlainRow(l.Map(v => v)), new SerialRow(l.Map(v => v))]; Kept.Add(JsonSerializer.Serialize(rows)); return S.Hold(0); });

    // S759. A property pattern reads a getter of a value that is one of two objects.
    public static Cell<int> GetterOfOneOfTwoObjectsByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new Titled(l.Map(v => v)) : new SerialRow(l.Map(v => v)); if (o is SerialRow { Now: var n }) { Kept.Add(n); } return S.Hold(0); });

    // S760. The key of a pair has a type of its own. Through that type, the value is not the object.
    public static Cell<int> KeyWithAStateOfADifferentTypeThanTheValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<SerialPlainRow, Titled> p = new(new SerialPlainRow(l.Map(v => v)), new Titled(l.Map(v => v))); Kept.Add(p.Key.ToString()!); return S.Hold(0); });

    // S761. Of two objects, only one has the interface. A method with the same name in the other type is not called.
    public static Cell<int> MethodOfAnInterfaceThatOneOfTwoObjectsHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new ShowsAlone(l.Map(v => v)) : new ShownPlain(l.Map(v => v)); Kept.Add(((IShown)o).Show()); return S.Hold(0); });

    // S762. Of two objects, only one has the interface. A property with the same name in the other type is not read.
    public static Cell<int> PropertyOfAnInterfaceThatOneOfTwoObjectsHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new CellAlone(l.Map(v => v)) : new CellHolder(l.Map(v => v)); int x = ((IHasCell)o).Share.Sample(); Kept.Add(x); return S.Hold(0); });

    // S763. One of two objects, and each ToString reads nothing that is pending.
    public static Cell<int> OneOfTwoPlainObjectsThroughObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new Plainly() : new Titled(Plain); Kept.Add(o.ToString()!); return S.Hold(0); });

    // S764. A foreach over a value that is one of two sequences of the compilation.
    public static Cell<int> ForeachOverOneOfTwoSequences() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> seq = Numbers.Length > 100 ? new Stroll(Plain) : new Sack(l.Map(v => v)); foreach (int x in seq) { Kept.Add(x); } return S.Hold(0); });

    // S765. A getter of an interface, on a value that is one of two objects.
    public static Cell<int> GetterOfAnInterfaceOnOneOfTwoObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { IHasNow h = Numbers.Length > 100 ? new NowPlain(l.Map(v => v)) : new NowReader(l.Map(v => v)); Kept.Add(h.Now); return S.Hold(0); });

    // S766. The same getter, read by a property pattern.
    public static Cell<int> GetterOfAnInterfaceOnOneOfTwoObjectsByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { IHasNow h = Numbers.Length > 100 ? new NowPlain(l.Map(v => v)) : new NowReader(l.Map(v => v)); if (h is { Now: var n }) { Kept.Add(n); } return S.Hold(0); });

    // S767. An interpolation calls ToString of a value that is one of two objects.
    public static Cell<int> InterpolationOfOneOfTwoObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new Plainly() : new Titled(l.Map(v => v)); Kept.Add($"{o}"); return S.Hold(0); });

    // S768. A method that is an iterator in one type and reads in the call in the other one. The call can read.
    public static Cell<int> MethodThatIsAnIteratorInOnlyOneOfTwoObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEach e = Numbers.Length > 100 ? new EachLazy(l.Map(v => v)) : new EachNow(l.Map(v => v)); Kept.Add(e.Each()); return S.Hold(0); });

    // S769. A cast of a value that is one of two objects, to the type of the second one.
    public static Cell<int> CastOfOneOfTwoObjectsToTheSecondType() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new SerialPlainRow(l.Map(v => v)) : new Titled(l.Map(v => v)); Kept.Add(((Titled)o).ToString()); return S.Hold(0); });

    // S770. A method of the platform enumerates a value that is one of two sequences of the compilation.
    public static Cell<int> OneOfTwoSequencesGivenToThePlatform() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> seq = Numbers.Length > 100 ? new Stroll(Plain) : new Sack(l.Map(v => v)); Kept.Add(seq.ToList()); return S.Hold(0); });

    // S771. One of the two types has the method of the interface as an explicit member, which the evaluator does not find.
    public static Cell<int> MethodOfAnInterfaceThatOneOfTwoObjectsHasExplicitly() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = Numbers.Length > 100 ? new ShownExplicitly(l.Map(v => v)) : new ShownReader(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S772. A property of an interface, on a value that is one of two objects. The property of each has a state.
    public static Cell<int> PropertyOfAnInterfaceOnOneOfTwoObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { IHasCell h = Numbers.Length > 100 ? new CellHolder(l.Map(v => v)) : new CellGiver(l.Map(v => v)); int x = h.Share.Sample(); Kept.Add(x); return S.Hold(0); });

    // S773. A method of the platform gets a value that is one of two objects, through the type of the second one.
    public static Cell<int> ConcatOfOneOfTwoObjectsThroughTheSecondType() =>
        Cell.Loop<int>().WithoutCaptures(l => { object o = Numbers.Length > 100 ? new SerialPlainRow(l.Map(v => v)) : new Titled(l.Map(v => v)); Titled t = (Titled)o; Kept.Add(string.Concat(t, "x")); return S.Hold(0); });

    // S774. One of the two types has the method as an explicit member. The method of the other type runs the lambda.
    public static Cell<int> LambdaGivenToAMethodThatOneOfTwoObjectsHasExplicitly() =>
        Cell.Loop<int>().WithoutCaptures(l => { IRuns r = Numbers.Length > 100 ? new RunsExplicitly(l.Map(v => v)) : new RunsNow(l.Map(v => v)); Kept.Add(r.Run(() => l.Sample())); return S.Hold(0); });

    // S775. A method that the type has as an explicit member of an interface, called through the interface.
    public static Cell<int> ExplicitMethodThroughTheInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = new ShownExplicitReader(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S776. The same for a getter.
    public static Cell<int> ExplicitGetterThroughTheInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IHasNow h = new NowExplicitReader(l.Map(v => v)); Kept.Add(h.Now); return S.Hold(0); });

    // S777. The same getter, read by a property pattern.
    public static Cell<int> ExplicitGetterByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { IHasNow h = new NowExplicitReader(l.Map(v => v)); if (h is { Now: var n }) { Kept.Add(n); } return S.Hold(0); });

    // S778. The same method, on an element of a list.
    public static Cell<int> ExplicitMethodOnAnElementOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IShown> all = [new ShownExplicitReader(l.Map(v => v))]; Kept.Add(all[0].Show()); return S.Hold(0); });

    // S779. An explicit method that reads nothing.
    public static Cell<int> ExplicitMethodThatReadsNothing() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = new ShownExplicitly(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S780. The type has a second method with the same name, which reads. The interface calls the explicit one.
    public static Cell<int> ExplicitMethodBesideAMethodWithTheSameName() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = new ShownTwice(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S781. The second method of that type, called on the type itself.
    public static Cell<int> MethodWithTheNameOfAnExplicitOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { ShownTwice s = new(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S782. An explicit member of a generic interface.
    public static Cell<int> ExplicitMethodOfAGenericInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IComparable<RankedExplicitly> c = new RankedExplicitly(l.Map(v => v)); Kept.Add(c.CompareTo(new RankedExplicitly(Plain))); return S.Hold(0); });

    // S783. The base type has the explicit member.
    public static Cell<int> ExplicitMethodOfABaseType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IShown s = new ShownDerived(l.Map(v => v)); Kept.Add(s.Show()); return S.Hold(0); });

    // S784. A using statement calls an explicit Dispose.
    public static Cell<int> ExplicitDisposeAtAUsing() =>
        Cell.Loop<int>().WithoutCaptures(l => { using (new ClosesExplicitly(l.Map(v => v))) { Kept.Add(1); } return S.Hold(0); });

    // S785. A foreach over a sequence whose GetEnumerator is an explicit member.
    public static Cell<int> ForeachOverASequenceWithAnExplicitEnumerator() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> seq = new SackExplicitly(l.Map(v => v)); foreach (int x in seq) { Kept.Add(x); } return S.Hold(0); });

    // S786. A generic interface with two methods of one name. The type has each as an explicit member, and the second one reads.
    public static Cell<int> ExplicitOverloadOfAGenericInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IPicks<int> picks = new PicksExplicitly(l.Map(v => v)); Kept.Add(picks.Pick(1, 2)); return S.Hold(0); });

    // S787. The first method of the two reads nothing.
    public static Cell<int> ExplicitOverloadThatReadsNothing() =>
        Cell.Loop<int>().WithoutCaptures(l => { IPicks<int> picks = new PicksExplicitly(l.Map(v => v)); Kept.Add(picks.Pick(1)); return S.Hold(0); });

    // S788. A list of queries that the block only keeps. The outer query made each inner one, and nothing enumerates those.
    public static Cell<int> InnerQueriesKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); Kept.Add(lists); return S.Hold(0); });

    // S789. The count of the list enumerates no inner query.
    public static Cell<int> InnerQueriesCountOfTheList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.Count); });

    // S790. An array of queries that the block keeps.
    public static Cell<int> InnerQueriesInAnArrayKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int>[] lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToArray(); Kept.Add(lists); return S.Hold(lists.Length); });

    // S791. Join makes text from each object of the array, and does not enumerate the query in it.
    public static Cell<int> QueryInAnArrayOfObjectsJoined() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(string.Join(",", new object[] { q }).Length); });

    // S792. A foreach over the list gives each inner query, and the body only keeps it.
    public static Cell<int> InnerQueriesForeachThatKeepsEach() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); foreach (IEnumerable<int> inner in lists) { Kept.Add(inner); } return S.Hold(0); });

    // S793. A foreach over a dictionary of queries that reads only the key.
    public static Cell<int> QueriesInADictionaryForeachReadsTheKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<int, IEnumerable<int>> byKey = new() { [1] = Numbers.Select(x => x + l.Sample()) }; foreach (KeyValuePair<int, IEnumerable<int>> pair in byKey) { Kept.Add(pair.Key); } return S.Hold(0); });

    // S794. The second foreach enumerates each inner query.
    public static Cell<int> InnerQueriesInTwoForeach() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); int n = 0; foreach (IEnumerable<int> inner in lists) { foreach (int y in inner) { n += y; } } return S.Hold(n); });

    // S795. An inner query by its index, then enumerated.
    public static Cell<int> InnerQueryByIndexEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists[0].ToList().Count); });

    // S796. First gives an inner query, and Count enumerates it.
    public static Cell<int> InnerQueryFirstThenCount() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.First().Count()); });

    // S797. A lambda gets each inner query and enumerates it.
    public static Cell<int> InnerQueriesCountOfEach() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.Select(q => q.Count()).Sum()); });

    // S798. The function of the outer query reads. ToList runs it.
    public static Cell<int> OuterFunctionOfAListOfQueriesReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => { int n = l.Sample(); return Numbers.Where(y => y > n); }).ToList(); Kept.Add(lists); return S.Hold(0); });

    // S799. The value of each pair of a dictionary of queries, enumerated.
    public static Cell<int> QueriesInADictionaryValueCounted() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<int, IEnumerable<int>> byKey = new() { [1] = Numbers.Select(x => x + l.Sample()) }; int n = 0; foreach (KeyValuePair<int, IEnumerable<int>> pair in byKey) { n += pair.Value.Count(); } return S.Hold(n); });

    // S800. An inner query held as object and enumerated as the IEnumerable that is not generic.
    public static Cell<int> InnerQueryAsAnObjectEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); object first = lists[0]; int n = 0; foreach (object y in (System.Collections.IEnumerable)first) { n++; } return S.Hold(n); });

    // S801. An inner query enumerated as dynamic.
    public static Cell<int> InnerQueryAsDynamicEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); dynamic first = lists[0]; int n = 0; foreach (object y in first) { n++; } return S.Hold(n); });

    // S802. A generic method enumerates a sequence of its type parameter.
    public static Cell<int> QueryEnumeratedInAGenericMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(CountOf(Numbers.Select(x => x + l.Sample()))));

    // S803. A generic method enumerates a value whose type is its type parameter.
    public static Cell<int> QueryEnumeratedAsATypeParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(CountAs(Numbers.Select(x => x + l.Sample()))));

    // S804. A generic method makes the query, so the type of the query has a type parameter.
    public static Cell<int> QueryFromAGenericMethodEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(OnlyWhen(Numbers, l).Count()));

    // S805. The enumerator of a query, moved.
    public static Cell<int> EnumeratorOfAQueryMoved() =>
        Cell.Loop<int>().WithoutCaptures(l => { using IEnumerator<int> e = Numbers.Select(x => x + l.Sample()).GetEnumerator(); return S.Hold(e.MoveNext() ? e.Current : 0); });

    // S806. An iterator whose type is the IEnumerable that is not generic. Its object is a sequence of objects.
    public static Cell<int> PlainIteratorAsASequenceOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = 0; foreach (object o in (IEnumerable<object>)Each(l)) { n++; } return S.Hold(n); static System.Collections.IEnumerable Each(Cell<int> c) { yield return c.Sample(); } });

    // S807. A query of texts enumerated as a sequence of objects.
    public static Cell<int> QueryOfTextsAsASequenceOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<object> all = Numbers.Select(x => (x + l.Sample()).ToString()); int n = 0; foreach (object o in all) { n++; } return S.Hold(n); });

    // S808. A chain of two queries with different types. The second one enumerates the first.
    public static Cell<int> ChainOfTwoTypesEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(Numbers.Where(x => x + l.Sample() > 0).Select(x => x.ToString()).Count()));

    // S809. A query expression over a query of a different type.
    public static Cell<int> QueryExpressionOverAQueryOfADifferentType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> source = Numbers.Where(x => x + l.Sample() > 0); return S.Hold((from x in source select x.ToString()).Count()); });

    // S810. The second source of a Zip has a different type from the result.
    public static Cell<int> SecondSourceOfAZipEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(Numbers.Select(x => x.ToString()).Zip(Numbers.Where(x => x + l.Sample() > 0), (s, n) => s + n).Count()));

    // S811. Repeat keeps the query as its element. Count enumerates only the Repeat.
    public static Cell<int> QueryAsTheElementOfARepeat() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(Enumerable.Repeat(q, 2).Count()); });

    // S812. Append keeps the query as one element more.
    public static Cell<int> QueryAppendedToAListOfQueries() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); List<IEnumerable<int>> lists = []; return S.Hold(lists.Append(q).ToList().Count); });

    // S813. A method returns a chain of two queries with different types. The walk of the method reads each query by itself.
    public static Cell<int> ChainOfTwoTypesFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(TextsOf(l).Count()));

    // S814. The same, where the method has the first query in a local.
    public static Cell<int> ChainOfTwoTypesFromAMethodWithALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(TextsThroughALocal(l).Count()));

    // S815. A chain through two methods.
    public static Cell<int> ChainOfThreeTypesFromTwoMethods() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(LengthsOf(l).Sum()));

    // S816. The chain from a method, which the block only keeps.
    public static Cell<int> ChainOfTwoTypesFromAMethodKept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(TextsOf(l)); return S.Hold(0); });

    // S817. SelectMany enumerates what its first function returns. The result has a different type.
    public static Cell<int> SelectManyWithAResultSelector() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.SelectMany(q => q, (q, y) => y.ToString()).Count()); });

    // S818. The same, where the function has a block.
    public static Cell<int> SelectManyWithABlockAndAResultSelector() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.SelectMany(q => { return q; }, (q, y) => y.ToString()).Count()); });

    // S819. Select keeps what its function returns.
    public static Cell<int> SelectKeepsEachQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.Select(q => q).ToList().Count); });

    // S820. The first function of SelectMany is a local function by its name.
    public static Cell<int> SelectManyByNameWithAResultSelector() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.SelectMany(Same, (q, y) => y.ToString()).Count()); static IEnumerable<int> Same(IEnumerable<int> q) => q; });

    // S821. The local function returns a different sequence, and not the query that it gets.
    public static Cell<int> SelectManyByNameThatGivesADifferentSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.SelectMany(Plain, (q, y) => y.ToString()).Count()); static IEnumerable<int> Plain(IEnumerable<int> q) => Numbers; });

    // S822. The first function of SelectMany is a platform method by its name, which gives back the query.
    public static Cell<int> SelectManyOfAPlatformMethodByName() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.SelectMany(Enumerable.AsEnumerable, (q, y) => y.ToString()).Count()); });

    // S823. ToList by its name enumerates each query that Select gives it.
    public static Cell<int> ToListByNameOverAListOfQueries() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.Select(Enumerable.ToList).ToList().Count); });

    // S824. Count by its name in a Sum.
    public static Cell<int> CountByNameInASumOfQueries() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); return S.Hold(lists.Sum(Enumerable.Count)); });

    // S825. A delegate in a local is a platform method.
    public static Cell<int> CountInALocalInASumOfQueries() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); Func<IEnumerable<int>, int> count = Enumerable.Count; return S.Hold(lists.Sum(count)); });

    // S826. Add by its name gets each query as an object, and keeps it.
    public static Cell<int> AddByNameKeepsEachQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); lists.ForEach(Kept.Add); return S.Hold(0); });

    // S827. SelectMany of an immutable array is not a method of Enumerable. The walk reads it at the call.
    public static Cell<int> SelectManyOfAnImmutableArrayOfQueries() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); return S.Hold(System.Collections.Immutable.ImmutableArray.Create(q).SelectMany(x => x, (x, y) => y.ToString()).Count()); });

    // S828. The first function of SelectMany is a platform cast by its name. It gets an object and gives back the query.
    public static Cell<int> SelectManyOfACastByName() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<int> q = Numbers.Select(x => x + l.Sample()); List<object> objects = [q]; return S.Hold(objects.SelectMany(System.Runtime.CompilerServices.Unsafe.As<IEnumerable<int>>, (o, y) => y.ToString()).Count()); });

    // S829. A method returns a chain. The function of the second query enumerates a query of its own, and then the second query enumerates the first.
    public static Cell<int> ChainFromAMethodWhoseFunctionEnumeratesAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(TextsOfCounts(l).Count()));

    // S830. A generic method enumerates a sequence of arrays of its type parameter.
    public static Cell<int> QueryOfArraysEnumeratedInAGenericMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(CountOfArrays(Numbers.Select(x => new[] { x + l.Sample() }))));

    // S831. An iterator whose type is the IEnumerator that is not generic. Its object is an enumerator of objects.
    public static Cell<int> PlainIteratorOfAnEnumeratorMoved() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerator<object> e = (IEnumerator<object>)Each(l); return S.Hold(e.MoveNext() ? 1 : 0); static System.Collections.IEnumerator Each(Cell<int> c) { yield return c.Sample(); } });

    // S832. Append by its name, with its receiver, gets each query as an element and keeps it.
    public static Cell<int> AppendByNameKeepsEachQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<IEnumerable<int>> lists = Numbers.Select(x => Numbers.Where(y => y > l.Sample())).ToList(); List<IEnumerable<int>> other = []; return S.Hold(lists.Select(other.Append).ToList().Count); });

    // S833. A pair of two objects of one type. The key is plain and the value is pending. The pair has a state for each part.
    public static Cell<int> PlainKeyOfAPairOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S834. The value of that pair.
    public static Cell<int> PendingValueOfAPairOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(p.Value.ToString()); return S.Hold(0); });

    // S835. The key is pending and the value is plain.
    public static Cell<int> PendingKeyOfAPairOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(l.Map(v => v)), new Titled(Plain)); Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S836. The plain value of that pair.
    public static Cell<int> PlainValueOfAPairOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(l.Map(v => v)), new Titled(Plain)); Kept.Add(p.Value.ToString()); return S.Hold(0); });

    // S837. Create of KeyValuePair gives each part its argument.
    public static Cell<int> PlainKeyOfAPairFromCreate() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = KeyValuePair.Create(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S838. The value of the pair from Create.
    public static Cell<int> PendingValueOfAPairFromCreate() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = KeyValuePair.Create(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(p.Value.ToString()); return S.Hold(0); });

    // S839. Named arguments in the other order.
    public static Cell<int> PlainKeyOfAPairWithNamedArguments() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(value: new Titled(l.Map(v => v)), key: new Titled(Plain)); Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S840. The value, with named arguments in the other order.
    public static Cell<int> PendingValueOfAPairWithNamedArguments() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(value: new Titled(l.Map(v => v)), key: new Titled(Plain)); Kept.Add(p.Value.ToString()); return S.Hold(0); });

    // S841. The plain item of a Tuple from Create.
    public static Cell<int> PlainItemOfATupleOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, Titled> t = Tuple.Create(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(t.Item1.ToString()); return S.Hold(0); });

    // S842. The pending item of that Tuple.
    public static Cell<int> PendingItemOfATupleOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, Titled> t = Tuple.Create(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(t.Item2.ToString()); return S.Hold(0); });

    // S843. The plain item of a Tuple from its constructor.
    public static Cell<int> PlainItemOfATupleFromItsConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, Titled> t = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(t.Item1.ToString()); return S.Hold(0); });

    // S844. The plain key of a DictionaryEntry.
    public static Cell<int> PlainKeyOfADictionaryEntry() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.DictionaryEntry e = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(e.Key.ToString()!); return S.Hold(0); });

    // S845. The pending value of a DictionaryEntry.
    public static Cell<int> PendingValueOfADictionaryEntry() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.DictionaryEntry e = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(e.Value!.ToString()!); return S.Hold(0); });

    // S846. A property pattern reads the plain key.
    public static Cell<int> PlainKeyOfAPairByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); if (p is { Key: var k }) { Kept.Add(k.ToString()); } return S.Hold(0); });

    // S847. A property pattern reads the pending value.
    public static Cell<int> PendingValueOfAPairByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); if (p is { Value: var v }) { Kept.Add(v.ToString()); } return S.Hold(0); });

    // S848. Deconstruct of the platform gives each target the state of its part.
    public static Cell<int> PlainKeyOfAPairFromADeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); (Titled k, Titled _) = p; Kept.Add(k.ToString()); return S.Hold(0); });

    // S849. The pending value from the Deconstruct.
    public static Cell<int> PendingValueOfAPairFromADeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); (Titled _, Titled v) = p; Kept.Add(v.ToString()); return S.Hold(0); });

    // S850. A deconstruction with var.
    public static Cell<int> PlainKeyOfAPairFromAVarDeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); var (k, _) = p; Kept.Add(k.ToString()); return S.Hold(0); });

    // S851. The pending value from a deconstruction with var.
    public static Cell<int> PendingValueOfAPairFromAVarDeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); var (_, v) = p; Kept.Add(v.ToString()); return S.Hold(0); });

    // S852. Deconstruct of a Tuple is an extension method of the platform.
    public static Cell<int> PlainItemOfATupleFromADeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, Titled> t = Tuple.Create(new Titled(Plain), new Titled(l.Map(v => v))); (Titled a, Titled _) = t; Kept.Add(a.ToString()); return S.Hold(0); });

    // S853. The pending item from the Deconstruct of a Tuple.
    public static Cell<int> PendingItemOfATupleFromADeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tuple<Titled, Titled> t = Tuple.Create(new Titled(Plain), new Titled(l.Map(v => v))); (Titled _, Titled b) = t; Kept.Add(b.ToString()); return S.Hold(0); });

    // S854. A var pattern with parts calls Deconstruct of the platform.
    public static Cell<int> PlainKeyOfAPairByAVarPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); if (p is var (k, _)) { Kept.Add(k.ToString()); } return S.Hold(0); });

    // S855. The pending value by a positional pattern.
    public static Cell<int> PendingValueOfAPairByAPositionalPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); if (p is (_, var v)) { Kept.Add(v.ToString()); } return S.Hold(0); });

    // S856. A foreach over a list of pairs that deconstructs each one.
    public static Cell<int> PlainKeyOfEachPairOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<KeyValuePair<Titled, Titled>> pairs = [new(new Titled(Plain), new Titled(l.Map(v => v)))]; foreach ((Titled k, Titled _) in pairs) { Kept.Add(k.ToString()); } return S.Hold(0); });

    // S857. The pending value of each pair of the list.
    public static Cell<int> PendingValueOfEachPairOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<KeyValuePair<Titled, Titled>> pairs = [new(new Titled(Plain), new Titled(l.Map(v => v)))]; foreach ((Titled _, Titled v) in pairs) { Kept.Add(v.ToString()); } return S.Hold(0); });

    // S858. A pair in a list, read by its index.
    public static Cell<int> PlainKeyOfAPairInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); List<KeyValuePair<Titled, Titled>> pairs = [p]; Kept.Add(pairs[0].Key.ToString()); return S.Hold(0); });

    // S859. The pending value of the pair in the list.
    public static Cell<int> PendingValueOfAPairInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); List<KeyValuePair<Titled, Titled>> pairs = [p]; Kept.Add(pairs[0].Value.ToString()); return S.Hold(0); });

    // S860. A value that is one of two pairs. The first one is from a dictionary, and the evaluator did not see it made. Its key is pending.
    public static Cell<int> KeyOfAPairThatCanBeFromADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new() { { new Titled(l.Map(v => v)), new Titled(Plain) } }; KeyValuePair<Titled, Titled> made = new(new Titled(Plain), new Titled(l.Map(v => v))); KeyValuePair<Titled, Titled> p = Numbers.Length > 0 ? d.First() : made; Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S861. The same, with the pair that the block made first.
    public static Cell<int> KeyOfAPairThatCanBeFromADictionaryAfter() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new() { { new Titled(l.Map(v => v)), new Titled(Plain) } }; KeyValuePair<Titled, Titled> made = new(new Titled(Plain), new Titled(l.Map(v => v))); KeyValuePair<Titled, Titled> p = Numbers.Length > 5 ? made : d.First(); Kept.Add(p.Key.ToString()); return S.Hold(0); });

    // S862. A list has a pair that the block made and a pair from a dictionary.
    public static Cell<int> KeyOfAPairFromADictionaryInAListOfTwo() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new() { { new Titled(l.Map(v => v)), new Titled(Plain) } }; KeyValuePair<Titled, Titled> made = new(new Titled(Plain), new Titled(l.Map(v => v))); List<KeyValuePair<Titled, Titled>> all = [made, d.First()]; Kept.Add(all[1].Key.ToString()); return S.Hold(0); });

    // S863. The pair from the dictionary has cells, and thus no object with members.
    public static Cell<int> KeyOfAPairOfCellsThatCanBeFromADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Cell<int>, Cell<int>> d = new() { { l.Map(v => v), Plain } }; KeyValuePair<Cell<int>, Cell<int>> made = new(Plain, l.Map(v => v)); KeyValuePair<Cell<int>, Cell<int>> p = Numbers.Length > 0 ? d.First() : made; return S.Hold(p.Key.Sample()); });

    // S864. The code sets the key of a DictionaryEntry after the constructor.
    public static Cell<int> KeyOfADictionaryEntrySetAfter() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.DictionaryEntry e = new(new Titled(Plain), new Titled(Plain)); e.Key = new Titled(l.Map(v => v)); Kept.Add(e.Key.ToString()!); return S.Hold(0); });

    // S865. The same, for an entry that has a state for each part.
    public static Cell<int> KeyOfADictionaryEntryWithPartsSetAfter() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.DictionaryEntry e = new(new Titled(Plain), new Titled(l.Map(v => v))); e.Key = new Titled(l.Map(v => v)); Kept.Add(e.Key.ToString()!); return S.Hold(0); });

    // S866. A method gets the pair and reads its plain key.
    public static Cell<int> PlainKeyOfAPairInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(KeyTextOf(p)); return S.Hold(0); });

    // S867. A method gets the pair and reads its pending value.
    public static Cell<int> PendingValueOfAPairOfOneTypeInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(ValueTextOfBoth(p)); return S.Hold(0); });

    // S868. The pair goes through a task of the platform, and keeps the state of each part.
    public static Cell<int> PlainKeyOfAPairThroughATask() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(Task.FromResult(p).Result.Key.ToString()); return S.Hold(0); });

    // S869. The pending value of the pair through the task.
    public static Cell<int> PendingValueOfAPairThroughATask() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(Task.FromResult(p).Result.Value.ToString()); return S.Hold(0); });

    // S870. A positional pattern with a variable for the key.
    public static Cell<int> PlainKeyOfAPairByAPositionalPatternWithAVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); if (p is (var k, _)) { Kept.Add(k.ToString()); } return S.Hold(0); });

    // S871. A var pattern with parts, for the pending value.
    public static Cell<int> PendingValueOfAPairByAVarPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); if (p is var (_, v)) { Kept.Add(v.ToString()); } return S.Hold(0); });

    // S872. Two pairs in a list, each with a state for its parts. The key of each is plain.
    public static Cell<int> PlainKeyOfTwoPairsInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<KeyValuePair<Titled, Titled>> pairs = [new(new Titled(Plain), new Titled(l.Map(v => v))), new(new Titled(Plain), new Titled(l.Map(v => v)))]; Kept.Add(pairs[0].Key.ToString()); return S.Hold(0); });

    // S873. A list from a dictionary gets one pair more, whose key is null. The key of a pair from the dictionary is pending.
    public static Cell<int> KeyOfAPairFromADictionaryInAListWithOneMore() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new() { { new Titled(l.Map(v => v)), new Titled(Plain) } }; List<KeyValuePair<Titled, Titled>> all = d.ToList(); all.Add(new KeyValuePair<Titled, Titled>(null!, new Titled(l.Map(v => v)))); Kept.Add(all[0].Key.ToString()); return S.Hold(0); });

    // S874. A Deconstruct of this compilation gives each parameter the property of the other name. The pair types of the platform do not.
    public static Cell<int> DeconstructThatCrossesItsParts() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Cell<int> a, Cell<int> _) = new CrossedParts(Plain, l.Map(v => v)); return S.Hold(a.Sample()); });

    // S875. A pair from a dictionary has no state for its parts. Deconstruct gives each target the state of all that the pair contains.
    public static Cell<int> KeyOfAPairFromADictionaryByADeconstruct() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new() { { new Titled(l.Map(v => v)), new Titled(Plain) } }; (Titled k, Titled _) = d.First(); Kept.Add(k.ToString()); return S.Hold(0); });

    // S876. A value that is one of two objects. The second type overrides the property, and the state knows the property of the base type also. Only a part of a pair is read by its own state.
    public static Cell<int> OverriddenPropertyOfAValueThatIsOneOfTwo() =>
        Cell.Loop<int>().WithoutCaptures(l => { Leveled x = Numbers.Length > 0 ? new LeveledLate(l.Map(v => v)) : new Leveled(Plain); return S.Hold(x.Level.Sample()); });

    // S877. The same read by a property pattern.
    public static Cell<int> OverriddenPropertyOfOneOfTwoByAPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { Leveled x = Numbers.Length > 0 ? new LeveledLate(l.Map(v => v)) : new Leveled(Plain); return S.Hold(x is { Level: var c } ? c.Sample() : 0); });

    // S878. A method deconstructs the pair that it gets, and reads the pending value.
    public static Cell<int> PendingValueOfAPairDeconstructedInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(SecondTextOf(p)); return S.Hold(0); });

    // S879. The method reads the plain key.
    public static Cell<int> PlainKeyOfAPairDeconstructedInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Titled> p = new(new Titled(Plain), new Titled(l.Map(v => v))); Kept.Add(FirstTextOf(p)); return S.Hold(0); });

    // S880. A list has a pair and a pending cell. No element is the object that the pair has as its key, and that object is plain.
    public static Cell<int> PlainObjectInAPairInAListWithACell() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Titled, Cell<int>> made = new(new Titled(Plain), l.Map(v => v)); List<object> all = [made, l.Map(v => v)]; foreach (object o in all) { if (o is Titled t) { Kept.Add(t.ToString()); } } return S.Hold(0); });

    // S881. A list from a literal that no code changes keeps its positions. The first element is plain.
    public static Cell<int> PlainElementOfAMixedListByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; int x = cs[0].Sample(); return S.Snapshot(cs[1], (a, b) => a + b + x).Hold(0); });

    // S882. The second element of that list is pending.
    public static Cell<int> PendingElementOfAMixedListByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; return S.Hold(cs[1].Sample()); });

    // S883. An Insert changes the positions.
    public static Cell<int> ElementOfAListAfterAnInsert() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, Plain]; cs.Insert(0, l.Map(v => v)); return S.Hold(cs[0].Sample()); });

    // S884. A Reverse changes the positions and adds nothing.
    public static Cell<int> ElementOfAMixedListAfterAReverse() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; cs.Reverse(); return S.Hold(cs[0].Sample()); });

    // S885. An array from a collection expression.
    public static Cell<int> PlainElementOfAMixedArray() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain, l.Map(v => v)]; return S.Hold(cs[0].Sample()); });

    // S886. An assignment to an element.
    public static Cell<int> ElementOfAnArrayAfterAnAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain, Plain]; cs[0] = l.Map(v => v); return S.Hold(cs[0].Sample()); });

    // S887. First of a list of objects is the first element of the literal.
    public static Cell<int> PlainFirstOfAMixedListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Plainly(), new Titled(l.Map(v => v))]; Kept.Add(all.First().ToString()!); return S.Hold(0); });

    // S888. Last is the last element.
    public static Cell<int> PendingLastOfAMixedListOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> all = [new Plainly(), new Titled(l.Map(v => v))]; Kept.Add(all.Last().ToString()!); return S.Hold(0); });

    // S889. A helper gets the list and changes it.
    public static Cell<int> ElementOfAListGivenToAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, Plain]; cs.AddOne(l.Map(v => v)); return S.Hold(cs[2].Sample()); });

    // S890. An array from an initializer with braces.
    public static Cell<int> PlainElementOfAnArrayFromAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = { Plain, l.Map(v => v) }; return S.Hold(cs[0].Sample()); });

    // S891. The pending element of that array.
    public static Cell<int> PendingElementOfAnArrayFromAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = { Plain, l.Map(v => v) }; return S.Hold(cs[1].Sample()); });

    // S892. An array from new[] with an initializer.
    public static Cell<int> PlainElementOfAnImplicitArray() =>
        Cell.Loop<int>().WithoutCaptures(l => { var cs = new[] { Plain, l.Map(v => v) }; return S.Hold(cs[0].Sample()); });

    // S893. An array from new with its type and an initializer.
    public static Cell<int> PlainElementOfAnArrayCreation() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = new Cell<int>[] { Plain, l.Map(v => v) }; return S.Hold(cs[0].Sample()); });

    // S894. A List from its constructor with an initializer.
    public static Cell<int> PlainElementOfAListFromAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { var cs = new List<Cell<int>> { Plain, l.Map(v => v) }; return S.Hold(cs[0].Sample()); });

    // S895. The pending element of that List.
    public static Cell<int> PendingElementOfAListFromAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { var cs = new List<Cell<int>> { Plain, l.Map(v => v) }; return S.Hold(cs[1].Sample()); });

    // S896. A List from new() with an initializer.
    public static Cell<int> PlainElementOfAListFromAnImpliedConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = new() { Plain, l.Map(v => v) }; return S.Hold(cs[0].Sample()); });

    // S897. The constructor gets a collection, so the initializer does not have the first position.
    public static Cell<int> ElementOfAListFromACollectionAndAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> start = [l.Map(v => v)]; List<Cell<int>> cs = new(start) { Plain }; return S.Hold(cs[0].Sample()); });

    // S898. An element with braces in the initializer of a List.
    public static Cell<int> ElementOfAListFromElementsWithBraces() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = new() { { l.Map(v => v) }, { Plain } }; return S.Hold(cs[0].Sample()); });

    // S899. A ConcurrentBag gives its elements in a different order from the code.
    public static Cell<int> FirstOfAConcurrentBag() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.Concurrent.ConcurrentBag<Cell<int>> cs = [Plain, l.Map(v => v)]; return S.Hold(cs.First().Sample()); });

    // S900. The same, from the constructor with an initializer.
    public static Cell<int> FirstOfAConcurrentBagFromAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { var cs = new System.Collections.Concurrent.ConcurrentBag<Cell<int>> { Plain, l.Map(v => v) }; return S.Hold(cs.First().Sample()); });

    // S901. A ref argument can change the element.
    public static Cell<int> ElementOfAnArraySetThroughARefArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain, Plain]; Put(ref cs[0], l.Map(v => v)); return S.Hold(cs[0].Sample()); static void Put(ref Cell<int> slot, Cell<int> c) => slot = c; });

    // S902. A deconstruction can change the elements.
    public static Cell<int> ElementsOfAnArraySetByADeconstruction() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain, Plain]; (cs[0], cs[1]) = (l.Map(v => v), Plain); return S.Hold(cs[0].Sample()); });

    // S903. A ref local is a second name for the element.
    public static Cell<int> ElementOfAMixedArraySetThroughARefLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain, l.Map(v => v)]; ref Cell<int> first = ref cs[0]; first = l.Map(v => v); return S.Hold(cs[0].Sample()); });

    // S904. An assignment to an element in parentheses.
    public static Cell<int> ElementOfAMixedArraySetInParentheses() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cs = [Plain, l.Map(v => v)]; (cs[0]) = l.Map(v => v); return S.Hold(cs[0].Sample()); });

    // S905. An increment puts the result of the operator in the element.
    public static Cell<int> ElementOfAnArrayAfterAnIncrement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stepped[] steps = [new Stepped(Plain, l.Map(v => v)), new Stepped(l.Map(v => v), Plain)]; steps[0]++; return S.Hold(steps[0].Now.Sample()); });

    // S906. A second name for the list can change it.
    public static Cell<int> ElementOfAMixedListSetThroughASecondName() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; List<Cell<int>> other = cs; other[0] = l.Map(v => v); return S.Hold(cs[0].Sample()); });

    // S907. A lambda reads an element by its index.
    public static Cell<int> PlainElementOfAMixedListInALambda() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; Func<Cell<int>> get = () => cs[0]; return S.Hold(get().Sample()); });

    // S908. A local function reads an element by its index.
    public static Cell<int> PlainElementOfAMixedListInALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; return S.Hold(Read()); int Read() => cs[0].Sample(); });

    // S909. A list of functions. The call runs only the function of the position.
    public static Cell<int> PlainFunctionOfAMixedListOfFunctions() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Func<int>> fs = [() => 1, () => l.Sample()]; return S.Hold(fs[0]()); });

    // S910. The function of the second position reads.
    public static Cell<int> ReadingFunctionOfAMixedListOfFunctions() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Func<int>> fs = [() => 1, () => l.Sample()]; return S.Hold(fs[1]()); });

    // S911. A method of this compilation with the name First changes the list and gives a different element.
    public static Cell<int> FirstOfThisCompilationChangesTheList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Marked> ms = [new Marked(Plain), new Marked(l.Map(v => v))]; return S.Hold(ms.First().Share.Sample()); });

    // S912. Count, a foreach, and a constant by its name are reads.
    public static Cell<int> PlainElementOfAMixedListWithOtherReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; const int Zero = 0; int n = cs.Count; foreach (Cell<int> c in cs) { Kept.Add(c); } return S.Hold(cs[Zero].Sample() + n); });

    // S913. A list in a list of objects is the element of its position. ToString of a list reads nothing.
    public static Cell<int> ListInAListOfObjectsByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> outer = [new List<Titled> { new(l.Map(v => v)) }]; object o = outer[0]; Kept.Add(o.ToString()!); return S.Hold(0); });

    // S914. A collection expression for IEnumerable keeps its positions.
    public static Cell<int> PlainFirstOfAMixedSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<Cell<int>> cs = [Plain, l.Map(v => v)]; return S.Hold(cs.First().Sample()); });

    // S915. The last element of that sequence.
    public static Cell<int> PendingLastOfAMixedSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<Cell<int>> cs = [Plain, l.Map(v => v)]; return S.Hold(cs.Last().Sample()); });

    // S916. A spread has a number of elements that the code does not say.
    public static Cell<int> ElementOfAListWithASpread() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> others = [l.Map(v => v)]; List<Cell<int>> cs = [.. others, Plain]; return S.Hold(cs[0].Sample()); });

    // S917. An Add through an element of a list of lists. The list that is the element then contains the value.
    public static Cell<int> AddThroughAnElementOfAListOfLists() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> cs = [new(), new()]; cs[0].Add(l.Map(v => v)); return S.Hold(cs[0][0].Sample()); });

    // S918. A helper adds to the list that an element is.
    public static Cell<int> HelperAddsThroughAnElementOfAListOfLists() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> cs = [new(), new()]; Fill(cs[0], l.Map(v => v)); return S.Hold(cs[0][0].Sample()); static void Fill(List<Cell<int>> xs, Cell<int> c) => xs.Add(c); });

    // S919. An Add through First of a list of lists.
    public static Cell<int> AddThroughTheFirstOfAListOfLists() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> cs = [new(), [l.Map(v => v)]]; cs.First().Add(l.Map(v => v)); return S.Hold(cs[0][0].Sample()); });

    // S920. An assignment to an element of an element.
    public static Cell<int> ElementOfAnElementAfterAnAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[][] cs = [[Plain], [l.Map(v => v)]]; cs[0][0] = l.Map(v => v); return S.Hold(cs[0][0].Sample()); });

    // S921. The elements of a list of lists before a change. The first list has only a plain cell.
    public static Cell<int> PlainElementOfAnElementOfAListOfLists() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> cs = [[Plain], [l.Map(v => v)]]; return S.Hold(cs[0][0].Sample()); });

    // S922. The constructor of the List, written with its type, gets a collection.
    public static Cell<int> ElementOfAListFromItsTypeACollectionAndAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> start = [l.Map(v => v)]; var cs = new List<Cell<int>>(start) { Plain }; return S.Hold(cs[0].Sample()); });

    // S923. A method of this compilation with the name Count changes the list.
    public static Cell<int> CountOfThisCompilationChangesTheList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Marked> ms = [new Marked(Plain), new Marked(l.Map(v => v))]; int n = ms.Count(); return S.Hold(ms[0].Share.Sample() + n); });

    // S924. The code sets a property of an element. The collection then contains an object with that state.
    public static Cell<int> PropertySetThroughAnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new SettableSlot { Share = Plain }, new SettableSlot { Share = Plain }]; slots[0].Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S925. The same through First.
    public static Cell<int> PropertySetThroughTheFirstElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new SettableSlot { Share = Plain }, new SettableSlot { Share = Plain }]; slots.First().Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S926. The elements before a change: the first has a plain cell and the second a pending one.
    public static Cell<int> PlainPropertyOfAnElementOfAMixedList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new SettableSlot { Share = Plain }, new SettableSlot { Share = l.Map(v => v) }]; return S.Hold(slots[0].Share!.Sample()); });

    // S927. An array with two dimensions. The first index gives a row.
    public static Cell<int> PlainRowOfAGrid() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[,] grid = { { Plain, Plain }, { Plain, l.Map(v => v) } }; return S.Hold(grid[0, 1].Sample()); });

    // S928. The row that has the pending cell.
    public static Cell<int> PendingRowOfAGrid() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[,] grid = { { Plain, Plain }, { Plain, l.Map(v => v) } }; return S.Hold(grid[1, 1].Sample()); });

    // S929. An object in an element with braces, read by its index.
    public static Cell<int> ObjectOfAListFromElementsWithBraces() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> ts = new() { { new Titled(l.Map(v => v)) }, { new Titled(Plain) } }; Kept.Add(ts[0].ToString()); return S.Hold(0); });

    // S930. An element on the right of an assignment is a read.
    public static Cell<int> PlainElementOfAMixedListAssignedToALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cs = [Plain, l.Map(v => v)]; Cell<int> first; first = cs[0]; return S.Hold(first.Sample()); });

    // S931. An operator that is not an increment does not change the element.
    public static Cell<int> PlainElementOfAMixedListThatCanBeNull() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>?> cs = [Plain, l.Map(v => v)]; return S.Hold(cs[0]!.Sample()); });

    // S932. An increment before the element.
    public static Cell<int> ElementOfAnArrayAfterAnIncrementBeforeIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stepped[] steps = [new Stepped(Plain, l.Map(v => v)), new Stepped(l.Map(v => v), Plain)]; ++steps[0]; return S.Hold(steps[0].Now.Sample()); });

    // S933. A decrement after the element.
    public static Cell<int> ElementOfAnArrayAfterADecrement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stepped[] steps = [new Stepped(Plain, l.Map(v => v)), new Stepped(l.Map(v => v), Plain)]; steps[0]--; return S.Hold(steps[0].Now.Sample()); });

    // S934. A decrement before the element.
    public static Cell<int> ElementOfAnArrayAfterADecrementBeforeIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { Stepped[] steps = [new Stepped(Plain, l.Map(v => v)), new Stepped(l.Map(v => v), Plain)]; --steps[0]; return S.Hold(steps[0].Now.Sample()); });

    // S935. An initializer with an index gives the dictionary its key also.
    public static Cell<int> KeyOfADictionaryFromAnIndexInTheInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new() { [new Titled(l.Map(v => v))] = new Titled(Plain) }; Kept.Add(d.First().Key.ToString()); return S.Hold(0); });

    // S936. An assignment through the indexer gives the dictionary its key also.
    public static Cell<int> KeyOfADictionaryFromAnAssignmentByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new(); d[new Titled(l.Map(v => v))] = new Titled(Plain); Kept.Add(d.First().Key.ToString()); return S.Hold(0); });

    // S937. The key through the Keys of the dictionary.
    public static Cell<int> KeyThroughKeysFromAnAssignmentByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new(); d[new Titled(l.Map(v => v))] = new Titled(Plain); Kept.Add(d.Keys.First().ToString()); return S.Hold(0); });

    // S938. The key is a cell.
    public static Cell<int> CellKeyFromAnAssignmentByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Cell<int>, int> d = new(); d[l.Map(v => v)] = 1; return S.Hold(d.Keys.First().Sample()); });

    // S939. The key of a Hashtable.
    public static Cell<int> KeyOfAHashtableFromAnAssignmentByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { System.Collections.Hashtable h = new(); h[new Titled(l.Map(v => v))] = new Titled(Plain); foreach (System.Collections.DictionaryEntry entry in h) { Kept.Add(entry.Key.ToString()!); } return S.Hold(0); });

    // S940. A plain key and a plain value.
    public static Cell<int> PlainKeyFromAnAssignmentByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Titled, Titled> d = new(); d[new Titled(Plain)] = new Titled(Plain); Kept.Add(d.First().Key.ToString()); return S.Hold(0); });

    // S941. An indexer of this compilation reads its index and keeps the value only. The walk reads its setter.
    public static Cell<int> IndexOfAnIndexerOfThisCompilation() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ledger ledger = new(); ledger[new Titled(Plain)] = Plain; return S.Hold(ledger.Last.Sample()); });

    // S942. The setter of that indexer reads a pending index.
    public static Cell<int> PendingIndexOfAnIndexerOfThisCompilation() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ledger ledger = new(); ledger[new Titled(l.Map(v => v))] = Plain; return S.Hold(ledger.Last.Sample()); });

    // S943. A var pattern with parts calls a Deconstruct of this compilation, which reads.
    public static Cell<int> VarPatternCallsADeconstructThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); if (t is var (a, b)) { Kept.Add(a + b); } return S.Hold(0); });

    // S944. The same pattern in a switch expression.
    public static Cell<int> VarPatternInASwitchCallsADeconstructThatReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { Tally t = new(l.Map(v => v)); int n = t switch { var (a, b) => a + b }; return S.Hold(n); });

    // S945. The Deconstruct gives two cells. The variable for the plain one has the state of its out parameter.
    public static Cell<int> PlainPartOfAVarPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyPair p = new(Plain, l.Map(v => v)); return S.Hold(p is var (a, b) ? a.Sample() : 0); });

    // S946. The variable for the pending cell.
    public static Cell<int> PendingPartOfAVarPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyPair p = new(Plain, l.Map(v => v)); return S.Hold(p is var (a, b) ? b.Sample() : 0); });

    // S947. A part with parts of its own calls the Deconstruct of that part.
    public static Cell<int> PlainPartOfAPartOfAVarPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyNest nest = new(new TallyPair(Plain, l.Map(v => v))); return S.Hold(nest is var ((a, b), n) ? a.Sample() + n : 0); });

    // S948. The pending cell of the inner part.
    public static Cell<int> PendingPartOfAPartOfAVarPattern() =>
        Cell.Loop<int>().WithoutCaptures(l => { TallyNest nest = new(new TallyPair(Plain, l.Map(v => v))); return S.Hold(nest is var ((a, b), n) ? b.Sample() + n : 0); });

    // S949. Find gives one of the lists of the list. A change to it is a change to what the list contains.
    public static Cell<int> ChangeThroughFind() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; rows.Find(r => true)!.Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S950. Last of a list that does not keep its positions.
    public static Cell<int> ChangeThroughLastOfAGrownList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new()]; rows.Add(new()); rows.Last().Add(l.Map(v => v)); return S.Hold(rows[1][0].Sample()); });

    // S951. ElementAt.
    public static Cell<int> ChangeThroughElementAt() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new()]; rows.Add(new()); rows.ElementAt(0).Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S952. The variable of a foreach has an element of the collection.
    public static Cell<int> ChangeThroughAForeachVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; foreach (List<Cell<int>> row in rows) { row.Add(l.Map(v => v)); } return S.Hold(rows[0][0].Sample()); });

    // S953. A property set through Last.
    public static Cell<int> PropertySetThroughLast() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new SettableSlot { Share = Plain }]; slots.Add(new SettableSlot { Share = Plain }); slots.Last().Share = l.Map(v => v); return S.Hold(slots[1].Share!.Sample()); });

    // S954. A property set through the variable of a foreach.
    public static Cell<int> PropertySetThroughAForeachVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new SettableSlot { Share = Plain }]; slots.Add(new SettableSlot { Share = Plain }); foreach (SettableSlot slot in slots) { slot.Share = l.Map(v => v); } return S.Hold(slots[0].Share!.Sample()); });

    // S955. A local that the code gives an element is a second name for that element.
    public static Cell<int> ChangeThroughALocalOfAnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; List<Cell<int>> first = rows[0]; first.Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S956. The local gets the element in an assignment.
    public static Cell<int> ChangeThroughALocalThatGetsAnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; List<Cell<int>> first; first = rows[0]; first.Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S957. The element is in parentheses and in a cast.
    public static Cell<int> ChangeThroughAnElementInACast() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; ((List<Cell<int>>)rows[0]).Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S958. TryGetValue gives an out variable one of the values of the dictionary.
    public static Cell<int> ChangeThroughAnOutVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, List<Cell<int>>> d = new() { ["a"] = new() }; if (d.TryGetValue("a", out List<Cell<int>>? found)) { found.Add(l.Map(v => v)); } return S.Hold(d["a"][0].Sample()); });

    // S959. ForEach gives its function each element.
    public static Cell<int> ChangeThroughALambdaParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; rows.ForEach(r => r.Add(l.Map(v => v))); return S.Hold(rows[0][0].Sample()); });

    // S960. The function of a query gets each element when something enumerates the query.
    public static Cell<int> ChangeThroughAParameterOfAQueryFunction() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; int n = rows.Select(r => { r.Add(l.Map(v => v)); return 1; }).Sum(); return S.Hold(rows[0][0].Sample() + n); });

    // S961. Values of a dictionary gives the values that the dictionary has.
    public static Cell<int> ChangeThroughValuesOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, List<Cell<int>>> d = new() { ["a"] = new() }; d.Values.First().Add(l.Map(v => v)); return S.Hold(d["a"][0].Sample()); });

    // S962. A helper adds to the list in the variable of a foreach.
    public static Cell<int> HelperOnAForeachVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; foreach (List<Cell<int>> row in rows) { Fill(row, l.Map(v => v)); } return S.Hold(rows[0][0].Sample()); static void Fill(List<Cell<int>> xs, Cell<int> c) => xs.Add(c); });

    // S963. The local gets a new list after the element. The change is to the new list.
    public static Cell<int> ElementInALocalThatGetsANewList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; List<Cell<int>> first = rows[0]; first = new(); first.Add(l.Map(v => v)); return S.Hold(rows[0].Count == 0 ? Plain.Sample() : rows[0][0].Sample()); });

    // S964. ToList makes a collection. A row more in the copy is not in the list.
    public static Cell<int> CopyOfAListGetsARow() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; rows.ToList().Add(new List<Cell<int>> { l.Map(v => v) }); return S.Hold(rows.Count == 2 ? Plain.Sample() : rows[2][0].Sample()); });

    // S965. The constructor of XElement gets its content as an object, and enumerates a query in it.
    public static Cell<int> QueryInTheContentOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S966. The constructor with more than one object of content.
    public static Cell<int> QueryInTheContentsOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", 1, Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S967. A query in an array in the content.
    public static Cell<int> QueryInAnArrayInTheContentOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", new object[] { Numbers.Select(x => x + l.Sample()) })); return S.Hold(0); });

    // S968. A query in the content of an element in the content.
    public static Cell<int> QueryInAnXElementInAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", new XElement("b", Numbers.Select(x => x + l.Sample())))); return S.Hold(0); });

    // S969. The constructor of XDocument.
    public static Cell<int> QueryInTheContentOfAnXDocument() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XDocument(new XElement("r"), Numbers.Select(x => x + l.Sample()).Select(n => new XComment(n.ToString())))); return S.Hold(0); });

    // S970. Add.
    public static Cell<int> QueryGivenToAddOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.Add(Numbers.Select(x => x + l.Sample())); return S.Hold(0); });

    // S971. Add on the root of a document.
    public static Cell<int> QueryGivenToAddOfTheRootOfAnXDocument() =>
        Cell.Loop<int>().WithoutCaptures(l => { XDocument doc = new(new XElement("r")); doc.Root!.Add(Numbers.Select(x => x + l.Sample())); return S.Hold(0); });

    // S972. AddFirst.
    public static Cell<int> QueryGivenToAddFirstOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.AddFirst(Numbers.Select(x => x + l.Sample())); return S.Hold(0); });

    // S973. ReplaceNodes.
    public static Cell<int> QueryGivenToReplaceNodesOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.ReplaceNodes(Numbers.Select(x => x + l.Sample())); return S.Hold(0); });

    // S974. ReplaceAll.
    public static Cell<int> QueryGivenToReplaceAllOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.ReplaceAll(Numbers.Select(x => x + l.Sample())); return S.Hold(0); });

    // S975. ReplaceAttributes.
    public static Cell<int> QueryGivenToReplaceAttributesOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.ReplaceAttributes(Numbers.Select(x => x + l.Sample()).Select(n => new XAttribute("b" + n, n))); return S.Hold(0); });

    // S976. AddAfterSelf.
    public static Cell<int> QueryGivenToAddAfterSelf() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement root = new("r", new XElement("a")); root.Element("a")!.AddAfterSelf(Numbers.Select(x => x + l.Sample()).Select(n => new XElement("b", n))); return S.Hold(0); });

    // S977. AddBeforeSelf.
    public static Cell<int> QueryGivenToAddBeforeSelf() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement root = new("r", new XElement("a")); root.Element("a")!.AddBeforeSelf(Numbers.Select(x => x + l.Sample()).Select(n => new XElement("b", n))); return S.Hold(0); });

    // S978. ReplaceWith.
    public static Cell<int> QueryGivenToReplaceWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement root = new("r", new XElement("a")); root.Element("a")!.ReplaceWith(Numbers.Select(x => x + l.Sample()).Select(n => new XElement("b", n))); return S.Hold(0); });

    // S979. An object in the content. The element makes text from it with ToString.
    public static Cell<int> ObjectInTheContentOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", new Titled(l.Map(v => v)))); return S.Hold(0); });

    // S980. A list of objects in the content.
    public static Cell<int> ObjectsInTheContentOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", new List<Titled> { new Titled(l.Map(v => v)) })); return S.Hold(0); });

    // S981. An object given to Add.
    public static Cell<int> ObjectGivenToAddOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.Add(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S982. An object as the value of an attribute.
    public static Cell<int> ObjectAsTheValueOfAnXAttribute() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XAttribute("a", new Titled(l.Map(v => v)))); return S.Hold(0); });

    // S983. SetValue.
    public static Cell<int> ObjectGivenToSetValueOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.SetValue(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S984. SetElementValue.
    public static Cell<int> ObjectGivenToSetElementValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.SetElementValue("b", new Titled(l.Map(v => v))); return S.Hold(0); });

    // S985. SetAttributeValue.
    public static Cell<int> ObjectGivenToSetAttributeValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.SetAttributeValue("b", new Titled(l.Map(v => v))); return S.Hold(0); });

    // S986. An attribute makes text from its value, and does not enumerate it.
    public static Cell<int> QueryAsTheValueOfAnXAttribute() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XAttribute("a", Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S987. SetElementValue makes text from the value, and does not enumerate it.
    public static Cell<int> QueryGivenToSetElementValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.SetElementValue("b", Numbers.Select(x => x + l.Sample())); return S.Hold(0); });

    // S988. An XStreamingElement keeps its content.
    public static Cell<int> QueryKeptByAnXStreamingElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XStreamingElement("a", Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S989. An XStreamingElement keeps an object, and makes no text from it in the constructor.
    public static Cell<int> ObjectKeptByAnXStreamingElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XStreamingElement("a", new Titled(l.Map(v => v)))); return S.Hold(0); });

    // S990. ToString of an XStreamingElement enumerates the content that it kept.
    public static Cell<int> QueryInAnXStreamingElementThatMakesText() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XStreamingElement("a", Numbers.Select(x => x + l.Sample())).ToString()); return S.Hold(0); });

    // S991. Save of an XStreamingElement.
    public static Cell<int> QueryInAnXStreamingElementThatIsSaved() =>
        Cell.Loop<int>().WithoutCaptures(l => { new XStreamingElement("a", Numbers.Select(x => x + l.Sample())).Save(new StringWriter()); return S.Hold(0); });

    // S992. Add of an XStreamingElement keeps the content, and ToString enumerates it.
    public static Cell<int> QueryAddedToAnXStreamingElementThatMakesText() =>
        Cell.Loop<int>().WithoutCaptures(l => { XStreamingElement e = new("a"); e.Add(Numbers.Select(x => x + l.Sample())); Kept.Add(e.ToString()); return S.Hold(0); });

    // S993. An XElement expands an XStreamingElement in its content.
    public static Cell<int> XStreamingElementInTheContentOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", new XStreamingElement("b", Numbers.Select(x => x + l.Sample())))); return S.Hold(0); });

    // S994. A sort of an array with one element compares nothing.
    public static Cell<int> SortOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Array.Sort(one); return S.Hold(0); });

    // S995. A sort of a list with one element.
    public static Cell<int> SortOfAListOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> one = [new(l.Map(v => v))]; one.Sort(); return S.Hold(one.Count); });

    // S996. Order of an array with one element.
    public static Cell<int> OrderOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(one.Order().Count()); });

    // S997. Max of an array with one element.
    public static Cell<int> MaxOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Kept.Add(one.Max()!); return S.Hold(0); });

    // S998. Distinct of an array with one element makes a hash code, and compares nothing.
    public static Cell<int> DistinctOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; return S.Hold(one.Distinct().Count()); });

    // S999. A set from a collection expression with one element.
    public static Cell<int> SetOfOneFromACollectionExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = [new Alike(l.Map(v => v))]; return S.Hold(set.Count); });

    // S1000. A set from an initializer with one element.
    public static Cell<int> SetOfOneFromAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = new() { new Alike(l.Map(v => v)) }; return S.Hold(set.Count); });

    // S1001. A set whose constructor gets an array with one element.
    public static Cell<int> SetOfOneFromAnArray() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = new(new[] { new Alike(l.Map(v => v)) }); return S.Hold(set.Count); });

    // S1002. A sorted set with one element.
    public static Cell<int> SortedSetOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> set = [new(l.Map(v => v))]; return S.Hold(set.Count); });

    // S1003. A dictionary with one key, from an index in the initializer.
    public static Cell<int> DictionaryOfOneFromAnIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Alike, int> d = new() { [new Alike(l.Map(v => v))] = 1 }; return S.Hold(d.Count); });

    // S1004. A dictionary with one key, from an Add in the initializer.
    public static Cell<int> DictionaryOfOneFromAnAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Alike, int> d = new() { { new Alike(l.Map(v => v)), 1 } }; return S.Hold(d.Count); });

    // S1005. A set of one element makes its hash code.
    public static Cell<int> SetOfOneWhoseHashCodeReads() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Hashed> set = [new Hashed(l.Map(v => v))]; return S.Hold(set.Count); });

    // S1006. A sort of an array with two elements.
    public static Cell<int> SortOfAnArrayOfTwo() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] two = [new(l.Map(v => v)), new(Plain)]; Array.Sort(two); return S.Hold(0); });

    // S1007. The list gets one element more before the sort.
    public static Cell<int> SortOfAListOfOneThenOneMore() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v))]; all.Add(new(Plain)); all.Sort(); return S.Hold(0); });

    // S1008. An array from two spreads of an array with one element.
    public static Cell<int> SortOfTwoSpreadsOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Ranked[] more = [.. one, .. one]; Array.Sort(more); return S.Hold(0); });

    // S1009. The local gets a different array before the sort.
    public static Cell<int> SortOfAnArrayOfOneThatIsReplaced() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; one = [new(l.Map(v => v)), new(Plain)]; Array.Sort(one); return S.Hold(0); });

    // S1010. Resize gives the local a longer array through a ref.
    public static Cell<int> SortOfAnArrayOfOneThatIsResized() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Array.Resize(ref one, 2); one[1] = new(Plain); Array.Sort(one); return S.Hold(0); });

    // S1011. A set of one element gets one element more.
    public static Cell<int> SetOfOneThenOneMore() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = [new Alike(l.Map(v => v))]; set.Add(new Alike(Plain)); return S.Hold(set.Count); });

    // S1012. The constructor of the set gets an array, so the element of the initializer is not the only one.
    public static Cell<int> SetFromAnArrayAndAnInitializer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] first = [new Alike(l.Map(v => v))]; HashSet<Alike> set = new(first) { new Alike(Plain) }; return S.Hold(set.Count); });

    // S1013. A search compares the value that it gets with the one element.
    public static Cell<int> ContainsInAListOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Alike> one = [new Alike(l.Map(v => v))]; Kept.Add(one.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1014. A set from a collection expression with two elements.
    public static Cell<int> SetOfTwoFromACollectionExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = [new Alike(l.Map(v => v)), new Alike(Plain)]; return S.Hold(set.Count); });

    // S1015. An assignment by index compares the new key with a key that the dictionary has.
    public static Cell<int> KeyComparedWithAKeyOfTheDictionaryAtAnAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Alike, int> d = new() { { new Alike(l.Map(v => v)), 1 } }; d[new Alike(Plain)] = 2; return S.Hold(d.Count); });

    // S1016. A read by index compares the key with a key that the dictionary has.
    public static Cell<int> KeyComparedWithAKeyOfTheDictionaryAtARead() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Alike, int> d = new() { { new Alike(l.Map(v => v)), 1 } }; return S.Hold(d[new Alike(Plain)]); });

    // S1017. A spread puts the elements of an array into the set before the one element of the expression.
    public static Cell<int> SetFromASpreadAndOneElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] first = [new Alike(l.Map(v => v))]; HashSet<Alike> set = [.. first, new Alike(Plain)]; return S.Hold(set.Count); });

    // S1018. List.Contains gives the call to the element. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ListContainsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(Plain)]; Kept.Add(all.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1019. The same call, where the object that gets the call is pending.
    public static Cell<int> ListContainsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(l.Map(v => v))]; Kept.Add(all.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1020. List.IndexOf. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ListIndexOfOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(Plain)]; return S.Hold(all.IndexOf(new AlikeOwn(l.Map(v => v)))); });

    // S1021. The same call, where the object that gets the call is pending.
    public static Cell<int> ListIndexOfOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(l.Map(v => v))]; return S.Hold(all.IndexOf(new AlikeOwn(Plain))); });

    // S1022. List.Remove. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ListRemoveOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(Plain)]; Kept.Add(all.Remove(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1023. The same call, where the object that gets the call is pending.
    public static Cell<int> ListRemoveOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(l.Map(v => v))]; Kept.Add(all.Remove(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1024. Array.IndexOf. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ArrayIndexOfOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; return S.Hold(Array.IndexOf(all, new AlikeOwn(l.Map(v => v)))); });

    // S1025. The same call, where the object that gets the call is pending.
    public static Cell<int> ArrayIndexOfOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; return S.Hold(Array.IndexOf(all, new AlikeOwn(Plain))); });

    // S1026. Enumerable.Contains over a query. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> QueryContainsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(Plain)]; Kept.Add(all.Where(_ => true).Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1027. The same call, where the object that gets the call is pending.
    public static Cell<int> QueryContainsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(l.Map(v => v))]; Kept.Add(all.Where(_ => true).Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1028. SequenceEqual gives the call to the element of the first sequence. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> SequenceEqualOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> first = [new(Plain)]; List<AlikeOwn> second = [new(l.Map(v => v))]; Kept.Add(first.SequenceEqual(second)); return S.Hold(0); });

    // S1029. The same call, where the object that gets the call is pending.
    public static Cell<int> SequenceEqualOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> first = [new(l.Map(v => v))]; List<AlikeOwn> second = [new(Plain)]; Kept.Add(first.SequenceEqual(second)); return S.Hold(0); });

    // S1030. HashSet.Contains. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> SetContainsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikeOwn> set = [new AlikeOwn(Plain)]; Kept.Add(set.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1031. The same call, where the object that gets the call is pending.
    public static Cell<int> SetContainsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikeOwn> set = [new AlikeOwn(l.Map(v => v))]; Kept.Add(set.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1032. HashSet.Add gives the call to the element that the set has. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> SetAddOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikeOwn> set = [new AlikeOwn(Plain)]; Kept.Add(set.Add(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1033. The same call, where the object that gets the call is pending.
    public static Cell<int> SetAddOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikeOwn> set = [new AlikeOwn(l.Map(v => v))]; Kept.Add(set.Add(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1034. Dictionary.ContainsKey. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryContainsKeyOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; Kept.Add(d.ContainsKey(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1035. The same call, where the object that gets the call is pending.
    public static Cell<int> DictionaryContainsKeyOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; Kept.Add(d.ContainsKey(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1036. Dictionary.TryGetValue. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryTryGetValueOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; Kept.Add(d.TryGetValue(new AlikeOwn(l.Map(v => v)), out int n) ? n : 0); return S.Hold(0); });

    // S1037. The same call, where the object that gets the call is pending.
    public static Cell<int> DictionaryTryGetValueOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; Kept.Add(d.TryGetValue(new AlikeOwn(Plain), out int n) ? n : 0); return S.Hold(0); });

    // S1038. An assignment by index to a dictionary. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryAssignmentOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; d[new AlikeOwn(l.Map(v => v))] = 2; return S.Hold(d.Count); });

    // S1039. The same call, where the object that gets the call is pending.
    public static Cell<int> DictionaryAssignmentOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; d[new AlikeOwn(Plain)] = 2; return S.Hold(d.Count); });

    // S1040. Dictionary.Add. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryAddOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; Kept.Add(d.TryAdd(new AlikeOwn(l.Map(v => v)), 2)); return S.Hold(0); });

    // S1041. The same call, where the object that gets the call is pending.
    public static Cell<int> DictionaryAddOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; Kept.Add(d.TryAdd(new AlikeOwn(Plain), 2)); return S.Hold(0); });

    // S1042. object.Equals with two values gives the call to the first one. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> StaticEqualsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(object.Equals(new AlikeOwn(Plain), new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1043. The same call, where the object that gets the call is pending.
    public static Cell<int> StaticEqualsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(object.Equals(new AlikeOwn(l.Map(v => v)), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1044. The Equals of a tuple gives the call to the element of the tuple that the code calls it on. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> TupleEqualsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add((new AlikeOwn(Plain), 1).Equals((new AlikeOwn(l.Map(v => v)), 1))); return S.Hold(0); });

    // S1045. The same call, where the object that gets the call is pending.
    public static Cell<int> TupleEqualsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add((new AlikeOwn(l.Map(v => v)), 1).Equals((new AlikeOwn(Plain), 1))); return S.Hold(0); });

    // S1046. List.BinarySearch gives the call to the element. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ListBinarySearchOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<RankedOwn> all = [new(Plain)]; return S.Hold(all.BinarySearch(new RankedOwn(l.Map(v => v)))); });

    // S1047. The same call, where the object that gets the call is pending.
    public static Cell<int> ListBinarySearchOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<RankedOwn> all = [new(l.Map(v => v))]; return S.Hold(all.BinarySearch(new RankedOwn(Plain))); });

    // S1048. Array.BinarySearch. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ArrayBinarySearchOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; return S.Hold(Array.BinarySearch(all, new RankedOwn(l.Map(v => v)))); });

    // S1049. The same call, where the object that gets the call is pending.
    public static Cell<int> ArrayBinarySearchOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; return S.Hold(Array.BinarySearch(all, new RankedOwn(Plain))); });

    // S1050. SortedList.Add gives the call to the key that the list has. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> SortedListAddOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<RankedOwn, int> d = new() { { new RankedOwn(Plain), 1 } }; d.Add(new RankedOwn(l.Map(v => v)), 2); return S.Hold(d.Count); });

    // S1051. The same call, where the object that gets the call is pending.
    public static Cell<int> SortedListAddOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<RankedOwn, int> d = new() { { new RankedOwn(l.Map(v => v)), 1 } }; d.Add(new RankedOwn(Plain), 2); return S.Hold(d.Count); });

    // S1052. The default comparer gives the call to the first of the two values. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DefaultComparerOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(Comparer<RankedOwn>.Default.Compare(new RankedOwn(Plain), new RankedOwn(l.Map(v => v)))); });

    // S1053. The same call, where the object that gets the call is pending.
    public static Cell<int> DefaultComparerOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(Comparer<RankedOwn>.Default.Compare(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); });

    // S1054. The default equality comparer. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DefaultEqualityComparerOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(EqualityComparer<AlikeOwn>.Default.Equals(new AlikeOwn(Plain), new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1055. The same call, where the object that gets the call is pending.
    public static Cell<int> DefaultEqualityComparerOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(EqualityComparer<AlikeOwn>.Default.Equals(new AlikeOwn(l.Map(v => v)), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1056. SortedSet.Add gives the call to the value that it gets. The value is plain, and its member reads only its own object.
    public static Cell<int> SortedSetAddOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(l.Map(v => v))]; Kept.Add(set.Add(new RankedOwn(Plain))); return S.Hold(0); });

    // S1057. The same call, where the value is pending.
    public static Cell<int> SortedSetAddOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(Plain)]; Kept.Add(set.Add(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1058. SortedSet.Contains. The value is plain, and its member reads only its own object.
    public static Cell<int> SortedSetContainsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(l.Map(v => v))]; Kept.Add(set.Contains(new RankedOwn(Plain))); return S.Hold(0); });

    // S1059. The same call, where the value is pending.
    public static Cell<int> SortedSetContainsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(Plain)]; Kept.Add(set.Contains(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1060. SortedDictionary.Add. The value is plain, and its member reads only its own object.
    public static Cell<int> SortedDictionaryAddOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<RankedOwn, int> d = new() { { new RankedOwn(l.Map(v => v)), 1 } }; d.Add(new RankedOwn(Plain), 2); return S.Hold(d.Count); });

    // S1061. The same call, where the value is pending.
    public static Cell<int> SortedDictionaryAddOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<RankedOwn, int> d = new() { { new RankedOwn(Plain), 1 } }; d.Add(new RankedOwn(l.Map(v => v)), 2); return S.Hold(d.Count); });

    // S1062. An assignment by index to a SortedDictionary. The value is plain, and its member reads only its own object.
    public static Cell<int> SortedDictionaryAssignmentOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<RankedOwn, int> d = new() { { new RankedOwn(l.Map(v => v)), 1 } }; d[new RankedOwn(Plain)] = 2; return S.Hold(d.Count); });

    // S1063. The same call, where the value is pending.
    public static Cell<int> SortedDictionaryAssignmentOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<RankedOwn, int> d = new() { { new RankedOwn(Plain), 1 } }; d[new RankedOwn(l.Map(v => v))] = 2; return S.Hold(d.Count); });

    // S1064. Contains of a span gives the call to the value that it gets. The value is plain, and its member reads only its own object.
    public static Cell<int> SpanContainsOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(((ReadOnlySpan<EquatableOwn>)all).Contains(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1065. The same call, where the value is pending.
    public static Cell<int> SpanContainsOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; Kept.Add(((ReadOnlySpan<EquatableOwn>)all).Contains(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1066. Enqueue of a PriorityQueue gives the call to the priority that it gets.
    public static Cell<int> PriorityQueueEnqueueOfAPendingPriority() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, RankedOwn> queue = new(); queue.Enqueue(1, new RankedOwn(Plain)); queue.Enqueue(2, new RankedOwn(l.Map(v => v))); return S.Hold(queue.Count); });

    // S1067. Equals reads only the object that it gets. The element gets the call, and the value is plain.
    public static Cell<int> ListContainsAPlainValueWhoseEqualsReadsTheOther() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikePeer> all = [new(l.Map(v => v))]; Kept.Add(all.Contains(new AlikePeer(Plain))); return S.Hold(0); });

    // S1068. The list is from a field, and the evaluator did not see its element made. That element gets the call and reads the value.
    public static Cell<int> FieldListContainsAPendingValueWhoseEqualsReadsTheOther() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(StoredPeers.Contains(new AlikePeer(l.Map(v => v)))); return S.Hold(0); });

    // S1069. The list is from a field, and Equals reads only its own object.
    public static Cell<int> FieldListContainsAPendingValueOwnState() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(StoredOwns.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1070. An object given to AddFirst of an XElement.
    public static Cell<int> ObjectGivenToAddFirstOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.AddFirst(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S1071. An object given to ReplaceNodes.
    public static Cell<int> ObjectGivenToReplaceNodesOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.ReplaceNodes(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S1072. An object given to ReplaceAll.
    public static Cell<int> ObjectGivenToReplaceAllOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { XElement e = new("a"); e.ReplaceAll(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S1073. An object given to SetValue of an XAttribute.
    public static Cell<int> ObjectGivenToSetValueOfAnXAttribute() =>
        Cell.Loop<int>().WithoutCaptures(l => { XAttribute a = new("a", 1); a.SetValue(new Titled(l.Map(v => v))); return S.Hold(0); });

    // S1074. WriteTo of an XStreamingElement enumerates the content that it kept.
    public static Cell<int> QueryInAnXStreamingElementThatIsWritten() =>
        Cell.Loop<int>().WithoutCaptures(l => { using System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(new StringBuilder()); new XStreamingElement("a", Numbers.Select(x => x + l.Sample())).WriteTo(writer); return S.Hold(0); });

    // S1075. A sequence of this compilation in the content of an XElement.
    public static Cell<int> SequenceOfThisCompilationInTheContentOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", new TallyBag(l.Map(v => v)))); return S.Hold(0); });

    // S1076. OrderBy of an array with one element compares nothing.
    public static Cell<int> OrderByOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(one.OrderBy(x => x).Count()); });

    // S1077. OrderByDescending.
    public static Cell<int> OrderByDescendingOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(one.OrderByDescending(x => x).Count()); });

    // S1078. OrderDescending.
    public static Cell<int> OrderDescendingOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(one.OrderDescending().Count()); });

    // S1079. Min.
    public static Cell<int> MinOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Kept.Add(one.Min()!); return S.Hold(0); });

    // S1080. MinBy.
    public static Cell<int> MinByOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Kept.Add(one.MinBy(x => x)!); return S.Hold(0); });

    // S1081. MaxBy.
    public static Cell<int> MaxByOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Kept.Add(one.MaxBy(x => x)!); return S.Hold(0); });

    // S1082. DistinctBy.
    public static Cell<int> DistinctByOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; return S.Hold(one.DistinctBy(x => x).Count()); });

    // S1083. GroupBy.
    public static Cell<int> GroupByOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; return S.Hold(one.GroupBy(x => x).Count()); });

    // S1084. ToDictionary.
    public static Cell<int> ToDictionaryOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; return S.Hold(one.ToDictionary(x => x).Count); });

    // S1085. ToHashSet.
    public static Cell<int> ToHashSetOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; return S.Hold(one.ToHashSet().Count); });

    // S1086. ToLookup.
    public static Cell<int> ToLookupOfAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; return S.Hold(one.ToLookup(x => x).Count); });

    // S1087. The constructor of a SortedSet gets an array with one element.
    public static Cell<int> SortedSetFromAnArrayOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(new SortedSet<Ranked>(one).Count); });

    // S1088. An initializer gives a set two objects whose Equals reads only the object that it gets. The second one is pending.
    public static Cell<int> SetInitializerWhoseEqualsReadsTheOther() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikePeer> set = new() { new AlikePeer(Plain), new AlikePeer(l.Map(v => v)) }; return S.Hold(set.Count); });

    // S1089. List.LastIndexOf. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> ListLastIndexOfOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(Plain)]; return S.Hold(all.LastIndexOf(new AlikeOwn(l.Map(v => v)))); });

    // S1090. The same call, where the object that gets the call is pending.
    public static Cell<int> ListLastIndexOfOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(l.Map(v => v))]; return S.Hold(all.LastIndexOf(new AlikeOwn(Plain))); });

    // S1091. HashSet.Remove. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> SetRemoveOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikeOwn> set = [new AlikeOwn(Plain)]; Kept.Add(set.Remove(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1092. The same call, where the object that gets the call is pending.
    public static Cell<int> SetRemoveOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<AlikeOwn> set = [new AlikeOwn(l.Map(v => v))]; Kept.Add(set.Remove(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1093. Dictionary.Remove. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryRemoveOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; Kept.Add(d.Remove(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1094. The same call, where the object that gets the call is pending.
    public static Cell<int> DictionaryRemoveOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; Kept.Add(d.Remove(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1095. A read by index of a dictionary. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryReadOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; return S.Hold(d[new AlikeOwn(l.Map(v => v))]); });

    // S1096. The same call, where the object that gets the call is pending.
    public static Cell<int> DictionaryReadOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; return S.Hold(d[new AlikeOwn(Plain)]); });

    // S1097. The CompareTo of a tuple. The object that gets the call is plain, and its member reads only its own object.
    public static Cell<int> TupleCompareToOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold((new RankedOwn(Plain), 1).CompareTo((new RankedOwn(l.Map(v => v)), 1))); });

    // S1098. The same call, where the object that gets the call is pending.
    public static Cell<int> TupleCompareToOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold((new RankedOwn(l.Map(v => v)), 1).CompareTo((new RankedOwn(Plain), 1))); });

    // S1099. Dictionary.Add of a key that is equal to the key that the dictionary has. The key that gets the call is plain, and its member reads only its own object.
    public static Cell<int> DictionaryAddOfAKeyOwnStateWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(Plain), 1 } }; try { d.Add(new AlikeOwn(l.Map(v => v)), 2); } catch (ArgumentException) { } return S.Hold(d.Count); });

    // S1100. The same call, where the key that gets the call is pending.
    public static Cell<int> DictionaryAddOfAKeyOwnStateWhereTheReceiverIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<AlikeOwn, int> d = new() { { new AlikeOwn(l.Map(v => v)), 1 } }; try { d.Add(new AlikeOwn(Plain), 2); } catch (ArgumentException) { } return S.Hold(d.Count); });

    // S1101. A method of this compilation makes a new list, and does not give one of the lists that its owner has.
    public static Cell<int> ChangeThroughTheResultOfAMethodOfThisCompilation() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; rows.Fresh().Add(l.Map(v => v)); return S.Hold(rows[0].Count == 0 ? Plain.Sample() : rows[0][0].Sample()); });

    // S1102. The variable of a foreach gets the change itself also. The read is through that variable.
    public static Cell<int> ChangeAndReadThroughAForeachVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<List<Cell<int>>> rows = [new(), new()]; foreach (List<Cell<int>> row in rows) { row.Add(l.Map(v => v)); return S.Hold(row[0].Sample()); } return S.Hold(0); });

    // S1103. A local gets an element of its own collection, and then names its own owner.
    public static Cell<int> ChangeThroughALocalThatGetsItsOwnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> list = [new List<object>()]; list = (List<object>)list[0]; list.Add(l.Map(v => v)); return S.Hold(((Cell<int>)list[0]).Sample()); });

    // S1104. Contains of a span gives the call to the value, which reads the element that the span has.
    public static Cell<int> SpanContainsAPlainValueWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Equatable[] all = [new(l.Map(v => v))]; Kept.Add(((ReadOnlySpan<Equatable>)all).Contains(new Equatable(Plain))); return S.Hold(0); });

    // S1105. The content of an XDocument is an array that has a query.
    public static Cell<int> QueryInAnArrayThatIsTheContentOfAnXDocument() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XDocument(new object[] { new XElement("r"), Numbers.Select(x => x + l.Sample()).Select(n => new XComment(n.ToString())) })); return S.Hold(0); });

    // S1106. The constructor of a PriorityQueue gets one pair, and compares nothing. Enqueue gives the call to the priority that it gets, which is plain.
    public static Cell<int> PriorityQueueOfOneThenEnqueueOfAPlainPriority() =>
        Cell.Loop<int>().WithoutCaptures(l => { (int, RankedOwn)[] one = [(1, new RankedOwn(l.Map(v => v)))]; PriorityQueue<int, RankedOwn> queue = new(one); queue.Enqueue(2, new RankedOwn(Plain)); return S.Hold(queue.Count); });

    // S1107. The constructor of a PriorityQueue gets two pairs, and compares their priorities.
    public static Cell<int> PriorityQueueOfTwoComparesThePriorities() =>
        Cell.Loop<int>().WithoutCaptures(l => { (int, RankedOwn)[] two = [(1, new RankedOwn(l.Map(v => v))), (2, new RankedOwn(Plain))]; PriorityQueue<int, RankedOwn> queue = new(two); return S.Hold(queue.Count); });

    // S1108. A local names itself as its owner, and an assignment by index goes up to each owner.
    public static Cell<int> AssignmentByIndexThroughALocalThatGetsItsOwnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> list = [new List<object> { 0 }]; list = (List<object>)list[0]; list[0] = l.Map(v => v); return S.Hold(((Cell<int>)list[0]).Sample()); });

    // S1109. Each of two locals has an element of the other one, and an assignment by index goes up to each owner.
    public static Cell<int> AssignmentByIndexThroughTwoLocalsThatOwnEachOther() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> first = [new List<object> { new List<object> { 0 } }]; List<object> second = (List<object>)first[0]; first = (List<object>)second[0]; first[0] = l.Map(v => v); return S.Hold(((Cell<int>)first[0]).Sample()); });

    // S1110. An indexer of this compilation does not keep its index. The list has only the value.
    public static Cell<int> PendingIndexThatAnIndexerOfThisCompilationDoesNotKeep() =>
        Cell.Loop<int>().WithoutCaptures(l => { Pile pile = new(); pile[l.Map(v => v)] = Plain; return S.Hold(pile[0].Sample()); });

    // S1111. The same indexer keeps a pending value.
    public static Cell<int> PendingValueThatAnIndexerOfThisCompilationKeeps() =>
        Cell.Loop<int>().WithoutCaptures(l => { Pile pile = new(); pile[Plain] = l.Map(v => v); return S.Hold(pile[0].Sample()); });

    // S1112. The same indexer in an initializer.
    public static Cell<int> PendingIndexInAnInitializerThatAnIndexerOfThisCompilationDoesNotKeep() =>
        Cell.Loop<int>().WithoutCaptures(l => { Pile pile = new() { [l.Map(v => v)] = Plain }; return S.Hold(pile[0].Sample()); });

    // S1113. A collection expression in the call of a constructor has no type of its own. The set compares its two elements.
    public static Cell<int> SetFromACollectionExpressionOfTwoInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new SortedSet<Ranked>([new Ranked(l.Map(v => v)), new Ranked(Plain)]).Count); });

    // S1114. The same call with a spread of an array that has two elements.
    public static Cell<int> SetFromASpreadOfTwoInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; return S.Hold(new SortedSet<Ranked>([..all]).Count); });

    // S1115. One element in the call. The set compares nothing.
    public static Cell<int> SetFromACollectionExpressionOfOneInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new SortedSet<Ranked>([new Ranked(l.Map(v => v))]).Count); });

    // S1116. A spread of an array of one in the call. The set compares nothing.
    public static Cell<int> SetFromASpreadOfOneInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(new SortedSet<Ranked>([..one]).Count); });

    // S1117. A spread and one more element in the call.
    public static Cell<int> SetFromASpreadAndOneElementInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; return S.Hold(new SortedSet<Ranked>([..one, new Ranked(Plain)]).Count); });

    // S1118. A HashSet from two elements in the call compares them, because each has the same hash code.
    public static Cell<int> HashSetFromACollectionExpressionOfTwoInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new HashSet<Alike>([new Alike(l.Map(v => v)), new Alike(Plain)]).Count); });

    // S1119. A HashSet from one element in the call.
    public static Cell<int> HashSetFromACollectionExpressionOfOneInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new HashSet<Alike>([new Alike(l.Map(v => v))]).Count); });

    // S1120. A PriorityQueue from one pair in the call.
    public static Cell<int> PriorityQueueFromOnePairInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, RankedOwn> queue = new([(1, new RankedOwn(l.Map(v => v)))]); return S.Hold(queue.Count); });

    // S1121. A PriorityQueue from two pairs in the call.
    public static Cell<int> PriorityQueueFromTwoPairsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<int, RankedOwn> queue = new([(1, new RankedOwn(l.Map(v => v))), (2, new RankedOwn(Plain))]); return S.Hold(queue.Count); });

    // S1122. A method that makes text gets a collection expression.
    public static Cell<int> JoinOfACollectionExpressionInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Join(",", [new Titled(l.Map(v => v))])); return S.Hold(0); });

    // S1123. Concat gets a collection expression.
    public static Cell<int> ConcatOfACollectionExpressionInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Concat([new Titled(l.Map(v => v)), new Titled(Plain)])); return S.Hold(0); });

    // S1124. A collection expression of plain objects in the call.
    public static Cell<int> SetFromPlainObjectsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(l.Map(v => v)); return S.Hold(new SortedSet<Ranked>([new Ranked(Plain), new Ranked(Plain)]).Count); });

    // S1125. A search in a collection expression in the call.
    public static Cell<int> ContainsInACollectionExpressionInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Enumerable.Contains([new AlikeOwn(l.Map(v => v))], new AlikeOwn(Plain))); return S.Hold(0); });

    // S1126. The same search, where the value is pending and the element is plain.
    public static Cell<int> ContainsOfAPendingValueInACollectionExpressionInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Enumerable.Contains([new AlikeOwn(Plain)], new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1127. A sort of a collection expression behind a cast.
    public static Cell<int> OrderOfACollectionExpressionBehindACast() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(((IEnumerable<Ranked>)[new Ranked(l.Map(v => v)), new Ranked(Plain)]).Order().ToArray().Length); });

    // S1128. A conditional with two collection expressions in the call has no type of its own.
    public static Cell<int> SetFromAConditionalOfCollectionExpressionsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { bool flag = Numbers.Length > 0; return S.Hold(new SortedSet<Ranked>(flag ? [new Ranked(l.Map(v => v)), new Ranked(Plain)] : []).Count); });

    // S1129. A collection expression with a query in the call of a method that enumerates each sequence.
    public static Cell<int> ConcatOfQueriesInACollectionExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { IEnumerable<IEnumerable<int>> all = [Numbers.Select(x => x + l.Sample())]; return S.Hold(all.SelectMany(x => x).Count()); });

    // S1130. A switch expression with collection expressions in the call has no type of its own.
    public static Cell<int> SetFromASwitchOfCollectionExpressionsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = Numbers.Length; return S.Hold(new SortedSet<Ranked>(n switch { 0 => [], _ => [new Ranked(l.Map(v => v)), new Ranked(Plain)] }).Count); });

    // S1131. A collection expression in parentheses in the call.
    public static Cell<int> SetFromACollectionExpressionInParenthesesInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new SortedSet<Ranked>(([new Ranked(l.Map(v => v)), new Ranked(Plain)])).Count); });

    // S1132. A conditional with one element for each result. The analyzer does not count a conditional.
    public static Cell<int> SetFromAConditionalOfOneElementInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { bool flag = Numbers.Length > 0; return S.Hold(new SortedSet<Ranked>(flag ? [new Ranked(l.Map(v => v))] : [new Ranked(Plain)]).Count); });

    // S1133. A switch expression with one element for each result.
    public static Cell<int> SetFromASwitchOfOneElementInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = Numbers.Length; return S.Hold(new SortedSet<Ranked>(n switch { 0 => [new Ranked(Plain)], _ => [new Ranked(l.Map(v => v))] }).Count); });

    // S1134. One element in parentheses in the call.
    public static Cell<int> SetFromOneElementInParenthesesInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new SortedSet<Ranked>(([new Ranked(l.Map(v => v))])).Count); });

    // S1135. A conditional where one result has two elements.
    public static Cell<int> SetFromAConditionalOfOneOrTwoElementsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { bool flag = Numbers.Length > 0; return S.Hold(new SortedSet<Ranked>(flag ? [new Ranked(l.Map(v => v)), new Ranked(Plain)] : [new Ranked(Plain)]).Count); });

    // S1136. A switch expression where one result has two elements.
    public static Cell<int> SetFromASwitchOfOneOrTwoElementsInTheCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { int n = Numbers.Length; return S.Hold(new SortedSet<Ranked>(n switch { 0 => [new Ranked(Plain)], _ => [new Ranked(l.Map(v => v)), new Ranked(Plain)] }).Count); });

    // S1137. A spread of an array of one, and that array then gets a new value with two elements.
    public static Cell<int> SetFromASpreadOfAnArrayThatIsReplaced() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(Plain)]; one = [new(l.Map(v => v)), new(Plain)]; return S.Hold(new SortedSet<Ranked>([..one]).Count); });

    // S1138. Join with a query and one more value gets the two as objects, one by one. It makes text from the query object, and does not enumerate it.
    public static Cell<int> JoinOfAQueryAndOneMoreValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Join(",", Numbers.Select(x => x + l.Sample()), 1)); return S.Hold(0); });

    // S1139. Join with only a query enumerates it.
    public static Cell<int> JoinOfOnlyAQuery() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Join(",", Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S1140. Join with a sequence of this compilation and one more value does not enumerate the sequence.
    public static Cell<int> JoinOfASequenceOfThisCompilationAndOneMoreValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Join(",", new TallyBag(l.Map(v => v)), 1)); return S.Hold(0); });

    // S1141. Join with a list of objects and one more value makes text from the list, and not from its objects.
    public static Cell<int> JoinOfAListOfObjectsAndOneMoreValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> all = [new(l.Map(v => v))]; Kept.Add(string.Join(",", all, 1)); return S.Hold(0); });

    // S1142. Join with two arrays makes text from each array, and not from its objects.
    public static Cell<int> JoinOfTwoArraysOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { Titled[] first = [new(l.Map(v => v))]; Titled[] second = [new(Plain)]; Kept.Add(string.Join(",", first, second)); return S.Hold(0); });

    // S1143. Join with an object and one more value makes text from the object.
    public static Cell<int> JoinOfAnObjectAndOneMoreValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Join(",", new Titled(l.Map(v => v)), 1)); return S.Hold(0); });

    // S1144. Join with one array of objects gets the array as the sequence, and makes text from each object.
    public static Cell<int> JoinOfOneArrayOfObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { object[] all = [new Titled(l.Map(v => v))]; Kept.Add(string.Join(",", all)); return S.Hold(0); });

    // S1145. Format with four values. One of them is a query.
    public static Cell<int> FormatOfAQueryAmongFourValues() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Format("{0}{1}{2}{3}", Numbers.Select(x => x + l.Sample()), 1, 2, 3)); return S.Hold(0); });

    // S1146. Format with four values. One of them is an object.
    public static Cell<int> FormatOfAnObjectAmongFourValues() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Format("{0}{1}{2}{3}", new Titled(l.Map(v => v)), 1, 2, 3)); return S.Hold(0); });

    // S1147. AppendJoin of a StringBuilder with a query and one more value.
    public static Cell<int> AppendJoinOfAQueryAndOneMoreValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new StringBuilder().AppendJoin(",", Numbers.Select(x => x + l.Sample()), 1).ToString()); return S.Hold(0); });

    // S1148. An XElement gets its content one by one, and enumerates each sequence in it.
    public static Cell<int> QueryAsOneOfTwoContentsOfAnXElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new XElement("a", Numbers.Select(x => x + l.Sample()).Select(n => new XElement("b", n)), new XElement("c"))); return S.Hold(0); });

    // S1149. Concat of LINQ gets two queries, each as a sequence.
    public static Cell<int> ConcatOfTwoQueriesThatIsEnumerated() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(Numbers.Concat(Numbers.Select(x => x + l.Sample())).ToArray().Length); });

    public sealed class Scene(Stream<int> restarts)
    {
        public Cell<int> Count { get; } = restarts.Accum(0, (_, n) => n + 1);
    }

    public sealed class HalvingRow(Cell<decimal> share)
    {
        public Cell<decimal> Half { get; } = Halve(share);

        private static Cell<decimal> Halve(Cell<decimal> c) => c.Map(v => v / 2);
    }

    public sealed class ExplicitRow
    {
        public ExplicitRow(Cell<decimal> share)
        {
            this.Share = share.Map(v => v * 2);
        }

        public Cell<decimal> Share { get; }
    }

    public sealed class Row(Cell<decimal> share)
    {
        public Cell<decimal> Share { get; } = share;

        public Cell<decimal> Plain { get; } = Cell.Constant(0m);

        public decimal ShareValue => this.Share.Sample();

        public decimal ShareNow() => this.Share.Sample();

        public decimal Total() => this.Compute() + 1;

        private decimal Compute() => this.ShareNow();

        public string Report => this.Total().ToString();

        public decimal ShareMappedNow() => this.Share.Map(v => v).Sample();

        public Cell<decimal> ShareDoubled => this.Share.Map(v => v * 2);

        public Cell<decimal> PlainDoubled => this.Plain.Map(v => v * 2);
    }

    public interface IShared
    {
        Cell<int> Share { get; }

        int Now();
    }

    public sealed class SharedRow(Cell<int> share) : IShared
    {
        public Cell<int> Share { get; } = share;

        public int Now() => this.Share.Sample();
    }

    public sealed class SelfReader
    {
        public SelfReader(Cell<int> share)
        {
            this.Share = share;
            this.Value = Read(this);
        }

        public Cell<int> Share { get; }

        public int Value { get; }

        private static int Read(SelfReader reader) => reader.Share.Sample();
    }

    public sealed class SettableRow
    {
        public Cell<int>? Share { get; set; }

        public Cell<int>? Other { get; set; }
    }

    public sealed class CapturingRow(Cell<int> cell)
    {
        public int Now() => cell.Sample();
    }

    public sealed class Command(Func<int> read)
    {
        public int Run() => read();
    }

    private static int ReadIt(Cell<int> c) => c.Sample();

    public sealed class SamplingRow(Cell<int> cell)
    {
        public int Value { get; } = cell.Sample();
    }

    public sealed class StoringRow(Cell<int> cell)
    {
        public Cell<int> Cell { get; } = cell;

        public int Value => 1;
    }

    public sealed class GenericSamplingRow<T>(Cell<T> cell)
    {
        public T Value { get; } = cell.Sample();
    }

    public sealed class GenericStoringRow<T>(Cell<T> cell)
    {
        public Cell<T> Cell { get; } = cell;
    }

    public sealed record RecordRow(Cell<int> Share);

    public sealed record RecordPair(Cell<int> Pending, Cell<int> Plain);

    public sealed class InitRow
    {
        public required Cell<int> Share { get; init; }
    }

    public sealed class ValueRow
    {
        public int Value { get; init; }
    }

    public class BaseRow(Cell<int> share)
    {
        public Cell<int> Share { get; } = share;
    }

    public sealed class DerivedRow(Cell<int> share) : BaseRow(share);

    public class SamplingBaseRow(Cell<int> cell)
    {
        public int Value { get; } = cell.Sample();
    }

    public sealed class SamplingDerivedRow(Cell<int> cell) : SamplingBaseRow(cell);

    public class WrittenBaseRow
    {
        public WrittenBaseRow(Cell<int> share)
            : this(share: share, offset: 0)
        {
        }

        private WrittenBaseRow(Cell<int> share, int offset)
        {
            this.Share = share.Map(v => v + offset);
        }

        public Cell<int> Share { get; }
    }

    public sealed class WrittenDerivedRow : WrittenBaseRow
    {
        public WrittenDerivedRow(Cell<int> share)
            : base(share)
        {
        }
    }

    public sealed class ReadingRow
    {
        private readonly Func<int> read;

        public ReadingRow(Cell<int> share)
        {
            this.read = () => share.Sample();
        }

        public int Now => this.read();
    }

    public sealed class LazyRow
    {
        private readonly Lazy<int> now;

        public LazyRow(Cell<int> share)
        {
            this.now = new Lazy<int>(() => share.Sample());
        }

        public int Now => this.now.Value;
    }

    public interface IPatternRow
    {
        int Now { get; }
    }

    public sealed class PatternRow(Cell<int> share) : IPatternRow
    {
        public Cell<int> Share { get; } = share;

        public int Now => this.Share.Sample();

        public bool OtherIsBig(PatternRow other) => other is { Now: > 0 };
    }

    public sealed class PatternOuter(PatternRow inner)
    {
        public PatternRow Inner { get; } = inner;
    }

    public sealed class ReadingComparer(Cell<int> offset) : IComparer<int>
    {
        public int Compare(int x, int y) => x.CompareTo(y + offset.Sample());
    }

    public sealed class MixedComparer(Cell<int> unused, Cell<int> offset) : IComparer<int>
    {
        public Cell<int> Unused { get; } = unused;

        public int Compare(int x, int y) => x.CompareTo(y + offset.Sample());
    }

    public sealed class ReadingEquality(Cell<int> offset) : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => x == y + offset.Sample() - offset.Sample();

        public int GetHashCode(int value) => value + offset.Sample() - offset.Sample();
    }

    public sealed class Tally(Cell<int> share) : IDisposable
    {
        public event Action Changed
        {
            add => Kept.Add(share.Sample());
            remove { }
        }

        public int Level
        {
            get => 0;
            set => Kept.Add(share.Sample() + value);
        }

        public int this[int index]
        {
            get => share.Sample() + index;
            set => Kept.Add(share.Sample() + value);
        }

        public static int operator +(Tally t, int n) => t.Read() + n;

        public static int operator -(Tally t) => -t.Read();

        public static Tally operator ++(Tally t)
        {
            Kept.Add(t.Read());

            return t;
        }

        public static bool operator true(Tally t) => t.Read() > 0;

        public static bool operator false(Tally t) => t.Read() <= 0;

        public static implicit operator long(Tally t) => t.Read();

        public static explicit operator short(Tally t) => (short)t.Read();

        public override string ToString() => this.Read().ToString();

        public void Deconstruct(out int a, out int b)
        {
            a = share.Sample();
            b = 0;
        }

        public void Dispose() => Kept.Add(share.Sample());

        private int Read() => share.Sample();
    }

    public sealed class TallyReading(int value)
    {
        public int Value { get; } = value;

        public static implicit operator TallyReading(Cell<int> cell) => new(cell.Sample());
    }

    public sealed class TallyHolder
    {
        public Cell<int> Source
        {
            get => Plain;
            set => Kept.Add(value.Sample());
        }
    }

    public sealed class TallyPair(Cell<int> first, Cell<int> second)
    {
        public void Deconstruct(out Cell<int> a, out Cell<int> b)
        {
            a = first;
            b = second;
        }
    }

    public sealed class TallyBag(Cell<int> share) : IEnumerable<int>
    {
        public void Add(int value) => Kept.Add(share.Sample() + value);

        public IEnumerator<int> GetEnumerator()
        {
            yield return share.Sample();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public sealed class TallyWalk(Cell<int> share)
    {
        public TallyWalker GetEnumerator() => new(share);
    }

    public sealed class TallyWalker(Cell<int> share)
    {
        private int steps;

        public int Current => share.Sample();

        public bool MoveNext() => this.steps++ < 1;
    }

    public sealed class Titled(Cell<int> share)
    {
        public override string ToString() => share.Sample().ToString();
    }

    // Each member of Alike, Ranked, and Equatable reads the two objects that it has. Thus, a shape
    // gives one result for each of the two that the platform can call.
    public sealed class Alike(Cell<int> share)
    {
        public override bool Equals(object? obj) => obj is Alike other && other.Read() == this.Read();

        public override int GetHashCode() => 1;

        private int Read() => share.Sample();
    }

    public sealed class Hashed(Cell<int> share)
    {
        public override int GetHashCode() => share.Sample();
    }

    public sealed class Sack(Cell<int> share) : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator()
        {
            yield return share.Sample();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public sealed class CellSack(Cell<int> share) : IEnumerable<Cell<int>>
    {
        public IEnumerator<Cell<int>> GetEnumerator()
        {
            yield return share;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public sealed class Bin : IEnumerable<int>
    {
        private readonly List<int> values = [];

        public void Add(Cell<int> cell) => this.values.Add(cell.Sample());

        public IEnumerator<int> GetEnumerator() => this.values.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    private static string TextOf<T>(T value) => value!.ToString()!;

    private static string TextOfAll<T>(IEnumerable<T> values)
    {
        StringBuilder text = new();

        foreach (T value in values)
        {
            text.Append(value!.ToString());
        }

        return text.ToString();
    }

    private static string TextOfAnObject(object value) => value.ToString()!;

    private static T FirstOf<T>(List<T> items) => items[0];

    private static T FirstBy<T>(IEnumerable<T> items) => items.First();

    private static readonly int[] Numbers = [1, 2];

    private static IEnumerable<int>? StoredQuery;

    private static int SumOf(IEnumerable<int> values) => values.Sum();

    private static int TotalOf(this IEnumerable<int> values) => values.Sum();

    private static IEnumerable<int> EachOf(IEnumerable<int> values)
    {
        foreach (int value in values)
        {
            yield return value;
        }
    }

    private static IEnumerable<int> QueryOf(Cell<int> c) => Numbers.Select(x => x + c.Sample());

    private static IEnumerable<int> QueryOfAgain(Cell<int> c) => QueryOf(c);

    private static void QueryOut(Cell<int> c, out IEnumerable<int> made) => made = Numbers.Select(x => x + c.Sample());

    private static Func<IEnumerable<int>> QueryMakerOf(Cell<int> c) => () => Numbers.Select(x => x + c.Sample());

    private static IEnumerable<int> SamplesOf(Cell<int> c)
    {
        yield return c.Sample();
    }

    private static IEnumerable<int> SamplesFrom(Cell<int> c) => SamplesOf(c);

    private static IEnumerator<int> StepsOf(Cell<int> c)
    {
        yield return c.Sample();
    }

    public sealed class QueryHolder(IEnumerable<int> values)
    {
        public int Total() => values.Sum();
    }

    public sealed class Summer(Cell<int> share)
    {
        private readonly IEnumerable<int> items = Numbers.Select(x => x + share.Sample());

        public IEnumerable<int> Items => Numbers.Select(x => x + share.Sample());

        public int Total() => this.items.Sum();
    }

    public sealed class QuerySlot
    {
        public IEnumerable<int> Values { get; set; } = [];
    }

    public sealed class IteratorOwner(Cell<int> share)
    {
        public IEnumerable<int> Each()
        {
            yield return share.Sample();
        }
    }

    public sealed class GenericBox<T>(T value)
    {
        public T Value { get; } = value;
    }

    public interface IShown
    {
        string Show();
    }

    public sealed class GenericLabel<T>(Cell<T> share) : IShown
    {
        public string Show() => share.Sample()!.ToString()!;

        public override string ToString() => share.Sample()!.ToString()!;
    }

    public sealed class GenericPile<T>(Cell<T> share) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
        {
            yield return share.Sample();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public sealed class GenericGrade<T>(Cell<T> share) : IComparable<GenericGrade<T>>
        where T : IComparable<T>
    {
        public int CompareTo(GenericGrade<T>? other) => this.Read().CompareTo(other == null ? default! : other.Read());

        private T Read() => share.Sample();
    }

    public sealed class GenericCloser<T>(Cell<T> share) : IDisposable
    {
        public void Dispose() => Kept.Add(share.Sample()!);
    }

    public abstract class ShownBase
    {
        public abstract string Show();
    }

    public sealed class GenericShower<T>(Cell<T> share) : ShownBase
    {
        public override string Show() => share.Sample()!.ToString()!;
    }

    public static class GenericOuter<T>
    {
        public sealed class Label(Cell<T> share)
        {
            public override string ToString() => share.Sample()!.ToString()!;
        }
    }

    public sealed class TitledPile(Cell<int> share) : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator()
        {
            yield return share.Sample();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();

        public override string ToString() => share.Sample().ToString();
    }

    private static string ValueTextOf<T>(KeyValuePair<string, T> pair) => pair.Value!.ToString()!;

    public sealed class Plainly
    {
        public override string ToString() => "plain";
    }

    public sealed class ObjectWrapper(object inner)
    {
        public override string ToString() => inner.GetType().Name;
    }

    private static Ranked Itself(Ranked r) => r;

    private static int Zero(Ranked r) => 0;

    private static Hashed SameHashed(Hashed h) => h;

    public sealed class Ranked(Cell<int> share) : IComparable<Ranked>
    {
        public int CompareTo(Ranked? other) => this.Read().CompareTo(other?.Read() ?? 0);

        private int Read() => share.Sample();
    }

    // Equals reads only the object that it gets.
    public sealed class AlikePeer(Cell<int> share)
    {
        public override bool Equals(object? obj) => obj is AlikePeer other && other.Read() >= 0;

        public override int GetHashCode() => 1;

        private int Read() => share.Sample();
    }

    public sealed class Equatable(Cell<int> share) : IEquatable<Equatable>
    {
        public bool Equals(Equatable? other) => other != null && other.Read() == this.Read();

        public override bool Equals(object? obj) => obj is Equatable other && this.Equals(other);

        public override int GetHashCode() => 1;

        private int Read() => share.Sample();
    }

    public sealed record Tagged(Cell<int> Share);

    public sealed class Stroll(Cell<int> share) : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => new Stroller(share);

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public sealed class Stroller(Cell<int> share) : IEnumerator<int>
    {
        private int steps;

        public int Current => share.Sample();

        object System.Collections.IEnumerator.Current => this.Current;

        public bool MoveNext() => this.steps++ < 1;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    public readonly struct TitledValue(Cell<int> share)
    {
        private readonly Cell<int> share = share;

        public override string ToString() => this.share.Sample().ToString();
    }

    private static void AddTo(List<Cell<int>> cells, Cell<int> one) => cells.Add(one);

    private static void AddToThroughOne(List<Cell<int>> cells, Cell<int> one) => AddTo(cells, one);

    private static void AddToANewList(List<Cell<int>> cells, Cell<int> one)
    {
        cells = [];
        cells.Add(one);
    }

    private static void FillSlot(SettableSlot slot, Cell<int> one) => slot.Share = one;

    private static void FillCopy(SlotValue slot, Cell<int> one) => slot.Share = one;

    private static void PutIn(Dictionary<string, Cell<int>> cells, Cell<int> one) => cells["a"] = one;

    private static void SetFirst(Cell<int>[] cells, Cell<int> one) => cells[0] = one;

    public sealed class SettableSlot
    {
        public Cell<int>? Share { get; set; }
    }

    public struct SlotValue
    {
        public Cell<int>? Share { get; set; }
    }

    public sealed class Registrar
    {
        public Registrar(List<Cell<int>> cells, Cell<int> one) => cells.Add(one);
    }

    public sealed class Yielder(Cell<int> share)
    {
        public IEnumerable<int> Values
        {
            get
            {
                yield return share.Sample();
            }
        }

        public IEnumerator<int> Steps
        {
            get
            {
                yield return share.Sample();
            }
        }

        public IEnumerable<int> Now
        {
            get
            {
                int now = share.Sample();

                return [now];
            }
        }

        public object Held() => this.Values;

        public int Sum() => this.Values.Sum();
    }

    public sealed class SerialRow(Cell<int> share)
    {
        public int Now => share.Sample();
    }

    public sealed class SerialPlainRow(Cell<int> share)
    {
        public int Three => 3;

        public bool Has() => share != null;
    }

    public sealed class SerialIgnoredRow(Cell<int> share)
    {
        [JsonIgnore]
        public int Now => share.Sample();

        internal int Later => share.Sample();

        public int this[int index] => share.Sample() + index;

        public static int Count => 0;
    }

    public sealed class SerialConditionRow(Cell<int> share)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Now => share.Sample();
    }

    public sealed class SerialOuter(SerialRow inner)
    {
        public SerialRow Inner => inner;
    }

    public sealed class SerialQueryRow(IEnumerable<int> values)
    {
        public IEnumerable<int> Values => values;
    }

    public sealed class XmlRow
    {
        private readonly Cell<int>? share;

        public XmlRow()
        {
        }

        public XmlRow(Cell<int> share) => this.share = share;

        public int Now
        {
            get => this.share?.Sample() ?? 0;
            set => _ = value;
        }
    }

    public sealed class XmlFixedRow
    {
        private readonly Cell<int>? share;

        public XmlFixedRow()
        {
        }

        public XmlFixedRow(Cell<int> share) => this.share = share;

        public int Fixed => this.share?.Sample() ?? 0;
    }

    public sealed class RankedByOffset(Cell<int> offset) : IComparer<Ranked>
    {
        public int Compare(Ranked? x, Ranked? y) => offset.Sample();
    }

    private static void FillTitled(TitledSlot slot, Cell<int> one) => slot.Share = one;

    public sealed class TitledSlot(Cell<int> first)
    {
        public Cell<int>? Share { get; set; }

        public override string ToString() => first.Sample().ToString();
    }

    public sealed class SerialStaticRow
    {
        public static Cell<int>? Shared { get; set; }

        public static int Now => Shared!.Sample();

        public int Three => 3;
    }

    private static void FillLate(LateSlot slot, Cell<int> one) => slot.Share = one;

    public sealed class LateSlot
    {
        public Cell<int>? Share { get; set; }

        public override string ToString() => this.Share!.Sample().ToString();
    }

    public sealed class YielderHolder(Yielder inner)
    {
        public Yielder Inner => inner;
    }

    private static IEnumerable<int> ValuesByAPattern(Yielder r) => r is { Values: var v } ? v : [];

    public sealed class ShownReader(Cell<int> share) : IShown
    {
        public string Show() => share.Sample().ToString();
    }

    public sealed class ShownPlain(Cell<int> share) : IShown
    {
        public string Show() => "plain";

        public bool Has() => share != null;
    }

    // Show is not the method of IShown.
    public sealed class ShowsAlone(Cell<int> share)
    {
        public string Show() => share.Sample().ToString();
    }

    public interface IHasCell
    {
        Cell<int> Share { get; }
    }

    public sealed class CellHolder(Cell<int> unused) : IHasCell
    {
        public Cell<int> Share { get; } = Cell.Constant(1);

        public bool Has() => unused != null;
    }

    // Share is not the property of IHasCell.
    public sealed class CellAlone(Cell<int> share)
    {
        public Cell<int> Share { get; } = share;
    }

    public interface IHasNow
    {
        int Now { get; }
    }

    public sealed class NowPlain(Cell<int> share) : IHasNow
    {
        public int Now => 0;

        public bool Has() => share != null;
    }

    public sealed class NowReader(Cell<int> share) : IHasNow
    {
        public int Now => share.Sample();
    }

    public interface IEach
    {
        IEnumerable<int> Each();
    }

    public sealed class EachLazy(Cell<int> share) : IEach
    {
        public IEnumerable<int> Each()
        {
            yield return share.Sample();
        }
    }

    public sealed class EachNow(Cell<int> share) : IEach
    {
        public IEnumerable<int> Each()
        {
            int now = share.Sample();

            return [now];
        }
    }

    public sealed class ShownExplicitly(Cell<int> share) : IShown
    {
        string IShown.Show() => "plain";

        public bool Has() => share != null;
    }

    public sealed class CellGiver(Cell<int> share) : IHasCell
    {
        public Cell<int> Share { get; } = share;
    }

    public interface IRuns
    {
        int Run(Func<int> function);
    }

    public sealed class RunsExplicitly(Cell<int> share) : IRuns
    {
        int IRuns.Run(Func<int> function) => 0;

        public bool Has() => share != null;
    }

    public sealed class RunsNow(Cell<int> share) : IRuns
    {
        public int Run(Func<int> function) => function();

        public bool Has() => share != null;
    }

    public sealed class ShownExplicitReader(Cell<int> share) : IShown
    {
        string IShown.Show() => share.Sample().ToString();
    }

    public sealed class NowExplicitReader(Cell<int> share) : IHasNow
    {
        int IHasNow.Now => share.Sample();
    }

    public sealed class ShownTwice(Cell<int> share) : IShown
    {
        string IShown.Show() => "plain";

        public string Show() => share.Sample().ToString();
    }

    public sealed class RankedExplicitly(Cell<int> share) : IComparable<RankedExplicitly>
    {
        int IComparable<RankedExplicitly>.CompareTo(RankedExplicitly? other) => share.Sample();
    }

    public class ShownBaseExplicitly(Cell<int> share) : IShown
    {
        string IShown.Show() => share.Sample().ToString();
    }

    public sealed class ShownDerived(Cell<int> share) : ShownBaseExplicitly(share);

    public sealed class ClosesExplicitly(Cell<int> share) : IDisposable
    {
        void IDisposable.Dispose() => share.Sample();
    }

    public sealed class SackExplicitly(Cell<int> share) : IEnumerable<int>
    {
        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            yield return share.Sample();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            ((IEnumerable<int>)this).GetEnumerator();
    }

    public interface IPicks<T>
    {
        int Pick(T value);

        int Pick(T value, int more);
    }

    public sealed class PicksExplicitly(Cell<int> share) : IPicks<int>
    {
        int IPicks<int>.Pick(int value) => value;

        int IPicks<int>.Pick(int value, int more) => share.Sample() + more;
    }

    private static int CountOf<T>(IEnumerable<T> values)
    {
        int n = 0;

        foreach (T _ in values)
        {
            n++;
        }

        return n;
    }

    private static int CountAs<T>(T values)
        where T : IEnumerable<int>
    {
        int n = 0;

        foreach (int value in values)
        {
            n += value;
        }

        return n;
    }

    private static IEnumerable<T> OnlyWhen<T>(IEnumerable<T> values, Cell<int> c) => values.Where(_ => c.Sample() > 0);

    private static IEnumerable<string> TextsOf(Cell<int> c) =>
        Numbers.Where(x => x + c.Sample() > 0).Select(x => x.ToString());

    private static IEnumerable<string> TextsThroughALocal(Cell<int> c)
    {
        IEnumerable<int> some = Numbers.Where(x => x + c.Sample() > 0);

        return some.Select(x => x.ToString());
    }

    private static IEnumerable<int> LengthsOf(Cell<int> c) => TextsOf(c).Select(t => t.Length);

    private static IEnumerable<string> TextsOfCounts(Cell<int> c) =>
        Numbers.Where(x => x + c.Sample() > 0).Select(x => Numbers.Where(y => y > x).Count().ToString());

    private static int CountOfArrays<T>(IEnumerable<T[]> values)
    {
        int n = 0;

        foreach (T[] value in values)
        {
            n += value.Length;
        }

        return n;
    }

    private static string KeyTextOf(KeyValuePair<Titled, Titled> pair) => pair.Key.ToString();

    private static string ValueTextOfBoth(KeyValuePair<Titled, Titled> pair) => pair.Value.ToString();

    public sealed class CrossedParts(Cell<int> first, Cell<int> second)
    {
        public Cell<int> First { get; } = first;

        public Cell<int> Second { get; } = second;

        public void Deconstruct(out Cell<int> first, out Cell<int> second)
        {
            first = this.Second;
            second = this.First;
        }
    }

    public class Leveled(Cell<int> level)
    {
        public virtual Cell<int> Level { get; } = level;
    }

    public sealed class LeveledLate(Cell<int> late) : Leveled(Plain)
    {
        public override Cell<int> Level { get; } = late;
    }

    private static string FirstTextOf(KeyValuePair<Titled, Titled> pair)
    {
        (Titled first, Titled _) = pair;

        return first.ToString();
    }

    private static string SecondTextOf(KeyValuePair<Titled, Titled> pair)
    {
        (Titled _, Titled second) = pair;

        return second.ToString();
    }

    public sealed class Stepped(Cell<int> now, Cell<int> next)
    {
        public Cell<int> Now { get; } = now;

        public static Stepped operator ++(Stepped step) => new(step.Later, step.Later);

        public static Stepped operator --(Stepped step) => new(step.Later, step.Later);

        private Cell<int> Later { get; } = next;
    }

    public sealed class Marked(Cell<int> share)
    {
        public Cell<int> Share { get; } = share;
    }

    public sealed class Ledger
    {
        public Cell<int> Last { get; private set; } = Plain;

        public Cell<int> this[Titled at]
        {
            get => this.Last;
            set
            {
                Kept.Add(at.ToString());
                this.Last = value;
            }
        }
    }

    public sealed class TallyNest(TallyPair inner)
    {
        public void Deconstruct(out TallyPair pair, out int count)
        {
            pair = inner;
            count = 1;
        }
    }

    private static readonly List<AlikePeer> StoredPeers = [new(Plain)];

    private static readonly List<AlikeOwn> StoredOwns = [new(Plain)];

    // Equals reads only its own object.
    public sealed class AlikeOwn(Cell<int> share)
    {
        public override bool Equals(object? obj) => obj is AlikeOwn && share.Sample() >= 0;

        public override int GetHashCode() => 1;
    }

    // Equals reads only its own object.
    public sealed class EquatableOwn(Cell<int> share) : IEquatable<EquatableOwn>
    {
        public bool Equals(EquatableOwn? other) => other != null && share.Sample() >= 0;

        public override bool Equals(object? obj) => obj is EquatableOwn other && this.Equals(other);

        public override int GetHashCode() => 1;
    }

    // CompareTo reads only its own object.
    public sealed class RankedOwn(Cell<int> share) : IComparable<RankedOwn>
    {
        public int CompareTo(RankedOwn? other) => share.Sample().CompareTo(0);
    }

    public sealed class Pile : List<Cell<int>>
    {
        public Cell<int> this[Cell<int> at]
        {
            set => this.Add(value);
        }
    }

    public sealed record ItemId(int Number) : IIdentity<int>
    {
        public int Key => this.Number;
    }
}

file static class Helpers
{
    public static T Fresh<T>(this List<T> all)
        where T : new() =>
        all.Count < 0 ? all[0] : new T();

    public static int Count(this List<Shapes.Marked> all)
    {
        all.Reverse();

        return all.Count;
    }

    public static Shapes.Marked First(this List<Shapes.Marked> all)
    {
        all.Reverse();

        return all[0];
    }

    public static void AddOne(this List<Cell<int>> cells, Cell<int> one) => cells.Add(one);

    public static Stream<T> SwitchCWithDeferredValues<T>(this Cell<Cell<T>> cca) =>
        Operational.Defer(cca.SwitchC().Values());
}
