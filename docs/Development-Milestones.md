# Development Milestones

The planned development sequence is M0 through M8. Work advances one reviewed milestone at a time.

| Milestone | Status and scope |
| --- | --- |
| M0 | Repository foundation: solution structure, dependency boundaries, documentation, test discovery, and minimal WPF shell. |
| M1 | Airport and weather domain: explicit runway ends, normalized wind observations and source contracts, deterministic wind components, and R1 runway recommendation/ranking. |
| M2 | Circuit definition: per-runway-end traffic side with provenance, explicit MSL/AGL altitude semantics and provenance, the C172 1,000 ft AGL training default, deterministic resolution, and immutable `CircuitDefinition`. |
| M3 | Implemented: threshold-based WGS84 horizontal geometry, explicit metre-based dimensions, mirrored LEFT/RIGHT leg corridors, static Base Turn Window/base area, and 45-degree downwind entry. UNKNOWN side remains unavailable. |
| M4 | Reserved for separately approved scope. |
| M5 | Reserved for separately approved scope. |
| M6 | Reserved for separately approved scope. |
| M7 | Reserved for separately approved scope. |
| M8 | Reserved for separately approved scope. |

Version 0.1.0 runway recommendations, circuit definition, and static pattern geometry are implemented through M3. Telemetry processing, live weather sources, phase detection, transition cues, coaching, scoring, integrations, and record/replay remain future work. Their allocation and detailed design require separate approval.

M1 is limited to airport/weather contracts and deterministic R1 wind geometry. M2 defines what circuit is trained. M3 defines where it exists horizontally using selected threshold positions, while preserving M2 side/altitude provenance. It does not use the stored runway heading as geographic rotation authority. The Base Turn Window is static geometry; aircraft membership, actual turn timing, phase detection, and cues are M4-or-later concerns and are not implemented. See [M3 Geometry](M3-Geometry.md) for bounds and invariants.

