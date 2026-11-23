#!/usr/bin/env bash
# Exports a debug APK of the game, installs it on the phone connected by USB and starts it.
#
#   tools/android-debug.sh               export, install and start
#   tools/android-debug.sh --no-install  only export build/android/bloom-debug.apk
#   tools/android-debug.sh --log         also follow the game's log after it starts (Ctrl+C to stop)
#
# The one-time setup (JDK, Android SDK and NDK, export templates, debug keystore) is in README.md.
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

# The game is compiled ahead of time (NativeAOT) and linked with the Android NDK's clang. Bloom.csproj finds the
# NDK through ANDROID_NDK_ROOT; without it, the newest NDK installed in the SDK is used.
if [ -z "${ANDROID_NDK_ROOT:-}" ]; then
  ANDROID_NDK_ROOT="$(ls -d "${ANDROID_HOME:-$HOME/Android/Sdk}"/ndk/*/ 2>/dev/null | sort -V | tail -n 1)"
  ANDROID_NDK_ROOT="${ANDROID_NDK_ROOT%/}"
fi
if [ ! -x "$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin/clang" ]; then
  echo "Android NDK not found. Install it as README.md describes, or set ANDROID_NDK_ROOT." >&2
  exit 1
fi
export ANDROID_NDK_ROOT

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
