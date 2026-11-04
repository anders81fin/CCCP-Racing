"""Exercise publication boundaries against real disposable Git repositories."""

from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile


EXPORTER = Path(__file__).with_name("export_public.py")


class PublicExportTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="cccp-public-export-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.script = self.root / "tools/source/export_public.py"
        self.script.parent.mkdir(parents=True)
        shutil.copyfile(EXPORTER, self.script)
        self.files = [".gitignore", "public-source-files.txt", "src/car.cs", "docs/public/README.md"]
        self.put(".gitignore", "build/\n")
        self.put("src/car.cs", "// public game code\n")
        self.put("docs/public/README.md", "# Public guide\n")
        self.put("docs/local-notes.md", "private discussion\n")
        self.put("AGENTS.md", "private agent instructions\n")
        self.put("assets/music/song.mp3", "private music\n")
        self.manifest()
        self.git("init", "-q")
        self.commit()

    def put(self, name, text):
        p = self.root / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.root, check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def commit(self):
        self.git("add", ".")
        self.git("-c", "user.name=Export test", "-c", "user.email=export@example.invalid",
                 "commit", "-qm", "fixture")

    def manifest(self):
        self.put("public-source-files.txt", "\n".join(self.files) + "\n")

    def run_export(self, *args):
        return subprocess.run([sys.executable, str(self.script), *args], cwd=self.root,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def test_only_explicit_files_and_public_ignore_rules(self):
        self.put("src/unreviewed.cs", "// not reviewed\n")
        self.commit()
        result = self.run_export("--output", "build/source.zip")
        self.assertEqual(result.returncode, 0, result.stderr)
        with zipfile.ZipFile(self.root / "build/source.zip") as archive:
            self.assertEqual({p for p in archive.namelist() if not p.endswith("/")}, set(self.files))
            self.assertEqual(archive.read("src/car.cs"), b"// public game code\n")
            self.assertIn(b"/docs/*", archive.read(".gitignore"))
            self.assertIn(b"!/docs/public/", archive.read(".gitignore"))

    def test_private_paths_refused_even_when_explicitly_listed(self):
        for name in ["docs/local-notes.md", "AGENTS.md", "assets/music/song.mp3",
                     "assets/marketing/draft.txt", "skills/example/SKILL.md", "../escape"]:
            with self.subTest(name=name):
                self.files.append(name)
                self.manifest()
                self.assertNotEqual(self.run_export("--preview").returncode, 0)
                self.files.pop()

    def test_uncommitted_code_is_not_silently_exported(self):
        self.put("src/car.cs", "// changed but uncommitted\n")
        self.assertNotEqual(self.run_export("--output", "build/source.zip").returncode, 0)
        self.assertFalse((self.root / "build/source.zip").exists())

    def test_uncommitted_file_list_is_refused(self):
        self.put("public-source-files.txt", "# pending review\n" + "\n".join(self.files))
        self.assertNotEqual(self.run_export("--output", "build/source.zip").returncode, 0)

    def test_committed_symlink_is_refused(self):
        (self.root / "src/car.cs").unlink()
        (self.root / "src/car.cs").symlink_to("../docs/local-notes.md")
        self.commit()
        self.assertNotEqual(self.run_export("--output", "build/source.zip").returncode, 0)

    def test_archive_attributes_cannot_silently_drop_reviewed_files(self):
        self.put(".gitattributes", "src/car.cs export-ignore\n")
        self.commit()
        self.assertNotEqual(self.run_export("--output", "build/source.zip").returncode, 0)
        self.assertFalse((self.root / "build/source.zip").exists())

    def test_existing_output_is_preserved(self):
        self.put("build/source.zip", "existing artifact")
        self.assertNotEqual(self.run_export("--output", "build/source.zip").returncode, 0)
        self.assertEqual((self.root / "build/source.zip").read_text(), "existing artifact")


if __name__ == "__main__":
    unittest.main()
