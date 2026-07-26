#!/usr/bin/env python3
"""Download PBR materials and HDRIs from Poly Haven for the Material Library demo.

Usage:
    python3 tools/download_polyhaven.py              # download all
    python3 tools/download_polyhaven.py --dry-run    # list what would be downloaded
    python3 tools/download_polyhaven.py --materials-only
    python3 tools/download_polyhaven.py --hdris-only
"""

import argparse
import json
import sys
import time
import urllib.request
from pathlib import Path

USER_AGENT = "FNA_Test_MaterialLibrary/1.0 (testing FNA3D_HLSL PBR pipeline)"
API_BASE = "https://api.polyhaven.com"
ASSETS_DIR = Path(__file__).resolve().parent.parent / "assets" / "materials"

# ── Material selection ──────────────────────────────────────────────────────
# 8 diverse materials, all with complete PBR map coverage at 2k JPG

MATERIALS = [
    "metal_plate",            # brushed steel (has separate Metal map)
    "corrugated_iron",        # wavy painted metal (has separate Metal map)
    "rust_coarse_01",         # heavy orange rust
    "green_metal_rust",       # verdigris patina (aged bronze look)
    "marble_01",              # polished white stone
    "leather_red_02",         # rich red leather (uses coll1, not Diffuse)
    "ceramic_roof_01",        # glazed ceramic tiles
    "fine_grained_wood",      # varnished wood
]

# For each material: map_type → (polyhaven API key, our output name)
# Some materials use nonstandard naming (coll1 instead of Diffuse, etc.)
# The "arm" map (AO+Roughness+Metallic packed) is always downloaded as fallback

RESOLUTION = "2k"
MAP_FORMAT = "jpg"

# ── HDRI selection ──────────────────────────────────────────────────────────
HDRIS = [
    "brown_photostudio_02",   # warm studio
    "photo_studio_01",        # neutral studio
    "studio_small_03",        # bright indoor
]
HDRI_RESOLUTION = "2k"
HDRI_FORMAT = "hdr"


def api_request(endpoint: str, max_retries: int = 3) -> dict:
    """Rate-limited API request with retries."""
    url = f"{API_BASE}/{endpoint}"
    for attempt in range(max_retries):
        time.sleep(0.8)
        try:
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=30) as resp:
                return json.loads(resp.read().decode())
        except Exception as e:
            if attempt < max_retries - 1:
                wait = 2 ** attempt
                print(f"\n    [retry {attempt+1}/{max_retries} in {wait}s: {e}]",
                      end="", flush=True)
                time.sleep(wait)
            else:
                raise


def download_file(url: str, dest: Path) -> bool:
    """Download a file. Returns True on success."""
    if dest.exists():
        print(f"    [skip] {dest.name} (exists)")
        return True

    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"    ↓ {dest.name} ...", end="", flush=True)
    try:
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = resp.read()
        dest.write_bytes(data)
        print(f" {len(data)//1024} KB ✓")
        return True
    except Exception as e:
        print(f" ✗ {e}")
        return False


def try_get_url(files_data: dict, api_keys: list, resolution: str,
                formats: list = None) -> tuple:
    """Try to find a download URL from a list of API key + format combinations.
    Returns (url, output_ext, actual_api_key) or (None, None, None).
    """
    if formats is None:
        formats = [MAP_FORMAT, "png", "exr"]

    for api_key in api_keys:
        if api_key not in files_data:
            continue
        map_data = files_data[api_key]
        if not isinstance(map_data, dict):
            continue
        if resolution not in map_data:
            # Try case-insensitive (2k vs 2K)
            for rk in map_data:
                if rk.lower() == resolution.lower():
                    resolution = rk
                    break
            if resolution not in map_data:
                continue
        for fmt in formats:
            if fmt in map_data[resolution]:
                info = map_data[resolution][fmt]
                return info["url"], fmt, api_key
    return None, None, None


def download_material(asset_id: str, dry_run: bool = False):
    """Download all PBR maps for one material."""
    print(f"\n── {asset_id} ──")
    files_data = api_request(f"files/{asset_id}")

    # Determine which maps to download.
    # Priority order for each PBR component:
    downloads = [
        # (our_label, [api_keys_to_try])
        ("albedo",    ["Diffuse", "coll1", "coll2"]),
        ("normal",    ["nor_gl"]),
        ("roughness", ["Rough"]),
        ("ao",        ["AO"]),
        ("metallic",  ["Metal"]),        # only some materials have this
        ("packed",    ["arm"]),          # AO+Roughness+Metallic packed
        ("displacement", ["Displacement"]),
    ]

    for label, api_keys in downloads:
        url, fmt, found_key = try_get_url(files_data, api_keys, RESOLUTION)
        if url is None:
            if label in ("metallic", "displacement"):
                # These are optional — many materials don't have them
                print(f"  - {label} (not available, using arm.G or 0)")
            else:
                print(f"  ✗ {label} — not found (tried: {', '.join(api_keys)})")
            continue

        filename = f"{asset_id}_{label}.{fmt}"
        dest = ASSETS_DIR / asset_id / filename

        if dry_run:
            # Find file size from the API data
            try:
                size_mb = files_data[found_key][RESOLUTION][fmt].get("size", 0) / (1024**2)
                print(f"  → {filename} ({size_mb:.1f} MB)")
            except (KeyError, TypeError):
                print(f"  → {filename}")
        else:
            download_file(url, dest)


def download_hdri(asset_id: str, dry_run: bool = False):
    """Download an HDRI environment map."""
    print(f"\n── {asset_id} (HDRI) ──")
    files_data = api_request(f"files/{asset_id}")

    url, fmt, _ = try_get_url(files_data, ["hdri"], HDRI_RESOLUTION,
                               formats=[HDRI_FORMAT, "exr"])
    if url is None:
        print(f"  ✗ No HDRI file at {HDRI_RESOLUTION}")
        return

    filename = f"{asset_id}.{fmt}"
    dest = ASSETS_DIR / "hdris" / filename

    if dry_run:
        print(f"  → {filename}")
    else:
        download_file(url, dest)


def main():
    parser = argparse.ArgumentParser(description="Download Poly Haven PBR assets")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--materials-only", action="store_true")
    parser.add_argument("--hdris-only", action="store_true")
    args = parser.parse_args()

    do_mats = not args.hdris_only
    do_hdris = not args.materials_only

    print(f"Target: {ASSETS_DIR.resolve()}")
    print(f"Resolution: {RESOLUTION} JPG, HDRI: {HDRI_RESOLUTION} {HDRI_FORMAT}")
    print(f"Materials: {len(MATERIALS)}, HDRIs: {len(HDRIS)}")
    print("=" * 60)

    if do_mats:
        for m in MATERIALS:
            download_material(m, dry_run=args.dry_run)

    if do_hdris:
        for h in HDRIS:
            download_hdri(h, dry_run=args.dry_run)

    if args.dry_run:
        print("\nDry run done. Run without --dry-run to download.")
    else:
        total = sum(1 for _ in ASSETS_DIR.rglob("*") if _.is_file())
        total_sz = sum(_.stat().st_size for _ in ASSETS_DIR.rglob("*") if _.is_file())
        print(f"\n✓ Done. {total} files, {total_sz/(1024**2):.1f} MB in {ASSETS_DIR}")


if __name__ == "__main__":
    main()
