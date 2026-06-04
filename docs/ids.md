# Calor ID Specification

Version: 1.0.0

This document defines the normative contract for Calor IDs. IDs are the foundation of semantic identity in Calor, enabling stable references across renames, refactors, file moves, code generation, and version control operations.

---

## 1. What IDs Represent

### 1.1 Semantic Identity

An ID represents the **semantic identity** of a declaration, not its name or location.

| Changes | ID Behavior |
|---------|-------------|
| Rename `Calculate` → `Compute` | ID unchanged |
| Move to different file | ID unchanged |
| Reformat/refactor surrounding code | ID unchanged |
| Extract to new helper function | New ID for helper |
| Copy code to new function | New ID required |

### 1.2 Why IDs Matter

IDs enable:
- **Stable navigation** - Links and references survive refactoring
- **Merge safety** - No collisions when branches modify same entities
- **Agent reliability** - Code generators can preserve identity
- **Traceability** - Track entities through development history

---

## 2. Declaration Kinds Requiring IDs

Every structural declaration in Calor must have a unique ID:

| Kind | Tag | Prefix | Example |
|------|-----|--------|---------|
| Module | `§M` | `m_` | `m_01J5X7K9M2NPQRSTABWXYZ12` |
| Function | `§F` | `f_` | `f_01J5X7K9M2NPQRSTABWXYZ12` |
| Class | `§CL` | `c_` | `c_01J5X7K9M2NPQRSTABWXYZ12` |
| Interface | `§IFACE` | `i_` | `i_01J5X7K9M2NPQRSTABWXYZ12` |
| Property | `§PROP` | `p_` | `p_01J5X7K9M2NPQRSTABWXYZ12` |
| Method | `§MT` | `mt_` | `mt_01J5X7K9M2NPQRSTABWXYZ12` |
| Constructor | `§CTOR` | `ctor_` | `ctor_01J5X7K9M2NPQRSTABWXYZ12` |
| Enum | `§EN` | `e_` | `e_01J5X7K9M2NPQRSTABWXYZ12` |
| Enum extension | `§EXT` | `ext_` | `ext_01J5X7K9M2NPQRSTABWXYZ12` |
| Operator overload | `§OP` | `op_` | `op_01J5X7K9M2NPQRSTABWXYZ12` |
| Refinement type | `§RTYPE` | `rt_` | `rt_01J5X7K9M2NPQRSTABWXYZ12` |
| Proof obligation | `§PROOF` | `po_` | `po_01J5X7K9M2NPQRSTABWXYZ12` |
| Indexed type | `§ITYPE` | `it_` | `it_01J5X7K9M2NPQRSTABWXYZ12` |
| Indexer | `§IXER` | `ix_` | `ix_01J5X7K9M2NPQRSTABWXYZ12` |

### 2.1 Declarations NOT Requiring IDs

Some elements use local scope identifiers rather than global IDs:

- Loop variables (`§L{for1:i:...}`) - scope-local
- If blocks (`§IF{if1}`) - scope-local
- Call targets (`§C{target}`) - reference, not declaration

### 2.2 Structural IDs Are Optional in v6+

As of v6, structural openers (`§M`, `§F`, `§AF`, `§L`,
`§IF`, `§TR`, `§CL`, `§IFACE`, `§MT`, `§CTOR`, `§EN`)
accept a **compact form** that omits the leading `{id:…}` block. The
compiler auto-assigns an ID of the shape `<prefix>_auto<N>`.

When the opener uses the compact form, the matching closing tag
(`§/M`, `§/F`, `§/AF`, `§/L`, `§/I`, `§/TR`, `§/CL`, `§/IFACE`,
`§/MT`, `§/CTOR`, `§/EN`) may also omit `{id}`. **The two sides are
linked**: if the opener carries an explicit `{id:…}`, the closer must
still carry the matching `{id}` (diagnostic `Calor0101` if they
diverge). Both forms parse and are valid side-by-side:

```calor
# Compact (recommended for new code)
§M{Calculator}

# Legacy (still accepted)
§M{m_01j5x7k9m2npqrstabwxyz12:Calculator}
```

