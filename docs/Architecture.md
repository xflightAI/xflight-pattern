# Architecture

## Product purpose

**XFlight Pattern — VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows application for VFR traffic-pattern training with Microsoft Flight Simulator 2024. Version 0.1.0 targets a Cessna 172 reference aircraft, simulator telemetry, weather inputs, training guidance, deterministic debriefing, and session record/replay.

M0 is repository foundation only. It contains no aeronautical calculations, runtime integrations, detection, coaching, scoring, replay engine, or product UI.

## Frozen principles

1. The architecture is deterministic-first.
2. Core logic is simulator-independent and does not reference SimConnect, Active Sky, WPF, or infrastructure implementations.
3. Detection, coaching, and scoring remain separate concerns.
4. Pattern legs will be modeled as detection corridors rather than GPS tracks.
5. Unknown airport or procedure information remains unknown.
6. Training defaults remain distinguishable from published or verified data.
7. The same telemetry and configuration must eventually produce the same result.
8. SimConnect and Active Sky are adapters around normalized domain models.
9. Development supports offline deterministic replay without requiring MSFS to be installed.

Active Sky FS is a Version 0.1.0 integration target, but it is optional at runtime. A future record/replay capability must preserve the deterministic boundary and permit laptop-only development and verification.

## Project responsibilities

| Project | Responsibility |
| --- | --- |
| `XFlight.Pattern.Core` | Simulator-independent deterministic domain and application logic. |
| `XFlight.Pattern.Telemetry` | Normalized telemetry contracts and processing boundary; no WPF dependency. |
| `XFlight.Pattern.SimConnect` | SimConnect adapter boundary; simulator-specific types must not leak into Core. |
| `XFlight.Pattern.ActiveSky` | Active Sky FS adapter boundary; provider-specific types must not leak into Core. |
| `XFlight.Pattern.Infrastructure` | Infrastructure implementations only. |
| `XFlight.Pattern.App` | WPF application and composition root. |

Test projects reference only the production projects whose assembly boundaries they verify.

## Dependency boundaries

```text
Core
  ^
  |-- Telemetry
  |     ^
  |     |-- SimConnect
  |
  |-- ActiveSky
  |-- Infrastructure

App --> Core, Telemetry, SimConnect, ActiveSky, Infrastructure
```

Dependencies point toward stable abstractions. Core has no project dependencies. Adapter-specific and UI-specific types stay outside Core. M0 introduces no integration SDK packages or speculative architectural frameworks.

