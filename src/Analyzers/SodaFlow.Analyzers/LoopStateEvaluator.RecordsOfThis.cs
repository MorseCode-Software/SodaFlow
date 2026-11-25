using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a write to the object of this through a different name while code on that object
// runs. A lambda of the caller that a method runs can make such a write. The write goes to the store, and not to the
// record of the members of this.
internal sealed partial class LoopStateEvaluator
{
    // Records where the record of the members of this began, for code on an object of the store. A callee on the same
    // object has the same record, and so does a method group of it. Thus, the first code with the record gives the
    // start.
    private void NotesTheStartOfItsRecord()
    {
        if (this.objectsOfThis.IsEmpty)
        {
            return;
        }

        ImmutableArray<MadeObject> objects = this.objectsOfThis;
        int changes = this.region.Objects.Changes;

        _ = this.region.StartsOfRecords.GetValue(
            key: this.thisMembers,
            createValueCallback: _ => new StartOfARecord(objects: objects, changes: changes));
    }

    // Records the change of the store at a sure write through this. The write replaces each change of the object
    // before it.
    private void NotesASureWriteThroughThis(ISymbol key)
    {
        if (this.region.StartsOfRecords.TryGetValue(key: this.thisMembers, value: out StartOfARecord? start))
        {
            start.WrittenAt[key] = this.region.Objects.Changes;
        }
    }

    // True when the store changed an object of a record of the members of this after the record began. A callee on
    // that object then has a state to read, also where its record has no state. See WrittenThroughASecondNameOfThis.
    private bool ChangedAfterTheStartOf(Dictionary<ISymbol, LoopState> record) =>
        this.region.StartsOfRecords.TryGetValue(key: record, value: out StartOfARecord? start)
        && this.region.Objects.Now(start.Objects).Any(made => this.region.Objects.ChangedAt(made) > start.Changes);

    // The state that the store has now for a member of the object of this code. The store changed that object after
    // the record began and after the last sure write through this. Such a change is a write through a different name
    // of the object, which the record does not have. None for each other read.
    private LoopState WrittenThroughASecondNameOfThis(ISymbol member)
    {
        if (!this.region.StartsOfRecords.TryGetValue(key: this.thisMembers, value: out StartOfARecord? start))
        {
            return LoopState.None;
        }

        ISymbol key = member.OriginalDefinition;

        int after = start.WrittenAt.TryGetValue(key: key, value: out int written)
            ? Math.Max(val1: written, val2: start.Changes)
            : start.Changes;

        LoopState found = LoopState.None;

        foreach (MadeObject made in this.region.Objects.Now(start.Objects))
        {
            if (this.region.Objects.ChangedAt(made) > after
                && this.region.Objects.StateOf(made)?.Members is { } members)
            {
                found = found.Join(members.StateOf(key).FromIfUnknown(key));
            }
        }

        return found;
    }

    // A copy of the record of the members of this, for a quiet evaluator. The copy has the start of the record. A write
    // through a different name of the object is also a write for the reads of that evaluator.
    private Dictionary<ISymbol, LoopState> ACopyOfTheRecordOfThis()
    {
        Dictionary<ISymbol, LoopState> copy =
            new(dictionary: this.thisMembers, comparer: SymbolEqualityComparer.Default);

        if (!this.region.Objects.IsEmpty
            && this.region.StartsOfRecords.TryGetValue(key: this.thisMembers, value: out StartOfARecord? start))
        {
            this.region.StartsOfRecords.Add(key: copy, value: start);
        }

        return copy;
    }

    // The origin in this code of a read in a callee of a cell that came through a parameter. The argument has no
    // origin, but a function of the cell can read a member of this. That member is then the origin, as for a read of
    // the cell in this code. The cell ran in the callee, thus a quiet evaluator runs it again. Null where the
    // argument has no cell to run.
    private ISymbol? OriginOfACellArgument(SyntaxNode argument, LoopState given) =>
        given.Computed.IsEmpty
            ? null
            : this.Quiet().AtTheFirstRead(read: argument, state: given, marks: null, again: true).Origin;

    /// <summary>
    ///     The objects of the store that a record of the members of this is
    ///     of. The count of changes of the store when the record began.
    /// </summary>
    /// <param name="objects">The objects.</param>
    /// <param name="changes">The count of changes of the store.</param>
    internal sealed class StartOfARecord(in ImmutableArray<MadeObject> objects, in int changes)
    {
        public ImmutableArray<MadeObject> Objects { get; } = objects;

        public int Changes { get; } = changes;

        // For each member: the count of changes of the store at the last sure write through this.
        public Dictionary<ISymbol, int> WrittenAt { get; } = new(SymbolEqualityComparer.Default);
    }
}
