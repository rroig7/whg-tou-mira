# WHG TOU-Mira × BetterCrewLink

Makes BetterCrewLink (BCL) aware of Town of Us: Mira (TOU-Mira) role effects. For example, a
jailed, blackmailed or Puppeteer-controlled player can't be heard. The rules are set by the lobby
host on a **Settings → Town of Us: Mira** page in BCL.

## Getting started

```sh
git clone --recurse-submodules git@github.com:rroig7/whg-tou-mira.git
cd whg-tou-mira
git -C BetterCrewlink checkout tou-mira      # submodules check out detached; work on the branch
```

Then follow **Building** below.

## Repository layout

This is an **umbrella repo**. It holds the code that belongs to neither upstream project (the
bridge plugin, docs, tests) and pins the two upstream projects as git submodules:

```
whg-tou-mira/        github.com/rroig7/whg-tou-mira
├── BetterCrewlink/  submodule → github.com/rroig7/BetterCrewLink (fork), branch tou-mira
├── TOU-Mira/        submodule → github.com/AU-Avengers/TOU-Mira (official), pinned to 1.7.9-beta1
├── TouMiraBclBridge/ BepInEx plugin that runs inside Among Us (C#)
├── tests/           rules smoke test (no game needed)
├── README.md
└── AGENT.md         instructions for AI coding agents
```

### Why fork BetterCrewLink but not TOU-Mira?

| Project | Forked? | Why |
|---|---|---|
| **BetterCrewLink** | **Yes**: `rroig7/BetterCrewLink` | Its source is modified: the voice rules, settings page and bridge client. We ship our own build of it. |
| **TOU-Mira** | **No** | It is never modified. The bridge only *compiles against* it, the way any add-on mod would. Players keep using the official TOU-Mira release. Not forking means we never merge TOU-Mira updates; we just move the submodule to the new tag. |
| **The bridge** | n/a | New code, lives in this repo. |

Keep TOU-Mira pinned to the **same release the group plays**, not whatever `main` happens to
be. The bridge reads TOU-Mira's classes directly, so a mismatched version can fail to load.

### Git remotes inside the BCL submodule

| Remote | URL | Use |
|---|---|---|
| `origin` | `git@github.com:rroig7/BetterCrewLink.git` | push our work (branch `tou-mira`) |
| `upstream` | `https://github.com/OhMyGuus/BetterCrewlink.git` | pull official BCL updates (branch `nightly`) |

A fresh clone only has `origin`. Add upstream once:
`git -C BetterCrewlink remote add upstream https://github.com/OhMyGuus/BetterCrewlink.git`

After committing in a submodule, commit the umbrella too, so it records the new submodule commit:

```sh
git -C BetterCrewlink push
git add BetterCrewlink && git commit -m "Bump BetterCrewlink" && git push
```

### How the fork is kept distinct from official BCL

These changes are already made in the fork:

* **`package.json` → `repository.url`** points at `rroig7/BetterCrewLink`. The auto-updater
  checks that repo's GitHub releases. That's the group's update channel, and installs won't
  "update" back to official BCL and lose the TOU features.
* **`electron-builder.yml`**: `appId: com.whg.bettercrewlink.toumira`,
  `productName: BetterCrewLink-TOU`. It installs alongside official BCL, with its own
  settings, so players re-pick their mic and keybinds once.
* **`package.json` → `name: bettercrewlink-tou`**. With the default per-user installer, the
  package name *is* the install folder (`%LOCALAPPDATA%Programs<name>`) and Electron's settings
  folder (`%APPDATA%<name>`). Left as `bettercrewlink`, the fork would install over official BCL
  and share its settings.
