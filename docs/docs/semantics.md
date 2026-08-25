# Denotational semantics

SodaFlow implements the **denotational semantics of Sodium**, unchanged.

That is a stronger claim than "it behaves similarly." Sodium is one of the few
FRP libraries with a written formal specification: every primitive has a precise
mathematical meaning, independent of any implementation. Stephen Blackheath wrote
that specification, along with an executable reference implementation in Haskell.
SodaFlow inherits the model and is tested against it.

## Why this matters

Most reactive libraries define their behaviour by what the code happens to do.
Edge cases — simultaneous events, a stream that feeds itself, a `switch` that
changes the graph mid-transaction — get whatever the implementation gives you,
and the answer can change between releases.

A denotational semantics removes that ambiguity. Each primitive is a function
over a timeline of occurrences, so questions like *"if two streams fire in the
same transaction, what does `Merge` produce?"* have an answer derived from the
specification rather than from reading the source. When SodaFlow and the
specification disagree, SodaFlow is wrong.

This is also what makes the guarantees in [Transactions](transactions.md) and
[Switch](switch.md) trustworthy rather than aspirational.

## The specification

The paper and its executable Haskell reference are vendored in this repository
under `denotational/`, with their full history. The links below point at the
canonical upstream copies:

| | In this repository | Canonical copy |
| --- | --- | --- |
| Paper | `denotational/Denotational Semantics of Sodium - 1.1.pdf` | [*Denotational Semantics of Sodium 1.1*](https://github.com/SodiumFRP/sodium/blob/master/denotational/Denotational%20Semantics%20of%20Sodium%20-%201.1.pdf) |
| Executable reference | `denotational/sodium.hs` | [`sodium.hs`](https://github.com/SodiumFRP/sodium/blob/master/denotational/sodium.hs) — the semantics as runnable HUnit cases |
| Core definitions | `denotational/Reactive/Sodium/Denotational.hs` | [`Denotational.hs`](https://github.com/SodiumFRP/sodium/blob/master/denotational/Reactive/Sodium/Denotational.hs) |

That material is Copyright © 2015 Stephen Blackheath and is licensed separately
from the rest of SodaFlow — see `denotational/LICENSE` and the `NOTICE` file. It
is included unmodified, and it describes **Sodium**; the naming inside it is
deliberately left alone.

## How conformance is checked

The semantics are not just cited, they are executed. `DenotationalSemanticsTests`
transcribes the Haskell reference cases into both language surfaces — **23 tests
in C# and 23 in F#** — and asserts SodaFlow produces the specified occurrences:

| | |
| --- | --- |
| C# | `src/CSharp/SodaFlow.Tests/DenotationalSemanticsTests.cs` |
| F# | `src/FSharp/SodaFlow.FSharp.Tests/DenotationalSemanticsTests.fs` |

They cover `Never`, `MapS`, `Snapshot`, `Merge`, `Filter`, `SwitchS`, `Updates`,
`Split`, `Constant`, `Hold`, `MapC`, `Apply` and `Sample` — the primitives the
paper defines — plus the .NET additions `Value`, `ListenC`, `ConstantLazy`,
`SwitchC` and `SampleLazy`.

Each test drives a simulated timeline: streams are given fixed occurrences at
integer times, the transaction at each time is run, and the observed output list
is compared against the value the specification requires. `Test_Merge_TestCase`,
for instance, pins down exactly what simultaneous occurrences produce — the case
most libraries leave undefined.

## Reading further

The paper is short and worth reading directly. For the informal treatment of the
same model, Blackheath and Jones'
[*Functional Reactive Programming*](https://www.manning.com/books/functional-reactive-programming)
(Manning) is the best reference both for the Sodium basics SodaFlow inherits and
for Functional Reactive Programming in general.
