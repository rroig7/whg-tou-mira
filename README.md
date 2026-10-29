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
| Deafen jailed player (off by default) | meeting; the jailee hears nobody |
| Medium ghost voice: None / Medium→Ghost / Ghost→Medium (default) / Both | task round, while the Medium is in spirit form: it listens from the spirit, living players can't hear it, and it talks with the ghosts it mediated |

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

## Releasing BetterCrewLink-TOU (app + bridge)

A release on [rroig7/BetterCrewLink](https://github.com/rroig7/BetterCrewLink/releases) carries
the app **and one voice bridge per supported TOU-Mira version**:

| Asset | Used by |
|---|---|
| `BetterCrewLink-TOU-Setup-<version>.exe`, `.blockmap`, `latest.yml` | the in-app auto-updater, the TOU-Mira installer |
| `TouMiraBclBridge-<tou-mira-tag>.dll` (e.g. `TouMiraBclBridge-1.7.9-beta1.dll`) | the TOU-Mira installer |

This is how releases are tied to TOU-Mira versions. Each bridge is compiled against that TOU-Mira
release's official DLLs. The installer installs the one matching the player's TOU-Mira version,
taken from the newest release that has one. When a newer app or a different matching bridge
exists, it shows a "voice chat update available" banner.

```sh
# commit your changes first; the script refuses to run on a dirty tree
scripts/release-bcl-tou.sh 3.2.3-alpha.3 1.7.9-beta1 1.7.3
```

The script:

1. Downloads each listed TOU-Mira release's official `TownOfUsMira.dll` and `MiraAPI.dll`, and
   builds a bridge against them.
2. Bumps and commits the app version if needed, runs typecheck, lint and format checks, and
   builds the installer.
3. Uploads everything under the names above. electron-builder's file names contain spaces, but
   `latest.yml` refers to dashes, so the script renames them.
4. Publishes as a prerelease when the version contains `-`, and commits the submodule pointer
   here.

Edit the release notes on GitHub afterwards to say what changed.

Use a semver version. `-alpha.N` keeps installed apps updating through prereleases. The app is
unsigned, so Windows SmartScreen warns players who run the setup by hand. The TOU-Mira installer
runs it silently from a temp download, which doesn't trigger that warning.

### Legacy bridge releases on this repo

[Bridge releases on this repo](https://github.com/rroig7/whg-tou-mira/releases) (a single
`TouMiraBclBridge.dll` plus a `Built for TOU-Mira: <tag>[, <tag>…]` line) are what TOU-Mira
installer **1.3.0** reads. Newer installers read the per-version assets above. To keep 1.3.0 users
current, also publish the bridge here.

Compile that DLL against the *oldest* TOU-Mira version it lists. A newer installed TOU-Mira
satisfies an older assembly reference, but not the other way round. Before listing a version,
check that the bridge's external references are identical against it.

```sh
mkdir -p .bcl-release/legacy && cp .bcl-release/TouMiraBclBridge-1.7.3.dll .bcl-release/legacy/TouMiraBclBridge.dll
gh release create v0.2.0-alpha.1 .bcl-release/legacy/TouMiraBclBridge.dll   --target main --prerelease --title "TOU-Mira Bridge v0.2.0-alpha.1"   --notes "Built for TOU-Mira: 1.7.3, 1.7.9-beta1"
```

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
* **Vampire / Lovers team radio.** BCL only has an impostor radio today.
* **Crewpostor uses the impostor radio.**
* **Overlay privacy:** BCL's overlay shows a talking player's *real* avatar. That leaks
  Morphling, Glitch-mimic and Camouflage identities. TOU-Mira's Perfect Comms integration
  hides or aliases these (see `ResolveOverlaySpeaker`).
* Translations for languages other than English. Until then they fall back to English.