Existing repositories can migrate mechanically with the
[`calor fix --drop-structural-ids`](/calor/cli/fix/) command, which
rewrites opener and closer together (and only touches values that
look like production IDs — short test IDs like `m001` are left alone):

```bash
calor fix --drop-structural-ids . --log migration.log.json
```

The migrator records every removed byte range in `migration.log.json`
and can be reverted byte-exactly with
`calor fix --drop-structural-ids --revert --log migration.log.json .`.
The opt-in lint **Calor0820** (`LegacyStructuralId`) flags any
remaining legacy closing-tag IDs and emits a `fix` patch that
points at the `calor fix` command.

---

## 3. Canonical ID Format

### 3.1 Production IDs (Required for Production Code)

Production IDs use one of two payload encodings, both valid:

```
{prefix}{payload}
```

| Form | Payload length | Encoding | Example |
|------|---------------|----------|---------|
| **Compact (v6+, default)** | 12 chars | Crockford Base32 lowercase, excludes `i/l/o/u` | `f_7k9m2npqrstv` |
| **Legacy ULID** | 26 chars | Crockford Base32 uppercase, excludes `I/L/O/U`, timestamp-prefixed | `f_01J5X7K9M2NPQRSTABWXYZ12` |

As of v0.6, `IdGenerator.Generate(IdKind)` mints the compact form by
default. The ULID form is still accepted by `IdValidator` and
`AttributeHelper` so existing repositories continue to compile
unchanged; `IdGenerator.GenerateUlid(IdKind)` remains available for
callers that need the historical format.

**Compact form properties:**
- 12 characters (vs. 26 for ULID) — saves ~9.7 tokens per ID in
  agent-facing serialisations (RFC §16.F, N=100, 95% CI 9.30–10.04)
- 60 bits of cryptographic randomness (vs. 80 for ULID); the migrator
  detects and re-mints in the statistically improbable case of
  collision when deriving compact payloads from ULIDs
- Lowercase distinguishes compact from ULID at a glance

**Legacy ULID properties:**
- 26 characters, Crockford Base32 encoding
- Timestamp-ordered for natural sorting
- 80-bit random component
- Case-insensitive, no ambiguous characters (I, L, O)

**Examples:**
```
# v6+ compact (default for new code)
f_7k9m2npqrstv     Function
m_3hj8x2bw9pkq     Module
c_z4w7n5q2gxht     Class
mt_5v8b3kxq2nhp    Method
ctor_9m4z7x2qhwjt  Constructor

# Legacy ULID (still accepted)
f_01J5X7K9M2NPQRSTABWXYZ12     Function
m_01J5X7K9M2NPQRSTABWXYZ12     Module
```

### 3.2 Test IDs (ONLY in tests/, docs/, examples/)

Test IDs are short sequential identifiers for readability in documentation and tests:

```
f001, f002, f003    Functions
m001, m002          Modules
c001, c002          Classes
```

**Location Restrictions:**
- `tests/` - Allowed
- `docs/` - Allowed
- `examples/` - Allowed
- All other paths - **FORBIDDEN** (CI will fail)

### 3.3 ID Validation Rules

| Rule | Valid | Invalid |
|------|-------|---------|
| Prefix matches kind | `f_7k9m2npqrstv` or `f_01...` for function | `m_7k9m2npqrstv` for function |
| Compact payload is 12 chars | `f_7k9m2npqrstv` | `f_7k9m` |
| ULID payload is 26 chars | `f_01J5X7K9M2NPQRSTABWXYZ12` | `f_01J5X7` |
| Crockford alphabet | `f_7k9m2npqrstv` (lower, no i/l/o/u) or `f_01J5X7K9M2NPQRSTABWXYZ12` (upper, no I/L/O/U) | `f_01I5X7...` (I invalid) |
| Test ID location | `tests/foo.calr: f001` | `src/foo.calr: f001` |

---

## 4. Preservation Rules

### 4.1 Operations That Preserve IDs

