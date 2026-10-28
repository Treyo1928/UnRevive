#!/usr/bin/env bash
# Build UnRevive (BepInEx 5, Attack on Quest PCVR).
#
#   bash build.sh            build + package dist/UnRevive-<version>.zip
#   bash build.sh --deploy   ...and install into $AOQ_GAME_DIR
#
# AOQ_GAME_DIR must point at a PCVR install with BepInEx 5 already extracted into it; the
# reference DLLs in Libraries/ are copied from there on first build.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="${AOQ_GAME_DIR:-../AoQ_PCVR_OpenVRTest}"
CONFIG="${CONFIG:-Release}"
VERSION="1.0.0"

MANAGED="$GAME_DIR/AttackOnQuest_Data/Managed"
BEPCORE="$GAME_DIR/BepInEx/core"

mkdir -p Libraries
for dll in Assembly-CSharp Oculus.VR UnityEngine UnityEngine.AudioModule UnityEngine.CoreModule UnityEngine.UI UnityEngine.VRModule UnityEngine.XRModule; do
    [ -f "Libraries/$dll.dll" ] || cp "$MANAGED/$dll.dll" Libraries/
done
for dll in BepInEx 0Harmony Mono.Cecil; do
    [ -f "Libraries/$dll.dll" ] || cp "$BEPCORE/$dll.dll" Libraries/
done

msbuild -nologo -verbosity:minimal -p:Configuration="$CONFIG" UnRevive.sln

STAGE=dist/stage
rm -rf dist
mkdir -p "$STAGE/BepInEx/patchers/UnRevive/OpenComposite" "$STAGE/BepInEx/plugins/UnRevive"
cp "Patcher/bin/$CONFIG/UnRevive.Patcher.dll" Native/openvr_api.dll Native/LICENSE-openvr.txt \
   "$STAGE/BepInEx/patchers/UnRevive/"
cp Native/OpenComposite/* "$STAGE/BepInEx/patchers/UnRevive/OpenComposite/"
cp "Plugin/bin/$CONFIG/UnRevive.dll" "$STAGE/BepInEx/plugins/UnRevive/"
(cd "$STAGE" && zip -qr "../UnRevive-$VERSION.zip" BepInEx)
echo "Packaged dist/UnRevive-$VERSION.zip"

if [[ "${1:-}" == "--deploy" ]]; then
    # Replace files rather than overwrite them in place, so a running game keeps its copies.
    (cd "$STAGE" && find BepInEx -type f) | while read -r f; do
        mkdir -p "$GAME_DIR/$(dirname "$f")"
        cp --remove-destination "$STAGE/$f" "$GAME_DIR/$f"
    done
    echo "Deployed to $GAME_DIR/BepInEx"
fi
