# tml-event-logger

`TteEventLogger`: a server-side tModLoader mod that pushes player and server events (join, leave,
chat, death, spawn, world save) to
[tte-server-manager](https://github.com/The-Terraria-Experiment/tte-server-manager). It is the
tModLoader counterpart of `tshock-event-notifier`, built on that repo's platform-agnostic
`EventNotifier.Core`.

- **Wire contract:** [`docs/contracts/event-push.md`](https://github.com/The-Terraria-Experiment/tte-server-manager/blob/main/docs/contracts/event-push.md) in tte-server-manager. That document is authoritative.
- **Build:** `dotnet build TteEventLogger/TteEventLogger.csproj`, with `TmlInstallDir` set.
- **Status:** all milestones implemented; not yet checked end to end with a real client. See [CLAUDE.md](CLAUDE.md).
- **Config:** `ModConfigs/TteEventLogger_EventLoggerConfig.json` in the tModLoader save directory (`EndpointUrl`, `ApiKey`, per-event toggles). On the fleet, the endpoint and key come instead from the root-owned file named by `TTE_EVENT_LOGGER_ENDPOINT_FILE`, which tte-server-manager's `setup.sh` writes.
- **Console:** `eventlogger <status|test|showconfig>`.
