namespace XFlight.Pattern.Core.Weather;

public sealed record WeatherSourceIdentity
{
    public static WeatherSourceIdentity ActiveSky { get; } = new("ActiveSky");
    public static WeatherSourceIdentity Simulator { get; } = new("Simulator");
    public static WeatherSourceIdentity Manual { get; } = new("Manual");

    public WeatherSourceIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
}
