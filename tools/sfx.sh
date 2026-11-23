#!/usr/bin/env bash
# Runs the sound-effect tool (tools/SfxGen) from anywhere in the repo.
#
#   tools/sfx.sh render          write every sound into game/audio/sfx and print its numbers
#   tools/sfx.sh play card-play  render one sound and play it
#   tools/sfx.sh check           run every check
#   tools/sfx.sh board           write the sound board page to build/sfx/board.html
#
# See tools/SfxGen/README.md for the recipe format and the other commands.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
exec dotnet run --project tools/SfxGen -c Release -- "$@"
