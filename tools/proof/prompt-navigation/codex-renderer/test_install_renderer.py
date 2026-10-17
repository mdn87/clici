"""Exercise deployment against disposable profiles, never real user settings."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import tomllib
import unittest

SPEC = importlib.util.spec_from_file_location("install_renderer", Path(__file__).with_name("install_renderer.py"))
deployment = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(deployment)


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.bundle = self.root / "source bundle"
        self.bundle.mkdir()
        files = []
        for name in ("bin/codex.exe", "helper/PromptNavigation.Probe.exe",
                     "helper/PromptNavigation.Probe.dll", "helper/clici.Core.dll",
                     "helper/PromptNavigation.Probe.runtimeconfig.json", "Enable-RendererNavigation.ps1",
                     "claude-plugin/.claude-plugin/plugin.json", "claude-plugin/hooks/hooks.json",
                     "claude-plugin/hooks/register.ts", "claude-plugin/hooks/anchors.ts"):
            path = self.bundle / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture")
            files.append(dict(path=name, sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        self.manifest = dict(version="0.162.1", upstreamCommit=deployment.UPSTREAM, files=files)
        self.manifest_path = self.bundle / "renderer-bundle.json"
        self.manifest_path.write_text(json.dumps(self.manifest))
        self.install = self.root / "installed bundle"
        self.ps = self.root / "profile.ps1"
        self.bash = self.root / ".bashrc"
        self.config = self.root / "config.toml"
        self.ps.write_text("# My shell 日本語\nfunction prompt { '>' }\n", encoding="utf-8")
        self.bash.write_text('alias cx="codex -a never -s workspace-write"\n')
        self.config.write_text('[tui]\nalternate_screen = "auto" # original\nanimations = false\n[other]\nvalue = "keep"\n')

    def deploy(self):
        return deployment.install(self.bundle, self.install, self.ps, self.bash, self.config)

    def test_install_idempotence_and_disable_preserve_user_edits(self):
        before = {p: p.read_bytes() for p in (self.ps, self.bash, self.config)}
        first = self.deploy()
        second = self.deploy()
        self.assertEqual(first["bundle"], second["bundle"])
        self.assertEqual(self.ps.read_text(encoding="utf-8").count(deployment.START), 1)
        self.assertEqual(self.bash.read_text().count(deployment.START), 1)
        self.assertIn("Enable-RendererNavigation.ps1", self.ps.read_text())
        self.assertIn("CLICI_RENDERER_KEYS=1", self.bash.read_text())
        self.assertIn("export CLAUDE_CODE_PLUGIN_DIRS='" + (self.install / "releases").as_posix(), self.bash.read_text())
        self.assertIn("CLICI_CLAUDE_ANCHORS=1 CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN=1", self.bash.read_text())
        self.assertEqual(tomllib.loads(self.config.read_text())["tui"]["alternate_screen"], "never")
        self.assertFalse(tomllib.loads(self.config.read_text())["tui"]["animations"])
        self.ps.write_bytes(self.ps.read_bytes() + b"# added after install\n")
        deployment.disable(self.install)
        self.assertEqual(self.ps.read_bytes(), before[self.ps] + b"# added after install\n")
        self.assertEqual(self.bash.read_bytes(), before[self.bash])
        self.assertEqual(self.config.read_bytes(), before[self.config])
        deployment.disable(self.install)  # Repeat disable is harmless.

    def test_invalid_bundle_never_changes_profiles_or_config(self):
        before = {p: p.read_bytes() for p in (self.ps, self.bash, self.config)}
        (self.bundle / "bin/codex.exe").write_bytes(b"tampered")
        with self.assertRaises(ValueError):
            self.deploy()
        self.assertEqual({p: p.read_bytes() for p in before}, before)

    def test_bundle_without_the_claude_plugin_is_incomplete(self):
        self.manifest["files"] = [f for f in self.manifest["files"] if not f["path"].startswith("claude-plugin/")]
        self.manifest_path.write_text(json.dumps(self.manifest))
        with self.assertRaisesRegex(ValueError, "incomplete"):
            self.deploy()
        self.assertNotIn(deployment.START, self.bash.read_text())

    def test_manifest_cannot_copy_outside_the_release(self):
        self.manifest["files"].append(dict(path="../outside", sha256="0" * 64))
        self.manifest_path.write_text(json.dumps(self.manifest))
        with self.assertRaises(ValueError):
            self.deploy()
        self.assertNotIn(deployment.START, self.ps.read_text())

    def test_missing_tui_table_and_later_setting_change(self):
        self.config.write_text('model = "example"\n')
        self.deploy()
        data = tomllib.loads(self.config.read_text())
        self.assertEqual(data["tui"]["alternate_screen"], "never")
        self.config.write_text(self.config.read_text().replace('alternate_screen = "never"', 'alternate_screen = "always"'))
        result = deployment.disable(self.install)
        self.assertEqual(tomllib.loads(self.config.read_text())["tui"]["alternate_screen"], "always")
        self.assertTrue(result["config_changed_by_user"])


if __name__ == "__main__":
    unittest.main()
