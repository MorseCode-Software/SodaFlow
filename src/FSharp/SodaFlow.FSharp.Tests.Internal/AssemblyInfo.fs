// SodaFlow runs each transaction behind one process-wide lock. The same static machinery that
// holds a graph from one test also holds a graph from a second test. Thus, these tests cannot
// operate at the same time. NUnit ran them one at a time. TUnit runs tests in parallel by
// default, thus this code must give the constraint.
module SodaFlow.Tests.Internal.AssemblyInfo

open TUnit.Core

[<assembly: NotInParallel>]
do ()
