from __future__ import annotations

import hashlib
import json
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parent
BUILD = ROOT / "src" / "PlayerModelAccess" / "bin" / "Release" / "net10.0"
DIST = ROOT / "dist"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_entries(zip_path: Path) -> list[str]:
    with zipfile.ZipFile(zip_path) as archive:
        entries = [entry.filename for entry in archive.infolist()]
    if any("\\" in entry for entry in entries):
        raise ValueError("Package contains a backslash path")
    normalized = [entry.rstrip("/").lower() for entry in entries]
    if len(normalized) != len(set(normalized)):
        raise ValueError("Package contains duplicate paths after normalization")
    if "modinfo.json" not in normalized:
        raise ValueError("modinfo.json is not at the package root")
    return entries


def main() -> None:
    modinfo_path = ROOT / "modinfo.json"
    modinfo = json.loads(modinfo_path.read_text(encoding="utf-8-sig"))
    version = modinfo["version"]
    dll_path = BUILD / "PlayerModelAccess.dll"
    if not dll_path.is_file():
        raise FileNotFoundError(f"Build output is missing: {dll_path}")

    DIST.mkdir(parents=True, exist_ok=True)
    zip_path = DIST / f"PlayerModelAccess_v{version}.zip"
    manifest_path = DIST / f"PlayerModelAccess_v{version}_manifest.json"
    zip_path.unlink(missing_ok=True)
    manifest_path.unlink(missing_ok=True)

    files = [
        (modinfo_path, "modinfo.json"),
        (dll_path, "PlayerModelAccess.dll"),
    ]
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for source, entry in files:
            archive.write(source, entry)

    entries = validate_entries(zip_path)
    manifest = {
        "zip": zip_path.name,
        "modid": modinfo["modid"],
        "version": version,
        "entries": entries,
        "size": zip_path.stat().st_size,
        "sha256": sha256(zip_path),
    }
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"zip": str(zip_path), "manifest": str(manifest_path)}, indent=2))


if __name__ == "__main__":
    main()
