"""Generate docs/plans/evidence/s2-1413/dispositions.json from the S1 packet and the hand-written
disposition plan below. Run from the repository root: python3 docs/plans/evidence/s2-1413/generate-dispositions.py"""
import json, hashlib, collections

S1 = 'docs/plans/evidence/s1-1311/'
findings = json.load(open(S1 + 'run2/findings.json'))
rowstatus = json.load(open(S1 + 'combined-row-status.json'))
reg = json.load(open('docs/plans/evidence/r1-1419/registration.json'))
contract = json.load(open('docs/plans/evidence/evidence-contract-1407/contract.json'))
regrows = {r['id']: r for r in reg['denominator']['rows']}

def sha(path):
    data = open(path, 'rb').read().replace(b'\r\n', b'\n')
    return hashlib.sha256(data).hexdigest()

classes = collections.defaultdict(list)
for b in ('B1', 'N1'):
    for line in open(f'bench/correctness/false-established/v024/run2/{b}/case-results.jsonl'):
        r = json.loads(line)
        classes[(b, r['rowId'])].append(r['class'])

OWNER = 'Claude Code agent (S2 #1413 session); agents never merge'
REVIEWER = '@juanmicrosoft (review and merge), Codex adversarial review recorded per PR'

repairs = [
    {
        'id': 'R-CACHE', 'kind': 'FIX-IN-0.24', 'pr': 1494,
        'branch': 'milestone-0.24/s2-1413-fix-cache-literal-width',
        'rootCause': 'ContractHasher keyed an integer literal by value only, so x + INT:1 and x + LONG:1 shared a verification-cache entry; a warm cache served the LONG text\'s Proven to the INT text (false proof, guard elided) and the INT text\'s Refuted to the LONG text.',
        'change': 'Literal keys carry width/signedness/base/sign/magnitude (and real-literal kind); output types are length-prefixed; inferred binding types hash distinctly; keys hash raw UTF-16 code units; cache format 1.20.',
        'nonTestChangedLines': 176,
        'regressionWitness': ['tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs'],
        'rows': ['CACHE-LITERAL-WIDTH'],
    },
    {
        'id': 'R-IMPL', 'kind': 'DEMOTE-IN-0.24', 'pr': 1495,
        'branch': 'milestone-0.24/s2-1413-fix-implication-definedness',
        'rootCause': 'Z3ImplicationProver decided interface->implementer precondition implication over total solver terms (non-null strings, bvsrem defined at 0) and reported Calor0815 "proven" for implementer preconditions that throw or are false on interface-accepted inputs.',
        'change': 'An implication is proven only when the antecedent entails the consequent\'s divisor and checked-overflow side conditions and no string/array/user-type sort is touched; otherwise Assumed, reported as the new warning Calor0819 with no heuristic fallback and no Calor0814 "valid"; conditional-position divisors are Unsupported.',
        'nonTestChangedLines': 213,
        'regressionWitness': ['tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs', 'tests/Calor.Verification.Tests/S2ImplicationProverDefinednessTests.cs'],
        'rows': ['IMPL-ASSUMPTION-FORMS', 'IMPL-DIVISION-TOTALIZED'],
    },
    {
        'id': 'R-OBL', 'kind': 'FIX-IN-0.24', 'pr': 1496,
        'branch': 'milestone-0.24/s2-1413-fix-obligation-state',
        'rootCause': 'ObligationSolver asserted preconditions for every obligation even after the body reassigned their variables (false Discharged, guard elided), and reported SAT models as counterexamples although its state over-approximated the program state (reassignments, else bodies without negated guards, unbound refined return values, unassumed named-refinement parameters).',
        'change': 'Entry facts are dropped when the body may rebind a name they read (incl. ref/out arguments, raw C#); a SAT model is a refutation only when the state is exact, otherwise Unsupported (guard kept); else/elseif negation facts and named-refinement parameter facts are added.',
        'nonTestChangedLines': 416,
        'regressionWitness': ['tests/Calor.Compiler.Tests/S2ObligationStateTests.cs'],
        'rows': ['OBL-MUTATION-KILL', 'OBL-BRANCH-FACTS', 'OBL-REFINEMENT-RETURN', 'OBL-SUBTYPE', 'OBL-SELFREF'],
    },
    {
        'id': 'R-TEXT', 'kind': 'FIX-IN-0.24', 'pr': 1497,
        'branch': 'milestone-0.24/s2-1413-fix-z3-text-encoding',
        'rootCause': 'String literals reached Z3 per UTF-8 byte ("é" has length 2), Substring/IndexOf-with-start were total in the solver, and (discovery #1493) Z3 symbol names took ANSI marshaling, so non-ASCII identifiers could collapse into one constant on Windows.',
        'change': 'Literals are escaped per UTF-16 code unit (and the backslash); indexed string operations carry range side conditions; every symbol is named through the injective ASCII encoding ContractTranslator.Z3Name; cache format 1.21.',
        'nonTestChangedLines': 181,
        'regressionWitness': ['tests/Calor.Verification.Tests/S2Z3TextEncodingTests.cs'],
        'rows': ['STR-NULL-NONASCII', 'STR-OPS-COUNT-INDEX'],
        'discoveries': ['D-1493'],
    },
    {
        'id': 'R-QNT', 'kind': 'DEMOTE-IN-0.24', 'pr': 1498,
        'branch': 'milestone-0.24/s2-1413-fix-nested-quantifier-claim',
        'rootCause': 'The verifier reported Proven for a nested bounded forall that the emitter cannot lower (Calor0326); the registered row requires refusal.',
        'change': 'Contract verifier, obligation solver, and implication prover return Unsupported for a quantifier nested in another; such cache keys are never stored or served.',
        'nonTestChangedLines': 92,
        'regressionWitness': ['tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs'],
        'rows': ['QNT-NESTED'],
    },
    {
        'id': 'R-NUM', 'kind': 'DEMOTE-IN-0.24', 'pr': None,
        'branch': None,
        'status': 'decision-required',
        'rootCause': 'The registration classifies NUM-NARROW-ARITH and NUM-LITERAL-OVERSIZE as unsupported-refused (divergences D1/D2 of the frozen docs/verification-modeled-forms.md say "refused") and NUM-OVERFLOW-CHECKED as assumed, but the verifier deliberately models C# narrow promotion (W1 Slice 1), types INT: literals as C# does (#774), and proves checked-arithmetic contracts whose overflow the preconditions rule out. Each Proven agrees with the oracle; none is a false proof.',
        'change': 'Not opened. A draft that implements the registered refusals (narrow arithmetic and INT:-inferred 64-bit literals Unsupported, checked-arithmetic postconditions Assumed whenever overflow is possible for some input) is 77 non-test lines but breaks 19 existing tests that pin the deliberate semantics (W1Slice1SoundnessTests narrow promotion x3, NumericExecutableSemanticsTests x2, OverflowSoundnessBenchmark *_Bounded_MustBeProven x7, VerifierTests bounded proofs x3, ProductionOverflowRuntimeTests guarded arithmetic x3) and changes the committed #1135 differential oracle reports (VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle). That reverses accepted verifier semantics and invalidates a G3 artifact, so it needs a maintainer decision.',
        'decisionOptions': [
            'Accept the demotion: open R-NUM (the sixth and last repair PR within the s2-repairs ceiling), update the 19 tests, and regenerate the #1135 differential reports under G3.',
            'Amend the contract (versioned, after decision-bearing inspection) to reclassify the three rows to the semantics the verifier deliberately implements; the 5 findings would then need re-dispositioning under the amended table.',
            'Record MILESTONE-FAILED for the 5 findings (terminal success predicate 5 then fails).',
        ],
        'nonTestChangedLines': 77,
        'regressionWitness': [],
        'rows': ['NUM-NARROW-ARITH', 'NUM-LITERAL-OVERSIZE', 'NUM-OVERFLOW-CHECKED'],
    },
]
for r in repairs:
    r.setdefault('status', 'open')
    r.setdefault('mergeCommit', None)
    r.setdefault('discoveries', [])
    r['owner'] = OWNER
    r['reviewer'] = REVIEWER
    r['blocks'] = [1423]

