## Calor Language Overview

Module, class and function bodies use indentation and lowercase, space-separated headers.
Functions use func, type:name parameters and → returnType; public visibility is the default.
Types and members include class, record, interface, property and this[...] indexer declarations.
Use let for immutable bindings, var for mutable bindings and set for updates.
Control flow includes if/else if/else, while, each collection iteration and two distinct for modes.
Contracts and authoritative effects describe obligations and side effects with requires, => and !.
Async functions distinguish their body result from the callable Task or ValueTask carrier.
Expressions use C# precedence, postfix calls and indexing. Collections use ordinary .NET types.
Types use C# names, T[] arrays and T? nullability. Literals include 42, true, "hello" and 18.00m.
Query a specific feature for complete examples; use action=features for conversion support.
