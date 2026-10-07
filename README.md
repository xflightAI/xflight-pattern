# XFlight Pattern

**VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows training application for Microsoft Flight Simulator 2024, focused on VFR traffic-pattern training.

This repository is currently at **M0 — Repository Foundation**. M0 establishes the solution, project boundaries, test discovery, documentation, and a minimal WPF application shell. It intentionally contains no aeronautical or product behavior.

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

