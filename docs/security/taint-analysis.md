# Taint analysis

Calor's taint analysis is a forward may-taint analysis over the bound
`ControlFlowGraph`. Facts use stable `SymbolId` roots and field/index access paths,
so branch joins and loop back edges use a monotonic union lattice. Reference variables
point to stable abstract objects, so rebinding a variable does not retarget aliases to
the object's old fields or elements. Alias joins retain every possible target. A field
or collection-element write is strong only for a
singleton target; otherwise it weakly updates every possible alias. A safe reassignment
therefore clears an earlier value's taint only when its target is proven singleton.

Built-in sources, sinks, and sanitizers are an exact identity manifest. A rule matches
either a resolved function/type/method signature or an exact unresolved call target;
it never uses a substring. Any populated resolved type-and-method identity prevents a
target-string sanitizer rule from applying, including non-BCL types. A resolved local
function named `sql_escape` is therefore not accidentally trusted.
For example, the manifest's `sql_escape` sanitizes SQL-query output only, while
`desanitize` is not a sanitizer. `Console.ReadLine` is an exact call-return user-input
source.

Every finding includes `ProvenancePath`, an ordered sequence from source through
assignments/calls to the sink. Direct source-to-sink findings are always reported:
`MinTaintHops` remains only as a compatibility/ranking option, and `--all-findings`
does not suppress or enable exploitable taint flows.

At a module boundary, exact resolved calls use parameter-to-return and
parameter-to-sink summaries, unions every resolved overload alternative, and splices
callee evidence into the caller's provenance. Exact BCL sink signatures include
`System.IO.File.WriteAllText`, `Delete`, and `Open`.
`StrictExternalCalls` also models unresolved external return values as `ExternalApi`
sources. The executable precision/recall scorecard lives in
`tests/TestData/Security/TaintAnalysis/manifest.json`. The analysis is intramodule:
persistent heap state across separate method calls and unresolved aliasing beyond
retained bound access paths remain conservative limitations.
