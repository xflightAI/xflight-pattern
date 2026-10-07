# XFlight Pattern

**VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows training application for Microsoft Flight Simulator 2024, focused on VFR traffic-pattern training.

This repository is currently at **M1 — Airport & Weather Domain**. M1 adds the normalized airport, runway-end, and wind domain plus deterministic runway recommendation logic. It contains contracts only for airport and weather providers; no live data source is implemented.

## Requirements

- Windows
- .NET 10 SDK

## Build and test

```powershell
dotnet restore
dotnet build
dotnet test
```

## Repository layout

- `src/XFlight.Pattern.Core` — deterministic, simulator-independent domain and application logic
- `src/XFlight.Pattern.Telemetry` — normalized telemetry boundary
- `src/XFlight.Pattern.SimConnect` — SimConnect adapter boundary
- `src/XFlight.Pattern.ActiveSky` — optional-at-runtime Active Sky FS adapter boundary
- `src/XFlight.Pattern.Infrastructure` — infrastructure implementations
- `src/XFlight.Pattern.App` — WPF composition root and application shell
- `tests` — focused unit and integration test projects
- `docs` — architecture and milestone documentation

See [Architecture](docs/Architecture.md) and [Development Milestones](docs/Development-Milestones.md) for the frozen foundation constraints.

