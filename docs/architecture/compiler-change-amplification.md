# Compiler change-amplification controls

Calor evolves compiler infrastructure incrementally rather than by rewriting the
parser, migration pipeline, or emitters in one flag day. The following contracts
make structural omissions fail mechanically.

## AST schema and visitors

`eng/ast-schema.json` is the source of truth for every concrete AST node and its
feature-oriented source file. Run:

```bash
python3 scripts/generate_ast_infrastructure.py
```

The generator updates the marked region in `Ast/AstNode.cs` and removes
per-node visitor dispatch boilerplate. It generates both visitor interfaces,
centralized exhaustive dispatch, and `AstSchemaMetadata`. Architecture tests
cross-check the schema against every concrete node, both visitor interfaces,
the generated dispatch surface, and every structural child property exposed by
`RecursiveAstWalker`.

Adding an AST node therefore requires:

1. Add the node to its feature file.
2. Declare it in `eng/ast-schema.json`.
3. Regenerate the infrastructure.
4. Implement feature-specific behavior in compiler visitors.

There is no per-node `Accept` plumbing to copy, and a missing schema or visitor
behavior fails generation, compilation, or architecture tests.

## Aggregate transformations

Transforms must use `ModuleNode.With(...)`, whose update object mirrors every
aggregate field and preserves base AST metadata. Architecture tests compare the
update surface with `ModuleNode` and restrict direct constructor calls to parser
and migration creation boundaries. A new module field cannot be omitted
silently by an existing transform.

## Emission state

`CSharpEmitter.Emit` and `CalorEmitter.Emit` reset all per-emission state.
Architecture regression coverage emits two modules through one instance and
requires the second result to equal output from a fresh emitter.

## Component ownership

`eng/compiler-components.json` declares ownership and permitted dependencies for
the parser, AST, binder, analysis, migration, code generation, verification, and
ID components. An architecture test derives actual source dependencies and
fails on undeclared edges or stale declarations. Existing legacy cycles are
explicit rather than hidden; future decomposition can remove edges from the
contract one at a time without allowing new coupling.

Package versions remain centralized in `Directory.Packages.props`, while
nullable, language version, warnings-as-errors, and analyzer settings are
centralized in `Directory.Build.props`.
