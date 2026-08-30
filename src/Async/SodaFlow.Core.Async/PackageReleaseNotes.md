2.0.1

Adds the release notes below. 2.0.0 shipped without any, because the mechanism
that reads them from a file landed after that version was tagged. No code
change since 2.0.0.

2.0.0

No public API change. Major because this assembly calls SodaFlow.Core's
internals, and 2.0.0 of the core renamed them: ListenWeakImpl became
ListenImpl, while ListenImpl kept its name and changed meaning from the strong
listener to the weak one.

That makes the pairing load-bearing in both directions. Against a 1.0.0 core
this version throws MissingMethodException; a 1.0.0 of this package against a
2.x core binds to the strong listener instead and roots the whole async
pipeline for good. Upgrade the two together.

---

About this package

The engine behind MapAsync: the tracking, the concurrency strategies and the
AsyncMapStatus a caller holds. Not installed directly - take SodaFlow.Async for
C# or SodaFlow.FSharp.Async for F#, both of which bring it with them.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
