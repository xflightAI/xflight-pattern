using XFlight.Pattern.Core.Airports;

namespace XFlight.Pattern.Core.Weather;

public interface IWeatherSource
{
    WeatherSourceIdentity Identity { get; }
    ValueTask<WindObservation> GetWindAsync(AirportIdentifier airportIdentifier, CancellationToken cancellationToken = default);
}
