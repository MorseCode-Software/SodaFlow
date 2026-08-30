2.0.1

Adds the release notes below. 2.0.0 shipped without any, because the mechanism
that reads them from a file landed after that version was tagged. No code
change since 2.0.0.

2.0.0

BREAKING: the two listen functions swapped names, as in the C# package.
Stream.listen and Cell.listen are now the weak ones, listenStrong the strong
ones, and the listenS and listenC shorthand aliases follow.

Same quiet breakage: a call that ignores the returned handle keeps compiling
and becomes a weak subscription, which fails later as listeners that stop
firing. Rename every existing listen call to listenStrong first, then decide
which of them wanted to be weak.

Fixed: alarms were never delivered while the thread pool was saturated. The
timer loop ran with Async.Start on the pool, so under pool pressure it was
never scheduled and no alarm fired. It now waits on a dedicated background
thread. The per-iteration CancellationTokenSource, allocated on every pass and
never disposed, is gone, and SetTimer no longer cancels while holding the timer
lock.

Every module, type and function is now documented. Previously this assembly
shipped an empty XML documentation file despite having generation enabled, so
IntelliSense had nothing to show for any of it. FS3390 is enabled, so the
comments are checked at build time.

Requires SodaFlow.Core 2.0.0 or newer. Do not mix 1.x and 2.x packages.

---

About this package

The F# surface over SodaFlow.Core: modules of curried, pipeline-friendly
functions over streams, cells and behaviors, plus an AutoOpen module of
suffixed aliases so open SodaFlow alone gives you mapS, listenC, sendS and the
rest. Install this to use SodaFlow from F#; it brings the core with it.

Full notes: https://github.com/MorseCode-Software/SodaFlow/releases
