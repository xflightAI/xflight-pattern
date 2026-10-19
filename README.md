# XFlight Pattern

**VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows training application for Microsoft Flight Simulator 2024, focused on VFR traffic-pattern training.

This repository is at the certified **M5-C1 — SimConnect Connection Lifecycle** baseline. M1 provides the airport/weather domain and R1 runway recommendations; M2 defines circuit authority; M3 constructs static horizontal pattern geometry. M4 adds simulator-independent, deterministic phase detection for the bounded `Unknown → Entry → Downwind → Base → Final` and `Unknown → Departure → Crosswind → Downwind → Base → Final` paths, using explicit policy, accepted observations, and an origin-only replay context. It emits no phase cues.

M5-B adds simulator-neutral raw telemetry contracts, a pure normalizer into Core snapshots, and a manually driven fake source. M5-C1 opens a real SimConnect SDK connection on Windows x64 and dispatches `Open`, `Quit`, and error notifications through caller-driven `Pump`; `Disconnect` and `Dispose` close the session. It does not subscribe to simulator variables: `TryRead` returns no sample. Live telemetry acquisition, coaching, scoring, an operational UI, and complete flight-session recording/replay are not implemented. The M4 deterministic replay boundary is not a session recorder.

## Requirements

- Windows
- .NET 10 SDK
- Official MSFS 2024 SimConnect SDK for a full solution build; set `Msfs2024SdkRoot` or `MSFS2024_SDK` if it is not at `C:\MSFS 2024 SDK`

## Build and test

```powershell
dotnet restore
dotnet build
dotnet test
```

The certified M5-C1 baseline reported **296/296 offline tests PASS**. The M5-C1 live test reported **PASS for observable connection lifecycle behavior**; independent verification of native cleanup remains outstanding. These results are certification evidence, not a test run performed by this documentation update.

## Repository layout

- `src/XFlight.Pattern.Core` — deterministic, simulator-independent domain and application logic
- `src/XFlight.Pattern.Telemetry` — raw contracts, pure normalizer, and manually driven fake source
- `src/XFlight.Pattern.SimConnect` — caller-driven SDK connection lifecycle; no telemetry subscription
- `src/XFlight.Pattern.ActiveSky` — optional-at-runtime Active Sky FS adapter boundary
- `src/XFlight.Pattern.Infrastructure` — infrastructure implementations
- `src/XFlight.Pattern.App` — WPF composition root and application shell
- `tests` — focused unit and integration test projects
- `docs` — architecture and milestone documentation

See [Architecture](docs/Architecture.md) and [Development Milestones](docs/Development-Milestones.md) for the current scope and frozen foundation constraints. The [M4 contracts](docs/M4-Phase-Detection-Contracts.md), [M4-B4 decision record](docs/M4-B4-Departure-Crosswind.md), and [M5-B telemetry boundary](docs/M5-B-Telemetry-Boundary.md) describe their approved limits.

See [M3 Geometry](docs/M3-Geometry.md) for WGS84 local ENU, runway axes, metre-based parameters, mirrored corridor bounds, and the horizontal-only M3/M4 boundary. These are training references, not ATC clearances or universally appropriate entry procedures.

