using XFlight.Pattern.Core.Circuits;

namespace XFlight.Pattern.Core.Tests;

public sealed class PatternAltitudeTests
{
    [Fact]
    public void Resolve_C172TrainingDefault_AddsAglHeightToAirportElevation()
    {
        var result = PatternAltitudeResolver.Resolve(
            airportElevationMsl: new AirportElevationMsl(850d),
            useC172TrainingDefault: true);

        Assert.Equal(PatternAltitudeProvenance.TrainingDefault, result.Provenance);
        Assert.Equal(850d, result.AirportElevationMsl!.Feet);
        Assert.Equal(1_000d, result.OriginatingHeightAgl!.Feet);
        Assert.Equal(1_850d, result.TargetMsl!.Feet);
    }

    [Fact]
    public void Resolve_PublishedMsl_RemainsDirectPublishedTarget()
    {
        var result = PatternAltitudeResolver.Resolve(
            published: new PatternAltitudeMsl(1_700d),
            airportElevationMsl: new AirportElevationMsl(850d),
            useC172TrainingDefault: true);

        Assert.Equal(PatternAltitudeProvenance.Published, result.Provenance);
        Assert.Equal(1_700d, result.TargetMsl!.Feet);
        Assert.Null(result.AirportElevationMsl);
        Assert.Null(result.OriginatingHeightAgl);
    }

    [Fact]
    public void Resolve_VerifiedMsl_RetainsVerifiedProvenance()
    {
        var result = PatternAltitudeResolver.Resolve(xFlightVerified: new PatternAltitudeMsl(2_100d));

        Assert.Equal(PatternAltitudeProvenance.XFlightVerified, result.Provenance);
        Assert.Equal(2_100d, result.TargetMsl!.Feet);
    }

    [Fact]
    public void Resolve_UserSelectedAglConfiguration_RetainsSourceSemantics()
    {
        var selected = PatternAltitudeResolution.UserSelected(
            new AirportElevationMsl(500d),
            new PatternHeightAgl(800d));

        var result = PatternAltitudeResolver.Resolve(userSelected: selected);

        Assert.Equal(PatternAltitudeProvenance.UserSelected, result.Provenance);
        Assert.Equal(500d, result.AirportElevationMsl!.Feet);
        Assert.Equal(800d, result.OriginatingHeightAgl!.Feet);
        Assert.Equal(1_300d, result.TargetMsl!.Feet);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AltitudeValueTypes_NonFiniteInputs_AreRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirportElevationMsl(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternHeightAgl(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternAltitudeMsl(value));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void PatternHeight_NonPositiveInput_IsRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternHeightAgl(value));
    }

    [Fact]
    public void Resolve_RequestedTrainingDefaultWithoutElevation_RemainsUnresolved()
    {
        var result = PatternAltitudeResolver.Resolve(useC172TrainingDefault: true);

        Assert.False(result.IsResolved);
        Assert.Equal(PatternAltitudeProvenance.Unresolved, result.Provenance);
        Assert.Null(result.TargetMsl);
    }

    [Fact]
    public void Resolve_NonUserCandidateInUserSlot_IsRejected()
    {
        var published = PatternAltitudeResolution.Published(new PatternAltitudeMsl(1_850d));

        Assert.Throws<ArgumentException>(() => PatternAltitudeResolver.Resolve(userSelected: published));
    }

    [Fact]
    public void Resolve_UsesPublishedThenVerifiedThenUserThenTrainingDefaultPrecedence()
    {
        var user = PatternAltitudeResolution.UserSelected(new PatternAltitudeMsl(1_900d));
        var allCandidates = PatternAltitudeResolver.Resolve(
            published: new PatternAltitudeMsl(1_600d),
            xFlightVerified: new PatternAltitudeMsl(1_700d),
            userSelected: user,
            airportElevationMsl: new AirportElevationMsl(800d),
            useC172TrainingDefault: true);
        var withoutPublished = PatternAltitudeResolver.Resolve(
            xFlightVerified: new PatternAltitudeMsl(1_700d),
            userSelected: user,
            airportElevationMsl: new AirportElevationMsl(800d),
            useC172TrainingDefault: true);

        Assert.Equal(PatternAltitudeProvenance.Published, allCandidates.Provenance);
        Assert.Equal(1_600d, allCandidates.TargetMsl!.Feet);
        Assert.Equal(PatternAltitudeProvenance.XFlightVerified, withoutPublished.Provenance);
        Assert.Equal(1_700d, withoutPublished.TargetMsl!.Feet);
    }

    [Fact]
    public void Resolve_RepeatedInput_ProducesEqualResolution()
    {
        PatternAltitudeResolution Resolve() => PatternAltitudeResolver.Resolve(
            airportElevationMsl: new AirportElevationMsl(850d),
            useC172TrainingDefault: true);

        Assert.Equal(Resolve(), Resolve());
    }
}
