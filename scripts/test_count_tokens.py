#!/usr/bin/env python3
"""Offline tests for #1597. Run: python3 -m unittest discover -s scripts -p test_count_tokens.py"""

import contextlib
import io
import json
import os
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError, URLError

import count_tokens as tool


MODEL = {"id": "provider-id-from-metadata", "display_name": "Claude Opus 5.5"}


def response(value):
    return io.BytesIO(json.dumps(value).encode("utf-8"))


class AnthropicTests(unittest.TestCase):
    def client(self, *values):
        client = tool.Anthropic("test-secret")
        client.opener = Mock()
        client.opener.open.side_effect = [response(value) for value in values]
        return client

    def test_metadata_pagination_and_exact_provider_id(self):
        client = self.client(
            {"data": [{"id": "other", "display_name": "Claude Opus 4.5"}],
             "has_more": True, "last_id": "other"},
            {"data": [MODEL], "has_more": False},
            {"input_tokens": 17},
        )
        self.assertEqual(MODEL, client.resolve_model("Claude Opus 5.5"))
        self.assertEqual(17, client.count(MODEL["id"], "§R x\r\n"))
        calls = client.opener.open.call_args_list
        self.assertIn("after_id=other", calls[1].args[0].full_url)
        request = calls[2].args[0]
        self.assertEqual("https://api.anthropic.com/v1/messages/count_tokens", request.full_url)
        self.assertEqual({
            "model": MODEL["id"], "messages": [{"role": "user", "content": "§R x\r\n"}],
        }, json.loads(request.data))
        self.assertEqual("test-secret", request.get_header("X-api-key"))
        self.assertEqual(tool.API_VERSION, request.get_header("Anthropic-version"))
        self.assertEqual(60, calls[2].kwargs["timeout"])

    def test_no_alias_or_model_fallback(self):
        for models in ([], [{"id": "claude-opus-5.5", "display_name": "Opus 5"}],
                       [MODEL, {**MODEL, "id": "second"}]):
            with self.subTest(models=models), self.assertRaises(tool.MeasurementError):
                self.client({"data": models, "has_more": False}).resolve_model("Claude Opus 5.5")

    def test_malformed_and_repeated_pagination(self):
        for page in ({}, {"data": {}, "has_more": False},
                     {"data": [{}], "has_more": False},
                     {"data": [], "has_more": True, "last_id": "x"},
                     {"data": [MODEL], "has_more": "false"}):
            with self.subTest(page=page), self.assertRaises(tool.MeasurementError):
                self.client(page).resolve_model("Claude Opus 5.5")
        page = {"data": [MODEL], "has_more": True, "last_id": MODEL["id"]}
        with self.assertRaises(tool.MeasurementError):
            self.client(page, page).resolve_model("Claude Opus 5.5")

    def test_count_must_be_nonnegative_integer(self):
        for value in (True, -1, "17", 1.2, None):
            with self.subTest(value=value), self.assertRaises(tool.MeasurementError):
                self.client({"input_tokens": value}).count(MODEL["id"], "text")
        self.assertEqual(0, self.client({"input_tokens": 0}).count(MODEL["id"], "text"))

    def test_http_and_transport_errors_do_not_leak_body_or_credentials(self):
        for error in (HTTPError(tool.API_ROOT, 401, "test-secret", {},
                                io.BytesIO(b"test-secret")),
                      HTTPError(tool.API_ROOT, 429, "test-secret", {}, None),
                      URLError("test-secret"), TimeoutError("test-secret")):
            client = tool.Anthropic("test-secret")
            client.opener = Mock()
            client.opener.open.side_effect = error
            client.sleep = Mock()
            with self.subTest(error=type(error)), self.assertRaises(tool.MeasurementError) as found:
                client.count(MODEL["id"], "text")
            self.assertNotIn("test-secret", str(found.exception))
            retried = not (isinstance(error, HTTPError) and error.code == 401)
            self.assertEqual(tool.MAX_ATTEMPTS if retried else 1, client.opener.open.call_count)

    def test_rate_limit_retry_is_bounded_and_resends_same_count_request(self):
        limited = HTTPError(tool.API_ROOT, 429, "limited", {"retry-after": "500"}, None)
        overloaded = HTTPError(tool.API_ROOT, 529, "overloaded", {}, None)
        client = tool.Anthropic("test-secret")
        client.opener = Mock()
        client.opener.open.side_effect = [limited, overloaded, response({"input_tokens": 5})]
        client.sleep = Mock()
        self.assertEqual(5, client.count(MODEL["id"], "text"))
        self.assertEqual([tool.MAX_RETRY_DELAY, 4.0],
                         [call.args[0] for call in client.sleep.call_args_list])
        urls = {call.args[0].full_url for call in client.opener.open.call_args_list}
        self.assertEqual({tool.API_ROOT + "/v1/messages/count_tokens"}, urls)

    def test_invalid_json_and_nonobject_response(self):
        client = tool.Anthropic("test-secret")
        client.opener = Mock()
        client.opener.open.return_value = io.BytesIO(b"invalid")
        with self.assertRaises(tool.MeasurementError):
            client.count(MODEL["id"], "text")
        with self.assertRaises(tool.MeasurementError):
            self.client([]).count(MODEL["id"], "text")

    def test_redirect_is_never_followed(self):
        with self.assertRaises(tool.MeasurementError):
            tool.NoRedirects().redirect_request(None, None, 307, "", {}, "https://example.com")


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "before.txt").write_bytes(b"before\r\n")
        (self.root / "after.txt").write_bytes("§ after\n".encode("utf-8"))
        self.path = self.root / "manifest.json"
        self.output = self.root / "result.json"
        self.manifest = {
            "schema_version": 1, "source_commit": "a" * 40,
            "target_model": "Claude Opus 5.5", "baseline": "current",
            "population": {"primer": 1, "converted": 20, "agent": 20},
            "samples": [
                {"id": f"{group}-{index}", "population": group,
                 "source": f"repo/{group}-{index}",
                 "variants": {"current": "before.txt", "infix": "after.txt"}}
                for group, count in (("primer", 1), ("converted", 20), ("agent", 20))
                for index in range(count)
            ],
        }
        self.save()

    def save(self):
        self.path.write_text(json.dumps(self.manifest), encoding="utf-8")

    def test_byte_fidelity_hashes_and_population(self):
        manifest, samples = tool.load_manifest(self.path)
        self.assertEqual(41, len(samples))
        before = samples[0]["variants"]["current"]
        self.assertEqual("before\r\n", before["text"])
        self.assertEqual(tool.digest(b"before\r\n"), before["sha256"])
        self.assertEqual(tool.digest(self.path.read_bytes()), manifest["manifest_sha256"])

    def test_reject_incomplete_population_and_variant_coverage(self):
        for mutation in ("population", "variants", "id", "source", "commit", "empty"):
            with self.subTest(mutation=mutation):
                original = json.loads(json.dumps(self.manifest))
                if mutation == "population":
                    self.manifest["samples"].pop()
                elif mutation == "variants":
                    del self.manifest["samples"][1]["variants"]["infix"]
                elif mutation in ("id", "source"):
                    self.manifest["samples"][1][mutation] = self.manifest["samples"][0][mutation]
                elif mutation == "commit":
                    self.manifest["source_commit"] = "main"
                else:
                    (self.root / "empty.txt").write_text("")
                    self.manifest["samples"][0]["variants"]["current"] = "empty.txt"
                self.save()
                with self.assertRaises(tool.MeasurementError):
                    tool.load_manifest(self.path)
                self.manifest = original

    def test_measurement_aggregates_pairs_and_removes_source_text(self):
        manifest, samples = tool.load_manifest(self.path)
        client = Mock()
        client.resolve_model.return_value = MODEL
        client.count.side_effect = [10, 7] * 41
        report = tool.measure(client, manifest, samples)
        row = next(row for row in report["table"]
                   if row["population"] == "agent" and row["variant"] == "infix")
        self.assertEqual((20, 140, -60, -30), (
            row["files"], row["input_tokens"], row["delta_tokens"], row["delta_percent"]))
        self.assertNotIn('"text"', json.dumps(report))
        self.assertIn(MODEL["id"], tool.markdown(report))
        self.assertEqual(1, tool.markdown(report).count("| Kind |"))
        self.assertEqual(-3, report["samples"][0]["variants"]["infix"]["delta_tokens"])

    def test_probe_baselines_only_include_matching_pairs(self):
        self.manifest["probes"] = [
            {"id": "p1", "source": "probe1", "population": "spellings",
             "variants": {"current": "before.txt", "one": "after.txt"}},
            {"id": "p2", "source": "probe2", "population": "spellings",
             "variants": {"current": "before.txt", "two": "after.txt"}},
        ]
        self.save()
        manifest, samples = tool.load_manifest(self.path)
        client = Mock()
        client.resolve_model.return_value = MODEL
        client.count.side_effect = [10, 7] * 41 + [30, 25, 100, 90]
        report = tool.measure(client, manifest, samples)
        row = next(row for row in report["table"] if row["variant"] == "one")
        self.assertEqual(30, row["baseline_tokens"])
        self.assertEqual(-5, row["delta_tokens"])
        self.assertEqual(1, row["files"])

    def run_main(self, *extra):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = tool.main([str(self.path), *extra])
        return code, out.getvalue(), err.getvalue()

    def test_validation_without_credentials_never_calls_api(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(tool, "Anthropic") as client:
            code, out, err = self.run_main("--validate-only")
        self.assertEqual(0, code, err)
        self.assertIn("NOT MEASURED", out)
        client.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_missing_credentials_is_blocked_not_offline_success(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(tool, "Anthropic") as client:
            code, out, err = self.run_main("--output", str(self.output))
        self.assertEqual(2, code)
        self.assertIn("ANTHROPIC_API_KEY is absent", err)
        self.assertNotIn("Measured with", out)
        client.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_partial_provider_failure_does_not_replace_previous_results(self):
        self.output.write_text("previous run")
        with patch.dict(os.environ, {"ANTHROPIC_API_KEY": "test-secret"}), \
                patch.object(tool, "Anthropic") as client:
            client.return_value.resolve_model.return_value = MODEL
            client.return_value.count.side_effect = [10, tool.MeasurementError("HTTP 429")]
            code, out, err = self.run_main("--output", str(self.output))
        self.assertEqual(2, code)
        self.assertIn("HTTP 429", err)
        self.assertEqual("previous run", self.output.read_text())
        self.assertNotIn("Measured with", out)

    def test_cli_writes_complete_results_and_table(self):
        table = self.root / "result.md"
        with patch.dict(os.environ, {"ANTHROPIC_API_KEY": "test-secret"}), \
                patch.object(tool, "Anthropic") as client:
            client.return_value.resolve_model.return_value = MODEL
            client.return_value.count.side_effect = [10, 7] * 41
            code, out, err = self.run_main("--output", str(self.output), "--table", str(table))
        self.assertEqual(0, code, err)
        self.assertEqual("measured", json.loads(self.output.read_text())["status"])
        self.assertIn(MODEL["id"], out)
        self.assertIn("| Population |", table.read_text())

    def test_end_to_end_mocked_http_never_uses_generation_endpoint(self):
        opener = Mock()
        opener.open.side_effect = [
            response({"data": [MODEL], "has_more": False}),
            *[response({"input_tokens": count}) for count in [10, 7] * 41],
        ]
        with patch.dict(os.environ, {"ANTHROPIC_API_KEY": "test-secret"}), \
                patch.object(tool, "build_opener", return_value=opener):
            code, _, err = self.run_main("--output", str(self.output))
        self.assertEqual(0, code, err)
        requests = [call.args[0] for call in opener.open.call_args_list]
        self.assertEqual(83, len(requests))
        self.assertEqual("GET", requests[0].get_method())
        self.assertTrue(all(request.full_url == tool.API_ROOT + "/v1/messages/count_tokens"
                            and request.get_method() == "POST" for request in requests[1:]))
        self.assertTrue(all(json.loads(request.data)["model"] == MODEL["id"]
                            for request in requests[1:]))

    def test_invalid_baseline_fails_explicitly(self):
        self.manifest["baseline"] = []
        self.save()
        code, _, err = self.run_main("--validate-only")
        self.assertEqual(2, code)
        self.assertIn("baseline must name", err)

    def test_inputs_digest_pins_exact_payload_bytes(self):
        code, out, err = self.run_main("--validate-only")
        self.assertEqual(0, code, err)
        digest = next(line.split("=", 1)[1] for line in out.splitlines()
                      if line.startswith("inputs_sha256="))
        self.assertIn("request_count=83", out)
        self.assertEqual(0, self.run_main("--validate-only", "--expect-inputs-sha256", digest)[0])
        (self.root / "after.txt").write_bytes("§ after\r\n".encode("utf-8"))
        code, _, err = self.run_main("--validate-only", "--expect-inputs-sha256", digest)
        self.assertEqual(2, code)
        self.assertIn("Inputs digest mismatch", err)

    def test_payloads_must_stay_inside_manifest_directory(self):
        outside = tempfile.TemporaryDirectory()
        self.addCleanup(outside.cleanup)
        secret = Path(outside.name) / "secret.txt"
        secret.write_text("not a payload")
        (self.root / "link.txt").symlink_to(secret)
        for relative in ("../" + Path(outside.name).name + "/secret.txt", str(secret),
                         "link.txt", "missing.txt", "."):
            with self.subTest(relative=relative):
                original = json.loads(json.dumps(self.manifest))
                self.manifest["samples"][0]["variants"]["infix"] = relative
                self.save()
                code, _, err = self.run_main("--validate-only")
                self.assertEqual(2, code)
                self.assertIn("payload", err)
                self.manifest = original

    def test_expected_model_and_request_bound_fail_closed(self):
        code, _, err = self.run_main("--validate-only", "--expect-target-model", "Claude Opus 5")
        self.assertEqual(2, code)
        self.assertIn("target_model differs", err)
        code, _, err = self.run_main("--validate-only", "--max-requests", "82")
        self.assertEqual(2, code)
        self.assertIn("exceed", err)
        self.assertEqual(0, self.run_main("--validate-only", "--max-requests", "83")[0])
        for value in ("main", "A" * 40, "a" * 39):
            with self.subTest(value=value):
                code, _, err = self.run_main("--validate-only", "--attest-tool-commit", value)
                self.assertEqual(2, code)

    def test_results_attest_inputs_and_never_contain_credential(self):
        table = self.root / "result.md"
        opener = Mock()
        opener.open.side_effect = [
            response({"data": [MODEL], "has_more": False}),
            *[response({"input_tokens": count}) for count in [10, 7] * 41],
        ]
        with patch.dict(os.environ, {"ANTHROPIC_API_KEY": "sk-test-credential"}), \
                patch.object(tool, "build_opener", return_value=opener):
            code, out, err = self.run_main(
                "--output", str(self.output), "--table", str(table),
                "--expect-inputs-sha256", tool.load_manifest(self.path)[0]["inputs_sha256"],
                "--expect-target-model", "Claude Opus 5.5", "--max-requests", "83",
                "--attest-tool-commit", "b" * 40, "--attest-payload-commit", "c" * 40)
        self.assertEqual(0, code, err)
        report = json.loads(self.output.read_text())
        self.assertEqual({"tool_commit": "b" * 40, "payload_commit": "c" * 40},
                         report["attestation"])
        self.assertEqual(83, report["request_count"])
        self.assertEqual("before.txt", report["samples"][0]["variants"]["current"]["path"])
        self.assertIn(report["inputs_sha256"], table.read_text())
        for text in (self.output.read_text(), table.read_text(), out, err):
            self.assertNotIn("sk-test-credential", text)
        for text in (self.output.read_text(), table.read_text()):
            self.assertNotIn(str(self.root), text)

    def test_output_cannot_overwrite_input_or_other_output(self):
        for args in (("--output", str(self.path)),
                     ("--output", str(self.root / "before.txt")),
                     ("--output", str(self.output), "--table", str(self.output))):
            with self.subTest(args=args):
                code, _, err = self.run_main(*args)
                self.assertEqual(2, code)
                self.assertIn("Output paths must be distinct", err)


class WorkflowTests(unittest.TestCase):
    """Static invariants of the count-only workflow (no YAML dependency)."""

    path = Path(__file__).resolve().parent.parent / ".github/workflows/syntax-token-count.yml"

    def setUp(self):
        self.text = self.path.read_text(encoding="utf-8")
        self.code = "\n".join(line for line in self.text.splitlines()
                              if not line.lstrip().startswith("#"))

    def job(self, name):
        match = re.search(rf"^  {name}:\n(.*?)(?=^  \S|\Z)", self.code, re.M | re.S)
        self.assertIsNotNone(match, name)
        return match.group(1)

    def test_manual_trigger_and_read_only_token(self):
        self.assertRegex(self.code, r"\non:\n  workflow_dispatch:\n")
        self.assertNotRegex(self.code, r"\n  (push|pull_request|pull_request_target|schedule|"
                                       r"workflow_run|repository_dispatch):")
        self.assertRegex(self.code, r"\npermissions:\n  contents: read\n\n")
        self.assertEqual(4, self.code.count("persist-credentials: false"))
        self.assertEqual(4, self.code.count("uses: actions/checkout@"))

    def test_secret_only_in_count_step_after_offline_preflight(self):
        self.assertEqual(["secrets.ANTHROPIC_API_KEY"], re.findall(r"secrets\.[A-Za-z_]+", self.code))
        preflight, count = self.job("preflight"), self.job("count")
        self.assertNotIn("secrets.", preflight)
        self.assertIn("needs: preflight", count)
        self.assertIn("environment: anthropic-count-tokens", count)
        self.assertIn("python3 scripts/test_count_tokens.py", preflight)
        self.assertIn("refs/heads/main", preflight)
        self.assertIn("count-tokens-only", preflight)

    def test_only_count_tool_runs_and_inputs_are_pinned(self):
        for banned in ("benchmark", "/v1/messages\"", "/v1/complete", "curl", "wget",
                       "pip install", "workflow_call"):
            self.assertNotIn(banned, self.code)
        commands = re.findall(r"python3 (\S+)", self.code)
        self.assertEqual({"scripts/test_count_tokens.py", "scripts/count_tokens.py"}, set(commands))
        for job in ("preflight", "count"):
            body = self.job(job)
            self.assertIn('--expect-inputs-sha256 "$INPUTS_SHA256"', body)
            self.assertIn('--expect-target-model "$TARGET_MODEL"', body)
            self.assertIn('--max-requests "$MAX_REQUESTS"', body)
        self.assertIn('TARGET_MODEL: "Claude Opus 5.5"', self.code)
        self.assertIn('--attest-payload-commit "$PAYLOAD_COMMIT"', self.job("count"))

    def test_dispatch_inputs_never_interpolated_into_scripts(self):
        for line in self.code.splitlines():
            if "${{ inputs." in line:
                self.assertRegex(line, r"^\s+([A-Z0-9_]+|ref): \$\{\{ inputs\.[a-z0-9_]+ \}\}$")

    def test_results_uploaded_only_after_complete_count(self):
        count = self.job("count")
        self.assertIn("if-no-files-found: error", count)
        self.assertNotIn("if: always()", count)
        self.assertNotIn("continue-on-error", self.code)
        self.assertLess(count.index("Count tokens"), count.index("upload-artifact"))
        self.assertIn('grep -rqF -- "$ANTHROPIC_API_KEY"', count)


if __name__ == "__main__":
    unittest.main()
