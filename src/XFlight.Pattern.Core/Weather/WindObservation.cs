using XFlight.Pattern.Core.Airports;

namespace XFlight.Pattern.Core.Weather;

public sealed record WindObservation
{
    private WindObservation(WindState state, Heading? direction, double? speedKnots, double? gustKnots, WeatherSourceIdentity source, DateTimeOffset? observedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        State = state;
        Direction = direction;
        SpeedKnots = speedKnots;
        GustKnots = gustKnots;
        Source = source;
        ObservedAt = observedAt;
    }

    public WindState State { get; }
    public Heading? Direction { get; }
    public double? SpeedKnots { get; }
    public double? GustKnots { get; }
    public WeatherSourceIdentity Source { get; }
    public DateTimeOffset? ObservedAt { get; }

    public static WindObservation Directional(Heading direction, double speedKnots, WeatherSourceIdentity source, double? gustKnots = null, DateTimeOffset? observedAt = null)
    {
        ValidateSpeed(speedKnots, nameof(speedKnots));
        if (speedKnots == 0d)
            throw new ArgumentOutOfRangeException(nameof(speedKnots), "Directional wind speed must be greater than zero; use Calm for zero wind.");

        ValidateGust(speedKnots, gustKnots);
        return new(WindState.Directional, direction, speedKnots, gustKnots, source, observedAt);
    }

    public static WindObservation Variable(double speedKnots, WeatherSourceIdentity source, double? gustKnots = null, DateTimeOffset? observedAt = null)
    {
        ValidateSpeed(speedKnots, nameof(speedKnots));
        ValidateGust(speedKnots, gustKnots);
        return new(WindState.Variable, null, speedKnots, gustKnots, source, observedAt);
    }

    public static WindObservation Calm(WeatherSourceIdentity source, DateTimeOffset? observedAt = null) =>
        new(WindState.Calm, null, 0d, null, source, observedAt);

    public static WindObservation Unavailable(WeatherSourceIdentity source, DateTimeOffset? observedAt = null) =>
        new(WindState.Unavailable, null, null, null, source, observedAt);

    private static void ValidateSpeed(double speedKnots, string parameterName)
    {
        if (!double.IsFinite(speedKnots) || speedKnots < 0d)
            throw new ArgumentOutOfRangeException(parameterName, "Wind speed must be finite and non-negative.");
    }

    private static void ValidateGust(double speedKnots, double? gustKnots)
    {
        if (gustKnots is not null && (!double.IsFinite(gustKnots.Value) || gustKnots.Value < speedKnots))
            throw new ArgumentOutOfRangeException(nameof(gustKnots), "Gust speed must be finite and not less than sustained speed.");
    }
}
