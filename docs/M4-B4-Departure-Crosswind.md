# M4-B4 — Departure & Crosswind Contract

Status: **FROZEN for the bounded implementation defined below.** D1, D2 and D3 were explicitly approved by the user. This document specifies future behavior; the baseline implementation still supports only the inbound transition path. Documentation freeze does not authorize automatic implementation.

## Baseline and source authority

The verified baseline is `main` at `49d815f330cdb7368db06ddc90ae7ade6dafe9f8`, with a clean working tree. The locally stored `origin/main` ref is at the same commit; no network operation was used to verify it.

The following existing definitions govern this contract:

- [M4-B1 contracts](M4-Phase-Detection-Contracts.md): immutable inputs, caller-supplied policy, state, attempts and events.
- [M4-B2 evidence](M4-B2-Geometry-Membership.md): closed membership, independent direction evidence and threshold-derived runway orientation.
- [PhaseDetectionPolicy](../src/XFlight.Pattern.Core/PhaseDetection/PhaseDetectionPolicy.cs): existing phase vocabulary and named thresholds.
- [PhaseDetectionState](../src/XFlight.Pattern.Core/PhaseDetection/PhaseDetectionState.cs): candidate intervals, consumed cursor and accepted-position continuity.
- [DeterministicPhaseTransition](../src/XFlight.Pattern.Core/PhaseDetection/DeterministicPhaseTransition.cs) and [InboundTransitionRules](../src/XFlight.Pattern.Core/PhaseDetection/InboundTransitionRules.cs): ordering, rejection precedence, persistence and ambiguity protections.
- [GeometryMovementEvidence](../src/XFlight.Pattern.Core/PhaseDetection/GeometryMovementEvidence.cs): runway contact, departure/upwind and leg observations.
- [PhaseReplayContext](../src/XFlight.Pattern.Core/PhaseDetection/PhaseReplayContext.cs): origin-only public replay and opaque cursors.
- [PhaseDetectionResult](../src/XFlight.Pattern.Core/PhaseDetection/PhaseDetectionResult.cs): outcome, quality and event consistency.

All durations and thresholds retain their existing caller-supplied values. This contract introduces no threshold value, phase enum member, telemetry field or named public API. Descriptions of additive contact evidence below specify required information, not new type or member names.

## Approved decision record

| Decision | Approved rule | Contract consequence |
| --- | --- | --- |
| D1 — Ground contact gate | Require sustained observed runway ground contact. Apply no maximum ground-speed restriction. Ground contact alone must never confirm `Departure`. | Accumulate contact only from accepted available position inside the runway-contact envelope and available `OnGround == true`. Use `GroundContactPersistence`. `GroundContactMaximumSpeedMetresPerSecond` is not used by this gate. |
| D2 — Ground roll continuity | Once established, the gate remains valid through a continuous ground roll on the same runway. Introduce no elapsed-time timeout. Distinguish valid liftoff from invalid loss of evidence. | Keep the gate through qualifying ground samples, regardless of total roll duration or speed. A qualifying first airborne sample uses the gate to begin departure persistence. Accepted evidence loss and interruptions invalidate it as defined below. |
| D3 — Positive climb | Require vertical speed strictly greater than zero and compliance with `DepartureMinimumClimbMetresPerSecond`. | Available vertical speed must satisfy both `value > 0` and `value >= DepartureMinimumClimbMetresPerSecond`. A zero configured minimum cannot admit zero vertical speed. |

## Bounded transition graph

The exact existing `PatternPhase` vocabulary is `Unknown`, `Entry`, `Departure`, `Crosswind`, `Downwind`, `Base`, `Final`, `LandingRoll`, `GoAround`, and `Exited`. `Departure` covers departure and initial climb; there is no separate initial-climb phase.

| Previous confirmed phase | Allowed next confirmed phase | Required duration |
| --- | --- | --- |
| `Unknown` | `Entry` | Existing `CandidatePersistence` and inbound rules |
| `Unknown` | `Departure` | Valid launch gate and airborne evidence for `DeparturePersistence` |
| `Entry` | `Downwind` | Existing `CandidatePersistence` and inbound rules |
| `Departure` | `Crosswind` | `CandidatePersistence` |
| `Crosswind` | `Downwind` | `CandidatePersistence` |
| `Downwind` | `Base` | Existing `CandidatePersistence` and inbound rules |
| `Base` | `Final` | Existing `CandidatePersistence` and inbound rules |

