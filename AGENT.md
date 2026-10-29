# AGENT.md

Instructions for AI coding agents working in this repo. Read `README.md` for the human-facing
overview; this file covers what you need to change code safely.

## What this is

An integration between two upstream projects, so that Town of Us: Mira role effects (jailed,
blackmailed, controlled, …) change BetterCrewLink proximity voice.

| Path | What | May you edit it? |
|---|---|---|
| `TouMiraBclBridge/` | C# BepInEx plugin inside Among Us; streams TOU-Mira state to BCL | Yes |
| `BetterCrewlink/` | Our fork of BetterCrewLink (Electron + React + TS), branch `tou-mira` | Yes, but keep upstream diffs small |
| `TOU-Mira/` | Upstream Town of Us: Mira, pinned to the release the group plays | **No.** Read-only compile reference. Never modify it; the group runs the official release. |
| `tests/` | Rules smoke test | Yes |

## Data flow

1. `TouMiraBclBridge/SnapshotBuilder.cs` reads TOU-Mira modifiers on the Unity thread (10×/s via
   `BridgeBehaviour`) and serializes one JSON line.
2. `BridgeServer.cs` pushes it over TCP `127.0.0.1:47652`. Only the newest snapshot is kept,
   and writes happen off the Unity thread.
3. `BetterCrewlink/src/main/touMiraBridge.ts` (Electron main) receives it.
   `GameReader.ts` attaches it to `AmongUsState.touMira` (or `null` if stale >3s).
4. `BetterCrewlink/src/renderer/voice/touMiraVoice.ts` turns it into rules, applied in
   `spatialAudio.ts` (listener side, per speaker) and `VoiceController.applyTouMiraSelfMute`
   (speaker side: stops sending your own mic).
5. Settings are `touMira_*` keys on `ILobbySettings`. The host's values sync to all peers. The UI
   is `src/renderer/settings/sections/TouMiraSection.tsx`.

The **wire format** is defined twice and must stay in sync:
`BetterCrewlink/src/common/TouMira.ts` (`TouMiraSnapshot`, `TouMiraPlayer`) ↔
`TouMiraBclBridge/SnapshotBuilder.cs` (`WritePlayer`). Changing or removing a field means bumping
`TOU_MIRA_PROTOCOL_VERSION` and `ProtocolVersion` together. Adding a field doesn't.

## Source of truth for rule behaviour

`TOU-Mira/TownOfUs/Integrations/PerfectCommsIntegration.cs` is TOU-Mira's own voice integration
for a different voice mod. When adding or changing a rule, match its semantics: which phase
(tasks / meeting / exile), defaults, and edge cases like a dead Jailor. Don't invent behaviour
when that file already defines it.

## Invariants (don't break these)

* **Fail open.** With no bridge, a stale snapshot, or `touMira_enabled` off, BCL must behave
  exactly like upstream. Every rule starts from `activeSnapshot()` returning `null`.
* **Dead players are never TOU-muted.** Ghost voice is BCL's own logic.
* **The bridge is client-only.** No RPCs, no `ReactorModFlags`, nothing that would require every
  lobby member to install it. Anything that needs RPCs (e.g. a Jailor "Allow Voice" button) is a
  design change; flag it to the user first.
* **Never block the Unity thread.** No socket I/O, sleeps or locks held for long in
  `Update`/`SnapshotBuilder`. `BridgeServer` has exactly one writer (`SendLoop`); keep it that way.
* **`SnapshotBuilder` runs only on the Unity thread.** IL2CPP objects aren't safe off it.
* **Lobby settings are flat `touMira_*` keys.** Peers shallow-merge received settings over
  defaults (`{...defaultLobbySettings, ...data}`), so nested objects would break, and older peers
  must ignore unknown keys.
* Defaults live in one place: `touMiraLobbyDefaults` in `src/common/TouMira.ts`. Both the renderer
  defaults and the electron-store schema spread it.
* Bridge port `47652` is hard-coded in `BridgePlugin.Port` and `TOU_MIRA_BRIDGE_PORT`. Change
  both or neither.

## Recipe: add a new interaction

1. `SnapshotBuilder.WritePlayer`: emit the fact. TOU-Mira modifier and role types are public;
   use MiraAPI's `HasModifier<T>()` / `GetModifier<T>()`.
2. `src/common/TouMira.ts`: add the field to `TouMiraPlayer`; add a `touMira_*` default to
   `touMiraLobbyDefaults`.
3. `src/common/ISettings.d.ts`: add the key to `ILobbySettings`.
4. `src/renderer/voice/touMiraVoice.ts`: a speaker mute goes in `touMiraMuteReason` (add the
   reason to `TouMiraMuteReason`). A listener change goes in `touMiraListenerEffects`.
5. `TouMiraSection.tsx`: add a row to `groups`. `static/locales/en/translation.json`: add
   `settings.toumira.<key>`, `<key>_description`, and `muted.<reason>` if it's a new reason. That
   file is **CRLF** and has duplicate keys; insert text, don't parse and re-serialize it.
6. `tests/touMiraVoice.smoke.ts`: add asserts.

## Commands

```sh
# TOU-Mira (needed before the bridge builds; -m:1 avoids a Unity-libs extract race)
cd TOU-Mira && git submodule update --init && dotnet build TownOfUs/TownOfUsMira.csproj -c Release -m:1

# Bridge
cd TouMiraBclBridge && dotnet build -c Release

# BCL: verify every change with all three
cd BetterCrewlink && npm run typecheck && npm run lint && npm run format:check

# Rules smoke test
cd BetterCrewlink && npx esbuild ../tests/touMiraVoice.smoke.ts --bundle --platform=node --format=cjs --outfile=../tests/out.cjs && node ../tests/out.cjs
```

You can't run Among Us, so the bridge's in-game behaviour is unverifiable here. Say so when you
report results; don't claim in-game behaviour works.

## Git layout

* This repo (`rroig7/whg-tou-mira`, branch `main`) contains `TouMiraBclBridge/`, `tests/`
  and the docs, plus two submodules.
* `BetterCrewlink/` is a submodule on `rroig7/BetterCrewLink`, branch `tou-mira`. `origin` is
  the fork; `upstream` is OhMyGuus (default branch `nightly`). BCL changes are committed **in
  the submodule** and pushed there first. Then commit the updated submodule pointer here.
* `TOU-Mira/` is a submodule pinned to a release tag of official AU-Avengers/TOU-Mira. Never
  commit inside it. Changing the pin is a deliberate "update TOU-Mira" step (see README).

## Style

* **BCL:** tabs, single quotes, Prettier (`.prettierrc.yaml`). Files are CRLF in the working
  tree (`core.autocrlf=true`). Keep edits to upstream BCL files minimal and put logic in
  `touMira*` files, so merges from `upstream/nightly` stay easy.
* **Bridge:** C# with file-scoped namespaces and 4 spaces. Mirror TOU-Mira's idioms when touching
  its types.
* Comments explain *why*, not *what*.
* Don't commit, push, or run `npm ci`/installs without asking the user.
