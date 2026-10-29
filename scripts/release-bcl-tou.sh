#!/usr/bin/env bash
# Builds and publishes a BetterCrewLink-TOU release with one voice bridge per TOU-Mira version.
#
#   scripts/release-bcl-tou.sh <bcl-tou-version> <tou-mira-tag>...
#   scripts/release-bcl-tou.sh 3.2.3-alpha.3 1.7.9-beta1 1.7.3
#
# Release assets (the TOU-Mira installer and the in-app updater depend on these names):
#   BetterCrewLink-TOU-Setup-<version>.exe (+ .blockmap, latest.yml)   the app
#   TouMiraBclBridge-<tou-mira-tag>.dll                               bridge for that TOU-Mira release
# Each bridge is compiled against that TOU-Mira release's official DLLs, so it binds to exactly
# the version players install. Versions with a "-" (e.g. -alpha.3) are published as prereleases.
#
# Commits and pushes the version bump in BetterCrewlink/ and the submodule pointer here.
set -euo pipefail

VERSION=${1:?usage: $0 <bcl-tou-version> <tou-mira-tag>...}
shift
TOU_TAGS=("$@")
[ ${#TOU_TAGS[@]} -gt 0 ] || { echo "give at least one TOU-Mira tag" >&2; exit 1; }

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/.bcl-release"
REFS="$ROOT/.release-refs"
cd "$ROOT"

for repo in . BetterCrewlink; do
	if [ -n "$(git -C "$repo" status --porcelain --untracked-files=no -- . ':!BetterCrewlink')" ]; then
		echo "$repo has uncommitted changes; commit them first" >&2
		exit 1
	fi
done
rm -rf "$OUT" && mkdir -p "$OUT"

# 1. One bridge per TOU-Mira release, compiled against that release's official DLLs.
for tag in "${TOU_TAGS[@]}"; do
	echo "== bridge for TOU-Mira $tag"
	rm -rf "$REFS/$tag" && mkdir -p "$REFS/$tag"
	gh release download "$tag" -R AU-Avengers/TOU-Mira -p '*[Ss]team*.zip' -D "$REFS/$tag"
	python - "$REFS/$tag" <<'PY'
import sys, zipfile, pathlib
d = pathlib.Path(sys.argv[1])
z = zipfile.ZipFile(next(d.glob("*.zip")))
for want in ("TownOfUsMira.dll", "MiraAPI.dll"):
    name = next(n for n in z.namelist() if n.endswith("BepInEx/plugins/" + want))
    (d / want).write_bytes(z.read(name))
PY
	refs_win="$(cd "$REFS/$tag" && pwd -W)/"
	dotnet build TouMiraBclBridge -c Release --no-incremental -v q -nologo \
		-p:TouMiraDir="$refs_win" -p:MiraApiDir="$refs_win" -o "$REFS/$tag/out"
	cp "$REFS/$tag/out/TouMiraBclBridge.dll" "$OUT/TouMiraBclBridge-$tag.dll"
done

# 2. The app.
echo "== BetterCrewLink-TOU $VERSION"
cd "$ROOT/BetterCrewlink"
if [ "$(node -p "require('./package.json').version")" != "$VERSION" ]; then
	npm version "$VERSION" --no-git-tag-version --ignore-scripts >/dev/null
	git commit -q -am "Release BetterCrewLink-TOU $VERSION"
fi
npm run typecheck && npm run lint && npm run format:check
rm -rf dist && npm run dist:64
git push -q
# electron-builder names files with spaces, latest.yml refers to dashes, and GitHub would turn
# spaces into dots: upload under the dashed names or auto-update breaks.
cp "dist/BetterCrewLink-TOU Setup $VERSION.exe" "$OUT/BetterCrewLink-TOU-Setup-$VERSION.exe"
cp "dist/BetterCrewLink-TOU Setup $VERSION.exe.blockmap" "$OUT/BetterCrewLink-TOU-Setup-$VERSION.exe.blockmap"
cp dist/latest.yml "$OUT/"

# 3. Publish.
supported=$(IFS=,; echo "${TOU_TAGS[*]}" | sed 's/,/, /g')
notes="$OUT/notes.md"
{
	echo "Built for TOU-Mira: $supported"
	echo
	echo "Install with the [TOU-Mira installer](https://github.com/rroig7/tou-mira-installer) (tick **BetterCrewLink-TOU + voice bridge**). It picks the bridge matching your TOU-Mira version."
	echo
	echo "By hand: run the setup, then copy the \`TouMiraBclBridge-<your TOU-Mira version>.dll\` to \`Among Us/BepInEx/plugins/TouMiraBclBridge.dll\`."
} > "$notes"
prerelease=()
[[ "$VERSION" == *-* ]] && prerelease=(--prerelease)
gh release create "v$VERSION" "$OUT"/*.exe "$OUT"/*.blockmap "$OUT"/latest.yml "$OUT"/*.dll \
	-R rroig7/BetterCrewLink --target tou-mira "${prerelease[@]}" \
	--title "BetterCrewLink-TOU v$VERSION" --notes-file "$notes"

cd "$ROOT"
git add BetterCrewlink
git diff --cached --quiet || { git commit -q -m "Bump BetterCrewlink to $VERSION"; git push -q; }
echo "== released v$VERSION; edit the notes on GitHub to describe what changed"
