"""Generate (or with --check, verify; --self-test, run only the controls) candidate-manifest.json for the 0.25
release candidate (#1430): the 0.25 form of 0.24's C1 generator, which stays frozen with 0.24. Run from the
repository root in a full clone after `git fetch origin`. Values come from git objects at the candidate; the
hand-recorded inputs.json is re-verified against git where git can. Any failed check exits non-zero. See README.md."""
import functools, hashlib, json, os, re, subprocess, sys

DIR = 'docs/plans/evidence/v0.25-candidate'
INPUTS_PATH, MANIFEST = f'{DIR}/inputs.json', f'{DIR}/candidate-manifest.json'
MAIN = 'refs/remotes/origin/main'
SCOPE = 'docs/plans/evidence/v0.25-r0-1426/scope.json'
REGISTER = 'docs/plans/evidence/v0.25-t1-1428/register.json'
D1 = 'docs/plans/evidence/v0.25-d1-1427/contract.json'
W1 = 'docs/plans/evidence/v0.25-w1-1429/records.json'
S2 = 'docs/plans/evidence/s2-1413/dispositions.json'
MAP = 'eng/release-adjudication-map.json'
TREES = ['src', 'tests', 'tools', 'bench', 'eng', 'scripts', '.github', '.claude', 'website', 'samples', 'docs']
PIN_FILES = ['global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
             '.github/z3-binaries-4.15.7.sha256', 'src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256',
             'eng/z3-consumers.json', 'eng/test-manifest.json', 'eng/tier2-fixture-expectations.json',
             '.github/actions/bootstrap-z3/action.yml', 'src/Calor.Compiler/scripts/download-z3.sh',
             'src/Calor.Compiler/scripts/download-z3.ps1', 'scripts/verify-z3-assets.py', 'website/package.json',
             'website/package-lock.json', 'website/src/fonts/fonts.json', '.gitmodules', '.gitattributes',
             'bench/phase0-agent-native/commit-stamp-index.json', MAP]
# Release workflows whose setup-dotnet steps must request exactly the release SDK (#1548, ReleaseSdkPinTests): each
# job's first install is the release SDK; a later install may only be the registered auxiliary SDK.
RELEASE_WORKFLOWS = ['publish-nuget.yml', 'verify-release.yml', 'reproducible-builds.yml', 'benchmark.yml']
AUX_SDKS = {'8.0.426'}
# Pinned seals in the candidate's own tests. Each is recomputed here with the validators' serialization.
PINNED_TESTS = {'scope': 'tests/Calor.Compiler.Tests/InteropScope/InteropScopeTests.cs',
                'd1': 'tests/Calor.Compiler.Tests/InteropScope/RefOutContractTests.cs',
                't1': 'tests/Calor.Compiler.Tests/InteropScope/T1RegisterTests.cs'}
# Every 0.25 gate the candidate must contain, by issue; inputs.json gives each one's merged PRs.
REQUIRED_GATES = {'R0': 1426, 'D1': 1427, 'F1': 943, 'F2': 906, 'F3': 847, 'F4': 1139, 'F5': 1132, 'F6': 1144,
                  'W0': 1143, 'T1': 1428, 'W1': 1429, 'R2-1545': 1545, 'R2-1546': 1546, 'R2-1547': 1547, 'PREP': 1430}
# Evidence producers: (id, title, directory or None, extra files).
PRODUCERS = [('r0-reproduce', '0.25 R0 reproduction driver and execution oracle (#1426)', 'docs/plans/evidence/v0.25-r0-1426/reproduce/', []),
             ('family-reproduce', 'F1, F3, F4, F5 re-run drivers', None,
              ['docs/plans/evidence/v0.25-f1-943/reproduce/reproduce.py', 'docs/plans/evidence/v0.25-f3-847/reproduce/reproduce.py',
               'docs/plans/evidence/v0.25-f4-1139/reproduce/reproduce.py', 'docs/plans/evidence/v0.25-f5-1132/reproduce/reproduce.py',
               'docs/plans/evidence/v0.25-f5-1132/reproduce/reproduce_pr2.py']),
             ('t1-aggregate', 'T1 register aggregator (#1428)', 'docs/plans/evidence/v0.25-t1-1428/reproduce/', []),
             ('w1-wording-scan', 'W1 wording scan (#1429)', 'docs/plans/evidence/v0.25-w1-1429/reproduce/', []),
             ('g2-determinism', '0.24 G2 determinism protocol (#1421), re-run on the candidate', None,
              ['scripts/determinism_protocol.py', 'scripts/determinism_runner.py', 'docs/plans/evidence/g2-1421/protocol.json',
               'docs/plans/evidence/g2-1421/cases.json']),
             ('r2-release-gate', '0.24 R2 release gate (#1410) and the 0.24.0 override check', None,
              ['scripts/verify_release_adjudication.py', 'scripts/render_release_notes.py', 'scripts/verify_maintainer_override.py',
               'scripts/check_release_credentials.py', 'scripts/test_reproducible_builds.py']),
             ('candidate-generator', 'this generator', None, [f'{DIR}/generate_candidate_manifest.py'])]
