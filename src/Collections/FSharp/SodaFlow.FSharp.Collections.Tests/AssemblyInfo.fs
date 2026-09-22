// SodaFlow puts each transaction in sequence behind one lock for the full process, and the same
// static code can get to a graph from one test and a graph from a second test. Thus, these tests
// cannot run at the same time. NUnit ran them one at a time. TUnit runs tests at the same time by
// default, thus this code must give the constraint.
module SodaFlow.Collections.Tests.AssemblyInfo

open TUnit.Core

[<assembly: NotInParallel>]
do ()
