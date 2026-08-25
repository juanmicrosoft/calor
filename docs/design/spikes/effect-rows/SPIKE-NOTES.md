# Effect-rows emitter spike — running notes

Roadmap §4.1 term 1; the specification is `docs/design/effect-rows-in-the-type-system.md`
(Draft v4, §12). These notes are written **as the spike runs**, so an interrupted run
loses only the step in progress. Nothing here is a conclusion until it carries an
executed command.

Branch: `spike/effect-rows-emitter`, cut from the design-doc branch
(`docs/effect-rows-design-draft-v1`, tip `5d99f900`).

## Step log

- **S0** branch cut; Z3 assets downloaded; `dotnet build src/Calor.Compiler` green;
  `bench/corpus/MediatR` submodule initialised at the pinned SHA
  `fb309026775ef953a64fb5339d074426c1ad2c37` (A2 lives there and `git clone` does not
  init it — CLAUDE.md).
- **S1** prototype, part 1 — syntax. `Diagnostic.cs` gains Calor0404/0405/0424/0425;
  `Effects/EffectSet.cs` gains `RowFit` + `EffectRow` (the three-point lattice, the
  nine-cell `fits`, the join, `ExtraEffects`, `ToDisplayString`) — **deliberately in an
  existing file** rather than §9's planned `Effects/EffectRow.cs`, so the spike adds
  **zero new files under `src/`** and needs no Calor-first allowlist-precursor PR;
  `ParameterNode.Row` / `ClassFieldNode.Row`; the `eff` branch in
  `ParseOptionalTypeParameterList` with its one-token lookahead (Z4's `<eff>` guard) and
  §7.2(c)'s taxonomy-collision ban; the line-adjacency row at **position 5** (inline
  parameter) and **position 8** (field), each with the Calor0405 parser-stage recovery.
  Effect variables are recorded as **binder indices**, never as names — that is what
  makes the interface/implementation comparison alpha-equivalent (§7.3, W1c).
  `GetDeclaredEffects` skips the reserved key, so a variable never leaks into a concrete
  `EffectSet`. Build green, 0 warnings.