FAILURES = []

def dotnet_json(value):
    """JsonNode.ToJsonString() with System.Text.Json's default encoder: compact, every non-ASCII, control and
    HTML-sensitive character escaped as uppercase \\uXXXX, and numbers written as their original source text."""
    if isinstance(value, dict):
        return '{' + ','.join(dotnet_json(k) + ':' + dotnet_json(v) for k, v in value.items()) + '}'
    if isinstance(value, list):
        return '[' + ','.join(dotnet_json(v) for v in value) + ']'
    if isinstance(value, RawNumber):
        return str(value)
    if value is None or isinstance(value, bool):
        return {None: 'null', True: 'true', False: 'false'}[value]
    out = []
    for ch in value:
        o = ord(ch)
        if ch == '\\':
            out.append('\\\\')
        elif ch in '\n\r\t\b\f':
            out.append({'\n': '\\n', '\r': '\\r', '\t': '\\t', '\b': '\\b', '\f': '\\f'}[ch])
        elif o < 0x20 or o > 0x7E or ch in '"&\'+<>`':
            for unit in ([o] if o <= 0xFFFF else [0xD800 + ((o - 0x10000) >> 10), 0xDC00 + ((o - 0x10000) & 0x3FF)]):
                out.append(f'\\u{unit:04X}')
        else:
            out.append(ch)
    return '"' + ''.join(out) + '"'

class RawNumber(str):
    """A JSON number kept as its source text, as JsonNode keeps it."""

def jloads(data):
    return json.loads(data, parse_int=RawNumber, parse_float=RawNumber)

def git(*args, binary=False, env_utc=False):
    out = subprocess.run(['git', *args], capture_output=True, check=False, env=dict(os.environ, TZ='UTC') if env_utc else None)
    return None if out.returncode != 0 else out.stdout if binary else out.stdout.decode().strip()

def check(ok, message):
    if not ok:
        FAILURES.append(message)
    return ok

def sha(data, lf=False):
    return hashlib.sha256(data.decode('utf-8').replace('\r\n', '\n').encode('utf-8') if lf else data).hexdigest()

def ancestor(commit, of):
    return subprocess.run(['git', 'merge-base', '--is-ancestor', commit, of], capture_output=True).returncode == 0

def tag_problems(ref, local_rc, local_commit, remote_rc, remote_out, candidate):
    """Local and remote are judged independently: each must be a confirmed absence or peel to the candidate."""
    problems = []
    if local_rc not in (0, 1) or (local_rc == 0 and local_commit is None):
        problems.append(f'cannot decide whether local {ref} exists or what it peels to')
    elif local_rc == 0 and local_commit != candidate:
        problems.append(f'local {ref} peels to {local_commit}, not the candidate (R2 gate G009)')
    found = {name: s for s, name in (l.split('\t', 1) for l in remote_out.splitlines() if '\t' in l)}
    peeled = found.get(ref + '^{}', found.get(ref))
    if (remote_rc != 0 or set(found) - {ref, ref + '^{}'} or len(remote_out.strip().splitlines()) != len(found)
            or not all(re.fullmatch('[0-9a-f]{40}', v) for v in found.values()) or (ref + '^{}' in found and ref not in found)):
        problems.append(f'cannot decide whether remote {ref} exists')
    elif found and peeled != candidate:
        problems.append(f'remote {ref} peels to {peeled}, not the candidate (R2 gate G009)')
    return problems

