---
layout: default
title: String Operations
parent: Syntax Reference
nav_order: 7
---

# String Operations

Calor provides native string manipulation operations that compile directly to efficient C# code. These operations follow the same prefix notation as other expressions.

---

## Basic String Operations

| Operation | Syntax | C# Equivalent | Description |
|:----------|:-------|:--------------|:------------|
| Length | `(len s)` | `s.Length` | Get string length |
| Upper | `(upper s)` | `s.ToUpper()` | Convert to uppercase |
| Lower | `(lower s)` | `s.ToLower()` | Convert to lowercase |
| Trim | `(trim s)` | `s.Trim()` | Remove leading/trailing whitespace |
| Contains | `(contains s sub)` | `s.Contains(sub)` | Check if string contains substring |
| Starts | `(starts s prefix)` | `s.StartsWith(prefix)` | Check if string starts with prefix |
| Ends | `(ends s suffix)` | `s.EndsWith(suffix)` | Check if string ends with suffix |
| IndexOf | `(indexof s sub)` | `s.IndexOf(sub)` | Find index of substring (-1 if not found) |
| Substring | `(substr s start len)` | `s.Substring(start, len)` | Extract substring |
| Replace | `(replace s old new)` | `s.Replace(old, new)` | Replace all occurrences |
| Concat | `(concat a b)` | `a + b` | Concatenate strings |
| Equals | `(equals a b)` | `a.Equals(b)` | String equality check |

### Examples

```
// Get string length
§B{length} (len "hello")        // 5

// Convert case
§B{upper} (upper "hello")       // "HELLO"
§B{lower} (lower "WORLD")       // "world"

// Trim whitespace
§B{trimmed} (trim "  hello  ")  // "hello"

// Check contents
§B{hasWorld} (contains "hello world" "world")  // true
§B{startsH} (starts "hello" "he")              // true
§B{endsO} (ends "hello" "lo")                  // true

// Find and extract
§B{idx} (indexof "hello" "ll")  // 2
§B{sub} (substr "hello" 1 3)    // "ell"

// Transform
§B{replaced} (replace "hello" "l" "L")  // "heLLo"
§B{full} (concat "hello" " world")      // "hello world"
```

---

## String Comparison Modes

String operations that compare text support optional comparison modes via keyword arguments:

| Mode | Keyword | C# Equivalent |
|:-----|:--------|:--------------|
| Ordinal | `:ordinal` | `StringComparison.Ordinal` |
| Ignore Case | `:ignore-case` | `StringComparison.OrdinalIgnoreCase` |
| Invariant | `:invariant` | `StringComparison.InvariantCulture` |
| Invariant Ignore Case | `:invariant-ignore-case` | `StringComparison.InvariantCultureIgnoreCase` |

### Supported Operations

- `contains`, `starts`, `ends`, `indexof`, `equals`

### Examples

```
// Case-insensitive contains
§B{found} (contains "Hello World" "HELLO" :ignore-case)  // true

// Case-insensitive equals
§B{same} (equals "YES" "yes" :ignore-case)  // true

// Ordinal comparison (exact match)
§B{exact} (contains "Hello" "hello" :ordinal)  // false

// Case-insensitive startsWith
§B{starts} (starts "Hello World" "HELLO" :ignore-case)  // true

// Find index ignoring case
§B{idx} (indexof "Hello World" "WORLD" :ignore-case)  // 6
```

---

## Regex Operations

Calor provides native regular expression support via the `regex-*` operations.

| Operation | Syntax | C# Equivalent | Description |
|:----------|:-------|:--------------|:------------|
| Test | `(regex-test s pattern)` | `Regex.IsMatch(s, pattern)` | Test if pattern matches |
| Match | `(regex-match s pattern)` | `Regex.Match(s, pattern)` | Get first match |
| Replace | `(regex-replace s pattern replacement)` | `Regex.Replace(s, pattern, replacement)` | Replace matches |
| Split | `(regex-split s pattern)` | `Regex.Split(s, pattern)` | Split by pattern |

### Pattern Escaping

In Calor strings, backslashes must be escaped. For regex patterns:
- `\d` (digit) becomes `"\\d"` in Calor
- `\s` (whitespace) becomes `"\\s"` in Calor
- `\w` (word char) becomes `"\\w"` in Calor

