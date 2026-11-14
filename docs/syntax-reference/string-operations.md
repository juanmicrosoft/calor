---
layout: default
title: "String Operations"
parent: Syntax Reference
nav_order: 11
---

# String Operations

<!-- Generated from website/content/syntax-reference/string-operations.mdx by calor self-check docs --fix. Edit the source, not this file. -->

Use ordinary .NET string members, postfix calls and indexing. Calor strings use
the same UTF-16 representation as C# strings; `Length` counts code units, not
displayed characters.

<a id="basic-string-operations"></a>

## Core operations

| Operation | Calor call syntax |
|:----------|:-------------|
| Length | `text.Length` |
| Case | `text.ToUpperInvariant()`, `text.ToLowerInvariant()` |
| Trim | `text.Trim()` |
| Search | `text.Contains(part, System.StringComparison.Ordinal)` |
| Prefix and suffix | `text.StartsWith(prefix, System.StringComparison.Ordinal)`, `text.EndsWith(suffix, System.StringComparison.Ordinal)` |
| Index | `text.IndexOf(part, System.StringComparison.Ordinal)` |
| Index from a position | `text.IndexOf(part, start, System.StringComparison.Ordinal)` |
| Index in a segment | `text.IndexOf(part, start, count, System.StringComparison.Ordinal)` |
| Slice | `text.Substring(start, length)` |
| Replace | `text.Replace(oldValue, newValue)` |
| Concatenate | `left + right` |
| Equality | `System.String.Equals(left, right, System.StringComparison.Ordinal)` |
| Character access | `text[index]` |

```calor
module StringBasics
  func Main () → (int, string, char, string)
    !
    let string:text = "hello world"
    return (text.Length, text.ToUpperInvariant(), text[1], text + "!")
```

`Main()` returns `(11, "HELLO WORLD", 'e', "hello world!")`. Strings are immutable:
methods such as `Trim` and `Replace` return a value rather than changing `text`.
Arguments and overloads are checked as ordinary .NET calls. There is no separate
prefix-string-operation syntax or operand-count convention.

Valid call syntax does not grant an effect contract. The current checker still
reports unknown effects for `Trim`, `Substring`, `Replace` and several comparison
overloads. `Calor0411` identifies the unknown call; `Calor0410` rejects its use in
a pure function. Do not disable effect checking to claim native acceptance.

## Comparison modes

Pass a `System.StringComparison` value when the comparison must not depend on
the current culture.

| Value | Meaning |
|:------|:--------|
| `Ordinal` | Compare without linguistic culture rules |
| `OrdinalIgnoreCase` | Ordinal comparison, ignoring case |
| `InvariantCulture` | Use invariant linguistic culture rules |
| `InvariantCultureIgnoreCase` | Invariant linguistic comparison, ignoring case |

The overloads without a comparison argument do not all behave alike.
`Contains(string)` and instance `Equals(string)` are ordinal; `StartsWith(string)`,
`EndsWith(string)` and `IndexOf(string)` use the current culture.
`ToUpperInvariant` and `ToLowerInvariant` also avoid the current culture.

The comparison arguments select ordinal runtime behavior. The built-in effect
manifest records every `System.String` method as pure, so the function can
declare `!`:

```calor
module StringComparisons
  func Main () → string
    !
    let string:text = "hello world"
    return "${text.Contains("WORLD", System.StringComparison.OrdinalIgnoreCase)}:${text.StartsWith("hello", System.StringComparison.Ordinal)}:${text.IndexOf("world", System.StringComparison.Ordinal)}:${System.String.Equals("I", "i", System.StringComparison.OrdinalIgnoreCase)}"
```

`Main()` returns `True:True:6:True`.

