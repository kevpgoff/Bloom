#!/usr/bin/env bash
# Builds the game's music from Voltz Supreme's "Analog Ambience" pack.
#
#   tools/import-music.sh
#
# The pack is paid, and its license doesn't cover sharing the files, so the repo holds neither the zips nor the
# music built from them (both are in .gitignore). Download the pack's zips from itch.io into music/ (or set
# MUSIC_DIR), then run this script. Without it, the game runs in silence.
#
# tools/music-tracks.tsv lists the files to build: which WAV from the zips, the mood it plays in, and optional cuts.
# Each file is streamed out of its zip (nothing is extracted), cut if the row asks for it, set to the same loudness
# and written as OGG Vorbis to game/audio/music/<mood>/<name>.ogg. The folder is rebuilt from scratch every time,
# so a row removed from the list leaves no file behind. Last, Godot imports the new files, so that the editor, the
# game and tools/android-debug.sh can use them. Set GODOT if it is not in the default place below.
set -euo pipefail

MUSIC_DIR="${MUSIC_DIR:-music}"
GODOT="${GODOT:-$HOME/Applications/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64}"
TRACKS=tools/music-tracks.tsv
OUT=game/audio/music

# Every file is brought to this integrated loudness (EBU R128), unless that would push its true peak above
# PEAK_CEILING; then it gets as much gain as the ceiling allows. Only the gain changes, never the dynamics.
TARGET_LUFS=-18
PEAK_CEILING=-1
# Vorbis quality, -1 to 10. 3 is about 112 kbps for stereo.
QUALITY=3

cd "$(dirname "$0")/.."

for tool in ffmpeg ffprobe unzip; do
  if ! command -v "$tool" >/dev/null; then
    echo "$tool is not installed." >&2
    exit 1
  fi
done
if ! ffmpeg -hide_banner -encoders 2>/dev/null | grep -q libvorbis; then
  echo "This ffmpeg can't encode Vorbis (libvorbis is missing)." >&2
  exit 1
fi