`Unknown → Departure → Crosswind → Downwind` joins the existing `Downwind → Base → Final` path. The existing `Unknown → Entry → Downwind → Base → Final` path retains its requirements. Only one confirmed transition may occur per accepted sample. Evaluate eligibility from the previous confirmed phase; a sample confirming `Departure` cannot also begin or confirm `Crosswind`.

Initial `Crosswind`, `Downwind`, `Base` or `Final`, `Departure → Downwind`, reverse transitions and all other unlisted transitions remain unsupported. Unsupported movement is normally an accepted sample with no transition, not a sample rejection. B4 adds no outgoing transition from `Final`, `LandingRoll`, `GoAround` or `Exited`.

## Runway-contact persistence and launch gate

Contact evidence is accumulated only while the confirmed phase is `Unknown`. Every contributing sample must pass the existing ordering, continuity and relocation checks, have available position, be inside `GeometryMovementEvidence.InsideRunwayContactEnvelope`, and have available `OnGround == true`.

The contact envelope is the existing closed `PhaseDetectionConfiguration.RunwayContactEnvelope`: along-track from the selected landing threshold to the opposite threshold, with the configured runway-contact half-width. Do not substitute the wider departure/upwind region or expand contact bounds with leg corridor margins. Available position outside the envelope is known loss of membership; unavailable position is an interruption, not proof of being outside.

Ground speed, track, altitude and vertical speed are not prerequisites for contact or ground-roll validity. They can be unavailable, and ground speed can exceed `GroundContactMaximumSpeedMetresPerSecond`. Existing telemetry-quality reporting still records unavailable observations. This avoids imposing an unapproved speed or direction test on observed contact.

The first qualifying accepted ground sample starts a contact interval with count one and equal first/last source time. Further qualifying ground samples extend it. Before establishment, a source-time interval since the preceding contributing sample greater than `MaximumCandidateGap` restarts contact persistence at the current ground sample; equality continues it. Establish the gate when the interval's last source time minus first source time is at least `GroundContactPersistence`. Equal source times can increase sample count but add no duration. Positive configured persistence prevents a single sample from establishing the gate.

An established gate remains valid while each accepted ground sample continues to have `OnGround == true` and closed runway-contact membership in the same fixed configuration. Its total age is unlimited. An inter-sample interval greater than `MaximumCandidateGap` but no greater than `MaximumSampleGap` does not expire an established ground-roll gate: it is no longer a pending contact candidate. The existing sample-continuity limit still applies. A stationary aircraft can establish a gate; neither stationarity nor continuous ground roll confirms departure or creates an attempt.

An accepted ground sample outside the contact envelope clears both incomplete contact evidence and an established gate. Re-entry starts contact persistence afresh. Available position with unavailable `OnGround` also clears them, even if horizontal membership remains true. No previous contact observation may be substituted for missing current ground status.

## Valid liftoff and airborne departure confirmation

The first accepted `OnGround == false` sample following an established ground-roll gate is a valid liftoff observation only if all departure candidate requirements hold on that sample:

- Position, ground speed, true ground track, vertical speed and on-ground observation are available.
- `DepartureUpwind.InsideEnterBounds` is true. This is the existing union of runway-centerline and upwind corridors, with existing enter margins.
- Departure/upwind direction status is `WithinEnterTolerance`; the ground speed meets the replay context's explicit `MinimumUsableGroundSpeedMetresPerSecond` as required by M4-B2.
- Ground speed is at least `DepartureMinimumSpeedMetresPerSecond`.
- Vertical speed is strictly positive and at least `DepartureMinimumClimbMetresPerSecond`.
- The unchanged horizontal ambiguity checks permit the departure target.

This sample starts `CandidateEvidence` for `Departure` at its own source time, with count one. Contact duration and ground-roll duration do not contribute to airborne departure persistence. The sample need not remain inside the physical runway-contact envelope: entering the existing upwind region at liftoff is valid. The preceding accepted ground sample must have retained the contact gate.

An airborne sample without an established gate cannot start a departure candidate. If the first airborne sample fails any departure requirement, clear the gate and contact evidence; later airborne samples cannot recover launch authorization. This includes missing climb/speed/track, zero or negative climb, insufficient configured speed or climb, movement outside departure enter bounds, and ambiguous horizontal evidence. The sample is still accepted when its ordering, position continuity and relocation checks pass.