def sdk_pins(wf, text, sdk):
    """Per job: the setup-dotnet versions, and problems unless the first is the release SDK and later ones auxiliary."""
    problems, pins = [], {}
    if 'global-json-file' in text:
        problems.append(f'{wf} selects an SDK through global-json-file')
    jobs = re.split(r'(?m)^  ([A-Za-z0-9_-]+):[ \t]*$', text.split('\njobs:', 1)[-1])
    for name, body in zip(jobs[1::2], jobs[2::2]):
        versions = [v.strip('\'"') for v in re.findall(r'dotnet-version:[ \t]*(\S+)', body)]
        if versions:
            pins[f'{wf}:{name}'] = versions
            if versions[0] != sdk or any(v != sdk and v not in AUX_SDKS for v in versions[1:]):
                problems.append(f'{wf} job {name} does not install exactly the release SDK {sdk} first '
                                f'(and only {sorted(AUX_SDKS)} after): {versions}')
    if not pins:
        problems.append(f'{wf} has no setup-dotnet job')
    return pins, problems

def self_test():
    """The 13 tag-rule controls of the 0.24 generator, plus serializer, seal and SDK-pin controls; run every time."""
    check(dotnet_json(jloads('{"a":["§<\'&+`\\"\\\\",1.50,-2,true,null]}')) ==
          '{"a":["\\u00A7\\u003C\\u0027\\u0026\\u002B\\u0060\\u0022\\\\",1.50,-2,true,null]}', 'dotnet_json self-test failed')
    check(dotnet_json('\U0001F600\n') == '"\\uD83D\\uDE00\\n"', 'dotnet_json surrogate self-test failed')
    wf = "on: push\njobs:\n  a:\n    steps:\n      - with:\n          dotnet-version: '10.0.401'\n      - with:\n          dotnet-version: '8.0.426'\n" \
         "  b:\n    steps:\n      - with:\n          dotnet-version: '10.0.401'\n"
    check(not sdk_pins('w', wf, '10.0.401')[1], 'SDK-pin control: a valid workflow is rejected')
    for bad in (wf.replace("'8.0.426'", "'10.0.999'"),
                wf.replace("b:\n    steps:\n      - with:\n          dotnet-version: '10.0.401'",
                           "b:\n    steps:\n      - with:\n          dotnet-version: '10.0.x'"),
                wf.replace("a:\n    steps:\n      - with:\n          dotnet-version: '10.0.401'", "a:\n    steps:\n      - with:\n          dotnet-version: '8.0.426'"),
                wf + "      - with:\n          global-json-file: global.json\n"):
        check(bool(sdk_pins('w', bad, '10.0.401')[1]), f'SDK-pin control: a mutated workflow passes: {bad!r}')
    c, o, r = 'c' * 40, 'a' * 40, 'refs/tags/v9.9.9'
    cases = [((0, c, 0, ''), 0), ((1, None, 0, ''), 0), ((0, c, 0, f'{o}\t{r}\n'), 1), ((1, None, 0, f'{o}\t{r}\n{c}\t{r}^{{}}\n'), 0),
             ((1, None, 0, f'{c}\t{r}\n{o}\t{r}^{{}}\n'), 1), ((1, None, 0, f'{c}\t{r}\n'), 0), ((0, o, 0, ''), 1),
             ((0, None, 0, ''), 1), ((128, None, 0, ''), 1), ((1, None, 128, ''), 1), ((1, None, 0, 'garbage\n'), 1),
             ((1, None, 0, f'nothex\t{r}\n{c}\t{r}^{{}}\n'), 1), ((1, None, 0, f'{c}\t{r}^{{}}\n'), 1)]
    for args, expected in cases:
        check(bool(tag_problems(r, *args, candidate=c)) == bool(expected), f'tag self-test failed for {args}')

