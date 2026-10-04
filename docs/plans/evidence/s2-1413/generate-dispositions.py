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
REVIEWS = 'docs/plans/evidence/s2-1413/reviews/'

repairs = [
    {
        'id': 'R-CACHE', 'pr': 1494, 'status': 'merged', 'mergeCommit': 'ef89c027dd933f4991fd3a85db4b1d1fd707c8db',
        'branch': 'milestone-0.24/s2-1413-fix-cache-literal-width',
        'rootCause': 'ContractHasher keyed an integer literal by value only, so x + INT:1 and x + LONG:1 shared a verification-cache entry; a warm cache served the LONG text\'s Proven to the INT text (false proof, guard elided) and the INT text\'s Refuted to the LONG text.',
        'change': 'Literal keys carry width/signedness/base/sign/magnitude (and real-literal kind); output types are length-prefixed; inferred binding types hash distinctly; keys hash raw UTF-16 code units; cache format 1.20.',
        'nonTestChangedLines': 59,
        'regressionWitness': ['tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs'],
        'rows': ['CACHE-LITERAL-WIDTH'],
        'reviews': REVIEWS + 'fix-cache-literal-width/',
        'reviewVerdict': 'Rounds 1-2 REQUEST-CHANGES (fixed), round 3 APPROVE, verification pass APPROVE.',
    },
    {
        'id': 'R-IMPL', 'pr': 1495,
        'branch': 'milestone-0.24/s2-1413-fix-implication-definedness',
        'rootCause': 'Z3ImplicationProver decided interface/implementer contract implications over total solver terms (non-null strings, bvsrem defined at 0) and reported Calor0815 "proven" for implementer preconditions that throw or are false on interface-accepted inputs. No guard is elided on this channel; the false claim is the LSP acceptance.',
        'change': 'The prover decides A and D(A) and not (D(C) and C) with D = divisor and checked-overflow definedness: a contract that throws on an accepted input is a genuine LSP refutation with that input; a model that may rely on a throwing contract is Unsupported; any string/array/user-type sort makes a proof Assumed (new warning Calor0819) and a model over such a sort Unsupported (index, substring, and null failures are not modeled); identical contracts are decided by structural identity before the solver; every undecided check, including the inherited-conflict check, reports Calor0816 and never yields Calor0814 "valid".',
        'nonTestChangedLines': 415,
        'regressionWitness': ['tests/Calor.Compiler.Tests/S2ImplicationDefinednessTests.cs', 'tests/Calor.Verification.Tests/S2ImplicationProverDefinednessTests.cs'],
        'rows': ['IMPL-ASSUMPTION-FORMS', 'IMPL-DIVISION-TOTALIZED'],
        'reviews': REVIEWS + 'fix-implication-definedness/',
        'reviewVerdict': 'Rounds 1-3 REQUEST-CHANGES (fixed), verification pass APPROVE (one MINOR: the conflict test does not assert the Unestablished status directly).',
    },
    {
        'id': 'R-OBL', 'pr': 1496,
        'branch': 'milestone-0.24/s2-1413-fix-obligation-state',
        'rootCause': 'ObligationSolver asserted preconditions for every obligation even after the body reassigned their variables (false Discharged, guard elided), and reported SAT models as counterexamples although its state over-approximated the program state (reassignments, else bodies without negated guards, unbound refined return values, unassumed named-refinement parameters).',
        'change': 'Entry facts are dropped when the body may write a name they read (incl. ref/out/in aliasing, element/field stores, collection updates); raw C#, unsafe/pointer code, and lambdas make a body opaque, and raw C# in an entry predicate makes every obligation Unsupported; every member/element read (a proof condition included) and foreach counts as a possible heap write (getters, indexers, enumerators), and only the exactness of a §PROOF counterexample ignores reads inside its own condition; a SAT model is a refutation only when the state is exact, otherwise Unsupported (guard kept), including when a dropped entry refinement constrains any name the query reads; else/elseif negation facts and named-refinement parameter facts (functions, methods, constructors, operators) are added.',
        'nonTestChangedLines': 591,
        'residual': 'See discoveries D-OBL-PROOF-GETTER and D-OBL-THROWING-PREDECESSOR (MILESTONE-FAILED pending decision Q7).',
        'overrunAmendment': '1.3.0',
        'reviewRoundOverrun': 'After its three review rounds R-OBL had two further Codex passes, each of which found a defect in the previous fix (BLOCKING, then MAJOR) and led to a compiler change with a regression. Under the frozen ceiling (three rounds per PR, then close and rescope; stopping rule 1) this is an overrun, recorded here; acceptance of R-OBL needs the maintainer decision Q8.',
        'regressionWitness': ['tests/Calor.Compiler.Tests/S2ObligationStateTests.cs'],
        'rows': ['OBL-MUTATION-KILL', 'OBL-BRANCH-FACTS', 'OBL-REFINEMENT-RETURN', 'OBL-SUBTYPE', 'OBL-SELFREF'],
        'reviews': REVIEWS + 'fix-obligation-state/',
        'reviewVerdict': 'Rounds 1-3 REQUEST-CHANGES (fixed); verification pass 1 found one BLOCKING in the round-3 fix (fixed), verification pass 2 found one MAJOR (fixed); a final verification-only pass authorized by the maintainer (decision Q8, amendment 1.3.0) on that last fix: APPROVE.',
    },
    {
        'id': 'R-TEXT', 'pr': 1497,
        'branch': 'milestone-0.24/s2-1413-fix-z3-text-encoding',
        'rootCause': 'String literals reached Z3 per UTF-8 byte ("é" has length 2), Substring was total in the solver, and (discovery #1493) Z3 symbol names took ANSI marshaling, so non-ASCII identifiers could collapse into one constant on Windows.',
        'change': 'Literals are escaped per UTF-16 code unit (and the backslash); Substring carries range side conditions; IndexOf with a start index is refused (the emitter drops the start); every symbol is named through the injective ASCII encoding ContractTranslator.Z3Name; "$" names are reserved; a side condition over a body local is not modeled (Unsupported); unsatisfiability and vacuity are Unsupported when a null-tolerant form (==, Equals, IsNullOrEmpty) over a possibly null term is present; cache format 1.21. Stacked on #1495 (GitHub base; nonTestChangedLines is measured against it). Its cache-key collision class for unpaired surrogates is closed by #1494 (lossless key hashing), which must merge first. Residual: the canonical string-model and contract-division assumption strings (pinned by docs/verification-modeled-forms.md and the G3 oracle) still describe byte counting and division only; the reason texts are corrected.',
        'nonTestChangedLines': 259,
        'regressionWitness': ['tests/Calor.Verification.Tests/S2Z3TextEncodingTests.cs'],
        'rows': ['STR-NULL-NONASCII', 'STR-OPS-COUNT-INDEX'],
        'discoveries': ['D-1493'],
        'dependsOn': [1495, 1494],
        'reviews': REVIEWS + 'fix-z3-text-encoding/',
        'reviewVerdict': 'Rounds 1-3 REQUEST-CHANGES (fixed), verification pass APPROVE.',
    },
    {
        'id': 'R-QNT', 'pr': 1498,
        'branch': 'milestone-0.24/s2-1413-fix-nested-quantifier-claim',
        'rootCause': 'The verifier reported Proven for a nested bounded forall whose runtime lowering the emitter rejects (Calor0326); the registered row requires refusal.',
        'change': 'Contract verifier, obligation solver (condition and assumptions), implication prover, and guard validation refuse a quantifier nested in another (Unsupported); such cache keys are never stored or served; k-induction no longer drops unparsable invariant conjuncts. The interface channel reports the refusal (Calor0816, no Calor0814) only with #1495, which must merge first.',
        'nonTestChangedLines': 116,
        'regressionWitness': ['tests/Calor.Verification.Tests/S2NestedQuantifierTests.cs', 'tests/Calor.Compiler.Tests/S2NestedQuantifierChannelTests.cs'],
        'rows': ['QNT-NESTED'],
        'dependsOn': [1495],
        'reviews': REVIEWS + 'fix-nested-quantifier-claim/',
        'reviewVerdict': 'Rounds 1-3 REQUEST-CHANGES (round 3 MINOR only, addressed), verification pass REQUEST-CHANGES for the PR-body merge prerequisite only (added; no code change requested).',
    },
    {
        'id': 'R-NUM', 'pr': 1502,
        'branch': 'milestone-0.24/s2-1413-fix-num-refusal',
        'rootCause': 'The registration classifies NUM-NARROW-ARITH and NUM-LITERAL-OVERSIZE as unsupported-refused (divergences D1/D2 of the frozen docs/verification-modeled-forms.md say "refused") and NUM-OVERFLOW-CHECKED as assumed, but the verifier modeled C# narrow promotion (W1 Slice 1), typed out-of-range INT: literals as 64-bit (#774), and proved checked-arithmetic contracts whose overflow the preconditions rule out. Each Proven agreed with the oracle; none was a false proof.',
        'change': 'Opened by maintainer decision Q1 (2026-10-04): demote per the frozen table exactly. Sub-32-bit arithmetic/shifts/negation are refused (D1); an INT: literal outside int32 is refused (D2; lexer marks the inferred width, the cache key distinguishes it); checked arithmetic that can overflow for some value of its types is Assumed (checked-arithmetic) even when entailed. Overflow sensitivity is decided without preconditions (some value of the operand types overflows), so arithmetic that cannot overflow stays Proven. The 15 Calor.Verification.Tests cases that pinned the old behavior are updated (each justified by the frozen table); the #1135 differential reports are unchanged; cache format 1.22.',
        'decision': 'Q1 (2026-10-04): open the sixth repair PR and demote per the frozen table exactly, with no reinterpretation of "unless entailed"; update the tests that pinned the old behavior; regenerate the #1135 reports if they change (they do not).',
        'nonTestChangedLines': 141,
        'regressionWitness': ['tests/Calor.Verification.Tests/S2NumericRefusalTests.cs'],
        'reviews': REVIEWS + 'fix-num-refusal/',
        'rows': ['NUM-NARROW-ARITH', 'NUM-LITERAL-OVERSIZE', 'NUM-OVERFLOW-CHECKED'],
    },
]
repairs.append({
    'id': 'R-OBL-RESIDUALS', 'pr': 1503,
    'branch': 'milestone-0.24/s2-1413-fix-obligation-residuals',
    'rootCause': 'Review-found residuals of the obligation solver (discoveries D-OBL-THROWING-PREDECESSOR and D-OBL-PROOF-GETTER): exactness did not track an earlier statement or enclosing condition that throws implicitly, and a proof reading a property (getter, or a property hiding an inherited field) could be refuted with an unreachable model. Spurious refutations, never false proofs.',
    'change': 'Visible demotion: after a statement that may throw (checked or dividing arithmetic, calls, member/element reads, casts, a retained proof guard), or under a guard condition that may throw, the state is not exact; an obligation reading a member named like a declared property is not exact. A SAT result is Unsupported (Calor1124, guard kept); UNSAT handling is unchanged. Stacked on #1496.',
    'decision': 'Q7 (2026-10-04): contract amendment 1.3.0 (merged, ffa75e8b) raises the S2 repair cap 6 -> 7 for these two discoveries only (capacity.exceptions[ceiling=s2-repairs]); at most 3 review rounds.',
    'dependsOnAmendment': '1.3.0',
    'dependsOn': [1496],
    'discoveries': ['D-OBL-PROOF-GETTER', 'D-OBL-THROWING-PREDECESSOR'],
    'nonTestChangedLines': 162,
    'regressionWitness': ['tests/Calor.Compiler.Tests/S2ObligationResidualTests.cs'],
    'rows': [],
    'reviews': REVIEWS + 'fix-obligation-residuals/',
})
for r in repairs:
    r.setdefault('status', 'open')
    r.setdefault('mergeCommit', None)
    r.setdefault('discoveries', [])
    r.setdefault('dependsOn', [])
    r['owner'] = OWNER
    r['reviewer'] = REVIEWER
    r['blocks'] = [1423]