After valid liftoff, retain the established contact proof solely to authorize the pending `Departure` candidate. Every further accepted airborne sample must meet all the same departure requirements. A qualifying interval greater than `MaximumCandidateGap` but no greater than `MaximumSampleGap` restarts the airborne candidate at the current sample, consistent with existing candidate behavior; it does not create new contact proof. There is no launch-gate age timeout. Any accepted airborne sample that fails the departure requirements clears both candidate and launch authorization.

An accepted return to `OnGround == true` before departure confirmation clears the airborne candidate and its authorization. If that sample is inside the runway-contact envelope, it starts a fresh contact interval; otherwise contact evidence remains absent. Previous pre-liftoff persistence cannot be reused after an airborne break. B4 does not interpret this as touch-and-go.

Confirm `Unknown → Departure` when the uninterrupted qualifying candidate interval reaches `DeparturePersistence`, inclusively. Clear the candidate and all contact/launch evidence on confirmation. This transition is the only departure confirmation; contact establishment and first liftoff produce no transition or cue. Altitude MSL is not an additional departure requirement, and no AGL or altitude-gain calculation is inferred.

## Leg candidates, exit hysteresis and ambiguity

For `Departure → Crosswind` and `Crosswind → Downwind`, require available `OnGround == false` and the target region's existing enter membership and `WithinEnterTolerance` direction. Use `CandidatePersistence`; do not require positive climb after `Departure` has been confirmed. Preserve the existing requirements for all inbound candidates.

Apply `InboundTransitionRules.Unambiguous` semantics to every target, including `Departure`:

1. The target must satisfy enter membership and enter direction tolerance.
2. When the confirmed phase is not `Unknown`, its region retains priority while both exit membership and enter-or-exit direction tolerance hold. In that case clear a pending successor candidate and hold the confirmed phase.
3. Independently check `Entry`, `Departure`, `Crosswind`, `Downwind`, `Base` and `Final`. If any region other than the target qualifies under the horizontal enter rules, hold and clear the candidate. Do not ignore a competing region because it is an unsupported next phase, lacks a launch gate or would fail a vertical condition.

For `Unknown`, neither `Entry` nor `Departure` receives a preference that overrides these ambiguity protections. An unambiguous eligible `Entry` remains available under B3 rules. Contact evidence is discarded once an accepted airborne sample is not eligible for departure; this does not prevent that same sample from starting an otherwise eligible inbound `Entry` candidate.

Candidate persistence uses source time and consecutive qualifying accepted observations. Continue an existing candidate of the same target when the difference from its last source time is no greater than `MaximumCandidateGap`; otherwise start at count one. A nonqualifying accepted sample clears it. Confirm when its elapsed interval reaches the applicable persistence duration. Sample-count increments alone never satisfy persistence.

Keep the existing corridor enter/exit margins, track enter/exit tolerances, closed spatial boundaries and M4-B2 one-representable-value angular rounding allowance. Do not add a fixed angular epsilon, widen contact membership, select a turn point or use an exit margin as candidate-enter evidence. Exit hysteresis blocks successors; losing current-region evidence does not itself reset a confirmed phase or confirm another phase.

## Sample outcomes and precedence

Preserve the existing processing precedence: locally validate state/configuration; reject epoch mismatch, duplicate sequence, reversed sequence or reversed source time in that order; detect excessive sample gap; interrupt on unavailable position; then check implausible relocation before evaluating contact or phase evidence. Increasing sequences may skip numbers; source time may be equal but must not reverse. No sample-count gap rule is added.

| Outcome or observation | Cursor and position behavior | Contact/gate and candidate behavior | Phase, attempt and events |
| --- | --- | --- | --- |
| Accepted valid position | Consume sequence/time; replace accepted-position anchor; mark `Continuous`; report existing missing-observation quality issues | Accumulate, retain, restart or clear according to the rules above | Hold phase unless a permitted candidate confirms; at most one transition; no cues |
| Rejected for any existing reason | Preserve consumed cursor, anchor and continuity; increment `RejectedSampleCount` exactly once | Preserve all prior contact, gate and candidate evidence unchanged; do not count the sample | Preserve phase, attempt, transition count and latches; no events; retain existing empty quality-issue behavior |
| Interrupted | Consume sequence/time; retain older accepted-position anchor; mark `Interrupted`, or `NoAcceptedPosition` without an anchor | Clear all contact, launch authorization and phase candidates | Preserve confirmed phase, attempt, counts and latches; no events |

