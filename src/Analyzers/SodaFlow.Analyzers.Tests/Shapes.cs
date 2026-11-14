using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
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

    // S1. Direct read.
    public static Cell<int> DirectSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Sample(); return S.Hold(x); });

    // S2. Read through Map.
    public static Cell<int> MapSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Map(v => v + 1).Sample(); return S.Hold(x); });

    // S3. Read through Lift, placeholder as argument.
    public static Cell<int> LiftSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Plain.Lift(l, (a, b) => a + b).Sample(); return S.Hold(x); });

    // S4. Read through Calm.
    public static Cell<int> CalmSample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Calm().Sample(); return S.Hold(x); });

    // S5. Read through SwitchC on a looped cell of cells.
    public static Cell<Cell<int>> SwitchCSample() =>
        Cell.Loop<Cell<int>>().WithoutCaptures(l => { int x = l.SwitchC().Sample(); return Cell.Constant(Cell.Constant(x)); });

    // S6. Read through HoldLazy of a lazy sample of the placeholder.
    public static Cell<int> HoldLazySample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Stream.Never<int>().HoldLazy(l.SampleLazy()).Sample(); return S.Hold(x); });

    // S7. Read through ConstantLazy and a lazy Map.
    public static Cell<int> ConstantLazySample() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = Cell.ConstantLazy(l.SampleLazy().Map(v => v + 1)).Sample(); return S.Hold(x); });

    // S8. A hold with a given value is not pending.
    public static Cell<int> HoldWithValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { int x = l.Updates().Hold(7).Sample(); return S.Hold(x); });

    // S9. A snapshot samples at firing time.
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
            int five = Five(l).Sample();
            return S.Hold(two + three + four + five);
        });

    private static Cell<int> One(Cell<int> c) => c.Map(v => v + 1);
    private static Cell<int> Two(Cell<int> c) => One(c);
    private static Cell<int> Three(Cell<int> c) => Two(c);
    private static Cell<int> Four(Cell<int> c) => Three(c);
    private static Cell<int> Five(Cell<int> c) => Four(c);

    // S53. A row method that reads a pending member through a second method and a computed property.
    public static Cell<decimal> RowMethodThroughSecondMethod() =>
        Cell.Loop<decimal>().WithoutCaptures(total =>
        {
            Row row = new(share: Cell.Constant(1m).Lift(total, (a, b) => a / b));
            return Cell.Constant(row.Total() + row.Report.Length);
        });

    // S54. A helper that samples its parameter through a second helper.
    public static Cell<int> HelperSamplesThroughHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => S.Hold(ReadViaOne(l) + ReadViaThree(l) + ReadViaFour(l)));

    private static int ReadViaOne(Cell<int> c) => ReadIt(c);
    private static int ReadViaTwo(Cell<int> c) => ReadViaOne(c);
    private static int ReadViaThree(Cell<int> c) => ReadViaTwo(c);
    private static int ReadViaFour(Cell<int> c) => ReadViaThree(c);

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

    // S1150. Contains of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanContainsOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.Contains(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1151. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanContainsOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.Contains(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1152. Contains of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanContainsOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1153. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanContainsOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1154. IndexOf of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanIndexOfOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.IndexOf(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1155. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanIndexOfOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOf(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1156. IndexOf of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanIndexOfOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.IndexOf(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1157. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanIndexOfOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOf(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1158. IndexOf of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanIndexOfASpanOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.IndexOf(other)); return S.Hold(0); });

    // S1159. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanIndexOfASpanOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOf(other)); return S.Hold(0); });

    // S1160. IndexOf of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanIndexOfASpanOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.IndexOf(other)); return S.Hold(0); });

    // S1161. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanIndexOfASpanOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOf(other)); return S.Hold(0); });

    // S1162. LastIndexOf of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanLastIndexOfOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOf(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1163. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOf(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1164. LastIndexOf of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOf(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1165. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanLastIndexOfOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOf(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1166. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAny(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1167. IndexOfAny of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.IndexOfAny(new AlikeOwn(Plain), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1168. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanIndexOfAnyOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAny(new AlikeOwn(l.Map(v => v)), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1169. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAny(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1170. LastIndexOfAny of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOfAny(new AlikeOwn(Plain), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1171. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanLastIndexOfAnyOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAny(new AlikeOwn(l.Map(v => v)), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1172. IndexOfAnyExcept of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyExceptOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.IndexOfAnyExcept(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1173. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanIndexOfAnyExceptOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAnyExcept(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1174. IndexOfAnyExcept of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyExceptOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.IndexOfAnyExcept(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1175. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanIndexOfAnyExceptOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAnyExcept(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1176. LastIndexOfAnyExcept of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyExceptOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOfAnyExcept(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1177. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanLastIndexOfAnyExceptOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAnyExcept(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1178. LastIndexOfAnyExcept of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyExceptOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOfAnyExcept(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1179. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanLastIndexOfAnyExceptOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAnyExcept(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1180. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.ContainsAny(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1181. ContainsAny of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.ContainsAny(new AlikeOwn(Plain), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1182. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanContainsAnyOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.ContainsAny(new AlikeOwn(l.Map(v => v)), new AlikeOwn(Plain))); return S.Hold(0); });

    // S1183. ContainsAnyExcept of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyExceptOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.ContainsAnyExcept(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1184. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanContainsAnyExceptOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.ContainsAnyExcept(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1185. ContainsAnyExcept of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyExceptOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.ContainsAnyExcept(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1186. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanContainsAnyExceptOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.ContainsAnyExcept(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1187. Count of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanCountOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.Count(new EquatableOwn(Plain))); return S.Hold(0); });

    // S1188. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanCountOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.Count(new EquatableOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1189. Count of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanCountOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.Count(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1190. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanCountOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.Count(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1191. CountAny of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanCountAnyOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.CountAny(other)); return S.Hold(0); });

    // S1192. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanCountAnyOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.CountAny(other)); return S.Hold(0); });

    // S1193. CountAny of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanCountAnyOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.CountAny(other)); return S.Hold(0); });

    // S1194. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanCountAnyOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.CountAny(other)); return S.Hold(0); });

    // S1195. SequenceEqual of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanSequenceEqualOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.SequenceEqual(other)); return S.Hold(0); });

    // S1196. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanSequenceEqualOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.SequenceEqual(other)); return S.Hold(0); });

    // S1197. SequenceEqual of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanSequenceEqualOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.SequenceEqual(other)); return S.Hold(0); });

    // S1198. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanSequenceEqualOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.SequenceEqual(other)); return S.Hold(0); });

    // S1199. StartsWith of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanStartsWithOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.StartsWith(other)); return S.Hold(0); });

    // S1200. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanStartsWithOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.StartsWith(other)); return S.Hold(0); });

    // S1201. StartsWith of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanStartsWithOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.StartsWith(other)); return S.Hold(0); });

    // S1202. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanStartsWithOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.StartsWith(other)); return S.Hold(0); });

    // S1203. EndsWith of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanEndsWithOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.EndsWith(other)); return S.Hold(0); });

    // S1204. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanEndsWithOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.EndsWith(other)); return S.Hold(0); });

    // S1205. EndsWith of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanEndsWithOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.EndsWith(other)); return S.Hold(0); });

    // S1206. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanEndsWithOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.EndsWith(other)); return S.Hold(0); });

    // S1207. CommonPrefixLength of a span, with an element type that is IEquatable. The element is pending, and it gets the call.
    public static Cell<int> SpanCommonPrefixLengthOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.CommonPrefixLength(other)); return S.Hold(0); });

    // S1208. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanCommonPrefixLengthOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.CommonPrefixLength(other)); return S.Hold(0); });

    // S1209. CommonPrefixLength of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanCommonPrefixLengthOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; Kept.Add(all.CommonPrefixLength(other)); return S.Hold(0); });

    // S1210. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanCommonPrefixLengthOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.CommonPrefixLength(other)); return S.Hold(0); });

    // S1211. Replace of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanReplaceOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; all.Replace(new EquatableOwn(Plain), new EquatableOwn(Plain)); return S.Hold(0); });

    // S1212. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanReplaceOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; all.Replace(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain)); return S.Hold(0); });

    // S1213. Replace of a span, with an element type that has only Equals of object. The element is pending, and it gets the call.
    public static Cell<int> SpanReplaceOfAnObjectWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; AlikeOwn[] other = [new(Plain)]; all.Replace(new AlikeOwn(Plain), new AlikeOwn(Plain)); return S.Hold(0); });

    // S1214. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanReplaceOfAnObjectWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; AlikeOwn[] other = [new(l.Map(v => v))]; all.Replace(new AlikeOwn(l.Map(v => v)), new AlikeOwn(Plain)); return S.Hold(0); });

    // S1215. Trim of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanTrimOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.AsSpan().Trim(new EquatableOwn(Plain)).Length); return S.Hold(0); });

    // S1216. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanTrimOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.AsSpan().Trim(new EquatableOwn(l.Map(v => v))).Length); return S.Hold(0); });

    // S1217. TrimStart of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanTrimStartOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.AsSpan().TrimStart(new EquatableOwn(Plain)).Length); return S.Hold(0); });

    // S1218. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanTrimStartOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.AsSpan().TrimStart(new EquatableOwn(l.Map(v => v))).Length); return S.Hold(0); });

    // S1219. TrimEnd of a span, with an element type that is IEquatable. The element is pending, and the value gets the call.
    public static Cell<int> SpanTrimEndOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain)]; Kept.Add(all.AsSpan().TrimEnd(new EquatableOwn(Plain)).Length); return S.Hold(0); });

    // S1220. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanTrimEndOfAnEquatableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; EquatableOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.AsSpan().TrimEnd(new EquatableOwn(l.Map(v => v))).Length); return S.Hold(0); });

    // S1221. BinarySearch of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanBinarySearchOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.BinarySearch(new RankedOwn(Plain))); return S.Hold(0); });

    // S1222. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanBinarySearchOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.BinarySearch(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1223. SequenceCompareTo of a span, with an element type that is IComparable. The element is pending, and it gets the call.
    public static Cell<int> SpanSequenceCompareToOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.SequenceCompareTo(other)); return S.Hold(0); });

    // S1224. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanSequenceCompareToOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.SequenceCompareTo(other)); return S.Hold(0); });

    // S1225. IndexOfAnyInRange of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanIndexOfAnyInRangeOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.IndexOfAnyInRange(new RankedOwn(Plain), new RankedOwn(Plain))); return S.Hold(0); });

    // S1226. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyInRangeOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAnyInRange(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); return S.Hold(0); });

    // S1227. LastIndexOfAnyInRange of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanLastIndexOfAnyInRangeOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOfAnyInRange(new RankedOwn(Plain), new RankedOwn(Plain))); return S.Hold(0); });

    // S1228. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyInRangeOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAnyInRange(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); return S.Hold(0); });

    // S1229. IndexOfAnyExceptInRange of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanIndexOfAnyExceptInRangeOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.IndexOfAnyExceptInRange(new RankedOwn(Plain), new RankedOwn(Plain))); return S.Hold(0); });

    // S1230. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyExceptInRangeOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAnyExceptInRange(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); return S.Hold(0); });

    // S1231. LastIndexOfAnyExceptInRange of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanLastIndexOfAnyExceptInRangeOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.LastIndexOfAnyExceptInRange(new RankedOwn(Plain), new RankedOwn(Plain))); return S.Hold(0); });

    // S1232. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyExceptInRangeOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAnyExceptInRange(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); return S.Hold(0); });

    // S1233. ContainsAnyInRange of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanContainsAnyInRangeOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.ContainsAnyInRange(new RankedOwn(Plain), new RankedOwn(Plain))); return S.Hold(0); });

    // S1234. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyInRangeOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.ContainsAnyInRange(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); return S.Hold(0); });

    // S1235. ContainsAnyExceptInRange of a span, with an element type that is IComparable. The element is pending, and the value gets the call.
    public static Cell<int> SpanContainsAnyExceptInRangeOfAComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(l.Map(v => v))]; RankedOwn[] other = [new(Plain)]; Kept.Add(all.ContainsAnyExceptInRange(new RankedOwn(Plain), new RankedOwn(Plain))); return S.Hold(0); });

    // S1236. The same call, where the value is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyExceptInRangeOfAComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] all = [new(Plain)]; RankedOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.ContainsAnyExceptInRange(new RankedOwn(l.Map(v => v)), new RankedOwn(Plain))); return S.Hold(0); });

    // S1237. Sort of a span with two elements. The first one gets the call.
    public static Cell<int> SpanSortOfTwo() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] two = [new(l.Map(v => v)), new(Plain)]; two.AsSpan().Sort(); return S.Hold(0); });

    // S1238. Sort of an array as a span, with one element. It compares nothing.
    public static Cell<int> SpanSortOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] one = [new(l.Map(v => v))]; MemoryExtensions.Sort<RankedOwn>(one); return S.Hold(0); });

    // S1239. Sort of keys and items compares the keys only.
    public static Cell<int> SpanSortOfKeysAndItems() =>
        Cell.Loop<int>().WithoutCaptures(l => { int[] keys = [2, 1]; RankedOwn[] items = [new(l.Map(v => v)), new(Plain)]; keys.AsSpan().Sort(items.AsSpan()); return S.Hold(0); });

    // S1240. Sort of keys that are objects, with plain items.
    public static Cell<int> SpanSortOfObjectKeysAndItems() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] keys = [new(l.Map(v => v)), new(Plain)]; int[] items = [1, 2]; keys.AsSpan().Sort(items.AsSpan()); return S.Hold(0); });

    // S1241. Sort of a span with a comparer in a type parameter calls the comparer, and not CompareTo of an element.
    public static Cell<int> SpanSortWithAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedOwn[] two = [new(l.Map(v => v)), new(Plain)]; two.AsSpan().Sort(Comparer<RankedOwn>.Create((x, y) => 0)); return S.Hold(0); });

    // S1242. A method of MemoryExtensions that gets a comparer calls the comparer, and not Equals of an element.
    public static Cell<int> SpanContainsWithAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.AsSpan().Contains(new AlikeOwn(Plain), EqualityComparer<AlikeOwn>.Create((x, y) => ReferenceEquals(x, y), x => 1))); return S.Hold(0); });

    // S1243. Contains of a span in a local of the span type. The element type is from the type of the span.
    public static Cell<int> SpanInALocalContainsWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; Kept.Add(Has(all)); return S.Hold(0); static bool Has(ReadOnlySpan<AlikeOwn> span) => span.Contains(new AlikeOwn(Plain)); });

    // S1244. The code has an object in a local, and a list literal gets it. A change through the local is a change to what the list has.
    public static Cell<int> ChangeThroughALocalThatAListLiteralHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1245. The list gets the object with Add.
    public static Cell<int> ChangeThroughALocalThatAListGotWithAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = new(); slots.Add(slot); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1246. The array gets the object with an assignment to an element.
    public static Cell<int> ChangeThroughALocalThatAnElementGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot[] slots = new SettableSlot[1]; slots[0] = slot; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1247. An array with an initializer.
    public static Cell<int> ChangeThroughALocalThatAnArrayInitializerHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot[] slots = { slot }; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1248. An array creation with an initializer.
    public static Cell<int> ChangeThroughALocalThatAnArrayCreationHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot[] slots = new[] { slot }; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1249. A List with a collection initializer.
    public static Cell<int> ChangeThroughALocalThatACollectionInitializerHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = new() { slot }; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1250. A dictionary gets the object through its indexer.
    public static Cell<int> ChangeThroughALocalThatADictionaryGotByIndex() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<string, SettableSlot> map = new(); map["a"] = slot; slot.Share = l.Map(v => v); return S.Hold(map["a"].Share!.Sample()); });

    // S1251. A dictionary gets the object with Add.
    public static Cell<int> ChangeThroughALocalThatADictionaryGotWithAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<string, SettableSlot> map = new(); map.Add("a", slot); slot.Share = l.Map(v => v); return S.Hold(map["a"].Share!.Sample()); });

    // S1252. A dictionary initializer with an index.
    public static Cell<int> ChangeThroughALocalThatADictionaryInitializerHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<string, SettableSlot> map = new() { ["a"] = slot }; slot.Share = l.Map(v => v); return S.Hold(map["a"].Share!.Sample()); });

    // S1253. A dictionary initializer with a pair in braces.
    public static Cell<int> ChangeThroughALocalThatAPairInADictionaryInitializerHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<string, SettableSlot> map = new() { { "a", slot } }; slot.Share = l.Map(v => v); return S.Hold(map["a"].Share!.Sample()); });

    // S1254. A local that the code declared before gets a literal with the object.
    public static Cell<int> ChangeThroughALocalThatAnAssignedLiteralHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = []; slots = [slot]; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1255. Insert of a list.
    public static Cell<int> ChangeThroughALocalThatAListGotWithInsert() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = new(); slots.Insert(0, slot); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1256. Push of a stack.
    public static Cell<int> ChangeThroughALocalThatAStackGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Stack<SettableSlot> slots = new(); slots.Push(slot); slot.Share = l.Map(v => v); return S.Hold(slots.Peek().Share!.Sample()); });

    // S1257. Enqueue of a queue.
    public static Cell<int> ChangeThroughALocalThatAQueueGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Queue<SettableSlot> slots = new(); slots.Enqueue(slot); slot.Share = l.Map(v => v); return S.Hold(slots.Peek().Share!.Sample()); });

    // S1258. A literal in a literal.
    public static Cell<int> ChangeThroughALocalThatAnInnerLiteralHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<List<SettableSlot>> rows = [[slot]]; slot.Share = l.Map(v => v); return S.Hold(rows[0][0].Share!.Sample()); });

    // S1259. A literal that a list gets with Add.
    public static Cell<int> ChangeThroughALocalThatAnAddedLiteralHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<List<SettableSlot>> rows = new(); rows.Add([slot]); slot.Share = l.Map(v => v); return S.Hold(rows[0][0].Share!.Sample()); });

    // S1260. Two lists get the object. The read is from the first one.
    public static Cell<int> ChangeThroughALocalThatTwoListsHaveReadFromTheFirst() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> first = new(); List<SettableSlot> second = new(); first.Add(slot); second.Add(slot); slot.Share = l.Map(v => v); return S.Hold(first[0].Share!.Sample()); });

    // S1261. The same, where the read is from the second one.
    public static Cell<int> ChangeThroughALocalThatTwoListsHaveReadFromTheSecond() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> first = new(); List<SettableSlot> second = new(); first.Add(slot); second.Add(slot); slot.Share = l.Map(v => v); return S.Hold(second[0].Share!.Sample()); });

    // S1262. A list gets the object, and a second list gets that list.
    public static Cell<int> ChangeThroughALocalThatAListInAListHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> inner = new(); List<List<SettableSlot>> outer = new(); inner.Add(slot); outer.Add(inner); slot.Share = l.Map(v => v); return S.Hold(outer[0][0].Share!.Sample()); });

    // S1263. The local gets a new object after the list got the first one. The change is to the new object.
    public static Cell<int> ChangeThroughALocalThatHasANewObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; slot = new SettableSlot { Share = Plain }; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1264. The list gets the object after the change, and has the pending member from the start.
    public static Cell<int> ListGetsAnObjectAfterTheChange() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; slot.Share = l.Map(v => v); List<SettableSlot> slots = [slot]; return S.Hold(slots[0].Share!.Sample()); });

    // S1265. The change is a plain value.
    public static Cell<int> PlainChangeThroughALocalThatAListHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; Kept.Add(l.Map(v => v)); slot.Share = Plain; return S.Hold(slots[0].Share!.Sample()); });

    // S1266. The object in the local is a list. The code adds it to a list of rows, and then fills it.
    public static Cell<int> RowFilledAfterTheRowsGotIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> row = new(); List<List<Cell<int>>> rows = new(); rows.Add(row); row.Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S1267. The same with a literal for the rows.
    public static Cell<int> RowFilledAfterALiteralOfRowsGotIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> row = new(); List<List<Cell<int>>> rows = [row]; row.Add(l.Map(v => v)); return S.Hold(rows[0][0].Sample()); });

    // S1268. A helper changes the object that the list has.
    public static Cell<int> HelperChangesALocalThatAListHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; Fill(slot, l.Map(v => v)); return S.Hold(slots[0].Share!.Sample()); static void Fill(SettableSlot into, Cell<int> share) => into.Share = share; });

    // S1269. A parameter of a lambda is the local whose object a list gets.
    public static Cell<int> ChangeThroughAParameterThatAListHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = new(); Action<SettableSlot> put = slot => { slots.Add(slot); slot.Share = l.Map(v => v); }; put(new SettableSlot { Share = Plain }); return S.Hold(slots[0].Share!.Sample()); });

    // S1270. A search reads the object of a local, and the list does not get it. A change through the local after that is not a change to what the list has.
    public static Cell<int> ChangeThroughALocalAfterContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [new() { Share = Plain }]; Kept.Add(slots.Contains(slot)); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1271. IndexOf.
    public static Cell<int> ChangeThroughALocalAfterIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [new() { Share = Plain }]; Kept.Add(slots.IndexOf(slot)); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1272. LastIndexOf.
    public static Cell<int> ChangeThroughALocalAfterLastIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [new() { Share = Plain }]; Kept.Add(slots.LastIndexOf(slot)); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1273. Remove of an object that the list does not have.
    public static Cell<int> ChangeThroughALocalAfterRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [new() { Share = Plain }]; Kept.Add(slots.Remove(slot)); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1274. ContainsKey of a dictionary.
    public static Cell<int> ChangeThroughALocalAfterContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<SettableSlot, SettableSlot> map = new() { { new() { Share = Plain }, new() { Share = Plain } } }; Kept.Add(map.ContainsKey(slot)); slot.Share = l.Map(v => v); return S.Hold(map.Keys.First().Share!.Sample()); });

    // S1275. ContainsValue of a dictionary.
    public static Cell<int> ChangeThroughALocalAfterContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<SettableSlot, SettableSlot> map = new() { { new() { Share = Plain }, new() { Share = Plain } } }; Kept.Add(map.ContainsValue(slot)); slot.Share = l.Map(v => v); return S.Hold(map.Values.First().Share!.Sample()); });

    // S1276. TryGetValue of a dictionary.
    public static Cell<int> ChangeThroughALocalAfterTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<SettableSlot, SettableSlot> map = new() { { new() { Share = Plain }, new() { Share = Plain } } }; Kept.Add(map.TryGetValue(slot, out _)); slot.Share = l.Map(v => v); return S.Hold(map.Values.First().Share!.Sample()); });

    // S1277. A literal has the object, and a second literal has the local of the first one.
    public static Cell<int> ChangeThroughALocalThatALiteralInALiteralOfLocalsHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> inner = [slot]; List<List<SettableSlot>> outer = [inner]; slot.Share = l.Map(v => v); return S.Hold(outer[0][0].Share!.Sample()); });

    // S1278. Each of two lists has the other one. The walk to the collections that got an object has an end.
    public static Cell<int> ListsThatHaveEachOtherGetAValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<object> first = new(); List<object> second = new(); first.Add(second); second.Add(first); first.Add(l.Map(v => v)); return S.Hold(((Cell<int>)((List<object>)second[0])[1]).Sample()); });

    // S1279. Equals of an object of this compilation gets the object of a local, and does not keep it. The object that got the call is not a collection.
    public static Cell<int> ChangeThroughALocalAfterEqualsOfAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot first = new() { Share = Plain }; Kept.Add(first.Equals(slot)); slot.Share = l.Map(v => v); return S.Hold(first.Share!.Sample()); });

    // S1280. A list type of this compilation gets the object with the Add of List.
    public static Cell<int> ChangeThroughALocalThatAListTypeOfThisCompilationGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SlotList slots = new(); slots.Add(slot); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1281. An object initializer gives a member the object of a local. The object with that member is not a collection, and its own member with the same name stays plain.
    public static Cell<int> ChangeThroughALocalThatAMemberOfAnObjectGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotNode tail = new() { Share = Plain }; SlotNode head = new() { Share = Plain, Next = tail }; tail.Share = l.Map(v => v); return S.Hold(head.Share!.Sample()); });

    // S1282. A conditional where the second result has two elements, and the code takes that one.
    public static Cell<int> SetFromAConditionalWhereTheSecondResultHasTwoElements() =>
        Cell.Loop<int>().WithoutCaptures(l => { bool flag = Numbers.Length == 0; return S.Hold(new SortedSet<Ranked>(flag ? [new Ranked(Plain)] : [new Ranked(l.Map(v => v)), new Ranked(Plain)]).Count); });

    // S1283. A PriorityQueue is a collection of the platform that is not a sequence. It gets the object with Enqueue.
    public static Cell<int> ChangeThroughALocalThatAPriorityQueueGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; PriorityQueue<SettableSlot, int> queue = new(); queue.Enqueue(slot, 1); slot.Share = l.Map(v => v); return S.Hold(queue.Peek().Share!.Sample()); });

    // S1284. An array creation with its type and an initializer.
    public static Cell<int> ChangeThroughALocalThatAnArrayCreationWithATypeHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot[] slots = new SettableSlot[] { slot }; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1285. An object creation with its type and a collection initializer.
    public static Cell<int> ChangeThroughALocalThatAnObjectCreationWithATypeHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = new List<SettableSlot> { slot }; slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1286. A params parameter gets one query, which the compiler gives as one element. The callee keeps the query.
    public static Cell<int> OneQueryForAParamsParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(System.Collections.Immutable.ImmutableList.Create(Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S1287. SequenceCompareTo of a span, with an element type that is IComparable with no type argument. The compiler takes the overload with a comparer parameter. The element is pending, and it gets the call.
    public static Cell<int> SpanSequenceCompareToOfAPlainComparableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedPlainOwn[] all = [new(l.Map(v => v))]; RankedPlainOwn[] other = [new(Plain)]; Kept.Add(all.SequenceCompareTo(other)); return S.Hold(0); });

    // S1288. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanSequenceCompareToOfAPlainComparableWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { RankedPlainOwn[] all = [new(Plain)]; RankedPlainOwn[] other = [new(l.Map(v => v))]; Kept.Add(all.SequenceCompareTo(other)); return S.Hold(0); });

    // S1289. Join gets one value, a query with the type of object. The compiler gives it as one element of the params parameter. Join makes text from the query object.
    public static Cell<int> JoinOfOneQueryAsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(string.Join(",", (object)Numbers.Select(x => x + l.Sample()))); return S.Hold(0); });

    // S1290. A comparer argument that is null is no comparer. Sort then uses the default one, which calls CompareTo.
    public static Cell<int> ListSortWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; all.Sort((IComparer<Ranked>?)null); return S.Hold(all.Count); });

    // S1291. Array.Sort with a null comparer.
    public static Cell<int> ArraySortWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Array.Sort(all, (IComparer<Ranked>?)null); return S.Hold(all.Length); });

    // S1292. Array.Sort with a default expression for the comparer.
    public static Cell<int> ArraySortWithADefaultComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Array.Sort(all, default(IComparer<Ranked>)); return S.Hold(all.Length); });

    // S1293. Array.Sort with the default literal for the comparer.
    public static Cell<int> ArraySortWithTheDefaultLiteral() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; Array.Sort<Ranked>(array: all, comparer: default); return S.Hold(all.Length); });

    // S1294. The constructor of a SortedSet with a null comparer.
    public static Cell<int> SortedSetWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; return S.Hold(new SortedSet<Ranked>(all, null).Count); });

    // S1295. The constructor of a HashSet with a null comparer.
    public static Cell<int> HashSetWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] all = [new(l.Map(v => v)), new(Plain)]; return S.Hold(new HashSet<Alike>(all, null).Count); });

    // S1296. OrderBy with a null comparer for its keys.
    public static Cell<int> OrderByWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; return S.Hold(all.OrderBy(x => x, null).ToArray().Length); });

    // S1297. Contains of LINQ with a null comparer.
    public static Cell<int> ContainsWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<AlikeOwn> all = [new(l.Map(v => v))]; Kept.Add(all.Contains(new AlikeOwn(Plain), null)); return S.Hold(0); });

    // S1298. Contains of a span with a null comparer. The element gets the call.
    public static Cell<int> SpanContainsWithANullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.AsSpan().Contains(new AlikeOwn(Plain), null)); return S.Hold(0); });

    // S1299. The same call, where the value is pending, and the element gets the call.
    public static Cell<int> SpanContainsWithANullComparerWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; Kept.Add(all.AsSpan().Contains(new AlikeOwn(l.Map(v => v)), null)); return S.Hold(0); });

    // S1300. A null comparer with the operator that says not null.
    public static Cell<int> ListSortWithANullComparerThatSaysNotNull() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; all.Sort((IComparer<Ranked>)null!); return S.Hold(all.Count); });

    // S1301. A comparer of this compilation that reads nothing. Sort calls it, and not CompareTo.
    public static Cell<int> ListSortWithAComparerThatReadsNothing() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; all.Sort(Comparer<Ranked>.Create((x, y) => 0)); return S.Hold(all.Count); });

    // S1302. A list that a property of an object in a local gives. The code puts a pending cell into it, and reads it through the same property.
    public static Cell<int> ListInAPropertyOfALocalObjectGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellBox box = new(); box.Cells.Add(l.Map(v => v)); return S.Hold(box.Cells[0].Sample()); });

    // S1303. The list gets an object with a pending member.
    public static Cell<int> ListInAPropertyOfALocalObjectGetsAPendingObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotBox box = new(); box.Slots.Add(new SettableSlot { Share = l.Map(v => v) }); return S.Hold(box.Slots[0].Share!.Sample()); });

    // S1304. The list is in a field.
    public static Cell<int> ListInAFieldOfALocalObjectGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellCrate crate = new(); crate.Cells.Add(l.Map(v => v)); return S.Hold(crate.Cells[0].Sample()); });

    // S1305. The list is in a property that an object initializer set.
    public static Cell<int> ListInASetPropertyOfALocalObjectGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellTray tray = new() { Cells = new() }; tray.Cells!.Add(l.Map(v => v)); return S.Hold(tray.Cells[0].Sample()); });

    // S1306. The list is in a property of a property.
    public static Cell<int> ListInAPropertyOfAPropertyGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellOuter outer = new(); outer.Box.Cells.Add(l.Map(v => v)); return S.Hold(outer.Box.Cells[0].Sample()); });

    // S1307. An element of the list gets the pending cell in an assignment.
    public static Cell<int> ElementOfAListInAPropertyGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellBox box = new(); box.Cells.Add(Plain); box.Cells[0] = l.Map(v => v); return S.Hold(box.Cells[0].Sample()); });

    // S1308. A dictionary in a property gets the pending cell through its indexer.
    public static Cell<int> DictionaryInAPropertyGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellMap map = new(); map.Cells["a"] = l.Map(v => v); return S.Hold(map.Cells["a"].Sample()); });

    // S1309. A method of the object reads the list that the code filled through the property.
    public static Cell<int> MethodOfTheObjectReadsTheListThatTheCodeFilled() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellBox box = new(); box.Cells.Add(l.Map(v => v)); return S.Hold(box.First()); });

    // S1310. Two objects of one type. The list of the first one gets the pending cell, and the read is from the second.
    public static Cell<int> ListOfASecondObjectOfTheSameTypeIsPlain() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellBox first = new(); CellBox second = new(); first.Cells.Add(l.Map(v => v)); second.Cells.Add(Plain); return S.Hold(second.Cells[0].Sample()); });

    // S1311. The list of an object in a local gets the object of a second local, and a change through that local.
    public static Cell<int> ChangeThroughALocalThatAListOfAnObjectHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SlotBox box = new(); box.Slots.Add(slot); slot.Share = l.Map(v => v); return S.Hold(box.Slots[0].Share!.Sample()); });

    // S1312. The object is a value of a list. The list in its property gets the pending cell.
    public static Cell<int> ListInAPropertyOfAnElementGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<CellBox> boxes = [new()]; boxes[0].Cells.Add(l.Map(v => v)); return S.Hold(boxes[0].Cells[0].Sample()); });

    // S1313. ExceptWith of a HashSet.
    public static Cell<int> HashSetExceptWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; c.ExceptWith(other); return S.Hold(0); });

    // S1314. ExceptWith of a SortedSet.
    public static Cell<int> SortedSetExceptWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; c.ExceptWith(other); return S.Hold(0); });

    // S1315. IntersectWith of a HashSet.
    public static Cell<int> HashSetIntersectWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; c.IntersectWith(other); return S.Hold(0); });

    // S1316. IntersectWith of a SortedSet.
    public static Cell<int> SortedSetIntersectWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; c.IntersectWith(other); return S.Hold(0); });

    // S1317. IsProperSubsetOf of a HashSet.
    public static Cell<int> HashSetIsProperSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain), new(Plain)]; Kept.Add(c.IsProperSubsetOf(other)); return S.Hold(0); });

    // S1318. IsProperSubsetOf of a SortedSet.
    public static Cell<int> SortedSetIsProperSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; Kept.Add(c.IsProperSubsetOf(other)); return S.Hold(0); });

    // S1319. IsProperSupersetOf of a HashSet.
    public static Cell<int> HashSetIsProperSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.IsProperSupersetOf(other)); return S.Hold(0); });

    // S1320. IsProperSupersetOf of a SortedSet.
    public static Cell<int> SortedSetIsProperSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; Kept.Add(c.IsProperSupersetOf(other)); return S.Hold(0); });

    // S1321. IsSubsetOf of a HashSet.
    public static Cell<int> HashSetIsSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.IsSubsetOf(other)); return S.Hold(0); });

    // S1322. IsSubsetOf of a SortedSet.
    public static Cell<int> SortedSetIsSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; Kept.Add(c.IsSubsetOf(other)); return S.Hold(0); });

    // S1323. IsSupersetOf of a HashSet.
    public static Cell<int> HashSetIsSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.IsSupersetOf(other)); return S.Hold(0); });

    // S1324. IsSupersetOf of a SortedSet.
    public static Cell<int> SortedSetIsSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; Kept.Add(c.IsSupersetOf(other)); return S.Hold(0); });

    // S1325. Overlaps of a HashSet.
    public static Cell<int> HashSetOverlaps() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.Overlaps(other)); return S.Hold(0); });

    // S1326. Overlaps of a SortedSet.
    public static Cell<int> SortedSetOverlaps() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; Kept.Add(c.Overlaps(other)); return S.Hold(0); });

    // S1327. SetEquals of a HashSet.
    public static Cell<int> HashSetSetEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.SetEquals(other)); return S.Hold(0); });

    // S1328. SetEquals of a SortedSet.
    public static Cell<int> SortedSetSetEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; Kept.Add(c.SetEquals(other)); return S.Hold(0); });

    // S1329. SymmetricExceptWith of a HashSet.
    public static Cell<int> HashSetSymmetricExceptWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; c.SymmetricExceptWith(other); return S.Hold(0); });

    // S1330. SymmetricExceptWith of a SortedSet.
    public static Cell<int> SortedSetSymmetricExceptWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; c.SymmetricExceptWith(other); return S.Hold(0); });

    // S1331. UnionWith of a HashSet.
    public static Cell<int> HashSetUnionWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; c.UnionWith(other); return S.Hold(0); });

    // S1332. UnionWith of a SortedSet.
    public static Cell<int> SortedSetUnionWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Ranked[] other = [new(Plain)]; c.UnionWith(other); return S.Hold(0); });

    // S1333. TryGetValue of a HashSet.
    public static Cell<int> HashSetTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> c = [new(l.Map(v => v))]; Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1334. Remove of a SortedSet.
    public static Cell<int> SortedSetRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Kept.Add(c.Remove(new Ranked(Plain))); return S.Hold(0); });

    // S1335. TryGetValue of a SortedSet.
    public static Cell<int> SortedSetTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Kept.Add(c.TryGetValue(new Ranked(Plain), out _)); return S.Hold(0); });

    // S1336. GetViewBetween of a SortedSet.
    public static Cell<int> SortedSetGetViewBetween() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> c = [new(l.Map(v => v))]; Kept.Add(c.GetViewBetween(new Ranked(Plain), new Ranked(Plain)).Count); return S.Hold(0); });

    // S1337. The constructor of a Dictionary.
    public static Cell<int> DictionaryConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] two = [new(new Alike(l.Map(v => v)), 1), new(new Alike(Plain), 2)]; Kept.Add(new Dictionary<Alike, int>(two).Count); return S.Hold(0); });

    // S1338. ContainsValue of a Dictionary, for a value.
    public static Cell<int> DictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Alike> c = new() { { "a", new Alike(l.Map(v => v)) } }; Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1339. The constructor of a SortedDictionary.
    public static Cell<int> SortedDictionaryConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new SortedDictionary<Ranked, int>(new Dictionary<Ranked, int> { { new Ranked(l.Map(v => v)), 1 }, { new Ranked(Plain), 2 } }).Count); return S.Hold(0); });

    // S1340. ContainsKey of a SortedDictionary.
    public static Cell<int> SortedDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.ContainsKey(new Ranked(Plain))); return S.Hold(0); });

    // S1341. ContainsKey of a SortedList.
    public static Cell<int> SortedListContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.ContainsKey(new Ranked(Plain))); return S.Hold(0); });

    // S1342. Remove of a SortedDictionary.
    public static Cell<int> SortedDictionaryRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.Remove(new Ranked(Plain))); return S.Hold(0); });

    // S1343. Remove of a SortedList.
    public static Cell<int> SortedListRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.Remove(new Ranked(Plain))); return S.Hold(0); });

    // S1344. TryGetValue of a SortedDictionary.
    public static Cell<int> SortedDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.TryGetValue(new Ranked(Plain), out _)); return S.Hold(0); });

    // S1345. A read by index of a SortedDictionary.
    public static Cell<int> SortedDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c[new Ranked(Plain)]); return S.Hold(0); });

    // S1346. ContainsValue of a SortedDictionary, for a value.
    public static Cell<int> SortedDictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedDictionary<string, Alike> c = new() { { "a", new Alike(l.Map(v => v)) } }; Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1347. The constructor of a SortedList.
    public static Cell<int> SortedListConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(new SortedList<Ranked, int>(new Dictionary<Ranked, int> { { new Ranked(l.Map(v => v)), 1 }, { new Ranked(Plain), 2 } }).Count); return S.Hold(0); });

    // S1348. TryGetValue of a SortedList.
    public static Cell<int> SortedListTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.TryGetValue(new Ranked(Plain), out _)); return S.Hold(0); });

    // S1349. IndexOfKey of a SortedList.
    public static Cell<int> SortedListIndexOfKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c.IndexOfKey(new Ranked(Plain))); return S.Hold(0); });

    // S1350. A read by index of a SortedList.
    public static Cell<int> SortedListRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(c[new Ranked(Plain)]); return S.Hold(0); });

    // S1351. An assignment by index of a SortedList.
    public static Cell<int> SortedListAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Ranked, int> c = new() { { new Ranked(l.Map(v => v)), 1 } }; c[new Ranked(Plain)] = 2; return S.Hold(0); });

    // S1352. ContainsValue of a SortedList, for a value.
    public static Cell<int> SortedListContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<string, Alike> c = new() { { "a", new Alike(l.Map(v => v)) } }; Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1353. IndexOfValue of a SortedList, for a value.
    public static Cell<int> SortedListIndexOfValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<string, Alike> c = new() { { "a", new Alike(l.Map(v => v)) } }; Kept.Add(c.IndexOfValue(new Alike(Plain))); return S.Hold(0); });

    // S1354. Contains of a LinkedList.
    public static Cell<int> LinkedListContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; LinkedList<Alike> c = new(one); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1355. Find of a LinkedList.
    public static Cell<int> LinkedListFind() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; LinkedList<Alike> c = new(one); Kept.Add(c.Find(new Alike(Plain)) != null); return S.Hold(0); });

    // S1356. FindLast of a LinkedList.
    public static Cell<int> LinkedListFindLast() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; LinkedList<Alike> c = new(one); Kept.Add(c.FindLast(new Alike(Plain)) != null); return S.Hold(0); });

    // S1357. Remove of a LinkedList.
    public static Cell<int> LinkedListRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; LinkedList<Alike> c = new(one); Kept.Add(c.Remove(new Alike(Plain))); return S.Hold(0); });

    // S1358. Contains of a Queue.
    public static Cell<int> QueueContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Queue<Alike> c = new(one); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1359. Contains of a Stack.
    public static Cell<int> StackContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Stack<Alike> c = new(one); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1360. DequeueEnqueue of a PriorityQueue.
    public static Cell<int> PriorityQueueDequeueEnqueue() =>
        Cell.Loop<int>().WithoutCaptures(l => { (int, Ranked)[] one = [(1, new Ranked(l.Map(v => v)))]; PriorityQueue<int, Ranked> c = new(one); Kept.Add(c.DequeueEnqueue(2, new Ranked(Plain))); return S.Hold(0); });

    // S1361. LastIndexOf of Array.
    public static Cell<int> ArrayLastIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Kept.Add(Array.LastIndexOf(c, new Alike(Plain))); return S.Hold(0); });

    // S1362. The constructor of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] two = [new(new Alike(l.Map(v => v)), 1), new(new Alike(Plain), 2)]; Kept.Add(new OrderedDictionary<Alike, int>(two).Count); return S.Hold(0); });

    // S1363. Add of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; c.Add(new Alike(Plain), 2); return S.Hold(0); });

    // S1364. ContainsKey of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1365. IndexOf of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1366. Remove of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.Remove(new Alike(Plain))); return S.Hold(0); });

    // S1367. Insert of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryInsert() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; c.Insert(0, new Alike(Plain), 2); return S.Hold(0); });

    // S1368. SetAt of an OrderedDictionary.
    public static Cell<int> OrderedDictionarySetAt() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; c.SetAt(0, new Alike(Plain), 2); return S.Hold(0); });

    // S1369. TryAdd of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryTryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.TryAdd(new Alike(Plain), 2)); return S.Hold(0); });

    // S1370. TryGetValue of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1371. A read by index of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c[new Alike(Plain)]); return S.Hold(0); });

    // S1372. An assignment by index of an OrderedDictionary.
    public static Cell<int> OrderedDictionaryAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; c[new Alike(Plain)] = 2; return S.Hold(0); });

    // S1373. ContainsValue of an OrderedDictionary, for a value.
    public static Cell<int> OrderedDictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { OrderedDictionary<string, Alike> c = new() { { "a", new Alike(l.Map(v => v)) } }; Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1374. The constructor of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryConstructor() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] two = [new(new Alike(l.Map(v => v)), 1), new(new Alike(Plain), 2)]; Kept.Add(new ConcurrentDictionary<Alike, int>(two).Count); return S.Hold(0); });

    // S1375. AddOrUpdate of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryAddOrUpdate() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.AddOrUpdate(new Alike(Plain), 2, (key, old) => old)); return S.Hold(0); });

    // S1376. ContainsKey of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1377. GetOrAdd of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryGetOrAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.GetOrAdd(new Alike(Plain), 2)); return S.Hold(0); });

    // S1378. TryAdd of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryTryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.TryAdd(new Alike(Plain), 2)); return S.Hold(0); });

    // S1379. TryGetValue of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1380. TryRemove of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryTryRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.TryRemove(new Alike(Plain), out _)); return S.Hold(0); });

    // S1381. TryUpdate of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryTryUpdate() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c.TryUpdate(new Alike(Plain), 2, 1)); return S.Hold(0); });

    // S1382. A read by index of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); Kept.Add(c[new Alike(Plain)]); return S.Hold(0); });

    // S1383. An assignment by index of a ConcurrentDictionary.
    public static Cell<int> ConcurrentDictionaryAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ConcurrentDictionary<Alike, int> c = new(one); c[new Alike(Plain)] = 2; return S.Hold(0); });

    // S1384. Contains of a Collection.
    public static Cell<int> CollectionContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Collection<Alike> c = new(new List<Alike> { new(l.Map(v => v)) }); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1385. IndexOf of a Collection.
    public static Cell<int> CollectionIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Collection<Alike> c = new(new List<Alike> { new(l.Map(v => v)) }); Kept.Add(c.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1386. Remove of a Collection.
    public static Cell<int> CollectionRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { Collection<Alike> c = new(new List<Alike> { new(l.Map(v => v)) }); Kept.Add(c.Remove(new Alike(Plain))); return S.Hold(0); });

    // S1387. Contains of a ReadOnlyCollection.
    public static Cell<int> ReadOnlyCollectionContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ReadOnlyCollection<Alike> c = new(new List<Alike> { new(l.Map(v => v)) }); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1388. IndexOf of a ReadOnlyCollection.
    public static Cell<int> ReadOnlyCollectionIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ReadOnlyCollection<Alike> c = new(new List<Alike> { new(l.Map(v => v)) }); Kept.Add(c.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1389. ContainsKey of a ReadOnlyDictionary.
    public static Cell<int> ReadOnlyDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { ReadOnlyDictionary<Alike, int> c = new(new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }); Kept.Add(c.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1390. TryGetValue of a ReadOnlyDictionary.
    public static Cell<int> ReadOnlyDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ReadOnlyDictionary<Alike, int> c = new(new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }); Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1391. A read by index of a ReadOnlyDictionary.
    public static Cell<int> ReadOnlyDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { ReadOnlyDictionary<Alike, int> c = new(new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }); Kept.Add(c[new Alike(Plain)]); return S.Hold(0); });

    // S1392. GetValueOrDefault of CollectionExtensions.
    public static Cell<int> CollectionExtensionsGetValueOrDefault() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<Alike, int> c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.GetValueOrDefault(new Alike(Plain))); return S.Hold(0); });

    // S1393. Remove of CollectionExtensions.
    public static Cell<int> CollectionExtensionsRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary<Alike, int> c = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.Remove(new Alike(Plain), out _)); return S.Hold(0); });

    // S1394. TryAdd of CollectionExtensions.
    public static Cell<int> CollectionExtensionsTryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary<Alike, int> c = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.TryAdd(new Alike(Plain), 2)); return S.Hold(0); });

    // S1395. ExceptBy of Enumerable.
    public static Cell<int> EnumerableExceptBy() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.ExceptBy(other, x => x).ToArray().Length); return S.Hold(0); });

    // S1396. IntersectBy of Enumerable.
    public static Cell<int> EnumerableIntersectBy() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.IntersectBy(other, x => x).ToArray().Length); return S.Hold(0); });

    // S1397. UnionBy of Enumerable.
    public static Cell<int> EnumerableUnionBy() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.UnionBy(other, x => x).ToArray().Length); return S.Hold(0); });

    // S1398. GroupJoin of Enumerable.
    public static Cell<int> EnumerableGroupJoin() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.GroupJoin(other, x => x, y => y, (x, ys) => ys.Count()).ToArray().Length); return S.Hold(0); });

    // S1399. LeftJoin of Enumerable.
    public static Cell<int> EnumerableLeftJoin() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.LeftJoin(other, x => x, y => y, (x, y) => 1).ToArray().Length); return S.Hold(0); });

    // S1400. RightJoin of Enumerable.
    public static Cell<int> EnumerableRightJoin() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v))]; Alike[] other = [new(Plain)]; Kept.Add(c.RightJoin(other, x => x, y => y, (x, y) => 1).ToArray().Length); return S.Hold(0); });

    // S1401. CountBy of Enumerable.
    public static Cell<int> EnumerableCountBy() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v)), new(Plain)]; Kept.Add(c.CountBy(x => x).ToArray().Length); return S.Hold(0); });

    // S1402. AggregateBy of Enumerable.
    public static Cell<int> EnumerableAggregateBy() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] c = [new(l.Map(v => v)), new(Plain)]; Kept.Add(c.AggregateBy(x => x, 0, (sum, x) => sum + 1).ToArray().Length); return S.Hold(0); });

    // S1403. Add of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Kept.Add(c.Add(new Alike(Plain)).Count); return S.Hold(0); });

    // S1404. Add of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Kept.Add(c.Add(new Ranked(Plain)).Count); return S.Hold(0); });

    // S1405. Contains of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1406. Contains of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Kept.Add(c.Contains(new Ranked(Plain))); return S.Hold(0); });

    // S1407. Remove of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Kept.Add(c.Remove(new Alike(Plain)).Count); return S.Hold(0); });

    // S1408. Remove of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Kept.Add(c.Remove(new Ranked(Plain)).Count); return S.Hold(0); });

    // S1409. TryGetValue of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1410. TryGetValue of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Kept.Add(c.TryGetValue(new Ranked(Plain), out _)); return S.Hold(0); });

    // S1411. IndexOf of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Kept.Add(c.IndexOf(new Ranked(Plain))); return S.Hold(0); });

    // S1412. Except of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetExcept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.Except(other).Count); return S.Hold(0); });

    // S1413. Except of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetExcept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.Except(other).Count); return S.Hold(0); });

    // S1414. Intersect of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetIntersect() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.Intersect(other).Count); return S.Hold(0); });

    // S1415. Intersect of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetIntersect() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.Intersect(other).Count); return S.Hold(0); });

    // S1416. IsProperSubsetOf of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetIsProperSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain), new(S.Hold(0))]; Kept.Add(c.IsProperSubsetOf(other)); return S.Hold(0); });

    // S1417. IsProperSubsetOf of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetIsProperSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain), new(S.Hold(0))]; Kept.Add(c.IsProperSubsetOf(other)); return S.Hold(0); });

    // S1418. IsProperSupersetOf of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetIsProperSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.IsProperSupersetOf(other)); return S.Hold(0); });

    // S1419. IsProperSupersetOf of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetIsProperSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.IsProperSupersetOf(other)); return S.Hold(0); });

    // S1420. IsSubsetOf of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetIsSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.IsSubsetOf(other)); return S.Hold(0); });

    // S1421. IsSubsetOf of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetIsSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.IsSubsetOf(other)); return S.Hold(0); });

    // S1422. IsSupersetOf of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetIsSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.IsSupersetOf(other)); return S.Hold(0); });

    // S1423. IsSupersetOf of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetIsSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.IsSupersetOf(other)); return S.Hold(0); });

    // S1424. Overlaps of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetOverlaps() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.Overlaps(other)); return S.Hold(0); });

    // S1425. Overlaps of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetOverlaps() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.Overlaps(other)); return S.Hold(0); });

    // S1426. SetEquals of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetSetEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.SetEquals(other)); return S.Hold(0); });

    // S1427. SetEquals of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetSetEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.SetEquals(other)); return S.Hold(0); });

    // S1428. SymmetricExcept of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetSymmetricExcept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.SymmetricExcept(other).Count); return S.Hold(0); });

    // S1429. SymmetricExcept of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetSymmetricExcept() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.SymmetricExcept(other).Count); return S.Hold(0); });

    // S1430. Union of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetUnion() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ImmutableHashSet<Alike> c = ImmutableHashSet.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.Union(other).Count); return S.Hold(0); });

    // S1431. Union of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetUnion() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; ImmutableSortedSet<Ranked> c = ImmutableSortedSet.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.Union(other).Count); return S.Hold(0); });

    // S1432. Create of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetCreate() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableHashSet.Create(new Alike(l.Map(v => v)), new Alike(Plain)).Count); return S.Hold(0); });

    // S1433. CreateRange of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetCreateRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] two = [new(l.Map(v => v)), new(Plain)]; Kept.Add(ImmutableHashSet.CreateRange(two).Count); return S.Hold(0); });

    // S1434. ToImmutableHashSet of an ImmutableHashSet.
    public static Cell<int> ImmutableHashSetToImmutableHashSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] two = [new(l.Map(v => v)), new(Plain)]; Kept.Add(two.ToImmutableHashSet().Count); return S.Hold(0); });

    // S1435. Create of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetCreate() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableSortedSet.Create(new Ranked(l.Map(v => v)), new Ranked(Plain)).Count); return S.Hold(0); });

    // S1436. CreateRange of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetCreateRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] two = [new(l.Map(v => v)), new(Plain)]; Kept.Add(ImmutableSortedSet.CreateRange(two).Count); return S.Hold(0); });

    // S1437. ToImmutableSortedSet of an ImmutableSortedSet.
    public static Cell<int> ImmutableSortedSetToImmutableSortedSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] two = [new(l.Map(v => v)), new(Plain)]; Kept.Add(two.ToImmutableSortedSet().Count); return S.Hold(0); });

    // S1438. Add of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.Add(new Alike(Plain), 2).Count); return S.Hold(0); });

    // S1439. AddRange of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryAddRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); KeyValuePair<Alike, int>[] pairs = [new(new Alike(Plain), 2)]; Kept.Add(c.AddRange(pairs).Count); return S.Hold(0); });

    // S1440. Contains of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.Contains(new KeyValuePair<Alike, int>(new Alike(Plain), 1))); return S.Hold(0); });

    // S1441. ContainsKey of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1442. Remove of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.Remove(new Alike(Plain)).Count); return S.Hold(0); });

    // S1443. RemoveRange of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryRemoveRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Alike[] other = [new(Plain)]; Kept.Add(c.RemoveRange(other).Count); return S.Hold(0); });

    // S1444. SetItem of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionarySetItem() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.SetItem(new Alike(Plain), 2).Count); return S.Hold(0); });

    // S1445. SetItems of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionarySetItems() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); KeyValuePair<Alike, int>[] pairs = [new(new Alike(Plain), 2)]; Kept.Add(c.SetItems(pairs).Count); return S.Hold(0); });

    // S1446. TryGetKey of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryTryGetKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.TryGetKey(new Alike(Plain), out _)); return S.Hold(0); });

    // S1447. TryGetValue of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1448. A read by index of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; ImmutableDictionary<Alike, int> c = ImmutableDictionary.CreateRange(one); Kept.Add(c[new Alike(Plain)]); return S.Hold(0); });

    // S1449. ContainsValue of an ImmutableDictionary, for a value.
    public static Cell<int> ImmutableDictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Alike>[] one = [new("a", new Alike(l.Map(v => v)))]; ImmutableDictionary<string, Alike> c = ImmutableDictionary.CreateRange(one); Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1450. CreateRange of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryCreateRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] two = [new(new Alike(l.Map(v => v)), 1), new(new Alike(Plain), 2)]; Kept.Add(ImmutableDictionary.CreateRange(two).Count); return S.Hold(0); });

    // S1451. ToImmutableDictionary of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryToImmutableDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] two = [new(new Alike(l.Map(v => v)), 1), new(new Alike(Plain), 2)]; Kept.Add(two.ToImmutableDictionary().Count); return S.Hold(0); });

    // S1452. Add of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.Add(new Ranked(Plain), 2).Count); return S.Hold(0); });

    // S1453. AddRange of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryAddRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); KeyValuePair<Ranked, int>[] pairs = [new(new Ranked(Plain), 2)]; Kept.Add(c.AddRange(pairs).Count); return S.Hold(0); });

    // S1454. Contains of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.Contains(new KeyValuePair<Ranked, int>(new Ranked(Plain), 1))); return S.Hold(0); });

    // S1455. ContainsKey of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.ContainsKey(new Ranked(Plain))); return S.Hold(0); });

    // S1456. Remove of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.Remove(new Ranked(Plain)).Count); return S.Hold(0); });

    // S1457. RemoveRange of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryRemoveRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Ranked[] other = [new(Plain)]; Kept.Add(c.RemoveRange(other).Count); return S.Hold(0); });

    // S1458. SetItem of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionarySetItem() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.SetItem(new Ranked(Plain), 2).Count); return S.Hold(0); });

    // S1459. SetItems of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionarySetItems() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); KeyValuePair<Ranked, int>[] pairs = [new(new Ranked(Plain), 2)]; Kept.Add(c.SetItems(pairs).Count); return S.Hold(0); });

    // S1460. TryGetKey of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryTryGetKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.TryGetKey(new Ranked(Plain), out _)); return S.Hold(0); });

    // S1461. TryGetValue of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.TryGetValue(new Ranked(Plain), out _)); return S.Hold(0); });

    // S1462. A read by index of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c[new Ranked(Plain)]); return S.Hold(0); });

    // S1463. ContainsValue of an ImmutableSortedDictionary, for a value.
    public static Cell<int> ImmutableSortedDictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Alike>[] one = [new("a", new Alike(l.Map(v => v)))]; ImmutableSortedDictionary<string, Alike> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1464. CreateRange of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryCreateRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] two = [new(new Ranked(l.Map(v => v)), 1), new(new Ranked(Plain), 2)]; Kept.Add(ImmutableSortedDictionary.CreateRange(two).Count); return S.Hold(0); });

    // S1465. ToImmutableSortedDictionary of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryToImmutableSortedDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] two = [new(new Ranked(l.Map(v => v)), 1), new(new Ranked(Plain), 2)]; Kept.Add(two.ToImmutableSortedDictionary().Count); return S.Hold(0); });

    // S1466. ValueRef of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryValueRef() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; ImmutableSortedDictionary<Ranked, int> c = ImmutableSortedDictionary.CreateRange(one); Kept.Add(c.ValueRef(new Ranked(Plain))); return S.Hold(0); });

    // S1467. Contains of an ImmutableList.
    public static Cell<int> ImmutableListContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<Alike> c = ImmutableList.Create(new Alike(l.Map(v => v))); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1468. IndexOf of an ImmutableList.
    public static Cell<int> ImmutableListIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<Alike> c = ImmutableList.Create(new Alike(l.Map(v => v))); Kept.Add(c.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1469. Remove of an ImmutableList.
    public static Cell<int> ImmutableListRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<Alike> c = ImmutableList.Create(new Alike(l.Map(v => v))); Kept.Add(c.Remove(new Alike(Plain)).Count); return S.Hold(0); });

    // S1470. RemoveRange of an ImmutableList.
    public static Cell<int> ImmutableListRemoveRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<Alike> c = ImmutableList.Create(new Alike(l.Map(v => v))); Alike[] other = [new(Plain)]; Kept.Add(c.RemoveRange(other).Count); return S.Hold(0); });

    // S1471. Replace of an ImmutableList.
    public static Cell<int> ImmutableListReplace() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<Alike> c = ImmutableList.Create(new Alike(l.Map(v => v))); Kept.Add(c.Replace(new Alike(Plain), new Alike(Plain)).Count); return S.Hold(0); });

    // S1472. Sort of an ImmutableList.
    public static Cell<int> ImmutableListSort() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableList.Create(new Ranked(l.Map(v => v)), new Ranked(Plain)).Sort().Count); return S.Hold(0); });

    // S1473. Contains of an ImmutableArray.
    public static Cell<int> ImmutableArrayContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<Alike> c = ImmutableArray.Create(new Alike(l.Map(v => v))); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1474. IndexOf of an ImmutableArray.
    public static Cell<int> ImmutableArrayIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<Alike> c = ImmutableArray.Create(new Alike(l.Map(v => v))); Kept.Add(c.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1475. Remove of an ImmutableArray.
    public static Cell<int> ImmutableArrayRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<Alike> c = ImmutableArray.Create(new Alike(l.Map(v => v))); Kept.Add(c.Remove(new Alike(Plain)).Length); return S.Hold(0); });

    // S1476. RemoveRange of an ImmutableArray.
    public static Cell<int> ImmutableArrayRemoveRange() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<Alike> c = ImmutableArray.Create(new Alike(l.Map(v => v))); Alike[] other = [new(Plain)]; Kept.Add(c.RemoveRange(other).Length); return S.Hold(0); });

    // S1477. Replace of an ImmutableArray.
    public static Cell<int> ImmutableArrayReplace() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<Alike> c = ImmutableArray.Create(new Alike(l.Map(v => v))); Kept.Add(c.Replace(new Alike(Plain), new Alike(Plain)).Length); return S.Hold(0); });

    // S1478. Sort of an ImmutableArray.
    public static Cell<int> ImmutableArraySort() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableArray.Create(new Ranked(l.Map(v => v)), new Ranked(Plain)).Sort().Length); return S.Hold(0); });

    // S1479. LastIndexOf of an ImmutableArray.
    public static Cell<int> ImmutableArrayLastIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<Alike> c = ImmutableArray.Create(new Alike(l.Map(v => v))); Kept.Add(c.LastIndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1480. BinarySearch of an ImmutableList.
    public static Cell<int> ImmutableListBinarySearch() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<Ranked> c = ImmutableList.Create(new Ranked(l.Map(v => v))); Kept.Add(c.BinarySearch(new Ranked(Plain))); return S.Hold(0); });

    // S1481. Contains of a FrozenSet.
    public static Cell<int> FrozenSetContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1482. IsProperSubsetOf of a FrozenSet.
    public static Cell<int> FrozenSetIsProperSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Alike[] other = [new(Plain)]; Kept.Add(c.IsProperSubsetOf(other)); return S.Hold(0); });

    // S1483. IsProperSupersetOf of a FrozenSet.
    public static Cell<int> FrozenSetIsProperSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Alike[] other = [new(Plain)]; Kept.Add(c.IsProperSupersetOf(other)); return S.Hold(0); });

    // S1484. IsSubsetOf of a FrozenSet.
    public static Cell<int> FrozenSetIsSubsetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Alike[] other = [new(Plain)]; Kept.Add(c.IsSubsetOf(other)); return S.Hold(0); });

    // S1485. IsSupersetOf of a FrozenSet.
    public static Cell<int> FrozenSetIsSupersetOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Alike[] other = [new(Plain)]; Kept.Add(c.IsSupersetOf(other)); return S.Hold(0); });

    // S1486. Overlaps of a FrozenSet.
    public static Cell<int> FrozenSetOverlaps() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Alike[] other = [new(Plain)]; Kept.Add(c.Overlaps(other)); return S.Hold(0); });

    // S1487. SetEquals of a FrozenSet.
    public static Cell<int> FrozenSetSetEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Alike[] other = [new(Plain)]; Kept.Add(c.SetEquals(other)); return S.Hold(0); });

    // S1488. TryGetValue of a FrozenSet.
    public static Cell<int> FrozenSetTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; FrozenSet<Alike> c = one.ToFrozenSet(); Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1489. ToFrozenSet of a FrozenSet.
    public static Cell<int> FrozenSetToFrozenSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] two = [new(l.Map(v => v)), new(Plain)]; Kept.Add(two.ToFrozenSet().Count); return S.Hold(0); });

    // S1490. ContainsKey of a FrozenDictionary.
    public static Cell<int> FrozenDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; FrozenDictionary<Alike, int> c = one.ToFrozenDictionary(); Kept.Add(c.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1491. TryGetValue of a FrozenDictionary.
    public static Cell<int> FrozenDictionaryTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; FrozenDictionary<Alike, int> c = one.ToFrozenDictionary(); Kept.Add(c.TryGetValue(new Alike(Plain), out _)); return S.Hold(0); });

    // S1492. A read by index of a FrozenDictionary.
    public static Cell<int> FrozenDictionaryRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; FrozenDictionary<Alike, int> c = one.ToFrozenDictionary(); Kept.Add(c[new Alike(Plain)]); return S.Hold(0); });

    // S1493. GetValueRefOrNullRef of a FrozenDictionary.
    public static Cell<int> FrozenDictionaryGetValueRefOrNullRef() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; FrozenDictionary<Alike, int> c = one.ToFrozenDictionary(); Kept.Add(c.GetValueRefOrNullRef(new Alike(Plain))); return S.Hold(0); });

    // S1494. ToFrozenDictionary of a FrozenDictionary.
    public static Cell<int> FrozenDictionaryToFrozenDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] two = [new(new Alike(l.Map(v => v)), 1), new(new Alike(Plain), 2)]; Kept.Add(two.ToFrozenDictionary().Count); return S.Hold(0); });

    // S1495. Contains of an ArrayList.
    public static Cell<int> ArrayListContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList c = new() { new Alike(l.Map(v => v)) }; Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1496. IndexOf of an ArrayList.
    public static Cell<int> ArrayListIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList c = new() { new Alike(l.Map(v => v)) }; Kept.Add(c.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1497. LastIndexOf of an ArrayList.
    public static Cell<int> ArrayListLastIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList c = new() { new Alike(l.Map(v => v)) }; Kept.Add(c.LastIndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1498. Remove of an ArrayList.
    public static Cell<int> ArrayListRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList c = new() { new Alike(l.Map(v => v)) }; c.Remove(new Alike(Plain)); return S.Hold(0); });

    // S1499. Add of a Hashtable.
    public static Cell<int> HashtableAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { new Alike(l.Map(v => v)), 1 } }; c.Add(new Alike(Plain), 2); return S.Hold(0); });

    // S1500. Contains of a Hashtable.
    public static Cell<int> HashtableContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1501. ContainsKey of a Hashtable.
    public static Cell<int> HashtableContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1502. Remove of a Hashtable.
    public static Cell<int> HashtableRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { new Alike(l.Map(v => v)), 1 } }; c.Remove(new Alike(Plain)); return S.Hold(0); });

    // S1503. A read by index of a Hashtable.
    public static Cell<int> HashtableRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(c[new Alike(Plain)] != null); return S.Hold(0); });

    // S1504. An assignment by index of a Hashtable.
    public static Cell<int> HashtableAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { new Alike(l.Map(v => v)), 1 } }; c[new Alike(Plain)] = 2; return S.Hold(0); });

    // S1505. ContainsValue of a Hashtable, for a value.
    public static Cell<int> HashtableContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { Hashtable c = new() { { "a", new Alike(l.Map(v => v)) } }; Kept.Add(c.ContainsValue(new Alike(Plain))); return S.Hold(0); });

    // S1506. GetHashCode of EqualityComparer.
    public static Cell<int> EqualityComparerGetHashCode() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(EqualityComparer<Hashed>.Default.GetHashCode(new Hashed(l.Map(v => v)))); return S.Hold(0); });

    // S1507. Combine of HashCode.
    public static Cell<int> HashCodeCombine() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(HashCode.Combine(new Hashed(l.Map(v => v)), 1)); return S.Hold(0); });

    // S1508. The constructor of a Dictionary, with one object. It compares nothing.
    public static Cell<int> DictionaryConstructorOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; Kept.Add(new Dictionary<Alike, int>(one).Count); return S.Hold(0); });

    // S1509. The constructor of an OrderedDictionary, with one object. It compares nothing.
    public static Cell<int> OrderedDictionaryConstructorOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; Kept.Add(new OrderedDictionary<Alike, int>(one).Count); return S.Hold(0); });

    // S1510. The constructor of a ConcurrentDictionary, with one object. It compares nothing.
    public static Cell<int> ConcurrentDictionaryConstructorOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; Kept.Add(new ConcurrentDictionary<Alike, int>(one).Count); return S.Hold(0); });

    // S1511. Create of an ImmutableHashSet, with one object. It compares nothing.
    public static Cell<int> ImmutableHashSetCreateOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableHashSet.Create(new Alike(l.Map(v => v))).Count); return S.Hold(0); });

    // S1512. CreateRange of an ImmutableHashSet, with one object. It compares nothing.
    public static Cell<int> ImmutableHashSetCreateRangeOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Kept.Add(ImmutableHashSet.CreateRange(one).Count); return S.Hold(0); });

    // S1513. ToImmutableHashSet of an ImmutableHashSet, with one object. It compares nothing.
    public static Cell<int> ImmutableHashSetToImmutableHashSetOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Kept.Add(one.ToImmutableHashSet().Count); return S.Hold(0); });

    // S1514. Create of an ImmutableSortedSet, with one object. It compares nothing.
    public static Cell<int> ImmutableSortedSetCreateOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableSortedSet.Create(new Ranked(l.Map(v => v))).Count); return S.Hold(0); });

    // S1515. CreateRange of an ImmutableSortedSet, with one object. It compares nothing.
    public static Cell<int> ImmutableSortedSetCreateRangeOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Kept.Add(ImmutableSortedSet.CreateRange(one).Count); return S.Hold(0); });

    // S1516. ToImmutableSortedSet of an ImmutableSortedSet, with one object. It compares nothing.
    public static Cell<int> ImmutableSortedSetToImmutableSortedSetOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] one = [new(l.Map(v => v))]; Kept.Add(one.ToImmutableSortedSet().Count); return S.Hold(0); });

    // S1517. CreateRange of an ImmutableDictionary, with one object. It compares nothing.
    public static Cell<int> ImmutableDictionaryCreateRangeOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; Kept.Add(ImmutableDictionary.CreateRange(one).Count); return S.Hold(0); });

    // S1518. ToImmutableDictionary of an ImmutableDictionary, with one object. It compares nothing.
    public static Cell<int> ImmutableDictionaryToImmutableDictionaryOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; Kept.Add(one.ToImmutableDictionary().Count); return S.Hold(0); });

    // S1519. CreateRange of an ImmutableSortedDictionary, with one object. It compares nothing.
    public static Cell<int> ImmutableSortedDictionaryCreateRangeOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; Kept.Add(ImmutableSortedDictionary.CreateRange(one).Count); return S.Hold(0); });

    // S1520. ToImmutableSortedDictionary of an ImmutableSortedDictionary, with one object. It compares nothing.
    public static Cell<int> ImmutableSortedDictionaryToImmutableSortedDictionaryOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Ranked, int>[] one = [new(new Ranked(l.Map(v => v)), 1)]; Kept.Add(one.ToImmutableSortedDictionary().Count); return S.Hold(0); });

    // S1521. ToFrozenSet of a FrozenSet, with one object. It compares nothing.
    public static Cell<int> FrozenSetToFrozenSetOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Kept.Add(one.ToFrozenSet().Count); return S.Hold(0); });

    // S1522. ToFrozenDictionary of a FrozenDictionary, with one object. It compares nothing.
    public static Cell<int> FrozenDictionaryToFrozenDictionaryOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<Alike, int>[] one = [new(new Alike(l.Map(v => v)), 1)]; Kept.Add(one.ToFrozenDictionary().Count); return S.Hold(0); });

    // S1523. CountBy of Enumerable, with one object. It compares nothing.
    public static Cell<int> EnumerableCountByOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Kept.Add(one.CountBy(x => x).ToArray().Length); return S.Hold(0); });

    // S1524. AggregateBy of Enumerable, with one object. It compares nothing.
    public static Cell<int> EnumerableAggregateByOfOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; Kept.Add(one.AggregateBy(x => x, 0, (sum, x) => sum + 1).ToArray().Length); return S.Hold(0); });

    // S1525. IndexOfAny of a span with two values, for a type that is IEquatable. Each value gets the call, and the element is pending.
    public static Cell<int> SpanIndexOfAnyOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAny(new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1526. IndexOfAny with three values. The element is pending.
    public static Cell<int> SpanIndexOfAnyOfThreeValuesWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.IndexOfAny(new EquatableOwn(Plain), new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1527. IndexOfAny with three values. The first value is pending, and it gets the call.
    public static Cell<int> SpanIndexOfAnyOfThreeValuesWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; Kept.Add(all.IndexOfAny(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1528. IndexOfAny with a span of values is a different overload, and the element gets the call on .NET 10. The element is pending.
    public static Cell<int> SpanIndexOfAnyOfASpanOfValuesWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain), new(Plain)]; Kept.Add(all.IndexOfAny(other)); return S.Hold(0); });

    // S1529. LastIndexOfAny of a span with two values, for a type that is IEquatable. Each value gets the call, and the element is pending.
    public static Cell<int> SpanLastIndexOfAnyOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAny(new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1530. LastIndexOfAny with three values. The element is pending.
    public static Cell<int> SpanLastIndexOfAnyOfThreeValuesWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.LastIndexOfAny(new EquatableOwn(Plain), new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1531. LastIndexOfAny with three values. The first value is pending, and it gets the call.
    public static Cell<int> SpanLastIndexOfAnyOfThreeValuesWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; Kept.Add(all.LastIndexOfAny(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1532. LastIndexOfAny with a span of values is a different overload, and the element gets the call on .NET 10. The element is pending.
    public static Cell<int> SpanLastIndexOfAnyOfASpanOfValuesWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain), new(Plain)]; Kept.Add(all.LastIndexOfAny(other)); return S.Hold(0); });

    // S1533. ContainsAny of a span with two values, for a type that is IEquatable. Each value gets the call, and the element is pending.
    public static Cell<int> SpanContainsAnyOfAnEquatableWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.ContainsAny(new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1534. ContainsAny with three values. The element is pending.
    public static Cell<int> SpanContainsAnyOfThreeValuesWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; Kept.Add(all.ContainsAny(new EquatableOwn(Plain), new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1535. ContainsAny with three values. The first value is pending, and it gets the call.
    public static Cell<int> SpanContainsAnyOfThreeValuesWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; Kept.Add(all.ContainsAny(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain), new EquatableOwn(Plain))); return S.Hold(0); });

    // S1536. ContainsAny with a span of values is a different overload, and the element gets the call on .NET 10. The element is pending.
    public static Cell<int> SpanContainsAnyOfASpanOfValuesWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(l.Map(v => v))]; EquatableOwn[] other = [new(Plain), new(Plain)]; Kept.Add(all.ContainsAny(other)); return S.Hold(0); });

    // S1537. Replace of a span compares the old value with each element. The new value is pending, and gets no call.
    public static Cell<int> SpanReplaceOfAnEquatableWhereTheNewValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; all.Replace(new EquatableOwn(Plain), new EquatableOwn(l.Map(v => v))); return S.Hold(0); });

    // S1538. The same for a type with only Equals of object.
    public static Cell<int> SpanReplaceOfAnObjectWhereTheNewValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] all = [new(Plain)]; all.Replace(new AlikeOwn(Plain), new AlikeOwn(l.Map(v => v))); return S.Hold(0); });

    // S1539. Replace of an ImmutableList. The new value is pending, and gets no call.
    public static Cell<int> ImmutableListReplaceWhereTheNewValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<AlikeOwn> c = ImmutableList.Create(new AlikeOwn(Plain)); Kept.Add(c.Replace(new AlikeOwn(Plain), new AlikeOwn(l.Map(v => v))).Count); return S.Hold(0); });

    // S1540. Replace of an ImmutableArray. The new value is pending, and gets no call.
    public static Cell<int> ImmutableArrayReplaceWhereTheNewValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<AlikeOwn> c = ImmutableArray.Create(new AlikeOwn(Plain)); Kept.Add(c.Replace(new AlikeOwn(Plain), new AlikeOwn(l.Map(v => v))).Length); return S.Hold(0); });

    // S1541. Replace of a span where the old value is pending, and it gets the call.
    public static Cell<int> SpanReplaceOfAnEquatableWhereTheOldValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { EquatableOwn[] all = [new(Plain)]; all.Replace(new EquatableOwn(l.Map(v => v)), new EquatableOwn(Plain)); return S.Hold(0); });

    // S1542. A list that is an element of a tuple gets a pending cell. An element of a tuple is a member of the platform.
    public static Cell<int> ListInAnElementOfATupleGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { (List<Cell<int>> First, List<Cell<int>> Second) pair = (new(), new()); pair.First.Add(l.Map(v => v)); return S.Hold(pair.First[0].Sample()); });

    // S1543. The other list of the same tuple is plain.
    public static Cell<int> OtherListOfATupleIsPlain() =>
        Cell.Loop<int>().WithoutCaptures(l => { (List<Cell<int>> First, List<Cell<int>> Second) pair = (new(), new()); pair.First.Add(l.Map(v => v)); pair.Second.Add(Plain); return S.Hold(pair.Second[0].Sample()); });

    // S1544. A list that is an element of a tuple with no names. Item1 is a member of the platform.
    public static Cell<int> ListInItem1OfATupleGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { (List<Cell<int>>, int) pair = (new(), 1); pair.Item1.Add(l.Map(v => v)); return S.Hold(pair.Item1[0].Sample()); });

    // S1545. A list that is the value of a KeyValuePair. Value is a property of the platform.
    public static Cell<int> ListInTheValueOfAPairGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, List<Cell<int>>> pair = new("a", new()); pair.Value.Add(l.Map(v => v)); return S.Hold(pair.Value[0].Sample()); });

    // S1546. A comparer in a local that only gets null is no comparer. Sort then uses the default one, which calls CompareTo.
    public static Cell<int> ListSortWithANullComparerInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? none = null; all.Sort(none); return S.Hold(all.Count); });

    // S1547. The same with the default literal, for Array.Sort.
    public static Cell<int> ArraySortWithADefaultComparerInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? none = default; Array.Sort(all, none); return S.Hold(all.Length); });

    // S1548. A local with no value in its declaration, and an assignment of null after it.
    public static Cell<int> ListSortWithANullComparerAssignedLater() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? none; none = null; all.Sort(none); return S.Hold(all.Count); });

    // S1549. A local that gets null with the operator that says not null.
    public static Cell<int> ListSortWithANullComparerInALocalThatSaysNotNull() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked> none = null!; all.Sort(none); return S.Hold(all.Count); });

    // S1550. A null comparer in a local for the constructor of a HashSet.
    public static Cell<int> HashSetWithANullComparerInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] all = [new(l.Map(v => v)), new(Plain)]; IEqualityComparer<Alike>? none = null; return S.Hold(new HashSet<Alike>(all, none).Count); });

    // S1551. A null comparer in a local for the keys of OrderBy.
    public static Cell<int> OrderByWithANullComparerInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Ranked[] all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? none = null; return S.Hold(all.OrderBy(x => x, none).ToArray().Length); });

    // S1552. A constant local that is null.
    public static Cell<int> ListSortWithAConstantNullComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; const IComparer<Ranked>? none = null; all.Sort(none); return S.Hold(all.Count); });

    // S1553. A null comparer in a local for the constructor of a SortedSet, and an Add after it.
    public static Cell<int> SortedSetWithANullComparerInALocalThenAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { IComparer<Ranked>? none = null; SortedSet<Ranked> set = new(none); set.Add(new(l.Map(v => v))); set.Add(new(Plain)); return S.Hold(set.Count); });

    // S1554. A local that gets null, and then a comparer that reads nothing. Sort calls the comparer, and not CompareTo.
    public static Cell<int> ListSortWithALocalThatGetsAComparerAfterNull() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? some = null; some = Comparer<Ranked>.Create((x, y) => 0); all.Sort(some); return S.Hold(all.Count); });

    // S1555. A local that gets null, and a comparer in a branch that runs.
    public static Cell<int> ListSortWithALocalThatGetsAComparerInABranch() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? some = null; if (all.Count > 0) { some = Comparer<Ranked>.Create((x, y) => 0); } all.Sort(some); return S.Hold(all.Count); });

    // S1556. A local that gets null, and a comparer through a ref argument.
    public static Cell<int> ListSortWithALocalThatGetsAComparerByRef() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? some = null; GiveAComparer(ref some); all.Sort(some); return S.Hold(all.Count); });

    // S1557. A local that gets null, and a comparer from a lambda.
    public static Cell<int> ListSortWithALocalThatGetsAComparerInALambda() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? some = null; Action give = () => some = Comparer<Ranked>.Create((x, y) => 0); give(); all.Sort(some); return S.Hold(all.Count); });

    // S1558. A local that gets null, and a comparer through the assignment that keeps a value that is not null.
    public static Cell<int> ListSortWithALocalThatGetsAComparerByCoalescing() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? some = null; some ??= Comparer<Ranked>.Create((x, y) => 0); all.Sort(some); return S.Hold(all.Count); });

    // S1559. Add through ISet, on a HashSet that the local got. The set calls Equals.
    public static Cell<int> AddThroughISetOfAHashSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { ISet<Alike> set = new HashSet<Alike>(); set.Add(new Alike(l.Map(v => v))); set.Add(new Alike(Plain)); return S.Hold(set.Count); });

    // S1560. Add through ISet, on a SortedSet. The set calls CompareTo.
    public static Cell<int> AddThroughISetOfASortedSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { ISet<Ranked> set = new SortedSet<Ranked>(); set.Add(new Ranked(l.Map(v => v))); set.Add(new Ranked(Plain)); return S.Hold(set.Count); });

    // S1561. Add through IDictionary, on a Dictionary.
    public static Cell<int> AddThroughIDictionaryOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary<Alike, int> map = new Dictionary<Alike, int>(); map.Add(new Alike(l.Map(v => v)), 1); map.Add(new Alike(Plain), 2); return S.Hold(map.Count); });

    // S1562. The setter of the indexer through IDictionary, on a Dictionary.
    public static Cell<int> IndexerThroughIDictionaryOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary<Alike, int> map = new Dictionary<Alike, int>(); map[new Alike(l.Map(v => v))] = 1; map[new Alike(Plain)] = 2; return S.Hold(map.Count); });

    // S1563. Add through ICollection, on a HashSet. The set has that member as an explicit implementation.
    public static Cell<int> AddThroughICollectionOfAHashSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = new HashSet<Alike>(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1564. Add through ICollection, on a List. A list compares nothing when it adds.
    public static Cell<int> AddThroughICollectionOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = new List<Alike> { new(l.Map(v => v)) }; all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1565. Contains through ICollection, on a List.
    public static Cell<int> ContainsThroughICollectionOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = new List<Alike> { new(l.Map(v => v)) }; Kept.Add(all.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1566. Contains through ICollection, on a List, where the value is pending and Equals reads only its own object. The element gets the call.
    public static Cell<int> ContainsThroughICollectionOfAListWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<AlikeOwn> all = new List<AlikeOwn> { new(Plain) }; Kept.Add(all.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1567. IndexOf through IList, on a List.
    public static Cell<int> IndexOfThroughIListOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { IList<Alike> all = new List<Alike> { new(l.Map(v => v)) }; Kept.Add(all.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1568. IndexOf through IList, on an array. The compiler does not say which method of an array that is, and the row of the interface holds.
    public static Cell<int> IndexOfThroughIListOfAnArray() =>
        Cell.Loop<int>().WithoutCaptures(l => { IList<Alike> all = new Alike[] { new(l.Map(v => v)) }; Kept.Add(all.IndexOf(new Alike(Plain))); return S.Hold(0); });

    // S1569. ContainsKey through IReadOnlyDictionary, on a Dictionary.
    public static Cell<int> ContainsKeyThroughIReadOnlyDictionaryOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { IReadOnlyDictionary<Alike, int> map = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(map.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1570. Contains through the IList with no type argument, on an ArrayList.
    public static Cell<int> ContainsThroughIListOfAnArrayList() =>
        Cell.Loop<int>().WithoutCaptures(l => { IList all = new ArrayList { new Alike(l.Map(v => v)) }; Kept.Add(all.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1571. Add through the IDictionary with no type argument, on a Hashtable.
    public static Cell<int> AddThroughIDictionaryOfAHashtable() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary table = new Hashtable(); table.Add(new Alike(l.Map(v => v)), 1); table.Add(new Alike(Plain), 2); return S.Hold(table.Count); });

    // S1572. Add through a cast to ISet, on a local that is a HashSet.
    public static Cell<int> AddThroughACastToISet() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = new(); ((ISet<Alike>)set).Add(new Alike(l.Map(v => v))); ((ISet<Alike>)set).Add(new Alike(Plain)); return S.Hold(set.Count); });

    // S1573. Contains through ILookup, on what ToLookup gives.
    public static Cell<int> ContainsThroughILookup() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] one = [new(l.Map(v => v))]; ILookup<Alike, Alike> by = one.ToLookup(x => x); Kept.Add(by.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1574. Add through ISet, on a set that a method gives.
    public static Cell<int> AddThroughISetFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { ISet<Alike> set = MakeASet(); set.Add(new Alike(l.Map(v => v))); set.Add(new Alike(Plain)); return S.Hold(set.Count); });

    // S1575. Contains through ICollection, in a local function that gets the collection.
    public static Cell<int> ContainsThroughICollectionInALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l => { static bool Has(ICollection<Alike> all, Alike value) => all.Contains(value); Kept.Add(Has(new HashSet<Alike> { new(l.Map(v => v)) }, new Alike(Plain))); return S.Hold(0); });

    // S1576. TryGetValue through IDictionary, on a dictionary that a method gives.
    public static Cell<int> TryGetValueThroughIDictionaryFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary<Alike, int> map = MakeAMap(); map[new Alike(l.Map(v => v))] = 1; Kept.Add(map.TryGetValue(new Alike(Plain), out int found) ? found : 0); return S.Hold(0); });

    // S1577. Remove through ICollection, on a collection that a method gives.
    public static Cell<int> RemoveThroughICollectionFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeASet(); all.Add(new Alike(l.Map(v => v))); Kept.Add(all.Remove(new Alike(Plain))); return S.Hold(0); });

    // S1578. Overlaps through IReadOnlySet, on a set that a method gives.
    public static Cell<int> OverlapsThroughIReadOnlySetFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { IReadOnlySet<Alike> set = MakeAReadOnlySet(new Alike(l.Map(v => v))); Kept.Add(set.Overlaps([new Alike(Plain)])); return S.Hold(0); });

    // S1579. Add through ICollection of a List that a method gives. No row: a list compares nothing when it adds.
    public static Cell<int> AddThroughICollectionFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeAList(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1580. RemoveWhere of a HashSet gets a function, and compares each element that it removes. With one element, it compares that element with itself.
    public static Cell<int> RemoveWhereOfAHashSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = [new(l.Map(v => v))]; Kept.Add(set.RemoveWhere(x => true)); return S.Hold(0); });

    // S1581. RemoveWhere of a SortedSet calls CompareTo.
    public static Cell<int> RemoveWhereOfASortedSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<Ranked> set = [new(l.Map(v => v))]; Kept.Add(set.RemoveWhere(x => true)); return S.Hold(0); });

    // S1582. RemoveWhere of a HashSet of plain objects reads nothing.
    public static Cell<int> RemoveWhereOfAHashSetOfPlainObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Alike> set = [new(Plain)]; Kept.Add(set.RemoveWhere(x => true)); return S.Hold(0); });

    // S1583. Remove of a PriorityQueue compares the element that it gets with each element of the queue.
    public static Cell<int> RemoveOfAPriorityQueue() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Alike, int)[] one = [(new Alike(l.Map(v => v)), 1)]; PriorityQueue<Alike, int> queue = new(one); Kept.Add(queue.Remove(new Alike(Plain), out _, out _)); return S.Hold(0); });

    // S1584. Remove of a PriorityQueue compares the elements, and not the priorities. The two types are
    // different: a state does not tell an element from a priority of the same type.
    public static Cell<int> RemoveOfAPriorityQueueWithAPendingPriority() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Alike, AlikeOwn)[] one = [(new Alike(Plain), new AlikeOwn(l.Map(v => v)))]; PriorityQueue<Alike, AlikeOwn> queue = new(one); Kept.Add(queue.Remove(new Alike(Plain), out _, out _)); return S.Hold(0); });

    // S1585. Equals of a Tuple object calls Equals of each element.
    public static Cell<int> EqualsOfATupleObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Tuple.Create(new Alike(l.Map(v => v)), 1).Equals(Tuple.Create(new Alike(Plain), 1))); return S.Hold(0); });

    // S1586. GetHashCode of a Tuple object calls GetHashCode of each element.
    public static Cell<int> GetHashCodeOfATupleObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Tuple.Create(new Hashed(l.Map(v => v)), 1).GetHashCode()); return S.Hold(0); });

    // S1587. CompareTo of a Tuple object, through IComparable, calls CompareTo of each element. It needs
    // IComparable with no type argument.
    public static Cell<int> CompareToOfATupleObjectThroughAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { IComparable first = Tuple.Create(new RankedLoose(l.Map(v => v)), 1); Kept.Add(first.CompareTo(Tuple.Create(new RankedLoose(Plain), 1))); return S.Hold(0); });

    // S1588. A HashSet of Tuple objects calls Equals of the elements of each tuple.
    public static Cell<int> HashSetOfTupleObjects() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<Tuple<Alike, int>> set = [Tuple.Create(new Alike(l.Map(v => v)), 1)]; set.Add(Tuple.Create(new Alike(Plain), 1)); return S.Hold(set.Count); });

    // S1589. TryUpdate of a ConcurrentDictionary compares the value that it has with the value for the comparison.
    public static Cell<int> TryUpdateComparesTheValues() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Alike>[] one = [new("a", new Alike(l.Map(v => v)))]; ConcurrentDictionary<string, Alike> map = new(one); Kept.Add(map.TryUpdate("a", new Alike(Plain), new Alike(Plain))); return S.Hold(0); });

    // S1590. TryUpdate only keeps the new value, and does not compare it.
    public static Cell<int> TryUpdateKeepsTheNewValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, Alike>[] one = [new("a", new Alike(Plain))]; ConcurrentDictionary<string, Alike> map = new(one); Kept.Add(map.TryUpdate("a", new Alike(l.Map(v => v)), new Alike(Plain))); return S.Hold(0); });

    // S1591. Add of a HashCode calls GetHashCode of the value.
    public static Cell<int> AddOfAHashCode() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashCode code = new(); code.Add(new Hashed(l.Map(v => v))); return S.Hold(code.ToHashCode()); });

    // S1592. Sort of an ArrayList calls CompareTo of IComparable with no type argument.
    public static Cell<int> SortOfAnArrayList() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList all = new() { new RankedLoose(l.Map(v => v)), new RankedLoose(Plain) }; all.Sort(); return S.Hold(all.Count); });

    // S1593. BinarySearch of an ArrayList.
    public static Cell<int> BinarySearchOfAnArrayList() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList all = new() { new RankedLoose(l.Map(v => v)) }; Kept.Add(all.BinarySearch(new RankedLoose(Plain))); return S.Hold(0); });

    // S1594. The constructor of a Hashtable calls GetHashCode of each key of the dictionary that it gets.
    public static Cell<int> HashtableFromADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedList<Hashed, int> from = new() { { new Hashed(l.Map(v => v)), 1 } }; return S.Hold(new Hashtable(from).Count); });

    // S1595. Contains of a LinkedList gives the call to the element. The value is pending, and Equals reads only its own object.
    public static Cell<int> LinkedListContainsWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] one = [new(Plain)]; LinkedList<AlikeOwn> list = new(one); Kept.Add(list.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1596. Contains of a LinkedList, where the element is pending.
    public static Cell<int> LinkedListContainsWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] one = [new(l.Map(v => v))]; LinkedList<AlikeOwn> list = new(one); Kept.Add(list.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1597. Contains of an ImmutableList gives the call to the value.
    public static Cell<int> ImmutableListContainsWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<AlikeOwn> list = ImmutableList.Create(new AlikeOwn(Plain)); Kept.Add(list.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1598. Contains of an ImmutableList, where the element is pending.
    public static Cell<int> ImmutableListContainsWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<AlikeOwn> list = ImmutableList.Create(new AlikeOwn(l.Map(v => v))); Kept.Add(list.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1599. Contains of a SortedSet gives the call to the value. The element is pending, and CompareTo reads only its own object.
    public static Cell<int> SortedSetContainsWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(l.Map(v => v))]; Kept.Add(set.Contains(new RankedOwn(Plain))); return S.Hold(0); });

    // S1600. ContainsKey of a ConcurrentDictionary gives the call to the key that it has.
    public static Cell<int> ConcurrentDictionaryContainsKeyWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<AlikeOwn, int>[] one = [new(new AlikeOwn(Plain), 1)]; ConcurrentDictionary<AlikeOwn, int> map = new(one); Kept.Add(map.ContainsKey(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1601. Contains of a FrozenSet gives the call to the value.
    public static Cell<int> FrozenSetContainsWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] one = [new(l.Map(v => v))]; FrozenSet<AlikeOwn> set = one.ToFrozenSet(); Kept.Add(set.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1602. Remove of a PriorityQueue gives the call to the element that it gets.
    public static Cell<int> PriorityQueueRemoveWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { (AlikeOwn, int)[] one = [(new AlikeOwn(l.Map(v => v)), 1)]; PriorityQueue<AlikeOwn, int> queue = new(one); Kept.Add(queue.Remove(new AlikeOwn(Plain), out _, out _)); return S.Hold(0); });

    // S1603. Equals of a Tuple object gives the call to the elements of that tuple.
    public static Cell<int> TupleObjectEqualsWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(Tuple.Create(new AlikeOwn(Plain), 1).Equals(Tuple.Create(new AlikeOwn(l.Map(v => v)), 1))); return S.Hold(0); });

    // S1604. A queue of pairs: the object that gets no call is the priority in the pair, and CompareTo reads it.
    public static Cell<int> PriorityQueueEnqueueDequeueReadsThePriorityInAPair() =>
        Cell.Loop<int>().WithoutCaptures(l => { (int, Ranked)[] one = [(1, new Ranked(l.Map(v => v)))]; PriorityQueue<int, Ranked> queue = new(one); Kept.Add(queue.EnqueueDequeue(2, new Ranked(Plain))); return S.Hold(0); });

    // S1605. A comparer of the platform in a local, which reads nothing. Sort calls it, and not CompareTo.
    public static Cell<int> ListSortWithAComparerInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked> some = Comparer<Ranked>.Create((x, y) => 0); all.Sort(some); return S.Hold(all.Count); });

    // S1606. Contains through the IDictionary with no type argument, on a Dictionary. The method of the Dictionary has no row of its own, and the row of the interface holds.
    public static Cell<int> ContainsThroughIDictionaryOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary map = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(map.Contains(new Alike(Plain))); return S.Hold(0); });

    // S1607. Sort of an ArrayList with one element compares nothing.
    public static Cell<int> SortOfAnArrayListWithOneElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { ArrayList all = [new RankedLoose(l.Map(v => v))]; all.Sort(); return S.Hold(all.Count); });

    // S1608. Add through IImmutableSet, on a set that a method gives.
    public static Cell<int> AddThroughIImmutableSetFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { IImmutableSet<Alike> set = MakeAnImmutableSet(new Alike(l.Map(v => v))); Kept.Add(set.Add(new Alike(Plain))); return S.Hold(0); });

    // S1609. Add through IImmutableDictionary, on a dictionary that a method gives.
    public static Cell<int> AddThroughIImmutableDictionaryFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { IImmutableDictionary<Alike, int> map = MakeAnImmutableMap(new Alike(l.Map(v => v))); Kept.Add(map.Add(new Alike(Plain), 2)); return S.Hold(0); });

    // S1610. ContainsKey through IReadOnlyDictionary, on a dictionary that a method gives.
    public static Cell<int> ContainsKeyThroughIReadOnlyDictionaryFromAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { IReadOnlyDictionary<Alike, int> map = MakeAReadOnlyMap(new Alike(l.Map(v => v))); Kept.Add(map.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1611. Add through ICollection, on a set that a helper makes. Each return of the helper makes a HashSet, thus the call is Add of a HashSet.
    public static Cell<int> AddThroughICollectionOfASetFromAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeASetAsACollection(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1612. The same, with a helper that keeps the set in a local and returns the local.
    public static Cell<int> AddThroughICollectionOfASetFromAHelperWithALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeASetInALocal(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1613. The same, with a helper that returns a set in two places.
    public static Cell<int> AddThroughICollectionOfASetFromAHelperWithTwoReturns() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeASetInTwoPlaces(early: false); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1614. A helper that returns the collection that it gets. The call has the type of the argument.
    public static Cell<int> AddThroughICollectionOfASetThroughAHelperThatReturnsItsParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = TheSame(new HashSet<Alike>()); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1615. A helper that returns a set or a list. The list returns, and a list adds with no comparison.
    public static Cell<int> AddThroughICollectionOfASetOrAListFromAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeASetOrAList(set: false); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1616. A virtual helper. The override returns a list, thus the type of the base method does not hold.
    public static Cell<int> AddThroughICollectionFromAnOverriddenHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { CollectionMaker maker = new ListMaker(); ICollection<Alike> all = maker.Make(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1617. A helper that gives its parameter a list before it returns it.
    public static Cell<int> AddThroughICollectionOfAHelperThatChangesItsParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = AListInstead(new HashSet<Alike>()); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1618. A local function that makes the set.
    public static Cell<int> AddThroughICollectionOfASetFromALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l => { static ICollection<Alike> Make() => new HashSet<Alike>(); ICollection<Alike> all = Make(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1619. Contains through ICollection, on a list that a helper makes. A list gives the call to its element, and the value that the code gives is pending.
    public static Cell<int> ContainsThroughICollectionOfAListFromAHelperWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<AlikeOwn> all = MakeAnOwnList(new AlikeOwn(Plain)); Kept.Add(all.Contains(new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1620. The same, where the element of the list is pending.
    public static Cell<int> ContainsThroughICollectionOfAListFromAHelperWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<AlikeOwn> all = MakeAnOwnList(new AlikeOwn(l.Map(v => v))); Kept.Add(all.Contains(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1621. Add through ISet, on a set from a helper that returns one of two types. The row of the interface holds.
    public static Cell<int> AddThroughISetOfAnUnknownType() =>
        Cell.Loop<int>().WithoutCaptures(l => { ISet<Alike> set = MakeAHashSetOrASortedSet(sorted: false); set.Add(new Alike(l.Map(v => v))); set.Add(new Alike(Plain)); return S.Hold(set.Count); });

    // S1622. TryGetValue through IDictionary, on a dictionary of a type that the analyzer cannot know.
    public static Cell<int> TryGetValueThroughIDictionaryOfAnUnknownType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IDictionary<Alike, int> map = MakeADictionaryOrASortedOne(sorted: false); map[new Alike(l.Map(v => v))] = 1; Kept.Add(map.TryGetValue(new Alike(Plain), out int found) ? found : 0); return S.Hold(0); });

    // S1623. Overlaps through IReadOnlySet, on a set of a type that the analyzer cannot know.
    public static Cell<int> OverlapsThroughIReadOnlySetOfAnUnknownType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IReadOnlySet<Alike> set = MakeAReadOnlySetOfOneOfTwoTypes(new Alike(l.Map(v => v)), sorted: false); Kept.Add(set.Overlaps([new Alike(Plain)])); return S.Hold(0); });

    // S1624. Add through IImmutableSet, on a set of a type that the analyzer cannot know.
    public static Cell<int> AddThroughIImmutableSetOfAnUnknownType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IImmutableSet<Alike> set = MakeAnImmutableSetOfOneOfTwoTypes(new Alike(l.Map(v => v)), sorted: false); Kept.Add(set.Add(new Alike(Plain))); return S.Hold(0); });

    // S1625. Add through IImmutableDictionary, on a dictionary of a type that the analyzer cannot know.
    public static Cell<int> AddThroughIImmutableDictionaryOfAnUnknownType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IImmutableDictionary<Alike, int> map = MakeAnImmutableMapOfOneOfTwoTypes(new Alike(l.Map(v => v)), sorted: false); Kept.Add(map.Add(new Alike(Plain), 2)); return S.Hold(0); });

    // S1626. ContainsKey through IReadOnlyDictionary, on a dictionary of a type that the analyzer cannot know.
    public static Cell<int> ContainsKeyThroughIReadOnlyDictionaryOfAnUnknownType() =>
        Cell.Loop<int>().WithoutCaptures(l => { IReadOnlyDictionary<Alike, int> map = MakeAReadOnlyMapOfOneOfTwoTypes(new Alike(l.Map(v => v)), sorted: false); Kept.Add(map.ContainsKey(new Alike(Plain))); return S.Hold(0); });

    // S1627. An override that a type can override again. The call can run a different body, thus the type of this one does not hold.
    public static Cell<int> AddThroughICollectionFromAnOverrideThatCanBeOverridden() =>
        Cell.Loop<int>().WithoutCaptures(l => { MiddleMaker maker = new LastMaker(); ICollection<Alike> all = maker.Make(); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1628. A helper with two returns of different types. The list returns, and the set is in the last return.
    public static Cell<int> AddThroughICollectionOfAListOrASetInTwoPlaces() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<Alike> all = MakeAListOrASetInTwoPlaces(list: true); all.Add(new Alike(l.Map(v => v))); all.Add(new Alike(Plain)); return S.Hold(all.Count); });

    // S1629. A helper that calls itself. The evaluator stops at the most calls that it follows for a type, and the row of the interface holds.
    public static Cell<int> AddThroughISetOfARecursiveHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { ISet<Alike> set = MakeASetByRecursion(2); set.Add(new Alike(l.Map(v => v))); set.Add(new Alike(Plain)); return S.Hold(set.Count); });

    // S1630. A local function gets a null comparer and gives it to Sort, which then calls CompareTo.
    public static Cell<int> ListSortWithANullComparerParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; static void SortWith(List<Ranked> items, IComparer<Ranked>? comparer) => items.Sort(comparer); SortWith(all, null); return S.Hold(all.Count); });

    // S1631. A method of the class gets the default literal for its comparer.
    public static Cell<int> ListSortWithADefaultComparerParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortWithComparer(all, default); return S.Hold(all.Count); });

    // S1632. An optional comparer parameter whose default is null, and a call that gives no comparer.
    public static Cell<int> ListSortWithAnOptionalComparerParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortWithAnOptionalComparer(all); return S.Hold(all.Count); });

    // S1633. A null comparer by name, with the arguments in a different order.
    public static Cell<int> ListSortWithANullComparerParameterByName() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortWithComparer(comparer: null, items: all); return S.Hold(all.Count); });

    // S1634. A helper that gives its comparer parameter to a second helper. CompareTo of RankedOwn reads with no
    // helper of its own, thus the chain is within the depth that the evaluator follows.
    public static Cell<int> ListSortWithANullComparerThroughTwoHelpers() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<RankedOwn> all = [new(l.Map(v => v)), new(Plain)]; SortThrough(all, null); return S.Hold(all.Count); });

    // S1635. A local that only gets null, given to the comparer parameter.
    public static Cell<int> ListSortWithANullComparerInALocalGivenToAParameter() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; IComparer<Ranked>? none = null; SortWithComparer(all, none); return S.Hold(all.Count); });

    // S1636. A helper whose comparer parameter gets a comparer in its body when it is null. That comparer reads nothing.
    public static Cell<int> ListSortWithAComparerParameterThatTheHelperFills() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortWithAComparerIfNone(all, null); return S.Hold(all.Count); });

    // S1637. A comparer that reads nothing, given to the parameter.
    public static Cell<int> ListSortWithAComparerParameterThatReadsNothing() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortWithComparer(all, Comparer<Ranked>.Create((x, y) => 0)); return S.Hold(all.Count); });

    // S1638. The block reads an optional comparer of the method that contains the block. The analyzer does not know the argument, thus the comparer is a comparer.
    public static Cell<int> ListSortWithAnOptionalComparerOfTheMethodOfTheBlock() =>
        SortInABlock(Comparer<Ranked>.Create((x, y) => 0));

    // S1639. An optional comparer parameter that the call gives a comparer. That comparer reads nothing, and Sort calls it.
    public static Cell<int> ListSortWithAnOptionalComparerParameterThatGetsAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortWithAnOptionalComparer(all, Comparer<Ranked>.Create((x, y) => 0)); return S.Hold(all.Count); });

    // S1640. A local function of the helper reads the comparer parameter that got null. CompareTo of RankedOwn reads
    // with no helper of its own, thus the chain is within the depth that the evaluator follows.
    public static Cell<int> ListSortWithANullComparerParameterInALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<RankedOwn> all = [new(l.Map(v => v)), new(Plain)]; SortInALocalFunction(all, null); return S.Hold(all.Count); });

    // S1641. Remove of a queue compares the elements. The element and the priority have one type, and the priority is pending.
    public static Cell<int> RemoveOfAQueueWhoseElementAndPriorityHaveOneTypeWithAPendingPriority() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Alike, Alike)[] one = [(new Alike(Plain), new Alike(l.Map(v => v)))]; PriorityQueue<Alike, Alike> queue = new(one); Kept.Add(queue.Remove(new Alike(Plain), out _, out _)); return S.Hold(0); });

    // S1642. The same, where the element is pending.
    public static Cell<int> RemoveOfAQueueWhoseElementAndPriorityHaveOneTypeWithAPendingElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Alike, Alike)[] one = [(new Alike(l.Map(v => v)), new Alike(Plain))]; PriorityQueue<Alike, Alike> queue = new(one); Kept.Add(queue.Remove(new Alike(Plain), out _, out _)); return S.Hold(0); });

    // S1643. Enqueue compares the priorities. The element and the priority have one type, and the element is pending.
    public static Cell<int> EnqueueOfAQueueWhoseElementAndPriorityHaveOneTypeWithAPendingElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Ranked, Ranked)[] one = [(new Ranked(l.Map(v => v)), new Ranked(Plain))]; PriorityQueue<Ranked, Ranked> queue = new(one); queue.Enqueue(new Ranked(Plain), new Ranked(Plain)); return S.Hold(queue.Count); });

    // S1644. The same, where the priority is pending.
    public static Cell<int> EnqueueOfAQueueWhoseElementAndPriorityHaveOneTypeWithAPendingPriority() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Ranked, Ranked)[] one = [(new Ranked(Plain), new Ranked(l.Map(v => v)))]; PriorityQueue<Ranked, Ranked> queue = new(one); queue.Enqueue(new Ranked(Plain), new Ranked(Plain)); return S.Hold(queue.Count); });

    // S1645. Enqueue compares the priorities. The element that it gets is pending and has the type of the priority, and gets no call.
    public static Cell<int> EnqueueOfAPendingElementWhoseTypeIsThePriorityType() =>
        Cell.Loop<int>().WithoutCaptures(l => { PriorityQueue<Ranked, Ranked> queue = new(); queue.Enqueue(new Ranked(Plain), new Ranked(Plain)); queue.Enqueue(new Ranked(l.Map(v => v)), new Ranked(Plain)); return S.Hold(queue.Count); });

    // S1646. The constructor of a queue compares the priorities of the pairs. The element is pending, has the type of the priority, and gets no call.
    public static Cell<int> PriorityQueueOfTwoPairsWhoseElementAndPriorityHaveOneTypeWithAPendingElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { (Ranked, Ranked)[] two = [(new Ranked(l.Map(v => v)), new Ranked(Plain)), (new Ranked(Plain), new Ranked(Plain))]; PriorityQueue<Ranked, Ranked> queue = new(two); return S.Hold(queue.Count); });

    // S1647. Contains of a pair through ICollection of a dictionary looks for the key, then compares the values.
    public static Cell<int> ContainsOfAPairThroughICollectionOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Alike, int>> pairs = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(pairs.Contains(new KeyValuePair<Alike, int>(new Alike(Plain), 1))); return S.Hold(0); });

    // S1648. Remove of a pair through ICollection of a dictionary.
    public static Cell<int> RemoveOfAPairThroughICollectionOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Alike, int>> pairs = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; Kept.Add(pairs.Remove(new KeyValuePair<Alike, int>(new Alike(Plain), 1))); return S.Hold(0); });

    // S1649. Contains of a pair whose value is pending. The dictionary finds the key, and compares the two values.
    public static Cell<int> ContainsOfAPairThroughICollectionOfADictionaryWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<int, Alike>> pairs = new Dictionary<int, Alike> { { 1, new Alike(l.Map(v => v)) } }; Kept.Add(pairs.Contains(new KeyValuePair<int, Alike>(1, new Alike(Plain)))); return S.Hold(0); });

    // S1650. Add of a pair through ICollection of a dictionary compares the key.
    public static Cell<int> AddOfAPairThroughICollectionOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Alike, int>> pairs = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; pairs.Add(new KeyValuePair<Alike, int>(new Alike(Plain), 2)); return S.Hold(pairs.Count); });

    // S1651. Add of a pair compares only the key. The value has the type of the key, is pending, and gets no call.
    public static Cell<int> AddOfAPairThroughICollectionOfADictionaryWhereTheValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Alike, Alike>> pairs = new Dictionary<Alike, Alike>(); pairs.Add(new KeyValuePair<Alike, Alike>(new Alike(Plain), new Alike(l.Map(v => v)))); return S.Hold(pairs.Count); });

    // S1652. Contains of a pair through ICollection of a SortedDictionary compares the keys with CompareTo.
    public static Cell<int> ContainsOfAPairThroughICollectionOfASortedDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Ranked, int>> pairs = new SortedDictionary<Ranked, int> { { new Ranked(l.Map(v => v)), 1 } }; Kept.Add(pairs.Contains(new KeyValuePair<Ranked, int>(new Ranked(Plain), 1))); return S.Hold(0); });

    // S1653. CopyTo of the pairs of a dictionary compares nothing.
    public static Cell<int> CopyToOfThePairsOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Alike, int>> pairs = new Dictionary<Alike, int> { { new Alike(l.Map(v => v)), 1 } }; KeyValuePair<Alike, int>[] copy = new KeyValuePair<Alike, int>[1]; pairs.CopyTo(copy, 0); return S.Hold(copy.Length); });

    // S1654. Contains of a list of pairs. Equals of a KeyValuePair calls Equals of each part.
    public static Cell<int> ContainsOfAPairInAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<KeyValuePair<Alike, int>> pairs = [new KeyValuePair<Alike, int>(new Alike(l.Map(v => v)), 1)]; Kept.Add(pairs.Contains(new KeyValuePair<Alike, int>(new Alike(Plain), 1))); return S.Hold(0); });

    // S1655. Contains of a pair whose key is pending, through ICollection of a dictionary. The key of the dictionary gets Equals with the pending key.
    public static Cell<int> ContainsOfAPairWithAPendingKeyThroughICollectionOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<Alike, int>> pairs = new Dictionary<Alike, int> { { new Alike(Plain), 1 } }; Kept.Add(pairs.Contains(new KeyValuePair<Alike, int>(new Alike(l.Map(v => v)), 1))); return S.Hold(0); });

    // S1656. The same, where the value of the pair is pending. The dictionary finds the key, and compares the values.
    public static Cell<int> ContainsOfAPairWithAPendingValueThroughICollectionOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { ICollection<KeyValuePair<int, Alike>> pairs = new Dictionary<int, Alike> { { 1, new Alike(Plain) } }; Kept.Add(pairs.Contains(new KeyValuePair<int, Alike>(1, new Alike(l.Map(v => v))))); return S.Hold(0); });

    // S1657. CompareTo of Ranked calls a helper of its own. Two helpers down, that helper is the fourth call, which the evaluator follows.
    public static Cell<int> CompareToTwoHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(CompareRankedThrough(all)); return S.Hold(0); });

    // S1658. A sort two helpers down.
    public static Cell<int> SortTwoHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortRankedThrough(all); return S.Hold(all.Count); });

    // S1659. A sort with a null comparer two helpers down.
    public static Cell<int> SortWithANullComparerTwoHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortRankedWithThrough(all, null); return S.Hold(all.Count); });

    // S1660. A sort with a null comparer in a local function of a helper.
    public static Cell<int> SortWithANullComparerInALocalFunctionOfAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortRankedInALocalFunction(all, null); return S.Hold(all.Count); });

    // S1661. A sort three helpers down. The helper of CompareTo is the fifth call, and the evaluator does not read it. The object has a pending member, thus the call gets the report for code that the analyzer does not read.
    public static Cell<int> SortThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; SortRankedThroughTwo(all); return S.Hold(all.Count); });

    // S1662. A call of CompareTo three helpers down.
    public static Cell<int> CompareToThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Ranked> all = [new(l.Map(v => v)), new(Plain)]; Kept.Add(CompareRankedThroughTwo(all)); return S.Hold(0); });

    // S1663. A getter that calls a helper, two helpers down.
    public static Cell<int> GetterTwoHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(l.Map(v => v)); Kept.Add(NowThrough(reader)); return S.Hold(0); });

    // S1664. The same getter three helpers down. The evaluator does not read its helper.
    public static Cell<int> GetterThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(l.Map(v => v)); Kept.Add(NowThroughTwo(reader)); return S.Hold(0); });

    // S1665. A constructor two helpers down reads the getter, whose helper the evaluator does not read.
    public static Cell<int> GetterInAConstructorTwoHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(l.Map(v => v)); Kept.Add(KeepNowThrough(reader)); return S.Hold(0); });

    // S1666. GetOrAdd with a function looks for the key that it gets. The function only makes the value.
    public static Cell<int> GetOrAddWithAFunctionForTheValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ConcurrentDictionary<Alike, int> map = new() { [new Alike(l.Map(v => v))] = 1 }; Kept.Add(map.GetOrAdd(new Alike(Plain), _ => 2)); return S.Hold(0); });

    // S1667. GetOrAdd with a function that gets an argument.
    public static Cell<int> GetOrAddWithAFunctionAndItsArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { ConcurrentDictionary<Alike, int> map = new() { [new Alike(l.Map(v => v))] = 1 }; Kept.Add(map.GetOrAdd(new Alike(Plain), (_, more) => more, 2)); return S.Hold(0); });

    // S1668. AddOrUpdate with two functions that make new values.
    public static Cell<int> AddOrUpdateWithTwoFunctions() =>
        Cell.Loop<int>().WithoutCaptures(l => { ConcurrentDictionary<Alike, int> map = new() { [new Alike(l.Map(v => v))] = 1 }; Kept.Add(map.AddOrUpdate(new Alike(Plain), _ => 2, (_, old) => old + 1)); return S.Hold(0); });

    // S1669. GetOrAdd where the key that the code gives is pending, and the key of the dictionary reads the two keys.
    public static Cell<int> GetOrAddWithAFunctionWhereTheGivenKeyIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ConcurrentDictionary<Alike, int> map = new() { [new Alike(Plain)] = 1 }; Kept.Add(map.GetOrAdd(new Alike(l.Map(v => v)), _ => 2)); return S.Hold(0); });

    // S1670. GetOrAdd gives the call to the key of the dictionary. The key that the code gives is pending, and its member reads only its own object.
    public static Cell<int> GetOrAddWithAFunctionWhereTheGivenKeyReadsOnlyItself() =>
        Cell.Loop<int>().WithoutCaptures(l => { ConcurrentDictionary<AlikeOwn, int> map = new() { [new AlikeOwn(Plain)] = 1 }; Kept.Add(map.GetOrAdd(new AlikeOwn(l.Map(v => v)), _ => 2)); return S.Hold(0); });

    // S1671. GetOrAdd does not compare the value that the function makes.
    public static Cell<int> GetOrAddDoesNotCompareTheMadeValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ConcurrentDictionary<int, Alike> map = new(); map.GetOrAdd(1, _ => new Alike(l.Map(v => v))); Kept.Add(map.GetOrAdd(1, _ => new Alike(Plain))); return S.Hold(0); });

    // S1672. ExceptBy compares what the function returns with each element of its second sequence.
    public static Cell<int> ExceptByComparesTheKeysWithTheSecondSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] second = [new(l.Map(v => v))]; return S.Hold(Numbers.ExceptBy(second, n => new Alike(Plain)).ToArray().Length); });

    // S1673. IntersectBy is the same.
    public static Cell<int> IntersectByComparesTheKeysWithTheSecondSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] second = [new(l.Map(v => v))]; return S.Hold(Numbers.IntersectBy(second, n => new Alike(Plain)).ToArray().Length); });

    // S1674. ExceptBy does not compare the elements of its first sequence, also when they have the type of the keys.
    public static Cell<int> ExceptByDoesNotCompareTheElementsOfItsFirstSequence() =>
        Cell.Loop<int>().WithoutCaptures(l => { Alike[] first = [new(l.Map(v => v))]; Alike[] second = [new(Plain)]; return S.Hold(first.ExceptBy(second, x => new Alike(Plain)).ToArray().Length); });

    // S1675. A collection expression of a HashSet calls Add for each element, and Add gives the call to the element that the set has. The pending element is the second one, and gets no call.
    public static Cell<int> SetExpressionWhereTheSecondElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain), new(l.Map(v => v))]; return S.Hold(set.Count); });

    // S1676. The same, where the pending element is the first one. The Add of the second element calls its Equals.
    public static Cell<int> SetExpressionWhereTheFirstElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v)), new(Plain)]; return S.Hold(set.Count); });

    // S1677. Three elements. The Add of the third calls Equals of the second, which is pending.
    public static Cell<int> SetExpressionOfThreeWhereTheSecondElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain), new(l.Map(v => v)), new(Plain)]; return S.Hold(set.Count); });

    // S1678. An initializer of a HashSet, where the pending element is the second one.
    public static Cell<int> SetInitializerWhereTheSecondElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = new() { new(Plain), new(l.Map(v => v)) }; return S.Hold(set.Count); });

    // S1679. An initializer of a HashSet, where the pending element is the first one.
    public static Cell<int> SetInitializerWhereTheFirstElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = new() { new(l.Map(v => v)), new(Plain) }; return S.Hold(set.Count); });

    // S1680. An initializer of a Dictionary, where the pending key is the second one.
    public static Cell<int> DictionaryInitializerWhereTheSecondKeyIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<SameOwn, int> map = new() { { new(Plain), 1 }, { new(l.Map(v => v)), 2 } }; return S.Hold(map.Count); });

    // S1681. An initializer of a Dictionary, where the pending key is the first one.
    public static Cell<int> DictionaryInitializerWhereTheFirstKeyIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<SameOwn, int> map = new() { { new(l.Map(v => v)), 1 }, { new(Plain), 2 } }; return S.Hold(map.Count); });

    // S1682. An initializer of a Dictionary with indexes, where the pending key is the second one.
    public static Cell<int> DictionaryIndexInitializerWhereTheSecondKeyIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<SameOwn, int> map = new() { [new(Plain)] = 1, [new(l.Map(v => v))] = 2 }; return S.Hold(map.Count); });

    // S1683. An initializer of a Dictionary with indexes, where the pending key is the first one.
    public static Cell<int> DictionaryIndexInitializerWhereTheFirstKeyIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<SameOwn, int> map = new() { [new(l.Map(v => v))] = 1, [new(Plain)] = 2 }; return S.Hold(map.Count); });

    // S1684. Add of a SortedSet gives the call to the element that it gets. The pending element is the second one.
    public static Cell<int> SortedSetExpressionWhereTheSecondElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(Plain), new(l.Map(v => v))]; return S.Hold(set.Count); });

    // S1685. The same, where the pending element is the first one. It is the argument of CompareTo, which reads only its own object.
    public static Cell<int> SortedSetExpressionWhereTheFirstElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(l.Map(v => v)), new(Plain)]; return S.Hold(set.Count); });

    // S1686. The value of an initializer of a Dictionary has the type of the key and is pending. Add compares the keys.
    public static Cell<int> DictionaryInitializerWhereAValueOfTheTypeOfTheKeyIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<SameOwn, SameOwn> map = new() { { new(Plain), new(l.Map(v => v)) }, { new(Plain), new(Plain) } }; return S.Hold(map.Count); });

    // S1687. IsSubsetOf of a HashSet, where the argument is a HashSet with the same comparer. The method then looks for each of its elements in the argument, and the elements of the argument get the call.
    public static Cell<int> IsSubsetOfAHashSetArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; HashSet<SameOwn> other = new(); other.Add(new(Plain)); other.Add(new(l.Map(v => v))); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1688. The same with an array as the argument. The element of the set gets the call.
    public static Cell<int> IsSubsetOfAnArrayArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; SameOwn[] other = [new(Plain), new(l.Map(v => v))]; Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1689. IsProperSubsetOf of a HashSet gives the call to the element of the set. A collection expression for a sequence is not a set.
    public static Cell<int> IsProperSubsetOfWhereAGivenValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; Kept.Add(set.IsProperSubsetOf([new SameOwn(Plain), new SameOwn(l.Map(v => v))])); return S.Hold(0); });

    // S1690. The same, where the element of the set is pending.
    public static Cell<int> IsProperSubsetOfWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; Kept.Add(set.IsProperSubsetOf([new SameOwn(Plain), new SameOwn(Plain)])); return S.Hold(0); });

    // S1691. IsSubsetOf where the analyzer does not know the type of the argument. The argument is a HashSet, and its element gets the call.
    public static Cell<int> IsSubsetOfAnArgumentOfAnUnknownTypeThatIsASet() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; IEnumerable<SameOwn> other = MakeASetOrAnArray(true, new SameOwn(Plain), new SameOwn(l.Map(v => v))); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1692. The same, where the argument is an array, and the element of the set is pending.
    public static Cell<int> IsSubsetOfAnArgumentOfAnUnknownTypeThatIsAnArray() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; IEnumerable<SameOwn> other = MakeASetOrAnArray(false, new SameOwn(Plain), new SameOwn(Plain)); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1693. IsSubsetOf where the argument is a HashSet that got the Default of the platform for its comparer. That is the comparer of the set also, and an element of the argument gets the call.
    public static Cell<int> IsSubsetOfAHashSetArgumentThatGotAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; HashSet<SameOwn> other = new(EqualityComparer<SameOwn>.Default); other.Add(new(Plain)); other.Add(new(l.Map(v => v))); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1694. Overlaps of a SortedSet with a SortedSet argument gives the call to the element of the set, and not to the element of the argument.
    public static Cell<int> OverlapsOfASortedSetArgumentWhereTheElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(l.Map(v => v))]; SortedSet<RankedOwn> other = [new(Plain)]; Kept.Add(set.Overlaps(other)); return S.Hold(0); });

    // S1695. The same, where the element of the argument is pending.
    public static Cell<int> OverlapsOfASortedSetArgumentWhereTheGivenElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> set = [new(Plain)]; SortedSet<RankedOwn> other = [new(l.Map(v => v))]; Kept.Add(set.Overlaps(other)); return S.Hold(0); });

    // S1696. Union of an ImmutableHashSet also compares the values that it gets with each other. Equals of SamePeer reads only the object that it gets.
    public static Cell<int> UnionOfAnImmutableHashSetComparesTheGivenValues() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SamePeer> set = ImmutableHashSet.Create(new SamePeer(Plain)); Kept.Add(set.Union([new SamePeer(l.Map(v => v)), new SamePeer(Plain)]).Count); return S.Hold(0); });

    // S1697. UnionWith of a SortedSet also compares the values that it gets with each other.
    public static Cell<int> UnionWithOfASortedSetComparesTheGivenValues() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedPeer> set = [new(Plain, 2)]; set.UnionWith([new RankedPeer(l.Map(v => v), 1), new RankedPeer(Plain, 0)]); return S.Hold(set.Count); });

    // S1698. IntersectWith of a HashSet removes an element, and that compares the elements of the set with each other.
    public static Cell<int> IntersectWithOfAHashSetComparesItsElements() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SamePeer> set = new(); set.Add(new(l.Map(v => v))); set.Add(new(Plain)); set.IntersectWith([new SamePeer(Plain)]); return S.Hold(set.Count); });

    // S1699. A second local is a second name of the object. A change through it is a change for the first local.
    public static Cell<int> ChangeThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot other = slot; other.Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1700. The same, with the change through the first local and the read through the second.
    public static Cell<int> ChangeThroughTheFirstLocalReadThroughTheSecond() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot other = slot; slot.Share = l.Map(v => v); return S.Hold(other.Share!.Sample()); });

    // S1701. An element of a list is a name of the object that the list got. The read is through the local.
    public static Cell<int> ChangeThroughAnElementReadThroughTheLocalThatTheListGot() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; slots[0].Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1702. A local that got an element is a name of that element.
    public static Cell<int> ChangeThroughAnElementReadThroughALocalOfThatElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new() { Share = Plain }]; SettableSlot slot = slots[0]; slots[0].Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1703. The variable of a foreach is a name of each element.
    public static Cell<int> ChangeThroughAForeachVariableReadThroughTheLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; foreach (SettableSlot each in slots) { each.Share = l.Map(v => v); } return S.Hold(slot.Share!.Sample()); });

    // S1704. A tuple holds the object, and not a copy of its state.
    public static Cell<int> ChangeThroughALocalThatATupleHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; (SettableSlot First, int Second) pair = (slot, 1); slot.Share = l.Map(v => v); return S.Hold(pair.First.Share!.Sample()); });

    // S1705. The same for a KeyValuePair.
    public static Cell<int> ChangeThroughALocalThatAKeyValuePairHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; KeyValuePair<string, SettableSlot> pair = new("a", slot); slot.Share = l.Map(v => v); return S.Hold(pair.Value.Share!.Sample()); });

    // S1706. The same for a member of a second object.
    public static Cell<int> ChangeThroughALocalThatAnObjectHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SlotHolder holder = new() { Item = slot }; slot.Share = l.Map(v => v); return S.Hold(holder.Item!.Share!.Sample()); });

    // S1707. A helper puts the object into a list. The element of the list is a name of the object.
    public static Cell<int> ChangeThroughALocalThatAHelperPutIntoAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = new(); Put(slots, slot); slot.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); static void Put(List<SettableSlot> into, SettableSlot one) => into.Add(one); });

    // S1708. A copy of a list is a new list that holds the same objects.
    public static Cell<int> ChangeThroughALocalThatACopyOfTheListHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> first = [slot]; List<SettableSlot> second = new(first); slot.Share = l.Map(v => v); return S.Hold(second[0].Share!.Sample()); });

    // S1709. The same for a spread of the list.
    public static Cell<int> ChangeThroughALocalThatASpreadOfTheListHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> first = [slot]; List<SettableSlot> second = [..first]; slot.Share = l.Map(v => v); return S.Hold(second[0].Share!.Sample()); });

    // S1710. A local that got the list in a property is a name of that list.
    public static Cell<int> ListInAPropertyThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellBox box = new(); List<Cell<int>> cells = box.Cells; cells.Add(l.Map(v => v)); return S.Hold(box.Cells[0].Sample()); });

    // S1711. A constructor keeps the list that it got. The list gets the pending cell after the constructor.
    public static Cell<int> ListThatAConstructorGotChangesLater() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = new(); CellKeeper keeper = new(cells); cells.Add(l.Map(v => v)); return S.Hold(keeper.Cells[0].Sample()); });

    // S1712. A second local of a list is a name of the list.
    public static Cell<int> ListGetsAPendingCellThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; List<Cell<int>> other = cells; other.Add(l.Map(v => v)); return S.Hold(cells[1].Sample()); });

    // S1713. A list in a list gets a pending cell through its local. The read is through the outer list.
    public static Cell<int> ListInAListGetsAPendingCellThroughItsLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; List<List<Cell<int>>> outer = [cells]; cells.Add(l.Map(v => v)); return S.Hold(outer[0][1].Sample()); });

    // S1714. The same, with the change through the outer list and the read through the local.
    public static Cell<int> ListGetsAPendingCellThroughAnElementOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; List<List<Cell<int>>> outer = [cells]; outer[0].Add(l.Map(v => v)); return S.Hold(cells[1].Sample()); });

    // S1715. A second local of a dictionary is a name of the dictionary.
    public static Cell<int> DictionaryGetsAPendingCellThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<string, Cell<int>> map = new(); Dictionary<string, Cell<int>> other = map; other["a"] = l.Map(v => v); return S.Hold(map["a"].Sample()); });

    // S1716. A second local of an array is a name of the array.
    public static Cell<int> ArrayGetsAPendingCellThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cells = [Plain]; Cell<int>[] other = cells; other[0] = l.Map(v => v); return S.Hold(cells[0].Sample()); });

    // S1717. A second local gives the object a plain cell after the pending one. The object has only the plain cell.
    public static Cell<int> PlainThroughASecondLocalAfterPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = l.Map(v => v) }; SettableSlot other = slot; other.Share = Plain; return S.Hold(slot.Share!.Sample()); });

    // S1718. Two objects of one type are not one object. A change to one is not a change to the other.
    public static Cell<int> ChangeToTheSecondOfTwoObjectsOfOneType() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = new() { Share = Plain }; SettableSlot b = new() { Share = Plain }; b.Share = l.Map(v => v); return S.Hold(a.Share!.Sample()); });

    // S1719. A change to an object that the list does not hold is not a change to its element.
    public static Cell<int> ChangeToAnObjectThatTheListDoesNotHold() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> slots = [new() { Share = Plain }]; SettableSlot other = new() { Share = Plain }; other.Share = l.Map(v => v); return S.Hold(slots[0].Share!.Sample()); });

    // S1720. A local that gets a new object is no longer a name of the first one.
    public static Cell<int> ChangeThroughALocalThatGotANewObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot other = slot; other = new() { Share = Plain }; other.Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1721. A copy of a list is a new list. A cell that the copy gets is not in the first list.
    public static Cell<int> CopyOfAListGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; List<Cell<int>> copy = new(cells); copy.Add(l.Map(v => v)); return S.Hold(cells[0].Sample()); });

    // S1722. The same for a copy that ToList makes.
    public static Cell<int> ToListCopyOfAListGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; List<Cell<int>> copy = cells.ToList(); copy.Add(l.Map(v => v)); return S.Hold(cells[0].Sample()); });

    // S1723. A struct is a copy at each assignment. A change to the copy is not a change to the first value.
    public static Cell<int> CopyOfAStructGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotValue first = new() { Share = Plain }; SlotValue second = first; second.Share = l.Map(v => v); return S.Hold(first.Share!.Sample()); });

    // S1724. A with expression makes a new object. A change to the copy is not a change to the first object.
    public static Cell<int> WithCopyOfARecordGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotRecord first = new() { Share = Plain }; SlotRecord second = first with { }; second.Share = l.Map(v => v); return S.Hold(first.Share!.Sample()); });

    // S1725. A write in an if statement does not always run. The object keeps the pending cell also.
    public static Cell<int> MemberSetUnderAConditionThatDoesNotRun() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; if (Never) { x.Share = Plain; } return S.Hold(x.Share!.Sample()); });

    // S1726. The same, with the write through a second name.
    public static Cell<int> MemberSetThroughASecondNameUnderACondition() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; if (Never) { y.Share = Plain; } return S.Hold(x.Share!.Sample()); });

    // S1727. A call of a helper in an if statement sets a plain cell. The call does not always run.
    public static Cell<int> HelperUnderAConditionThatDoesNotRunSetsAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; if (Never) { FillSlot(x, Plain); } return S.Hold(x.Share!.Sample()); });

    // S1728. A helper sets a pending cell through a second name of the object.
    public static Cell<int> HelperSetsAPendingCellThroughASecondName() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; FillSlot(y, l.Map(v => v)); return S.Hold(x.Share!.Sample()); });

    // S1729. A helper with no argument makes the object. The object has two names in the block.
    public static Cell<int> ObjectThatAHelperMadeUnderTwoNames() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = MakeAPlainSlot(); SettableSlot other = slot; other.Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1730. A method of the platform makes the object. The object has two names in the block.
    public static Cell<int> ObjectThatThePlatformMadeUnderTwoNames() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = Activator.CreateInstance<SettableSlot>(); SettableSlot other = slot; other.Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1731. A copy that ToList makes is a new list, and it has two names.
    public static Cell<int> CopyOfAListUnderTwoNames() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; List<Cell<int>> copy = cells.ToList(); List<Cell<int>> other = copy; other.Add(l.Map(v => v)); return S.Hold(copy[1].Sample()); });

    // S1732. A list in a member with an interface type, where the code names the type of the list.
    public static Cell<int> CastOfAListInAMember() =>
        Cell.Loop<int>().WithoutCaptures(l => { ListBehindAnInterface box = new(); ((List<Cell<int>>)box.Cells).Add(l.Map(v => v)); return S.Hold(box.Cells[0].Sample()); });

    // S1733. Two calls of one helper make two objects. The first one has the pending cell.
    public static Cell<int> FirstObjectOfTwoCallsOfOneHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = MakeASlotWith(l.Map(v => v)); SettableSlot b = MakeASlotWith(Plain); Kept.Add(b); return S.Hold(a.Share!.Sample()); });

    // S1734. The same two calls. The second object has only the plain cell.
    public static Cell<int> SecondObjectOfTwoCallsOfOneHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = MakeASlotWith(l.Map(v => v)); SettableSlot b = MakeASlotWith(Plain); Kept.Add(a); return S.Hold(b.Share!.Sample()); });

    // S1735. Two calls of a helper with no argument make two objects. A change to one is not a change to the other.
    public static Cell<int> ChangeToTheSecondObjectOfTwoCallsOfOneHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = MakeAPlainSlot(); SettableSlot b = MakeAPlainSlot(); b.Share = l.Map(v => v); return S.Hold(a.Share!.Sample()); });

    // S1736. A write that always runs replaces the pending cell of an object that a helper made.
    public static Cell<int> PlainAfterPendingInAnObjectThatAHelperMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = MakeASlotWith(l.Map(v => v)); slot.Share = Plain; return S.Hold(slot.Share!.Sample()); });

    // S1737. The same, with the write through a second name.
    public static Cell<int> PlainThroughASecondNameInAnObjectThatAHelperMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = MakeASlotWith(l.Map(v => v)); SettableSlot other = slot; other.Share = Plain; return S.Hold(slot.Share!.Sample()); });

    // S1738. A helper makes the object and replaces its pending cell before it returns.
    public static Cell<int> HelperReplacesThePendingCellOfTheObjectThatItMakes() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = MakeASlotAndClear(l.Map(v => v)); return S.Hold(slot.Share!.Sample()); });

    // S1739. An object of a loop gets a pending cell and then a plain one, in each turn.
    public static Cell<int> PlainAfterPendingInAnObjectOfALoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; x.Share = Plain; list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1740. A helper makes the object, puts it into the list of the caller, and returns it. The result and the element are two names of the object.
    public static Cell<int> ObjectThatAHelperMadeAndPutIntoAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; SettableSlot slot = MakeASlotInto(list, Plain); slot.Share = l.Map(v => v); return S.Hold(list[0].Share!.Sample()); });

    // S1741. A constructor three helpers down reads the getter. The evaluator does not read the getter, and the record is in the frame of the constructor.
    public static Cell<int> GetterInAConstructorThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(l.Map(v => v)); Kept.Add(KeepNowThroughTwo(reader)); return S.Hold(0); });

    // S1742. The same, where the object has only a plain cell. A member that the evaluator does not read gets no report when no member of its object has a state.
    public static Cell<int> GetterOfAPlainObjectInAConstructorThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(Plain); Kept.Add(KeepNowThroughTwo(reader)); return S.Hold(0); });

    // S1743. A lambda that the block only keeps does not run. A member that it calls at a depth that the evaluator does not read gets no report.
    public static Cell<int> KeptLambdaCallsAGetterThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(l.Map(v => v)); Func<int> later = () => KeepNowThroughTwo(reader); Kept.Add(later); return S.Hold(0); });

    // S1744. A set expression where Equals reads the object that it gets. The first element is pending, and it gets the call with the plain one.
    public static Cell<int> SetExpressionOfPeersWhereTheFirstElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SamePeer> set = [new(l.Map(v => v)), new(Plain)]; Kept.Add(set); return S.Hold(0); });

    // S1745. The same for an initializer.
    public static Cell<int> SetInitializerOfPeersWhereTheFirstElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SamePeer> set = new() { new(l.Map(v => v)), new(Plain) }; Kept.Add(set); return S.Hold(0); });

    // S1746. IsSubsetOf where the argument is a HashSet with a different comparer. The platform does not use the path for two sets, and the element of the set gets the call.
    public static Cell<int> IsSubsetOfAHashSetArgumentWithADifferentComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; HashSet<SameOwn> other = new(ReferenceEqualityComparer.Instance) { new(Plain) }; Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1747. IsSubsetOf where the argument is a local that got a set with a different comparer after its declaration.
    public static Cell<int> IsSubsetOfAHashSetArgumentThatGotADifferentSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; HashSet<SameOwn> other = [new(Plain)]; other = new(ReferenceEqualityComparer.Instance) { new(Plain) }; Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1748. IsSubsetOf where a factory of the platform made the argument with a different comparer.
    public static Cell<int> IsSubsetOfASetFromAFactoryWithADifferentComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; HashSet<SameOwn> other = new SameOwn[] { new(Plain) }.ToHashSet<SameOwn>(ReferenceEqualityComparer.Instance); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1749. IsSubsetOf of a HashSet where the argument is an ImmutableHashSet. The table has no row for that pair, and the element of the HashSet gets the call.
    public static Cell<int> IsSubsetOfAHashSetWhereTheArgumentIsAnImmutableHashSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; ImmutableHashSet<SameOwn> other = ImmutableHashSet.Create(new SameOwn(Plain)); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1750. The function of a Map does not run in the block. A member that it calls at a depth that the evaluator does not read gets no report.
    public static Cell<int> MapFunctionCallsAGetterThreeHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepReader reader = new(l.Map(v => v)); Kept.Add(Plain.Map(v => v + KeepNowThroughTwo(reader))); return S.Hold(0); });

    // S1751. An object whose pending cell the block replaced with a plain one. A getter of it that the evaluator does not read gets no report, because no member of the object has a state.
    public static Cell<int> GetterOfAnObjectThatGotAPlainCellFourHelpersDown() =>
        Cell.Loop<int>().WithoutCaptures(l => { DeepSlot slot = new() { Share = l.Map(v => v) }; slot.Share = Plain; Kept.Add(NowOfASlotThroughThree(slot)); return S.Hold(0); });

    // S1752. IsSubsetOf where the set of the call has a comparer of the platform. That comparer calls no member of an element.
    public static Cell<int> IsSubsetOfWhereTheSetHasADifferentComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = new(ReferenceEqualityComparer.Instance) { new(Plain) }; HashSet<SameOwn> other = [new(l.Map(v => v))]; Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1753. IsSubsetOf where the argument has a different comparer and a pending element. The plain element of the set gets the call.
    public static Cell<int> IsSubsetOfAnArgumentWithADifferentComparerWhereItsElementIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; HashSet<SameOwn> other = new(ReferenceEqualityComparer.Instance); other.Add(new(l.Map(v => v))); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1754. IsSubsetOf where the comparer of the argument is in a local. The analyzer does not know that it is the default one, and reads the two objects.
    public static Cell<int> IsSubsetOfAnArgumentWhoseComparerIsInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(Plain)]; IEqualityComparer<SameOwn> same = EqualityComparer<SameOwn>.Default; HashSet<SameOwn> other = new(same); other.Add(new(l.Map(v => v))); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1755. IsSubsetOf where the argument got null for its comparer, which is the default one. The plain element of the argument gets the call.
    public static Cell<int> IsSubsetOfAnArgumentThatGotNullForItsComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; HashSet<SameOwn> other = new((IEqualityComparer<SameOwn>?)null) { new(Plain) }; Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1756. IsSubsetOf where the set of the call has a comparer of this code that reads nothing.
    public static Cell<int> IsSubsetOfWhereTheSetHasAComparerOfThisCode() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = new(new NeverTheSame()) { new(Plain) }; HashSet<SameOwn> other = [new(l.Map(v => v))]; Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1757. IsSubsetOf where a helper of this code made the argument with a different comparer.
    public static Cell<int> IsSubsetOfASetFromAHelperWithADifferentComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; HashSet<SameOwn> other = MakeASetThatComparesReferences(new SameOwn(Plain)); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1758. Overlaps of a SortedSet where the argument is a view of a set with its own comparer. The view has that comparer, and the analyzer does not know it.
    public static Cell<int> OverlapsOfASortedSetWhereTheArgumentIsAViewOfASetWithAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedPeer> set = [new(l.Map(v => v), 1)]; SortedSet<RankedPeer> source = new(Comparer<RankedPeer>.Create(static (a, b) => 0)) { new(Plain, 1) }; SortedSet<RankedPeer> other = source.GetViewBetween(source.Min!, source.Max!); Kept.Add(set.Overlaps(other)); return S.Hold(0); });

    // S1759. A list holds an object of the store and an object of a loop, which the store does not have. The variable of the foreach can be the second one.
    public static Cell<int> ElementThatIsNoObjectOfTheStoreBesideOneThatIs() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = new() { Share = Plain }; List<SettableSlot> slots = [a]; for (int i = 0; i < 1; i++) { slots.Add(new() { Share = l.Map(v => v) }); } foreach (SettableSlot each in slots) { Kept.Add(each.Share!.Sample()); } return S.Hold(0); });

    // S1760. A local that can be an object of the store or an object of a loop. A plain cell for the first one does not change the second.
    public static Cell<int> NameOfAnObjectOfTheStoreOrOfAnObjectOfALoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = new() { Share = Plain }; SettableSlot? kept = null; for (int i = 0; i < 1; i++) { kept = new() { Share = l.Map(v => v) }; } SettableSlot one = Never ? a : kept!; a.Share = Plain; return S.Hold(one.Share!.Sample()); });

    // S1761. A plain cell through a name of two objects is a change to one of them only. The other one keeps its pending cell.
    public static Cell<int> PlainThroughANameOfTwoObjectsWhereTheOtherIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = new() { Share = l.Map(v => v) }; SettableSlot b = new() { Share = Plain }; SettableSlot one = !Never ? b : a; one.Share = Plain; return S.Hold(a.Share!.Sample()); });

    // S1762. A local that can be one of two objects of the store. The first one gets a plain cell after the local got it, and the second one has a plain cell.
    public static Cell<int> NameOfTwoObjectsAfterTheFirstGotAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot a = new() { Share = l.Map(v => v) }; SettableSlot b = new() { Share = Plain }; SettableSlot one = Never ? b : a; a.Share = Plain; return S.Hold(one.Share!.Sample()); });

    // S1763. The function of a Map does not run in the block. A member that it sets has no new state for the block.
    public static Cell<int> MapFunctionSetsAMemberOfAnObjectOfTheStore() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Kept.Add(Plain.Map(v => { slot.Share = l.Map(x => x); return v; })); return S.Hold(slot.Share!.Sample()); });

    // S1764. A second local of an array that an array creation makes.
    public static Cell<int> ArrayCreationGetsAPendingCellThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cells = new Cell<int>[] { Plain }; Cell<int>[] other = cells; other[0] = l.Map(v => v); return S.Hold(cells[0].Sample()); });

    // S1765. The same for an array creation with no type.
    public static Cell<int> ArrayCreationWithNoTypeGetsAPendingCellThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cells = new[] { Plain }; Cell<int>[] other = cells; other[0] = l.Map(v => v); return S.Hold(cells[0].Sample()); });

    // S1766. The same for an array creation with a length and no element.
    public static Cell<int> ArrayCreationWithALengthGetsAPendingCellThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>[] cells = new Cell<int>[1]; Cell<int>[] other = cells; other[0] = l.Map(v => v); return S.Hold(cells[0].Sample()); });

    // S1767. A method of the platform gives an element of the list. The result is a name of the object that the list got.
    public static Cell<int> ChangeThroughAnElementFromACallReadThroughTheLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; List<SettableSlot> slots = [slot]; SettableSlot got = slots.Single(); got.Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1768. A record keeps the list that its constructor got in a property. The list gets the pending cell after the constructor.
    public static Cell<int> ListThatARecordGotChangesLater() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = new(); CellsRecord keeper = new(cells); cells.Add(l.Map(v => v)); return S.Hold(keeper.Cells[0].Sample()); });

    // S1769. A class keeps the list in a parameter of its primary constructor, and a method of it reads the list.
    public static Cell<int> ListThatAPrimaryConstructorGotChangesLater() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = new(); CellsCapturer keeper = new(cells); cells.Add(l.Map(v => v)); return S.Hold(keeper.First()); });

    // S1770. A helper reads the list in a member of an object that it gets. The report is about the argument of the call.
    public static Cell<int> ListInAMemberOfAnObjectReadInAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { CellBox box = new(); List<Cell<int>> cells = box.Cells; cells.Add(l.Map(v => v)); return S.Hold(FirstOfABox(box)); });

    // S1771. A dictionary holds the object, and the variable of the foreach is a pair. The pair is not the object, and its value is.
    public static Cell<int> ChangeThroughALocalReadThroughAPairOfADictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<string, SettableSlot> map = new() { ["a"] = slot }; slot.Share = l.Map(v => v); foreach (KeyValuePair<string, SettableSlot> pair in map) { Kept.Add(pair.Value.Share!.Sample()); } return S.Hold(0); });

    // S1772. A list of cells of cells gets a cell with a pending cell through a second local.
    public static Cell<int> ListOfCellsOfCellsGetsAPendingContentThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<Cell<int>>> cells = [Cell.Constant(Plain)]; List<Cell<Cell<int>>> other = cells; other.Add(Cell.Constant(l.Map(v => v))); return S.Hold(cells[1].Sample().Sample()); });

    // S1773. A local with an interface type is a name of the list.
    public static Cell<int> ListGetsAPendingCellThroughALocalOfAnInterface() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; IList<Cell<int>> view = cells; view.Add(l.Map(v => v)); return S.Hold(cells[1].Sample()); });

    // S1774. A helper with a type parameter returns the object that it gets.
    public static Cell<int> ChangeThroughTheResultOfAGenericHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; SettableSlot other = TheSameObject(slot); other.Share = l.Map(v => v); return S.Hold(slot.Share!.Sample()); });

    // S1775. A helper with a body sets a plain cell, and the call is in an if statement. The assignment always runs in the helper, and the helper does not always run.
    public static Cell<int> HelperWithABodyInAnIfStatementSetsAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; if (Never) { ClearASlot(x); } return S.Hold(x.Share!.Sample()); });

    // S1776. A helper in a loop makes an object, and gives a plain cell to the object of the turn before it. The object of the last turn keeps its pending cell.
    public static Cell<int> ObjectOfAHelperInALoopWhereTheHelperChangesTheObjectBeforeIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? last = null; for (int i = 0; i < 2; i++) { last = NextSlot(last, l.Map(v => v)); } return S.Hold(last!.Share!.Sample()); });

    // S1777. A local function makes the object of each call. Two calls make two objects.
    public static Cell<int> TwoObjectsOfALocalFunction() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot Make(Cell<int> share) { SettableSlot made = new() { Share = share }; return made; } SettableSlot a = Make(l.Map(v => v)); SettableSlot b = Make(Plain); b.Share = Plain; return S.Hold(a.Share!.Sample()); });

    // S1778. A lambda of a query makes the object of each element.
    public static Cell<int> TwoObjectsOfALambda() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = new[] { 0, 1 }.Select(i => new SettableSlot { Share = i == 0 ? l.Map(v => v) : Plain }).ToList(); SettableSlot first = list[0]; SettableSlot second = list[1]; second.Share = Plain; return S.Hold(first.Share!.Sample()); });

    // S1779. A helper returns a list as an interface, and the block names the type of the list.
    public static Cell<int> ListFromAHelperThatReturnsAnInterfaceUnderTwoNames() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = (List<Cell<int>>)MakeAListAsAnInterface(); List<Cell<int>> other = cells; other.Add(l.Map(v => v)); return S.Hold(cells[1].Sample()); });

    // S1780. A list in a member with an interface type, with two names that have the type of the list.
    public static Cell<int> ListInAnInterfaceMemberUnderTwoNames() =>
        Cell.Loop<int>().WithoutCaptures(l => { ListBehindAnInterface box = new(); List<Cell<int>> cells = (List<Cell<int>>)box.Cells; List<Cell<int>> other = cells; other.Add(l.Map(v => v)); return S.Hold(cells[0].Sample()); });

    // S1781. IsSubsetOf where the argument got a different comparer by the name of the parameter.
    public static Cell<int> IsSubsetOfAnArgumentThatGotItsComparerByName() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> set = [new(l.Map(v => v))]; HashSet<SameOwn> other = new(comparer: ReferenceEqualityComparer.Instance); other.Add(new(Plain)); Kept.Add(set.IsSubsetOf(other)); return S.Hold(0); });

    // S1782. An object of a foreach loop gets a pending cell and then a plain one, in each turn.
    public static Cell<int> PlainAfterPendingInAnObjectOfAForeachLoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; foreach (int i in new[] { 0, 1 }) { SettableSlot x = new() { Share = l.Map(v => v) }; x.Share = Plain; list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1783. The same for a while loop.
    public static Cell<int> PlainAfterPendingInAnObjectOfAWhileLoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; int i = 0; while (i < 2) { SettableSlot x = new() { Share = l.Map(v => v) }; x.Share = Plain; list.Add(x); i++; } return S.Hold(list[0].Share!.Sample()); });

    // S1784. The same for a do loop.
    public static Cell<int> PlainAfterPendingInAnObjectOfADoLoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; int i = 0; do { SettableSlot x = new() { Share = l.Map(v => v) }; x.Share = Plain; list.Add(x); i++; } while (i < 2); return S.Hold(list[0].Share!.Sample()); });

    // S1785. An object in a member, where the walk did not read the constructor of its owner. A plain cell replaces its pending cell.
    public static Cell<int> PlainAfterPendingInAnObjectInAMember() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotInAMember box = new(); box.Slot.Share = l.Map(v => v); box.Slot.Share = Plain; return S.Hold(box.Slot.Share!.Sample()); });

    // S1786. A hold of a stream of an object. The value of the hold in the block is the first value, and the object of the stream comes after the close.
    public static Cell<int> HoldOfAStreamOfAnObjectWithAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = l.Map(v => v) }; SettableSlot other = new() { Share = Plain }; Cell<SettableSlot> held = S.Map(_ => slot).Hold(other); SettableSlot now = held.Sample(); return S.Hold(now.Share!.Sample()); });

    // S1787. A dictionary has a pending key and an object of the store as its value. The pair of the foreach is not that object, and has the state of its key.
    public static Cell<int> KeyOfAPairOfADictionaryThatHoldsAnObjectOfTheStore() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = new() { Share = Plain }; Dictionary<Cell<int>, SettableSlot> map = new() { [l.Map(v => v)] = slot }; foreach (KeyValuePair<Cell<int>, SettableSlot> pair in map) { Kept.Add(pair.Key.Sample()); } return S.Hold(0); });

    // S1788. A local function in a loop makes an object, and gives a plain cell to the object of the turn before it.
    public static Cell<int> ObjectOfALocalFunctionInALoopThatChangesTheObjectBeforeIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? last = null; SettableSlot Next(SettableSlot? before, Cell<int> share) { SettableSlot made = new() { Share = share }; (before ?? made).Share = Plain; return made; } for (int i = 0; i < 2; i++) { last = Next(last, l.Map(v => v)); } return S.Hold(last!.Share!.Sample()); });

    // S1789. The same, where a helper made the object. The type of such an object is only the type that the code names, and a pair is still not that object.
    public static Cell<int> KeyOfAPairOfADictionaryThatHoldsAnObjectFromAHelper() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot slot = MakeAPlainSlot(); Dictionary<Cell<int>, SettableSlot> map = new() { [l.Map(v => v)] = slot }; foreach (KeyValuePair<Cell<int>, SettableSlot> pair in map) { Kept.Add(pair.Key.Sample()); } return S.Hold(0); });

    // S1790. An initializer on a set whose constructor got only a comparer. The set is empty before the initializer, and its first element compares nothing.
    public static Cell<int> InitializerOnASetThatGotOnlyAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> made = new(EqualityComparer<SameOwn>.Default) { new(l.Map(v => v)) }; Kept.Add(made); return S.Hold(0); });

    // S1791. The same for a constructor that got only a capacity.
    public static Cell<int> InitializerOnASetThatGotOnlyACapacity() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> made = new(4) { new(l.Map(v => v)) }; Kept.Add(made); return S.Hold(0); });

    // S1792. The same for a dictionary whose constructor got a capacity and a comparer.
    public static Cell<int> InitializerOnADictionaryThatGotACapacityAndAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { Dictionary<SameOwn, int> made = new(4, EqualityComparer<SameOwn>.Default) { [new(l.Map(v => v))] = 1 }; Kept.Add(made); return S.Hold(0); });

    // S1793. The same for a SortedSet whose constructor got only a comparer.
    public static Cell<int> InitializerOnASortedSetThatGotOnlyAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { SortedSet<RankedOwn> made = new(Comparer<RankedOwn>.Default) { new(l.Map(v => v)) }; Kept.Add(made); return S.Hold(0); });

    // S1794. The same for a creation that names its type.
    public static Cell<int> InitializerOnANamedSetThatGotOnlyAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> made = new HashSet<SameOwn>(EqualityComparer<SameOwn>.Default) { new SameOwn(l.Map(v => v)) }; Kept.Add(made); return S.Hold(0); });

    // S1795. The set compares the second element of such an initializer with the first one, which is pending.
    public static Cell<int> SecondElementOfAnInitializerOnASetThatGotOnlyAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> made = new(EqualityComparer<SameOwn>.Default) { new(l.Map(v => v)), new(Plain) }; Kept.Add(made); return S.Hold(0); });

    // S1796. The same for a constructor that got only a capacity.
    public static Cell<int> SecondElementOfAnInitializerOnASetThatGotOnlyACapacity() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> made = new(4) { new(l.Map(v => v)), new(Plain) }; Kept.Add(made); return S.Hold(0); });

    // S1797. A constructor that got a sequence and a comparer. The set has the pending object of the sequence before the initializer.
    public static Cell<int> InitializerOnASetThatGotASequenceAndAComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { HashSet<SameOwn> made = new(new[] { new SameOwn(l.Map(v => v)) }, EqualityComparer<SameOwn>.Default) { new(Plain) }; Kept.Add(made); return S.Hold(0); });

    // S1798. A query expression selects a pending cell. An element of its result is that cell.
    public static Cell<int> QueryExpressionSelectsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> list = (from i in new[] { 0 } select l.Map(v => v)).ToList(); return S.Hold(list[0].Sample()); });

    // S1799. A query expression selects an object with a pending cell.
    public static Cell<int> QueryExpressionSelectsAnObjectWithAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = (from i in new[] { 0 } select new SettableSlot { Share = l.Map(v => v) }).ToList(); return S.Hold(list[0].Share!.Sample()); });

    // S1800. A foreach over a query expression that selects an object with a pending cell.
    public static Cell<int> ForeachOverAQueryExpressionThatSelectsAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { foreach (SettableSlot each in from i in new[] { 0 } select new SettableSlot { Share = l.Map(v => v) }) { Kept.Add(each.Share!.Sample()); } return S.Hold(0); });

    // S1801. A query expression selects an anonymous object with a pending cell.
    public static Cell<int> QueryExpressionSelectsAnAnonymousObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { var list = (from i in new[] { 0 } select new { Share = l.Map(v => v) }).ToList(); return S.Hold(list[0].Share.Sample()); });

    // S1802. The range variable of a from clause is an element of its source. The select clause samples it.
    public static Cell<int> SelectClauseSamplesItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells select c.Sample()).ToList().Count); });

    // S1803. The where clause samples the range variable.
    public static Cell<int> WhereClauseSamplesItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells where c.Sample() > 0 select 1).ToList().Count); });

    // S1804. The orderby clause samples the range variable.
    public static Cell<int> OrderByClauseSamplesItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells orderby c.Sample() select 1).ToList().Count); });

    // S1805. A let clause gives its variable the state of its expression.
    public static Cell<int> LetClauseGivesItsVariableTheRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells let d = c select d.Sample()).ToList().Count); });

    // S1806. The range variable of a second from clause.
    public static Cell<int> SecondFromClauseSamplesItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from i in new[] { 0 } from c in cells select c.Sample()).ToList().Count); });

    // S1807. The range variable of a join clause.
    public static Cell<int> JoinClauseSamplesItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from i in new[] { 0 } join c in cells on i equals 0 select c.Sample()).ToList().Count); });

    // S1808. The variable of a join into clause has the elements of the second source.
    public static Cell<int> JoinIntoClauseSamplesAnElementOfItsGroup() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from i in new[] { 0 } join c in cells on i equals 0 into found select found.First().Sample()).ToList().Count); });

    // S1809. The variable after into has what the select clause before it makes.
    public static Cell<int> ContinuationSamplesWhatTheSelectBeforeItMakes() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells select c into d select d.Sample()).ToList().Count); });

    // S1810. A group clause makes groups of its elements. An element of a group is a pending cell.
    public static Cell<int> GroupClauseKeepsItsElements() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; var groups = (from c in cells group c by 1).ToList(); return S.Hold(groups[0].First().Sample()); });

    // S1811. The key of a group is a pending cell.
    public static Cell<int> GroupClauseKeepsItsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; var groups = (from c in cells group 1 by c).ToList(); return S.Hold(groups[0].Key.Sample()); });

    // S1812. The variable after into of a group clause is a group.
    public static Cell<int> ContinuationOfAGroupSamplesAnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells group c by 1 into g select g.First().Sample()).ToList().Count); });

    // S1813. The range variable itself is the result. An element of the result is an element of the source.
    public static Cell<int> QueryExpressionSelectsItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; List<Cell<int>> list = (from c in cells select c).ToList(); return S.Hold(list[0].Sample()); });

    // S1814. A query expression over a list with a pending cell selects a plain cell. The result has only what the select clause makes.
    public static Cell<int> QueryExpressionOverAPendingSourceSelectsAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; List<Cell<int>> list = (from c in cells select Plain).ToList(); return S.Hold(list[0].Sample()); });

    // S1815. A query expression over plain cells samples its range variable.
    public static Cell<int> SelectClauseSamplesAPlainRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [Plain]; return S.Hold((from c in cells select c.Sample()).ToList().Count); });

    // S1816. A query expression that samples its range variable, and that the block only keeps.
    public static Cell<int> KeptQueryExpressionThatSamplesItsRangeVariable() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; Kept.Add(from c in cells select c.Sample()); return S.Hold(0); });

    // S1817. A let clause gives its variable a plain cell beside a pending range variable.
    public static Cell<int> LetClauseGivesItsVariableAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = [l.Map(v => v)]; return S.Hold((from c in cells let d = Plain select d.Sample()).ToList().Count); });

    // S1818. Add of an ImmutableDictionary with a key that it has compares the two values. The value that it has gets the call, and it is pending.
    public static Cell<int> ImmutableDictionaryAddWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Add("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1819. The same, where Equals reads the value that it gets, and the value that the call gives is pending.
    public static Cell<int> ImmutableDictionaryAddWhereEqualsReadsTheGivenValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikePeer> map = ImmutableDictionary<string, AlikePeer>.Empty.Add("a", new AlikePeer(Plain)); Kept.Add(map.Add("a", new AlikePeer(l.Map(v => v)))); return S.Hold(0); });

    // S1820. SetItem compares the two values also.
    public static Cell<int> ImmutableDictionarySetItemWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.SetItem("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1821. SetItems compares the two values also.
    public static Cell<int> ImmutableDictionarySetItemsWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.SetItems(new[] { new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain)) })); return S.Hold(0); });

    // S1822. AddRange compares the two values also.
    public static Cell<int> ImmutableDictionaryAddRangeWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.AddRange(new[] { new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain)) })); return S.Hold(0); });

    // S1823. AddRange of an ImmutableDictionary also gives the value that it has to its own Equals. Equals reads the value that it gets, and the value of the dictionary is pending.
    public static Cell<int> ImmutableDictionaryAddRangeComparesTheValueThatItHasWithItself() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikePeer> map = ImmutableDictionary<string, AlikePeer>.Empty.Add("a", new AlikePeer(l.Map(v => v))); Kept.Add(map.AddRange(new[] { new KeyValuePair<string, AlikePeer>("a", new AlikePeer(Plain)) })); return S.Hold(0); });

    // S1824. Add of a pair through ICollection of a builder.
    public static Cell<int> ImmutableDictionaryBuilderAddOfAPairWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn>.Builder map = ImmutableDictionary.CreateBuilder<string, AlikeOwn>(); map.Add("a", new AlikeOwn(l.Map(v => v))); map.Add(new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1825. Add of a builder of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryBuilderAddWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn>.Builder map = ImmutableDictionary.CreateBuilder<string, AlikeOwn>(); map.Add("a", new AlikeOwn(l.Map(v => v))); map.Add("a", new AlikeOwn(Plain)); return S.Hold(0); });

    // S1826. The indexer of a builder of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryBuilderIndexerWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn>.Builder map = ImmutableDictionary.CreateBuilder<string, AlikeOwn>(); map.Add("a", new AlikeOwn(l.Map(v => v))); map["a"] = new AlikeOwn(Plain); return S.Hold(0); });

    // S1827. AddRange of a builder of an ImmutableDictionary.
    public static Cell<int> ImmutableDictionaryBuilderAddRangeWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn>.Builder map = ImmutableDictionary.CreateBuilder<string, AlikeOwn>(); map.Add("a", new AlikeOwn(l.Map(v => v))); map.AddRange(new[] { new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain)) }); return S.Hold(0); });

    // S1828. Add of an ImmutableSortedDictionary. The keys have CompareTo, and the values have Equals.
    public static Cell<int> ImmutableSortedDictionaryAddWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<string, AlikeOwn> map = ImmutableSortedDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Add("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1829. SetItem of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionarySetItemWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<string, AlikeOwn> map = ImmutableSortedDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.SetItem("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1830. AddRange of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryAddRangeWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<string, AlikeOwn> map = ImmutableSortedDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.AddRange(new[] { new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain)) })); return S.Hold(0); });

    // S1831. Add of a builder of an ImmutableSortedDictionary.
    public static Cell<int> ImmutableSortedDictionaryBuilderAddWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<string, AlikeOwn>.Builder map = ImmutableSortedDictionary.CreateBuilder<string, AlikeOwn>(); map.Add("a", new AlikeOwn(l.Map(v => v))); map.Add("a", new AlikeOwn(Plain)); return S.Hold(0); });

    // S1832. ToImmutableDictionary over two pairs with one key, where the first value is pending.
    public static Cell<int> ToImmutableDictionaryWhereAKeyRepeats() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, AlikeOwn>[] pairs = [new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(l.Map(v => v))), new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain))]; Kept.Add(pairs.ToImmutableDictionary()); return S.Hold(0); });

    // S1833. CreateRange of an ImmutableSortedDictionary over two pairs with one key.
    public static Cell<int> CreateRangeOfAnImmutableSortedDictionaryWhereAKeyRepeats() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, AlikeOwn>[] pairs = [new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(l.Map(v => v))), new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain))]; Kept.Add(ImmutableSortedDictionary.CreateRange(pairs)); return S.Hold(0); });

    // S1834. Contains of a pair. The value that the dictionary has gets the call.
    public static Cell<int> ImmutableDictionaryContainsOfAPairWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Contains(new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain)))); return S.Hold(0); });

    // S1835. Add where the value that the call gives is pending. The plain value of the dictionary gets the call.
    public static Cell<int> ImmutableDictionaryAddWhereTheGivenValueIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(Plain)); Kept.Add(map.Add("a", new AlikeOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1836. Add where Equals reads the value that it gets, and the value of the dictionary is pending.
    public static Cell<int> ImmutableDictionaryAddWhereEqualsReadsThePlainGivenValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikePeer> map = ImmutableDictionary<string, AlikePeer>.Empty.Add("a", new AlikePeer(l.Map(v => v))); Kept.Add(map.Add("a", new AlikePeer(Plain))); return S.Hold(0); });

    // S1837. Remove and TryGetValue compare no value.
    public static Cell<int> ImmutableDictionaryRemoveAndTryGetValueCompareNoValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Remove("a")); Kept.Add(map.TryGetValue("a", out AlikeOwn? found)); return S.Hold(0); });

    // S1838. ContainsValue gives the call to the value that the call gives.
    public static Cell<int> ImmutableDictionaryContainsValueWhereTheValueThatItHasIsPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.ContainsValue(new AlikeOwn(Plain))); return S.Hold(0); });

    // S1839. A value with no Equals of its own.
    public static Cell<int> ImmutableDictionaryAddOfAValueWithNoEquals() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, SettableSlot> map = ImmutableDictionary<string, SettableSlot>.Empty.Add("a", new SettableSlot { Share = l.Map(v => v) }); Kept.Add(map.SetItem("a", new SettableSlot { Share = Plain })); return S.Hold(0); });

    // S1840. ToImmutableDictionary over one pair compares nothing.
    public static Cell<int> ToImmutableDictionaryOfOnePair() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, AlikeOwn>[] pairs = [new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(l.Map(v => v)))]; Kept.Add(pairs.ToImmutableDictionary()); return S.Hold(0); });

    // S1841. Add to the empty dictionary of the platform compares nothing, also when Equals reads the value that it gets.
    public static Cell<int> ImmutableDictionaryAddToTheEmptyDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableDictionary<string, AlikePeer>.Empty.Add("a", new AlikePeer(l.Map(v => v)))); return S.Hold(0); });

    // S1842. The same for the empty sorted dictionary.
    public static Cell<int> ImmutableSortedDictionaryAddToTheEmptyDictionary() =>
        Cell.Loop<int>().WithoutCaptures(l => { Kept.Add(ImmutableSortedDictionary<string, AlikePeer>.Empty.Add("a", new AlikePeer(l.Map(v => v)))); return S.Hold(0); });

    // S1843. ToImmutableDictionary with a comparer for the keys compares the values with Equals, as with no comparer.
    public static Cell<int> ToImmutableDictionaryWithAKeyComparerWhereAKeyRepeats() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, AlikeOwn>[] pairs = [new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(l.Map(v => v))), new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain))]; Kept.Add(pairs.ToImmutableDictionary(StringComparer.Ordinal)); return S.Hold(0); });

    // S1844. ToImmutableDictionary with a function for the key makes the values from the elements of its source.
    public static Cell<int> ToImmutableDictionaryWithAKeyFunctionWhereAKeyRepeats() =>
        Cell.Loop<int>().WithoutCaptures(l => { AlikeOwn[] items = [new(l.Map(v => v)), new(Plain)]; Kept.Add(items.ToImmutableDictionary(static _ => "a")); return S.Hold(0); });

    // S1845. A query expression with a continuation. The last select clause makes the elements of the result.
    public static Cell<int> QueryExpressionWithAContinuationSelectsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> list = (from i in new[] { 0 } select i into j select l.Map(v => v)).ToList(); return S.Hold(list[0].Sample()); });

    // S1846. ToImmutableDictionary with a comparer for the values calls that comparer, and not Equals of a value.
    public static Cell<int> ToImmutableDictionaryWithAValueComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { KeyValuePair<string, AlikeOwn>[] pairs = [new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(l.Map(v => v))), new KeyValuePair<string, AlikeOwn>("a", new AlikeOwn(Plain))]; Kept.Add(pairs.ToImmutableDictionary(StringComparer.Ordinal, new AlwaysTheSame())); return S.Hold(0); });

    // S1847. Remove of a key of a builder compares no value.
    public static Cell<int> ImmutableDictionaryBuilderRemoveOfAKeyComparesNoValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn>.Builder map = ImmutableDictionary.CreateBuilder<string, AlikeOwn>(); map.Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Remove("a")); return S.Hold(0); });

    // S1848. A set of this code whose constructor gets a number and fills the set. The set is not empty before the initializer, and its element reads the pending object that the initializer gives.
    public static Cell<int> InitializerOnASetOfThisCodeThatItsConstructorFills() =>
        Cell.Loop<int>().WithoutCaptures(l => { FilledPeers made = new(1) { new(l.Map(v => v)) }; Kept.Add(made); return S.Hold(0); });

    // S1849. The range variable is one element of its source, also through the type object.
    public static Cell<int> RangeVariableThroughObjectIsAnElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> rows = [new(l.Map(v => v))]; return S.Hold((from row in rows let o = (object)row select o.ToString()).ToList().Count); });

    // S1850. The variable of a join into clause is a group, and not one element. ToString of a group calls no member of an element.
    public static Cell<int> GroupOfAJoinIntoClauseThroughObjectIsNoElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Titled> rows = [new(l.Map(v => v))]; return S.Hold((from i in new[] { 0 } join row in rows on i equals 0 into found let o = (object)found select o.ToString()).ToList().Count); });

    // S1851. The result of a query expression is a sequence, and not one element.
    public static Cell<int> ResultOfAQueryExpressionThroughObjectIsNoElement() =>
        Cell.Loop<int>().WithoutCaptures(l => { object made = from i in new[] { 0 } select new Titled(l.Map(v => v)); Kept.Add(made.ToString()!); return S.Hold(0); });

    // S1852. A dictionary that the code made with a comparer for its keys only compares its values with Equals.
    public static Cell<int> ImmutableDictionaryThatGotAKeyComparerOnly() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary.Create<string, AlikeOwn>(StringComparer.Ordinal).Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Add("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1853. A dictionary that the code made with a comparer for its values calls that comparer, and not Equals of a value.
    public static Cell<int> ImmutableDictionaryThatGotAValueComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary.Create<string, AlikeOwn>(StringComparer.Ordinal, new AlwaysTheSame()).Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Add("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1854. The same for WithComparers.
    public static Cell<int> ImmutableDictionaryThatGotAValueComparerFromWithComparers() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary<string, AlikeOwn>.Empty.WithComparers(StringComparer.Ordinal, new AlwaysTheSame()).Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Add("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1855. A dictionary that got null for the comparer of its values has the default one, and compares its values with Equals.
    public static Cell<int> ImmutableDictionaryThatGotNullForItsValueComparer() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<string, AlikeOwn> map = ImmutableDictionary.Create<string, AlikeOwn>(StringComparer.Ordinal, null).Add("a", new AlikeOwn(l.Map(v => v))); Kept.Add(map.Add("a", new AlikeOwn(Plain))); return S.Hold(0); });

    // S1856. A local of the block keeps the first object of a loop, and only that object gets a pending cell.
    public static Cell<int> TurnFirstObjectKeptInALocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { first = x; x.Share = l.Map(v => v); } else { x.Share = Plain; } } return S.Hold(first!.Share!.Sample()); });

    // S1857. Each object of a loop starts with a pending cell, and only the second one gets a plain cell.
    public static Cell<int> TurnPlainForTheSecondObjectOnly() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i > 0) { x.Share = Plain; } list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1858. A second local of the body of a loop is a name of the object of that turn.
    public static Cell<int> TurnChangeThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; y.Share = l.Map(v => v); list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1859. An element of a list that the turn makes is a name of the object of that turn.
    public static Cell<int> TurnChangeThroughAnElementOfAListOfTheTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; List<SettableSlot> one = [x]; one[0].Share = l.Map(v => v); list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1860. A member of an object that the turn makes has the object of that turn. A change through the local is a change for the member.
    public static Cell<int> TurnChangeThroughALocalThatAnObjectOfTheTurnHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SlotHolder> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SlotHolder holder = new() { Item = x }; x.Share = l.Map(v => v); list.Add(holder); } return S.Hold(list[0].Item!.Share!.Sample()); });

    // S1861. A tuple that the turn makes has the object of that turn.
    public static Cell<int> TurnChangeThroughALocalThatATupleHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<(SettableSlot First, int Second)> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; (SettableSlot First, int Second) pair = (x, i); x.Share = l.Map(v => v); list.Add(pair); } return S.Hold(list[0].First.Share!.Sample()); });

    // S1862. A change through a second local after the list of the block got the object.
    public static Cell<int> TurnChangeAfterTheListGotTheObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; list.Add(x); SettableSlot y = x; y.Share = l.Map(v => v); } return S.Hold(list[0].Share!.Sample()); });

    // S1863. The second local in the body of a foreach.
    public static Cell<int> TurnForeachChangeThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; foreach (int i in new[] { 0, 1 }) { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; y.Share = l.Map(v => v); list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1864. The second local in the body of a while.
    public static Cell<int> TurnWhileChangeThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; int i = 0; while (i < 2) { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; y.Share = l.Map(v => v); list.Add(x); i++; } return S.Hold(list[0].Share!.Sample()); });

    // S1865. The second local in the body of a do.
    public static Cell<int> TurnDoChangeThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; int i = 0; do { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; y.Share = l.Map(v => v); list.Add(x); i++; } while (i < 2); return S.Hold(list[0].Share!.Sample()); });

    // S1866. A local of the block has the object of a turn before, which gets a pending cell. The plain cell that comes after is for the object of this turn only.
    public static Cell<int> TurnOlderObjectGetsAPendingCellThroughALocalOfTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? keep = null; List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; keep ??= x; keep.Share = l.Map(v => v); x.Share = Plain; list.Add(x); } return S.Hold(keep!.Share!.Sample()); });

    // S1867. An element of the list of the block is the object of a turn before, which gets a pending cell.
    public static Cell<int> TurnOlderElementGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; list.Add(x); list[0].Share = l.Map(v => v); x.Share = Plain; } return S.Hold(list[0].Share!.Sample()); });

    // S1868. After the loop, a local has one of its objects, and a plain cell for that one is not for the others.
    public static Cell<int> TurnWriteAfterTheLoopThroughOneOfTwoLocals() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? a = null; SettableSlot? b = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i == 0) { a = x; } else { b = x; } } b!.Share = Plain; return S.Hold(a!.Share!.Sample()); });

    // S1869. An inner loop makes the objects. After it, the outer turn gives a plain cell to one of them.
    public static Cell<int> TurnWriteInTheOuterTurnAfterTheInnerLoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int j = 0; j < 1; j++) { SettableSlot? a = null; SettableSlot? b = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i == 0) { a = x; } else { b = x; } } b!.Share = Plain; Kept.Add(a!.Share!.Sample()); } return S.Hold(0); });

    // S1870. A member of an object of the block keeps the first object of the loop.
    public static Cell<int> TurnObjectInAMemberOfAnObjectOfTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder outer = new(); for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { outer.Item = x; x.Share = l.Map(v => v); } else { x.Share = Plain; } } return S.Hold(outer.Item!.Share!.Sample()); });

    // S1871. A member of an object of the outer turn keeps the first object of the inner loop.
    public static Cell<int> TurnObjectInAMemberOfAnObjectOfTheOuterTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int j = 0; j < 1; j++) { SlotHolder outer = new(); for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { outer.Item = x; x.Share = l.Map(v => v); } else { x.Share = Plain; } } Kept.Add(outer.Item!.Share!.Sample()); } return S.Hold(0); });

    // S1872. The creation in the initializer of a for runs one time, and its object is not an object of a turn. Here, no turn runs, and the plain cell of the body is for no object.
    public static Cell<int> TurnObjectOfTheInitializerOfAForWhenNoTurnRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? keep = null; int n = 0; SettableSlot? x; for (x = new SettableSlot { Share = l.Map(v => v) }, keep = x; n > 0; n--) { x.Share = Plain; } return S.Hold(keep!.Share!.Sample()); });

    // S1873. A plain cell that only some turns give is not for each object.
    public static Cell<int> TurnPlainUnderAConditionAfterPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i > 5) { x.Share = Plain; } Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1874. An inner loop that can run no turn gives the plain cell.
    public static Cell<int> TurnWriteInAnInnerLoop() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i == 0) { first = x; } for (int j = 0; j < i; j++) { x.Share = Plain; } } return S.Hold(first!.Share!.Sample()); });

    // S1875. A continue statement comes before the plain cell, thus the first object keeps its pending cell.
    public static Cell<int> TurnContinueBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i == 0) { first = x; } if (i == 0) { continue; } x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1876. A break statement comes before the plain cell, thus the last object keeps its pending cell.
    public static Cell<int> TurnBreakBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? last = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; last = x; if (i == 1) { break; } x.Share = Plain; } return S.Hold(last!.Share!.Sample()); });

    // S1877. A throw statement comes before the plain cell, and the code around the loop catches the exception.
    public static Cell<int> TurnThrowBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? last = null; try { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; last = x; if (i == 1) { throw new InvalidOperationException(); } x.Share = Plain; } } catch (InvalidOperationException) { } return S.Hold(last!.Share!.Sample()); });

    // S1878. The result of a call in a loop is the object of that turn, and a second local is a name of it.
    public static Cell<int> TurnResultOfACallChangedThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = MakeASlotWith(Plain); SettableSlot y = x; y.Share = l.Map(v => v); list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1879. A member of the result of a call in a loop has the object that the turn gave to the call.
    public static Cell<int> TurnMemberOfTheResultOfACall() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SlotHolder> list = []; for (int i = 0; i < 2; i++) { SlotHolder holder = HolderWith(new SettableSlot { Share = Plain }); list.Add(holder); SettableSlot y = holder.Item!; y.Share = l.Map(v => v); } return S.Hold(list[0].Item!.Share!.Sample()); });

    // S1880. A plain cell through a second local takes the pending cell of the object of the turn away.
    public static Cell<int> TurnPlainAfterPendingThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; y.Share = Plain; list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1881. A turn makes two objects, and only the second one gets a pending cell.
    public static Cell<int> TurnTwoObjectsOfOneTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot other = new() { Share = Plain }; other.Share = l.Map(v => v); list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1882. A local of the body gets a new object, and is then not a name of the first one.
    public static Cell<int> TurnALocalOfTheTurnThatGetsANewObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; y = new() { Share = Plain }; y.Share = l.Map(v => v); list.Add(x); } return S.Hold(list[0].Share!.Sample()); });

    // S1883. A member of an object of the same turn reads the state that the object of the turn has now.
    public static Cell<int> TurnObjectInAMemberOfAnObjectOfTheSameTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SlotHolder holder = new() { Item = x }; x.Share = Plain; Kept.Add(holder.Item!.Share!.Sample()); } return S.Hold(0); });

    // S1884. A plain cell through the same local, in the body of a do.
    public static Cell<int> TurnDoPlainAfterPendingThroughTheSameLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { int i = 0; do { SettableSlot x = new() { Share = l.Map(v => v) }; x.Share = Plain; Kept.Add(x.Share!.Sample()); i++; } while (i < 2); return S.Hold(0); });

    // S1885. A plain cell in a lock statement at the first level of the body runs in each turn.
    public static Cell<int> TurnPlainInALockAfterPending() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; lock (Kept) { y.Share = Plain; } Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1886. The result of a call in a loop gets a plain cell through a second local.
    public static Cell<int> TurnResultOfACallPlainThroughASecondLocal() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = MakeASlotWith(l.Map(v => v)); SettableSlot y = x; y.Share = Plain; Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1887. A return statement in the loop of a method comes before the plain cell.
    public static Cell<int> TurnReturnBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot got = LastSlotOfALoopThatReturns(l.Map(v => v)); return S.Hold(got.Share!.Sample()); });

    // S1888. A goto statement comes before the plain cell.
    public static Cell<int> TurnGotoBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i == 0) { first = x; goto skip; } x.Share = Plain; skip: ; } return S.Hold(first!.Share!.Sample()); });

    // S1889. A throw expression comes before the plain cell, and the code around the loop catches the exception.
    public static Cell<int> TurnThrowExpressionBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? last = null; try { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; last = x; int k = i == 1 ? throw new InvalidOperationException() : 0; Kept.Add(k); x.Share = Plain; } } catch (InvalidOperationException) { } return S.Hold(last!.Share!.Sample()); });

    // S1890. A lambda that the turn runs one time returns before its plain cell.
    public static Cell<int> TurnWriteInALambdaThatReturnsEarly() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; if (i == 0) { first = x; } Transaction.RunVoid(() => { if (i == 0) { return; } x.Share = Plain; }); } return S.Hold(first!.Share!.Sample()); });

    // S1891. A break statement of a switch statement does not end the turn.
    public static Cell<int> TurnBreakOfASwitchBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; switch (i) { case 0: break; default: break; } y.Share = Plain; Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1892. A continue statement and a break statement of an inner loop do not end the turn of the outer one.
    public static Cell<int> TurnJumpsOfAnInnerLoopBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; for (int j = 0; j < 3; j++) { if (j == 0) { continue; } break; } y.Share = Plain; Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1893. A continue statement after the plain cell does not stop that write.
    public static Cell<int> TurnContinueAfterTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; y.Share = Plain; Kept.Add(x.Share!.Sample()); if (i == 0) { continue; } Kept.Add(i); } return S.Hold(0); });

    // S1894. A member that the turn did not set is one object for each name of its owner. A local of the block has the owner after a for.
    public static Cell<int> TurnMemberThatTheTurnDidNotSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder? keep = null; for (int i = 0; i < 2; i++) { SlotHolder holder = HolderWithItsOwnSlot(); keep = holder; SettableSlot y = holder.Item!; y.Share = l.Map(v => v);  } return S.Hold(keep!.Item!.Share!.Sample()); });

    // S1895. The same, after a foreach.
    public static Cell<int> TurnForeachMemberThatTheTurnDidNotSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder? keep = null; foreach (int i in new[] { 0, 1 }) { SlotHolder holder = HolderWithItsOwnSlot(); keep = holder; SettableSlot y = holder.Item!; y.Share = l.Map(v => v); } return S.Hold(keep!.Item!.Share!.Sample()); });

    // S1896. The same, after a foreach that deconstructs its element.
    public static Cell<int> TurnForeachOfPartsMemberThatTheTurnDidNotSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder? keep = null; foreach ((int i, int j) in new[] { (0, 0), (1, 1) }) { SlotHolder holder = HolderWithItsOwnSlot(); keep = holder; SettableSlot y = holder.Item!; y.Share = l.Map(v => v); } return S.Hold(keep!.Item!.Share!.Sample()); });

    // S1897. The same, after a while.
    public static Cell<int> TurnWhileMemberThatTheTurnDidNotSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder? keep = null; int i = 0; while (i < 2) { SlotHolder holder = HolderWithItsOwnSlot(); keep = holder; SettableSlot y = holder.Item!; y.Share = l.Map(v => v); i++; } return S.Hold(keep!.Item!.Share!.Sample()); });

    // S1898. The same, after a do.
    public static Cell<int> TurnDoMemberThatTheTurnDidNotSet() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder? keep = null; int i = 0; do { SlotHolder holder = HolderWithItsOwnSlot(); keep = holder; SettableSlot y = holder.Item!; y.Share = l.Map(v => v); i++; } while (i < 2); return S.Hold(keep!.Item!.Share!.Sample()); });

    // S1899. A member of the object that a method is in keeps the first object of the loop of that method.
    public static Cell<int> TurnObjectInAMemberOfThisObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new SlotKeeper().FirstShareOfTwo(l.Map(v => v)).Sample()); });

    // S1900. A member of a static object keeps the first object of the loop.
    public static Cell<int> TurnObjectInAMemberOfAStaticObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { SharedHolder.Item = x; } SharedHolder.Item!.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(SharedHolder.Item!.Share!.Sample()); });

    // S1901. The result of a call can be the same object in each turn, and its member keeps the first object of the loop.
    public static Cell<int> TurnObjectInAMemberOfTheResultOfACall() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SlotHolder h = TheHolderOfEachCall(); if (i == 0) { h.Item = x; } h.Item!.Share = l.Map(v => v); x.Share = Plain; Kept.Add(h.Item!.Share!.Sample()); } return S.Hold(0); });

    // S1902. A function from a call that returned keeps the first object of the loop in a local of that call.
    public static Cell<int> TurnLocalOfACallThatReturnedKeepsTheFirstObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<SettableSlot, SettableSlot> first = FirstSlotKeeper(); for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot k = first(x); k.Share = l.Map(v => v); x.Share = Plain; Kept.Add(k.Share!.Sample()); } return S.Hold(0); });

    // S1903. A list of the outer turn gets each object of the inner loop.
    public static Cell<int> TurnInnerObjectInAListOfTheOuterTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int j = 0; j < 1; j++) { List<SettableSlot> row = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; row.Add(x); if (i == 0) { x.Share = l.Map(v => v); } } Kept.Add(row[0].Share!.Sample()); } return S.Hold(0); });

    // S1904. The initializer of a for makes one object for all the turns, and a local of the body is a name of it.
    public static Cell<int> TurnObjectOfTheInitializerOfAForChangedThroughALocalOfTheBody() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? keep = null; int n = 0; SettableSlot? x; for (x = new SettableSlot { Share = Plain }, keep = x; n < 2; n++) { SettableSlot y = x; y.Share = l.Map(v => v); } return S.Hold(keep!.Share!.Sample()); });

    // S1905. A member that a constructor sets has the object of the turn, and the constructor runs in that turn.
    public static Cell<int> TurnObjectInAMemberThatAConstructorSets() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SlotOwner owner = new(x); SettableSlot y = owner.Item; y.Share = Plain; Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1906. A local of a callee has the object of the turn, and the callee runs in that turn.
    public static Cell<int> TurnObjectThroughALocalOfACallee() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = SameSlotThroughALocal(x); y.Share = Plain; Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1907. A list got the first object of a local of the body. The local then gets a new object, and a change to it is not a change for the list.
    public static Cell<int> TurnALocalThatAListGotAndThatGetsANewObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> other = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; other.Add(y); y = new() { Share = Plain }; y.Share = l.Map(v => v); Kept.Add(y); } return S.Hold(other[0].Share!.Sample()); });

    // S1908. A deconstruction of a tuple expression gives the plain cell in each turn.
    public static Cell<int> TurnPlainFromATupleExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; (y.Share, int k) = (Plain, i); Kept.Add(k); Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1909. A deconstruction of the tuple from a call gives the plain cell in each turn.
    public static Cell<int> TurnPlainFromATupleOfACall() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; (y.Share, int k) = PlainAndZero(); Kept.Add(k); Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1910. A deconstruction that calls a Deconstruct method gives the plain cell in each turn.
    public static Cell<int> TurnPlainFromADeconstructMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; (y.Share, int k) = new PlainParts(); Kept.Add(k); Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1911. A yield break statement in the loop of an iterator comes before the plain cell.
    public static Cell<int> TurnYieldBreakBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? got = null; foreach (SettableSlot s in SlotsUntilABreak(l.Map(v => v))) { got = s; } return S.Hold(got!.Share!.Sample()); });

    // S1912. A return statement of a lambda in the body does not end the turn.
    public static Cell<int> TurnReturnOfALambdaBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; Func<int> one = () => { return 1; }; Kept.Add(one()); y.Share = Plain; Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1913. A return statement of a local function in the body does not end the turn.
    public static Cell<int> TurnReturnOfALocalFunctionBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; int One() { return 1; } Kept.Add(One()); y.Share = Plain; Kept.Add(x.Share!.Sample());  } return S.Hold(0); });

    // S1914. A yield return statement does not end the turn. Here, the code reads each object after the last turn.
    public static Cell<int> TurnYieldReturnBeforeTheWrite() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> all = []; foreach (SettableSlot s in SlotsThatGetAPlainCell(l.Map(v => v))) { all.Add(s); } return S.Hold(all[0].Share!.Sample()); });

    // S1915. A local of the block has the first object of the loop. Each turn gives that object a pending cell, and the object of the turn a plain cell.
    public static Cell<int> TurnOlderObjectThroughALocalOfTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { first = x; } first!.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1916. The same, where a local function gives the local of the block its value.
    public static Cell<int> TurnOlderObjectThroughALocalThatALocalFunctionAssigns() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; void Keep(SettableSlot given) { first = given; } for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { Keep(x); } first!.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1917. The same, with a member of an object of the block.
    public static Cell<int> TurnOlderObjectThroughAMemberOfAnObjectOfTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder outer = new(); for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { outer.Item = x; } outer.Item!.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(outer.Item!.Share!.Sample()); });

    // S1918. An element of the list of the block gets the pending cell, and a local of the block reads it.
    public static Cell<int> TurnOlderElementReadThroughALocalOfTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { first = x; } list.Add(x); list[0].Share = l.Map(v => v); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1919. An element of a tuple in the list of the block gets the pending cell, and a local of the block reads it.
    public static Cell<int> TurnOlderPartOfATupleReadThroughALocalOfTheBlock() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; List<(SettableSlot First, int Second)> pairs = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { first = x; } pairs.Add((x, i)); pairs[0].First.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1920. A local of the block has the first holder of the loop, and the member of that holder is the first object.
    public static Cell<int> TurnOlderObjectThroughAMemberOfAnOlderHolder() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder? kept = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SlotHolder holder = new() { Item = x }; if (i == 0) { kept = holder; } kept!.Item!.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(kept!.Item!.Share!.Sample()); });

    // S1921. A local of the block has the first list of the loop, and the element of that list is the first object.
    public static Cell<int> TurnOlderObjectThroughAnElementOfAnOlderList() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; List<SettableSlot>? kept = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; List<SettableSlot> one = [x]; if (i == 0) { kept = one; first = x; } kept![0].Share = l.Map(v => v); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1922. A foreach over the first list of the loop gives its element the pending cell.
    public static Cell<int> TurnOlderObjectThroughAForeachOverAnOlderList() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; List<SettableSlot>? kept = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; List<SettableSlot> one = [x]; if (i == 0) { kept = one; first = x; } foreach (SettableSlot each in kept!) { each.Share = l.Map(v => v); } x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1923. An object of the inner turn has the object of the outer turn, and a local of the outer body gets that object from it.
    public static Cell<int> TurnObjectOfTheOuterTurnInAnObjectOfTheInnerTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int j = 0; j < 1; j++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot? got = null; for (int i = 0; i < 2; i++) { SlotHolder holder = new() { Item = x }; got = holder.Item; } got!.Share = Plain; Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1924. A method of an object of the block keeps the first object of the loop in a member, and returns it in each turn.
    public static Cell<int> TurnOlderObjectThroughAMemberOfTheReceiverOfAMethod() =>
        Cell.Loop<int>().WithoutCaptures(l => { FirstSlotOwner owner = new(); for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot k = owner.FirstOf(x); k.Share = l.Map(v => v); x.Share = Plain; Kept.Add(k.Share!.Sample()); } return S.Hold(0); });

    // S1925. A lambda with a loop runs two times, and the walk reads the loop for each call. No cell is pending.
    public static Cell<int> TurnLoopInALambdaThatRunsTwoTimes() =>
        Cell.Loop<int>().WithoutCaptures(l => { Action run = () => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot y = x; y.Share = Plain; Kept.Add(x.Share!.Sample()); } }; run(); run(); return S.Hold(0); });

    // S1926. A cell of the block has the first object of the loop as its value.
    public static Cell<int> TurnOlderObjectThroughTheValueOfACell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<SettableSlot>? kept = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { kept = Cell.Constant(x); } kept!.Sample().Share = l.Map(v => v); x.Share = Plain; } return S.Hold(kept!.Sample().Share!.Sample()); });

    // S1927. The loop of a constructor keeps its first object in a member of the new object.
    public static Cell<int> TurnObjectInAMemberThatTheLoopOfAConstructorSets() =>
        Cell.Loop<int>().WithoutCaptures(l => { return S.Hold(new FirstSlotOfALoop(l.Map(v => v)).First!.Share!.Sample()); });

    // S1928. A member that the constructor of a base class sets has the object of the turn.
    public static Cell<int> TurnObjectInAMemberThatABaseConstructorSets() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; DerivedSlotOwner owner = new(x); SettableSlot y = owner.Item; y.Share = Plain; Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1929. A lambda that a query runs for each element makes two objects in one turn, and they have no one state.
    public static Cell<int> TurnObjectsOfALambdaThatAQueryRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { List<SettableSlot> rows = new[] { 1, 2 }.Select(n => new SettableSlot { Share = l.Map(v => v) }).ToList(); SettableSlot a = rows[0]; SettableSlot b = rows[1]; a.Share = Plain; Kept.Add(b.Share!.Sample()); } return S.Hold(0); });

    // S1930. A local function that a query runs for each element makes two objects in one turn.
    public static Cell<int> TurnObjectsOfALocalFunctionThatAQueryRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot Make(int n) => new() { Share = l.Map(v => v) }; List<SettableSlot> rows = new[] { 1, 2 }.Select(Make).ToList(); SettableSlot a = rows[0]; SettableSlot b = rows[1]; a.Share = Plain; Kept.Add(b.Share!.Sample()); } return S.Hold(0); });

    // S1931. A query expression makes two objects in one turn.
    public static Cell<int> TurnObjectsOfAQueryExpression() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { List<SettableSlot> rows = (from n in new[] { 1, 2 } select new SettableSlot { Share = l.Map(v => v) }).ToList(); SettableSlot a = rows[0]; SettableSlot b = rows[1]; a.Share = Plain; Kept.Add(b.Share!.Sample()); } return S.Hold(0); });

    // S1932. A local function of the body gives the plain cell, and only some turns call it.
    public static Cell<int> TurnPlainInALocalFunctionThatSomeTurnsCall() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; void MakePlain() { x.Share = Plain; } if (i > 5) { MakePlain(); } Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1933. A member of an object of the outer turn has the first object of the inner loop. Each inner turn gives that object a pending cell.
    public static Cell<int> TurnOlderObjectThroughAMemberOfAnObjectOfTheOuterTurn() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int j = 0; j < 1; j++) { SlotHolder outer = new(); for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { outer.Item = x; } outer.Item!.Share = l.Map(v => v); x.Share = Plain; } Kept.Add(outer.Item!.Share!.Sample()); } return S.Hold(0); });

    // S1934. A local of the block keeps the lambda of the first turn, which returns the object of that turn.
    public static Cell<int> TurnLambdaOfTheFirstTurnGivesItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<SettableSlot>? getFirst = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { getFirst = () => x; } SettableSlot k = getFirst!(); k.Share = l.Map(v => v); x.Share = Plain; Kept.Add(k.Share!.Sample()); } return S.Hold(0); });

    // S1935. A local of the block keeps the lambda of the first turn, which gives the object of that turn a pending cell.
    public static Cell<int> TurnLambdaOfTheFirstTurnChangesItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; Action? change = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { first = x; change = () => { x.Share = l.Map(v => v); }; } change!(); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1936. A local function of the body returns the object of the turn, and the result is a name of that object.
    public static Cell<int> TurnLocalFunctionOfTheBodyGivesItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot Same() => x; SettableSlot k = Same(); k.Share = l.Map(v => v); Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1937. A lambda of the block returns its argument, which is the object of the turn and not an older one.
    public static Cell<int> TurnLambdaOfTheBlockGivesItsArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<SettableSlot, SettableSlot> same = given => given; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = same(x); y.Share = Plain; Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1938. A holder that the walk did not see made keeps the first object of the loop in its member.
    public static Cell<int> TurnOlderObjectThroughAMemberOfAnObjectThatTheWalkDidNotSeeMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder outer = SharedHolders[0]; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { outer.Item = x; } outer.Item!.Share = l.Map(v => v); x.Share = Plain; } return S.Hold(outer.Item!.Share!.Sample()); });

    // S1939. A list that the walk did not see made keeps each tuple that it got, and its first tuple has an older object.
    public static Cell<int> TurnOlderPartOfATupleInAListThatTheWalkDidNotSeeMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; List<(SettableSlot First, int Second)> pairs = SharedPairLists[0]; pairs.Add((x, i)); pairs[0].First.Share = l.Map(v => v); x.Share = Plain; Kept.Add(pairs[0].First.Share!.Sample()); } return S.Hold(0); });

    // S1940. A field that a new object got counts for each object of its type. A plain cell through an object that the walk does not know is then not sure to be for the object of the turn.
    public static Cell<int> TurnPlainThroughTheFieldOfAnObjectThatTheWalkDoesNotKnow() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SlotPairs made = new() { Pairs = [] }; made.Pairs.Add((x, i)); SharedSlotPairs.Pairs[0].First.Share = Plain; Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1941. A local of the block keeps the local function of the first turn, which returns the object of that turn.
    public static Cell<int> TurnLocalFunctionOfTheFirstTurnGivesItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<SettableSlot>? getFirst = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; SettableSlot Get() => x; if (i == 0) { getFirst = Get; } SettableSlot k = getFirst!(); k.Share = l.Map(v => v); x.Share = Plain; Kept.Add(k.Share!.Sample()); } return S.Hold(0); });

    // S1942. A local of the block keeps the lambda that a method made in the first turn, which returns the argument of that call.
    public static Cell<int> TurnLambdaOfACallOfTheFirstTurnGivesItsArgument() =>
        Cell.Loop<int>().WithoutCaptures(l => { Func<SettableSlot>? getFirst = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { getFirst = KeeperOf(x); } SettableSlot k = getFirst!(); k.Share = l.Map(v => v); x.Share = Plain; Kept.Add(k.Share!.Sample()); } return S.Hold(0); });

    // S1943. The lambda of the first turn gives the object of that turn a pending cell in a lambda that a list runs.
    public static Cell<int> TurnLambdaOfTheFirstTurnChangesItsObjectInALambdaOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? first = null; Action? change = null; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; if (i == 0) { first = x; change = () => { new List<int> { 1 }.ForEach(n => { x.Share = l.Map(v => v); }); }; } change!(); x.Share = Plain; } return S.Hold(first!.Share!.Sample()); });

    // S1944. An assignment in the body gives a local of the block a new object. Here, no turn runs, and the local keeps its first object.
    public static Cell<int> TurnAssignmentToALocalOfTheBlockWhenNoTurnRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot keep = new() { Share = l.Map(v => v) }; int n = 0; for (int i = 0; i < n; i++) { keep = new SettableSlot { Share = Plain }; } return S.Hold(keep.Share!.Sample()); });

    // S1945. A deconstruction of the tuple from a call, where the tuple has an object also, gives the plain cell in each turn.
    public static Cell<int> TurnPlainFromATupleOfACallThatHasAnObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot y = x; (y.Share, SettableSlot z) = PlainAnd(x); Kept.Add(z); Kept.Add(x.Share!.Sample()); } return S.Hold(0); });

    // S1946. A lambda that a list runs for each element makes two objects in one turn. A plain cell for the first one is not for the second one.
    public static Cell<int> TurnObjectsOfALambdaThatAListRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot? first = null; SettableSlot? second = null; new List<int> { 1, 2 }.ForEach(n => { SettableSlot made = new() { Share = l.Map(v => v) }; if (n == 1) { first = made; } else { second = made; } }); first!.Share = Plain; Kept.Add(second!.Share!.Sample()); } return S.Hold(0); });

    // S1947. A local function that a list runs for each element makes two objects in one turn.
    public static Cell<int> TurnObjectsOfALocalFunctionThatAListRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot? first = null; SettableSlot? second = null; void Make(int n) { SettableSlot made = new() { Share = l.Map(v => v) }; if (n == 1) { first = made; } else { second = made; } } new List<int> { 1, 2 }.ForEach(Make); first!.Share = Plain; Kept.Add(second!.Share!.Sample()); } return S.Hold(0); });

    // S1948. An element of a collection expression reads the state that its object has now.
    public static Cell<int> ElementReadsTheStateOfItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; List<SettableSlot> one = [x]; x.Share = Plain; return S.Hold(one[0].Share!.Sample()); });

    // S1949. The same, for an object that Add put into the list.
    public static Cell<int> ElementThatAddPutReadsTheStateOfItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; List<SettableSlot> list = []; list.Add(x); x.Share = Plain; return S.Hold(list[0].Share!.Sample()); });

    // S1950. The same, for an element of an array.
    public static Cell<int> ElementOfAnArrayReadsTheStateOfItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; SettableSlot[] items = [x]; x.Share = Plain; return S.Hold(items[0].Share!.Sample()); });

    // S1951. The same, for a value of a dictionary.
    public static Cell<int> ValueOfADictionaryReadsTheStateOfItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; Dictionary<string, SettableSlot> map = new() { ["a"] = x }; x.Share = Plain; return S.Hold(map["a"].Share!.Sample()); });

    // S1952. A plain cell through the element replaces the pending one for a read through the element.
    public static Cell<int> ElementReadsWhatAWriteThroughItGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; List<SettableSlot> list = []; list.Add(x); list[0].Share = Plain; return S.Hold(list[0].Share!.Sample()); });

    // S1953. In a loop, an element of a list of the turn reads the state that the object of the turn has now.
    public static Cell<int> TurnElementOfAListOfTheTurnReadsTheStateOfItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<SettableSlot> list = []; for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = l.Map(v => v) }; List<SettableSlot> one = [x]; x.Share = Plain; list.Add(one[0]); } return S.Hold(list[0].Share!.Sample()); });

    // S1954. A key of a dictionary is not one of its values, and the pending cell of the key is not in a value.
    public static Cell<int> ValueOfADictionaryWhoseKeyHasAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot key = new() { Share = l.Map(v => v) }; SettableSlot value = new() { Share = Plain }; Dictionary<SettableSlot, SettableSlot> map = []; map[key] = value; return S.Hold(map[key].Share!.Sample()); });

    // S1955. A search does not put its argument into the list.
    public static Cell<int> ElementOfAListThatSearchedForAnObjectWithAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; List<SettableSlot> list = [new SettableSlot { Share = Plain }]; Kept.Add(list.Contains(x)); return S.Hold(list[0].Share!.Sample()); });

    // S1956. A list got an object that the walk did not see made, with a pending cell, and then an object of the store.
    public static Cell<int> ElementBesideAnObjectThatTheWalkDidNotSeeMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot old = SharedSlots[0]; old.Share = l.Map(v => v); List<SettableSlot> list = []; list.Add(old); list.Add(new SettableSlot { Share = Plain }); return S.Hold(list[0].Share!.Sample()); });

    // S1957. The same, where a collection expression has the object that the walk did not see made.
    public static Cell<int> ElementOfALiteralWithAnObjectThatTheWalkDidNotSeeMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot old = SharedSlots[0]; old.Share = l.Map(v => v); List<SettableSlot> list = [old]; list.Add(new SettableSlot { Share = Plain }); return S.Hold(list[0].Share!.Sample()); });

    // S1958. An object that the walk did not see made gets its pending cell after the list got it.
    public static Cell<int> ElementThatGetsAPendingCellAfterTheListGotIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot old = SharedSlots[0]; List<SettableSlot> list = [new SettableSlot { Share = Plain }]; list.Insert(0, old); old.Share = l.Map(v => v); return S.Hold(list[0].Share!.Sample()); });

    // S1959. A list got the elements of a second list, and one of them has a pending cell.
    public static Cell<int> ElementThatAddRangePut() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; List<SettableSlot> first = [x]; List<SettableSlot> list = [new SettableSlot { Share = Plain }]; list.AddRange(first); return S.Hold(list[1].Share!.Sample()); });

    // S1960. The same, where the element gets its pending cell after the call of AddRange.
    public static Cell<int> ElementThatAddRangePutAndThatGetsAPendingCellAfterIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = Plain }; List<SettableSlot> first = [x]; List<SettableSlot> list = [new SettableSlot { Share = Plain }]; list.AddRange(first); x.Share = l.Map(v => v); return S.Hold(list[1].Share!.Sample()); });

    // S1961. A copy of a list holds the same objects, and one of them gets a pending cell after the copy.
    public static Cell<int> ElementOfACopyOfAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = Plain }; List<SettableSlot> first = [x]; List<SettableSlot> copy = new(first); copy.Add(new SettableSlot { Share = Plain }); x.Share = l.Map(v => v); return S.Hold(copy[0].Share!.Sample()); });

    // S1962. A collection expression got an object with no state yet. The object gets a pending cell and then a plain one.
    public static Cell<int> ElementThatHadNoStateWhenItsListGotIt() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new(); List<SettableSlot> one = [x]; x.Share = l.Map(v => v); x.Share = Plain; return S.Hold(one[0].Share!.Sample()); });

    // S1963. A collection expression got an object with no state yet, and the list got a second object. The first object then gets a pending cell.
    public static Cell<int> ElementWithNoStateBesideASecondObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new(); List<SettableSlot> list = [x]; list.Add(new SettableSlot { Share = Plain }); x.Share = l.Map(v => v); return S.Hold(list[0].Share!.Sample()); });

    // S1964. A list got the elements of a second list, one of them with no state yet. That element then gets a pending cell.
    public static Cell<int> ElementWithNoStateThatAddRangePut() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new(); List<SettableSlot> first = [x]; List<SettableSlot> list = [new SettableSlot { Share = Plain }]; list.AddRange(first); x.Share = l.Map(v => v); return S.Hold(list[1].Share!.Sample()); });

    // S1965. A builder of an ImmutableHashSet gives the call of Equals to the object that the code gives, which has a pending cell. Add.
    public static Cell<int> BuilderOfAnImmutableHashSetAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SameOwn>.Builder made = ImmutableHashSet.CreateBuilder<SameOwn>(); made.Add(new(Plain)); made.Add(new(l.Map(v => v))); return S.Hold(0); });

    // S1966. A builder of an ImmutableHashSet gives the call of Equals to the object that the code gives, which has a pending cell. Contains.
    public static Cell<int> BuilderOfAnImmutableHashSetContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SameOwn>.Builder made = ImmutableHashSet.CreateBuilder<SameOwn>(); made.Add(new(Plain)); Kept.Add(made.Contains(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1967. A builder of an ImmutableHashSet gives the call of Equals to the object that the code gives, which has a pending cell. TryGetValue.
    public static Cell<int> BuilderOfAnImmutableHashSetTryGetValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SameOwn>.Builder made = ImmutableHashSet.CreateBuilder<SameOwn>(); made.Add(new(Plain)); Kept.Add(made.TryGetValue(new SameOwn(l.Map(v => v)), out _)); return S.Hold(0); });

    // S1968. A builder of an ImmutableHashSet gives the call of Equals to the object that the code gives, which has a pending cell. UnionWith, which has no row for its receiver.
    public static Cell<int> BuilderOfAnImmutableHashSetUnionWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SameOwn>.Builder made = ImmutableHashSet.CreateBuilder<SameOwn>(); made.Add(new(Plain)); made.UnionWith([new SameOwn(l.Map(v => v))]); return S.Hold(0); });

    // S1969. A builder of an ImmutableSortedSet gives the call of CompareTo to the object that the code gives, which has a pending cell. Add.
    public static Cell<int> BuilderOfAnImmutableSortedSetAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedSet<RankedOwn>.Builder made = ImmutableSortedSet.CreateBuilder<RankedOwn>(); made.Add(new(Plain)); made.Add(new(l.Map(v => v))); return S.Hold(0); });

    // S1970. A builder of an ImmutableSortedSet gives the call of CompareTo to the object that the code gives, which has a pending cell. Contains.
    public static Cell<int> BuilderOfAnImmutableSortedSetContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedSet<RankedOwn>.Builder made = ImmutableSortedSet.CreateBuilder<RankedOwn>(); made.Add(new(Plain)); Kept.Add(made.Contains(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1971. A builder of an ImmutableSortedSet gives the call of CompareTo to the object that the code gives, which has a pending cell. Remove.
    public static Cell<int> BuilderOfAnImmutableSortedSetRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedSet<RankedOwn>.Builder made = ImmutableSortedSet.CreateBuilder<RankedOwn>(); made.Add(new(Plain)); Kept.Add(made.Remove(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1972. A builder of an ImmutableSortedSet gives the call of CompareTo to the object that the code gives, which has a pending cell. IntersectWith, which has no row for its receiver.
    public static Cell<int> BuilderOfAnImmutableSortedSetIntersectWith() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedSet<RankedOwn>.Builder made = ImmutableSortedSet.CreateBuilder<RankedOwn>(); made.Add(new(Plain)); made.IntersectWith([new RankedOwn(l.Map(v => v))]); return S.Hold(0); });

    // S1973. A builder of an ImmutableDictionary gives the call of Equals to the object that the code gives, which has a pending cell. Add of a key.
    public static Cell<int> BuilderOfAnImmutableDictionaryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<SameOwn, int>.Builder made = ImmutableDictionary.CreateBuilder<SameOwn, int>(); made.Add(new(Plain), 1); made.Add(new(l.Map(v => v)), 2); return S.Hold(0); });

    // S1974. A builder of an ImmutableSortedDictionary gives the call of CompareTo to the object that the code gives, which has a pending cell. Add of a key.
    public static Cell<int> BuilderOfAnImmutableSortedDictionaryAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<RankedOwn, int>.Builder made = ImmutableSortedDictionary.CreateBuilder<RankedOwn, int>(); made.Add(new(Plain), 1); made.Add(new(l.Map(v => v)), 2); return S.Hold(0); });

    // S1975. A builder of an ImmutableSortedDictionary gives the call of CompareTo to the object that the code gives, which has a pending cell. ContainsKey.
    public static Cell<int> BuilderOfAnImmutableSortedDictionaryContainsKey() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<RankedOwn, int>.Builder made = ImmutableSortedDictionary.CreateBuilder<RankedOwn, int>(); made.Add(new(Plain), 1); Kept.Add(made.ContainsKey(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1976. A builder of an ImmutableSortedDictionary gives the call of CompareTo to the object that the code gives, which has a pending cell. The indexer.
    public static Cell<int> BuilderOfAnImmutableSortedDictionaryIndexer() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<RankedOwn, int>.Builder made = ImmutableSortedDictionary.CreateBuilder<RankedOwn, int>(); made.Add(new(Plain), 1); made[new RankedOwn(l.Map(v => v))] = 2; return S.Hold(0); });

    // S1977. A builder of an ImmutableList gives the call of Equals to the object that the code gives, which has a pending cell. Contains.
    public static Cell<int> BuilderOfAnImmutableListContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<SameOwn>.Builder made = ImmutableList.CreateBuilder<SameOwn>(); made.Add(new(Plain)); Kept.Add(made.Contains(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1978. A builder of an ImmutableList gives the call of Equals to the object that the code gives, which has a pending cell. IndexOf.
    public static Cell<int> BuilderOfAnImmutableListIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<SameOwn>.Builder made = ImmutableList.CreateBuilder<SameOwn>(); made.Add(new(Plain)); Kept.Add(made.IndexOf(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1979. A builder of an ImmutableList gives the call of Equals to the object that the code gives, which has a pending cell. Remove.
    public static Cell<int> BuilderOfAnImmutableListRemove() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<SameOwn>.Builder made = ImmutableList.CreateBuilder<SameOwn>(); made.Add(new(Plain)); Kept.Add(made.Remove(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1980. A builder of an ImmutableArray gives the call of Equals to the object that it has, which has a pending cell. Contains.
    public static Cell<int> BuilderOfAnImmutableArrayContains() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<SameOwn>.Builder made = ImmutableArray.CreateBuilder<SameOwn>(); made.Add(new(l.Map(v => v))); Kept.Add(made.Contains(new SameOwn(Plain))); return S.Hold(0); });

    // S1981. The same for IndexOf.
    public static Cell<int> BuilderOfAnImmutableArrayIndexOf() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<SameOwn>.Builder made = ImmutableArray.CreateBuilder<SameOwn>(); made.Add(new(l.Map(v => v))); Kept.Add(made.IndexOf(new SameOwn(Plain))); return S.Hold(0); });

    // S1982. A builder of an ImmutableHashSet gives the call of Equals in Remove to the object that it has, when that object is the first one with its hash code. Remove has no row for its receiver.
    public static Cell<int> BuilderOfAnImmutableHashSetRemoveOfTheObjectThatItHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SameOwn>.Builder made = ImmutableHashSet.Create(new SameOwn(l.Map(v => v))).ToBuilder(); Kept.Add(made.Remove(new SameOwn(Plain))); return S.Hold(0); });

    // S1983. A builder of an ImmutableDictionary gives the call of Equals in ContainsKey to the key that it has, when that key is the first one with its hash code. ContainsKey has no row for its receiver.
    public static Cell<int> BuilderOfAnImmutableDictionaryContainsKeyOfTheKeyThatItHas() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<SameOwn, int>.Builder made = ImmutableDictionary.CreateRange(new[] { new KeyValuePair<SameOwn, int>(new(l.Map(v => v)), 1) }).ToBuilder(); Kept.Add(made.ContainsKey(new SameOwn(Plain))); return S.Hold(0); });

    // S1984. A builder of an ImmutableList has an object with a pending cell, and the object that the code gives gets the call.
    public static Cell<int> BuilderOfAnImmutableListThatHasAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<SameOwn>.Builder made = ImmutableList.CreateBuilder<SameOwn>(); made.Add(new(l.Map(v => v))); Kept.Add(made.Contains(new SameOwn(Plain))); return S.Hold(0); });

    // S1985. A builder of an ImmutableArray gets an object with a pending cell, and the object that it has gets the call.
    public static Cell<int> BuilderOfAnImmutableArrayThatGetsAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<SameOwn>.Builder made = ImmutableArray.CreateBuilder<SameOwn>(); made.Add(new(Plain)); Kept.Add(made.Contains(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S1986. A builder of an ImmutableHashSet has an object with a pending cell, and the object that the code gives gets the call.
    public static Cell<int> BuilderOfAnImmutableHashSetThatHasAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableHashSet<SameOwn>.Builder made = ImmutableHashSet.Create(new SameOwn(l.Map(v => v))).ToBuilder(); Kept.Add(made.Contains(new SameOwn(Plain))); return S.Hold(0); });

    // S1987. A builder of an ImmutableSortedSet has an object with a pending cell, and the object that the code gives gets the call.
    public static Cell<int> BuilderOfAnImmutableSortedSetThatHasAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedSet<RankedOwn>.Builder made = ImmutableSortedSet.Create(new RankedOwn(l.Map(v => v))).ToBuilder(); Kept.Add(made.Contains(new RankedOwn(Plain))); return S.Hold(0); });

    // S1988. Add of a builder of an ImmutableList compares nothing.
    public static Cell<int> BuilderOfAnImmutableListAdd() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<SameOwn>.Builder made = ImmutableList.CreateBuilder<SameOwn>(); made.Add(new(l.Map(v => v))); made.Add(new(l.Map(v => v))); return S.Hold(0); });

    // S1989. The second turn of a for reads the cell that the first turn gave to a local of the block.
    public static Cell<int> TurnBeforeGaveAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2; i++) { if (last != null) { Kept.Add(last.Sample()); } last = l.Map(v => v); } return S.Hold(0); });

    // S1990. The same for an object with a pending cell.
    public static Cell<int> TurnBeforeGaveAnObjectWithAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot? last = null; for (int i = 0; i < 2; i++) { if (last != null) { Kept.Add(last.Share!.Sample()); } last = new SettableSlot { Share = l.Map(v => v) }; } return S.Hold(0); });

    // S1991. The same in a while.
    public static Cell<int> TurnBeforeOfAWhileGaveAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; int i = 0; while (i < 2) { if (last != null) { Kept.Add(last.Sample()); } last = l.Map(v => v); i++; } return S.Hold(0); });

    // S1992. The same in a do.
    public static Cell<int> TurnBeforeOfADoGaveAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; int i = 0; do { if (last != null) { Kept.Add(last.Sample()); } last = l.Map(v => v); i++; } while (i < 2); return S.Hold(0); });

    // S1993. The same in a foreach.
    public static Cell<int> TurnBeforeOfAForeachGaveAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; foreach (int i in new[] { 0, 1 }) { if (last != null) { Kept.Add(last.Sample()); } last = l.Map(v => v); } return S.Hold(0); });

    // S1994. The same in a foreach that deconstructs its element.
    public static Cell<int> TurnBeforeOfAForeachOfPartsGaveAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; foreach ((int i, int j) in new[] { (0, 0), (1, 1) }) { if (last != null) { Kept.Add(last.Sample()); } last = l.Map(v => v); } return S.Hold(0); });

    // S1995. The condition of a while reads the cell that the turn before gave.
    public static Cell<int> ConditionReadsWhatTheTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; int i = 0; while (i < 2 && (last == null || last.Sample() >= 0)) { last = l.Map(v => v); i++; } return S.Hold(0); });

    // S1996. The condition of a for reads the cell that the turn before gave.
    public static Cell<int> ConditionOfAForReadsWhatTheTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2 && (last == null || last.Sample() >= 0); i++) { last = l.Map(v => v); } return S.Hold(0); });

    // S1997. A member of an object of the block has the object that the turn before gave.
    public static Cell<int> TurnBeforeGaveAMemberItsObject() =>
        Cell.Loop<int>().WithoutCaptures(l => { SlotHolder outer = new(); for (int i = 0; i < 2; i++) { if (outer.Item != null) { Kept.Add(outer.Item.Share!.Sample()); } outer.Item = new SettableSlot { Share = l.Map(v => v) }; } return S.Hold(0); });

    // S1998. A list of the block has the cell that the turn before put into it.
    public static Cell<int> TurnBeforePutAPendingCellIntoAList() =>
        Cell.Loop<int>().WithoutCaptures(l => { List<Cell<int>> cells = []; for (int i = 0; i < 2; i++) { if (cells.Count > 0) { Kept.Add(cells[0].Sample()); } cells.Add(l.Map(v => v)); } return S.Hold(0); });

    // S1999. A lambda that each turn runs reads the cell that the turn before gave.
    public static Cell<int> LambdaReadsWhatTheTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2; i++) { Transaction.RunVoid(() => { if (last != null) { Kept.Add(last.Sample()); } }); last = l.Map(v => v); } return S.Hold(0); });

    // S2000. A method that each turn calls reads the cell that the turn before gave.
    public static Cell<int> CalleeReadsWhatTheTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2; i++) { SampleIfAny(last); last = l.Map(v => v); } return S.Hold(0); });

    // S2001. An inner loop reads the cell that the outer turn before gave.
    public static Cell<int> InnerLoopReadsWhatTheOuterTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int j = 0; j < 2; j++) { for (int i = 0; i < 1; i++) { if (last != null) { Kept.Add(last.Sample()); } } last = l.Map(v => v); } return S.Hold(0); });

    // S2002. An inner loop gives the cell, and its second turn in the second outer turn reads it.
    public static Cell<int> InnerLoopReadsWhatItsTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int j = 0; j < 1; j++) { Cell<int>? last = null; for (int i = 0; i < 2; i++) { if (last != null) { Kept.Add(last.Sample()); } last = l.Map(v => v); } } return S.Hold(0); });

    // S2003. Each turn gives the local a plain cell before it reads the local.
    public static Cell<int> TurnGivesAPlainCellBeforeItsRead() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2; i++) { last = Plain; Kept.Add(last.Sample()); last = l.Map(v => v); } return S.Hold(0); });

    // S2004. The turn before gave a plain cell.
    public static Cell<int> TurnBeforeGaveAPlainCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2; i++) { if (last != null) { Kept.Add(last.Sample()); } last = Plain; } return S.Hold(0); });

    // S2005. A local of the body is new in each turn, and has no value from the turn before.
    public static Cell<int> LocalOfTheBodyHasNoValueFromTheTurnBefore() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { Cell<int>? now = null; if (now != null) { Kept.Add(now.Sample()); } now = l.Map(v => v); Kept.Add(now); } return S.Hold(0); });

    // S2006. An object of the body is new in each turn, and the read comes before its pending cell.
    public static Cell<int> ObjectOfTheBodyHasNoStateFromTheTurnBefore() =>
        Cell.Loop<int>().WithoutCaptures(l => { for (int i = 0; i < 2; i++) { SettableSlot x = new() { Share = Plain }; Kept.Add(x.Share!.Sample()); x.Share = l.Map(v => v); Kept.Add(x); } return S.Hold(0); });

    // S2007. A list got the elements of a list that the walk did not see made, which has an object with a pending cell.
    public static Cell<int> ElementFromAListThatTheWalkDidNotSeeMade() =>
        Cell.Loop<int>().WithoutCaptures(l => { SettableSlot x = new() { Share = l.Map(v => v) }; SharedSlotList.Clear(); SharedSlotList.Add(x); List<SettableSlot> list = [new SettableSlot { Share = Plain }]; list.AddRange(SharedSlotList); return S.Hold(list[1].Share!.Sample()); });

    // S2008. A builder of an ImmutableDictionary gives the call of Equals in ContainsValue to the value that the code gives.
    public static Cell<int> BuilderOfAnImmutableDictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<int, SameOwn>.Builder made = ImmutableDictionary.CreateBuilder<int, SameOwn>(); made.Add(1, new(Plain)); Kept.Add(made.ContainsValue(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S2009. The same for a builder of an ImmutableSortedDictionary.
    public static Cell<int> BuilderOfAnImmutableSortedDictionaryContainsValue() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableSortedDictionary<int, SameOwn>.Builder made = ImmutableSortedDictionary.CreateBuilder<int, SameOwn>(); made.Add(1, new(Plain)); Kept.Add(made.ContainsValue(new SameOwn(l.Map(v => v)))); return S.Hold(0); });

    // S2010. A builder of an ImmutableList gives the call of CompareTo in BinarySearch to the object that the code gives.
    public static Cell<int> BuilderOfAnImmutableListBinarySearch() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<RankedOwn>.Builder made = ImmutableList.CreateBuilder<RankedOwn>(); made.Add(new(Plain)); Kept.Add(made.BinarySearch(new RankedOwn(l.Map(v => v)))); return S.Hold(0); });

    // S2011. A builder of an ImmutableList sorts two objects, and each has a pending cell.
    public static Cell<int> BuilderOfAnImmutableListSort() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableList<RankedOwn>.Builder made = ImmutableList.CreateBuilder<RankedOwn>(); made.Add(new(l.Map(v => v))); made.Add(new(l.Map(v => v))); made.Sort(); return S.Hold(0); });

    // S2012. A builder of an ImmutableArray sorts two objects, and each has a pending cell.
    public static Cell<int> BuilderOfAnImmutableArraySort() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableArray<RankedOwn>.Builder made = ImmutableArray.CreateBuilder<RankedOwn>(); made.Add(new(l.Map(v => v))); made.Add(new(l.Map(v => v))); made.Sort(); return S.Hold(0); });

    // S2013. A builder of an ImmutableDictionary gives the call of Equals in Add to the key that the code gives, and the key that it has has the pending cell.
    public static Cell<int> BuilderOfAnImmutableDictionaryThatHasAPendingCell() =>
        Cell.Loop<int>().WithoutCaptures(l => { ImmutableDictionary<SameOwn, int>.Builder made = ImmutableDictionary.CreateRange(new[] { new KeyValuePair<SameOwn, int>(new(l.Map(v => v)), 1) }).ToBuilder(); made.Add(new SameOwn(Plain), 2); return S.Hold(0); });

    // S2014. The condition of a do reads a cell that the body got from the turn before.
    public static Cell<int> ConditionOfADoReadsWhatTheTurnBeforeGave() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; Cell<int>? now = null; int i = 0; do { now = last; last = l.Map(v => v); i++; } while (i < 3 && (now == null || now.Sample() >= 0)); return S.Hold(0); });

    // S2015. An assignment in the body gives a local of the block a plain cell. Here, no turn runs, and the local keeps its pending cell.
    public static Cell<int> TurnAssignmentOfACellWhenNoTurnRuns() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int> keep = l.Map(v => v); int n = 0; for (int i = 0; i < n; i++) { keep = Plain; } return S.Hold(keep.Sample()); });

    // S2016. Each turn gives a local of the block a pending cell and then a plain one. After the loop, the local has the plain cell or the value from before the loop.
    public static Cell<int> TurnGivesAPendingCellAndThenAPlainOne() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int>? last = null; for (int i = 0; i < 2; i++) { last = l.Map(v => v); last = Plain; } return S.Hold(last!.Sample()); });

    // S2017. The last turn ends at a break statement after the assignment, and the local keeps what that turn gave.
    public static Cell<int> TurnEndsAfterItsAssignment() =>
        Cell.Loop<int>().WithoutCaptures(l => { Cell<int> last = Plain; for (int i = 0; i < 2; i++) { last = l.Map(v => v); if (i == 0) { break; } last = Plain; } return S.Hold(last.Sample()); });

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

    public sealed class SlotList : List<SettableSlot>;

    public sealed class SlotNode
    {
        public Cell<int>? Share { get; set; }

        public SlotNode? Next { get; set; }
    }

    public sealed class RankedPlainOwn(Cell<int> share) : IComparable
    {
        public int CompareTo(object? obj) => share.Sample().CompareTo(0);
    }

    public sealed class CellBox
    {
        public List<Cell<int>> Cells { get; } = new();

        public int First() => this.Cells[0].Sample();
    }

    public sealed class SlotBox
    {
        public List<SettableSlot> Slots { get; } = new();
    }

    public sealed class CellCrate
    {
        public readonly List<Cell<int>> Cells = new();
    }

    public sealed class CellTray
    {
        public List<Cell<int>>? Cells { get; set; }
    }

    public sealed class CellOuter
    {
        public CellBox Box { get; } = new();
    }

    public sealed class CellMap
    {
        public Dictionary<string, Cell<int>> Cells { get; } = new();
    }

    // Gives a comparer that reads nothing.
    private static void GiveAComparer(ref IComparer<Ranked>? comparer) =>
        comparer = Comparer<Ranked>.Create(static (_, _) => 0);

    private static ISet<Alike> MakeASet() => new HashSet<Alike>();

    private static IDictionary<Alike, int> MakeAMap() => new Dictionary<Alike, int>();

    private static IReadOnlySet<Alike> MakeAReadOnlySet(Alike first) => new HashSet<Alike> { first };

    private static ICollection<Alike> MakeAList() => new List<Alike>();

    // CompareTo of IComparable with no type argument, which reads both objects.
    public sealed class RankedLoose(Cell<int> share) : IComparable
    {
        public int CompareTo(object? obj) => this.Read().CompareTo((obj as RankedLoose)?.Read() ?? 0);

        private int Read() => share.Sample();
    }

    private static IImmutableSet<Alike> MakeAnImmutableSet(Alike first) => ImmutableHashSet.Create(first);

    private static IImmutableDictionary<Alike, int> MakeAnImmutableMap(Alike first) =>
        ImmutableDictionary<Alike, int>.Empty.Add(key: first, value: 1);

    private static IReadOnlyDictionary<Alike, int> MakeAReadOnlyMap(Alike first) =>
        new Dictionary<Alike, int> { { first, 1 } };

    private static ICollection<Alike> MakeASetAsACollection() => new HashSet<Alike>();

    private static ICollection<Alike> MakeASetInALocal()
    {
        HashSet<Alike> set = [];

        return set;
    }

    private static ICollection<Alike> MakeASetInTwoPlaces(bool early)
    {
        if (early)
        {
            return new HashSet<Alike>();
        }

        return new HashSet<Alike>();
    }

    private static ICollection<Alike> TheSame(ICollection<Alike> given) => given;

    private static ICollection<Alike> MakeASetOrAList(bool set) => set ? new HashSet<Alike>() : new List<Alike>();

    // The helper gives its parameter a new value. The call then has the type of that value.
    private static ICollection<Alike> AListInstead(ICollection<Alike> given)
    {
        given = new List<Alike>();

        return given;
    }

    private static ICollection<AlikeOwn> MakeAnOwnList(AlikeOwn first) => new List<AlikeOwn> { first };

    public class CollectionMaker
    {
        public virtual ICollection<Alike> Make() => new HashSet<Alike>();
    }

    public sealed class ListMaker : CollectionMaker
    {
        public override ICollection<Alike> Make() => new List<Alike>();
    }

    // Each helper below returns an object of one of two types, thus the analyzer does not know the type.
    private static ISet<Alike> MakeAHashSetOrASortedSet(bool sorted) =>
        sorted ? new SortedSet<Alike>() : new HashSet<Alike>();

    private static IDictionary<Alike, int> MakeADictionaryOrASortedOne(bool sorted) =>
        sorted ? new SortedDictionary<Alike, int>() : new Dictionary<Alike, int>();

    private static IReadOnlySet<Alike> MakeAReadOnlySetOfOneOfTwoTypes(Alike first, bool sorted) =>
        sorted ? new SortedSet<Alike> { first } : new HashSet<Alike> { first };

    private static IImmutableSet<Alike> MakeAnImmutableSetOfOneOfTwoTypes(Alike first, bool sorted) =>
        sorted ? ImmutableSortedSet.Create(first) : ImmutableHashSet.Create(first);

    private static IImmutableDictionary<Alike, int> MakeAnImmutableMapOfOneOfTwoTypes(Alike first, bool sorted) =>
        sorted
            ? ImmutableSortedDictionary<Alike, int>.Empty.Add(key: first, value: 1)
            : ImmutableDictionary<Alike, int>.Empty.Add(key: first, value: 1);

    private static IReadOnlyDictionary<Alike, int> MakeAReadOnlyMapOfOneOfTwoTypes(Alike first, bool sorted) =>
        sorted ? new SortedDictionary<Alike, int> { { first, 1 } } : new Dictionary<Alike, int> { { first, 1 } };

    private static ICollection<Alike> MakeAListOrASetInTwoPlaces(bool list)
    {
        if (list)
        {
            return new List<Alike>();
        }

        return new HashSet<Alike>();
    }

    private static ISet<Alike> MakeASetByRecursion(int depth)
    {
        if (depth > 0)
        {
            return MakeASetByRecursion(depth - 1);
        }

        return new HashSet<Alike>();
    }

    public class MiddleMaker : CollectionMaker
    {
        public override ICollection<Alike> Make() => new HashSet<Alike>();
    }

    public sealed class LastMaker : MiddleMaker
    {
        public override ICollection<Alike> Make() => new List<Alike>();
    }

    private static void SortWithComparer(List<Ranked> items, IComparer<Ranked>? comparer) => items.Sort(comparer);

    private static void SortWithAnOptionalComparer(List<Ranked> items, IComparer<Ranked>? comparer = null) =>
        items.Sort(comparer);

    private static void SortThrough(List<RankedOwn> items, IComparer<RankedOwn>? comparer) =>
        SortOwnWithComparer(items: items, comparer: comparer);

    private static void SortOwnWithComparer(List<RankedOwn> items, IComparer<RankedOwn>? comparer) =>
        items.Sort(comparer);

    // A null comparer gets one that reads nothing.
    private static void SortWithAComparerIfNone(List<Ranked> items, IComparer<Ranked>? comparer)
    {
        comparer ??= Comparer<Ranked>.Create(static (_, _) => 0);
        items.Sort(comparer);
    }

    private static Cell<int> SortInABlock(IComparer<Ranked>? comparer = null) =>
        Cell.Loop<int>().WithoutCaptures(l =>
        {
            List<Ranked> all = [new(l.Map(v => v)), new(Plain)];
            all.Sort(comparer);

            return S.Hold(all.Count);
        });

    private static void SortInALocalFunction(List<RankedOwn> items, IComparer<RankedOwn>? comparer)
    {
        void Sort() => items.Sort(comparer);

        Sort();
    }

    private static void SortRanked(List<Ranked> items) => items.Sort();

    private static void SortRankedThrough(List<Ranked> items) => SortRanked(items);

    private static void SortRankedThroughTwo(List<Ranked> items) => SortRankedThrough(items);

    private static int CompareRanked(List<Ranked> items) => items[0].CompareTo(items[1]);

    private static int CompareRankedThrough(List<Ranked> items) => CompareRanked(items);

    private static int CompareRankedThroughTwo(List<Ranked> items) => CompareRankedThrough(items);

    private static void SortRankedWithThrough(List<Ranked> items, IComparer<Ranked>? comparer) =>
        SortWithComparer(items: items, comparer: comparer);

    private static void SortRankedInALocalFunction(List<Ranked> items, IComparer<Ranked>? comparer)
    {
        void Sort() => items.Sort(comparer);

        Sort();
    }

    // The getter reads through a helper of the object.
    public sealed class DeepReader(Cell<int> share)
    {
        public int Now => this.Read();

        private int Read() => share.Sample();
    }

    private static int NowOf(DeepReader reader) => reader.Now;

    private static int NowThrough(DeepReader reader) => NowOf(reader);

    private static int NowThroughTwo(DeepReader reader) => NowThrough(reader);

    public sealed class NowKeeper
    {
        public NowKeeper(DeepReader reader)
        {
            this.Now = reader.Now;
        }

        public int Now { get; }
    }

    private static int KeepNow(DeepReader reader) => new NowKeeper(reader).Now;

    private static int KeepNowThrough(DeepReader reader) => KeepNow(reader);

    // Equals reads only its own object, and is true only for the same object.
    public sealed class SameOwn(Cell<int> share)
    {
        public override bool Equals(object? obj) => share.Sample() >= 0 && ReferenceEquals(this, obj);

        public override int GetHashCode() => 1;
    }

    // Equals reads only the object that it gets, and is true only for the same object.
    public sealed class SamePeer(Cell<int> share)
    {
        public override bool Equals(object? obj) => obj is SamePeer other && other.Read() >= 0 && ReferenceEquals(this, obj);

        public override int GetHashCode() => 1;

        private int Read() => share.Sample();
    }

    // CompareTo reads only the object that it gets, and gives the order of the two ranks.
    public sealed class RankedPeer(Cell<int> share, int rank) : IComparable<RankedPeer>
    {
        public int CompareTo(RankedPeer? other) => other == null || other.Read() < 0 ? 1 : this.Rank.CompareTo(other.Rank);

        private int Rank => rank;

        private int Read() => share.Sample();
    }

    // The analyzer does not know the type of the result.
    private static IEnumerable<SameOwn> MakeASetOrAnArray(bool set, SameOwn first, SameOwn second) =>
        set ? new HashSet<SameOwn> { first, second } : new[] { first, second };

    public sealed class SlotHolder
    {
        public SettableSlot? Item { get; set; }
    }

    public sealed class CellKeeper(List<Cell<int>> cells)
    {
        public List<Cell<int>> Cells { get; } = cells;
    }

    public sealed record SlotRecord
    {
        public Cell<int>? Share { get; set; }
    }

    private static SettableSlot MakeAPlainSlot() => new() { Share = Plain };

    private static SettableSlot MakeASlotWith(Cell<int> share) => new() { Share = share };

    private static SettableSlot MakeASlotAndClear(Cell<int> share)
    {
        SettableSlot made = new() { Share = share };
        made.Share = Plain;

        return made;
    }

    public sealed class ListBehindAnInterface
    {
        public IList<Cell<int>> Cells { get; } = new List<Cell<int>>();
    }

    private static SettableSlot MakeASlotInto(List<SettableSlot> into, Cell<int> share)
    {
        SettableSlot made = new() { Share = share };
        into.Add(made);

        return made;
    }

    private static int KeepNowThroughTwo(DeepReader reader) => KeepNowThrough(reader);

    public sealed class DeepSlot
    {
        public Cell<int>? Share { get; set; }

        public int Now => this.Read();

        private int Read() => this.Share!.Sample();
    }

    private static int NowOfASlot(DeepSlot slot) => slot.Now;

    private static int NowOfASlotThrough(DeepSlot slot) => NowOfASlot(slot);

    private static int NowOfASlotThroughTwo(DeepSlot slot) => NowOfASlotThrough(slot);

    private static int NowOfASlotThroughThree(DeepSlot slot) => NowOfASlotThroughTwo(slot);

    public sealed class NeverTheSame : IEqualityComparer<SameOwn>
    {
        public bool Equals(SameOwn? x, SameOwn? y) => false;

        public int GetHashCode(SameOwn obj) => 1;
    }

    private static HashSet<SameOwn> MakeASetThatComparesReferences(SameOwn one) =>
        new(ReferenceEqualityComparer.Instance) { one };

    public sealed record CellsRecord(List<Cell<int>> Cells);

    public sealed class CellsCapturer(List<Cell<int>> cells)
    {
        public int First() => cells[0].Sample();
    }

    private static int FirstOfABox(CellBox box) => box.Cells[0].Sample();

    private static T TheSameObject<T>(T item) => item;

    private static void ClearASlot(SettableSlot slot)
    {
        slot.Share = Plain;
    }

    private static SettableSlot NextSlot(SettableSlot? before, Cell<int> share)
    {
        SettableSlot made = new() { Share = share };
        (before ?? made).Share = Plain;

        return made;
    }

    private static IList<Cell<int>> MakeAListAsAnInterface() => new List<Cell<int>> { Plain };

    public sealed class SlotInAMember
    {
        public SettableSlot Slot { get; } = new() { Share = Plain };
    }

    public sealed class AlwaysTheSame : IEqualityComparer<AlikeOwn>
    {
        public bool Equals(AlikeOwn? x, AlikeOwn? y) => true;

        public int GetHashCode(AlikeOwn obj) => 1;
    }

    public sealed class FilledPeers : HashSet<SamePeer>
    {
        public FilledPeers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                this.Add(new SamePeer(Plain));
            }
        }
    }

    private static SlotHolder HolderWith(SettableSlot item) => new() { Item = item };

    private static readonly SlotHolder SharedHolder = new();

    private static readonly SlotHolder HolderOfEachCall = new();

    private static SlotHolder TheHolderOfEachCall() => HolderOfEachCall;

    private static SlotHolder HolderWithItsOwnSlot() => new() { Item = new SettableSlot { Share = Plain } };

    private static SettableSlot SameSlotThroughALocal(SettableSlot given)
    {
        SettableSlot kept = given;

        return kept;
    }

    private static (Cell<int>? First, int Second) PlainAndZero() => (Plain, 0);

    private static SettableSlot LastSlotOfALoopThatReturns(Cell<int> share)
    {
        SettableSlot? last = null;

        for (int i = 0; i < 2; i++)
        {
            SettableSlot x = new() { Share = share };
            last = x;

            if (i == 1)
            {
                return last;
            }

            x.Share = Plain;
        }

        return last!;
    }

    private static Func<SettableSlot, SettableSlot> FirstSlotKeeper()
    {
        SettableSlot? kept = null;

        return given =>
        {
            if (kept == null)
            {
                kept = given;
            }

            return kept;
        };
    }

    public sealed class SlotOwner(SettableSlot item)
    {
        public SettableSlot Item { get; } = item;
    }

    public sealed class PlainParts
    {
        public void Deconstruct(out Cell<int>? first, out int second)
        {
            first = Plain;
            second = 0;
        }
    }

    public sealed class SlotKeeper
    {
        private SettableSlot? first;

        public Cell<int> FirstShareOfTwo(Cell<int> pending)
        {
            for (int i = 0; i < 2; i++)
            {
                SettableSlot x = new() { Share = Plain };

                if (i == 0)
                {
                    this.first = x;
                }

                this.first!.Share = pending;
                x.Share = Plain;
            }

            return this.first!.Share!;
        }
    }

    private static IEnumerable<SettableSlot> SlotsUntilABreak(Cell<int> share)
    {
        for (int i = 0; i < 2; i++)
        {
            SettableSlot x = new() { Share = share };

            yield return x;

            if (i == 0)
            {
                yield break;
            }

            x.Share = Plain;
        }
    }

    private static IEnumerable<SettableSlot> SlotsThatGetAPlainCell(Cell<int> share)
    {
        for (int i = 0; i < 2; i++)
        {
            SettableSlot x = new() { Share = share };

            yield return x;

            x.Share = Plain;
        }
    }

    public sealed class FirstSlotOwner
    {
        private SettableSlot? first;

        public SettableSlot FirstOf(SettableSlot given)
        {
            if (this.first == null)
            {
                this.first = given;
            }

            return this.first;
        }
    }

    public sealed class FirstSlotOfALoop
    {
        public FirstSlotOfALoop(Cell<int> pending)
        {
            for (int i = 0; i < 2; i++)
            {
                SettableSlot x = new() { Share = Plain };

                if (i == 0)
                {
                    this.First = x;
                }

                this.First!.Share = pending;
                x.Share = Plain;
            }
        }

        public SettableSlot? First { get; }
    }

    public class SlotOwnerBase
    {
        protected SlotOwnerBase(SettableSlot item) => this.Item = item;

        public SettableSlot Item { get; }
    }

    public sealed class DerivedSlotOwner : SlotOwnerBase
    {
        public DerivedSlotOwner(SettableSlot item)
            : base(item)
        {
        }
    }

    private static readonly SlotHolder[] SharedHolders = [new()];

    private static readonly List<(SettableSlot First, int Second)>[] SharedPairLists = [[]];

    private static readonly SlotPairs SharedSlotPairs = new() { Pairs = [(new SettableSlot(), 0)] };

    public sealed class SlotPairs
    {
        public List<(SettableSlot First, int Second)> Pairs { get; set; } = [];
    }

    private static Func<SettableSlot> KeeperOf(SettableSlot kept) => () => kept;

    private static (Cell<int>? First, SettableSlot Second) PlainAnd(SettableSlot slot) => (Plain, slot);

    private static readonly SettableSlot[] SharedSlots = [new()];

    private static void SampleIfAny(Cell<int>? cell)
    {
        if (cell != null)
        {
            Kept.Add(cell.Sample());
        }
    }

    private static readonly List<SettableSlot> SharedSlotList = [];

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
