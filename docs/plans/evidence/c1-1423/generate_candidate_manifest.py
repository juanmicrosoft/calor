"""Generate (or with --check, verify) candidate-manifest.json for 0.24 C1 (#1423). Run from the repository root in
a full clone after `git fetch origin`. Values come from git objects at the candidate; hand-recorded inputs come from
inputs.json and are verified against git. Any failed check exits non-zero."""
import functools, hashlib, json, os, re, subprocess, sys

INPUTS = json.load(open('docs/plans/evidence/c1-1423/inputs.json', encoding='utf-8'))
CANDIDATE = INPUTS['candidate']['commit']
MANIFEST = 'docs/plans/evidence/c1-1423/candidate-manifest.json'
MAIN = 'refs/remotes/origin/main'
FAILURES = []

def git(*args, binary=False, env_utc=False):
    env = dict(os.environ, TZ='UTC') if env_utc else None
    out = subprocess.run(['git', *args], capture_output=True, check=False, env=env)
    if out.returncode != 0:
        return None
    return out.stdout if binary else out.stdout.decode().strip()

@functools.lru_cache(maxsize=None)
def blob(path, commit=CANDIDATE):
    data = git('show', f'{commit}:{path}', binary=True)
    if data is None:
        FAILURES.append(f'{path} is absent at {commit}')
    return data

def sha(data, lf=False):
    if lf:
        data = data.replace(b'\r\n', b'\n')
    return hashlib.sha256(data).hexdigest()

def check(ok, message):
    if not ok:
        FAILURES.append(message)
    return ok

def ancestor(commit, of=CANDIDATE):
    return subprocess.run(['git', 'merge-base', '--is-ancestor', commit, of]).returncode == 0

def jload(path, commit=CANDIDATE):
    data = blob(path, commit)
    return json.loads(data) if data is not None else {}

PRS = {int(k): (v['mergeCommit'], v['mergedAtUtc'], v['mergedViaPr']) for k, v in INPUTS['pullRequests'].items()}
GATES = [(g['gate'], g['issue'], g['prs'], g['evidence'], g['closureStatus']) for g in INPUTS['gates']]
EXTRA = [(e['issue'], e['pr'], e['title'], e['evidence']) for e in INPUTS['additionalMerged']]
OPEN_ITEMS, N = INPUTS['openItems'], INPUTS['notes']
PIN_FILES, TREES = INPUTS['pinFiles'], INPUTS['trees']
PRODUCERS = [(p['id'], p['title'], p['version'], p['directory']) for p in INPUTS['producers']]
PRODUCER_FILES = {p['id']: p['files'] for p in INPUTS['producers']}
PACKETS = [(p['path'], p['lf']) for p in INPUTS['packets']]
ISSUE_STATES = INPUTS['issueStatesAtFreeze']['states']


def releasability():
    version = re.search(rb'<Version>([^<]+)</Version>', blob('Directory.Build.props')).group(1).decode()
    ref = f'refs/tags/v{version}'
    local = subprocess.run(['git', 'show-ref', '--verify', '--quiet', ref]).returncode   # 0 present, 1 absent, else error
    tagged = git('rev-parse', '--verify', '--quiet', ref + '^{commit}') if local == 0 else None
    remote = subprocess.run(['git', 'ls-remote', '--tags', 'origin', ref, ref + '^{}'], capture_output=True, text=True)
    for problem in tag_problems(ref, local, tagged, remote.returncode, remote.stdout):
        check(False, problem)
    check(version == INPUTS['candidate']['version'], f'candidate declares {version}, not the decided release version')
    return dict(INPUTS['releasability'], versionAtCandidate=version, tagAtFreeze=tagged)

def tag_problems(ref, local_rc, local_commit, remote_rc, remote_out, candidate=CANDIDATE):
    """Local and remote are judged independently: each must be a confirmed absence or peel to the candidate."""
    problems = []
    if local_rc not in (0, 1) or (local_rc == 0 and local_commit is None):
        problems.append(f'cannot decide whether local {ref} exists or what it peels to')
    elif local_rc == 0 and local_commit != candidate:
        problems.append(f'local {ref} peels to {local_commit}, not the candidate (R2 gate G009)')
    found = {name: sha for sha, name in (l.split('\t', 1) for l in remote_out.splitlines() if '\t' in l)}
    peeled = found.get(ref + '^{}', found.get(ref))
    if (remote_rc != 0 or set(found) - {ref, ref + '^{}'} or len(remote_out.strip().splitlines()) != len(found)
            or not all(re.fullmatch('[0-9a-f]{40}', v) for v in found.values()) or (ref + '^{}' in found and ref not in found)):
        problems.append(f'cannot decide whether remote {ref} exists')
    elif found and peeled != candidate:
        problems.append(f'remote {ref} peels to {peeled}, not the candidate (R2 gate G009)')
    return problems

