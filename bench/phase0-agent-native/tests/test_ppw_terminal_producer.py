"""Execute the actual invalid producer branch with SYNTHETIC client/observer boundaries."""
import json
import os
import shutil
import subprocess
import unittest

from test_ppw_gateway import BENCH, Fixture, budget


@unittest.skipUnless(shutil.which("bash") and shutil.which("jq"), "Bash and jq are required")
class TerminalProducerTests(Fixture):
    def invoke(self, raw=None, exit_code=0, gateway_mode=True):
        source = (BENCH / "run-pair.sh").read_text(encoding="utf-8")
        functions = []
        for name in ("detect_invalid_run", "write_invalid_result"):
            start = source.index(name + "() {")
            end = source.index("\n}\n", start) + 3
            functions.append(source[start:end])
        start = source.index('        if reason="$(policy_changed "$WS" "$WS_OUT")"')
        end = source.index("        if [[ $REDESIGNED_POLICY -eq 1 ]]", start)
        branch = source[start:end]
        archive = self.root / "archive"
        directory = archive / "runs/task/calor-permissive/run-1"
        directory.mkdir(parents=True)
        (directory / "final-src").mkdir()
        (directory / "final-src/Source.calr").write_text("SYNTHETIC sealed source\n")
        (directory / "client-invocation.json").write_text(
            json.dumps({"exitCode": exit_code}) + "\n")
        if raw is not None:
            (directory / "agent.json").write_text(raw)
        workspace = self.root / "synthetic-client-workspace"
        workspace.mkdir()
        environment = dict(os.environ, **{
            "WS": str(workspace), "WS_OUT": str(directory), "SCRIPT_DIR": str(BENCH),
            "PPW_TRUSTED_ARCHIVE_ROOT": str(archive),
            "PPW_GATEWAY_CLIENT": "SYNTHETIC-not-invoked" if gateway_mode else "",
            "PAIR_ID": "task", "ARM": "calor", "ARM_LABEL": "calor-permissive",
            "ARM_CONFIG_KEY": "calor-permissive", "CONTROL_ARM_KIND": "permissive",
            "PERMISSIVE_EFFECTS": "true", "ITERATION_BUDGET": "10", "HELDOUT_TEST_COUNT": "4",
            "NULL_AGENT": "0", "CALOR_CLI_DLL": "/SYNTHETIC/not-invoked.dll",
            "EDIT_MECHANISM": "raw", "ARM_REPO_ROOT": "/SYNTHETIC/frozen-product",
            "FIXTURE_DIR": "starter-a", "TEMPLATE_SOURCE": "SYNTHETIC",
            "ARM_CANARY_STATUS": "SYNTHETIC", "ARM_CANARY_COMPILER_HASH": "1" * 64,
            "REFERENCE_DIR": "", "REFERENCE_SOURCE": "SYNTHETIC",
            "AGENT_RC": str(exit_code), "PYTHONDONTWRITEBYTECODE": "1",
        })
        script = "\n".join(functions) + """
MAX_INVALID_RETRIES=0
INVALID_MARKERS=("hit your session limit" "rate limit" "overloaded" "api error")
policy_changed() { return 1; }
archive_reason=""
attempt=0
run=1
if [[ -n "$PPW_GATEWAY_CLIENT" ]]; then
    python3 "$SCRIPT_DIR/ppw-gateway-budget.py" attempt-start \
        --run-directory "$WS_OUT" --authoritative-root "$PPW_TRUSTED_ARCHIVE_ROOT" \
        --slot "$PAIR_ID/$ARM_LABEL/$run" --attempt-number 1 >/dev/null
fi
for synthetic_once in 1; do
""" + branch + "\ndone\n"
        result = subprocess.run(
            [shutil.which("bash"), "--noprofile", "--norc", "-euo", "pipefail", "-c", script],
            env=environment, capture_output=True, text=True, timeout=30)
        return result, directory

    def test_gateway_missing_result_uses_actual_jq_and_terminal_cli(self):
        process, directory = self.invoke(exit_code=1)
        self.assertEqual(0, process.returncode, process.stderr)
        record = json.loads((directory / "result.json").read_text())
        self.assertIs(record["invalid"], True)
        self.assertIs(record["censored"], True)
        self.assertIs(record["nullAgent"], False)
        for field in ("taskSuccess", "escapedBugs", "heldoutPassed", "finalBuild",
                      "heldoutFinal", "iterations", "iterationsToGreen"):
            self.assertIsNone(record[field], field)
        self.assertEqual({"input": None, "output": None}, record["tokens"])
        self.assertEqual({"source": "missing", "reason": "terminal-invalid"}, record["tokenUsage"])
        terminal = budget.validate_terminal_attempt(
            directory, self.root / "archive", "task/calor-permissive/1")
        self.assertEqual("MISSING_AGENT_RESULT", terminal["classification"])
        ledger, owner = self.ledger(planned=["task/calor-permissive/1"], terminal=True)
        ledger.complete_invalid_slot(
            owner, "task/calor-permissive/1", directory, self.root / "archive",
            self.isolation_evidence(), budget.source_identities())
        ledger.complete(owner)
        self.assertEqual((1, 0), (
            ledger.snapshot()["invalidTerminalSlots"], ledger.snapshot()["validCompletedSlots"]))

    def test_gateway_other_admitted_reasons_run_the_actual_producer_branch(self):
        for raw, exit_code, code in (
                ("SYNTHETIC malformed JSON", 0, "MALFORMED_AGENT_RESULT"),
                ('{"result":"SYNTHETIC"}', 1, "CLIENT_EXIT_WITHOUT_OBSERVED_WORK"),
                ('{"result":"SYNTHETIC"}', 0, "MISSING_TRANSCRIPT")):
            with self.subTest(code=code):
                process, directory = self.invoke(raw, exit_code)
                self.assertEqual(0, process.returncode, process.stderr)
                terminal = budget.validate_terminal_attempt(
                    directory, self.root / "archive", "task/calor-permissive/1")
                self.assertEqual(code, terminal["classification"])
                shutil.rmtree(self.root / "archive")

    def test_gateway_api_error_and_interrupted_exit_cannot_produce_terminal_proof(self):
        for raw, code in (('{"result":"SYNTHETIC api error"}', 0), (None, 124)):
            with self.subTest(code=code):
                process, directory = self.invoke(raw, code)
                self.assertNotEqual(0, process.returncode)
                self.assertFalse((directory / "invalid-terminal.json").exists())
                shutil.rmtree(self.root / "archive")

    def test_legacy_invalid_placeholder_is_unchanged_and_has_no_terminal_record(self):
        process, directory = self.invoke(gateway_mode=False)
        self.assertEqual(0, process.returncode, process.stderr)
        value = json.loads((directory / "result.json").read_text())
        self.assertIs(value["taskSuccess"], False)
        self.assertEqual(4, value["escapedBugs"])
        self.assertEqual(0, value["heldoutPassed"])
        self.assertEqual(0, value["iterations"])
        self.assertEqual(11, value["iterationsToGreen"])
        self.assertEqual({"input": 0, "output": 0}, value["tokens"])
        self.assertEqual({"source": "invalid"}, value["tokenUsage"])
        self.assertFalse((directory / "attempt-start.json").exists())
        self.assertFalse((directory / "invalid-terminal.json").exists())


if __name__ == "__main__":
    unittest.main()