shopt -s nullglob
zips=("$MUSIC_DIR"/*.zip)
if [ ${#zips[@]} -eq 0 ]; then
  echo "No zips in $MUSIC_DIR/. Download Voltz Supreme's \"Analog Ambience\" pack there first." >&2
  exit 1
fi

# Which zip holds each WAV.
declare -A zip_of
for zip in "${zips[@]}"; do
  while IFS= read -r member; do
    zip_of["$member"]="$zip"
  done < <(unzip -Z1 "$zip")
done

# Checks every row before anything is deleted or written.
rows=()
line_no=0
while IFS= read -r line || [ -n "$line" ]; do
  line_no=$((line_no + 1))
  line="${line%$'\r'}"
  [[ -z "$line" || "$line" == \#* ]] && continue
  IFS=$'\t' read -r mood name source start end fade extra <<<"$line"
  where="$TRACKS:$line_no"
  if [ -z "${fade:-}" ] || [ -n "${extra:-}" ]; then
    echo "$where: expected 6 columns separated by tabs (write - for an empty one)." >&2
    exit 1
  fi
  case "$mood" in
    voyage | fight | boss | stings) ;;
    *) echo "$where: unknown mood \"$mood\"." >&2; exit 1 ;;
  esac
  if [[ ! "$name" =~ ^[a-z0-9]+(-[a-z0-9]+)*$ ]]; then
    echo "$where: the name \"$name\" must be lowercase words joined by hyphens." >&2
    exit 1
  fi
  if [ -z "${zip_of[$source]+set}" ]; then
    echo "$where: \"$source\" is in none of the zips in $MUSIC_DIR/." >&2
    exit 1
  fi
  for value in "$start" "$end" "$fade"; do
    if [ "$value" != - ] && [[ ! "$value" =~ ^[0-9]+(\.[0-9]+)?$ ]]; then
      echo "$where: \"$value\" is not a number of seconds or -." >&2
      exit 1
    fi
  done
  if [ "$fade" != - ] && [ "$end" = - ]; then
    echo "$where: a fade-out needs an end." >&2
    exit 1
  fi
  rows+=("$line")
done <"$TRACKS"

for sting in won lost; do
  if ! grep -q "^stings"$'\t'"$sting"$'\t' <<<"$(printf '%s\n' "${rows[@]}")"; then
    echo "$TRACKS: the stings need a file named $sting." >&2
    exit 1
  fi
done

rm -rf "$OUT"
total_seconds=0
for line in "${rows[@]}"; do
  IFS=$'\t' read -r mood name source start end fade _ <<<"$line"
  zip="${zip_of[$source]}"
  target="$OUT/$mood/$name.ogg"
  mkdir -p "$OUT/$mood"

  # The cuts, as one ffmpeg filter chain that the measurement and the encoding share.
  filters="anull"
  if [ "$start" != - ] || [ "$end" != - ]; then
    trim="atrim"
    [ "$start" != - ] && trim+="=start=$start"
    if [ "$end" != - ]; then
      [ "$start" != - ] && trim+=":end=$end" || trim+="=end=$end"
    fi
    filters="$trim,asetpts=PTS-STARTPTS"
  fi
  if [ "$fade" != - ]; then
    length=$(awk -v s="${start/-/0}" -v e="$end" 'BEGIN { print e - s }')
    fade_start=$(awk -v l="$length" -v f="$fade" 'BEGIN { print (l > f) ? l - f : 0 }')
    filters+=",afade=t=out:st=$fade_start:d=$fade"
  fi

  # ffmpeg stops reading at the end of a cut, so unzip may be stopped by a broken pipe; that is not an error.
  measured=$({ unzip -p "$zip" "$source" || true; } |
    ffmpeg -nostats -hide_banner -f wav -i - -af "$filters,ebur128=peak=true" -f null - 2>&1 || true)
  lufs=$(awk '$1 == "I:" { v = $2 } END { print v }' <<<"$measured")
  peak=$(awk '$1 == "Peak:" { v = $2 } END { print v }' <<<"$measured")
  if [ -z "$lufs" ] || [ -z "$peak" ]; then
    echo "Could not measure the loudness of \"$source\":" >&2
    tail -n 5 <<<"$measured" >&2
    exit 1
  fi
  gain=$(awk -v t="$TARGET_LUFS" -v c="$PEAK_CEILING" -v i="$lufs" -v p="$peak" \
    'BEGIN { g = t - i; if (c - p < g) g = c - p; printf "%.2f", g }')

  title="${source%.wav}"
  title="${title#Voltz Supreme - }"
  title="${title#[0-9][0-9] - }"
  { unzip -p "$zip" "$source" || true; } |
    ffmpeg -nostats -hide_banner -loglevel error -f wav -i - -af "$filters,volume=${gain}dB" \
      -map_metadata -1 -metadata title="$title" -metadata artist="Voltz Supreme" \
      -c:a libvorbis -q:a "$QUALITY" "$target"

  seconds=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$target")
  total_seconds=$(awk -v a="$total_seconds" -v b="$seconds" 'BEGIN { print a + b }')
  printf '%-44s %6s  %6.1f LUFS  %+6.2f dB\n' "$target" \
    "$(awk -v s="$seconds" 'BEGIN { printf "%d:%02d", s / 60, s % 60 }')" "$lufs" "$gain"
done

echo "Built ${#rows[@]} files, $(awk -v s="$total_seconds" 'BEGIN { printf "%.1f", s / 60 }') minutes, $(du -sh "$OUT" | cut -f1)B in $OUT."

if [ -x "$GODOT" ]; then
  echo "Importing them into the Godot project…"
  "$GODOT" --headless --path game --import >/dev/null 2>&1 || {
    echo "Godot's import failed. Open the project in the editor once to import the music." >&2
    exit 1
  }
  echo "Done."
else
  echo "Godot was not found at $GODOT. Set GODOT, or open the project in the editor once to import the music."
fi
