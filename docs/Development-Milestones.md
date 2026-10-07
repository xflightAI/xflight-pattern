# Development Milestones

The planned development sequence is M0 through M8. Work advances one reviewed milestone at a time.

| Milestone | Status and scope |
| --- | --- |
| M0 | Repository foundation: solution structure, dependency boundaries, documentation, test discovery, and minimal WPF shell. |
| M1 | Airport and weather domain: explicit runway ends, normalized wind observations and source contracts, deterministic wind components, and R1 runway recommendation/ranking. |
| M2 | Circuit definition: per-runway-end traffic side with provenance, explicit MSL/AGL altitude semantics and provenance, the C172 1,000 ft AGL training default, deterministic resolution, and immutable `CircuitDefinition`. |
| M3 | Reserved for separately approved pattern-geometry scope. |
| M4 | Reserved for separately approved scope. |
| M5 | Reserved for separately approved scope. |
| M6 | Reserved for separately approved scope. |
| M7 | Reserved for separately approved scope. |
| M8 | Reserved for separately approved scope. |

The functional areas listed for Version 0.1.0—including normalized telemetry, weather sources, runway recommendations, pattern geometry, phase detection, transition cues, coaching, scoring, integrations, and deterministic record/replay—belong to later milestones. Their allocation and detailed design must be approved before implementation rather than inferred during M0.

M1 is limited to airport/weather contracts and deterministic R1 wind geometry. M2 defines what circuit is trained, but intentionally excludes data loading, provider connectivity, METAR handling, circuit geometry, telemetry processing, replay, coaching, scoring, and product UI. Geographic points, bearings, leg corridors, spacing, entry geometry, and turn windows remain M3-or-later work.

