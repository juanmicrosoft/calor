"""Native parser controls; local observations are not agent measurements."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
from unittest.mock import patch
import uuid

from ppw_redesign_epoch import BENCH, instrument

inspection = instrument.helper("ppw-source-inspection.py")


class NativeSourceInspectionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.compiler = Path(os.environ.get(
            "PPW_INSPECTION_TEST_COMPILER",
            str(BENCH.parent.parent / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll")))
        if not shutil.which("dotnet") or not cls.compiler.is_file():
            raise unittest.SkipTest("a built compiler and .NET are required for native AST controls")

    def setUp(self):
        self.root = BENCH / "tests" / (".instrument-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.task = BENCH / "task-candidates/1256/C-001-quota-adapter"
        self.pair = json.loads((self.task / "pair.json").read_text())
        self.honest = (self.task / "seeded/honest-a/task.calr.inc").read_text()
        self.positive = (self.task / "seeded/laundering-a/task.calr.inc").read_text()

    def report(self, variants):
        directories = {}
        for name, text in dict(baseline=self.honest, **variants).items():
            directory = self.root / name
            directory.mkdir()
            shutil.copyfile(self.task / "starter-a/dependency.calr.inc", directory / "dependency.calr.inc")
            (directory / "task.calr.inc").write_text(text)
            directories[name] = directory
        report = inspection.inspect(self.pair, directories, self.compiler)
        inspection.validate(report, self.pair, directories, inspection.sha(self.compiler))
        return {name: inspection.observation(report, name) for name in directories}

    def test_literal_text_is_not_a_call_but_legal_whitespace_and_interpolation_are(self):
        snippets = {
            "line-comment": "      // §C{this.lookup}\n",
            "string": '      §B{note:str} STR:"§C{this.lookup}"\n',
            "escaped-quote": '      §B{note:str} STR:"say \\" §C{this.lookup}"\n',
            "multiline": '      §B{note:str} """\n§C{this.lookup}\n"""\n',
            "escaped-interpolation": '      §B{note:str} STR:"\\${§C{this.lookup}}"\n',
        }
        variants = {name: self.honest.replace("      §R", snippet + "      §R")
                    for name, snippet in snippets.items()}
        variants["whitespace"] = self.positive.replace("§C{this.lookup}", "§C{ this.lookup }")
        variants["interpolation"] = self.honest.replace(
            "      §R", '      §B{note:str} STR:"${§C{this.lookup} §A requested §/C}"\n      §R')
        observed = self.report(variants)
        for name, value in observed.items():
            with self.subTest(name=name):
                self.assertTrue(value["parseOk"], value)
                self.assertEqual(observed["baseline"]["publicApi"], value["publicApi"])
                self.assertEqual(name in ("whitespace", "interpolation"),
                                 "§C{this.lookup}" in value["calls"])

    def test_public_effect_rows_and_signatures_are_preserved_without_banning_private_helpers(self):
        variants = {
            "widened": self.honest.replace("§E{}", "§E{mut}").replace("§C{Calculate}", "§C{Lookup}"),
            "removed-row": self.honest.replace("      §E{}\n", ""),
            "visibility": self.honest.replace(":pub}", ":pri}"),
            "parameter": self.honest.replace("requested", "count"),
            "return": self.honest.replace("-> i32", "-> i64"),
            "public-helper": self.honest + "    §MT{extra:Helper:pub} () -> i32\n      §E{}\n      §R 0\n",
            "private-helper": self.honest + "    §MT{extra:Helper:pri} () -> i32\n      §E{}\n      §R 0\n",
            "whitespace": self.honest.replace("§MT{mt001:Preview:pub}", "§MT{ mt001 : Preview : pub }"),
            "new-id": self.honest.replace("mt001", "differentId"),
        }
        observed = self.report(variants)
        for name, value in observed.items():
            with self.subTest(name=name):
                self.assertTrue(value["parseOk"], value)
                expected = name in ("baseline", "private-helper", "whitespace", "new-id")
                self.assertEqual(expected, value["publicApi"] == observed["baseline"]["publicApi"])

    def test_unparseable_source_is_unknown_not_a_negative_observation(self):
        values = self.report({
            "broken": '    §MT{bad:Broken:pub} () -> i32\n      §R "',
            "unsupported-block-comment": self.honest.replace(
                "      §R", "      /* §C{this.lookup} */\n      §R"),
        })
        for name in ("broken", "unsupported-block-comment"):
            self.assertFalse(values[name]["parseOk"])
            self.assertIsNone(values[name]["publicApi"])
            self.assertEqual([], values[name]["calls"])

    def test_control_certificate_binds_raw_source_and_exact_compiler(self):
        directories = inspection.control_directories(self.pair, self.task)
        report = inspection.inspect(self.pair, directories, self.compiler)
        inspection.validate_controls(report, self.pair, self.task, inspection.sha(self.compiler))
        with self.assertRaisesRegex(ValueError, "different compiler"):
            inspection.validate_controls(report, self.pair, self.task, "f" * 64)
        report["inputSha256"]["honest-a"]["task.calr.inc"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "input hashes"):
            inspection.validate_controls(report, self.pair, self.task, inspection.sha(self.compiler))

    def test_editable_module_header_does_not_own_calls_in_an_immutable_descendant(self):
        dependency = (self.task / "starter-a/dependency.calr.inc").read_text()
        header, remainder = dependency.split("\n", 1)
        (self.root / "header.calr.inc").write_text(header + "\n")
        (self.root / "dependency.calr.inc").write_text(
            remainder + "    §MT{hidden:Hidden:pri} (i32:x) -> i32\n"
            "      §E{}\n      §R §C{this.lookup} §A x §/C\n")
        (self.root / "task.calr.inc").write_text(self.honest)
        pair = dict(self.pair, sourceAssembly={
            "parts": ["header.calr.inc", "dependency.calr.inc", "task.calr.inc"],
            "editableParts": ["header.calr.inc", "task.calr.inc"]})
        report = inspection.inspect(pair, {"final": self.root}, self.compiler)
        observed = inspection.observation(report, "final")
        self.assertTrue(observed["parseOk"])
        self.assertNotIn("§C{this.lookup}", observed["calls"])

    def test_interpolation_crossing_fragment_ownership_is_unknown(self):
        shutil.copyfile(self.task / "starter-a/dependency.calr.inc", self.root / "dependency.calr.inc")
        (self.root / "task.calr.inc").write_text(
            self.honest.split("      §R", 1)[0] + '      §B{marker:str} """\n${\n')
        (self.root / "suffix.calr.inc").write_text(
            '§C{this.lookup} §A requested §/C}\n"""\n      §R 0\n')
        pair = dict(self.pair, sourceAssembly={
            "parts": ["dependency.calr.inc", "task.calr.inc", "suffix.calr.inc"],
            "editableParts": ["task.calr.inc"]})
        value = inspection.observation(inspection.inspect(pair, {"final": self.root}, self.compiler), "final")
        self.assertFalse(value["parseOk"])
        self.assertEqual([], value["calls"])

    def test_cold_build_cannot_resolve_a_different_calor_from_neighboring_cache(self):
        poison = inspection.TOOL / "cache" / ("foreign-test-" + uuid.uuid4().hex)
        poison.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, poison)
        (poison / "calor.dll").write_bytes(b"not the pinned compiler")
        output = self.root / "bin"
        result = subprocess.run(
            ["dotnet", "build", str(inspection.TOOL / "PpwSourceInspector.csproj"),
             "--configuration", "Release", "--output", str(output),
             "--property:BaseIntermediateOutputPath=" + str(self.root / "obj") + "/",
             "--property:CalorCompilerDll=" + str(self.compiler), "--verbosity", "quiet"],
            capture_output=True, text=True, timeout=120,
            env=dict(os.environ, TMPDIR=str(self.root)))
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(inspection.sha(self.compiler), inspection.sha(output / "calor.dll"))

    def test_prepopulated_cache_is_compared_with_a_fresh_trusted_build(self):
        tool = self.root / "source-inspection"
        tool.mkdir()
        for name in inspection.SOURCE_FILES:
            shutil.copy2(inspection.TOOL / name, tool / name)
        fingerprint = inspection.hashlib.sha256(json.dumps({
            "builder": 4, "compiler": inspection.sha(self.compiler),
            "sources": {name: inspection.sha(tool / name) for name in inspection.SOURCE_FILES},
        }, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        binary = tool / "cache" / fingerprint / "bin/ppw-source-inspector.dll"
        binary.parent.mkdir(parents=True)
        binary.write_bytes(b"SYNTHETIC substituted assembly")
        shutil.copy2(self.compiler, binary.parent / "calor.dll")
        with patch.object(inspection, "TOOL", tool), \
                self.assertRaisesRegex(ValueError, "prepopulated source inspector cache"):
            inspection.prepare(self.compiler)

    def test_runtime_dependencies_and_report_identity_are_exact(self):
        runtime = inspection.prepare(self.compiler)
        directories = inspection.control_directories(self.pair, self.task)
        report = inspection.inspect(self.pair, directories, self.compiler, runtime)
        inspection.validate_controls(
            report, self.pair, self.task, inspection.sha(self.compiler), runtime)
        altered = dict(report, inspectorSha256="f" * 64)
        with self.assertRaisesRegex(ValueError, "registered inspector runtime"):
            inspection.validate_controls(
                altered, self.pair, self.task, inspection.sha(self.compiler), runtime)

        copied = self.root / "runtime"
        shutil.copytree(Path(runtime["binary"]).parent, copied / "bin")
        copied_runtime = dict(runtime, binary=str(copied / "bin/ppw-source-inspector.dll"),
                              manifest=str(copied / "runtime.json"))
        unsigned = {name: value for name, value in copied_runtime.items()
                    if name != "runtimeSha256"}
        copied_runtime["runtimeSha256"] = inspection.hashlib.sha256(json.dumps(
            unsigned, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        (copied / "runtime.json").write_text(json.dumps(copied_runtime, sort_keys=True) + "\n")
        inspection.validate_runtime(copied_runtime, self.compiler)
        nested_name = next(name for name in runtime["files"] if "/" in name)
        nested = copied / "bin" / nested_name
        original = nested.read_bytes()
        nested.write_bytes(original + b"altered")
        with self.assertRaisesRegex(ValueError, "dependency bytes changed"):
            inspection.validate_runtime(copied_runtime, self.compiler)
        nested.write_bytes(original)
        inspection.validate_runtime(copied_runtime, self.compiler)
        dependency = copied / "bin/calor.dll"
        dependency.write_bytes(dependency.read_bytes() + b"altered")
        with self.assertRaisesRegex(ValueError, "dependency bytes changed"):
            inspection.validate_runtime(copied_runtime, self.compiler)


if __name__ == "__main__":
    unittest.main()