Accepted insufficient speed, climb, track or ground status is evidence failure, not a new rejection reason. Missing altitude is reported but does not block B4 transitions. Missing vertical speed blocks only departure confirmation; it does not weaken or change the existing inbound path. After confirmation, an on-ground or otherwise nonqualifying accepted sample holds `Departure` or `Crosswind` and clears its pending successor; it does not arm a new departure gate.

## Gaps, interruption, relocation and reset

An interval greater than `MaximumSampleGap` from the previous consumed source time produces `SampleContinuityGap` interruption with `ContinuityGap` quality issue. Equality is allowed. Unavailable position produces `PositionContinuityLost` with `MissingPosition`, unless the earlier sample-gap check already determined the interruption reason. Both outcomes invalidate all launch evidence. The interrupted sample cannot contribute ground contact or start an airborne candidate, even when it contains a valid position.

On the next accepted valid position after interruption, start a new displacement interval; never compare relocation against the older interrupted anchor. If phase is `Unknown`, a qualifying ground sample can start fresh contact persistence. A still-airborne sample cannot regain a cleared departure gate. Confirmed `Departure` or `Crosswind` is retained across interruption, and its next qualifying accepted successor sample can start fresh candidate persistence under the usual rules.

Relocation continues to use displacement in the configured runway frame from the last continuous accepted-position anchor. Reject if distance is greater than `MaximumRelocationDistanceMetres` or displacement speed is greater than `MaximumRelocationSpeedMetresPerSecond`; equality is permitted. At equal source time, nonzero displacement is rejected. A relocation rejection preserves launch evidence, exactly like every other rejection: the unconsumed outlier cannot change accepted history. Subsequent samples are compared with the unchanged accepted anchor and cursor. A gap interruption takes precedence over relocation, as in B3.

Intentional repositioning, a selected-runway/configuration/algorithm change, stream restart or a changed explicit minimum usable speed requires a fresh `PhaseReplayContext` and its `Start()` origin. The origin has `Unknown`, no contact proof, gate, candidate or attempt, zero counts and clear cue latches. Evidence never transfers between contexts, even when IDs are reused. A wrong-epoch snapshot in the existing context is rejected and preserves that context's state; it does not silently switch epochs or reset. Advancing a cursor owned by another context remains an argument error with no successor result. B4 adds no reset or imported-state API.

## Minimal additive state and invariants

The existing `PhaseDetectionState` must retain one optional immutable contact-evidence payload, separate from `CandidateEvidence`. The required information is the contact interval's first and last source times, positive contributing-sample count, and last contributing ground sample sequence. A gate's establishment is derivable from its contact duration and the bound policy; no additional lifetime timer is needed. Pending airborne use is distinguishable by the existing `Departure` candidate. Do not encode contact stages in another `PatternPhase`, event ordinal, cue latch or sample count.

This information must be part of the replayable state value and its equality. It must not live only in mutable context fields, a provider, an external cache or unexposed cursor-only history. Preserve existing constructor signatures and their meaning: construction without the additive evidence means no runway-contact proof and no launch gate. Implementation may choose the additive representation and names without changing this behavioral contract.

Required local and previous-state invariants are:

- Contact evidence is absent at an unstarted origin and whenever confirmed phase is other than `Unknown`. `Unknown` continues to have no attempt identity and clear cue latches.
- Contact times are nonnegative and ordered, sample count is positive, and count one requires equal first/last source times. The last contributing ground sequence is positive and no greater than the consumed sequence; its source time is no later than the consumed source time. The engine and trusted replay validate its membership in the actual consumed history. Contact shares the enclosing state's algorithm, configuration and epoch and the context's fixed geometry authority; it cannot be copied into a different replay context.
- While accumulating contact or retaining an established ground-roll gate, the contact evidence's last sequence/time equals the consumed cursor, position continuity is `Continuous`, and no phase candidate is present. The contributing current sample has available on-ground truth and runway-contact membership.
- An incomplete contact interval cannot authorize an airborne `Departure` candidate. When such a candidate exists, retained contact duration has met `GroundContactPersistence`, its last ground sequence is strictly before the cursor, and its last ground source time is no later than the airborne candidate's first source time. Equal ground/liftoff source times are allowed but add no airborne duration.
- Retained airborne contact proof exists only with a pending `Departure` candidate under `Unknown`, with continuous accepted position. A `Departure` candidate requires that proof; an `Entry` candidate cannot coexist with it. Candidate last source time equals the consumed cursor.
- Candidates must be permitted successors in the bounded graph. Confirmed `Entry`, `Departure`, `Crosswind`, `Downwind`, `Base` and `Final` require an attempt identity. Confirmation clears its candidate and contact proof.
- Rejection copies additive evidence exactly. Interruption clears it. Accepted evidence failure cannot leave a stale authorized launch behind.
- Constructors enforce locally visible ordering and consistency. The internal engine checks successor/history invariants against previous state and current snapshot; public replay provenance supplies historical authority. A self-contained state value is not historical proof.

