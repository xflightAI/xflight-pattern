# XFlight Pattern

**VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows training application for Microsoft Flight Simulator 2024, focused on VFR traffic-pattern training.

This repository is currently at **M3 — Pattern Geometry & Entry**. Core retains the M1 airport/weather and R1 recommendation domain and the M2 circuit authority, and now constructs immutable horizontal training corridors, a static Base Turn Window, and a 45-degree downwind entry from explicit runway thresholds and dimensions. UNKNOWN side returns unavailable geometry; unresolved altitude remains unresolved. No live integrations, phase detection, coaching, scoring, or map UI are implemented.

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

See [M3 Geometry](docs/M3-Geometry.md) for WGS84 local ENU, runway axes, metre-based parameters, mirrored corridor bounds, and the horizontal-only M3/M4 boundary. These are training references, not ATC clearances or universally appropriate entry procedures.

