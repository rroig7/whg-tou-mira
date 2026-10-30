# Project log: TOU-Mira × BetterCrewLink

Condensed history of the build-out (Oct 2026), so future sessions keep the context. For *how*
things work, see `README.md` (humans) and `AGENT.md` (agents). This file records *what happened,
why, and what's left*.

## Goal

Make BetterCrewLink (BCL) proximity voice react to Town of Us: Mira (TOU-Mira) role effects:
jailed, blackmailed and controlled players are muted, and more. Ship it to the WHG group through
their own installer. The user (rroig7) wants many more interactions over time.

## Repos and current state (as of 2026-10-29)

| Repo | Local path | Branch | Latest |
|---|---|---|---|
| `rroig7/whg-tou-mira` (umbrella: bridge, docs, tests, release script) | `~/dev/whg-tou-mira` | **`increase-muffle`** (user's branch; `main` is behind it) | bridge releases `v0.1.0-alpha.1/2` and **`v0.2.0-alpha.1`** (legacy path, for installer 1.3.0) |
| `rroig7/BetterCrewLink` (fork, submodule `BetterCrewlink/`) | `whg-tou-mira/BetterCrewlink` | `tou-mira` (`upstream` = OhMyGuus, `nightly`) | **`v3.2.3-alpha.3`** (adds the bridge status on the main menu): app + `TouMiraBclBridge-1.7.3.dll` + `TouMiraBclBridge-1.7.9-beta1.dll` |
| `AU-Avengers/TOU-Mira` (official, submodule `TOU-Mira/`, never modified) | `whg-tou-mira/TOU-Mira` | pinned `1.7.9-beta1` | n/a |
| `rroig7/tou-mira-installer` (Python/PySide6, PyInstaller exe) | `~/dev/tou-mira-installer` | **`tou-bcl-integration`** (user's branch; `main` at 1.2.1) | **`v1.4.0`** (latest, tagged at `b482b2c`) |

The user's own game is at `G:\SteamLibrary\steamapps\common\Among Us`, on **TOU-Mira 1.7.3**
(MiraAPI 0.5.0). They installed BetterCrewLink-TOU `alpha.1` themselves, and official BCL 3.2.2 is
also installed. Don't uninstall either.

## Architecture (short)

The `TouMiraBclBridge.dll` BepInEx plugin reads TOU-Mira modifiers 10×/s and pushes NDJSON over
TCP `127.0.0.1:47652`. BCL's main process (`touMiraBridge.ts`) adds it to
`AmongUsState.touMira`. Rules live in `renderer/voice/touMiraVoice.ts` and are applied in
`spatialAudio.ts` (listener side) and `VoiceController.applyTouMiraSelfMute` (speaker side).
Settings are flat `touMira_*` lobby settings, synced from the host and shown on BCL's **Town of
Us: Mira** settings page. With no bridge, BCL behaves like upstream (fail-open).

The reference spec for rule semantics is TOU-Mira's own
`TownOfUs/Integrations/PerfectCommsIntegration.cs`.

**Implemented rules:**
- Mute blackmailed players in meetings, optionally also the next round.
- Mute jailed players in meetings, with "jail persists after Jailor death" and an optional
  **deafen** (default off).
- Mute Puppeteer- and Parasite-controlled players; the controller hears from the victim.
- Mute swooped and Glitch-hacked players.
- Muffle blinded, flashed and Mass-Hysteria listeners: user-tuned to 200 Hz lowpass, ×1.5 gain,
  plus reverb.
- **Medium ghost voice:** None / Medium→Ghost / **Ghost→Medium (default, the user's request;
  TOU-Mira's own default is None)** / Both. In spirit form the Medium listens from the spirit,
  living players can't hear it, and it talks only with the ghosts it mediated. Bridge 0.2 sends
  `mediumSpirit` and `mediatedBy`.

## Key decisions and why

- **Fork BCL, not TOU-Mira.** TOU-Mira is untouched and only a compile reference, so the group
  keeps using official releases. The bridge is a separate, **client-only** plugin: no RPCs, so it
  doesn't need to be on every client.
- **Fork identity.** Package `name: bettercrewlink-tou`, `productName: BetterCrewLink-TOU`,
  `appId com.whg.bettercrewlink.toumira`, and the updater pointed at `rroig7/BetterCrewLink`. The
  NSIS per-user install folder *and* the settings folder come from the package `name`, so without
  this it installed over official BCL. The WinGet workflow was removed.
- **Releases are tied to TOU-Mira versions.** Each BCL-TOU release attaches
  `TouMiraBclBridge-<tou tag>.dll`, compiled against **that TOU-Mira release's official DLLs**
  (taken from its Steam zip). The installer installs the bridge matching the installed TOU-Mira
  version. `scripts/release-bcl-tou.sh <version> <tou tags…>` does everything.
- **Why one build per version:** the bridge references TOU-Mira and MiraAPI by assembly version.
  A newer installed assembly satisfies an older reference, not vice versa. The 1.7.3 build also
  works on 1.7.9-beta1, because its member signatures were verified identical with a metadata
  diff.
- **Installer 1.4.0.** A "BetterCrewLink-TOU + voice bridge (alpha)" checkbox, mutually exclusive
  with the official-BCL checkbox (the user didn't want both installed). The installer never
  uninstalls; it only suggests it. There's a blue **"Voice chat update available"** banner
  (`voice_update.py`) for a newer app, a mismatched bridge digest, or a missing half of the pair.
  "Update voice chat" updates without reinstalling the mod.
- **Version format.** `x.y.z-alpha.N` keeps electron-updater on prereleases.

## Gotchas learned (don't relearn these)

**TOU-Mira and bridge builds**
- Build TOU-Mira with `-m:1`; otherwise there's a parallel Unity-libs extract race (MSB4018).

**BCL**
- `static/locales/en/translation.json` is CRLF and has duplicate keys. Insert text; never
  `JSON.parse`/re-serialize it (that reorders keys).
- BCL files are CRLF in the working tree (`autocrlf=true`). Python/sed edits must keep endings.
  In this Bash tool, `\\n` inside heredoc Python strings became real newlines, so use the Edit
  tool for those.

**Releases and GitHub**
- electron-builder output has spaces, while `latest.yml` refers to dashes and GitHub turns
  spaces into dots. Upload dashed names (the script does this).
- The fork inherited upstream tags, and GitHub's release feed lists them. The updater is fine
  once our release is newest.
- `gh release create --target` needs a full SHA or a branch name. `file#name` sets a display
  label, not the asset name.
- Installer `v1.3.0` was released by the user with `--target main`, so that tag points at 1.2.1
  code. Use the commit SHA.
- npm 11 blocks install scripts (`npm install-scripts ls`). electron-builder rebuilds the native
  modules during `dist` anyway.

**Installer**
- NSIS uninstallers relaunch from `%TEMP%` and return immediately, so poll for completion.
- PySide: keep a `QThread` referenced until its `finished` signal. Garbage-collecting a running
  thread killed the installer with exit code 127.
- The installer's old `version_gt` mis-orders `1.7.9-beta1` vs `1.7.3`, so its TOU update banner
  says "up to date". That bug predates this work and wasn't fixed. `semver_gt` exists for
  BCL-TOU.

**General**
- The app and the setup exe are **unsigned**, so SmartScreen warns on manual runs. Silent runs
  from the installer are fine.

## How the user works

- Asks before `npm ci`/installs. Explicit "build it" counts as permission.
- Works on their own feature branches (`increase-muffle`, `tou-bcl-integration`). Commit there and
  don't push to `main` unprompted.
- Sometimes asks for terse answers ("quick, don't burn tokens").
- Tunes values themselves (muffle) and commits locally. Run Prettier before releasing, because
  their edits used spaces.

## Open items

- **Nothing has been tested in a live game.** The in-game bridge behaviour and coordinates
  (spirit vs. player positions) are unverified.
- `alpha.3` (2026-10-29) shipped the main-menu bridge status. No muffle changes newer than
  `alpha.2` existed, so if the user still has unsaved muffle edits they need an `alpha.4`. The
  installer needed no change: it discovers releases, and its `find_for`/`latest_setup` were
  checked against the live `alpha.3` release.
- Merge `increase-muffle` → `main` (whg) and `tou-bcl-integration` → `main` (installer) when the
  user is ready.
- Backlog:
  - Jailor "Allow Voice" button (needs RPC: a design change, flag it first).
  - Vampire/Lovers team radio.
  - Crewpostor on the impostor radio.
  - Overlay privacy (the talking indicator leaks Morphling/Mimic/Camouflage identity).
  - Non-English strings.
- `TODO.md` (untracked, the user's) holds their wishlist. All four items there are done.
