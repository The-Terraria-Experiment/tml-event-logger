# AI Coding Agent Instructions — tml-event-logger

> **Living document.** When something here turns out stale or wrong, fix it in the same change.

## What this is

`TteEventLogger` is a **server-side tModLoader mod** that pushes player and server events to
[tte-server-manager](https://github.com/The-Terraria-Experiment/tte-server-manager)'s `pushLog`
endpoint over HTTPS. It is the tModLoader counterpart of the TShock plugin
[`tshock-event-notifier`](../tshock-event-notifier), checked out next door.

**This feed is not optional.** Besides the player log in the UI, it is the **only** input to:
- auto-shutoff's idle detection. A server that never pushes a `player.*` event is never considered idle, so it never shuts off.
- the live roster updates.
- the item-rule scan wake-up.

## The contract (authoritative, owned by tte-server-manager)

@../../tte-server-manager/docs/contracts/event-push.md

If that import resolved to nothing, the sibling checkout is missing. The same file is on GitHub at
`The-Terraria-Experiment/tte-server-manager` → `docs/contracts/event-push.md`. Payload changes start
in that document, and the backend's `PayloadSchemaV1` changes with it.

## Reuse, don't port

`../tshock-event-notifier/src/EventNotifier.Core` is the envelope, serializer, HTTP sender, retry
logic and bounded dispatch queue, with **no TShock dependency**. This mod should be a thin adapter
over it, as `EventNotifier.Plugin` is for TShock, so the two can't drift on the wire.

- **The runtime mismatch:** tModLoader runs on **.NET 8**, and `EventNotifier.Core` currently targets `net9.0`. The first job is in *that* repo: change it to `<TargetFrameworks>net8.0;net9.0</TargetFrameworks>` and fix any .NET 9-only API use, using `/add-dir ../tshock-event-notifier`. Its tests must still pass.
- **Shipping it:** put the built `net8.0/EventNotifier.Core.dll` in `TteEventLogger/lib/` and add `dllReferences = EventNotifier.Core` to `build.txt`. That's how tModLoader bundles a plain library into a `.tmod`. Reference the same DLL from the `.csproj` for compilation.
- **No forks.** Don't copy Core's source into this repo. Fix bugs in Core and rebuild both adapters.

## Repository layout

```
tml-event-logger/
  TteEventLogger/          ← the mod. The folder name is the mod's internal name; keep it.
    build.txt
    TteEventLogger.csproj
    TteEventLogger.cs
    lib/                   ← (to add) EventNotifier.Core.dll (net8.0)
```

## Build and run

- **Prerequisites:** the .NET 8 SDK and `TmlInstallDir` pointing at the tModLoader install (`D:\SteamLibrary\steamapps\common\tModLoader` on the maintainer's machine).
- **Build:** `dotnet build TteEventLogger/TteEventLogger.csproj`.
- **Smoke test:** point the config's endpoint at a local listener (for example a throwaway `http.server`, or `npx http-echo-server`), join and leave with a client, and check each payload against the contract.

## Mapping from the TShock plugin

| TShock plugin (`EventNotifier.Plugin`) | tML adapter |
|---|---|
| `TShock.Utils.GetActivePlayerCount()` | count of `Main.player[i].active` |
| `accountName` / `groupName` | `null` (no accounts or groups on tML) |
| `isLoggedIn` | `false` |
| `server.version` = TShock version | tModLoader version (`BuildInfo.tMLVersion`) |
| config in `tshock/event-notifier.json` | server-side `ModConfig`: endpoint URL, API key and header name, timeouts, per-event toggles |
| `server.reload` hook | none; don't send it |
| join, leave, chat, death, spawn, save hooks | `ModPlayer.OnEnterWorld`, disconnect detection, chat hook, `ModPlayer.Kill`, `ModPlayer.OnRespawn`, `ModSystem.SaveWorldData`/`OnWorldUnload` |

Verify that each hook fires **on the server**, since many `ModPlayer` hooks run client-side. Also
verify that `player.leave` still has the player's name at send time; the TShock plugin keeps a
last-known cache for this, and it drives `playerDataSource`.

## Things that will bite

- **Never block the game loop.** Enqueue from the hook and send from Core's background queue.
- **Keep `occurredAtUtc` fixed across retries.** It is the row key in the backend, so a retry with a fresh timestamp creates a duplicate row.
- **The API key is a secret.** Don't log it, and don't print it from any admin command.
- **The endpoint URL already contains the instance ID** (`/logging/{instanceId}/players/push`). The instance's `setup.sh` seeds it. Nothing hardcodes it.

## Milestones

0. `EventNotifier.Core` multi-targets `net8.0` (in `../tshock-event-notifier`).
1. The config plus the join and leave events. That is enough for auto-shutoff and the live roster to work on a tML server.
2. Chat, death, spawn and world-save events.
3. An admin `ModCommand`: status, test and showconfig, mirroring `/eventnotifier`.