| Operation | Before | After | ID |
|-----------|--------|-------|-----|
| Rename | `§F{f_01...:OldName:pub}` | `§F{f_01...:NewName:pub}` | Same |
| Change visibility | `§F{f_01...:Calc:pub}` | `§F{f_01...:Calc:pri}` | Same |
| Move file | `src/old.calr` | `src/new.calr` | Same |
| Reformat | Any formatting | Any formatting | Same |
| Add/remove comments | Any comments | Any comments | Same |
| Change implementation | Body changes | Body changes | Same |

### 4.2 Operations That Require New IDs

| Operation | Original ID | New Entity |
|-----------|-------------|------------|
| Extract helper | Keeps original | New ID for helper |
| Copy code | N/A | New ID required |
| Split function | Original keeps ID | New parts get new IDs |
| Clone class | N/A | New ID required |

### 4.3 Anti-Pattern: ID Reuse

**NEVER** reuse an ID for a different semantic entity:

```calor
// WRONG: Reusing ID for different function
§F{f_01ABC:Calculate:pub}
  §O{i32}
  §R 42

// Later in same file
§F{f_01ABC:Validate:pub}   // ERROR: Duplicate ID
  §O{bool}
  §R true
```

---

## 5. Collision Handling

### 5.1 Duplicate Detection

The compiler detects duplicate IDs and reports them as errors:

```
Error Calor0803: Duplicate ID 'f_01J5X7K9M2NPQRSTABWXYZ12'
  First defined: src/math.calr:15 (function Calculate)
  Also defined:  src/utils.calr:42 (function Validate)
```

### 5.2 Resolution Strategy

When duplicates are detected:

1. **First occurrence** - Keeps the ID
2. **Subsequent occurrences** - Must be assigned new IDs

Use `calor ids assign --fix-duplicates` to automatically resolve.

### 5.3 Merge Conflicts

ULID's 80-bit random component makes collisions statistically impossible:
- Probability of collision: ~10^-24 per ID pair
- Safe for parallel development across branches

---

## 6. Generator Contract

Code generators (converters, agents, transpilers) MUST follow these rules:

### 6.1 Preservation Requirements

| Scenario | Generator Behavior |
|----------|-------------------|
| Unchanged entity | Preserve exact ID |
| Renamed entity | Preserve exact ID |
| Moved entity | Preserve exact ID |
| New entity | Generate new ULID |
| Copied entity | Generate new ULID |

### 6.2 Semantic Identity Matching

Generators identify "same entity" by:

1. **Same file + same name** - Match
2. **Different file + same signature** - Match only if explicit mapping
3. **Extracted code** - New entity, new ID

### 6.3 Round-Trip Stability

Converting Calor → C# → Calor must preserve all IDs:

```calor
// Original
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
```

```csharp
// Generated C#
[CalorId("f_01J5X7K9M2NPQRSTABWXYZ12")]
public static int Add(int a, int b) => a + b;
```

```calor
// Re-converted to Calor - ID preserved
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
```

---

## 7. Agent Rules

AI agents and code assistants MUST follow these rules:

### 7.1 Immutability Rule

**NEVER** modify an existing ID. IDs are immutable once assigned.

```calor
// WRONG: Agent changed ID
§F{f_01NEW:Calculate:pub}  // Changed from f_01OLD

// CORRECT: Agent preserved ID
§F{f_01OLD:Calculate:pub}  // Kept original
```

### 7.2 New Declaration Rule

**OMIT** IDs for new declarations. Let `calor ids assign` generate them.

```calor
// CORRECT: Agent creates function without ID
§F{:NewHelper:pri}
  §O{void}
  §C{Console.WriteLine!}("helper")
```

### 7.3 Copy Rule

**NEVER** copy IDs when extracting or duplicating code.

```calor
// WRONG: Agent copied ID to new function
§F{f_01OLD:ExtractedHelper:pri}  // Copied from original

// CORRECT: Agent omitted ID for new function
§F{:ExtractedHelper:pri}  // No ID, will be assigned
```

### 7.4 Verification Rule

**VERIFY** before commit:

```bash
calor ids check .
```

---

## 8. CLI Commands

### 8.1 Check IDs

```bash
calor ids check <paths>
calor ids check .
calor ids check src/
```

