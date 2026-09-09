1.0.0

First release.

Requires SodaFlow.FSharp 4.x and SodaFlow.Collections.Core 1.x. Installing this
brings the F# API it extends, so one install gives you the whole surface.

SodaFlow.Functional arrives with it, transitively, where SodaFlow.FSharp
deliberately leaves it out. Optionality here is Maybe<'T> rather than option,
because that is what the collection itself answers with; converting at this
boundary would cost a graph node per cell to restate what both types already
say.

---

About this package

A large keyed collection for cases where the number of items being actively
observed is a small fraction of the total. Per-item observation costs one hash
lookup per observer per transaction, independent of collection size.

The same library as SodaFlow.Collections, with the arguments ordered for the
pipeline - the collection comes last, so a chain reads in the order it runs -
and F# functions rather than Func for predicates and selectors.

  let accounts = create keyOf initial [ fromAdds opened; fromRemoves closed ]

  let topTen =
      accounts
      |> sortByDescending (fun _ state -> state.Balance)
      |> filter (fun _ state -> not state.IsFrozen)
      |> take 10

  let selected = accounts |> stateCell selectedKey

Everything that can change the collection is declared at construction: create
takes the initial contents and every edit stream, lifted from domain streams by
fromAdds, fromRemoves, fromUpdates and fromStates. There is no imperative entry
point.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
