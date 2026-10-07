# XFlight Pattern

**VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows training application for Microsoft Flight Simulator 2024, focused on VFR traffic-pattern training.

This repository is currently at **M2 — Circuit Definition**. In addition to the M1 airport, runway-end, wind, and recommendation domain, Core can define which runway-end circuit is being trained, its resolved traffic-pattern side, and its resolved pattern altitude. No live data source or pattern geometry is implemented.

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