### Examples

```
// Test if string contains digits
§B{hasDigits} (regex-test "hello123" "\\d+")  // true

// Test email format (simplified)
§B{isEmail} (regex-test email "^[^@]+@[^@]+$")

// Replace whitespace with dashes
§B{slugified} (regex-replace "hello world" "\\s+" "-")  // "hello-world"

// Replace all digits with X
§B{masked} (regex-replace "abc123xyz" "\\d" "X")  // "abcXXXxyz"

// Split by comma
§B{parts} (regex-split "a,b,c" ",")  // ["a", "b", "c"]

// Split by any whitespace
§B{words} (regex-split "hello   world\ttab" "\\s+")
```

---

## Character Operations

Calor provides operations for working with individual characters.

| Operation | Syntax | C# Equivalent | Description |
|:----------|:-------|:--------------|:------------|
| Char At | `(char-at s index)` | `s[index]` | Get character at index |
| Char Code | `(char-code c)` | `(int)c` | Get Unicode code point |
| Char From Code | `(char-from-code n)` | `(char)n` | Create char from code point |
| Is Letter | `(is-letter c)` | `char.IsLetter(c)` | Check if letter |
| Is Digit | `(is-digit c)` | `char.IsDigit(c)` | Check if digit |
| Is Whitespace | `(is-whitespace c)` | `char.IsWhiteSpace(c)` | Check if whitespace |
| Is Upper | `(is-upper c)` | `char.IsUpper(c)` | Check if uppercase |
| Is Lower | `(is-lower c)` | `char.IsLower(c)` | Check if lowercase |
| Char Upper | `(char-upper c)` | `char.ToUpper(c)` | Convert to uppercase |
| Char Lower | `(char-lower c)` | `char.ToLower(c)` | Convert to lowercase |

### Examples

```
// Get first character
§B{first} (char-at "hello" 0)  // 'h'

// Check character properties
§B{isLetter} (is-letter 'A')      // true
§B{isDigit} (is-digit '5')        // true
§B{isSpace} (is-whitespace ' ')   // true
§B{isUpper} (is-upper 'A')        // true
§B{isLower} (is-lower 'a')        // true

// Convert character case
§B{upper} (char-upper 'a')  // 'A'
§B{lower} (char-lower 'Z')  // 'z'

// Character code conversions
§B{code} (char-code 'A')       // 65
§B{char} (char-from-code 65)   // 'A'

// Check if first char is uppercase letter
§B{startsWithUpper} (&& (is-letter (char-at s 0)) (is-upper (char-at s 0)))
```

---

## StringBuilder Operations

For building strings efficiently, Calor provides StringBuilder operations that compile to `System.Text.StringBuilder`.

| Operation | Syntax | C# Equivalent | Description |
|:----------|:-------|:--------------|:------------|
| New | `(sb-new)` | `new StringBuilder()` | Create empty builder |
| New with init | `(sb-new "text")` | `new StringBuilder("text")` | Create with initial value |
| Append | `(sb-append sb text)` | `sb.Append(text)` | Append text |
| Append Line | `(sb-appendline sb text)` | `sb.AppendLine(text)` | Append text with newline |
| Insert | `(sb-insert sb index text)` | `sb.Insert(index, text)` | Insert at position |
| Remove | `(sb-remove sb start length)` | `sb.Remove(start, length)` | Remove characters |
| Clear | `(sb-clear sb)` | `sb.Clear()` | Clear all content |
| To String | `(sb-tostring sb)` | `sb.ToString()` | Get final string |
| Length | `(sb-length sb)` | `sb.Length` | Get current length |

### Functional Chaining Pattern

StringBuilder operations return the builder, enabling functional chaining:

```
// Build a string with multiple appends
§B{result} (sb-tostring
             (sb-append
               (sb-append
                 (sb-new)
                 "Hello")
               " World"))
// Result: "Hello World"
```

### Examples

