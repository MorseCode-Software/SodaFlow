; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SF1001 | SodaFlow.Loops | Warning | A looped value is read before its loop closes
SF1002 | SodaFlow.Loops | Warning | A loop closes with a value that is linked to its own placeholder
SF1003 | SodaFlow.Loops | Info | A looped value leaves the library before its loop closes
SF1004 | SodaFlow.Loops | Warning | A callee reads a looped value before its loop closes
SF1005 | SodaFlow.Loops | Warning | A member built from a looped value is read before its loop closes
SF1006 | SodaFlow.Loops | Warning | A loop closes with a value whose initial value reads its own placeholder
SF2001 | SodaFlow.Listeners | Warning | Nothing holds the listener that Listen or ListenOnce returns