# finding number -> (disposition, repair, reason)
def F(nums, disp, rep, reason):
    return {n: (disp, rep, reason) for n in nums}

plan = {}
plan.update(F([31], 'FIX-IN-0.24', 'R-CACHE', 'False unconditional proof (warm cache served the LONG:1 Proven to the INT:1 text). Fixed: the key distinguishes literal width; the warm verdict now equals the cold Refuted, which the oracle confirms (int.MaxValue + 1 wraps).'))
plan.update(F([32], 'FIX-IN-0.24', 'R-CACHE', 'Stale-cache proof, same case and cause as the false proof above. Fixed by the same key change.'))
plan.update(F([33], 'FIX-IN-0.24', 'R-CACHE', 'Spurious refutation: the warm cache served the INT:1 Refuted to the LONG:1 text, which holds. Fixed: the warm verdict now equals the cold Proven.'))
plan.update(F([34], 'FIX-IN-0.24', 'R-CACHE', 'Stale-cache proof for the same case. Fixed by the same key change.'))
plan.update(F([27, 29], 'DEMOTE-IN-0.24', 'R-IMPL', 'False unconditional proof: Calor0815 "precondition weakening proven" for (>= (len s) 0), which throws at s = null on an interface-accepted input. Demoted: the implication is Assumed (string-model), reported as Calor0819; no proof and no "inheritance valid" claim.'))
plan.update(F([28], 'DEMOTE-IN-0.24', 'R-IMPL', 'False unconditional proof: Calor0815 for (|| (! (isempty s)) (== s "")), false at s = null. Demoted to Assumed (string-model), Calor0819.'))
plan.update(F([30], 'DEMOTE-IN-0.24', 'R-IMPL', 'False unconditional proof: Calor0815 for (> (% x y) -2), which throws at y = 0. Demoted: the divisor side condition is not entailed by the interface precondition, so the implication is Assumed (contract-division), Calor0819.'))
plan.update(F([14, 15], 'FIX-IN-0.24', 'R-OBL', 'False unconditional proof: a §Q fact on x survived §ASSIGN x and discharged §PROOF on the new value (guard elided). Fixed: entry facts are dropped when the body rebinds their names; the obligation is no longer discharged (Unsupported, guard kept), which agrees with the oracle (violated).'))
plan.update(F([16, 17, 18, 19], 'DEMOTE-IN-0.24', 'R-OBL', 'Spurious refutation: the model ignored the reassignment inside the guarded body. Demoted: an obligation that reads a reassigned variable gets no counterexample claim; its outcome is Unsupported (Calor1124, guard kept) instead of a Failed compile error.'))
plan.update(F([12], 'FIX-IN-0.24', 'R-OBL', 'Spurious refutation (the model x = 2 does not reach the else body). Fixed: else bodies assume the negated condition; the obligation is Failed with the reaching model x = 548596110, which violates the claim as the oracle says.'))
plan.update(F([13], 'FIX-IN-0.24', 'R-OBL', 'Spurious refutation in an else body. Fixed by the else-negation fact: the obligation is Discharged, which agrees with the oracle (holds).'))
plan.update(F([20], 'DEMOTE-IN-0.24', 'R-OBL', 'Spurious refutation: the refined-return obligation ran with `result` unbound, so result = 2 was no execution. Demoted: a refined return gets no counterexample claim (Unsupported, guard kept).'))
plan.update(F([21], 'FIX-IN-0.24', 'R-OBL', 'Spurious refutation: the model violated the parameter\'s named refinement type, which the entry guard enforces. Fixed: named-refinement parameters are entry facts; the obligation is Discharged (oracle: holds).'))
plan.update(F([22, 23, 24, 25, 26], 'FIX-IN-0.24', 'R-OBL', 'Spurious refutation: the model violated the parameter\'s self-referential named refinement. Fixed: the refinement is an entry fact; no refutation remains (Discharged, Assumed for checked arithmetic, or Unsupported when no overflow-free state exists).'))
plan.update(F([10, 11], 'FIX-IN-0.24', 'R-TEXT', 'Spurious refutation: "é" had length 2 in the solver (UTF-8 bytes) and 1 in .NET. Fixed: literals are sent per UTF-16 code unit; the postcondition is Assumed (string model) as the assumed row requires, with no refutation (oracle: holds).'))
plan.update(F([9], 'FIX-IN-0.24', 'R-TEXT', 'Spurious refutation: the model s = "" makes s.Substring(1, 1) throw. Fixed: Substring carries its range side condition; the refutation now has the reaching model s = "AB" (result 1, oracle: violated).'))
plan.update(F([6, 7, 8], 'DEMOTE-IN-0.24', 'R-QNT', 'Required demotion absent: Proven for a nested bounded forall (oracle holds) on a row registered unsupported-refused. Demoted: nested quantifiers are refused (Unsupported) on every channel and never served from cache.'))
plan.update(F([1], 'DEMOTE-IN-0.24', 'R-NUM', 'Required demotion absent: Proven for i8 * i8 (oracle: holds) on a row registered unsupported-refused (D1). Pending maintainer decision; see repair R-NUM.'))
plan.update(F([2], 'DEMOTE-IN-0.24', 'R-NUM', 'Required demotion absent: Proven for an INT: literal outside int32 (oracle: holds) on a row registered unsupported-refused (D2). Pending maintainer decision; see repair R-NUM.'))
plan.update(F([3, 4, 5], 'DEMOTE-IN-0.24', 'R-NUM', 'Required demotion absent: Proven for (< (- x 1) x) where §Q rules out the overflow (oracle: holds) on a row registered assumed. The row title says "unless entailed", but the frozen classification table makes any Proven on an assumed row a finding. Pending maintainer decision; see repair R-NUM.'))
assert sorted(plan) == list(range(1, 35))

