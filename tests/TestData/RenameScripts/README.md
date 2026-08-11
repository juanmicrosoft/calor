# Rename corpus

The registered denominator for the **rename gate** (roadmap-v0.13-v0.15 §2.5
gate 4).

The claim under test: **rename edits target exact identifier tokens, and the
renamed program still behaves the same.** Compile success is explicitly *not*
the oracle — a capturing or over-broad rename compiles perfectly well and
quietly means something else — so every applying case is executed before and
after and the results compared.

Harness: `tests/Calor.Compiler.Tests/RenameHarnessTests.cs`.
Instrument: `calor rename` (`src/Calor.Compiler/Commands/RenameCommand.cs`),
over `ProjectSymbolIndex` + `RenameEngine`.

## Layout

```
RN-NN-name/
  script.json     registration: what it pins, which symbol, the new name,
                  the expected outcome, and what must survive untouched
  *.calr          the project
```

`target` names the symbol by the **nth whole-identifier occurrence** of a
marker. Whole-identifier matching matters: a marker of `Pick` would otherwise
select the class `Picker` appearing earlier in the file, silently pointing the
case at a different symbol than the one it claims to test. That happened while
this corpus was being written.

## What each case pins

| id | shape | outcome |
|----|-------|---------|
| RN-01 | a function and its call sites in another file | applies |
| RN-02 | two functions declaring the same local name; one renamed | applies |
| RN-03 | a parameter shadowing a field, with a second file reading that field | applies |
| RN-04 | renaming a local onto a name already bound in its scope | refuses (`NameCollision`) |
| RN-05 | one overload renamed, its same-named sibling untouched | applies |
| RN-06 | a module declared in two files | refuses (`SplitDeclaration`) |

## Why behaviour alone is not enough

Renaming *every* occurrence of a name consistently also preserves behaviour. A
purely behavioural oracle therefore cannot catch an over-broad rename, only a
capturing one. The first version of this corpus passed unchanged when the engine
was replaced with a text-directed rename — it pinned nothing about the property
the gate exists to establish.

Two things fix that, and both are load-bearing:

- **`preserved`** — names that must still appear, and how often, after the
  rename (RN-02: the other function still declares and returns its own `value`).
- **A second file that references the renamed symbol's neighbours** (RN-03: a
  separate module reads the field the parameter shadows, so a text-directed
  rename rewrites the field declaration and that file stops resolving).

## Discrimination

Every case was checked by injecting the fault it exists to catch:

| injected fault | cases that fail |
|---|---|
| text-directed rename (whole-identifier matches, identity ignored) | RN-02, RN-03 |
| cross-file call references not indexed | RN-01 |
| collision guard removed | RN-04 |
| split-declaration guard removed | RN-06 |

## Adding a case

Add a directory and a `script.json`; the harness enumerates the corpus.
`RegisteredCaseIdsAreStable` pins the id set, so shrinking the denominator is a
deliberate, reviewable edit. State in `pins` the failure the case would catch —
and check it, by injecting that failure and watching this case fail.