## Attempt/event identity and deterministic replay boundary

Preserve B3 attempt timing: create `DetectionAttemptIdentity` at the sample confirming the first `Unknown → Entry` or `Unknown → Departure` transition. Its `StartSampleSequence` is the confirmation sample, not the contact-start or first-liftoff sample. Contact persistence and pending candidates create no attempt. Keep that same identity through crosswind, downwind, base and final; no attempt rollover is added.

Each confirmation increments `TransitionCount` exactly once and emits one `PhaseTransitionEvent` with the configured algorithm/configuration IDs, stream epoch, confirmation sample sequence, ordinal `0` and that sample's source time. Its `From` is the prior confirmed phase and `To` is the new phase. Candidate updates, gate establishment and evidence failure emit no events. All B4 and retained inbound results continue to have no cues and clear cue latches.

The same fixed configuration, explicit minimum usable speed, origin and ordered snapshots must yield equal states including contact evidence, outcomes, reasons, quality issues, attempts and events. No wall clock, randomness, provider state, locale-dependent ordering or mutable cursor is permitted. Branching from an earlier immutable cursor remains supported and deterministic.

`PhaseReplayContext.Start()` and `Advance(cursor, snapshot)` remain the public calculation boundary. `PhaseReplayCursor` retains its internal constructor and internal state/owner; no public state injection, imported checkpoint or cursor construction is introduced. `DeterministicPhaseTransition` remains internal. Do not add `InternalsVisibleTo`, a friend-assembly grant or a testing bypass. Behavior tests advance trusted cursors through the public boundary; context-free contract tests may construct state values without treating them as certified replay history.

## Focused implementation test matrix

| Area | Cases | Required assertion |
| --- | --- | --- |
| Existing inbound behavior | Both sides; complete `Unknown → Entry → Downwind → Base → Final`; missing altitude/vertical speed | Existing transitions, attempts, persistence, quality and no-cue behavior remain intact |
| Contact persistence | First sample; equal times; just below/exact `GroundContactPersistence`; high speed; unavailable speed/track/climb/altitude | Gate only after source duration; no maximum speed restriction; no departure event or attempt |
| Closed contact bounds | Thresholds and lateral edges; just outside; membership differs from departure corridor | Use physical closed contact envelope; outside clears evidence; re-entry restarts |
| Continuous roll | Long total duration; contact gap equal/above `MaximumCandidateGap` before establishment; established gate with interval above candidate gap but at/below sample gap | Incomplete interval restarts; established roll does not expire by age or candidate-gap limit |
| Liftoff eligibility | Gate absent/present; lift while still above runway or already in upwind; false ground status without qualifying climb/track/speed; missing ground status | Only full qualifying liftoff starts departure; invalid loss clears authorization; no midair recovery |
| Positive climb and speed | Zero configured climb with negative/zero/positive vertical speed; below/exact positive climb minimum; below/exact departure ground-speed minimum; explicit usable-speed boundary | Apply strict positivity plus inclusive configured minimum and unchanged direction usability |
| Airborne persistence | Below/exact `DeparturePersistence`; equal times; candidate gap equality/excess; one failed accepted sample; return to ground | No ground-time credit; candidate gap restart; failure burns gate; ground return requires fresh contact persistence |
| Full departure route | Left/right and rotated threshold axis; `Unknown → Departure → Crosswind → Downwind → Base → Final` | Correct selected-side track evidence; one event per confirmation; one stable attempt |
| Hysteresis and ambiguity | Departure/crosswind and crosswind/downwind joins; retained current exit bounds/direction; competing entry or other region even if vertically ineligible | Hold current phase and clear successor; no target preference or weakened competing-region check |
| Unsupported transitions | Initial crosswind/downwind; departure-to-downwind skip; reversals; `Final` reached through trusted replay; other terminal phase values remain unreachable from the public graph | Hold reachable confirmed phase; no new path, gate or attempt rollover; do not inject terminal states through a testing bypass |
| Accepted telemetry gaps | Missing speed/track/on-ground; missing vertical speed before versus after departure; missing altitude | Clear relevant candidate/gate as specified; preserve confirmed phase; correct existing quality issues |
| Rejections and precedence | Epoch, duplicate, reversed sequence/time; simultaneous faults; relocation during contact and airborne candidate | Preserve all prior evidence/cursor/anchor; rejected count +1; no events or quality issues |
| Sample/position interruption | Exact/excess sample gap; missing position; both together; far-position recovery | Correct reason precedence; consume cursor; clear launch proof; retain phase/attempt; fresh anchor and contact |
| Relocation boundaries | Exact/above distance and speed; equal-time stationary/moving; outlier followed by valid continuation | Existing strict relocation limits; outlier cannot arm, clear or advance gate/candidate |
| Replay/context isolation | Repeat from origin; branch same cursor; reject/interruption successors; same IDs with different contexts/configuration/runway/speed; fresh epoch | Equal results; no evidence transfer; foreign cursor rejected; origin reset is empty |
| Additive state invariants | Absent/default payload; invalid interval/count/cursor; incomplete proof with departure candidate; proof in wrong phase; stale candidate | Reject local contradictions; previous-state invariants upheld through public replay |
| Public boundary | Existing constructor surface and cursor ownership checks; `FriendAssemblyBoundaryTests` | No public evaluator/state injection/cursor constructor or friend-assembly bypass |

