using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;
using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.App.Controls;

public enum FlightStripVariant { Compact, UltraMinimal }

/// <summary>
/// Passive presentation of caller-supplied domain values. No acquisition, phase detection,
/// altitude resolution, or mockup data. Missing inputs remain unavailable in either layout.
/// </summary>
public partial class FlightStrip : UserControl, INotifyPropertyChanged
{
    private const string Unavailable = "—";

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(FlightStripVariant), typeof(FlightStrip),
        new PropertyMetadata(FlightStripVariant.Compact, OnInputChanged),
        value => value is FlightStripVariant variant && Enum.IsDefined(variant));

    public static readonly DependencyProperty RunwayProperty = DependencyProperty.Register(
        nameof(Runway), typeof(RunwayEnd), typeof(FlightStrip), new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty PatternSideProperty = DependencyProperty.Register(
        nameof(PatternSide), typeof(TrafficPatternSideResolution), typeof(FlightStrip), new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty PhaseStateProperty = DependencyProperty.Register(
        nameof(PhaseState), typeof(PhaseDetectionState), typeof(FlightStrip), new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty PatternAltitudeProperty = DependencyProperty.Register(
        nameof(PatternAltitude), typeof(PatternAltitudeResolution), typeof(FlightStrip), new PropertyMetadata(null, OnInputChanged));

    public FlightStrip() => InitializeComponent();

    public FlightStripVariant Variant
    {
        get => (FlightStripVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public RunwayEnd? Runway
    {
        get => (RunwayEnd?)GetValue(RunwayProperty);
        set => SetValue(RunwayProperty, value);
    }

    public TrafficPatternSideResolution? PatternSide
    {
        get => (TrafficPatternSideResolution?)GetValue(PatternSideProperty);
        set => SetValue(PatternSideProperty, value);
    }

    public PhaseDetectionState? PhaseState
    {
        get => (PhaseDetectionState?)GetValue(PhaseStateProperty);
        set => SetValue(PhaseStateProperty, value);
    }

    public PatternAltitudeResolution? PatternAltitude
    {
        get => (PatternAltitudeResolution?)GetValue(PatternAltitudeProperty);
        set => SetValue(PatternAltitudeProperty, value);
    }

    public string RunwayText => Runway?.Identifier ?? Unavailable;
    public string PatternText => PatternSide?.Side switch
    {
        TrafficPatternSide.Left => "LEFT",
        TrafficPatternSide.Right => "RIGHT",
        _ => Unavailable
    };

    // Candidate evidence is deliberately never presented as a confirmed phase.
    public string PhaseText => PhaseState?.ConfirmedPhase is { } phase && phase != PatternPhase.Unknown
        ? phase.ToString().ToUpperInvariant()
        : Unavailable;

    public string PatternAltitudeText => PatternAltitude?.TargetMsl is { } altitude
        ? altitude.Feet.ToString("N0", CultureInfo.CurrentCulture) + " ft MSL"
        : Unavailable;

    public string AccessibleSummary => Variant == FlightStripVariant.Compact
        ? $"Flight Strip Compact. RWY {Describe(RunwayText)}. PATTERN {Describe(PatternText)}. PHASE {Describe(PhaseText)}. PATTERN ALT {Describe(PatternAltitudeText)}."
        : $"Flight Strip Ultra Minimal. RWY {Describe(RunwayText)}. PHASE {Describe(PhaseText)}.";

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string Describe(string value) => value == Unavailable ? "Non disponibile" : value;

    private static void OnInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var strip = (FlightStrip)sender;
        foreach (var property in new[] { nameof(RunwayText), nameof(PatternText), nameof(PhaseText), nameof(PatternAltitudeText), nameof(AccessibleSummary) })
            strip.PropertyChanged?.Invoke(strip, new PropertyChangedEventArgs(property));
    }
}
