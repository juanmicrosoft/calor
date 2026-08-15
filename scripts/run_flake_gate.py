#!/usr/bin/env python3
from __future__ import annotations

import argparse
import errno
import json
import os
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

EXPECTED_TEST_CLASS = "Calor.LanguageServer.Tests.E2E.LspE2ETests"
EXPECTED_TEST_METHOD = "CoreCapabilityFlow_IsReadyBoundedAndLeakFreeAsync"
EXPECTED_TEST_FQN = f"{EXPECTED_TEST_CLASS}.{EXPECTED_TEST_METHOD}"
TRX_NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def is_valid_run(return_code: int, executed: int, passed: int) -> bool:
    return return_code == 0 and executed == 1 and passed == 1


@dataclass(frozen=True)
class TrxValidation:
    valid: bool
    executed: int
    passed: int
    error: str


def validate_trx_root(root: ET.Element) -> TrxValidation:
    def split_tag(element: ET.Element) -> tuple[str, str]:
        if element.tag.startswith("{"):
            namespace, local_name = element.tag[1:].split("}", 1)
            return namespace, local_name
        return "", element.tag

    def canonical_children(
        parent: ET.Element,
        local_name: str,
    ) -> list[ET.Element]:
        return [
            child
            for child in list(parent)
            if split_tag(child) == (TRX_NAMESPACE, local_name)
        ]

    root_namespace, root_name = split_tag(root)
    if root_name != "TestRun" or root_namespace != TRX_NAMESPACE:
        return TrxValidation(
            False,
            0,
            0,
            "TRX root must be TestRun in the Visual Studio TeamTest 2010 namespace",
        )

    required_containers = (
        "TestDefinitions",
        "Results",
        "ResultSummary",
    )
    containers: dict[str, ET.Element] = {}
    for local_name in required_containers:
        all_matches = [
            element
            for element in root.iter()
            if split_tag(element)[1] == local_name
        ]
        direct_matches = canonical_children(root, local_name)
        if len(all_matches) != 1 or len(direct_matches) != 1:
            return TrxValidation(
                False,
                0,
                0,
                f"TRX must contain exactly one direct {local_name}",
            )
        containers[local_name] = direct_matches[0]

    all_counters = [
        element
        for element in root.iter()
        if split_tag(element)[1] == "Counters"
    ]
    direct_counters = canonical_children(
        containers["ResultSummary"],
        "Counters",
    )
    if len(all_counters) != 1 or len(direct_counters) != 1:
        return TrxValidation(
            False,
            0,
            0,
            "TRX must contain exactly one Counters directly under ResultSummary",
        )
    counters = direct_counters[0]

    required_counter_values = {
        "total": 1,
        "executed": 1,
        "passed": 1,
        "failed": 0,
        "error": 0,
        "timeout": 0,
        "aborted": 0,
        "inconclusive": 0,
        "passedButRunAborted": 0,
        "notRunnable": 0,
        "notExecuted": 0,
        "disconnected": 0,
        "warning": 0,
        # VSTest's TRX schema reports Passed separately; its Completed outcome
        # bucket is therefore zero for this real one-Passed-result inventory.
        "completed": 0,
        "inProgress": 0,
        "pending": 0,
    }
    parsed_counters: dict[str, int] = {}
    for attribute, expected in required_counter_values.items():
        raw = counters.attrib.get(attribute)
        if raw is None:
            return TrxValidation(
                False,
                0,
                0,
                f"TRX Counters is missing {attribute}",
            )
        try:
            value = int(raw)
        except ValueError:
            return TrxValidation(
                False,
                0,
                0,
                f"TRX counter {attribute} is not an integer",
            )
        parsed_counters[attribute] = value
        if value != expected:
            return TrxValidation(
                False,
                parsed_counters.get("executed", 0),
                parsed_counters.get("passed", 0),
                f"TRX counter {attribute} is {value}, expected {expected}",
            )
    for attribute, raw in counters.attrib.items():
        if attribute in required_counter_values:
            continue
        try:
            value = int(raw)
        except ValueError:
            continue
        if value != 0:
            return TrxValidation(
                False,
                executed,
                passed,
                f"TRX unrecognized counter {attribute} is nonzero",
            )

    executed = parsed_counters["executed"]
    passed = parsed_counters["passed"]
    if not is_valid_run(0, executed, passed):
        return TrxValidation(
            False,
            executed,
            passed,
            f"TRX counters are {passed}/{executed}, expected 1/1",
        )

    definitions = canonical_children(
        containers["TestDefinitions"],
        "UnitTest",
    )
    all_definitions = [
        element
        for element in root.iter()
        if split_tag(element)[1] == "UnitTest"
    ]
    results = canonical_children(
        containers["Results"],
        "UnitTestResult",
    )
    all_results = [
        element
        for element in root.iter()
        if split_tag(element)[1] == "UnitTestResult"
    ]
    if len(definitions) != 1 or len(all_definitions) != 1:
        return TrxValidation(
            False,
            executed,
            passed,
            f"TRX has {len(definitions)} test definitions, expected exactly one",
        )
    if len(results) != 1 or len(all_results) != 1:
        return TrxValidation(
            False,
            executed,
            passed,
            f"TRX has {len(results)} test results, expected exactly one",
        )

    definition = definitions[0]
    result = results[0]
    test_id = definition.attrib.get("id")
    if not test_id or result.attrib.get("testId") != test_id:
        return TrxValidation(
            False,
            executed,
            passed,
            "TRX result testId does not map to the sole definition",
        )
    if definition.attrib.get("name") != EXPECTED_TEST_FQN:
        return TrxValidation(
            False,
            executed,
            passed,
            f"TRX definition is not {EXPECTED_TEST_FQN}",
        )
    if result.attrib.get("testName") != EXPECTED_TEST_FQN:
        return TrxValidation(
            False,
            executed,
            passed,
            f"TRX result is not {EXPECTED_TEST_FQN}",
        )
    if result.attrib.get("outcome") != "Passed":
        return TrxValidation(
            False,
            executed,
            passed,
            f"TRX result outcome is {result.attrib.get('outcome')!r}",
        )

    methods = canonical_children(definition, "TestMethod")
    all_methods = [
        element
        for element in definition.iter()
        if split_tag(element)[1] == "TestMethod"
    ]
    if len(methods) != 1 or len(all_methods) != 1:
        return TrxValidation(
            False,
            executed,
            passed,
            "TRX definition does not contain exactly one TestMethod",
        )
    method = methods[0]
    if (
        method.attrib.get("className") != EXPECTED_TEST_CLASS
        or method.attrib.get("name") != EXPECTED_TEST_METHOD
    ):
        return TrxValidation(
            False,
            executed,
            passed,
            "TRX TestMethod class/name does not match the expected FQN",
        )
    return TrxValidation(True, executed, passed, "")


