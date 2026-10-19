"""Generate (or with --check, verify) candidate-manifest.json for the 0.25 release candidate (#1430).

The 0.25 form of the 0.24 C1 generator (docs/plans/evidence/c1-1423/generate_candidate_manifest.py), which
stays frozen with the 0.24 candidate. Run from the repository root in a full clone after `git fetch origin`.
Values come from git objects at the candidate, never the working tree. Hand-recorded inputs (the candidate,
merge commits, issue states, the observed toolchain, decisions, open items) come from inputs.json in this
directory and are re-verified against git where git can verify them. Any failed check exits non-zero.

  python3 docs/plans/evidence/v0.25-candidate/generate_candidate_manifest.py           # write
  python3 docs/plans/evidence/v0.25-candidate/generate_candidate_manifest.py --check   # verify
  python3 docs/plans/evidence/v0.25-candidate/generate_candidate_manifest.py --self-test
"""
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
# Release workflows whose setup-dotnet steps must request exactly the release SDK (#1548).
RELEASE_WORKFLOWS = ['publish-nuget.yml', 'verify-release.yml', 'reproducible-builds.yml', 'benchmark.yml']
# Pinned seals in the candidate's own tests: the manifest records them and checks the packets against them.
PINNED = {'scopeFrozenSeal': ('tests/Calor.Compiler.Tests/InteropScope/InteropScopeTests.cs', 'FrozenSeal'),
          'd1FrozenSeal': ('tests/Calor.Compiler.Tests/InteropScope/RefOutContractTests.cs', 'FrozenSeal'),
          't1BaselineApprovalSeal': ('tests/Calor.Compiler.Tests/InteropScope/T1RegisterTests.cs', 'BaselineApprovalSeal')}
FAILURES = []


def git(*args, binary=False, env_utc=False):
    env = dict(os.environ, TZ='UTC') if env_utc else None
    out = subprocess.run(['git', *args], capture_output=True, check=False, env=env)
    if out.returncode != 0:
        return None
    return out.stdout if binary else out.stdout.decode().strip()


def check(ok, message):
    if not ok:
        FAILURES.append(message)
    return ok


def sha(data, lf=False):
    if lf:
        data = data.decode('utf-8').replace('\r\n', '\n').encode('utf-8')
    return hashlib.sha256(data).hexdigest()


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


def self_test():
    """The 13 tag-rule controls of the 0.24 generator, run on every invocation."""
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
                'durableIdentity': {'treeHashes': trees, 'inputContentHashes': self.hashes(PIN_FILES)}}

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
            text = self.blob(f'.github/workflows/{wf}').decode()
            versions = re.findall(r"dotnet-version:\s*['\"]?([0-9][^'\"\s]*)", text)
            check(versions and versions[0] == sdk and all(re.fullmatch(r'\d+\.\d+\.\d{3}', v) for v in versions),
                  f'{wf} does not pin the release SDK {sdk} exactly: {versions}')
            pins[wf] = versions
        check(re.fullmatch(r'sha256:[0-9a-f]{64}', observed['dotnetSdkBuild']['value']) is not None, 'dotnetSdkBuild is not a sha256 fingerprint')
        consumers = self.json('eng/z3-consumers.json')
        parse = lambda path: [{'name': p[1], 'sha256': p[0], 'bytes': int(p[2])} for p in
                              (l.split() for l in self.blob(path).decode().splitlines() if not l.startswith('#')) if len(p) == 3]
        return {'globalJson': json.loads(self.blob('global.json'))['sdk'], 'releaseSdk': sdk, 'releaseWorkflowPins': pins,
                'dotnetSdkBuild': observed['dotnetSdkBuild'], 'runtime': observed['runtime'], 'observedEnvironment': observed.get('environment'),
                'z3': {'version': consumers.get('z3Version'), 'assets': parse('.github/z3-binaries-4.15.7.sha256'),
                       'upstreamArchives': parse('src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256')}}

    # ---- the 0.25 packets and their seals ----

    def pinned(self):
        found = {}
        for key, (path, name) in PINNED.items():
            m = re.search(rf'{name}\s*=\s*"([0-9a-f]{{64}})"', self.blob(path).decode())
            check(m is not None, f'{path} pins no {name}')
            found[key] = m.group(1) if m else None
        return found

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
        lines += [f'results {res["path"]} {res["sha256"]}', f'corpus {cc.get("source")} {json.dumps(cc.get("counts"), separators=(",", ":"))}']
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
                'pinnedSeals': self.pinned()}

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
        rows = []
        for g in self.inputs['gates']:
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

    def build(self):
        identity = self.identity()
        packets = self.packets()
        return {
            'schema': 'calor.release-candidate-manifest/1', 'release': self.inputs['candidate']['version'], 'issue': 1430, 'epic': 1425,
            'status': 'FROZEN-AT-MERGE', 'statusNote': self.inputs['notes']['statusNote'],
            'candidate': identity, 'releasability': self.releasability(), 'dependencies': self.dependencies(),
            'toolchain': self.toolchain(), 'packets': packets, 's2Witnesses': self.s2_witnesses(), 'merged': self.merged(),
            'issueStatesAtFreeze': self.inputs['issueStatesAtFreeze'], 'maintainerDecisions': self.inputs['maintainerDecisions'],
            'knownOpenItems': self.inputs['openItems'], 'invalidation': self.inputs['invalidation'], 'review': self.inputs['notes']['review'],
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
