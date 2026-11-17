using System;
using System.Collections;
using System.Collections.Generic;
using SodaFlow;

namespace SodaFlow.Analyzers.ShapesNoBody;

// The harness compiles this file to an assembly of its own, and the shapes reference that
// assembly. Thus, the analyzer has no body to read for a member of these classes, as for the code
// of a package. A class is generic where it uses a type of the shapes, because this assembly
// cannot name such a type. This file is test data, and the project does not compile it.
public static class NoBody
{
    // The analyzer knows that code of an assembly with no reference to the library cannot sample
    // a cell. This field makes the reference. Thus, each class here is code that can know a cell.
    public static readonly Type KnowsTheLibrary = typeof(Cell<>);

    public static IFiller<T> Filler<T>()
        where T : new() =>
        new Filler<T>();

    public static IBag<T> Bag<T>() => new Bag<T>();

    public static IRow<T> Row<T>() => new Row<T>();

    public static IHolderFiller<THolder> HolderFiller<THolder, T>(string list)
        where THolder : class
        where T : new() =>
        new HolderFiller<THolder, T>(list);

    public static ILine<T> Line<T>() => new Line<T>();

    public static IActionKeeper ActionKeeper() => new ActionKeeper();

    public static IMaker<T> Maker<T>()
        where T : new() =>
        new Maker<T>();

    public static IRunner Runner() => new Runner();

    public static IEcho Echo() => new Echo();

    public static IDvLinksHolder<T> DvLinksHolder<T>() => new DvLinksHolder<T>();

    public static IKeeper<T, TBox> Keeper<T, TBox>(string list)
        where T : new()
        where TBox : class =>
        new Keeper<T, TBox>(list);

    // The list in a property of an object of the shapes. The name of the property comes from the
    // shapes, because this assembly cannot name the type of the object.
    internal static List<T> ListIn<T>(object holder, string property) =>
        (List<T>)holder.GetType().GetProperty(property)!.GetValue(holder)!;
}

// A base class whose constructor puts each new object into a static list. The analyzer has no
// body to read for that constructor.
public class NoBodyListing
{
    public static readonly List<NoBodyListing> All = [];

    public NoBodyListing() => All.Add(this);
}

// A method with the name of a query of the platform, for a list of arrays only. It gives the last
// array, and the analyzer has no body to learn that from.
public static class NoBodyQueries
{
    public static T[] First<T>(this List<T[]> all) => all[all.Count - 1];
}

public interface IFiller<T>
{
    void Fill(List<T> list);
}

public interface IBag<T>
{
    T this[int index] { get; set; }

    void Put(T slot);
}

public interface IRow<T> : IEnumerable<T>
{
    void Put(T slot);
}

public interface IHolderFiller<THolder>
{
    void Fill(THolder holder);
}

public interface ILine<T>
{
    void Put(T slot);

    IEnumerable<T> All();

    T Last();

    int Count();
}

public interface IActionKeeper
{
    void Keep(Action action);

    void Run();
}

public interface IMaker<T>
{
    void Put(T slot);

    T Fresh();

    IEnumerable<T> AllFresh();
}

public interface IRunner
{
    void Keep(Action change);

    void Run();
}

// An object that is no sequence, with a method that gives back its value as object.
public interface IEcho
{
    object Back(object value);
}

public interface IKeeper<T, TBox>
{
    void Keep(List<T> all);

    void KeepBox(TBox box);

    void Put(T first);

    void Swap(T first);

    List<T> Made();
}

internal sealed class Filler<T> : IFiller<T>
    where T : new()
{
    public void Fill(List<T> list) => list.Add(new T());
}

internal sealed class Bag<T> : IBag<T>
{
    private readonly List<T> slots = [];

    public T this[int index]
    {
        get => this.slots[index];
        set
        {
            while (this.slots.Count <= index)
            {
                this.slots.Add(value);
            }

            this.slots[index] = value;
        }
    }

    public void Put(T slot) => this.slots.Add(slot);
}

internal sealed class Row<T> : IRow<T>
{
    private readonly List<T> slots = [];

    public void Put(T slot) => this.slots.Add(slot);

    public IEnumerator<T> GetEnumerator() => this.slots.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

internal sealed class HolderFiller<THolder, T>(string list) : IHolderFiller<THolder>
    where THolder : class
    where T : new()
{
    private readonly string list = list;

    public void Fill(THolder holder) => NoBody.ListIn<T>(holder: holder, property: this.list).Add(new T());
}

internal sealed class Line<T> : ILine<T>
{
    private readonly List<T> slots = [];

    public void Put(T slot) => this.slots.Add(slot);

    public IEnumerable<T> All() => this.slots;

    public T Last() => this.slots[this.slots.Count - 1];

    public int Count() => this.slots.Count;
}

internal sealed class ActionKeeper : IActionKeeper
{
    private readonly List<Action> actions = [];

    public void Keep(Action action) => this.actions.Add(action);

    public void Run() => this.actions.ForEach(static action => action());
}

internal sealed class Maker<T> : IMaker<T>
    where T : new()
{
    private readonly List<T> slots = [];

    public void Put(T slot) => this.slots.Add(slot);

    public T Fresh() => new();

    public IEnumerable<T> AllFresh() => [new T()];
}

internal sealed class Runner : IRunner
{
    private Action kept = static () => { };

    public void Keep(Action change) => this.kept = change;

    public void Run() => this.kept();
}

public interface IDvLinksHolder<T>
{
    T Held { get; set; }
}

internal sealed class DvLinksHolder<T> : IDvLinksHolder<T>
{
    public T Held { get; set; } = default!;
}

internal sealed class Echo : IEcho
{
    public object Back(object value) => value;
}

internal sealed class Keeper<T, TBox>(string list) : IKeeper<T, TBox>
    where T : new()
    where TBox : class
{
    private readonly string list = list;
    private List<T> kept = [];

    public void Keep(List<T> all) => this.kept = all;

    public void KeepBox(TBox box) => this.kept = NoBody.ListIn<T>(holder: box, property: this.list);

    public void Put(T first) => this.kept.Insert(index: 0, item: first);

    public void Swap(T first) => this.kept[0] = first;

    public List<T> Made() => [new T()];
}