def validate_trx(path: Path) -> TrxValidation:
    if not path.is_file():
        return TrxValidation(False, 0, 0, "TRX result file is missing")
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as error:
        return TrxValidation(False, 0, 0, f"TRX parse failed: {error}")
    return validate_trx_root(root)


def process_exists(pid: int) -> bool:
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


def decode_output(value: str | bytes | None) -> str:
    if value is None:
        return ""
    return value.decode("utf-8", errors="replace") if isinstance(value, bytes) else value


@dataclass(frozen=True)
class ProcessIdentity:
    pid: int
    birth: str


@dataclass(frozen=True)
class ProcessGroupIdentity:
    pgid: int
    leader_birth: str


@dataclass(frozen=True)
class ProcessRecord:
    parent: int
    group: int
    birth: str


class SupervisionError(RuntimeError):
    pass


def read_process_table() -> dict[int, ProcessRecord]:
    if os.name != "posix":
        raise SupervisionError("process supervision requires a POSIX host")
    try:
        completed = subprocess.run(
            ["ps", "-axo", "pid=,ppid=,pgid=,lstart="],
            text=True,
            capture_output=True,
            check=False,
            timeout=2,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise SupervisionError(f"process table query failed: {error}") from error
    if completed.returncode != 0:
        raise SupervisionError(
            f"process table query exited {completed.returncode}"
        )

    records: dict[int, ProcessRecord] = {}
    for line in completed.stdout.splitlines():
        parts = line.strip().split(None, 7)
        if len(parts) != 8:
            continue
        try:
            pid, parent, group = (int(part) for part in parts[:3])
        except ValueError:
            continue
        records[pid] = ProcessRecord(
            parent,
            group,
            " ".join(parts[3:8]),
        )
    if not records:
        raise SupervisionError("process table query returned no parseable records")
    return records


def preflight_supervision() -> tuple[bool, str]:
    try:
        records = read_process_table()
    except SupervisionError as error:
        return False, str(error)
    current = records.get(os.getpid())
    if current is None or current.parent <= 0 or current.group <= 0:
        return False, "process supervision preflight could not identify itself"
    return True, ""


class ProcessTracker:
    """Tracks only observed descendants. This is not a containment boundary."""

    def __init__(self, root_pid: int):
        self.root_pid = root_pid
        self.known_processes: dict[int, ProcessIdentity] = {}
        self.known_groups: set[ProcessGroupIdentity] = set()

    def scan(
        self,
    ) -> tuple[set[ProcessIdentity], set[ProcessGroupIdentity]]:
        records = read_process_table()
        root = records.get(self.root_pid)
        if not self.known_processes and root is not None:
            self.known_processes[self.root_pid] = ProcessIdentity(
                self.root_pid,
                root.birth,
            )

        owned = {
            pid
            for pid, identity in self.known_processes.items()
            if pid in records and records[pid].birth == identity.birth
        }
        changed = True
        while changed:
            changed = False
            for pid, record in records.items():
                if record.parent in owned and pid not in owned:
                    owned.add(pid)
                    changed = True

        live = {
            ProcessIdentity(pid, records[pid].birth)
            for pid in owned
            if pid in records
        }
        for identity in live:
            existing = self.known_processes.get(identity.pid)
            if existing is None or existing.birth == identity.birth:
                self.known_processes[identity.pid] = identity

        live_pids = {identity.pid for identity in live}
        groups: set[ProcessGroupIdentity] = set()
        for identity in live:
            group = records[identity.pid].group
            leader = records.get(group)
            if group in live_pids and leader is not None:
                groups.add(ProcessGroupIdentity(group, leader.birth))
        self.known_groups.update(groups)
        return live, groups

    def identity_is_current(self, identity: ProcessIdentity) -> bool:
        try:
            record = read_process_table().get(identity.pid)
        except SupervisionError:
            return False
        return (
            record is not None
            and record.birth == identity.birth
            and self.known_processes.get(identity.pid) == identity
        )

    def group_is_current(self, group: ProcessGroupIdentity) -> bool:
        return self.identity_is_current(
            ProcessIdentity(group.pgid, group.leader_birth)
        )


def safe_signal_pid(
    tracker: ProcessTracker,
    identity: ProcessIdentity,
    sig: signal.Signals,
) -> bool:
    if not tracker.identity_is_current(identity):
        return False
    try:
        os.kill(identity.pid, sig)
        return True
    except ProcessLookupError:
        return True
    except PermissionError:
        return False
    except OSError as error:
        return error.errno == errno.ESRCH


def safe_signal_group(
    tracker: ProcessTracker,
    group: ProcessGroupIdentity,
    sig: signal.Signals,
) -> bool:
    if (
        group.pgid <= 0
        or group.pgid == os.getpgrp()
        or not tracker.group_is_current(group)
    ):
        return False
    try:
        os.killpg(group.pgid, sig)
        return True
    except ProcessLookupError:
        return True
    except PermissionError:
        return False
    except OSError as error:
        return error.errno == errno.ESRCH


def terminate_root(process: subprocess.Popen[str]) -> bool:
    process.poll()
    if process.returncode is not None:
        return True
    try:
        process.terminate()
        process.wait(timeout=1)
        return True
    except subprocess.TimeoutExpired:
        try:
            process.kill()
            process.wait(timeout=1)
            return True
        except (subprocess.TimeoutExpired, OSError, PermissionError):
            return False
    except (OSError, PermissionError):
        return False


def best_effort_known_descendant_cleanup(
    tracker: ProcessTracker,
) -> bool:
    identities = set(tracker.known_processes.values())
    groups = set(tracker.known_groups)
    for group in groups:
        safe_signal_group(tracker, group, signal.SIGTERM)
    for identity in identities:
        safe_signal_pid(tracker, identity, signal.SIGTERM)
    time.sleep(0.05)
    for group in groups:
        safe_signal_group(tracker, group, signal.SIGKILL)
    for identity in identities:
        safe_signal_pid(tracker, identity, signal.SIGKILL)
    try:
        live, _ = tracker.scan()
    except SupervisionError:
        return False
    return not live


def bounded_communicate(
    process: subprocess.Popen[str],
    timeout: float,
) -> tuple[str, str, bool]:
    try:
        stdout, stderr = process.communicate(timeout=timeout)
        return stdout, stderr, True
    except subprocess.TimeoutExpired as error:
        stdout = decode_output(error.stdout)
        stderr = decode_output(error.stderr)
        for stream in (process.stdout, process.stderr):
            if stream is not None:
                try:
                    stream.close()
                except OSError:
                    pass
        return stdout, stderr, False


def run_with_tree_timeout(
    command: list[str],
    timeout: float,
) -> tuple[subprocess.CompletedProcess[str], bool, bool, bool]:
    preflight_ok, preflight_error = preflight_supervision()
    if not preflight_ok:
        return (
            subprocess.CompletedProcess(command, 126, "", preflight_error + "\n"),
            False,
            False,
            False,
        )

    process: subprocess.Popen[str] | None = None
    tracker: ProcessTracker | None = None
    stdout = ""
    stderr = ""
    return_code = 125
    timed_out = False
    supervision_failed = False
    known_descendants_remaining = False
    normal_completion = False
    root_exited = False
    pipes_drained = False
    try:
        process = subprocess.Popen(
            command,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            start_new_session=True,
        )
        tracker = ProcessTracker(process.pid)
        deadline = time.monotonic() + timeout
        while True:
            try:
                tracker.scan()
            except Exception as error:
                supervision_failed = True
                stderr += (
                    f"Process supervision failed: "
                    f"{type(error).__name__}: {error}\n"
                )
                break

            remaining = deadline - time.monotonic()
            if remaining <= 0:
                timed_out = True
                return_code = 124
                stderr += "Live LSP run exceeded 30 seconds.\n"
                break
            try:
                stdout, process_stderr = process.communicate(
                    timeout=min(0.1, remaining)
                )
                stderr = process_stderr + stderr
                return_code = process.returncode
                normal_completion = True
                try:
                    live, _ = tracker.scan()
                    known_descendants_remaining = bool(live)
                except Exception as error:
                    supervision_failed = True
                    stderr += (
                        f"Final process supervision failed: "
                        f"{type(error).__name__}: {error}\n"
                    )
                break
            except subprocess.TimeoutExpired:
                continue
    except Exception as error:
        supervision_failed = True
        stderr += f"Supervisor lifecycle failed: {type(error).__name__}: {error}\n"
    finally:
        if process is not None:
            root_exited = terminate_root(process)
            if tracker is not None:
                try:
                    descendants_cleaned = best_effort_known_descendant_cleanup(
                        tracker
                    )
                    known_descendants_remaining = (
                        known_descendants_remaining or not descendants_cleaned
                    )
                except Exception as error:
                    known_descendants_remaining = True
                    stderr += (
                        f"Best-effort descendant cleanup failed: "
                        f"{type(error).__name__}: {error}\n"
                    )
            try:
                drained_stdout, drained_stderr, pipes_drained = bounded_communicate(
                    process,
                    timeout=2,
                )
                if drained_stdout:
                    stdout = drained_stdout
                if drained_stderr:
                    stderr = drained_stderr + stderr
            except Exception as error:
                stderr += (
                    f"Bounded pipe drain failed: {type(error).__name__}: {error}\n"
                )

    cleanup_verified = (
        normal_completion
        and not timed_out
        and not supervision_failed
        and root_exited
        and pipes_drained
        and not known_descendants_remaining
    )
    if not cleanup_verified and return_code == 0:
        return_code = 125
    return (
        subprocess.CompletedProcess(command, return_code, stdout, stderr),
        cleanup_verified,
        known_descendants_remaining,
        timed_out,
    )


def assert_process_dead(pid: int) -> None:
    if process_exists(pid):
        raise AssertionError(f"process {pid} is still alive")


def self_test() -> None:
    if is_valid_run(0, 0, 0) or not is_valid_run(0, 1, 1):
        raise AssertionError("flake gate did not enforce the exact test inventory")

    def make_trx(
        fqn: str,
        *,
        outcome: str = "Passed",
        duplicate_result: bool = False,
        include_definition: bool = True,
        root_name: str = "TestRun",
        namespace: str = TRX_NAMESPACE,
        counter_overrides: dict[str, int] | None = None,
    ) -> ET.Element:
        class_name, method_name = fqn.rsplit(".", 1)
        definition = (
            f"""
            <TestDefinitions>
              <UnitTest name="{fqn}" id="test-1">
                <TestMethod className="{class_name}" name="{method_name}" />
              </UnitTest>
            </TestDefinitions>
            """
            if include_definition
            else "<TestDefinitions />"
        )
        result = (
            f'<UnitTestResult testId="test-1" testName="{fqn}" '
            f'outcome="{outcome}" />'
        )
        results = result + (result if duplicate_result else "")
        counter_values = {
            "total": 1,
            "executed": 1,
            "passed": 1,
            "failed": 0,
            "error": 0,
            "timeout": 0,
            "aborted": 0,
            "inconclusive": 0,
            "passedButRunAborted": 0,
            "notRunnable": 0,
            "notExecuted": 0,
            "disconnected": 0,
            "warning": 0,
            "completed": 0,
            "inProgress": 0,
            "pending": 0,
        }
        counter_values.update(counter_overrides or {})
        counters = " ".join(
            f'{name}="{value}"'
            for name, value in counter_values.items()
        )
        return ET.fromstring(
            f"""
            <{root_name} xmlns="{namespace}">
              {definition}
              <Results>{results}</Results>
              <ResultSummary>
                <Counters {counters} />
              </ResultSummary>
            </{root_name}>
            """
        )

    if not validate_trx_root(make_trx(EXPECTED_TEST_FQN)).valid:
        raise AssertionError("exact TRX identity was rejected")
    impostor = EXPECTED_TEST_FQN + "_Impostor"
    if validate_trx_root(make_trx(impostor)).valid:
        raise AssertionError("same-substring impostor TRX was accepted")
    if validate_trx_root(
        make_trx(EXPECTED_TEST_FQN, duplicate_result=True)
    ).valid:
        raise AssertionError("duplicate TRX result was accepted")
    if validate_trx_root(
        make_trx(EXPECTED_TEST_FQN, outcome="NotExecuted")
    ).valid:
        raise AssertionError("skipped TRX result was accepted")
    if validate_trx_root(
        make_trx(EXPECTED_TEST_FQN, include_definition=False)
    ).valid:
        raise AssertionError("TRX without a definition was accepted")
    if validate_trx_root(
        make_trx(EXPECTED_TEST_FQN, root_name="NotATestRun")
    ).valid:
        raise AssertionError("non-TestRun root was accepted")
    if validate_trx_root(
        make_trx(EXPECTED_TEST_FQN, namespace="urn:not-trx")
    ).valid:
        raise AssertionError("unexpected TRX namespace was accepted")
    inconsistent = make_trx(
        EXPECTED_TEST_FQN,
        counter_overrides={"total": 2},
    )
    if validate_trx_root(inconsistent).valid:
        raise AssertionError("inconsistent TRX counters were accepted")
    for counter in (
        "passedButRunAborted",
        "notRunnable",
        "disconnected",
        "warning",
        "completed",
        "inProgress",
        "pending",
    ):
        if validate_trx_root(
            make_trx(
                EXPECTED_TEST_FQN,
                counter_overrides={counter: 1},
            )
        ).valid:
            raise AssertionError(f"nonzero {counter} counter was accepted")
    missing_counter = make_trx(EXPECTED_TEST_FQN)
    missing_summary = next(
        element
        for element in missing_counter
        if element.tag.endswith("ResultSummary")
    )
    missing_counters = next(
        element
        for element in missing_summary
        if element.tag.endswith("Counters")
    )
    del missing_counters.attrib["pending"]
    if validate_trx_root(missing_counter).valid:
        raise AssertionError("missing required standard counter was accepted")

    duplicate_containers = make_trx(EXPECTED_TEST_FQN)
    definitions = next(
        element
        for element in duplicate_containers
        if element.tag.endswith("TestDefinitions")
    )
    duplicate_containers.append(
        ET.fromstring(ET.tostring(definitions))
    )
    if validate_trx_root(duplicate_containers).valid:
        raise AssertionError("duplicate TRX containers were accepted")

    duplicate_counters = make_trx(EXPECTED_TEST_FQN)
    summary = next(
        element
        for element in duplicate_counters
        if element.tag.endswith("ResultSummary")
    )
    counters = next(
        element
        for element in summary
        if element.tag.endswith("Counters")
    )
    summary.append(ET.fromstring(ET.tostring(counters)))
    if validate_trx_root(duplicate_counters).valid:
        raise AssertionError("duplicate TRX counters were accepted")

    misnested = make_trx(EXPECTED_TEST_FQN)
    results_container = next(
        element
        for element in misnested
        if element.tag.endswith("Results")
    )
    result_summary = next(
        element
        for element in misnested
        if element.tag.endswith("ResultSummary")
    )
    misnested.remove(results_container)
    result_summary.append(results_container)
    if validate_trx_root(misnested).valid:
        raise AssertionError("misnested TRX Results was accepted")

    original_preflight = preflight_supervision
    original_popen = subprocess.Popen
    spawned = False
    try:
        globals()["preflight_supervision"] = lambda: (False, "injected")

        def reject_spawn(*_args, **_kwargs):
            nonlocal spawned
            spawned = True
            raise AssertionError("spawned despite failed preflight")

        subprocess.Popen = reject_spawn
        result = run_with_tree_timeout(
            [sys.executable, "-c", "raise SystemExit(0)"],
            timeout=1,
        )
    finally:
        globals()["preflight_supervision"] = original_preflight
        subprocess.Popen = original_popen
    if result[0].returncode != 126 or result[1] or spawned:
        raise AssertionError("preflight did not fail closed before spawn")

    timeout_script = (
        "import os,time;"
        "print(os.getpid(),flush=True);"
        "time.sleep(60)"
    )
    started = time.monotonic()
    timeout_result = run_with_tree_timeout(
        [sys.executable, "-c", timeout_script],
        timeout=0.25,
    )
    timeout_pid = int(timeout_result[0].stdout.strip().splitlines()[0])
    if (
        timeout_result[0].returncode != 124
        or timeout_result[1]
        or not timeout_result[3]
        or time.monotonic() - started > 6
    ):
        raise AssertionError("timeout was incorrectly certified or unbounded")
    assert_process_dead(timeout_pid)

    escape_script = """
import os
import time

print(f"root:{os.getpid()}", flush=True)
first = os.fork()
if first == 0:
    os.setsid()
    second = os.fork()
    if second > 0:
        os._exit(0)
    os.environ.clear()
    print(f"escape:{os.getpid()}", flush=True)
    time.sleep(60)
os.waitpid(first, 0)
time.sleep(60)
"""
    escape_result = run_with_tree_timeout(
        [sys.executable, "-c", escape_script],
        timeout=0.5,
    )
    escape_pids = {
        int(line.split(":", 1)[1])
        for line in escape_result[0].stdout.splitlines()
        if line.startswith(("root:", "escape:"))
    }
    if escape_result[0].returncode != 124 or escape_result[1]:
        raise AssertionError("env-clearing escape was falsely certified")
    for pid in escape_pids:
        if process_exists(pid):
            try:
                os.kill(pid, signal.SIGKILL)
            except ProcessLookupError:
                pass

    original_scan = ProcessTracker.scan
    scan_calls = 0
    try:
        def scan_then_raise(self):
            nonlocal scan_calls
            scan_calls += 1
            if scan_calls == 1:
                time.sleep(0.15)
                return original_scan(self)
            raise RuntimeError("injected scanner exception")

        ProcessTracker.scan = scan_then_raise
        scanner_result = run_with_tree_timeout(
            [sys.executable, "-c", timeout_script],
            timeout=5,
        )
    finally:
        ProcessTracker.scan = original_scan
    scanner_pid = int(scanner_result[0].stdout.strip().splitlines()[0])
    if scanner_result[0].returncode != 125 or scanner_result[1]:
        raise AssertionError("scanner exception was falsely certified")
    assert_process_dead(scanner_pid)

    print("Flake gate negative self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", type=int, default=10)
    parser.add_argument("--output", default="artifacts/flake/flake-report.json")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.runs < 2:
        parser.error("--runs must be at least 2")

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    logs = output.parent / "logs"
    logs.mkdir(exist_ok=True)
    results = []
    for run in range(1, args.runs + 1):
        results_dir = logs / f"results-{run}"
        started = time.monotonic()
        completed, cleanup_verified, known_remaining, timed_out = (
            run_with_tree_timeout(
                [
                    "dotnet",
                    "test",
                    "tests/Calor.LanguageServer.Tests/Calor.LanguageServer.Tests.csproj",
                    "-c",
                    "Release",
                    "--no-build",
                    "--no-restore",
                    "--filter",
                    f"FullyQualifiedName={EXPECTED_TEST_FQN}",
                    "--logger",
                    "trx;LogFileName=results.trx",
                    "--results-directory",
                    str(results_dir),
                    "--verbosity",
                    "minimal",
                ],
                timeout=30,
            )
        )
        elapsed = time.monotonic() - started
        (logs / f"run-{run}.log").write_text(
            completed.stdout + completed.stderr,
            encoding="utf-8",
        )
        trx = results_dir / "results.trx"
        trx_validation = validate_trx(trx)
        executed = trx_validation.executed
        passed = trx_validation.passed
        run_passed = (
            completed.returncode == 0
            and trx_validation.valid
            and cleanup_verified
            and not known_remaining
            and not timed_out
        )
        results.append(
            {
                "run": run,
                "executed": executed,
                "passedTests": passed,
                "passed": run_passed,
                "cleanupVerified": cleanup_verified,
                "knownDescendantsRemaining": known_remaining,
                "timedOut": timed_out,
                "testIdentityVerified": trx_validation.valid,
                "trxError": trx_validation.error,
                "seconds": round(elapsed, 3),
            }
        )
        print(
            f"flake run {run}: "
            f"{'passed' if run_passed else 'failed'} ({passed}/{executed})"
        )

    report = {
        "schemaVersion": 2,
        "test": "live LSP core capability and exact server PID teardown flow",
        "certification": (
            f"The sole Passed TRX result must map by testId to {EXPECTED_TEST_FQN}, "
            "which asserts its exact calor-lsp PID exits. "
            "Outer cleanup is bounded root supervision plus best-effort "
            "observed-descendant cleanup, not a kernel containment boundary."
        ),
        "runs": results,
        "passed": all(result["passed"] for result in results),
    }
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
