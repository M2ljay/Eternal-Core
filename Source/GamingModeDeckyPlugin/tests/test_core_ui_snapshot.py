import importlib.util
import json
import logging
import os
import pathlib
import sys
import tempfile
import types
import unittest
from unittest import mock


# Eternal Core 3.3.4: the Core UI snapshot's backend half (main.py): written to the user's AppData only, overwritten,
# size-capped, with CSS Loader's themes listed; the legacy Core themes switched off and back on (only those); the
# user's AppData found even when Decky's environment points elsewhere.
class CoreUiSnapshotBackendTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.home = self.root / "Users" / "player"
        self.appdata = self.home / "AppData" / "Roaming"
        self.appdata.mkdir(parents=True)
        decky = types.ModuleType("decky")
        decky.DECKY_PLUGIN_SETTINGS_DIR = str(self.root / "settings")
        decky.DECKY_USER_HOME = str(self.home)
        decky.logger = logging.getLogger("core-ui-snapshot-test")
        sys.modules["decky"] = decky
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        spec = importlib.util.spec_from_file_location("core_ui_snapshot_test_runtime", path)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.themes = self.home / "homebrew" / "themes"
        self.themes.mkdir(parents=True)

    def theme(self, folder, active, name=None, installed=True):
        path = self.themes / folder
        path.mkdir()
        if installed:
            (path / "theme.json").write_text(json.dumps({"name": name or folder}), encoding="utf-8")
        (path / "config_USER.json").write_text(json.dumps({"active": active, "Option": "Value"}), encoding="utf-8")
        return path

    def config(self, folder):
        return json.loads((self.themes / folder / "config_USER.json").read_text(encoding="utf-8"))

    def snapshot(self, **extra):
        return json.dumps({"kind": "core-ui-snapshot", "format": 1, "surfaces": [], **extra})

    def test_snapshot_written_to_appdata_and_overwritten(self):
        self.theme("Core", True, installed=False)
        self.theme("CapyMenu (Context Menus)", True)
        answer = self.module._save_core_ui_snapshot(self.snapshot(reason="start"), appdata=str(self.appdata), themes_dir=str(self.themes))
        self.assertTrue(answer["ok"], answer)
        path = self.appdata / "GamingMode" / "diagnostics" / "core-ui-snapshot.json"
        self.assertEqual(pathlib.Path(answer["path"]), path)
        data = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(data["reason"], "start")
        css = data["backend"]["cssLoader"]
        self.assertEqual(sorted(css["active"]), ["CapyMenu (Context Menus)", "Core"])
        self.assertEqual(css["legacyActive"], ["Core"])
        self.assertFalse([t for t in css["themes"] if t["folder"] == "Core"][0]["installed"])
        again = self.module._save_core_ui_snapshot(self.snapshot(reason="manual"), appdata=str(self.appdata), themes_dir=str(self.themes))
        self.assertTrue(again["ok"])
        self.assertEqual(json.loads(path.read_text(encoding="utf-8"))["reason"], "manual")
        self.assertEqual(len(list(path.parent.iterdir())), 1, "one file, overwritten")

    def test_snapshot_refuses_bad_input_and_paths_outside_appdata(self):
        ok = dict(appdata=str(self.appdata), themes_dir=str(self.themes))
        self.assertEqual(self.module._save_core_ui_snapshot("", **ok)["error"], "empty")
        self.assertEqual(self.module._save_core_ui_snapshot("{", **ok)["error"], "not_json")
        self.assertEqual(self.module._save_core_ui_snapshot(json.dumps({"kind": "other"}), **ok)["error"], "not_snapshot")
        big = self.snapshot(pad="x" * (2 * 1024 * 1024 + 100 * 1024))
        self.assertEqual(self.module._save_core_ui_snapshot(big, **ok)["error"], "too_large")
        outside = self.root / "elsewhere"
        outside.mkdir()
        self.assertEqual(self.module._save_core_ui_snapshot(self.snapshot(), appdata=str(outside), themes_dir="")["error"], "no_appdata")
        service = self.root / "Windows" / "System32" / "config" / "systemprofile" / "AppData" / "Roaming"
        self.assertEqual(self.module._core_ui_snapshot_path(str(service)), "")

    def test_legacy_themes_off_then_back_on_only_those(self):
        self.theme("Core", True, installed=False)
        self.theme("Core Accents", False)
        self.theme("CapyMenu (Context Menus)", True)
        self.theme("Renamed", True, name="Core")
        record = self.appdata / "GamingMode" / "core-ui-css-themes.json"
        status = self.module._core_ui_legacy_themes("status", str(self.themes), str(record))
        self.assertEqual(status["changed"], [])
        self.assertTrue(self.config("Core")["active"])
        off = self.module._core_ui_legacy_themes("disable", str(self.themes), str(record))
        self.assertTrue(off["ok"], off)
        self.assertEqual(sorted(off["changed"]), ["Core", "Core"])
        self.assertFalse(self.config("Core")["active"])
        self.assertEqual(self.config("Core")["Option"], "Value", "the theme's options are kept")
        self.assertFalse(self.config("Renamed")["active"], "matched by theme name too")
        self.assertFalse(self.config("Core Accents")["active"], "already off: untouched")
        self.assertTrue(self.config("CapyMenu (Context Menus)")["active"], "the user's other themes are never written")
        self.assertEqual(sorted(item["folder"] for item in json.loads(record.read_text(encoding="utf-8"))["disabled"]), ["Core", "Renamed"])
        twice = self.module._core_ui_legacy_themes("disable", str(self.themes), str(record))
        self.assertEqual(twice["changed"], [], "nothing left to switch off")
        self.assertEqual(len(json.loads(record.read_text(encoding="utf-8"))["disabled"]), 2, "the record keeps what Core changed")
        on = self.module._core_ui_legacy_themes("restore", str(self.themes), str(record))
        self.assertEqual(sorted(on["restored"]), ["Core", "Core"])
        self.assertTrue(self.config("Core")["active"])
        self.assertTrue(self.config("Renamed")["active"])
        self.assertFalse(self.config("Core Accents")["active"], "a theme Core did not switch off stays off")
        self.assertFalse(record.exists(), "record cleared after restore")
        self.assertEqual(self.module._core_ui_legacy_themes("nope", str(self.themes), str(record))["error"], "bad_action")

    def test_user_appdata_skips_service_profiles_and_icon_folder_prefers_a_map(self):
        service = self.root / "Windows" / "System32" / "config" / "systemprofile" / "AppData" / "Roaming"
        service.mkdir(parents=True)
        with mock.patch.dict(os.environ, {"APPDATA": str(service), "USERPROFILE": str(self.home)}):
            candidates = self.module._user_appdata_candidates()
            self.assertNotIn(str(service), candidates)
            self.assertEqual(self.module._user_appdata(), str(self.appdata))
        other = self.root / "Other" / "AppData" / "Roaming"
        (other / "GamingMode" / "icons").mkdir(parents=True)
        icons = self.appdata / "GamingMode" / "icons"
        icons.mkdir(parents=True)
        (icons / "map.json").write_text("{}", encoding="utf-8")
        with mock.patch.dict(os.environ, {"APPDATA": str(other), "USERPROFILE": str(self.home)}):
            self.assertEqual(self.module._core_icon_folder(), str(icons), "the folder holding map.json wins")
        (icons / "map.json").unlink()
        with mock.patch.dict(os.environ, {"APPDATA": str(other), "USERPROFILE": str(self.home)}):
            self.assertEqual(self.module._core_icon_folder(), str(other / "GamingMode" / "icons"))

    def test_css_themes_dir_from_decky_home(self):
        self.assertEqual(self.module._css_themes_dir(), str(self.themes))


if __name__ == "__main__":
    unittest.main()
