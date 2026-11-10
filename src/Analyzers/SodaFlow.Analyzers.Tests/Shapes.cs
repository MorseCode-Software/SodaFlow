using System;
using System.Collections.Generic;
using System.Linq;
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

    public sealed record ItemId(int Number) : IIdentity<int>
    {
        public int Key => this.Number;
    }
}

file static class Helpers
{
    public static Stream<T> SwitchCWithDeferredValues<T>(this Cell<Cell<T>> cca) =>
        Operational.Defer(cca.SwitchC().Values());
}