```
// Simple string building
§B{sb1} (sb-new)
§B{sb2} (sb-append sb1 "Hello")
§B{sb3} (sb-append sb2 " World")
§B{result} (sb-tostring sb3)  // "Hello World"

// Initialize with value
§B{sb} (sb-new "Start: ")
§B{sb} (sb-append sb "End")
§B{result} (sb-tostring sb)  // "Start: End"

// Build with newlines
§B{sb1} (sb-new)
§B{sb2} (sb-appendline sb1 "Line 1")
§B{sb3} (sb-appendline sb2 "Line 2")
§B{multiline} (sb-tostring sb3)

// Insert and remove
§B{sb} (sb-append (sb-new) "HelloWorld")
§B{sb} (sb-insert sb 5 " ")          // "Hello World"
§B{sb} (sb-remove sb 5 1)            // "HelloWorld"

// Get length
§B{len} (sb-length (sb-append (sb-new) "test"))  // 4

// Clear and reuse
§B{sb} (sb-append (sb-new) "temporary")
§B{sb} (sb-clear sb)
§B{len} (sb-length sb)  // 0
```

---

## Composing Operations

String operations can be freely composed with each other and with other Calor expressions.

### String + Char Operations

```
// Check if first character is a letter
§B{firstIsLetter} (is-letter (char-at name 0))

// Get uppercase first character
§B{firstUpper} (char-upper (char-at name 0))

// Capitalize: uppercase first char + lowercase rest
§B{sb1} (sb-new)
§B{sb2} (sb-append sb1 (str (char-upper (char-at s 0))))
§B{sb3} (sb-append sb2 (lower (substr s 1)))
§B{capitalized} (sb-tostring sb3)
```

### String + Regex Operations

```
// Check if trimmed input has digits
§B{hasDigits} (regex-test (trim input) "\\d")

// Normalize whitespace then check pattern
§B{normalized} (regex-replace (trim s) "\\s+" " ")
§B{isValid} (regex-test normalized "^[a-z ]+$")
```

### In Conditions

```
// Validate input
§IF{v1} (== (len input) 0)
  §R "Input required"
§EI (! (regex-test input "^[a-zA-Z]+$"))
  §R "Letters only"
§EL
  §R (concat "Valid: " input)
§/I{v1}
```

### In Contracts

```
§F{f001:ProcessName:pub}
  §I{string:name}
  §O{string}
  §Q (> (len name) 0)                    // Requires: non-empty
  §Q (is-letter (char-at name 0))        // Requires: starts with letter
  §S (== (len result) (len name))        // Ensures: same length
  §R (upper name)
§/F{f001}
```

---

## Error Handling

String operations can throw exceptions at runtime:

| Error | Cause | Example |
|:------|:------|:--------|
| `IndexOutOfRangeException` | Invalid index in `char-at` | `(char-at "hi" 10)` |
| `ArgumentOutOfRangeException` | Invalid range in `substr`, `sb-remove` | `(substr "hi" 10 5)` |
| `RegexParseException` | Invalid regex pattern | `(regex-test s "[invalid")` |

### Safe Patterns

```
// Check length before accessing
§IF{safe} (> (len s) 0)
  §B{first} (char-at s 0)
§/I{safe}

// Check index before substring
§IF{valid} (&& (>= start 0) (<= (+ start len) (len s)))
  §B{sub} (substr s start len)
§/I{valid}
```

---

## C# Migration

When converting C# code to Calor, string operations are automatically recognized:

| C# | Calor |
|:---|:------|
| `s.ToUpper()` | `(upper s)` |
| `s.ToLower()` | `(lower s)` |
| `s.Trim()` | `(trim s)` |
| `s.Contains("x")` | `(contains s "x")` |
| `s.Contains("x", StringComparison.OrdinalIgnoreCase)` | `(contains s "x" :ignore-case)` |
| `s[i]` | `(char-at s i)` |
| `char.IsLetter(c)` | `(is-letter c)` |
| `(int)c` | `(char-code c)` |
| `(char)n` | `(char-from-code n)` |
| `Regex.IsMatch(s, p)` | `(regex-test s p)` |
| `Regex.Replace(s, p, r)` | `(regex-replace s p r)` |
| `new StringBuilder()` | `(sb-new)` |
| `sb.Append(x)` | `(sb-append sb x)` |
| `sb.ToString()` | `(sb-tostring sb)` |

---

## Next

- [Control Flow](/calor/syntax-reference/control-flow/) - Loops and conditionals
- [Contracts](/calor/syntax-reference/contracts/) - Pre/post conditions
