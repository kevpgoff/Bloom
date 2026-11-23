#!/usr/bin/env bash
# Exports a debug APK of the game, installs it on the phone connected by USB and starts it.
#
#   tools/android-debug.sh               export, install and start
#   tools/android-debug.sh --no-install  only export build/android/bloom-debug.apk
#   tools/android-debug.sh --log         also follow the game's log after it starts (Ctrl+C to stop)
#
# The one-time setup (JDK, Android SDK, export templates, debug keystore) is in README.md.
# Set GODOT or ANDROID_HOME if they are not in the default places below.
set -euo pipefail

GODOT="${GODOT:-$HOME/Applications/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64}"
ADB="${ANDROID_HOME:-$HOME/Android/Sdk}/platform-tools/adb"
PACKAGE=com.kevpgoff.bloom
APK=build/android/bloom-debug.apk

install=1
log=0
for arg in "$@"; do
  case "$arg" in
    --no-install) install=0 ;;
    --log) log=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

cd "$(dirname "$0")/.."
mkdir -p "$(dirname "$APK")"
rm -f "$APK"

# The export reads the editor's settings (JDK and SDK paths, debug keystore), so XDG_* must not point at a scratch
# folder here. Exporting never runs the game, so the desktop save is not touched.
"$GODOT" --headless --path game --export-debug Android "../$APK"
if [ ! -f "$APK" ]; then
  echo "Export failed: $APK was not written. See the errors above." >&2
  exit 1
fi
echo "Exported $APK"
[ "$install" = 1 ] || exit 0

"$ADB" install -r "$APK"
"$ADB" shell am start -W -n "$PACKAGE/com.godot.game.GodotAppLauncher"
if [ "$log" = 1 ]; then
  "$ADB" logcat --pid="$("$ADB" shell pidof "$PACKAGE")"
fi