Choosing `Ordinal` defines runtime behavior; it does not by itself establish a
verification proof. Read the verifier's actual outcome. Only an assumption-free,
non-vacuous `proven` postcondition is eligible for proof-based guard removal.
Inconclusive outcomes do not authorize removal. Runtime checks require an enabled
contract mode and supported lowering. See
[verification guarantees](https://calor.dev/docs/guides/verification-guarantees/).

<a id="regex-operations"></a>
<a id="character-operations"></a>
<a id="stringbuilder-operations"></a>

## Regex, characters and StringBuilder

Use `System.Text.RegularExpressions.Regex` for regular expressions, `char`
methods for character classification and `System.Text.StringBuilder` for a
mutable buffer. Backslashes in a regex pattern must also be escaped in the
Calor string: regex `\d` is written as `"\\d"`.

```calor
module TextTools
  func Main () → string
    ! alloc, mut
    let bool:digits = System.Text.RegularExpressions.Regex.IsMatch("42", "^[0-9]+$")
    let char:letter = "Cat"[0]
    let System.Text.StringBuilder:builder = new System.Text.StringBuilder()
    builder.Append(letter)
    builder.Append("at")
    return "${digits}:${char.IsDigit('4')}:${builder.ToString()}"
```

`Main()` returns `True:True:Cat`. The effect row declares allocation (`alloc`)
and mutation (`mut`) for the buffer.

| Purpose | Ordinary .NET operation |
|:--------|:------------------------|
| First regex match | `Regex.Match(text, pattern)`; inspect the match's `Success` and `Value` |
| Regex replacement | `Regex.Replace(text, pattern, replacement)` |
| Regex split | `Regex.Split(text, pattern)` |
| Character code | `(int)letter` |
| Character from code | `(char)code` |
| Classification | `char.IsLetter(c)`, `char.IsWhiteSpace(c)`, `char.IsUpper(c)`, `char.IsLower(c)` |
| Character case | `char.ToUpperInvariant(c)`, `char.ToLowerInvariant(c)` |
| Buffer append | `builder.Append(value)`, `builder.AppendLine(value)` |
| Buffer edits | `builder.Insert(index, value)`, `builder.Remove(start, count)`, `builder.Clear()` |
| Buffer size and result | `builder.Length`, `builder.ToString()` |

These are API calls, not special expression operators. Preserve the actual
receiver type, overload, arguments and effects when composing them.
`Append` returns the same mutable builder; chaining does not turn mutation
into an immutable string operation.

## Error handling

`Substring` and indexing throw for invalid bounds. Validate `start >= 0`,
`count >= 0`, and `start <= text.Length - count` before slicing when those
values come from untrusted input. Guard nullable receivers before reading
members; `text != null && text.Length > 0` short circuits, while a non-null
annotation alone is not a runtime check on .NET callers.

Regex patterns can be invalid or expensive. Use a valid pattern and an
appropriate timeout for untrusted text; an effect row is not a complexity
bound. In contracts, use only supported runtime lowering and read the actual
verifier status rather than assuming every string or regex operation is
modeled.

## Interpolated strings

A `${...}` hole inside a string holds an expression. It can include alignment
(`,10`) and a format specifier (`:F2`), as in C#. Holes are evaluated from left
to right. Formatting normally uses the current .NET culture.

```calor
module StringHoles
  func Greet (string:name) → string
    !
    return "hello ${name}:\${x}"

  func Main () → string
    !
    return Greet("Calor")
```

`Main()` returns `hello Calor:${x}`. String-valued holes do not require numeric
formatting.

A numeric hole is charged the effects of its type's `ToString` contract. The
built-in manifest records `double.ToString` as pure, so `Line` can declare `!`.
That is an approximation: formatting reads the current culture, and the effect
system does not track culture. A type whose `ToString` has no contract is charged
`unknown`.

```calor
module Receipt
  func Line (string:item, double:amount) → string
    !
    return "${item,-8}|${amount,10:F2}|${amount * 2:F1}"

  func Main () → string
    !
    return Line("coffee", 3.5) + ":\${x}"
```

At the CLR runtime, under the invariant or `en-US` culture, the corresponding
interpolation produces
`coffee  |      3.50|7.0:${x}`. Under `de-DE`, it returns
`coffee  |      3,50|7,0:${x}`. For culture-independent numeric text, pass
`System.Globalization.CultureInfo.InvariantCulture` to an appropriate numeric
`ToString` overload.

Escape the dollar sign to write a literal hole: `"\${x}"` contains the four
characters `${x}` and does not read a variable named `x`.

### Holes are ordinary expressions

Names, calls, indexing and operators in a hole use normal binding and effect
checking. Named arguments are ordinary call syntax, not an instruction to treat
the hole as raw C#. An undefined name still reports `Calor0200` at its source
location:

```calor expect=Calor0200
module MissingInterpolationName
  func Main () → string
    !
    return "hello ${missing}"
```

Calls must also fit the enclosing function's effect row. For example,
`System.Console.ReadLine()` needs `cr`, the console-read effect, even inside a
hole.

<a id="c-migration"></a>

<a id="converting-c-interpolated-strings"></a>

## Converting C# interpolation

The C# target type can change overload selection. The converter uses its
semantic context to retain that choice.

| C# target | Conversion behavior |
|:----------|:--------------------|
| `string` or `object` | Native interpolation when the expression is representable |
| `FormattableString` or `IFormattable`, with holes | An explicit target-type cast retains the selected overload |
| A custom interpolated-string handler | Preserve the enclosing C# statement or member; report `string-interpolation-handler` |
| `FormattableString` with no holes | Preserve the enclosing C# boundary because a plain Calor string cannot express that conversion |

Unsupported holes and special raw/verbatim format clauses can also require
preservation. Converter-owned C# payloads are not native Calor expressions and
do not establish native effect or verification acceptance. With fallback
disabled, an unsupported conversion fails instead of producing that payload.

`calor feature-check string-interpolation` reports **Partial**.
Inspect the conversion
report and supply the original references and parse context; a missing target
type is not evidence that the original overload choice was recovered.