class Candidate:
    def __init__(self, inputs):
        self.inputs = inputs
        self.commit = inputs['candidate']['commit']

    @functools.lru_cache(maxsize=None)
    def blob(self, path):
        data = git('show', f'{self.commit}:{path}', binary=True)
        check(data is not None, f'{path} is absent at the candidate')
        return data

    def json(self, path):
        data = self.blob(path)
        return json.loads(data) if data is not None else {}

    def files(self, prefix):
        return sorted(p for p in (git('ls-tree', '-r', '--name-only', self.commit, '--', prefix) or '').splitlines() if p)

    def hashes(self, paths, lf=False):
        return {p: sha(self.blob(p), lf) for p in paths if self.blob(p) is not None}
    # ---- identity and releasability ----
    def identity(self):
        c, cand = self.commit, self.inputs['candidate']
        check(re.fullmatch('[0-9a-f]{40}', c or '') is not None, 'candidate.commit is not a full SHA')
        check(git('rev-parse', '--is-shallow-repository') == 'false', 'clone is shallow or not a git repository')
        main_tip = git('rev-parse', '--verify', '--quiet', MAIN + '^{commit}')
        check(main_tip is not None and ancestor(c, main_tip), f'candidate is not an ancestor of fetched {MAIN}')
        pr = cand['mergeOf']['pr']
        subject = git('log', '-1', '--format=%s', c) or ''
        check(subject.startswith(f'Merge pull request #{pr} from '), f'candidate is not GitHub\'s merge of PR #{pr}')
        first_parent = git('rev-parse', c + '^1')
        check(first_parent is not None and ancestor(first_parent, main_tip or c) and
              c in (git('rev-list', '--first-parent', main_tip or c) or '').split(), 'candidate is not on the first-parent chain of main')
        trees = {'/': git('rev-parse', c + '^{tree}')}
        trees.update({t: git('rev-parse', f'{c}:{t}') for t in TREES})
        return {'commit': c, 'committedUtc': git('show', '-s', '--date=iso-strict-local', '--format=%cd', c, env_utc=True),
                'mergeOf': cand['mergeOf'], 'firstParent': first_parent, 'freezeObservedUtc': cand['freezeObservedUtc'],
                'originMainAtFreeze': cand['originMainAtFreeze'],
                'durableIdentity': {'status': 'complete', 'commit': c, 'resolvedVia': MAIN, 'treeHashes': trees,
                                    'inputContentHashes': self.hashes(PIN_FILES)}}

    def releasability(self):
        version = re.search(rb'<Version>([^<]+)</Version>', self.blob('Directory.Build.props')).group(1).decode()
        check(version == self.inputs['candidate']['version'], f'candidate declares {version}, not the decided release version')
        ref = f'refs/tags/v{version}'
        local = subprocess.run(['git', 'show-ref', '--verify', '--quiet', ref]).returncode
        tagged = git('rev-parse', '--verify', '--quiet', ref + '^{commit}') if local == 0 else None
        remote = subprocess.run(['git', 'ls-remote', '--tags', 'origin', ref, ref + '^{}'], capture_output=True, text=True)
        for problem in tag_problems(ref, local, tagged, remote.returncode, remote.stdout, self.commit):
            check(False, problem)
        changelog, site = self.blob('CHANGELOG.md').decode(), self.blob('website/content/changelog.mdx').decode()
        section = lambda text: (re.search(rf'^## \[{re.escape(version)}\][\s\S]*?(?=^## \[)', text, re.M) or [None])[0]
        notes = section(changelog)
        check(notes is not None and notes == section(site), 'the CHANGELOG.md and changelog.mdx release sections differ or are missing')
        check(re.search(r'independently\s+(reviewed|verified|adjudicated)', notes or '', re.I) is None, 'notes claim independent review')
        entry = [r for r in self.json(MAP).get('releases', []) if r.get('version') == '.'.join(version.split('.')[:2]) + '.x']
        check(len(entry) == 1 and entry[0].get('status') == 'not-yet-adjudicated' and entry[0].get('a1Issue') == 1430,
              f'{MAP} has no single not-yet-adjudicated 0.25.x entry for #1430')
        return {'versionAtCandidate': version, 'tagAtFreeze': tagged, 'notesSectionSha256': sha((notes or '').encode()),
                'adjudicationMapEntry': entry[0] if entry else None}
    # ---- pins, dependencies, toolchain ----
    def dependencies(self):
        everything = (git('ls-tree', '-r', '--name-only', self.commit) or '').splitlines()
        locks = [p for p in everything if p.endswith(('packages.lock.json', 'Directory.Packages.props', 'dotnet-tools.json'))
                 or p.lower().endswith('nuget.config')]
        gitlinks = [dict(zip(('path', 'commit'), (l.split(None, 3)[3], l.split()[2])))
                    for l in (git('ls-tree', '-r', self.commit) or '').splitlines() if l.split()[1] == 'commit']
        check(len(gitlinks) == 3, f'expected 3 gitlinks (the corpus submodules), found {len(gitlinks)}')
        return {'submodules': gitlinks, 'lockfiles': self.hashes(sorted(locks)),
                'node': self.hashes(['website/package.json', 'website/package-lock.json'])}

    def toolchain(self):
        observed = self.inputs['toolchain']
        sdk = observed['releaseSdk']
        pins = {}
        for wf in RELEASE_WORKFLOWS:
            found, problems = sdk_pins(wf, self.blob(f'.github/workflows/{wf}').decode(), sdk)
            pins.update(found)
            for p in problems:
                check(False, p)
        check(re.fullmatch(r'sha256:[0-9a-f]{64}', observed['dotnetSdkBuild']['value']) is not None, 'dotnetSdkBuild is not a sha256 fingerprint')
        consumers = self.json('eng/z3-consumers.json')
        parse = lambda path: [{'name': p[1], 'sha256': p[0], 'bytes': int(p[2])} for p in
                              (l.split() for l in self.blob(path).decode().splitlines() if not l.startswith('#')) if len(p) == 3]
        protocol = self.json('docs/plans/evidence/g2-1421/protocol.json')
        return {'globalJson': json.loads(self.blob('global.json'))['sdk'], 'releaseSdk': sdk, 'releaseWorkflowPins': pins,
                'dotnetSdkBuild': observed['dotnetSdkBuild'], 'runtime': observed['runtime'], 'observedEnvironment': observed.get('environment'),
                'determinismProtocol': {'version': protocol.get('protocolVersion'), 'environments': protocol.get('environments'),
                                        'environmentRule': protocol.get('environmentRule'), 'checkout': protocol.get('checkout'),
                                        **{k: protocol.get('toolchain', {}).get(k) for k in ('sdk', 'runtime', 'globalJson', 'configuration')},
                                        'releaseEpoch': protocol.get('budget', {}).get('releaseEpoch')},
                'seeds': [{'id': 'z3-random-seed', **{k: protocol.get('z3', {}).get('randomSeed', {}).get(k) for k in ('value', 'source', 'applied')},
                           'note': 'G2 protocol 1.2.0: Z3 4.15.7 ignores random_seed, so the constant never reaches the solver.'},
                          {'id': '0.25-producers', 'value': None,
                           'note': 'The 0.25 reproduction drivers and the T1 aggregator take no seed; their inputs are fixed fixtures.'}],
                'z3': {'version': consumers.get('z3Version'), 'assets': parse('.github/z3-binaries-4.15.7.sha256'),
                       'upstreamArchives': parse('src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256')}}
    # ---- the 0.25 packets and their seals ----
    def pin(self, which, name):
        m = re.search(rf'{name}\s*=\s*"([0-9a-f]{{64}})"', self.blob(PINNED_TESTS[which]).decode())
        check(m is not None, f'{PINNED_TESTS[which]} pins no {name}')
        return m.group(1) if m else None

    def seals(self):
        """Recompute each packet seal exactly as its C# validator does, and compare it with the test pin."""
        scope, d1, reg = jloads(self.blob(SCOPE)), jloads(self.blob(D1)), jloads(self.blob(REGISTER))
        def scope_seal_of(s):
            es = s.get('evidenceSeal', {})
            return sha('\n'.join([dotnet_json(s.get('denominatorV1')), es.get('baselineResults', ''), es.get('generated', ''),
                                  dotnet_json(es.get('producers'))]).encode(), True)

        def d1_seal_of(c):
            frozen = {k: c.get(k) for k in ('measuredAt', 'consumes', 'rules', 'preludes', 'requiredShapes', 'diagnostics', 'cases')}
            frozen['diagnostics'] = [{k: v for k, v in d.items() if k != 'code'} for d in frozen['diagnostics'] or []]
            frozen['cases'] = [{k: v for k, v in x.items() if k not in ('observed', 'status')} for x in frozen['cases'] or []]
            return sha(dotnet_json(frozen).encode())

        scope_seal, d1_seal = scope_seal_of(scope), d1_seal_of(d1)
        # Controls on the real packets: a sealed field must move its seal; an unsealed one must not.
        for label, mutated_seal, base, sealed in (
                ('scope denominator emptied', scope_seal_of(dict(scope, denominatorV1=[])), scope_seal, True),
                ('D1 rules emptied', d1_seal_of(dict(d1, rules=[])), d1_seal, True),
                ('D1 case status changed', d1_seal_of(dict(d1, cases=[dict(c, status='x') for c in d1.get('cases', [])])), d1_seal, False)):
            check((mutated_seal != base) == sealed, f'seal control failed: {label}')
        pinned_amendments = re.findall(r'\("([0-9.]+)",\s*"([0-9a-f]{64})"\)', self.blob(PINNED_TESTS['t1']).decode())
        recorded = [(a.get('version'), a.get('approvalSeal')) for a in reg.get('amendments', [])]
        out = {'scope': {'computed': scope_seal, 'pinned': self.pin('scope', 'FrozenSeal')},
               'd1Contract': {'computed': d1_seal, 'pinned': self.pin('d1', 'FrozenSeal')},
               't1Baseline': {'recorded': reg.get('approvalSeal'), 'pinned': self.pin('t1', 'BaselineApprovalSeal')},
               't1Amendments': {'recorded': recorded, 'pinned': pinned_amendments}}
        check(scope_seal == out['scope']['pinned'], f'scope seal {scope_seal} differs from the pinned FrozenSeal')
        check(d1_seal == out['d1Contract']['pinned'], f'D1 contract seal {d1_seal} differs from the pinned FrozenSeal')
        check(reg.get('approvalSeal') == out['t1Baseline']['pinned'], 'T1 baseline approval seal differs from its pin')
        check([list(r) for r in recorded] == [list(p) for p in pinned_amendments], 'T1 amendment approval seals differ from their pins')
        return out

    def scope(self):
        s = self.json(SCOPE)
        check(s.get('status') == 'FROZEN' and s.get('gateStatus') == 'MET', 'scope is not FROZEN/MET')
        check(s.get('scopeVersion') == s.get('amendments', [{}])[-1].get('version'), 'scopeVersion is not the latest amendment')
        seal, root = s.get('evidenceSeal', {}), 'docs/plans/evidence/v0.25-r0-1426/'
        check(seal.get('baselineResults') == sha(self.blob(root + 'baseline-results.json'), True), 'baseline-results.json differs from evidenceSeal')
        for producer, want in seal.get('producers', {}).items():
            check(sha(self.blob(producer), True) == want, f'{producer} differs from evidenceSeal')
        lines = sorted(f'{p} {sha(self.blob(p), True)}' for p in self.files(root + 'generated/'))
        check(seal.get('generated') == sha('\n'.join(lines).encode()), 'generated/ differs from evidenceSeal')
        pre = s.get('acceptancePreconditions', {})
        check(pre.get('reproof', {}).get('onCandidate') == 'own-sha' and [g['issue'] for g in pre.get('releaseGaps', [])] == [1545, 1546, 1547],
              'acceptancePreconditions do not require an own-SHA re-proof and the three release gaps')
        raw = self.blob(SCOPE)
        return {'path': SCOPE, 'sha256': sha(raw), 'sha256LF': sha(raw, True), 'scopeVersion': s.get('scopeVersion'),
                'status': s.get('status'), 'gateStatus': s.get('gateStatus'), 'evidenceSeal': seal, 'evidenceSealVerified': True,
                'amendments': [{k: a.get(k) for k in ('version', 'dateUtc', 'pr', 'afterInspection')} for a in s.get('amendments', [])],
                'readiness': {f['id']: f.get('readiness', {}).get('status') for f in s.get('families', [])},
                'acceptancePreconditions': pre}

    def register(self, scope_version):
        r = self.json(REGISTER)
        check(r.get('consumes', {}).get('scopeVersion') == scope_version, 'T1 register consumes another scope version')
        lines = []
        for f in r.get('families', []):
            for e in f.get('evidence', []):
                check(sha(self.blob(e['path']), True) == e['sha256'], f'T1 evidence {e["path"]} differs from its approved hash')
                lines.append(f'evidence {f["id"]} {e["path"]} {e["sha256"]}')
        for c in r.get('controls', []):
            check(sha(self.blob(c['fixture']), True) == c['fixtureSha256'], f'T1 control fixture {c["fixture"]} differs')
            lines.append(f'fixture {c["id"]} {c["fixture"]} {c["fixtureSha256"]}')
        res, cc = r.get('results', {}), r.get('corpusCounts', {})
        check(sha(self.blob(res['path']), True) == res['sha256'], 'T1 controls-results.json differs from its approved hash')
        corpus = f'corpus {cc.get("source")} {json.dumps(cc.get("counts"), separators=(",", ":"))}'
        if 'reRegistration' in cc:  # T1 register 1.0.3 (D-F2-INTERP-09-RECANDIDATE): T1RegisterValidator.ApprovalSeal seals it too
            corpus += ' reRegistration ' + dotnet_json(jloads(self.blob(REGISTER))['corpusCounts']['reRegistration'])
        lines += [f'results {res["path"]} {res["sha256"]}', corpus]
        seal = sha('\n'.join(sorted(lines)).encode())
        approved = (r.get('amendments') or [r])[-1].get('approvalSeal')
        check(seal == approved, f'T1 approval seal {seal} differs from the approved {approved}')
        raw = self.blob(REGISTER)
        return {'path': REGISTER, 'sha256': sha(raw), 'sha256LF': sha(raw, True), 'registerVersion': r.get('registerVersion'),
                'approvalSeal': seal, 'baselineApprovalSeal': r.get('approvalSeal'),
                'readiness': {f['id']: f.get('readiness') for f in r.get('families', [])}}

    def packets(self):
        scope = self.scope()
        d1, w1 = self.json(D1), self.json(W1)
        check(w1.get('scopeVersion') == scope['scopeVersion'], 'W1 records consume another scope version')
        files = lambda p: {'path': p, 'sha256': sha(self.blob(p)), 'sha256LF': sha(self.blob(p), True)}
        return {'scope': scope, 't1Register': self.register(scope['scopeVersion']),
                'd1Contract': dict(files(D1), contractVersion=d1.get('contractVersion'), status=d1.get('status'),
                                   consumes=d1.get('consumes')),
                'w1Records': dict(files(W1), scopeVersion=w1.get('scopeVersion'), registerVersion=w1.get('registerVersion')),
                'seals': self.seals()}

    def producers(self):
        out = []
        for pid, title, directory, extra in PRODUCERS:
            paths = sorted(set((self.files(directory) if directory else []) + extra))
            check(bool(paths), f'producer {pid} has no files at the candidate')
            out.append({'id': pid, 'title': title, 'files': self.hashes(paths)})
        return out

    def s2_witnesses(self):
        """0.24 S2 repairs (acceptancePreconditions proof-s2-witnesses): present at the candidate; C2-equivalent runs them."""
        out = []
        for r in self.json(S2).get('repairs', []):
            present = all(self.blob(w) is not None for w in r['regressionWitness'])
            check(present and ancestor(r['mergeCommit'], self.commit), f'S2 repair {r["id"]} or its witnesses are missing at the candidate')
            out.append({'id': r['id'], 'pr': r['pr'], 'mergeCommit': r['mergeCommit'], 'regressionWitness': r['regressionWitness'],
                        'presentAtCandidate': present, 'run': 'not run by the freeze; the re-proof runs them on this SHA'})
        check(len(out) == 7, f'expected 7 S2 repairs, found {len(out)}')
        return out

    def merged(self):
        rows, gates = [], self.inputs['gates']
        check({g['gate']: g['issue'] for g in gates} == REQUIRED_GATES and len(gates) == len(REQUIRED_GATES),
              f'the recorded gates differ from the required 0.25 set {sorted(REQUIRED_GATES)}')
        check(all(g['prs'] for g in gates), 'a required gate records no merged PR')
        prep = next((g for g in gates if g['gate'] == 'PREP'), {})
        check(prep.get('prs', [None])[-1] == self.inputs['candidate']['mergeOf']['pr'], 'the PREP gate does not end with the candidate PR')
        for g in gates:
            prs = []
            for pr in g['prs']:
                rec = self.inputs['pullRequests'][str(pr)]
                ok = ancestor(rec['mergeCommit'], self.commit)
                subject = git('log', '-1', '--format=%s', rec['mergeCommit']) or ''
                check(ok and subject.startswith(f'Merge pull request #{pr} from '), f'{g["gate"]} PR #{pr} is not merged into the candidate as GitHub\'s merge')
                prs.append({'pr': pr, 'mergeCommit': rec['mergeCommit'], 'mergedAtUtc': rec['mergedAtUtc'], 'ancestorOfCandidate': ok})
            if g.get('evidence'):
                check(bool(self.files(g['evidence'])), f'{g["gate"]} evidence {g["evidence"]} is absent at the candidate')
            rows.append(dict(g, prs=prs, issueStateAtFreeze=self.inputs['issueStatesAtFreeze']['states'].get(str(g['issue']))))
        return rows

    def supersedes(self):
        """A re-freeze: the superseded candidate, its freeze landing, and the maintainer decision recorded at this candidate."""
        sup = self.inputs.get('supersedes')
        if sup is None:
            return None
        old, landing = sup['commit'], sup['frozenBy']['landing']
        check(re.fullmatch('[0-9a-f]{40}', old) is not None and ancestor(old, self.commit), 'the superseded candidate is not an ancestor of this one')
        frozen = git('show', f'{landing}:{MANIFEST}')
        check(frozen is not None and json.loads(frozen)['candidate']['commit'] == old and ancestor(landing, self.commit),
              'the superseded freeze landing does not hold a manifest naming the superseded candidate inside this candidate')
        subject = git('log', '-1', '--format=%s', landing) or ''
        check(subject.startswith(f"Merge pull request #{sup['frozenBy']['pr']} from "), 'the superseded freeze landing is not its PR\'s merge')
        scope = self.json(SCOPE)
        decision = [d for a in scope.get('amendments', []) for d in a.get('maintainerDecisions', [])
                    if d.get('id') == sup['decision']['id'] and d.get('supersedes') == old and d.get('decision') == sup['decision']['decision']]
        check(len(decision) == 1, f"the candidate's scope packet records no single {sup['decision']['id']} superseding {old}")
        check(sup['amendments']['mergeCommit'] == self.commit, 'the amendment PR of a re-freeze is the candidate itself')
        return sup

    def build(self):
        identity = self.identity()
        packets = self.packets()
        return {
            'schema': 'calor.release-candidate-manifest/1', 'release': self.inputs['candidate']['version'], 'issue': 1430, 'epic': 1425,
            'status': 'FROZEN-AT-MERGE', 'statusNote': self.inputs['notes']['statusNote'],
            'candidate': identity, 'releasability': self.releasability(), 'dependencies': self.dependencies(),
            'toolchain': self.toolchain(), 'packets': packets, 'producers': self.producers(),
            's2Witnesses': self.s2_witnesses(), 'merged': self.merged(),
            'issueStatesAtFreeze': self.inputs['issueStatesAtFreeze'], 'maintainerDecisions': self.inputs['maintainerDecisions'],
            'knownOpenItems': self.inputs['openItems'], 'invalidation': self.inputs['invalidation'], 'review': self.inputs['notes']['review'],
            **({'supersedes': self.supersedes()} if self.inputs.get('supersedes') else {}),
        }

def main():
    self_test()
    if '--self-test' in sys.argv:
        print('OK' if not FAILURES else '\n'.join(FAILURES))
        return 1 if FAILURES else 0
    if not os.path.exists(INPUTS_PATH):
        print(f'FAIL {INPUTS_PATH} is missing: no candidate has been frozen yet')
        return 1
    manifest = Candidate(json.load(open(INPUTS_PATH, encoding='utf-8'))).build()
    text = json.dumps(manifest, indent=2, ensure_ascii=False) + '\n'
    if '--check' in sys.argv:
        check(os.path.exists(MANIFEST) and open(MANIFEST, encoding='utf-8').read() == text, f'{MANIFEST} differs from the regenerated manifest')
    elif not FAILURES:
        open(MANIFEST, 'w', encoding='utf-8', newline='\n').write(text)
    for f in FAILURES:
        print('FAIL', f)
    print('OK' if not FAILURES else f'{len(FAILURES)} failure(s)')
    return 1 if FAILURES else 0

if __name__ == '__main__':
    sys.exit(main())
