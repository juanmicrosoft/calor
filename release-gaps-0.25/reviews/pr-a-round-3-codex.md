# PR A (#1545, #1548), Codex round 3 (last round)

Reviewing the round 2 head. Raw final message: `pr-a-round-3-codex.raw.txt`.

**Verdict: REQUEST-CHANGES** (1 MAJOR, 1 MINOR). Codex confirmed that the committed guard works and
found no new issue with the settings check or the override pin.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | An escaped key (`"environment": nuget-release`) and a flow mapping under a block job header still produced CLOSED. | Fixed. Every line is decoded (YAML `\x`/`\u`/`\U` escapes, escaped line breaks) and case-folded before the search for a possible `environment` key, in key, flow, or complex-key (`? environment`) form. Each one must be on an allowlisted declaration. A whole-file backstop catches keys split across escaped line breaks (`W010`). Workflows called from another repository fail (`W011`). New cases in Python (5) and C# (+2). |
| 2 | MINOR | Wrapping the guard in an uncalled shell function still passed. | Fixed. The guard step must equal the committed text exactly (`GUARD_LINES`). An uncalled function, an added key, or `exit 0` all fail (`W007`). New mutant added. |

Budget: this was round 3. The verification pass decides whether PR A ships.
