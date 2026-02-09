---
layout: default
title: Stable Identifiers
parent: Philosophy
nav_order: 3
---

# Stable Identifiers: Language-Level Identity

One of Calor's most distinctive features is embedding unique identifiers directly into the language syntax. This document explains why this matters, what challenges it creates, and how Calor overcomes them.

---

## The Problem: Code Identity is Fragile

Traditional programming relies on **names and locations** to identify code:

```csharp
// How do you reference this function?
public static int Calculate(int x) => x * 2;  // Line 42 in Calculator.cs
```

This creates fundamental problems:

| Reference Method | Failure Mode |
|:-----------------|:-------------|
| File + line number | Breaks when any line above changes |
| Function name | Breaks on rename, ambiguous with overloads |
| File + function name | Breaks on file moves or renames |
| Hash of content | Changes on any modification |

For AI agents that need to track, reference, and modify code across development workflows, these fragile identities create real problems:

- **Lost context** - Agent references become stale after refactoring
- **Merge conflicts** - Parallel branches create identity collisions
- **Imprecise edits** - "Edit the calculate function" is ambiguous
- **Broken traceability** - Can't track an entity through its history

---

## The Solution: Semantic Identity in Syntax

Calor assigns every declaration a **unique ID that represents semantic identity**, not name or location:

```calor
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Calculate:pub}
   ^^^^^^^^^^^^^^^^^^^^^^^^^^^^
   This ID survives ANY refactoring
  §I{i32:x}
  §O{i32}
  §R (* x 2)
§/F{f_01J5X7K9M2NPQRSTABWXYZ12}
```

The ID `f_01J5X7K9M2NPQRSTABWXYZ12`:
- Survives renaming (`Calculate` → `Compute`)
- Survives moving to a different file
- Survives reformatting and refactoring
- Is globally unique across all codebases (ULID)
- Can be referenced unambiguously forever

---

## Benefits of Language-Level IDs

### 1. Agent Instructions Become Precise

Without IDs:
```
"Update the validate function in the user module to check email format"
```
- Which validate function? There might be multiple
- What if it was renamed to `validateUser`?
- What if the file was moved?

With IDs:
```
"Update function f_01J5X7K9M2NPQRSTABWXYZ12 to check email format"
```
- Unambiguous target
- Survives any refactoring
- Machine-verifiable reference

### 2. Merge Safety Across Branches

Traditional code suffers from merge conflicts when parallel branches modify the same entities. With ULIDs:

| Branch A | Branch B | Result |
|:---------|:---------|:-------|
| Adds `f_01ABC...` | Adds `f_01DEF...` | Clean merge |
| Modifies `f_01ABC...` | Renames `f_01ABC...` | Clean merge |
| Both add unnamed function | Conflict | N/A in Calor |

ULID's 80-bit random component makes collisions statistically impossible (~10^-24 probability per pair).

### 3. Round-Trip Stability

Code generation and conversion preserve identity:

```calor
// Original Calor
§F{f_01ABC:Add:pub}
  §O{i32}
  §R (+ a b)
§/F{f_01ABC}
```

```csharp
// Generated C# preserves ID
[CalorId("f_01ABC")]
public static int Add(int a, int b) => a + b;
```

```calor
// Re-converted: Same ID
§F{f_01ABC:Add:pub}
  §O{i32}
  §R (+ a b)
§/F{f_01ABC}
```

### 4. Traceable History

IDs enable tracking entities through their entire lifecycle:

```
f_01J5X7... created in commit a1b2c3 (Juan, 2024-01-15)
f_01J5X7... renamed Calculate→Compute in commit d4e5f6 (AI Agent, 2024-02-20)
f_01J5X7... moved to utils.calr in commit g7h8i9 (Maria, 2024-03-10)
f_01J5X7... implementation updated in commit j0k1l2 (AI Agent, 2024-04-05)
```

The entity's history is continuous regardless of name or location changes.

---

## Why This Matters for AI-First Development

Traditional languages assume humans are the primary code authors. When AI agents become primary authors, different tradeoffs make sense.

| Human-First | AI-First |
|:------------|:---------|
| Names are primary identifiers | Names are documentation |
| Location matters | Identity is intrinsic |
| Minimize syntax | Maximize precision |
| Trust implicit conventions | Require explicit contracts |

Calor's stable identifiers are part of a broader shift toward **explicit, machine-verifiable code** that agents can reliably understand, reference, and modify.

---

## The Challenges

Embedding IDs in code creates real challenges. Here's how Calor addresses each:

### Challenge 1: ID Generation

**Problem:** Who generates IDs? When? How to ensure uniqueness?

**Solution:** ULID (Universally Unique Lexicographically Sortable Identifier)

- **Timestamp + Random**: 48-bit timestamp + 80-bit random = unique without coordination
- **No central registry**: Any machine can generate IDs independently
- **Sortable**: IDs sort chronologically by creation time
- **CLI tooling**: `calor ids assign` generates IDs automatically

```bash
# Generate IDs for new declarations
calor ids assign .

# Preview what would be assigned
calor ids assign . --dry-run
```