def row_disposition(row_findings):
    disps = {f['disposition'] for f in row_findings}
    if 'MILESTONE-FAILED' in disps:
        return 'MILESTONE-FAILED'
    if 'DEMOTE-IN-0.24' in disps:
        return 'DEMOTE-IN-0.24'
    return 'FIX-IN-0.24'

baselines = {}
for b in ('B1', 'N1'):
    flist = []
    for f in findings[b]:
        n = int(f['findingId'].split('-')[-1])
        disp, rep, reason = plan[n]
        flist.append({
            'findingId': f['findingId'], 'rowId': f['rowId'], 'caseId': f['caseId'],
            'class': f['class'], 'token': f['token'],
            'disposition': disp, 'repair': rep, 'reason': reason,
        })
    rlist = []
    for r in rowstatus['baselines'][b]['rows']:
        rid, status = r['row'], r['combined']
        rf = [f for f in flist if f['rowId'] == rid]
        entry = {'rowId': rid, 'status': status, 'releaseCritical': r['releaseCritical']}
        if status == 'CLEAN-WITHIN-BUDGET':
            entry['disposition'] = 'VALIDATED'
            noproof = regrows[rid]['classification'] == 'modeled' and 'validated-proof' not in classes[(b, rid)]
            entry['reason'] = ('Clean within the registered matrix and budget in both runs; no establishing outcome was observed (zero validated-proof cases), so this supports no claim that relies on Proven/Discharged for the form.'
                               if noproof else
                               'Clean within the registered matrix and budget in both runs (no counterexample found; not a soundness guarantee).')
            entry['establishingOutcomeObserved'] = not noproof
        elif status == 'NOT-INVESTIGATED':
            entry['disposition'] = 'NOT-INVESTIGATED'
            entry['reason'] = 'Excluded by the frozen #1419 scope before execution (zero cases, not release-critical).'
        else:
            entry['disposition'] = row_disposition(rf)
            entry['repairs'] = sorted({f['repair'] for f in rf})
            entry['findings'] = [f['findingId'] for f in rf]
            entry['reason'] = f'{len(rf)} finding(s); the row takes the most conservative finding disposition (DEMOTE if any finding is demoted).'
        rlist.append(entry)
    baselines[b] = {'findings': flist, 'rows': rlist}