FIX, DEMOTE = 'FIX-IN-0.24', 'DEMOTE-IN-0.24'
plan = {}
def F(nums, disp, rep, reason):
    for n in nums:
        plan[n] = (disp, rep, reason)

F([31], FIX, 'R-CACHE', 'False unconditional proof (a warm cache served the LONG:1 Proven to the INT:1 text). Fixed: the key distinguishes literal width; the warm verdict equals the cold Refuted, which the oracle confirms (int.MaxValue + 1 wraps).')
F([32], FIX, 'R-CACHE', 'Stale-cache proof, same case and cause as the false proof above. Fixed by the same key change.')
F([33], FIX, 'R-CACHE', 'Spurious refutation: the warm cache served the INT:1 Refuted to the LONG:1 text, which holds. Fixed: the warm verdict equals the cold Proven.')
F([34], FIX, 'R-CACHE', 'Stale-cache proof for the same case. Fixed by the same key change.')
F([27, 29], DEMOTE, 'R-IMPL', 'False unconditional proof: Calor0815 for (>= (len s) 0), which throws at s = null on an interface-accepted input (no guard elided; the false claim is the LSP acceptance). Demoted: null strings cannot be modeled, so the implication is Assumed (string-model), Calor0819; no "proven" and no "inheritance valid".')
F([28], DEMOTE, 'R-IMPL', 'False unconditional proof: Calor0815 for (|| (! (isempty s)) (== s "")), false at s = null. Demoted to Assumed (string-model), Calor0819.')
F([30], FIX, 'R-IMPL', 'False unconditional proof: Calor0815 for (> (% x y) -2), which throws at y = 0. Fixed: the implication is decided with explicit definedness and is refuted with the oracle\'s own witness (x = 3, y = 0) as a Calor0810 LSP error.')
F([14, 15], DEMOTE, 'R-OBL', 'False unconditional proof: a §Q fact on x survived §ASSIGN x and discharged §PROOF on the new value (guard elided). The stale fact is no longer used, and the obligation visibly becomes Unsupported (Calor1124, guard kept); the oracle says violated, and no claim is made.')
F([16, 17, 18, 19], DEMOTE, 'R-OBL', 'Spurious refutation: the model ignored the reassignment inside the guarded body. Demoted: an obligation that reads a reassigned variable gets no counterexample claim; it is Unsupported (Calor1124, guard kept) instead of a Failed compile error.')
F([12], FIX, 'R-OBL', 'Spurious refutation (the model x = 2 does not reach the else body). Fixed: else bodies assume the negated condition; the obligation is Failed with the reaching model x = 548596110, which violates the claim as the oracle says.')
F([13], FIX, 'R-OBL', 'Spurious refutation in an else body. Fixed by the else-negation fact: the obligation is Discharged, which agrees with the oracle (holds).')
F([20], DEMOTE, 'R-OBL', 'Spurious refutation: the refined-return obligation ran with `result` unbound, so result = 2 was no execution. Demoted: a refined return gets no counterexample claim (Unsupported, guard kept).')
F([21], FIX, 'R-OBL', 'Spurious refutation: the model violated the parameter\'s named refinement type, which the entry guard enforces. Fixed: named-refinement parameters are entry facts; the obligation is Discharged (oracle: holds).')
F([22], DEMOTE, 'R-OBL', 'Spurious refutation (SELFREF-001): the model violated the parameter\'s named refinement. With the refinement as an entry fact there is no refutation; the obligation is Assumed (checked-arithmetic: the predicate\'s subtraction can overflow), guard kept.')
F([23], FIX, 'R-OBL', 'Spurious refutation (SELFREF-002). Fixed: with the named refinement as an entry fact the obligation is Discharged (oracle: holds).')
F([24], DEMOTE, 'R-OBL', 'Spurious refutation (SELFREF-003). With the refinement as an entry fact there is no refutation; the obligation is Assumed (checked-arithmetic), guard kept.')
F([25], FIX, 'R-OBL', 'Spurious refutation (SELFREF-004). Fixed: with the named refinement as an entry fact the obligation is Discharged (oracle: holds).')
F([26], DEMOTE, 'R-OBL', 'Spurious refutation (SELFREF-005, oracle vacuous-in-domain: no input reaches the binding). No refutation remains; the obligation is Unsupported ("no overflow-free state satisfies the assumptions"), guard kept. This withdraws the claim; it does not prove the property.')
F([10, 11], FIX, 'R-TEXT', 'Spurious refutation: "é" had length 2 in the solver (UTF-8 bytes) and 1 in .NET. Fixed: literals are sent per UTF-16 code unit; no refutation remains, and the postcondition stays Assumed under the existing string-model limitation, as the assumed row requires (oracle: holds).')
F([9], FIX, 'R-TEXT', 'Spurious refutation: the model s = "" makes s.Substring(1, 1) throw. Fixed: Substring carries its range side condition; the refutation now has the reaching model s = "AB" (result 1, oracle: violated).')
F([6, 7, 8], DEMOTE, 'R-QNT', 'Required demotion absent: Proven for a nested bounded forall (oracle holds) on a row registered unsupported-refused. Demoted: nested quantifiers are refused (Unsupported) on every verifier channel and never served from cache.')
F([1], DEMOTE, 'R-NUM', 'Required demotion absent: Proven for i8 * i8 (oracle: holds) on a row registered unsupported-refused (D1). Demoted by R-NUM (#1502): sub-32-bit arithmetic is refused (Unsupported, guard kept).')
F([2], DEMOTE, 'R-NUM', 'Required demotion absent: Proven for an INT: literal outside int32 (oracle: holds) on a row registered unsupported-refused (D2). Demoted by R-NUM (#1502): the out-of-range INT: literal is refused (Unsupported, guard kept); LONG: spells it modeled.')
F([3, 4, 5], DEMOTE, 'R-NUM', 'Required demotion absent: Proven for (< (- x 1) x) where §Q rules out the overflow (oracle: holds) on a row registered assumed. The row title says "unless entailed", but the frozen classification table makes any Proven on an assumed row a finding. Demoted by R-NUM (#1502): Assumed (checked-arithmetic), guard kept.')
assert sorted(plan) == list(range(1, 35))