def tag_self_test():
    c, o, r = 'c' * 40, 'a' * 40, 'refs/tags/v9.9.9'
    cases = [((0, c, 0, ''), 0), ((1, None, 0, ''), 0), ((0, c, 0, f'{o}\t{r}\n'), 1), ((1, None, 0, f'{o}\t{r}\n{c}\t{r}^{{}}\n'), 0),
             ((1, None, 0, f'{c}\t{r}\n{o}\t{r}^{{}}\n'), 1), ((1, None, 0, f'{c}\t{r}\n'), 0), ((0, o, 0, ''), 1),
             ((0, None, 0, ''), 1), ((128, None, 0, ''), 1), ((1, None, 128, ''), 1), ((1, None, 0, 'garbage\n'), 1),
             ((1, None, 0, f'nothex\t{r}\n{c}\t{r}^{{}}\n'), 1), ((1, None, 0, f'{c}\t{r}^{{}}\n'), 1)]
    for args, expected in cases:
        check(bool(tag_problems(r, *args, candidate=c)) == bool(expected), f'tag self-test failed for {args}')

def files_under(prefix):
    out = git('ls-tree', '-r', '--name-only', CANDIDATE, '--', prefix) or ''
    return sorted(p for p in out.splitlines() if p)

def hashes(paths):
    return {p: sha(blob(p)) for p in paths if blob(p) is not None}

def verify_packet(path, lf):
    packet = jload(path)
    bad = [p for p, want in packet.get('files', {}).items()
           if blob(p) is None or sha(blob(p), lf) != want]
    check(not bad and packet.get('files'), f'{path}: {len(bad)} file(s) do not verify at the candidate: {bad[:5]}')
    raw = blob(path)
    return {'path': path, 'sha256': sha(raw), 'sha256LF': sha(raw, True), 'normalization': 'LF' if lf else 'raw',
            'fileCount': len(packet.get('files', {})), 'verifiedAtCandidate': not bad}

def parse_pins(path):
    rows = []
    for line in blob(path).decode().splitlines():
        parts = line.split()
        if len(parts) == 3 and not line.startswith('#'):
            rows.append({'name': parts[1], 'sha256': parts[0], 'bytes': int(parts[2])})
    return rows