baselines['B1']['artifact'] = {
    'identity': 'tag v0.22.0, commit 72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1 (GitHub prerelease; no NuGet package)',
    'immutable': True,
    'dispositionScope': 'Candidate-side: every FIX-IN-0.24/DEMOTE-IN-0.24 lands on the post-repair candidate frozen by #1423. The v0.22.0 tag is not changed; its findings stay recorded against it (registration dispositionHandoff).',
}
baselines['N1']['artifact'] = {
    'identity': 'Calor 0.21.0 on nuget.org, packed from 88b5d38df97fd7e438882c956b9f9dcdc7a6cef5 (the binary users install)',
    'immutable': True,
    'dispositionScope': 'Candidate-side, as for B1. A fix in 0.24 source does not change the published 0.21.0 package: the false unconditional proofs (OBL-MUTATION-KILL, IMPL-ASSUMPTION-FORMS, IMPL-DIVISION-TOTALIZED, CACHE-LITERAL-WIDTH) remain in it, and so does the Windows symbol collision (#1493).',
    'publishedArtifactObligation': 'Proposed, needs a maintainer decision: the first release that carries these repairs names, in its release notes, the four false-proof forms and #1493 as affecting 0.21.0 (and the v0.22.0 prerelease), says that runtime guards for those forms may have been removed, and advises upgrading; a GitHub security advisory is optional. Contract §8 History keeps 0.21.0 as a historical record; nothing claims it is fixed.',
}

