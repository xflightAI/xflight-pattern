# Development Milestones

The planned development sequence is M0 through M8. Work advances one reviewed milestone at a time.

| Milestone | Status and scope |
| --- | --- |
| M0 | Repository foundation: solution structure, dependency boundaries, documentation, test discovery, and minimal WPF shell. |
| M1 | Airport and weather domain: explicit runway ends, normalized wind observations and source contracts, deterministic wind components, and R1 runway recommendation/ranking. |
| M2 | Reserved for separately approved scope. |
| M3 | Reserved for separately approved scope. |
| M4 | Reserved for separately approved scope. |
| M5 | Reserved for separately approved scope. |
| M6 | Reserved for separately approved scope. |
| M7 | Reserved for separately approved scope. |
| M8 | Reserved for separately approved scope. |

The functional areas listed for Version 0.1.0—including normalized telemetry, weather sources, runway recommendations, pattern geometry, phase detection, transition cues, coaching, scoring, integrations, and deterministic record/replay—belong to later milestones. Their allocation and detailed design must be approved before implementation rather than inferred during M0.

M1 is limited to airport/weather contracts and deterministic R1 wind geometry. It intentionally excludes data loading, provider connectivity, METAR handling, traffic-pattern logic, telemetry processing, replay, coaching, scoring, and product UI.

