using System;
using System.Collections.Generic;
using System.Linq;
using SodaFlow;

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
}

file static class Helpers
{
    public static Stream<T> SwitchCWithDeferredValues<T>(this Cell<Cell<T>> cca) =>
        Operational.Defer(cca.SwitchC().Values());
}
