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
