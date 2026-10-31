# -*- coding: utf-8 -*-
"""Build a Thunderstore / r2modman ready package for DSPQualitySystem.

Layout produced inside the zip (Thunderstore package format v1):
    manifest.json
    icon.png                (256x256 PNG)
    README.md
    CHANGELOG.md
    BepInEx/plugins/DSPQualitySystem/DSPQualitySystem.dll

Usage:
    python tools/pack-r2modman.py [--version 0.15.0] [--dll <path>] [--no-build-check]

The version defaults to the value in DSPQualitySystem.csproj (<Version>).
Sources live in tools/r2/ (icon.png, README.md, CHANGELOG.md, manifest.template.json).
Output: dist/DSPQualitySystem-<version>-r2modman.zip (+ dist/r2modman/DSPQualitySystem/)
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import zipfile

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_DIR = os.path.join(ROOT, "tools", "r2")
CSPROJ = os.path.join(ROOT, "DSPQualitySystem.csproj")
DEFAULT_DLL = os.path.join(ROOT, "bin", "Release", "DSPQualitySystem.dll")
DIST_DIR = os.path.join(ROOT, "dist")
STAGE_DIR = os.path.join(DIST_DIR, "r2modman", "DSPQualitySystem")

NAME_RE = re.compile(r"^[A-Za-z0-9_]+$")
SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+$")
DEP_RE = re.compile(r"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$")


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def read_version():
    with open(CSPROJ, "r", encoding="utf-8-sig") as fh:
        text = fh.read()
    m = re.search(r"<Version>([^<]+)</Version>", text)
    if not m:
        raise SystemExit("cannot find <Version> in " + CSPROJ)
    return m.group(1).strip()


def validate_manifest(manifest):
    errors = []
    name = manifest.get("name", "")
    ver = manifest.get("version_number", "")
    url = manifest.get("website_url", "")
    desc = manifest.get("description", "")
    deps = manifest.get("dependencies", [])

    if not NAME_RE.match(name):
        errors.append("name must be alphanumeric/underscore: %r" % name)
    if len(name) > 64:
        errors.append("name must be <= 64 chars")
    if not SEMVER_RE.match(ver):
        errors.append("version_number must be Major.Minor.Patch: %r" % ver)
    if len(ver) > 16:
        errors.append("version_number must be <= 16 chars")
    if url and not url.startswith("http"):
        errors.append("website_url must be empty or a http(s) URL")
    if len(url) > 255:
        errors.append("website_url must be <= 255 chars")
    if len(desc) > 250:
        errors.append("description must be <= 250 chars (Thunderstore limit), got %d" % len(desc))
    if "\n" in desc:
        errors.append("description must be a single line")
    if not isinstance(deps, list):
        errors.append("dependencies must be a list")
    else:
        for dep in deps:
            if not DEP_RE.match(dep):
                errors.append("bad dependency (Namespace-Name-Version): %r" % dep)
    return errors


def main():
    parser = argparse.ArgumentParser(description="Build Thunderstore/r2modman package")
    parser.add_argument("--version", default=None, help="version_number override (default: csproj)")
    parser.add_argument("--dll", default=DEFAULT_DLL, help="built plugin DLL")
    args = parser.parse_args()

    version = args.version or read_version()
    dll = os.path.abspath(args.dll)
    if not os.path.isfile(dll):
        raise SystemExit("DLL not found: " + dll)

    with open(os.path.join(SRC_DIR, "manifest.template.json"), "r", encoding="utf-8-sig") as fh:
        manifest = json.load(fh)
    manifest["version_number"] = version
    errors = validate_manifest(manifest)
    if errors:
        for e in errors:
            print("MANIFEST ERROR:", e)
        raise SystemExit(1)

    if os.path.isdir(STAGE_DIR):
        shutil.rmtree(STAGE_DIR)
    plugin_dir = os.path.join(STAGE_DIR, "BepInEx", "plugins", "DSPQualitySystem")
    os.makedirs(plugin_dir)

    icon_src = os.path.join(SRC_DIR, "icon.png")
    with Image.open(icon_src) as img:
        if img.size != (256, 256):
            raise SystemExit("icon.png must be 256x256, got %s" % (img.size,))
        img.convert("RGB").save(os.path.join(STAGE_DIR, "icon.png"), format="PNG", optimize=True)

    with open(os.path.join(STAGE_DIR, "manifest.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    for name in ("README.md", "CHANGELOG.md"):
        shutil.copyfile(os.path.join(SRC_DIR, name), os.path.join(STAGE_DIR, name))
    shutil.copy2(dll, os.path.join(plugin_dir, "DSPQualitySystem.dll"))

    files = []
    for root, dirs, names in os.walk(STAGE_DIR):
        dirs.sort()
        for file_name in sorted(names):
            full = os.path.join(root, file_name)
            rel = os.path.relpath(full, STAGE_DIR).replace("\\", "/")
            files.append((rel, full))
    files.sort()

    zip_path = os.path.join(DIST_DIR, "DSPQualitySystem-%s-r2modman.zip" % version)
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for rel, full in files:
            info = zipfile.ZipInfo(rel, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(full, "rb") as fh:
                zf.writestr(info, fh.read())

    print("R2 PACKAGE OK ->", zip_path)
    print("  version        :", version)
    print("  size           :", os.path.getsize(zip_path), "bytes")
    print("  sha256         :", sha256_file(zip_path))
    print("  dll size/sha256:", os.path.getsize(dll), "/", sha256_file(dll))
    print("  description    : %d chars" % len(manifest["description"]))
    for rel, _ in files:
        print("  includes       :", rel)
    return 0


if __name__ == "__main__":
    sys.exit(main())
