"""Rebrand phase 2: the plugin finds Core's renamed files and the migrated data folder."""
from pathlib import Path
import tempfile
import unittest

import store_library
from quick_settings import support_report


class CoreDataRootTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.local = Path(self.folder.name)

    def roots(self):
        return (Path(store_library.local_data_root(str(self.local))), support_report.local_data_root(self.local))

    def test_fresh_profile_uses_core(self):
        self.assertEqual(self.roots(), (self.local / "Core",) * 2)

    def test_playhub_until_the_marker_exists(self):
        (self.local / "Playhub").mkdir()
        (self.local / "Core").mkdir()
        self.assertEqual(self.roots(), (self.local / "Playhub",) * 2)
        (self.local / "Core" / ".core-migrated.json").write_text("{}", encoding="utf-8")
        self.assertEqual(self.roots(), (self.local / "Core",) * 2)


class CoreAppLookupTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.local = Path(self.folder.name)

    def test_core_folder_and_name_first_old_ones_still_found(self):
        legacy = self.local / "Programs" / "Playhub"
        core = self.local / "Programs" / "Core"
        self.assertEqual(support_report._installed_app(self.local), core / "Core.exe")
        legacy.mkdir(parents=True)
        (legacy / "Playhub.exe").write_bytes(b"old")
        self.assertEqual(support_report._installed_app(self.local), legacy / "Playhub.exe")
        (legacy / "Core.exe").write_bytes(b"new")
        self.assertEqual(support_report._installed_app(self.local), legacy / "Core.exe")
        core.mkdir(parents=True)
        (core / "Core.exe").write_bytes(b"fresh")
        self.assertEqual(support_report._installed_app(self.local), core / "Core.exe")


class CoreSignInExecutableTests(unittest.TestCase):
    def test_sign_in_uses_the_app_named_in_the_store_manifest(self):
        import hashlib, importlib.util, json
        source = Path(__file__).resolve().parents[1] / "store_sign_in.py"
        spec = importlib.util.spec_from_file_location("store_sign_in_core_names", source)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "StoreLibrary").mkdir()
            for name in ("Core.exe", "Playhub.exe"):
                (root / name).write_bytes(name.encode())
            def manifest(name):
                (root / "StoreLibrary" / "store-runtime.json").write_text(json.dumps({"protocol": 1, "finalized": True, "allowedClients": [
                    {"kind": "app", "path": name, "sha256": hashlib.sha256((root / name).read_bytes()).hexdigest()}]}), encoding="utf-8")
            launcher = module.StoreSignInLauncher(lambda: root, lambda *args, **kwargs: None)
            manifest("Core.exe")
            self.assertEqual(launcher._executable(), root / "Core.exe")
            manifest("Playhub.exe")
            self.assertEqual(launcher._executable(), root / "Playhub.exe")


if __name__ == "__main__":
    unittest.main()
