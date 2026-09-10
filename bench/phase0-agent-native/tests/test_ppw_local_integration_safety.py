"""The opt-in real-product check cannot quietly become a paid collection."""
import os
from pathlib import Path
import shutil
import sys
import unittest
from unittest.mock import patch
import uuid

import ppw_local_candidate_integration as integration


class LocalIntegrationSafetyTests(unittest.TestCase):
    def setUp(self):
        self.root = integration.REPO / ".instrument-validation" / ("safety-" + uuid.uuid4().hex)
        self.root.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.root)

    def test_product_outside_owned_scratch_is_refused_before_process_execution(self):
        with patch.object(integration.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "owned paths"):
                integration.run(self.root, integration.REPO, self.root / "output")
            run.assert_not_called()
        self.assertFalse((self.root / "output").exists())

    def test_existing_attempt_directory_cannot_be_overwritten(self):
        output = self.root / "existing"
        output.mkdir()
        evidence = output / "retained.txt"
        evidence.write_text("original evidence")
        with self.assertRaisesRegex(ValueError, "never replace"):
            integration.run(self.root, self.root / "product", output)
        self.assertEqual("original evidence", evidence.read_text())

    def test_stand_in_needs_explicit_engineering_guard(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(sys, "argv", ["stand-in"]), \
                patch.object(integration.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "explicit engineering"):
                integration.stand_in()
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
