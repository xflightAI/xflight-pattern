# Development Milestones

The planned development sequence is M0 through M8. Work advances one reviewed milestone at a time.

| Milestone | Status and scope |
| --- | --- |
| M0 | Repository foundation: solution structure, dependency boundaries, documentation, test discovery, and minimal WPF shell. |
| M1 | Airport and weather domain: explicit runway ends, normalized wind observations and source contracts, deterministic wind components, and R1 runway recommendation/ranking. |
| M2 | Circuit definition: per-runway-end traffic side with provenance, explicit MSL/AGL altitude semantics and provenance, the C172 1,000 ft AGL training default, deterministic resolution, and immutable `CircuitDefinition`. |
| M3 | Implemented: threshold-based WGS84 horizontal geometry, explicit metre-based dimensions, mirrored LEFT/RIGHT leg corridors, static Base Turn Window/base area, and 45-degree downwind entry. UNKNOWN side remains unavailable. |
| M4 | Implemented within the approved boundary: phase contracts and geometry/movement evidence; deterministic, source-time-based transitions for `Unknown → Entry → Downwind → Base → Final` and `Unknown → Departure → Crosswind → Downwind → Base → Final`; origin-only offline replay. No cue emission or other phase paths. |
| M5 | Partially implemented: M5-B simulator-neutral raw telemetry contracts, pure normalizer, and manually driven fake source; M5-C1 real, caller-driven SimConnect connection lifecycle. No simulator-variable subscription or live telemetry samples. |
| M6 | Reserved for separately approved scope. |
| M7 | Reserved for separately approved scope. |
| M8 | Reserved for separately approved scope. |

The certified M5-C1 baseline reported **296/296 offline tests PASS**. The M5-C1 live test reported **PASS within observable connection lifecycle behavior**; independent verification of native cleanup remains outstanding. These are reported certification results, not tests rerun during this documentation update. Live telemetry acquisition, live weather sources, transition cues, coaching, scoring, an operational UI, and complete flight-session recording/replay remain future work. M4's deterministic replay of Core snapshots is not a flight-session recording system.

M1 is limited to airport/weather contracts and deterministic R1 wind geometry. M2 defines what circuit is trained. M3 defines where it exists horizontally using selected threshold positions, while preserving M2 side/altitude provenance. It does not use the stored runway heading as geographic rotation authority. The Base Turn Window remains static geometry, not a selected turn point or cue trigger. M4 evaluates horizontal membership and direction evidence and confirms only the approved transitions after caller-supplied persistence; it does not decide actual turn timing or emit cues. See [M3 Geometry](M3-Geometry.md), [M4-B2 Geometry Membership](M4-B2-Geometry-Membership.md), [M4-B4 Departure and Crosswind](M4-B4-Departure-Crosswind.md), and [M5-B Telemetry Boundary](M5-B-Telemetry-Boundary.md) for their respective bounds and invariants.

