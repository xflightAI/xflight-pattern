# M5-B telemetry boundary

`XFlight.Pattern.Telemetry` owns simulator-neutral raw samples, a pure normalizer,
source status, and a manually driven fake source. Core continues to own
`TelemetrySnapshot` and phase detection. A future adapter supplies raw samples;
this boundary has no simulator SDK or UI dependency.

## Units and validity

| Raw observation | Core observation | Rule |
| --- | --- | --- |
| Latitude/longitude, degrees | `GeographicCoordinate`, degrees | Latitude in `[-90, 90]`, longitude in `[-180, 180]`; `+180` canonicalizes to `-180`. |
| MSL altitude, feet | MSL altitude, metres | Multiply by `0.3048`. Negative finite altitude is permitted. |
| Ground speed, knots | Ground speed, metres/second | Multiply by `1852 / 3600`; negative speed is unavailable. |
| True ground track, degrees | True ground track, degrees | Wrap finite values into `[0, 360)` using modulo 360. |
| Vertical speed, feet/minute | Vertical speed, metres/second | Multiply by `0.3048 / 60`; positive means climb. |
| On-ground, Boolean | On-ground, Boolean | Copy the observed value, including `false`. |

Every raw observation explicitly carries availability. An unavailable observation,
an available observation without a value, a non-finite numeric value, or a value
outside the Core domain becomes unavailable with no payload or provenance.
Valid observations retain measured provenance. Invalid fields do not erase other
valid fields. Missing source ID or epoch, sequence zero, negative source elapsed
time, or negative receipt time makes the whole sample unnormalizable; no Core
snapshot is created. The normalizer never supplies a default position, speed,
heading, altitude, vertical speed, or on-ground state.

## Identity, clocks, and lifecycle

`SourceId` identifies the producing source and stays beside the normalized Core
snapshot. `StreamEpoch`, `SampleSequence`, and `SourceTime` pass through to Core
unchanged. A producer is responsible for choosing a new epoch when its source
timeline resets. The boundary retains delivery order, including out-of-order
sequence numbers; Core replay decides whether an ordered input is acceptable.

`SourceTime` is elapsed simulator-source time within an epoch. `ReceiptTime` is
elapsed time from an independent monotonic receipt clock, captured when the fake
source enqueues a sample. Advancing receipt time does not advance source time or
create a sample. Future freshness monitoring can compare receipt times with a
current `IReceiptClock.Elapsed`; it must not infer source movement from that
comparison. The clock's origin and monotonicity are supplied by the adapter.

`ITelemetrySource.Status` reports disconnected, connected, or paused independently
of samples. Pausing holds queued samples; resuming permits reads. Disconnecting
clears queued samples; reconnecting permits new samples. Status changes do not
produce `TelemetrySnapshot` objects. The fake is single-threaded and manually
driven; it has no sleeps, timers, or background workers.