def most_conservative(dispositions):
    if 'MILESTONE-FAILED' in dispositions:
        return 'MILESTONE-FAILED'
    return DEMOTE if DEMOTE in dispositions else FIX

for r in repairs:
    r['kind'] = most_conservative({d for d, rep, _ in plan.values() if rep == r['id']} or {DEMOTE})

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
            proofs = classes[(b, rid)].count('validated-proof')
            entry['disposition'] = 'VALIDATED'
            entry['validatedProofCases'] = proofs
            entry['reason'] = ('Clean within the registered matrix and budget in both runs (no counterexample found; not a soundness guarantee). '
                               + (f'{proofs} validated-proof case(s).' if proofs else
                                  'No validated-proof case: this supports no claim that relies on Proven/Discharged for the form.'))
        elif status == 'NOT-INVESTIGATED':
            entry['disposition'] = 'NOT-INVESTIGATED'
            entry['reason'] = 'Excluded by the frozen #1419 scope before execution (zero cases, not release-critical).'
        else:
            entry['disposition'] = most_conservative({f['disposition'] for f in rf})
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
    'dispositionScope': 'Candidate-side, as for B1. A fix in 0.24 source does not change the published 0.21.0 package; its false unconditional proofs remain in it.',
    'releaseNotesAdvisoryDraft': 'Calor 0.21.0 (NuGet) and the v0.22.0 prerelease contain verifier defects fixed in 0.24. (1) A proof obligation after a reassignment, and a warm verification cache that confused INT:1 with LONG:1, could report a proof and remove a runtime check the program then needed. (2) Interface contract checks could accept an implementing method whose precondition throws on inputs the interface allows; no runtime check was removed, but the acceptance was wrong. (3) On Windows, two non-ASCII identifiers could in principle share one solver symbol (#1493; not observed). The published packages are not changed; upgrade to 0.24 for the fixes.',
    'publishedArtifactObligation': 'Adopted (maintainer decision Q3, 2026-10-04): the first release that carries these repairs says in its release notes that 0.21.0 that carries these repairs says in its release notes that 0.21.0 (and the v0.22.0 prerelease) are affected, distinguishing the mechanisms: (1) proof obligations after a reassignment (OBL-MUTATION-KILL) and warm-cache literal-width proofs (CACHE-LITERAL-WIDTH) could remove a runtime guard that the program then needed; (2) interface contract checks (IMPL-ASSUMPTION-FORMS, IMPL-DIVISION-TOTALIZED) could accept an implementer precondition that throws on inputs the interface allows (no guard is removed; the acceptance is wrong); (3) #1493 is a potential, not observed, Windows-only collision of non-ASCII identifiers. Nothing claims the published package is fixed (contract §8 History).',
}

