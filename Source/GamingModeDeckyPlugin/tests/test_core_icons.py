import base64
import importlib.util
import json
import logging
import pathlib
import sys
import tempfile
import types
import unittest


# Eternal Core: Core UI's own icon set (docs/eternal/CORE-UI.md, "Your own icons"): map.json in
# %APPDATA%\GamingMode\icons\, read by main.py's _core_icon_set.
class CoreIconSetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        decky = types.ModuleType("decky")
        decky.DECKY_PLUGIN_SETTINGS_DIR = str(self.root / "settings")
        decky.logger = logging.getLogger("core-icons-test")
        sys.modules["decky"] = decky
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        spec = importlib.util.spec_from_file_location("core_icons_test_runtime", path)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.icons = self.root / "icons"
        self.icons.mkdir()

    def write(self, name, data=b"\x89PNG fake"):
        (self.icons / name).write_bytes(data)

    def test_sections_files_and_data_urls(self):
        self.write("family-playstation.png")
        self.write("ps2.png", b"ps2")
        (self.icons / "map.json").write_text(json.dumps({
            "version": 1,
            "tabs": {"PlayStation": "family-playstation.png"},
            "collections": {"PlayStation 2": "ps2.png"},
            "keys": {"ps2": "ps2.png"},
        }), encoding="utf-8")
        answer = self.module._core_icon_set(str(self.icons))
        self.assertTrue(answer["ok"])
        self.assertTrue(answer["tabs"]["PlayStation"].startswith("data:image/png;base64,"))
        self.assertEqual(answer["collections"]["PlayStation 2"], "data:image/png;base64," + base64.b64encode(b"ps2").decode())
        self.assertEqual(answer["keys"]["ps2"], answer["collections"]["PlayStation 2"])
        self.assertEqual(answer["errors"], [])

    def test_flat_map_applies_to_tabs_and_collections(self):
        self.write("vr.svg", b"<svg/>")
        (self.icons / "map.json").write_text(json.dumps({"VR": "vr.svg"}), encoding="utf-8")
        answer = self.module._core_icon_set(str(self.icons))
        self.assertTrue(answer["tabs"]["VR"].startswith("data:image/svg+xml;base64,"))
        self.assertIn("VR", answer["collections"])

    def test_paths_types_and_sizes_are_refused(self):
        outside = self.root / "secret.png"
        outside.write_bytes(b"no")
        self.write("big.png", b"x" * (self.module._CORE_ICON_MAX + 1))
        self.write("notes.txt", b"text")
        (self.icons / "map.json").write_text(json.dumps({"tabs": {
            "A": "../secret.png", "B": "C:\\Windows\\x.png", "C": "notes.txt", "D": "big.png", "E": "missing.png", "F": 3,
        }}), encoding="utf-8")
        answer = self.module._core_icon_set(str(self.icons))
        self.assertTrue(answer["ok"])
        self.assertEqual(answer["tabs"], {})
        joined = " ".join(answer["errors"])
        for marker in ("bad_name:../secret.png", "bad_name:notes.txt", "too_large:big.png", "missing:missing.png"):
            self.assertIn(marker, joined)

    def test_missing_folder_or_map_is_quiet(self):
        self.assertEqual(self.module._core_icon_set(str(self.root / "nowhere"))["errors"], ["folder_missing"])
        self.assertEqual(self.module._core_icon_set(str(self.icons))["errors"], ["map_missing"])
        (self.icons / "map.json").write_text("{not json", encoding="utf-8")
        self.assertEqual(self.module._core_icon_set(str(self.icons))["errors"], ["map_unreadable"])
        self.assertEqual(self.module._core_icon_set("")["errors"], ["folder_missing"])


if __name__ == "__main__":
    unittest.main()
