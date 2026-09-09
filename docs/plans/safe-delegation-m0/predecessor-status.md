# Safe delegation M0 predecessor status

**Recorded:** 2026-09-09. **Issue:** #1279. **Status:** administrative
predecessor accounting; no experiment, feasibility verdict, or independent
methods approval.

## Released baseline

Calor [v0.18.0](https://github.com/juanmicrosoft/calor/tree/514f538024df990af86054af25975b756ba42ab1)
and v0.19.0 shipped. In v0.18.0, the row-escape table passed
12 of 12 registered shapes and release-path gate 15 passed on 21 consecutive
post-split compiler-shard runs with zero exit-143 kills and zero retries. The
process split removed that observed kill symptom from the measured window, but
it did not identify or repair the underlying allocation: a single process
could still exhaust a 16 GB Linux machine.

The v0.19.0 baseline is commit
`74e55ce4861f9548199629ab6e18623ef1759f21`, released through #1277.
The [v0.19 audit disposition](../v0.19-audit-disposition.md) accounts for all
31 children of the language and website audits and records the limits retained
after their individual fixes.

That disposition was checked against its two issue groups:

| Audit | Child issues | Recorded rows | Status |
|---|---:|---:|---|
| Language audit #1182 | #1183-#1198 | 16 | Closed with separate merged PR references |
| Website audit #1202 | #1203-#1217 | 15 | Closed with separate merged PR references |
| **Total** |  | **31** | Complete administrative accounting |

The source document was reviewed row by row, not accepted from its count alone.
The issue and PR references resolve for all 31 rows. The reconciliation retains
the following qualifications rather than widening the recorded outcomes:

- Language fixes cover their tested constructs and production paths, not every
  composition. Unsupported migration forms remain explicit rejection or C#
  preservation, and optional Tier 2 inherited corpus defects remain open.
- Combined expression acceptance changed from the isolated boundary result;
  neither denominator may be substituted for the other.
- The conditional-access runtime fixture disables effect enforcement, so its
  result is not evidence of default-CLI effect verification.
- Website results are scoped to the reviewed routes and instruments. Local
  source execution, targeted accessibility checks, media budgets, and grammar
  highlighting are not field-performance, installed-package, site-wide, or
  universal syntax evidence.
- The legacy benchmark source pairs and uncertainty interpretations were found
  non-equivalent or invalid. Static composites are not agent-value evidence.
- No row establishes whole-language soundness, complete cross-file effect
  checking, or production adoption approval.

The immutable reviewed version is the
[v0.19 disposition at the v0.20 planning commit](https://github.com/juanmicrosoft/calor/blob/fc55e3cf065560f49133beb794cd63403bceabf7/docs/plans/v0.19-audit-disposition.md).

### Row-specific reconciliation

| Issue / merged PR | Accepted resolution | Retained boundary |
|---|---|---|
| #1183 / [#1230](https://github.com/juanmicrosoft/calor/commit/4444696d8e352c750d332341c10ecc94c8cb42c7) | Parameter post-state proof handling | Runtime guards remain; covered mutation paths are not whole-program alias analysis |
| #1184 / [#1237](https://github.com/juanmicrosoft/calor/commit/1389fd96eb49305364211079f8b4a095ad4c86c7) | Numeric simplification fixes and refusals | Unsupported executable semantics must still be refused or demoted |
| #1185 / [#1247](https://github.com/juanmicrosoft/calor/commit/a02b4e400d6f6969321e39f522a055d1e44491c1) | Indexed writes and assignment targets charged | This is tested target coverage, not complete cross-file effect inference |
| #1186 / [#1248](https://github.com/juanmicrosoft/calor/commit/743723cd83e6fa1690ee59c5c2de820a6dc777bd) | Covered nested/interface inheritance paths | Optional Tier 2 inherited corpus defects in #1241 remain open |
| #1187 / [#1249](https://github.com/juanmicrosoft/calor/commit/3e3d91dc707c4f04e8b18198596f85f4d931dfaa) | Expression and compound-pattern grouping | Evidence is limited to the represented grouping families |
| #1188 / [#1250](https://github.com/juanmicrosoft/calor/commit/06986f865b4861716f1294f0997d661206aa8605) | Statement-losing expression-match forms rejected | Rejection is not native support for those forms |
| #1189 / [#1251](https://github.com/juanmicrosoft/calor/commit/bc2ac802eb51eb771f6d6f29e99da6fe3cf1f690) | Production overflow policy enforced | Claims remain tied to the documented checked/unchecked policy |
| #1190 / [#1253](https://github.com/juanmicrosoft/calor/commit/639fa93a1a240ab16293b12aadc9a080c7fc8164) | Match sibling scopes separated | Scope tests do not establish all pattern-lowering interactions |
| #1191 / [#1252](https://github.com/juanmicrosoft/calor/commit/a90b65ebecfcc6d9fee6980e663f8369e2e3b7bf) | Conditional/eager ordering corrected | 20/180 isolated boundaries and 11/268 integrated results remain separate; unsupported forms are preserved |
| #1192 / [#1269](https://github.com/juanmicrosoft/calor/commit/28c01bc0b74c3a0058b267fcbd69fc4eb3ecb843) | Tuple RHS evaluated before supported writes | Unsupported tuple targets remain interop rather than native lowering |
| #1193 / [#1268](https://github.com/juanmicrosoft/calor/commit/48d5cba4072186e430690bea8a345a5c801d53cd) | Dictionary operation/type semantics preserved | Fallback cases remain reported preservation, not native support |
| #1194 / [#1270](https://github.com/juanmicrosoft/calor/commit/6d4815aba2e10504cbddee74d69f662c1d03dbae) | Loop-condition reevaluation and continue semantics preserved | Only supported lowering shapes are claimed |
| #1195 / [#1272](https://github.com/juanmicrosoft/calor/commit/7f000ffdc96058ce1caa5d74feadde455f8f2501) | LINQ selectors and deferral preserved | Unsupported query forms remain explicit fallback/preservation |
| #1196 / [#1275](https://github.com/juanmicrosoft/calor/commit/0b54fe0bea602984d483a97525bb4001f87a553d) | Production value/exception/output/state/trace oracles added | These oracles cover named fixtures, not every compiler composition |
| #1197 / [#1228](https://github.com/juanmicrosoft/calor/commit/27b63c7b2f13d9d8f9e2798c3aeeba1298e46331) | Coverage floors and native mutation targets added | Floors are measured gates, not semantic completeness |
| #1198 / [#1263](https://github.com/juanmicrosoft/calor/commit/882d7cb4729249f781eab1ab14f5d8b06badc190) | Semantics/documentation inventories checked | Inventory agreement does not prove implementation soundness |
| #1203 / [#1229](https://github.com/juanmicrosoft/calor/commit/0f7296c88f72b793c24952b750060f686f06d9d6) | Benchmark row identity fixed | It does not validate benchmark source equivalence or scientific interpretation |
| #1204 / [#1231](https://github.com/juanmicrosoft/calor/commit/23a700a26208d683cc1790b8ea057c025ad643b9) | MDX anchors and fragment history fixed | Scope is generated documentation routes under test |
| #1205 / [#1233](https://github.com/juanmicrosoft/calor/commit/087af03ff799738393851a719a42d63e41ac6439) | Mobile drawers made accessible | This is targeted component evidence, not a site-wide accessibility certification |
| #1206 / [#1232](https://github.com/juanmicrosoft/calor/commit/1e338071d1359d181a6d3b1e74335b4af5e3cd87) | Provider-neutral first program completed | Validation used the repository source path, not every installed-package environment |
| #1207 / [#1234](https://github.com/juanmicrosoft/calor/commit/cb02815626b0af58c53f0966e4635e0e8f793751) | Contrast and copy controls improved | Targeted checks are not a complete readability/accessibility audit |
| #1208 / [#1236](https://github.com/juanmicrosoft/calor/commit/fbb5857decb17ed57349f4922950e4d13498fe5e) | Dates made deterministic and timezone-labelled | This addresses rendered timestamp semantics only |
| #1209 / [#1238](https://github.com/juanmicrosoft/calor/commit/c260a7a4716f10b2b5013b15f60d171b876aa1d6) | Active navigation corrected | Route/base-path cases under test define the boundary |
| #1210 / [#1239](https://github.com/juanmicrosoft/calor/commit/c2310aa7b6dac973716f7893a8bd7ce04d316629) | Theme persisted and initialized from system | Browser preference behavior is not universal client compatibility |
| #1211 / [#1235](https://github.com/juanmicrosoft/calor/commit/397c404778a16fded449cb8d5295bc342f854d6f) | Table sorting/selection keyboard state fixed | Covered tables and interactions define the scope |
| #1212 / [#1240](https://github.com/juanmicrosoft/calor/commit/a5b6e63c3d240ff47da18580b216aa9f8218c847) | Logos sized and decorative media made optional | Asset/layout budgets are not field-performance measurements |
| #1213 / [#1246](https://github.com/juanmicrosoft/calor/commit/7dcd1d3b514504bda3c1dd397795483b5d39c25e) | Public guarantees and provenance qualified | Copy corrections do not broaden effects or inheritance guarantees |
| #1214 / [#1242](https://github.com/juanmicrosoft/calor/commit/d5fc0b62e8d6cdfb023a1c011ff68fa8e1435f37) | Product target and setup clarified | This is orientation copy, not adoption evidence |
| #1215 / [#1243](https://github.com/juanmicrosoft/calor/commit/0afa8961e197de89cf7f734e7acd0261b0e03164) | Local documentation search and task paths added | Search quality is limited to the indexed local corpus |
| #1216 / [#1244](https://github.com/juanmicrosoft/calor/commit/40849f0c55370cb8ef63ad80c3ff0c44cc301834) | Canonicals, sitemap, and robots metadata added | Metadata correctness is not proof of external indexing |
| #1217 / [#1245](https://github.com/juanmicrosoft/calor/commit/533ca8e00e3484c7ac42203ef33b05ffbfb07d72) | Examples labelled accurately and kept readable | Calor uses an accurate neutral-text fallback; grammar-aware highlighting was not claimed |

## Preceding research outcomes

Call W was adjudicated on 2026-08-04. Its
[commit-pinned record](https://github.com/juanmicrosoft/calor/blob/fc55e3cf065560f49133beb794cd63403bceabf7/docs/plans/call-w-adjudication.md)
states:

- PP-A2 was **DEMAND UNPROVEN**.
- No adopter meeting the registered demand condition was secured.
- Call 3 remained closed.
- PP-W2 was not adjudicated, so the wedge result did not establish the proposed
  production advantage.

The repository records reviewed for this M0 item do not establish what
recruitment activity occurred after 2026-08-04. That interval is **unknown**.
It is not evidence of a continued search, a failed search, or zero demand.

The PP-W-rows practice collection produced zero escapes on both arms across
28 valid runs. Its formal disposition was **UNDERPOWERED**: the main
`w-rows-001` epoch never ran, `epochRun` remained false, the registered route
and leg results remained null, and only the sizing evidence was written to the
ledger. The affordable `N=3` had 0.48 power where the registered calculation
required `N=9` for 0.868 power. The ledger's top-level `achievablePower` field
contains 0.868 even though its own reason and power curve identify 0.48 as the
largest affordable power. This predecessor record treats that as an unresolved
field-semantics inconsistency and does not use the top-level value as affordable
power.

The pinned
[v0.18 plan](https://github.com/juanmicrosoft/calor/blob/514f538024df990af86054af25975b756ba42ab1/docs/plans/roadmap-v0.18.md)
and
[benefit ledger](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/bench/phase0-agent-native/effect-rows-benefit-ledger.json)
are the immutable evidence for these release and ledger facts.

The
[commit-pinned fixture redesign](https://github.com/juanmicrosoft/calor/blob/fc55e3cf065560f49133beb794cd63403bceabf7/docs/plans/2026-09-05-ppw-rows-fixture-redesign.md)
attributes the
result to a design defect: the prohibited laundering behavior was also a
visible specification violation. The redesign and open epic #1254 are
proposals with prerequisites and stopping rules, not a positive outcome. The
replacement task set, per-task visible and held-out suites, written spend
ceiling, stage-1 sizing/estimands, arm-discrimination evidence, and the
R1-and-R4 buildability test remain unresolved. So do the instrument migration
(#1264), model/agent pin (#1265), independent pre-freeze task review (#1266),
pilot-containment and registration coverage (#1271), and separate pilot epoch
handling (#1260). No new epoch or paid collection follows from this predecessor
record.

## Evidence still required by a reopened study

Closing a defect issue proves that a repository change was accepted. It does
not provide the discriminating evidence package required for a protected
workflow study. Before admitting a construct or property, a reopened study
would still need:

| Construct/property family | Pre-fix witness needed | Pinned v0.19 evidence and oracle |
|---|---|---|
| Parameter mutation and post-state (#1183) | False postcondition proof or stale parameter-state witness | Production verification rejects/demotes the false proof; runtime/post-state oracle and mutation negative control |
| Numeric simplification and contracts (#1184) | Unsound fold, overflow, division, or type-semantics witness | Exact value/exception/refusal oracle at the pinned compiler, including boundary controls |
| Assignment effects (#1185) | Indexed/member write accepted without the required charge | Production effect diagnostic plus state oracle; enforcement-disabled negative control |
| Inherited contracts (#1186) | Missed or unsound interface/base contract path | Nested/interface regression when admitted; otherwise a mechanically enforced exclusion |
| Expression grouping (#1187) | Source whose emitted grouping changes value or exception order | Value/exception/trace oracle through production code generation |
| Expression match execution (#1188) | Block arm whose statement/value behavior is lost | Explicit rejection or full value/state/trace oracle; no statement-losing acceptance |
| Overflow policy (#1189) | False-established arithmetic property under production overflow behavior | Checked/unchecked value or exception oracle matching documented policy |
| Statement match scope (#1190) | Sibling-arm declaration leakage or collision | Compile/runtime scope oracle with independently named sibling controls |
| Evaluation regions (#1191) | Conditional/eager ordering or short-circuit witness | Side-effect trace and exception-order oracle; unsupported preservation counted separately |
| Tuple/dictionary/loop/LINQ lowering (#1192-#1195) | Ordering, type, reevaluation, continue, selector, or deferral counterexample | Production value/exception/state/trace regression for each admitted form |

Every row additionally needs reproducible commands, hashes, diagnostics, and
expected outcomes; a construct-to-evidence manifest must name supported forms
and exclude unsupported compositions instead of counting C# preservation as
native Calor support. The oracle must fail when the old defect is reintroduced
or the relevant enforcement is disabled.

The repository already contains regression starting points, including
[ParameterPoststateTests.cs](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/tests/Calor.Verification.Tests/ParameterPoststateTests.cs),
[IndexedAssignmentEffectTests.cs](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/tests/Calor.Enforcement.Tests/IndexedAssignmentEffectTests.cs),
[TupleAssignmentMigrationTests.cs](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/tests/Calor.Compiler.Tests/TupleAssignmentMigrationTests.cs),
[DictionaryInitializerSemanticsTests.cs](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/tests/Calor.Compiler.Tests/DictionaryInitializerSemanticsTests.cs),
[ForLoopConditionSemanticsTests.cs](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/tests/Calor.Compiler.Tests/ForLoopConditionSemanticsTests.cs),
and
[LinqGroupingSemanticsTests.cs](https://github.com/juanmicrosoft/calor/blob/74e55ce4861f9548199629ab6e18623ef1759f21/tests/Calor.Compiler.Tests/LinqGroupingSemanticsTests.cs).
These are not yet the required study
captures: the reopened study must separately archive the pre-fix and pinned
v0.19.0 executions, source and compiler hashes, full outputs, oracle decisions,
negative controls, and construct-to-evidence manifest.

Standalone #1311 owns the bounded search for false-established properties in
the released compiler. Its results may supply counterexamples or regression
artifacts, but zero findings would not establish whole-compiler soundness.

## Entry disposition

The predecessors support an administrative M0 review and continued parking of
downstream work. They do not establish an adopter, adequate task supply,
affordable statistical power, a protected-C# cost advantage, or independent
methods approval. Those remain explicit inputs for #1280-#1283.
