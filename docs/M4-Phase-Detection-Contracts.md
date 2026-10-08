# M4-B1 — Phase Detection Contracts

M4-B1 adds immutable, simulator-independent contracts in `XFlight.Pattern.Core.PhaseDetection`. It does not implement a phase detector. This document consolidates the approved M4-A/A2 decision summaries available in the review history and the actual M4-B1 implementation. The verbatim M4-A/A2 reports are not present in this repository.

## Architecture and scope

M2 selects the circuit and retains side and altitude authority. M3 supplies static horizontal geometry from the selected runway thresholds. M4 uses that geometry as detection reference without changing it. Detection, coaching, and scoring remain separate concerns. Normalized telemetry and explicit configuration cross into deterministic Core logic; SimConnect, WPF, audio, weather providers, and other runtime adapters do not.

The approved design uses a pure, replayable transition from previous state and one source snapshot to a new result. M4-B1 supplies the values and local validation for that boundary. The pure transition rules, observation classification, phase confirmation, and replay/checkpoint validation are deferred to M4-B2/B3. No threshold is inferred from an aircraft profile or hidden default: policy durations, margins, tolerances, speeds, and relocation limits are caller-supplied.

## Immutable inputs and configuration

`TelemetrySnapshot` identifies one sample by nonblank stream epoch, positive sequence, and nonnegative elapsed source time within that epoch. Each position, altitude MSL, ground speed, true ground track, vertical speed, and on-ground observation carries availability and provenance. Unavailable observations have neither a value nor claimed provenance. Available numeric measurements must be finite; ground speed is nonnegative and track is in `[0, 360)` degrees. Source time is not wall-clock time.

`PhaseDetectionPolicy` holds positive candidate, ground-contact, departure, exit, sample-gap, and candidate-gap durations; candidate gap cannot exceed sample gap. It holds explicit finite corridor margins, track tolerances, speed/climb thresholds, outer-exit margin, and relocation speed/distance limits. Exit thresholds are no smaller than enter thresholds where hysteresis is defined. The policy validates values; only the future detector can accumulate samples and decide whether persistence has been met.

`PhaseDetectionConfiguration` binds nonblank algorithm and configuration IDs, a policy, available M3 geometry, and a positive runway-contact half-width. Its closed runway-contact envelope uses along-track bounds from the landing threshold (`0`) to the opposite threshold (the M3 projected runway length) and the supplied symmetric cross-track half-width. Its closed outer-exit envelope bounds the runway centerline, all M3 leg-corridor corners, and the M3 base area, then expands each side by the policy exit margin. These M4 envelopes are separate from and do not mutate M3 geometry. They are spatial references, not aircraft membership or phase decisions.

## Replayable state, results, and events

`PhaseDetectionState` retains algorithm/configuration/epoch identity; the last consumed sample sequence and source time; confirmed phase; optional attempt and candidate evidence; cumulative transition and rejected-sample counts; cue latches; and the last accepted valid position with continuity status. An unstarted stream has no last sequence or time. Candidate evidence records a defined phase, ordered source-time interval, and positive sample count. The position anchor records its own sequence and time. An accepted unavailable position retains an existing older anchor and marks continuity `Interrupted`; with no anchor, continuity remains `NoAcceptedPosition`. A subsequent accepted valid position becomes the current `Continuous` anchor. Rejected samples are intended to retain the prior anchor and continuity, as the detector must verify against previous state.

`DetectionAttemptIdentity` comprises algorithm ID, configuration ID, stream epoch, and the sequence of the sample that began the attempt. State and event constructors validate that attempt identity belongs to their named stream and does not start after the represented sample. Cue latches are scoped to the active attempt: a state without an attempt cannot have any set latch. The future detector decides when a new attempt begins and resets its latches.

