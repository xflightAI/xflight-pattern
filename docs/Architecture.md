# Architecture

## Product purpose

**XFlight Pattern — VFR Traffic Pattern Assistant for MSFS 2024**

XFlight Pattern is a local Windows application for VFR traffic-pattern training with Microsoft Flight Simulator 2024. Version 0.1.0 targets a Cessna 172 reference aircraft, simulator telemetry, weather inputs, training guidance, deterministic debriefing, and session record/replay. These targets do not imply that the corresponding runtime features are implemented at M5-C1.

M2 adds the circuit-definition domain after the M1 airport/weather domain and R1 runway recommendation calculation. M3 adds static horizontal pattern geometry and the authorized 45-degree entry. M4 adds bounded deterministic phase detection and an origin-only offline replay boundary. M5-B adds raw telemetry contracts, normalization, and a fake source. M5-C1 adds a real SimConnect connection lifecycle, but no simulator-variable subscription or live telemetry acquisition.

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
| `XFlight.Pattern.Core` | Simulator-independent domain, geometry, bounded deterministic phase transitions, and origin-only replay context. |
| `XFlight.Pattern.Telemetry` | Simulator-neutral raw contracts, pure raw-to-Core normalizer, source status, and manually driven fake; no WPF dependency. |
| `XFlight.Pattern.SimConnect` | Windows x64 SDK connection lifecycle driven by the caller; simulator-specific types stay outside Core, and telemetry reads yield no samples. |
| `XFlight.Pattern.ActiveSky` | Active Sky FS adapter boundary; provider-specific types must not leak into Core. |
| `XFlight.Pattern.Infrastructure` | Infrastructure implementations only. |
| `XFlight.Pattern.App` | WPF application and composition root. |

Test projects reference only the production projects whose assembly boundaries they verify.

## M1 airport and weather domain

Core owns the normalized `Airport`, explicit directional `RunwayEnd`, `WindObservation`, `IWeatherSource`, and `IAirportRepository` types. Provider projects may later implement these interfaces, but provider-specific APIs and types must not cross into Core. M1 supplies no Active Sky, simulator, download, parsing, or airport-data implementation.

R1 evaluates each usable runway end independently using its exact stored heading; a runway number is not treated as its heading. Heading input accepts the aviation north equivalents 0° and 360° and stores both canonically as 0°. For a minimum angular difference `delta`, the signed longitudinal component is `speed × cos(delta)` and crosswind magnitude is `speed × abs(sin(delta))`. Results expose separate non-negative headwind, tailwind, and crosswind magnitudes, plus a longitudinal classification, so consumers never interpret a hidden negative-headwind convention.

Directional wind candidates rank by descending signed longitudinal component, then lower crosswind, then ordinal runway-end identifier and exact heading for a stable tie break. This is wind geometry, not an operational-safety determination or ATC clearance. Calm, variable, and unavailable wind return `NoWindPreferredRunway`; no airport procedure is fabricated. Gust is retained as provenance data but sustained speed drives the M1 calculation.

## M2 circuit-definition domain

M2 defines what circuit is being trained. An immutable `CircuitDefinition` retains the airport identity, selected directional runway end, traffic-pattern side resolution, and pattern-altitude resolution. Reciprocal runway ends remain independent, so runway 16 may be left traffic while runway 34 is right traffic.

Traffic-pattern side is `Left`, `Right`, or `Unknown`. Unknown remains unknown: wind, geography, and terrain do not infer a side, and there is no hidden left default. Side provenance is retained as `Published`, `XFlightVerified`, or `UserSelected`; an unknown side can only have unknown provenance. Deterministic precedence is published, then XFlight-verified, then user-selected, then unknown. Published wins the explicit published-versus-verified tie decision.

Altitude types keep airport elevation MSL, pattern height AGL, and target pattern altitude MSL distinct. Published and XFlight-verified authority are direct MSL targets. User selection may be a direct MSL target or an AGL height derived using airport elevation. The Version 0.1.0 C172 training default is explicitly 1,000 ft AGL and is labeled `TrainingDefault`, never published. It is used only when explicitly requested and airport elevation is available; otherwise altitude remains unresolved. Altitude precedence is published, XFlight-verified, user-selected, explicitly requested training default, then unresolved.

