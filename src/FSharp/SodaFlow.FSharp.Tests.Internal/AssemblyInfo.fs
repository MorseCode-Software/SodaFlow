// SodaFlow serializes every transaction behind a single process-wide lock, and a graph built in one
// test is reachable from the same static machinery as a graph built in another, so these tests
// cannot run alongside each other. NUnit ran them one at a time; TUnit runs tests in parallel by
// default, so the constraint has to be stated rather than assumed.
module SodaFlow.Tests.Internal.AssemblyInfo

open TUnit.Core

[<assembly: NotInParallel>]
do ()