`PhaseDetectionResult` groups the input snapshot, resulting state, outcome (`Accepted`, `Rejected`, or `Interrupted`), corresponding reason when required, telemetry-quality issues, ordered transition events, and cue events. Accepted and interrupted results consume the supplied snapshot, so their resulting cursor must equal its sequence and source time. Rejected results do not claim to consume it. Only accepted results can emit events. Accepted available position must match the current continuous anchor exactly in value, sequence, and time; accepted unavailable position requires `MissingPosition` and interrupted continuity when an older anchor exists. Available position cannot claim `MissingPosition`.

Every event has algorithm/configuration/epoch, sample sequence, and ordinal identity plus source time. Result construction requires event identities and times to match the cycle, unique ordered ordinals, a coherent transition chain, and a final transition destination equal to the resulting confirmed phase. The cumulative transition count must at least accommodate transitions emitted in that result. Its exact increment cannot be checked without previous state.

`PhaseCueEvent` is retrospective: it references an earlier confirmed transition in the same sample and source time. Entry, Base Turn, Final, Go Around, and Exit cues require transitions to `Entry`, `Base`, `Final`, `GoAround`, and `Exited`, respectively. The transition must carry an attempt identity. Result construction requires the referenced transition to be in the result, the cue attempt to match the resulting state attempt, and the corresponding resulting latch to be set. This makes emitted cues locally consistent; it does not prove that the latch was previously clear or that a cue occurs only once over the full attempt.

## Base Turn Window and intended cycle semantics

M3's Base Turn Window is a static range before the landing threshold, not a selected turn point or a cue trigger. The M3 base area allows positions across that window toward the final axis. The planned detector must use observed evidence and explicit policy to confirm a `Base` transition; a Base Turn cue is retrospective to that confirmation. Merely entering the static window does not confirm a turn or emit a cue.

The phase vocabulary includes `LandingRoll`, `GoAround`, and `Exited`, while outcome and event contracts can represent accepted, rejected, interrupted, and confirmed transitions. Intended detector behavior is to distinguish a go-around from a completed landing, a touch-and-go from a full stop, and renewed departure from landing roll using ordered source evidence. A full stop must not silently become a new departure; a touch-and-go requires an explicit, deterministic attempt boundary; and a go-around requires a confirmed `GoAround` transition before its cue. M4-B1 does not choose evidence thresholds, exact transition paths, completion criteria, or attempt rollover for these cases. Those decisions and tests belong to M4-B2/B3.

## Determinism and deferred invariants

The same ordered snapshots, configuration, and valid previous state must eventually yield equal states, events, event identities, and results without wall-clock time, randomness, simulator installation, locale-sensitive ordering, or mutable provider state. Source sequence and elapsed source time define ordering within an epoch. Event ordinals provide stable order within one sample. Replay must validate checkpoint provenance and each successor rather than trusting a self-contained state value as historical proof.

The following require `previousState` and are **detector-owned**, not missing context-free constructor checks:

| Deferred rule | Required transition context |
| --- | --- |
| Exact transition-count increment | Prior cumulative count and emitted transitions |
| First transition starts at the prior confirmed phase | Prior confirmed phase |
| Rejected-state preservation and rejected-count increment | Prior cursor, phase, candidate, attempt, position, latches, and count |
| Cue latch false-to-true update and once per attempt | Prior attempt identity and latches |
| Retention of the actual previous valid position on unavailable input | Prior position anchor and continuity |
| New-attempt latch reset and cycle boundary | Prior attempt and confirmed cycle history |
| Checkpoint provenance and replay continuity | Validated prior checkpoint and ordered source history |

## Certification boundary

M4-B1 local contracts received **PASS_WITH_CONDITIONS**: locally visible contradictions are rejected, while historical guarantees above remain for the pure detector and replay validation. The last completed solution validation reported **106 passing tests, zero failures**. That result covers contracts and existing repository tests, not detector behavior. Core has no simulator or UI dependency, and M4-B1 adds no package dependency.