### Challenge 2: ID Churn

**Problem:** Agents might accidentally change IDs during edits, breaking references.

**Solution:** Multi-layer protection combining shipped tooling and convention-based rules.

**Shipped protections:**

1. **CLI validation**: `calor ids check` detects ID modifications, duplicates, and missing IDs. Run in CI to fail builds with ID issues.
   ```bash
   calor ids check src/
   # Error Calor0801: ID 'f_01ABC...' was modified (expected stable)
   ```

2. **Hook validation**: Pre-write hooks detect ID modifications before they're saved:
   ```json
   {
     "hooks": {
       "PreToolUse": [{
         "matcher": { "tool": "Write", "file": "*.calr" },
         "hooks": [{ "command": "calor hook validate-ids $TOOL_INPUT" }]
       }]
     }
   }
   ```

**Convention-based protections:**

3. **Agent rules**: Skills (e.g., SKILL.md) explicitly forbid ID modification:
   ```markdown
   ## ID Integrity Rules - CRITICAL
   1. **NEVER** modify an existing ID
   2. **NEVER** copy IDs when extracting code
   3. **OMIT** IDs for new declarations
   ```
   These are conventions enforced by agent prompts, not by the compiler.

### Challenge 3: Duplicate IDs

**Problem:** Copy-paste or extraction might create duplicate IDs.

**Solution:** Detection and automatic resolution

```bash
# Detect duplicates
calor ids check .
# Error Calor0803: Duplicate ID 'f_01ABC...'
#   First: src/math.calr:15 (Calculate)
#   Also:  src/utils.calr:42 (Compute)

# Fix duplicates (keep first, reassign others)
calor ids assign . --fix-duplicates
```

### Challenge 4: Visual Noise

**Problem:** IDs add visual clutter to code.

**Solution:** IDs are for machines, not humans

1. **Agents are the primary audience**: Calor optimizes for machine readability
2. **IDE support (roadmap)**: Future tooling can hide IDs in display
3. **Test IDs for docs**: Short IDs (`f001`) allowed in tests/docs/examples

```calor
// Production code: Full ULID
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Calculate:pub}

// Documentation: Readable test ID
§F{f001:Calculate:pub}
```

Until IDE tooling ships, full ULIDs are visible in source. This is an accepted tradeoff — agents are the primary audience. For human review, the generated C# uses readable `[CalorId("f_01ABC...")]` attributes that collapse in most editors.

### Challenge 5: New Developers

**Problem:** Developers unfamiliar with IDs might create invalid ones.

**Solution:** Omit IDs, let tooling assign them

```calor
// Developer writes (no ID):
§F{:NewFunction:pub}
  §O{void}
§/F{}

// After `calor ids assign`:
§F{f_01NEW...:NewFunction:pub}
  §O{void}
§/F{f_01NEW...}
```

The tooling handles the complexity; developers just write code.

---

## Implementation

### ID Format

Production IDs use ULID with kind prefix:

| Kind | Prefix | Example |
|:-----|:-------|:--------|
| Module | `m_` | `m_01J5X7K9M2NPQRSTABWXYZ12` |
| Function | `f_` | `f_01J5X7K9M2NPQRSTABWXYZ12` |
| Class | `c_` | `c_01J5X7K9M2NPQRSTABWXYZ12` |
| Interface | `i_` | `i_01J5X7K9M2NPQRSTABWXYZ12` |
| Method | `mt_` | `mt_01J5X7K9M2NPQRSTABWXYZ12` |
| Property | `p_` | `p_01J5X7K9M2NPQRSTABWXYZ12` |
| Constructor | `ctor_` | `ctor_01J5X7K9M2NPQRSTABWXYZ12` |
| Enum | `e_` | `e_01J5X7K9M2NPQRSTABWXYZ12` |

### CLI Commands

```bash
# Validate all IDs in a directory
calor ids check src/

# Assign IDs to declarations missing them
calor ids assign src/

# Fix duplicate IDs
calor ids assign src/ --fix-duplicates

# Preview changes
calor ids assign src/ --dry-run

# Generate ID index
calor ids index src/ > calor.ids.json
```

### Preservation Rules

| Operation | ID Behavior |
|:----------|:------------|
| Rename | Preserved |
| Move file | Preserved |
| Reformat | Preserved |
| Change implementation | Preserved |
| Extract helper | **New ID** for helper |
| Copy code | **New ID** required |

---

## Conclusion

Language-level stable identifiers are a foundational feature of AI-first language design. By making identity explicit and intrinsic rather than implicit and derived, Calor enables:

- Precise agent instructions that survive refactoring
- Safe parallel development without merge conflicts
- Complete traceability of code entities through time
- Reliable round-trip conversion between formats

The challenges are real - ID generation, churn prevention, duplicate detection - but solvable with appropriate tooling and conventions. The benefits unlock new possibilities for AI-assisted development workflows.

---

## Next

- [ID Specification](/calor/docs/ids/) - Complete technical specification
- [CLI Reference: ids](/calor/cli/ids/) - Command-line tools
- [Design Principles](/calor/philosophy/design-principles/) - "Everything has an ID"
