#!/usr/bin/env python3
"""
SDF Font Builder — wrapper around msdf-atlas-gen by Viktor Chlumsky.

Usage:
  python3 sdf_font_builder.py <font> <charset.txt> <fontSize> <atlasSize> <outDir> <prefix>
  python3 sdf_font_builder.py <font> --chars 'ABC' <fontSize> <atlasSize> <outDir> <prefix>
  python3 sdf_font_builder.py <font> <charset.txt> <fontSize> <atlasSize> <outDir> <prefix> \\
      --pxrange 4 --fontindex 0 --pxpadding 2

Output files (written to <outDir>/):
  <prefix>_atlas.png   — SDF atlas PNG (single-channel grayscale)
  <prefix>_metrics.json — glyph metrics (msdf-atlas-gen format)
"""

import argparse
import json
import os
import subprocess
import sys

# Path to msdf-atlas-gen binary, relative to this script
MSDF_BIN = os.path.join(os.path.dirname(os.path.abspath(__file__)), "msdf-atlas-gen")


def charset_to_hex(charset: str) -> str:
    """Convert a string of characters to comma-separated hex codepoints."""
    seen = set()
    codes = []
    for c in charset:
        if c in ('\n', '\r'):
            continue
        cp = ord(c)
        if cp not in seen:
            seen.add(cp)
            codes.append(f"0x{cp:X}")
    return ",".join(codes)


def build_font(font_path: str, charset: str, font_size: int,
               atlas_size: int, out_dir: str, prefix: str,
               pxrange: int = None, pxpadding: int = 2):
    """Build an SDF atlas using msdf-atlas-gen."""
    os.makedirs(out_dir, exist_ok=True)

    hex_chars = charset_to_hex(charset)
    if not hex_chars:
        print("ERROR: Empty character set", file=sys.stderr)
        sys.exit(1)

    if pxrange is None:
        pxrange = max(4, font_size // 8)

    png_path = os.path.join(out_dir, f"{prefix}_atlas.png")
    json_path = os.path.join(out_dir, f"{prefix}_metrics.json")

    print(f"Font: {font_path}")
    print(f"Characters: {len(hex_chars.split(','))} glyphs")
    print(f"Size: {font_size}px, Atlas: {atlas_size}², pxrange: {pxrange}, pxpadding: {pxpadding}")

    cmd = [
        MSDF_BIN,
        "-font", font_path,
        "-chars", hex_chars,
        "-type", "sdf",
        "-format", "png",
        "-size", str(font_size),
        "-pxrange", str(pxrange),
        "-pxpadding", str(pxpadding),
        "-dimensions", str(atlas_size), str(atlas_size),
        "-imageout", png_path,
        "-json", json_path,
    ]

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"msdf-atlas-gen failed:", file=sys.stderr)
        print(result.stderr, file=sys.stderr)
        sys.exit(result.returncode)

    # Print summary from stdout
    for line in result.stdout.strip().split('\n'):
        print(f"  {line.strip()}")

    # Verify output
    if not os.path.exists(png_path):
        print(f"ERROR: Output PNG not found at {png_path}", file=sys.stderr)
        sys.exit(1)
    if not os.path.exists(json_path):
        print(f"ERROR: Output JSON not found at {json_path}", file=sys.stderr)
        sys.exit(1)

    print(f"Wrote: {png_path} ({os.path.getsize(png_path)} bytes)")
    print(f"Wrote: {json_path} ({os.path.getsize(json_path)} bytes)")


def main():
    parser = argparse.ArgumentParser(
        description='Generate SDF font atlas via msdf-atlas-gen')
    parser.add_argument('font', help='Path to .ttf, .otf, or .ttc font file')
    parser.add_argument('charset', nargs='?', default='',
                        help='Path to charset file (UTF-8 text), optional if --chars is used')
    parser.add_argument('font_size', type=int, help='Reference font size in pixels')
    parser.add_argument('atlas_size', type=int, help='Atlas width/height in pixels')
    parser.add_argument('out_dir', help='Output directory')
    parser.add_argument('prefix', help='Output filename prefix (e.g., cn, en)')
    parser.add_argument('--limit', type=int, default=0,
                        help='Limit number of characters (for testing)')
    parser.add_argument('--chars', type=str, default='',
                        help='Direct character string instead of file')
    parser.add_argument('--pxrange', type=int, default=None,
                        help='SDF distance range in pixels (default: max(4, fontSize/8))')
    parser.add_argument('--pxpadding', type=int, default=2,
                        help='Pixel padding between glyphs (default: 2)')
    args = parser.parse_args()

    if args.chars:
        charset = args.chars
    elif args.charset:
        with open(args.charset, 'r', encoding='utf-8') as f:
            charset = f.read()
    else:
        parser.error('Either charset file or --chars must be provided')

    if args.limit > 0:
        charset = charset[:args.limit]

    build_font(args.font, charset, args.font_size,
               args.atlas_size, args.out_dir, args.prefix,
               pxrange=args.pxrange, pxpadding=args.pxpadding)


if __name__ == '__main__':
    main()