Exit codes:
- `0` - All IDs valid
- `1` - Issues found

Issues detected:
- Missing IDs (production code)
- Duplicate IDs
- Invalid format
- Wrong prefix for kind
- Test IDs in production code

### 8.2 Assign IDs

```bash
calor ids assign <paths>
calor ids assign . --dry-run
calor ids assign . --fix-duplicates
```

Options:
- `--dry-run` / `-n` - Show changes without modifying files
- `--fix-duplicates` - Reassign duplicate IDs (keep first occurrence)
- `--allow-test-ids` - Allow test IDs (for test files)

### 8.3 Migrate Legacy ULIDs to Compact Form

`calor fix --compact-ids` rewrites every ULID-form ID inside a
known section marker (`§M`, `§F`, `§AF`, `§L`, `§IF`, `§TR`, `§CL`,
`§IFACE`, `§MT`, `§CTOR`, `§EN`, `§EXT`, `§RTYPE`, `§PROOF`, `§ITYPE`,
`§IXER`, `§OP`, and their closers) to the v6 12-char compact form.
The mapping is deterministic (last 12 chars of the ULID payload
lowercased) and the migrator detects collisions both within and
across files, re-minting fresh compact IDs as needed.

```bash
# Migrate the whole repo
calor fix --compact-ids . --log compact.log.json

# Revert byte-exactly
calor fix --compact-ids --revert --log compact.log.json .
```

ULID-shaped strings outside section markers (comments, doc prose,
string literals) are left untouched. Re-running `calor fix
--compact-ids` on already-migrated source is a no-op.

### 8.4 Example Workflow

```bash
# Check for issues
calor ids check .

# Preview fixes
calor ids assign . --dry-run

# Apply fixes
calor ids assign .

# Verify
calor ids check .
```

---

## 9. Diagnostics

| Code | Name | Severity | Description |
|------|------|----------|-------------|
| Calor0800 | MissingId | Error | Declaration missing required ID |
| Calor0801 | InvalidIdFormat | Error | ID doesn't match ULID format |
| Calor0802 | WrongIdPrefix | Error | ID prefix doesn't match kind |
| Calor0803 | DuplicateId | Error | ID used for multiple declarations |
| Calor0804 | TestIdInProduction | Error | Test ID (f001) in production code |
| Calor0820 | LegacyStructuralId | Info (opt-in lint) | Closing tag still carries a `{id}` payload; suggests `calor fix --drop-structural-ids` |
| Calor0821 | LegacyUlidPayload | Info (opt-in lint, reserved) | ID payload is a 26-char ULID rather than the v6+ 12-char compact form; suggests `calor fix --compact-ids` |

---

## 10. Implementation Notes

### 10.1 Storage

IDs are stored in the first positional attribute (`_pos0`):

```calor
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Name:pub}
     ^^^^^^^^^^^^^^^^^^^^^^^^^
     _pos0 = ID
```

### 10.2 ID Generation

As of v0.6, IDs are generated in the **compact form** by default:
- 12 characters of unbiased Crockford Base32 lowercase (`a-z` minus
  `i/l/o/u`, plus digits)
- Drawn from `RandomNumberGenerator.Fill` and masked with `byte & 0x1F`
  (`256 % 32 == 0`, so no modulo bias)
- 60 bits of randomness — sufficient for repo-scale uniqueness; the
  compiler detects and reports any collision via `Calor0803`

The legacy ULID form is still produced by `IdGenerator.GenerateUlid`:
- First 10 chars: Timestamp (48-bit, millisecond precision)
- Last 16 chars: Randomness (80-bit, cryptographically random)
- 26 chars total

Both forms are accepted as input to the parser and validator.

### 10.3 Backward Compatibility

Existing files with test IDs (`f001`) in production code:
1. Run `calor ids assign` to upgrade
2. CI will enforce after migration period

---

## References

- ULID Specification: https://github.com/ulid/spec
- ID Implementation: `src/Calor.Compiler/Ids/`
- ID Tests: `tests/Calor.Ids.Tests/`
- Attribute Parsing: `src/Calor.Compiler/Parsing/AttributeHelper.cs`
