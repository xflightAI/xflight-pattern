using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.Tests;

public sealed class PatternGeometryValidationTests
{
    [Fact]
    public void Distance_RejectsInvalidValuesAndConvertsExplicitNauticalMiles()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, -1d })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HorizontalDistance.FromMetres(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => HorizontalDistance.FromNauticalMiles(invalid));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => HorizontalDistance.FromNauticalMiles(double.MaxValue));
        Assert.Equal(1_852d, HorizontalDistance.FromNauticalMiles(1d).Metres);
        Assert.Equal(HorizontalDistance.FromMetres(1_852d), HorizontalDistance.FromNauticalMiles(1d));
    }

    [Fact]
    public void Parameters_RequireAllSevenExplicitDimensions()
    {
        for (var missing = 0; missing < 7; missing++)
        {
            var dimensions = new HorizontalDistance?[] { M(800), M(500), M(600), M(1_000), M(1_500), M(700), M(50) };
            dimensions[missing] = null;
            Assert.Throws<ArgumentNullException>(() => new PatternGeometryParameters(
                dimensions[0]!, dimensions[1]!, dimensions[2]!, dimensions[3]!, dimensions[4]!, dimensions[5]!, dimensions[6]!));
        }
    }

    [Fact]
    public void Parameters_RejectEmptyOrReversedWindowAndInsufficientFinal()
    {
        Assert.Throws<ArgumentException>(() => Parameters(nearer: 1_000, farther: 1_000));
        Assert.Throws<ArgumentException>(() => Parameters(nearer: 1_100, farther: 1_000));
        Assert.Throws<ArgumentException>(() => Parameters(final: 999));
        Assert.Equal(1_000d, Parameters(final: 1_000).FinalExtension.Metres);
    }

    [Fact]
    public void Parameters_KeepDownwindOnItsSideAndEntryOutsideCorridor()
    {
        Assert.Throws<ArgumentException>(() => Parameters(offset: 50, halfWidth: 50));
        Assert.Throws<ArgumentException>(() => Parameters(offset: 49, halfWidth: 50));
        Assert.Throws<ArgumentException>(() => Parameters(entry: 50, halfWidth: 50));
        Assert.Throws<ArgumentException>(() => Parameters(entry: 50 * Math.Sqrt(2d), halfWidth: 50));
    }

    [Fact]
    public void Primitives_RejectDegenerateNonfiniteAndUnrepresentableGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HorizontalSegment(new(0, 0), new(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HorizontalSegment(new(-double.MaxValue, 0), new(double.MaxValue, 0)));
        Assert.Throws<ArgumentException>(() => new HorizontalArea(1, 1, 0, 1));
        Assert.Throws<ArgumentException>(() => new HorizontalArea(2, 1, 0, 1));
        Assert.Throws<ArgumentException>(() => new HorizontalArea(0, 1, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HorizontalArea(0, double.PositiveInfinity, 0, 1));
        Assert.Throws<ArgumentException>(() => new HorizontalArea(-double.MaxValue, double.MaxValue, 0, 1));
        Assert.Throws<ArgumentException>(() => new LegCorridor(new(new(0, 1e20), new(100, 1e20)), M(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LegCorridor(new(new(0, double.MaxValue), new(100, double.MaxValue)), M(double.MaxValue)));
    }

    internal static PatternGeometryParameters Parameters(double offset = 800, double upwind = 500,
        double nearer = 600, double farther = 1_000, double final = 1_500, double entry = 700, double halfWidth = 50) =>
        new(M(offset), M(upwind), M(nearer), M(farther), M(final), M(entry), M(halfWidth));

    private static HorizontalDistance M(double metres) => HorizontalDistance.FromMetres(metres);
}
