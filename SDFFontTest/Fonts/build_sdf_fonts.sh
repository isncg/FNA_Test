#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# SDF Font Atlas Builder
# ─────────────────────────────────────────────────────────────────────────────
# Generates SDF font atlases for the SDFFontTest program using msdf-atlas-gen.
#
# Quick start:
#   ./build_sdf_fonts.sh              # build both EN + CN with defaults
#   ./build_sdf_fonts.sh en           # build English only
#   ./build_sdf_fonts.sh cn           # build Chinese only
#
# Custom overrides (environment variables):
#   EN_SIZE=160 CN_SIZE=36 ./build_sdf_fonts.sh
#   CN_RANGE=8 CN_PAD=0 ./build_sdf_fonts.sh cn
#
# All tunable parameters (and their defaults):
#   EN_FONT    — path to English .ttf/.otf
#                (default: /usr/share/fonts/liberation/LiberationSans-Regular.ttf)
#   EN_CHARSET — charset file or string
#                (default: ../../tools/charset_en_ascii.txt)
#   EN_SIZE    — reference font size in pixels        (default: 160)
#   EN_ATLAS   — atlas width/height in pixels         (default: 2048)
#   EN_RANGE   — SDF distance range in pixels         (default: auto = max(4, EN_SIZE/8))
#   EN_PAD     — pixel padding between glyphs         (default: 0)
#
#   CN_FONT    — path to Chinese .ttf/.otf/.ttc
#                (default: /usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc)
#   CN_CHARSET — charset file or string
#                (default: ../../tools/charset_cjk_common.txt)
#   CN_SIZE    — reference font size in pixels        (default: 36)
#   CN_ATLAS   — atlas width/height in pixels         (default: 4096)
#   CN_RANGE   — SDF distance range in pixels         (default: 8)
#   CN_PAD     — pixel padding between glyphs         (default: 0)
#
#   CN_TTC_INDEX — font index within .ttc file        (default: 2 = SC)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILDER="$(cd "$SCRIPT_DIR/../../tools" && pwd)/sdf_font_builder.py"
MSDF_BIN="$(cd "$SCRIPT_DIR/../../tools" && pwd)/msdf-atlas-gen"

# ── Check prerequisites ─────────────────────────────────────────────────────
if [ ! -f "$MSDF_BIN" ]; then
    echo "ERROR: msdf-atlas-gen not found at $MSDF_BIN" >&2
    echo "Download from: https://github.com/Chlumsky/msdf-atlas-gen" >&2
    exit 1
fi

# ── User-selectable targets ─────────────────────────────────────────────────
TARGET="${1:-all}"

# ── Parameter defaults (override via env vars) ──────────────────────────────
# English
EN_FONT="${EN_FONT:-/usr/share/fonts/liberation/LiberationSans-Regular.ttf}"
EN_CHARSET="${EN_CHARSET:-$SCRIPT_DIR/../../tools/charset_en_ascii.txt}"
EN_SIZE="${EN_SIZE:-160}"
EN_ATLAS="${EN_ATLAS:-2048}"
EN_RANGE="${EN_RANGE:-}"      # empty = auto
EN_PAD="${EN_PAD:-0}"

# Chinese
CN_FONT="${CN_FONT:-/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc}"
CN_CHARSET="${CN_CHARSET:-$SCRIPT_DIR/../../tools/charset_cjk_common.txt}"
CN_SIZE="${CN_SIZE:-36}"
CN_ATLAS="${CN_ATLAS:-4096}"
CN_RANGE="${CN_RANGE:-8}"
CN_PAD="${CN_PAD:-0}"
CN_TTC_INDEX="${CN_TTC_INDEX:-2}"

# Temporary extracted OTF for TTC sources
CN_OTF=""

# ── Helpers ─────────────────────────────────────────────────────────────────
cleanup() {
    if [ -n "$CN_OTF" ] && [ -f "$CN_OTF" ]; then
        rm -f "$CN_OTF"
    fi
}
trap cleanup EXIT

build_font() {
    local name="$1" font="$2" charset="$3" size="$4" atlas="$5" range="$6" pad="$7" prefix="$8"

    echo ""
    echo "══════════════════════════════════════════════════════════════════"
    echo "  Building $name SDF atlas"
    echo "══════════════════════════════════════════════════════════════════"
    echo "  Font:      $font"
    echo "  Charset:   $charset"
    echo "  Size:      ${size}px"
    echo "  Atlas:     ${atlas}²"
    echo "  pxrange:   ${range:-auto}"
    echo "  pxpadding: $pad"
    echo "  Output:    ${prefix}_atlas.png + ${prefix}_metrics.json"
    echo ""

    local range_args=()
    if [ -n "$range" ]; then
        range_args=(--pxrange "$range")
    fi

    python3 "$BUILDER" \
        "$font" "$charset" "$size" "$atlas" \
        "$SCRIPT_DIR" "$prefix" \
        --pxpadding "$pad" \
        "${range_args[@]}"
}

# ── Build English font ──────────────────────────────────────────────────────
build_en() {
    if [ ! -f "$EN_FONT" ]; then
        echo "ERROR: EN font not found: $EN_FONT" >&2
        return 1
    fi
    build_font "English (EN)" "$EN_FONT" "$EN_CHARSET" \
        "$EN_SIZE" "$EN_ATLAS" "$EN_RANGE" "$EN_PAD" "en"
}

# ── Build Chinese font ──────────────────────────────────────────────────────
build_cn() {
    local font="$CN_FONT"

    if [[ "$font" == *.ttc ]] || [[ "$font" == *.ttc ]] || [[ "$font" == *.TTC ]]; then
        CN_OTF="/tmp/NotoSansCJK_SC_$$.otf"
        echo "  Extracting TTC face index $CN_TTC_INDEX..."
        python3 -c "
from fontTools.ttLib import TTCollection
ttc = TTCollection('$font')
ttc[$CN_TTC_INDEX].save('$CN_OTF')
print(f'  Extracted to $CN_OTF')
"
        font="$CN_OTF"
    fi

    if [ ! -f "$font" ]; then
        echo "ERROR: CN font not found: $font" >&2
        return 1
    fi

    build_font "Chinese (CN)" "$font" "$CN_CHARSET" \
        "$CN_SIZE" "$CN_ATLAS" "$CN_RANGE" "$CN_PAD" "cn"
}

# ── Main ────────────────────────────────────────────────────────────────────
case "$TARGET" in
    all)
        build_en
        build_cn
        ;;
    en)
        build_en
        ;;
    cn)
        build_cn
        ;;
    *)
        echo "Usage: $0 {all|en|cn}" >&2
        exit 1
        ;;
esac

echo ""
echo "══════════════════════════════════════════════════════════════════"
echo "  Done. Atlas files in: $SCRIPT_DIR/"
ls -lh "$SCRIPT_DIR/en_atlas.png" "$SCRIPT_DIR/en_metrics.json" 2>/dev/null || true
ls -lh "$SCRIPT_DIR/cn_atlas.png" "$SCRIPT_DIR/cn_metrics.json" 2>/dev/null || true
echo ""
echo "  Rebuild and run:  dotnet run --project SDFFontTest"
echo "══════════════════════════════════════════════════════════════════"