M2 contains no geometry, phase detection, coaching, or scoring. M3 consumes its immutable circuit definition without changing resolution or precedence.

## M3 horizontal geometry

Core's `Geometry` domain locates the selected circuit from an airport/runway-end reference and landing/opposite WGS84 threshold coordinates. The landing threshold anchors a local ECEF/ENU tangent plane; the opposite threshold defines the forward axis and local horizontal runway length. Stored `RunwayEnd.Heading` is not a geographic bearing authority. AlongTrack points forward and CrossTrack points right; LEFT uses negative CrossTrack and RIGHT uses positive CrossTrack.

All seven horizontal dimensions are explicit positive distances stored in metres. Immutable flat-ended leg corridors and bounded base areas are detection references. M3 supplies no aircraft membership calculation. The Base Turn Window spans configured nearer/farther limits before the landing threshold; the base area permits all positions in that window without choosing a turn point. The entry joins downwind abeam runway midpoint at 45 degrees from outside the lateral circuit boundary.

UNKNOWN side returns an explicit unavailable result. The source circuit and all side/altitude provenance remain intact, including unresolved altitude. ENU uses ellipsoid height zero solely for horizontal projection; it creates no target altitude or vertical limits. LEFT/RIGHT geometry mirrors exactly in runway coordinates. Outputs contain fixed named immutable values, with value equality and stable corner ordering.

See [M3 Geometry](M3-Geometry.md) for precise bounds and validation. M4 consumes this geometry without changing it. Core remains free of external GIS packages and adapter dependencies.

## M4 bounded phase detection

Core evaluates closed horizontal region membership and direction evidence against the M3 runway frame. With explicit, caller-supplied policy and ordered normalized snapshots, its deterministic transition evaluates source-time persistence, quality, continuity, relocation, and ambiguity. The implemented paths are `Unknown → Entry → Downwind → Base → Final` and `Unknown → Departure → Crosswind → Downwind → Base → Final`. Departure requires sustained observed runway contact and qualifying airborne evidence; ground contact alone does not confirm it. A static Base Turn Window does not choose a turn point or emit a cue.

`PhaseReplayContext.Start()` and `Advance(cursor, snapshot)` provide an origin-only, offline deterministic replay lineage with opaque cursors. They do not record or reproduce full flight sessions. The implemented detector emits no phase cues and has no confirmed outgoing transition from `Final`, `LandingRoll`, `GoAround`, or `Exited`. See [M4-B1 contracts](M4-Phase-Detection-Contracts.md), [M4-B2 evidence](M4-B2-Geometry-Membership.md), and the [M4-B4 approved contract](M4-B4-Departure-Crosswind.md).

## M5 telemetry and SimConnect boundary

M5-B defines simulator-neutral raw observations, source identity, stream epoch, sequence, source time, and independent monotonic receipt time. Its pure normalizer converts valid feet, knots, and feet-per-minute observations into Core units, preserves measured provenance, marks invalid individual observations unavailable, and rejects invalid sample identity or time. `FakeTelemetrySource` is single-threaded and manually driven; its connection and pause status does not create samples. See [M5-B Telemetry Boundary](M5-B-Telemetry-Boundary.md).

M5-C1 uses the official MSFS 2024 SimConnect SDK on Windows x64. `Connect()` opens a session but reports `Connecting` until an `Open` callback is dispatched by caller-driven `Pump()`. `Quit` or an SDK/receive error closes the session; `Disconnect()` closes it explicitly, and `Dispose()` is terminal. SDK calls and callbacks are serialized, with no background worker. The adapter does not subscribe to simulator variables, and `TryRead` always returns no sample. The certified baseline reported **296/296 offline tests PASS**; the M5-C1 live test reported **PASS for observable connection lifecycle behavior**. Independent verification of native cleanup remains outstanding. These results were not rerun for this documentation update.

Live telemetry acquisition, transition cues, coaching, scoring, an operational UI, complete flight-session recording/replay, live weather integration, and wind correction remain outside the implemented M5-C1 boundary.

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

Dependencies point toward stable abstractions. Core has no project or package dependencies. Adapter-specific and UI-specific types stay outside Core. M2 and M3 introduce no integration SDK packages or speculative architectural frameworks.

