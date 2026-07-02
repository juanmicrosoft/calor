# N1-003 — CSV Row Encoding (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Encoding rules (RFC-4180 style)

A *row* is an ordered list of string fields serialized to a single line:

- Fields are separated by commas.
- A field **needs quoting** when it contains a comma, a double-quote (`"`), a
  carriage return, or a line feed.
- A quoted field is wrapped in double-quotes, and every double-quote inside
  the field is **doubled** (`"` becomes `""`).
- A field that does not need quoting is serialized verbatim. The empty field
  serializes as the empty string (unquoted).

## Existing behavior (already implemented in the starting fixture)

`NeedsQuoting(field)` — returns whether `field` needs quoting per the rules
above.

## Task: implement the missing operations

1. `EscapeField(field)` → string — serializes one field: quoted (with
   internal quotes doubled) when `NeedsQuoting(field)` holds, verbatim
   otherwise.
2. `JoinRow(fields)` → string — serializes the list of fields, escaping each
   and joining with commas. An empty list serializes to the empty string.
3. `SplitRow(line)` → list of strings — parses one serialized row back into
   its fields. The input is always a well-formed row (an output of
   `JoinRow`). Rules:
   - The empty line parses to a single empty field.
   - Unquoted fields end at the next comma or end of line.
   - A field beginning with a double-quote is quoted: it ends at the next
     un-doubled double-quote; a doubled double-quote inside contributes one
     literal double-quote; commas (and CR/LF) inside the quotes are field
     content, not separators.
   - A trailing comma means the row ends with an empty field.

`SplitRow(JoinRow(fields))` must reproduce `fields` exactly for every list of
fields.

## Constraints

- Pure computation only: no I/O, no global state.
- Do not change `NeedsQuoting`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `NeedsQuoting(string) → bool`, `EscapeField(string) → string`,
`JoinRow(List<string>) → string`, `SplitRow(string) → List<string>`, reachable
through the arm's `TestShim.cs` (provided by the harness; not editable by the
agent).