## Proposed implementation file manifest

This documentation change creates exactly `docs/M4-B4-Departure-Crosswind.md`. It changes no existing file. Future implementation is bounded to the following existing files; this manifest is not an instruction to modify them now.

| File | Proposed implementation purpose |
| --- | --- |
| `src/XFlight.Pattern.Core/PhaseDetection/PhaseDetectionState.cs` | Add immutable contact evidence alongside existing state types; preserve existing constructor signatures; validate local invariants and equality |
| `src/XFlight.Pattern.Core/PhaseDetection/DeterministicPhaseTransition.cs` | Accumulate/retain/clear contact proof, evaluate liftoff and departure duration, preserve outcomes and identity, extend history validation |
| `src/XFlight.Pattern.Core/PhaseDetection/InboundTransitionRules.cs` | Express the bounded permitted successors and share existing region, retention and ambiguity checks without weakening them |
| `tests/XFlight.Pattern.Core.Tests/DeterministicPhaseTransitionTests.cs` | Exercise contact, liftoff, full route, continuity, rejection, deterministic replay and public-boundary behavior |
| `tests/XFlight.Pattern.Core.Tests/PhaseDetectionContractsTests.cs` | Verify additive evidence and state constructor invariants and existing-call compatibility |

`PhaseReplayContext` can continue to construct origin state through the preserved constructor with absent contact evidence. No replay signature change or separate cursor history is needed. Existing `FriendAssemblyBoundaryTests` remains a required validation check without requiring a source change. No production or test file is added by this proposed manifest.

## Exclusions, compatibility and unresolved issues

Go-around, touch-and-go, landing/full-stop completion, renewed departure from `LandingRoll`, exit detection, new cue kinds, cue emission, coaching/scoring, simulator/UI adapters and automatic implementation are excluded. There are no new phases, unrelated M3 geometry changes, runway-heading authority changes, policy fields/defaults or threshold changes. `GroundContactMaximumSpeedMetresPerSecond`, `ExitPersistence` and exit detection retain their existing definitions and are not assigned new B4 behavior.

Preserve existing public telemetry, policy, configuration, result and event contracts and constructor signatures. The additive replay-state evidence changes state contents/equality as necessary; existing states without it retain their prior meaning. The public replay boundary does not support serialized checkpoint import, and B4 adds no serialization format or migration claim. Approved launch behavior intentionally requires observed contact before liftoff; starting a replay midair cannot establish departure. Delayed or missing qualifying liftoff evidence requires fresh ground contact rather than retrospective inference.

**Unresolved essential product decisions: none.** D1/D2/D3 and the existing source definitions resolve the bounded contract. Additive type/member naming and internal helper arrangement are implementation details, not new public API decisions. A need to widen the graph, relax launch evidence, alter a public replay contract or expand the manifest must be reported before doing that work.

**GO for a separately authorized implementation of this contract.** This document records a contract freeze only; production behavior and tests remain at the verified baseline until implementation is explicitly requested.
