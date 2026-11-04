#!/usr/bin/env python3
"""Export an explicit, reviewed list of committed public source files. No network I/O."""

import argparse
from pathlib import Path, PurePosixPath
import subprocess
import sys
import zipfile


ROOT = Path(__file__).resolve().parents[2]
MANIFEST = "public-source-files.txt"
PRIVATE_ROOTS = {"build", "media", "shots", "skills"}
PRIVATE_NAMES = {"AGENTS.md", "CLAUDE.md", "id_rsa", "id_ed25519"}
PUBLIC_MUSIC_NOTICES = {"assets/music/LICENSE", "assets/music/radio/.gitkeep"}
PUBLIC_IGNORE = b"""
# Public snapshot: private working material must not be added here.
/AGENTS.md
/CLAUDE.md
/skills/
/assets/marketing/
/docs/*
!/docs/public/
assets/music/*.mp3
assets/music/*.mp3.import
assets/music/radio/*.mp3
assets/music/radio/*.mp3.import
"""


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def validate_path(name):
    path = PurePosixPath(name)
    if (not name or path.is_absolute() or str(path) != name
            or ".." in path.parts or "\\" in name
            or any(ord(c) < 32 for c in name)):
        raise ValueError(f"Unsafe path: {name!r}")
    if (path.parts[0] in PRIVATE_ROOTS or any(p in PRIVATE_NAMES for p in path.parts)
            or any(p.startswith(".") and p not in {".gitignore", ".gitattributes", ".gdignore", ".gitkeep"}
                   for p in path.parts)):
        raise ValueError(f"Private path cannot be exported: {name}")
    if name.startswith("docs/") and not name.startswith("docs/public/"):
        raise ValueError(f"Only reviewed docs/public files can be exported: {name}")
    if name.startswith("assets/marketing/"):
        raise ValueError(f"Marketing production material cannot be exported: {name}")
    if name.startswith("assets/music/") and name not in PUBLIC_MUSIC_NOTICES:
        raise ValueError(f"Separately licensed music cannot be exported: {name}")
    if any(p == ".env" or p.startswith(".env.") for p in path.parts):
        raise ValueError(f"Environment file cannot be exported: {name}")


def read_manifest(text):
    paths = []
    for line in text.splitlines():
        name = line.strip()
        if not name or name.startswith("#"):
            continue
        validate_path(name)
        if name in paths:
            raise ValueError(f"Duplicate manifest entry: {name}")
        paths.append(name)
    if not paths or MANIFEST not in paths:
        raise ValueError(f"The file list must include itself: {MANIFEST}")
    return paths


def preview():
    paths = read_manifest((ROOT / MANIFEST).read_text())
    for name in paths:
        path = ROOT / name
        if any(p.is_symlink() for p in [path, *path.parents] if p != ROOT.parent):
            raise ValueError(f"Symlink cannot be exported: {name}")
        if not path.is_file() or not path.resolve().is_relative_to(ROOT.resolve()):
            raise ValueError(f"Missing or non-regular file: {name}")
    print(f"Working-tree preview: {len(paths)} explicitly selected files.")
    print("No archive created; no network operation performed.")
    print("Selected public documents:")
    for name in paths:
        if name.startswith("docs/"):
            print(f"  {name}")


def export(revision, output):
    commit = git("rev-parse", "--verify", f"{revision}^{{commit}}").decode().strip()
    manifest = git("show", f"{commit}:{MANIFEST}").decode()
    paths = read_manifest(manifest)
    entries = {}
    for record in git("ls-tree", "-r", "-z", commit).split(b"\0"):
        if record:
            meta, name = record.split(b"\t", 1)
            mode, kind, _ = meta.split()
            entries[name.decode()] = (mode, kind)
    for name in paths:
        if entries.get(name) not in {(b"100644", b"blob"), (b"100755", b"blob")}:
            raise ValueError(f"Not a committed regular file: {name}")

    # A local edit to a selected file must never be silently mistaken for the
    # exported version. Unrelated private notes may remain uncommitted.
    if git("diff", "--name-only", commit, "--", *paths).strip():
        raise ValueError("Selected files differ from the revision. Commit or review them before export.")
    output = output.resolve()
    if output.exists():
        raise ValueError(f"Output already exists; use a new name: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".part")
    raw = output.with_name(output.name + ".raw.part")
    if temporary.exists() or raw.exists():
        raise ValueError(f"Temporary output already exists: {temporary}")
    try:
        # Exact git pathspecs, not shell globs. Attributes provide an independent
        # archive guard; compare the result to catch unexpected exclusions too.
        with raw.open("xb") as stream:
            subprocess.run(["git", "--literal-pathspecs", "archive", "--format=zip",
                            commit, "--", *paths], cwd=ROOT, stdout=stream, check=True)
        with zipfile.ZipFile(raw) as archive:
            actual = [name for name in archive.namelist() if not name.endswith("/")]
            if len(actual) != len(paths) or set(actual) != set(paths):
                raise ValueError("Archive differs from the explicit file list; inspect export attributes.")
            if archive.testzip() is not None:
                raise ValueError("Archive CRC verification failed")
            # The public clone needs stricter ignore rules than the private
            # development checkout. This is the only content transformation.
            with zipfile.ZipFile(temporary, "x") as public:
                for info in archive.infolist():
                    data = archive.read(info)
                    if info.filename == ".gitignore":
                        data += PUBLIC_IGNORE
                    public.writestr(info, data)
        temporary.replace(output)
    finally:
        for path in (temporary, raw):
            if path.exists():
                path.unlink()
    print(f"Exported {len(paths)} files from {commit} to {output}")
    print("No network operation performed. Review before publishing.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preview", action="store_true", help="Validate the local list without exporting")
    mode.add_argument("--output", type=Path, help="New ZIP path; export committed files only")
    parser.add_argument("--revision", default="HEAD", help="Commit to export (default: HEAD)")
    args = parser.parse_args()
    try:
        if args.preview:
            preview()
        else:
            export(args.revision, args.output)
    except (ValueError, OSError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f"Public source export refused: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