def build():
    shallow = git('rev-parse', '--is-shallow-repository')
    main_tip = git('rev-parse', '--verify', '--quiet', MAIN + '^{commit}')
    check(shallow == 'false', 'clone is shallow or not a git repository (contract §6: fail, never skip)')
    check(main_tip is not None, f'{MAIN} is not fetched')
    check(main_tip is not None and ancestor(CANDIDATE, main_tip), f'candidate is not an ancestor of {MAIN}')

    trees = {'/': git('rev-parse', CANDIDATE + '^{tree}')}
    trees.update({t: git('rev-parse', f'{CANDIDATE}:{t}') for t in TREES})
    lockfiles = [p for p in (git('ls-tree', '-r', '--name-only', CANDIDATE) or '').splitlines()
                 if p.endswith('packages.lock.json') or p.endswith('Directory.Packages.props')
                 or p.lower().endswith('nuget.config') or p.endswith('dotnet-tools.json')]
    submodules = []
    for line in (git('ls-tree', '-r', CANDIDATE) or '').splitlines():
        mode, kind, obj, path = line.split(None, 3)
        if kind == 'commit':
            submodules.append({'path': path, 'commit': obj})
    check(len(submodules) == 3, f'expected 3 gitlinks (the corpus submodules), found {len(submodules)}')

    contract = jload('docs/plans/evidence/evidence-contract-1407/contract.json')
    check(contract.get('contractVersion') == '1.3.2', 'contractVersion is not 1.3.2')
    check(contract.get('status') == 'FROZEN' and contract.get('gateStatus') == 'MET', 'contract not FROZEN/MET')
    pr_of_amendment = {a['version']: a['reviewedInPr'] for a in contract.get('amendmentLog', [])}
    amendments = []
    for a in contract.get('amendmentLog', []):
        pr = a['reviewedInPr']
        merge = PRS.get(pr, (None,))[0]
        check(merge is not None and ancestor(merge), f'amendment {a["version"]} PR #{pr} merge is not an ancestor')
        amendments.append({'version': a['version'], 'timestampUtc': a['timestampUtc'], 'reviewedInPr': pr,
                           'mergeCommit': merge, 'afterDecisionBearingInspection': a['afterDecisionBearingInspection']})
    check([a['version'] for a in amendments] == ['1.0.1', '1.1.0', '1.1.1', '1.2.0', '1.2.1', '1.3.0', '1.3.1', '1.3.2'],
          f'amendment versions differ: {list(pr_of_amendment)}')
    log_bytes = ''.join(f'{a["version"]}|{a["timestampUtc"]}|{a["reviewedInPr"]}|'
                        f'{str(a["afterDecisionBearingInspection"]).lower()}|{a["mergeCommit"]}\n' for a in amendments).encode()

    children = []
    for gate, issue, prs, evidence, status in GATES:
        merges = []
        for pr in prs:
            commit, merged_at, via = PRS[pr]
            ok = ancestor(commit)
            check(ok, f'{gate} PR #{pr} merge {commit} is not an ancestor of the candidate')
            check(via is not None or git('log', '-1', '--format=%s', commit).startswith(f'Merge pull request #{pr} from '),
                  f'{gate} PR #{pr}: {commit} is not GitHub\'s merge of that PR')
            row = {'pr': pr, 'mergeCommit': commit, 'mergedAtUtc': merged_at, 'ancestorOfCandidate': ok}
            if via:
                row['mergedViaPr'] = via
                row['mergedVia'] = (f'Stacked PR: GitHub recorded {commit[:8]} (the merge of its head into #{via}\'s branch) '
                                    f'when #{via} merged. Its identity rests on the GitHub API record; git checks only ancestry.')
            merges.append(row)
        check(bool(files_under(evidence)), f'{gate} evidence {evidence} is absent at the candidate')
        children.append({'gate': gate, 'issue': issue, 'mergedPrs': merges, 'evidence': evidence,
                         'issueStateAtFreeze': ISSUE_STATES[str(issue)], 'closureStatus': status})
    required = sorted(['R0 #1407'] + [f'{c["gate"]} #{c["issue"]}' for c in contract.get('children', []) if c['issue'] not in (1423, 1424, 1408)])
    check(required == sorted(f'{c["gate"]} #{c["issue"]}' for c in children), 'child set differs from contract.json children')
    extra = []
    for issue, pr, title, evidence in EXTRA:
        commit, merged_at, _ = PRS[pr]
        check(ancestor(commit) and git('log', '-1', '--format=%s', commit).startswith(f'Merge pull request #{pr} from '),
              f'PR #{pr} merge is not an ancestor of the candidate, or not GitHub\'s merge of that PR')
        extra.append({'issue': issue, 'pr': pr, 'title': title, 'mergeCommit': commit, 'mergedAtUtc': merged_at,
                      'ancestorOfCandidate': ancestor(commit), 'evidence': evidence})

    s2 = jload('docs/plans/evidence/s2-1413/dispositions.json')
    closure = s2.get('closure', {})
    check(closure.get('status') == 'CLOSED' and closure.get('result') == 'SUCCESS', 'S2 closure is not CLOSED/SUCCESS')
    repairs = []
    for r in s2.get('repairs', []):
        witnesses_ok = all(blob(w) is not None for w in r['regressionWitness'])
        check(r['status'] == 'merged' and ancestor(r['mergeCommit']) and witnesses_ok,
              f'S2 repair {r["id"]} is not merged into the candidate with its witnesses')
        check(PRS.get(r['pr'], (None,))[0] == r['mergeCommit'], f'S2 repair {r["id"]} merge commit differs from the PR record')
        repairs.append({'id': r['id'], 'pr': r['pr'], 'kind': r['kind'], 'mergeCommit': r['mergeCommit'],
                        'regressionWitness': r['regressionWitness'], 'presentAtCandidate': witnesses_ok})
    check(len(repairs) == 7, f'expected 7 S2 repairs, found {len(repairs)}')
    open_findings = [d['findingId'] for b in s2.get('baselines', {}).values() for d in b.get('findings', [])
                     if d.get('disposition') in ('NOT-INVESTIGATED', 'MILESTONE-FAILED')]
    open_findings += [d['id'] for d in s2.get('discoveryFindings', [])
                      if d.get('disposition') in ('NOT-INVESTIGATED', 'MILESTONE-FAILED')]
    check(not open_findings, f'S2 findings without an accepted disposition: {open_findings}')

    protocol = jload('docs/plans/evidence/g2-1421/protocol.json')
    consumers = jload('eng/z3-consumers.json')
    ledger = jload('docs/plans/evidence/g3-1135/ledger.json')
    for e in ledger.get('entries', []):
        for key, path_key in (('resultSha256', 'result'), ('attemptRecordsSha256', 'attemptRecords')):
            check(sha(blob(e[path_key])) == e[key], f'G3 ledger {e["executionId"]} {path_key} hash mismatch')
    exec2 = None
    branch = 'refs/remotes/origin/evidence/g3-1135-exec-2'
    if git('rev-parse', '--verify', '--quiet', branch) is not None:
        root = 'docs/plans/evidence/g3-1135/executions/g3-exec-2/'
        entry = json.loads(blob(root + 'ledger-entry.json', branch))
        ok = (sha(blob(root + 'result.json', branch)) == entry['resultSha256']
              and sha(blob(root + 'attempts.tar.gz', branch)) == entry['attemptRecordsSha256'])
        check(ok, 'parked G3 execution 2 records do not match their ledger entry')
        exec2 = {k: entry[k] for k in ('runId', 'executionId', 'commit', 'protocolVersion', 'verdict', 'complete',
                                       'classCounts', 'resultSha256', 'attemptRecordsSha256')}
        exec2.update({'branch': 'evidence/g3-1135-exec-2', 'branchTip': git('rev-parse', branch),
                      'recordsVerifiedAgainstLedgerEntry': ok, 'commitIsAncestorOfCandidate': ancestor(entry['commit'])})

    contract_sha = jload('docs/plans/evidence/evidence-contract-1407/sha256.json')
    producers = []
    for pid, title, version, prefix in PRODUCERS:
        paths = files_under(prefix) if prefix else []
        paths += PRODUCER_FILES.get(pid, [])
        producers.append({'id': pid, 'title': title, 'version': version, 'files': hashes(sorted(set(paths)))})

    b1_agg = jload('docs/plans/evidence/b1-1276/registration/registration.json')['comparability']['aggregationMethod']
    check('SplitMix64 seed 1276' in b1_agg and '10000 resamples' in b1_agg, 'B1 bootstrap seed is not 1276 at the candidate')
    seeds = [
        {'id': 'r1-master-seed', 'value': jload('docs/plans/evidence/r1-1419/registration.json')['generation']['masterSeed'],
         'source': 'docs/plans/evidence/r1-1419/registration.json generation.masterSeed', 'usedBy': 'R1 case generation; S1 runs 1 and 2'},
        {'id': 'b1-bootstrap', 'value': '1276', 'source': 'docs/plans/evidence/b1-1276/registration/registration.json comparability.aggregationMethod',
         'usedBy': 'B1 percentile bootstrap, SplitMix64, 10000 resamples, 95%'},
        {'id': 'z3-random-seed', 'value': protocol['z3']['randomSeed']['value'], 'source': protocol['z3']['randomSeed']['source'],
         'applied': protocol['z3']['randomSeed']['applied'],
         'note': 'G2 protocol 1.2.0: Z3 4.15.7 ignores random_seed as a context parameter, so the constant never reaches the solver.'},
        {'id': 'r1-z3-seed', 'value': None, 'note': 'R1 injects no seed into the baseline compiler (registration oracle.z3Seed).'},
    ]

    return {
        'schemaVersion': 1, 'issue': 1423, 'gate': 'C1', 'epic': 1409, 'contractVersion': contract.get('contractVersion'),
        'status': 'FROZEN-AT-MERGE',
        'statusNote': N['statusNote'],
        'candidate': {
            'commit': CANDIDATE,
            'committedUtc': git('show', '-s', '--date=iso-strict-local', '--format=%cd', CANDIDATE, env_utc=True),
            'mergeOf': N['mergeOf'],
            'durableIdentity': {
                'status': 'complete', 'commit': CANDIDATE, 'resolvedVia': MAIN,
                'treeHashes': trees,
                'inputContentHashes': hashes(PIN_FILES),
            },
            'reachability': {
                'rule': N['reachabilityRule'],
                'originMainAtFreeze': CANDIDATE,
                'freezeObservedUtc': INPUTS['candidate']['freezeObservedUtc'],
                'freezeObservation': N['freezeObservation'],
            },
        },
        'submodules': {'source': N['submodulesSource'],
                       'entries': submodules},
        'dependencies': {
            'lockfiles': hashes(sorted(lockfiles)),
            'toolManifests': {'dotnetTools': N['dotnetTools'],
                              'node': hashes(['website/package.json', 'website/package-lock.json'])},
            'centralPackageVersions': N['centralPackageVersions'],
        },
        'toolchain': {
            'globalJson': json.loads(blob('global.json'))['sdk'],
            'determinismProtocol': {k: protocol['toolchain'][k] for k in ('sdk', 'runtime', 'globalJson', 'configuration')},
            **{k: N[k] for k in ('ciSetupDotnet', 'productVersion', 'translatorSemanticsVersion', 'verificationCacheFormat')},
        },
        'z3': {
            'version': consumers.get('z3Version'),
            'assetPins': {'file': '.github/z3-binaries-4.15.7.sha256', 'assets': parse_pins('.github/z3-binaries-4.15.7.sha256')},
            'upstreamArchivePins': {'file': 'src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256',
                                    'archives': parse_pins('src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256')},
            'bootstrap': consumers.get('bootstrap'),
        },
        'platforms': {
            'supportedRids': [r['rid'] for r in consumers.get('supportedRids', [])],
            'unsupportedRids': consumers.get('unsupportedRids'),
            'determinismEnvironments': protocol.get('environments'),
            'runnerImages': {
                'determinismProtocol': sorted({e['runner'] for e in protocol.get('environments', [])}),
                'rule': protocol.get('environmentRule'),
                'ordinaryCi': N['ordinaryCiImages'],
                'observedAtFreeze': INPUTS['observedEnvironment'],
            },
        },
        'configuration': {'build': 'Release', 'checkout': protocol.get('checkout'),
                          'treatWarningsAsErrors': True, 'cultureForOracles': 'en-US (R1-O1, B1 oracle)'},
        'seeds': seeds,
        'producers': producers,
        'contractPacket': {
            'sha256Json': {'path': 'docs/plans/evidence/evidence-contract-1407/sha256.json',
                           'sha256': sha(blob('docs/plans/evidence/evidence-contract-1407/sha256.json')),
                           'files': contract_sha.get('files')},
            'contractVersion': contract.get('contractVersion'), 'status': contract.get('status'),
            'gateStatus': contract.get('gateStatus'), 'acceptance': contract.get('acceptance'),
            'amendmentLog': amendments,
            'amendmentLogSha256': sha(log_bytes),
            'amendmentLogHashRule': N['amendmentLogHashRule'],
        },
        'registeredManifests': {
            'packets': [verify_packet(p, lf) for p, lf in PACKETS],
            'files': hashes(INPUTS['registeredFiles']),
            'g2ProtocolVersion': protocol.get('protocolVersion'),
        },
        'acceptedChildren': children,
        'issueStatesObservedVia': INPUTS['issueStatesAtFreeze']['observedVia'],
        'additionalMerged': extra,
        's2Closure': {'source': 'docs/plans/evidence/s2-1413/dispositions.json closure', 'closure': closure,
                      'repairs': repairs, 'findingsWithoutAcceptedDisposition': open_findings,
                      'note': N['s2Note']},
        'g3Execution2': exec2,
        'releasability': releasability(),
        'maintainerDecisions': INPUTS['maintainerDecisions'],
        'knownOpenItems': OPEN_ITEMS,
        'resolvedOpenItems': INPUTS['resolvedOpenItems'],
        'invalidation': INPUTS['invalidation'],
        'review': N['review'],
    }

def main():
    tag_self_test()   # controls for the tag rule run on every invocation
    manifest = build()
    text = json.dumps(manifest, indent=2, ensure_ascii=False) + '\n'
    if '--check' in sys.argv:
        current = open(MANIFEST, encoding='utf-8').read()
        check(current == text, f'{MANIFEST} differs from the regenerated manifest')
    elif not FAILURES:
        open(MANIFEST, 'w', encoding='utf-8', newline='\n').write(text)
    for f in FAILURES:
        print('FAIL', f)
    print('OK' if not FAILURES else f'{len(FAILURES)} failure(s)')
    return 1 if FAILURES else 0

if __name__ == '__main__':
    sys.exit(main())
