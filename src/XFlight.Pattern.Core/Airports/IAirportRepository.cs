namespace XFlight.Pattern.Core.Airports;

public interface IAirportRepository
{
    ValueTask<Airport?> FindByIdentifierAsync(AirportIdentifier identifier, CancellationToken cancellationToken = default);
}
