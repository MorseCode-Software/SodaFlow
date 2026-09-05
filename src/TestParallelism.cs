// SodaFlow serializes every transaction behind a single process-wide lock, and a graph built in one
// test is reachable from the same static machinery as a graph built in another. Tests which span
// threads and time their own interleavings therefore cannot run alongside each other: they contend
// for that lock and observe each other's timing. NUnit ran this suite one test at a time; TUnit runs
// tests in parallel by default, so the constraint has to be stated rather than assumed.
[assembly: TUnit.Core.NotInParallel]