discoveries = [{
    'id': 'D-1493', 'issue': 1493, 'registered': False,
    'source': 'Discovery by code reading during #1135 (G3, PR #1492); not part of the registered #1311 sweep and not executed by S1.',
    'finding': 'Z3 symbol names take the .NET binding\'s ANSI marshaling; on Windows two non-ASCII identifiers outside the code page can become one Z3 constant, a possible false proof (Windows only).',
    'affects': ['B1', 'N1', 'main at 0142438f'],
    'observed': False,
    'disposition': 'FIX-IN-0.24', 'repair': 'R-TEXT',
    'reason': 'Fixed by the injective ASCII symbol encoding in R-TEXT; the end-to-end witness runs on Windows in the z3-consumer-matrix job.',
}]

record = {
    'schemaVersion': 1, 'issue': 1413, 'gate': 'S2', 'epic': 1409,
    'contractVersion': contract['contractVersion'],
    'vocabulary': contract['findingDispositions'],
    'registrationCommit': '6a1a78db71b04bee3f2d42268e92800d94333f67',
    'sources': {
        'findings': S1 + 'run2/findings.json',
        'run1Findings': S1 + 'findings.json',
        'rowStatus': S1 + 'combined-row-status.json',
        'sha256': {p: sha(p) for p in (S1 + 'run2/findings.json', S1 + 'findings.json', S1 + 'combined-row-status.json')},
    },
    'closure': {
        'status': 'OPEN',
        'note': 'OPEN until every repair is merged (status "merged" with its merge commit) and R-NUM is decided. The validator rejects a CLOSED record with any unmerged or undecided repair.',
    },
    'capacity': {'ceiling': 's2-repairs', 'maxRepairs': 6, 'maxNonTestChangedLinesPerRepair': 600,
                 'used': len(repairs), 'note': 'Six repairs counting the undecided R-NUM; no capacity remains for further repairs.'},
    'repairs': repairs,
    'baselines': baselines,
    'discoveryFindings': discoveries,
    'openQuestions': [
        {'id': 'Q1-R-NUM', 'question': 'Disposition path for NUM-NARROW-ARITH, NUM-LITERAL-OVERSIZE, NUM-OVERFLOW-CHECKED (5 findings per baseline); see repair R-NUM decisionOptions.'},
        {'id': 'Q2-O2-GEN-REFUSAL', 'question': 'S1 condition 10: O2 ran only partially on GEN-REFUSAL-002/-004 (the frozen input new object() cannot be passed to the compiled double[] parameter). Proposed: keep GEN-REFUSAL VALIDATED on both baselines. Both cases are Unsupported (refusal-validated): there is no claim and no elided guard for O2 to corroborate, O1 agrees, and O2 is corroboration, not the adjudicating oracle. The partial replay is recorded as an O2 coverage limitation of the frozen generator, not a finding.'},
        {'id': 'Q3-N1-ADVISORY', 'question': 'Whether to adopt the N1 publishedArtifactObligation (release-notes advisory for 0.21.0 / v0.22.0).'},
        {'id': 'Q4-ISSUE-LINKS', 'question': 'Issue acceptance: each repair PR must be attached to #1409 and encoded as blocking #1423. Agents do not edit issues; the maintainer links PRs #1494-#1498 (and R-NUM if opened).'},
    ],
}
open('docs/plans/evidence/s2-1413/dispositions.json', 'w').write(json.dumps(record, indent=2, ensure_ascii=False) + '\n')
print('written', sum(len(baselines[b]['findings']) for b in baselines), 'findings,',
      sum(len(baselines[b]['rows']) for b in baselines), 'rows')