discoveries = [
    {
    'id': 'D-OBL-PROOF-GETTER', 'issue': 1413, 'registered': False,
    'source': 'Found by the R-OBL second verification pass (static, not executed); not part of the registered #1311 sweep. No separate issue is filed (agents do not file issues); #1413 tracks it.',
    'finding': 'In a §PROOF counterexample the reads inside the proof condition count as its evaluation. A getter there that writes state the same condition reads later, or a property that hides an inherited field (ContractTranslator models it as the field, on every channel), can make the SAT model an unreachable state: a spurious refutation (Failed, a compile error). Never a false Discharged: facts use the global heap rule.',
    'affects': ['B1', 'N1', 'candidate with R-OBL'],
    'observed': False,
    'disposition': DEMOTE, 'repair': 'R-OBL-RESIDUALS',
    'reason': 'Visibly demoted (counterexample withheld: Unsupported, guard kept) by R-OBL-RESIDUALS (#1503) under contract amendment 1.3.0, which raises the S2 repair cap to 7 for these two discoveries only (maintainer decision Q7, 2026-10-04).',
    },
    {
    'id': 'D-OBL-THROWING-PREDECESSOR', 'issue': 1413, 'registered': False,
    'source': 'Residual documented by R-OBL since review round 1 (FactCollector.IsExact) and confirmed by its verification passes (static); not part of the registered #1311 sweep. No separate issue is filed; #1413 tracks it.',
    'finding': 'Obligation exactness does not track an earlier statement that throws implicitly (checked overflow, a call, an earlier guard), so a SAT model that such a statement would stop can be reported as a counterexample: a spurious refutation (Failed, a compile error). Pre-existing on B1/N1. Never a false Discharged.',
    'affects': ['B1', 'N1', 'candidate with R-OBL'],
    'observed': False,
    'disposition': DEMOTE, 'repair': 'R-OBL-RESIDUALS',
    'reason': 'Visibly demoted (counterexample withheld: Unsupported, guard kept) by R-OBL-RESIDUALS (#1503) under contract amendment 1.3.0, which raises the S2 repair cap to 7 for these two discoveries only (maintainer decision Q7, 2026-10-04).',
    },
    {
    'id': 'D-1493', 'issue': 1493, 'registered': False,
    'source': 'Discovery by code reading during #1135 (G3, PR #1492); not part of the registered #1311 sweep and not executed by S1.',
    'finding': 'Z3 symbol names take the .NET binding\'s ANSI marshaling; on Windows two non-ASCII identifiers outside the code page can become one Z3 constant, a possible false proof (Windows only).',
    'affects': ['B1', 'N1', 'main at 0142438f'],
    'observed': False,
    'disposition': FIX, 'repair': 'R-TEXT',
    'reason': 'Fixed by the injective ASCII symbol encoding in R-TEXT; the end-to-end witness runs on Windows in the z3-consumer-matrix job.',
    },
]

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
    'requiredDiscoveries': [1493],
    'closure': {
        'status': 'OPEN',
        'result': None,
        'note': 'OPEN until every repair is merged (status "merged" with its merge commit on main, being the GitHub merge of its PR from its S2 branch, and containing its regression witnesses), amendment 1.3.0 (capacity allowance and the R-OBL review overrun) is in the contract (merged as ffa75e8b; contract version 1.3.0). The discovery ids are pinned by the validator tests, so a discovery is resolved by a repair or amendment, never deleted. At closure, result is SUCCESS (no MILESTONE-FAILED anywhere) or MILESTONE-FAILED (required if any finding or discovery is MILESTONE-FAILED); only SUCCESS satisfies terminal predicates 4-5.',
    },
    'capacity': {'ceiling': 's2-repairs', 'maxRepairs': 6, 'maxNonTestChangedLinesPerRepair': 600,
                 'amendmentAllowance': {'amendment': '1.3.0', 'extraRepairs': 1, 'repair': 'R-OBL-RESIDUALS',
                                        'discoveries': ['D-OBL-PROOF-GETTER', 'D-OBL-THROWING-PREDECESSOR']},
                 'used': len(repairs), 'openedRepairPRs': len(repairs), 'reservedSlots': 0,
                 'note': 'Seven repair PRs: six within the frozen ceiling and one (R-OBL-RESIDUALS) under amendment 1.3.0, for the two review-found discoveries only. The validator accepts the seventh while the record is open and requires the amendment merged (contract version >= 1.3.0) at closure. nonTestChangedLines excludes committed review records.'},
    'validatorLimitations': [
        'Repair acceptance is bound mechanically only to: the pinned PR number of each opened repair (tests), an S2 branch, a merge commit on main that is GitHub\'s merge of that PR from that branch, and the regression witnesses present in it. Whether the merged contents match the reviewed repair and its affected findings is the maintainer\'s merge review, not validated here.',
        'A review-round overrun names its amendment version (overrunAmendment); at closure the contract version must be at least that version. That the amendment text actually covers the overrun is the maintainer\'s review.',
        'The R0 terminal validator does not read this record (Q6).',
    ],
    'repairs': repairs,
    'baselines': baselines,
    'discoveryFindings': discoveries,
    'maintainerDecisions': [
        {'id': 'Q1-R-NUM', 'date': '2026-10-04', 'decision': 'Open the sixth repair PR (R-NUM, #1502): demote per the frozen table exactly, update the 19 tests, regenerate the #1135 reports.'},
        {'id': 'Q2-O2-GEN-REFUSAL', 'date': '2026-10-04', 'decision': 'Keep GEN-REFUSAL VALIDATED; the partial O2 replay of GEN-REFUSAL-002/-004 is recorded as a coverage limitation of the frozen generator.'},
        {'id': 'Q3-N1-ADVISORY', 'date': '2026-10-04', 'decision': 'Adopted: baselines.N1.artifact.releaseNotesAdvisoryDraft is published with the first release that carries the repairs (CHANGELOG/release notes at release time).'},
        {'id': 'Q4-ISSUE-LINKS', 'date': '2026-10-04', 'decision': 'Each repair PR body states that it blocks #1423; agents do not edit issues.'},
        {'id': 'Q5-MERGE-ORDER', 'date': '2026-10-04', 'decision': 'As proposed: #1494 and #1495 first, then #1497 (retargeted to main after #1495) and #1498; #1496, then #1503 after amendment 1.3.0; #1502 independent. Cache-format numbers and manifest/CHANGELOG resolve at merge.'},
        {'id': 'Q6-TERMINAL-BINDING', 'date': '2026-10-04', 'decision': 'Noted: closure.result is carried into the terminal adjudication by hand.'},
        {'id': 'Q7-REVIEW-DISCOVERIES', 'date': '2026-10-04', 'decision': 'Amendment 1.3.0 raises the S2 repair cap 6 -> 7 for D-OBL-PROOF-GETTER and D-OBL-THROWING-PREDECESSOR only; both are demoted visibly in R-OBL-RESIDUALS (#1503), which merges after the amendment.'},
        {'id': 'Q8-R-OBL-REVIEW-OVERRUN', 'date': '2026-10-04', 'decision': 'Amendment 1.3.0 records the overrun; one final verification-only pass on the last fix (APPROVE, reviews/fix-obligation-state/verification-3-codex.md); #1496 merges only after the amendment.'},
    ],
}
open('docs/plans/evidence/s2-1413/dispositions.json', 'w').write(json.dumps(record, indent=2, ensure_ascii=False) + '\n')
print('written', sum(len(baselines[b]['findings']) for b in baselines), 'findings,',
      sum(len(baselines[b]['rows']) for b in baselines), 'rows')
