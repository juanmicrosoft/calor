# Calor Language Support for VS Code

Syntax highlighting for the [Calor programming language](https://github.com/juanmicrosoft/calor-2).

## Features

- Syntax highlighting for `.calr` files
- Bracket matching and auto-closing
- Code folding for modules, functions, loops, and conditionals
- Comment toggling with `//`

## Supported Syntax Elements

### Control Structures
- Module declarations: `§M{id:name}`, `§/M{id}`
- Function declarations: `§F{id:name:visibility}`, `§/F{id}`
- Loops: `§L{id:var:from:to:step}`, `§/L{id}`
- Conditionals: `§IF{id}`, `§EI`, `§EL`, `§/I{id}`
- Function calls: `§C{target}`, `§/C`

### Type System
- Input/Output: `§I{type:name}`, `§O{type}`
- Effects: `§E{effects}`
- Bindings: `§B{name}`
- Options: `§SOME`, `§NONE{type=T}`
- Results: `§OK`, `§ERR`

### Contracts
- Preconditions: `§Q`, `§Q{message="..."}`
- Postconditions: `§S`

### Other
- Arguments: `§A`
- Return: `§R`
- Print: `§P`
- Arrow operator: `→` or `->`

### Primitives
- Types: `i32`, `i64`, `str`, `bool`, `void`
- Visibility: `pub`, `pri`, `prot`
- Booleans: `true`, `false`
- Effect codes: `cw`, `cr`, `fw`, `fr`, `net`, `db`

## Example

```calor
§M{m001:FizzBuzz}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §L{for1:i:1:100:1}
    §IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

## Installation

### From VSIX

1. Download the `.vsix` file from the releases
2. In VS Code, open the Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`)
3. Run "Extensions: Install from VSIX..."
4. Select the downloaded file

### From Source

1. Clone the repository
2. Copy the `editors/vscode` folder to your VS Code extensions directory:
   - Windows: `%USERPROFILE%\.vscode\extensions\calor`
   - macOS: `~/.vscode/extensions/calor`
   - Linux: `~/.vscode/extensions/calor`
3. Restart VS Code

## Building VSIX

```bash
npm install -g @vscode/vsce
cd editors/vscode
vsce package
```

## License

Apache-2.0 - See [LICENSE](https://github.com/juanmicrosoft/calor-2/blob/main/LICENSE)
