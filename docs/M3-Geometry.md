# M3 — Pattern Geometry & Entry

M2 defines **what** circuit is trained. M3 defines **where** it exists horizontally. `PatternGeometryResult.Generate(circuit, runwayReference, parameters)` retains the original immutable circuit, runway reference, and parameters. Side and altitude authority/provenance are never re-resolved. UNKNOWN side returns `UnknownTrafficPatternSide` with no geometry. Invalid inputs throw domain argument exceptions, including identity mismatch even when side is unknown.

## Threshold reference and coordinates

`RunwayGeometryReference` identifies the airport, selected directional runway-end identifier, landing threshold, and opposite/departure threshold. Identifiers use the existing invariant normalization convention; airport value equality and ordinal runway-end equality must match the circuit. Reciprocal runway ends use separate references with their own landing origins and M2 sides.

`GeographicCoordinate` accepts finite latitude in [-90, 90] and longitude in [-180, 180] degrees. +180 longitude canonicalizes to -180. `LocalHorizontalFrame` converts WGS84 ellipsoid-surface positions to ECEF with semi-major axis 6,378,137 m and inverse flattening 298.257223563, subtracts the origin's ECEF position, and projects into East/North. It handles longitude wrap through ECEF, without subtracting longitudes directly. This is an airport-scale tangent-plane approximation, not a geodesic/GIS system. Both positions use ellipsoid height zero; altitude authority stays entirely in M2.

The landing threshold maps to (AlongTrack 0, CrossTrack 0). The opposite threshold's local East/North vector defines the unit forward vector `(fe, fn)` and local horizontal runway length `L`. Length is the projected threshold separation, not surveyed surface distance. Separations of 0.001 m or less are rejected as effectively zero. `RunwayEnd.Heading` never rotates M3 geometry: its true/magnetic semantics are unspecified.

For local metres `(E, N)`:

```text
AlongTrack = E * fe + N * fn
CrossTrack = E * fn - N * fe
right unit vector = (fn, -fe)
```

Northward runways have East to their right; eastward runways have South to their right. AlongTrack increases toward the opposite threshold/upwind. CrossTrack is right-positive and left-negative.

## Explicit dimensions and invariants

`HorizontalDistance.FromMetres` and `FromNauticalMiles` (1 NM = 1,852 m) accept only finite positive values, stored canonically in metres. `PatternGeometryParameters` requires all seven dimensions; there are no horizontal defaults.

| Symbol | Parameter | Meaning |
| --- | --- | --- |
| D | DownwindOffset | Distance from runway centerline to downwind axis |
| U | UpwindExtension | Extension beyond opposite threshold |
| Bn | NearerBaseTurnDistance | Nearer limit before landing threshold |
| Bf | FartherBaseTurnDistance | Farther limit before landing threshold |
| F | FinalExtension | Final axis extension before landing threshold |
| E | EntryLength | Length of the 45-degree entry axis |
| W | CorridorHalfWidth | Common lateral half-width (full width is 2W) |

Required relationships: `Bn < Bf`, `F >= Bf`, `W < D`, and `E / sqrt(2) > W`. These keep the base window nonempty, its entire range connected to final, downwind wholly on its selected side, and the entry start strictly outside the lateral circuit footprint. Finite inputs that overflow or collapse derived coordinates/segments/corridors at floating-point scale are rejected during construction.

## Static corridors and bounds

Use `s = -1` for LEFT, `s = +1` for RIGHT. Every pair below is `(AlongTrack metres, CrossTrack metres)`. Segments are oriented start-to-end. A `LegCorridor` is a closed rectangle offset by `W` on each side of its axis, with flat ends at the axis endpoints. Its fixed corners are StartPlus, EndPlus, EndMinus, StartMinus; plus is the normal `(-deltaCross, deltaAlong)` normalized to `W`. Reflection swaps plus/minus corners because it reverses winding. This axis defines corridor orientation, not an exact GPS track to fly.

| Output | Axis or closed bounds |
| --- | --- |
| RunwayCenterline | `(0, 0)` to `(L, 0)` |
| Upwind | `(L, 0)` to `(L + U, 0)` |
| Crosswind | `(L + U, 0)` to `(L + U, sD)` |
| Downwind | `(L + U, sD)` to `(-Bf, sD)`; decreasing AlongTrack |
| BaseTurnWindow | AlongTrack `[-Bf, -Bn]`, CrossTrack `[sD - W, sD + W]` |
| BaseArea | AlongTrack `[-Bf, -Bn]`, CrossTrack `[min(0, sD) - W, max(0, sD) + W]` |
| Final | `(-F, 0)` to `(0, 0)`; increasing AlongTrack |
| EntryJoinTarget | `(L / 2, sD)`; abeam physical runway midpoint in the local frame |

The base area permits travel from downwind toward CrossTrack zero at any longitudinal position in the window. It overlaps final across the complete window and selects no actual turn point. The window remains entirely before the landing threshold. LEFT/RIGHT outputs have identical longitudinal coordinates, lengths, and widths, and opposite lateral signs; area minimum/maximum lateral bounds exchange under reflection.

## Authorized 45-degree entry

Let `q = E / sqrt(2)`. Entry starts at `(L / 2 + q, s(D + q))` and ends at the join target `(L / 2, sD)`. Its travel vector is `(-q, -sq)`: it progresses with downwind while converging from outside. Its dot product with downwind's unit direction `(-1, 0)`, divided by its magnitude, equals `1 / sqrt(2)`. Tests verify this angle with vector mathematics and verify the configured length. No other entry procedures are supplied.

This geometry is a training reference, not ATC clearance or a claim that this entry is appropriate in every real-world situation.

## Altitude, determinism, and the M4 boundary

The geometry retains `CircuitDefinition.PatternAltitude` unchanged. Unresolved altitude does not block known-side horizontal geometry and never becomes the C172 altitude default. There are no vertical corridor limits or vertical phase calculations.

Domain values are immutable records with get-only properties. The aggregate uses fixed named members and corners rather than mutable collections or dictionary enumeration. Equal inputs produce equal results independent of locale, time, random state, or external providers.

M3 has no telemetry ingestion, aircraft corridor membership, phase detection, turn timing, transition cues, coaching, scoring, live data loading, provider integrations, wind correction, terrain clearance, performance/turn-radius models, replay, or map/UI rendering. Those require separately authorized M4-or-later work. Existing R1 recommendation behavior and M2 precedence remain unchanged.
