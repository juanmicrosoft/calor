"""Synthetic HTTP only; this suite never runs the native client probe."""
import http.client
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import threading
import unittest
from unittest.mock import patch
import uuid

BENCH = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ppw_probe_test", BENCH / "probe-ppw-gateway.py")
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class ProbeTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "existing shell capture tools required")
    def test_real_gateway_shell_uses_the_same_flags_as_the_probe(self):
        from test_ppw_instrument import InstrumentTests
        fixture = InstrumentTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        with patch.dict(os.environ, {"CLAUDE_MODEL": probe.budget.MODEL}):
            fixture.fake_capture("success")
        arguments = json.loads((fixture.root / "synthetic-agent-calls.arguments.json").read_text())
        self.assertEqual(probe.isolation.registered_client_flags() + ["--model", probe.budget.MODEL],
                         arguments[:-1])
        self.assertIn("iteration budget is 10", arguments[-1])

    def test_shared_client_flags_match_the_existing_registered_invocation(self):
        expected = ["--print", "--verbose", "--output-format", "stream-json",
                    "--forward-subagent-text", "--dangerously-skip-permissions"]
        result = subprocess.run([sys.executable, str(BENCH / "ppw-gateway-client.py"), "--client-flags"],
                                capture_output=True, text=True, check=True)
        self.assertEqual(expected, json.loads(result.stdout))
        self.assertEqual(expected, probe.isolation.registered_client_flags())
        self.assertNotIn("--safe-mode", result.stdout)
        self.assertNotIn("--no-session-persistence", result.stdout)

    def test_probe_and_launcher_share_environment_without_diagnostic_feature_overrides(self):
        root = BENCH / "tests" / (".probe-env-" + uuid.uuid4().hex)
        root.mkdir()
        self.addCleanup(shutil.rmtree, root)
        with patch.dict(os.environ, {"PATH": "SYNTHETIC_PATH",
                                     "PPW_TRUSTED_OBSERVER_URL": "SYNTHETIC-secret"}, clear=True):
            environment = probe.isolation.client_environment(root, "http://127.0.0.1:12345/SYNTHETIC")
        self.assertEqual("1", environment["DISABLE_AUTOUPDATER"])
        self.assertNotIn("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC", environment)
        self.assertNotIn("ANTHROPIC_API_KEY", environment)
        self.assertNotIn("ANTHROPIC_AUTH_TOKEN", environment)
        self.assertNotIn("PPW_TRUSTED_OBSERVER_URL", environment)
        self.assertTrue(Path(environment["TMPDIR"]).is_relative_to(root))
        self.assertEqual(sys.executable, environment["PPW_PYTHON_EXECUTABLE"])

    def test_real_rejecting_endpoint_reports_only_nonsecret_shape_and_never_forwards(self):
        server = probe.RejectingServer()
        worker = threading.Thread(target=server.serve_forever)
        worker.start()
        self.addCleanup(worker.join)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        private = "SYNTHETIC_PRIVATE_SENTINEL_1406"
        body = {
            "model": probe.budget.MODEL, "max_tokens": 64000, "stream": True,
            "messages": [{"role": "user", "content": private}], "metadata": {"user_id": private},
            "system": private, "thinking": {"type": "adaptive"}, "output_config": {"effort": "high"},
            "tools": [{"name": private, "description": private, "input_schema": {"type": "object"}}],
            "context_management": {"edits": [{"type": "clear_thinking_20251015", "keep": "all"}]},
        }
        client = http.client.HTTPConnection("127.0.0.1", server.server_port)
        client.request("POST", "/" + server.capability + "/v1/messages?beta=true", json.dumps(body), {
            "Content-Type": "application/json", "Authorization": private,
            "anthropic-beta": "oauth-2025-04-20, context-management-2025-06-27," + private,
        })
        response = client.getresponse()
        self.assertEqual(400, response.status)
        self.assertIn(probe.MARKER, response.read().decode())
        client.close()
        self.assertEqual(1, len(server.observations))
        record = server.observations[0]
        self.assertTrue(record["messagesPath"])
        self.assertTrue(record["credentialHeaderPresent"])
        self.assertEqual(64000, record["maxTokens"])
        self.assertEqual(["context-management-2025-06-27", "oauth-2025-04-20"],
                         record["betaCapabilities"])
        self.assertEqual(1, record["unclassifiedBetaCount"])
        self.assertFalse(record["priceContractAccepted"])
        self.assertNotIn(private, json.dumps(record))
        self.assertNotIn(server.capability, json.dumps(record))


if __name__ == "__main__":
    unittest.main()