* CI installer globs were updated to the new name, and the WinGet workflow was removed (it
  publishes under official BCL's package identity).

## How it works

```
Among Us + TOU-Mira ── TouMiraBclBridge.dll ──TCP 127.0.0.1:47652──▶ BetterCrewLink
   (modifiers: Jailed,      reads modifiers 10×/s,       NDJSON        main: touMiraBridge.ts
    Blackmailed, ...)       pushes on change + 1s                      → AmongUsState.touMira
                            heartbeat                                  renderer: touMiraVoice.ts
                                                                       → gain/muffle per speaker
```

* BCL reads vanilla game state by scanning process memory. TOU-Mira state lives in mod objects
  that scanning can't reach reliably, so a small plugin inside the game reports it instead.
* The bridge is **client-only**. It sends no game RPCs and doesn't change the lobby's mod list.
* **Listener side:** each BCL drops audio from any speaker who is muted by a TOU rule.
* **Speaker side:** a muted player's own BCL also stops sending mic audio and shows "You are
  jailed…". Even peers without the bridge then won't hear them.
* Rules are **lobby settings**: the BCL host decides them and they sync to everyone.
* **Fails open:** if the bridge isn't running, or its data is more than 3s old, BCL behaves
  exactly like vanilla.

The rule semantics copy TOU-Mira's own Perfect Comms integration
(`TOU-Mira/TownOfUs/Integrations/PerfectCommsIntegration.cs`). That file is the reference
for how each role *should* affect voice.

### Implemented rules

| Setting | When |
|---|---|
| Mute blackmailed player | meeting + exile cutscene |
| …keep muted next round (off by default) | the task round after that meeting |
| Mute jailed player | meeting; only while the Jailor is alive unless "persists" is on |
| Mute Puppeteer / Parasite-controlled player | task round |
| Puppeteer hears from victim | Puppeteer's listening position becomes the victim's |
| Parasite also hears from victim | hears around itself **and** its victim |
| Mute Swooper while swooped, mute Glitch-hacked | tasks + meetings |
| Muffle hearing when Eclipsal-blinded / Grenadier-flashed / in Mass Hysteria | task round |

## Building

### Prerequisites

* Windows, git
* [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer (the projects target `net6.0`;
  a newer SDK builds them fine)
* Node.js 22+ and npm

### 1. TOU-Mira (compile reference for the bridge)

```sh
cd TOU-Mira
git submodule update --init          # TOU-Mira's own submodules (MiraAPI, AchievementsAPI)
dotnet build TownOfUs/TownOfUsMira.csproj -c Release -m:1
```

`-m:1` matters: in parallel builds, two projects unpack the Unity libraries at the same time and
fail with a file-in-use error (`MSB4018 … GetUnityLibsAsync`).

### 2. Bridge plugin

```sh
cd TouMiraBclBridge
dotnet build -c Release
# -> TouMiraBclBridge/bin/Release/net6.0/TouMiraBclBridge.dll
# Add -p:AmongUs="C:/Program Files (x86)/Steam/steamapps/common/Among Us" to copy it into BepInEx/plugins.
```

### 3. BetterCrewLink

```sh
cd BetterCrewlink
npm ci            # full install; must run install scripts (native modules + Electron)
npm run dev       # run from source with hot reload
npm run dist:64   # build the Windows installer -> BetterCrewlink/dist/
```

If `npm run dev` starts but can't read the game, or complains about a native module, run
`npm run rebuild:native`.

Checks to run before committing BCL changes:

```sh
npm run typecheck && npm run lint && npm run format:check
```

### 4. Rules smoke test

```sh
cd BetterCrewlink
npx esbuild ../tests/touMiraVoice.smoke.ts --bundle --platform=node --format=cjs --outfile=../tests/out.cjs
node ../tests/out.cjs        # prints "ALL PASSED"
```

## Testing in game

1. Copy `TouMiraBclBridge.dll` into `Among Us/BepInEx/plugins/`, next to `TownOfUsMira.dll`.
2. Start BCL (`npm run dev`), then launch Among Us.
3. In `Among Us/BepInEx/LogOutput.log`, look for `Listening for BetterCrewLink on 127.0.0.1:47652`
   and then `BetterCrewLink connected.`
4. In BCL: **Settings → Town of Us: Mira** should show a green **"bridge connected"** banner.
5. With 2–3 people, all running the bridge and your BCL build:
   - [ ] Jailor jails someone. In the meeting nobody hears the jailee, and the jailee sees "You are jailed".
   - [ ] Blackmailer blackmails someone. The victim is silent in the next meeting.
   - [ ] Puppeteer controls someone. The victim is silent. The Puppeteer hears people near the victim.
   - [ ] Turn a rule off on the host's settings page. Everyone's behaviour follows.
   - [ ] Close the game. BCL's banner goes back to "not detected", and voice behaves normally.

If something is wrong, check `LogOutput.log` for `Snapshot failed:` warnings from the bridge.

## Distributing to your group

The [TOU-Mira installer](https://github.com/rroig7/tou-mira-installer) does all of this when a
player ticks **BetterCrewLink-TOU + voice bridge (alpha)**. By hand, each player installs:

1. **TOU-Mira**: an official release listed in the bridge's "Built for TOU-Mira" line.
2. **`TouMiraBclBridge.dll`** in `BepInEx/plugins/`
   ([releases](https://github.com/rroig7/whg-tou-mira/releases)).
3. **BetterCrewLink-TOU**
   ([releases](https://github.com/rroig7/BetterCrewLink/releases)). It auto-updates from there.

Someone on official BCL still connects and talks normally. They just won't apply the TOU rules
as a listener. The speaker-side mute still silences a jailed or blackmailed player for them, as
long as that *player* has the bridge and your build.

## Releasing BetterCrewLink-TOU

Releases live on [rroig7/BetterCrewLink](https://github.com/rroig7/BetterCrewLink/releases). The
in-app auto-updater and the TOU-Mira installer both download from there.

```sh
cd BetterCrewlink
npm version 3.2.3-alpha.2 --no-git-tag-version   # semver; "-alpha.N" keeps the updater on prereleases
npm run typecheck && npm run lint && npm run format:check
npm run dist:64                                  # -> dist/
git commit -am "Release BetterCrewLink-TOU 3.2.3-alpha.2" && git push

# electron-builder names the file with spaces, but latest.yml refers to dashes, and GitHub would
# turn spaces into dots. Upload under the dashed names or auto-update breaks.
V=3.2.3-alpha.2; mkdir -p ../.bcl-release && rm -f ../.bcl-release/*
cp "dist/BetterCrewLink-TOU Setup $V.exe" "../.bcl-release/BetterCrewLink-TOU-Setup-$V.exe"
cp "dist/BetterCrewLink-TOU Setup $V.exe.blockmap" "../.bcl-release/BetterCrewLink-TOU-Setup-$V.exe.blockmap"
cp dist/latest.yml ../.bcl-release/
gh release create "v$V" ../.bcl-release/* -R rroig7/BetterCrewLink --target tou-mira   --prerelease --title "BetterCrewLink-TOU v$V" --notes "..."
cd .. && git add BetterCrewlink && git commit -m "Bump BetterCrewlink to $V" && git push
```

The installer is unsigned, so Windows SmartScreen warns players who run it by hand. The TOU-Mira
installer runs it silently from a temp download, which doesn't trigger that warning.

## Releasing the bridge

Bridge releases live on this repo's GitHub releases page. The
[TOU-Mira installer](https://github.com/rroig7/tou-mira-installer) downloads the newest one,
prereleases included, when a player ticks its bridge checkbox. It relies on two conventions:

* The DLL is attached as an asset named exactly **`TouMiraBclBridge.dll`**.
* The release notes contain the line **`Built for TOU-Mira: <tag>[, <tag>…]`**, listing every
  TOU-Mira release this DLL works with (e.g. `1.7.3, 1.7.9-beta1`). The installer warns when it
  installs a TOU-Mira version that isn't listed.

**Compile releases against the oldest TOU-Mira release you support**, using its official DLLs.
The bridge references TOU-Mira by assembly version. A newer installed TOU-Mira satisfies an older
reference, but an older one may refuse a newer reference.

Before listing a version as supported, check that the bridge compiles against that version's
DLLs too. For 1.7.3 and 1.7.9-beta1 this was verified: compiled against each, the bridge's
external references were identical apart from TOU-Mira's assembly version.

```sh
# 1. Bump <Version> in TouMiraBclBridge.csproj (and [BepInPlugin] in BridgePlugin.cs when it's not a pre-release bump).
# 2. Fetch the oldest supported release's official DLLs and build against them:
cd TouMiraBclBridge
gh release download 1.7.3 -R AU-Avengers/TOU-Mira -p TownOfUsMira.dll -p MiraAPI.dll -D ../.release-refs/1.7.3 --clobber
REFS="$(cd ../.release-refs/1.7.3 && pwd -W)/"
dotnet build -c Release --no-incremental -p:TouMiraDir="$REFS" -p:MiraApiDir="$REFS"
# 3. Publish
gh release create v0.1.0-alpha.3 bin/Release/net6.0/TouMiraBclBridge.dll --target main \
  --prerelease --title "TOU-Mira Bridge v0.1.0-alpha.3" \
  --notes "Built for TOU-Mira: 1.7.3, 1.7.9-beta1"
```

Use `--prerelease` while in alpha or beta. Drop it once the bridge is stable.

## Updating TOU-Mira

```sh
git -C TOU-Mira fetch --tags && git -C TOU-Mira checkout <new-tag>
git -C TOU-Mira submodule update --init
git add TOU-Mira && git commit -m "Pin TOU-Mira <new-tag>"
```

Then rebuild TOU-Mira (step 1) and the bridge (step 2). If `TOU-Mira/AmongUs.props` changed any
package versions, copy them into `TouMiraBclBridge.csproj`. Commit the new submodule pointer and
ship the new DLL.

## Updating BetterCrewLink from upstream

```sh
cd BetterCrewlink
git fetch upstream && git merge upstream/nightly  # BCL's default branch; resolve conflicts, re-run the checks above
```

The integration touches few upstream files: `spatialAudio.ts`, `VoiceController.ts`,
`AudioController.ts`, `GameReader.ts`, `settingsStore.ts`, `SettingsPanel.tsx`,
`VoiceView.tsx`, `types.ts`, `AmongUsState.ts`, `ISettings.d.ts` and the English locale. All
real logic is in new `touMira*` / `TouMira*` files, so merges stay small.

## Adding a new interaction

1. **Bridge:** report the fact in `SnapshotBuilder.WritePlayer` (e.g. `HasModifier<XModifier>()`).
2. **Protocol:** add the field to `TouMiraPlayer` in `BetterCrewlink/src/common/TouMira.ts`. If
   you rename or remove a field, bump `ProtocolVersion` / `TOU_MIRA_PROTOCOL_VERSION` on both sides.
3. **Setting:** add a `touMira_*` key to `ILobbySettings` (`src/common/ISettings.d.ts`) and to
   `touMiraLobbyDefaults`.
4. **Rule:** implement it in `src/renderer/voice/touMiraVoice.ts`. Use `touMiraMuteReason` for
   "speaker can't be heard" and `touMiraListenerEffects` for "listener hears differently".
5. **UI:** add a row to `groups` in `src/renderer/settings/sections/TouMiraSection.tsx`, plus
   strings under `settings.toumira` in `static/locales/en/translation.json`.
6. Add a case to `tests/touMiraVoice.smoke.ts`.

## Not done yet / ideas

* **Jailor "Allow Voice" button.** This needs an in-game meeting button plus an RPC, which means
  the bridge would have to be installed by every player.
* **Medium ↔ ghost voice**, and **Vampire / Lovers team radio**. BCL only has an impostor radio
  today.
* **Crewpostor uses the impostor radio.**
* **Overlay privacy:** BCL's overlay shows a talking player's *real* avatar. That leaks
  Morphling, Glitch-mimic and Camouflage identities. TOU-Mira's Perfect Comms integration
  hides or aliases these (see `ResolveOverlaySpeaker`).
* Translations for languages other than English. Until then they fall back to English.
