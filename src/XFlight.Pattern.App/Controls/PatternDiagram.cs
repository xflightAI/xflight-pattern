using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using XFlight.Pattern.Core.Circuits;

namespace XFlight.Pattern.App.Controls;

/// <summary>
/// Presentation-only illustration in drawing units, not geographic coordinates or M3 geometry.
/// The caller supplies the side; this control neither resolves circuit authority nor selects a turn.
/// </summary>
public sealed class PatternDiagram : FrameworkElement
{
    private const double DrawingWidth = 620;
    private const double DrawingHeight = 650;
    private const double AxisX = DrawingWidth / 2;

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(TrafficPatternSide), typeof(PatternDiagram),
        new FrameworkPropertyMetadata(TrafficPatternSide.Unknown,
            FrameworkPropertyMetadataOptions.AffectsRender, OnSideChanged),
        value => value is TrafficPatternSide side && Enum.IsDefined(side));

    public PatternDiagram()
    {
        ClipToBounds = true;
        UpdateAccessibleName();
    }

    public TrafficPatternSide Side
    {
        get => (TrafficPatternSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private static void OnSideChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PatternDiagram)sender).UpdateAccessibleName();

    private void UpdateAccessibleName() => AutomationProperties.SetName(this, Side switch
    {
        TrafficPatternSide.Left => "Schema statico: circuito sinistro, ingresso a 45 gradi, Base Turn Window. Nessun dato live.",
        TrafficPatternSide.Right => "Schema statico: circuito destro, ingresso a 45 gradi, Base Turn Window. Nessun dato live.",
        _ => "Schema statico: lato non definito, nessuna geometria direzionale disponibile."
    });

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var scale = Math.Min(ActualWidth / DrawingWidth, ActualHeight / DrawingHeight);
        drawing.PushTransform(new TranslateTransform(
            (ActualWidth - DrawingWidth * scale) / 2,
            (ActualHeight - DrawingHeight * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));

        var primary = Brush("PrimaryTextBrush", "#D8EAF7");
        var secondary = Brush("SecondaryTextBrush", "#9DC9E2");
        var muted = Brush("MutedTextBrush", "#789AAF");
        var turquoise = Brush("TurquoiseBrush", "#16CBE6");
        var amber = Brush("AmberBrush", "#FFBD35");
        var edge = Brush("PanelEdgeBrush", "#075676");

        Label("SCHEMA DEL CIRCUITO", AxisX, 22, 22, primary, true);
        Label(Side switch
        {
            TrafficPatternSide.Left => "LEFT · CIRCUITO SINISTRO",
            TrafficPatternSide.Right => "RIGHT · CIRCUITO DESTRO",
            _ => "UNKNOWN · LATO NON DEFINITO"
        }, AxisX, 55, 16, Side == TrafficPatternSide.Unknown ? muted : turquoise);

        // Neutral, unnumbered runway. No airport identity, heading or aircraft position is invented.
        drawing.DrawRoundedRectangle(Brush("HeaderBrush", "#041B2C"), new Pen(secondary, 1.5),
            new Rect(AxisX - 17, 240, 34, 200), 3, 3);
        drawing.DrawLine(new Pen(muted, 1) { DashStyle = DashStyles.Dash },
            new Point(AxisX, 258), new Point(AxisX, 422));
        foreach (var y in new[] { 246d, 430d })
            for (var stripe = 0; stripe < 4; stripe++)
                drawing.DrawRectangle(primary, null, new Rect(AxisX - 12 + stripe * 7, y, 3, 7));

        Label("Testata opposta", Side == TrafficPatternSide.Right ? DrawingWidth - 430 : 430,
            246, 15, secondary);
        Label("Testata di atterraggio", Side == TrafficPatternSide.Right ? DrawingWidth - 439 : 439,
            433, 15, primary);

        if (Side == TrafficPatternSide.Unknown)
        {
            Label("Lato non definito", AxisX, 490, 21, primary, true);
            Label("Nessun percorso direzionale attribuito", AxisX, 522, 16, muted);
        }
        else
        {
            // One canonical LEFT drawing. Reflect every route primitive, arrow and window together.
            // The text anchors are reflected separately so glyphs are never mirrored.
            var mirror = Side == TrafficPatternSide.Right;
            var route = new Pen(turquoise, 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var entry = new Pen(amber, 2.3) { DashStyle = DashStyles.Dash };
            drawing.PushTransform(new ScaleTransform(mirror ? -1 : 1, 1, AxisX, 0));

            // Longitudinal screen direction: bottom to top. Landing threshold y=440, midpoint y=340.
            Segment(new Point(310, 440), new Point(310, 160), route);
            Segment(new Point(310, 160), new Point(165, 160), route);
            Segment(new Point(165, 160), new Point(165, 540), route);

            // This band represents a range before the landing threshold, not a selected turn point.
            var windowFill = amber.Clone();
            windowFill.Opacity = 0.16;
            drawing.DrawRectangle(windowFill, new Pen(amber, 1.3), new Rect(147, 500, 36, 40));
            var baseRange = new Pen(turquoise, 1.8) { DashStyle = DashStyles.Dash };
            Segment(new Point(183, 500), new Point(310, 500), baseRange);
            Segment(new Point(183, 540), new Point(310, 540), baseRange);
            Arrow(new Point(242, 520), new Vector(1, 0), turquoise);
            Segment(new Point(310, 540), new Point(310, 440), route);

            // Fixed 45-degree artwork joins downwind abeam the physical runway midpoint, from outside.
            Segment(new Point(70, 245), new Point(165, 340), entry);
            Segment(new Point(165, 340), new Point(293, 340),
                new Pen(edge, 1) { DashStyle = DashStyles.Dot });
            drawing.DrawEllipse(amber, null, new Point(165, 340), 3, 3);
            Arrow(new Point(310, 195), new Vector(0, -1), turquoise);
            Arrow(new Point(232, 160), new Vector(-1, 0), turquoise);
            Arrow(new Point(165, 405), new Vector(0, 1), turquoise);
            Arrow(new Point(310, 479), new Vector(0, -1), turquoise);
            Arrow(new Point(121, 296), new Vector(1, 1), amber);
            Segment(new Point(165, 540), new Point(165, 566), new Pen(amber, 1));
            drawing.Pop();

            double X(double leftX) => mirror ? DrawingWidth - leftX : leftX;
            Label("CROSSWIND", X(236), 130, 16, turquoise, true);
            Label("DEPARTURE ↑", AxisX, 102, 16, turquoise, true);
            Label("DOWNWIND", X(95), 397, 16, turquoise, true);
            Label("BASE", X(237), 473, 16, turquoise, true);
            Label("FINAL", X(365), 503, 16, turquoise, true);
            Label("INGRESSO 45°", X(102), 217, 16, amber, true);
            Label("abeam metà pista", X(225), 350, 13, muted);
            Label("BASE TURN WINDOW", X(165), 574, 15, amber, true);
            Label("fascia · nessun punto di virata scelto", AxisX, 600, 13, muted);
        }

        Label("SCHEMA STATICO · NON IN SCALA · NESSUN DATO LIVE", AxisX, 626, 13, muted);
        drawing.Pop();
        drawing.Pop();

        void Segment(Point start, Point end, Pen pen) => drawing.DrawLine(pen, start, end);

        void Arrow(Point tip, Vector direction, Brush fill)
        {
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(tip, true, true);
                context.LineTo(tip - direction * 10 + normal * 4.5, true, false);
                context.LineTo(tip - direction * 10 - normal * 4.5, true, false);
            }
            drawing.DrawGeometry(fill, null, geometry);
        }

        void Label(string text, double x, double y, double fontSize, Brush foreground, bool bold = false)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                    bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                fontSize, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(formatted, new Point(x - formatted.Width / 2, y));
        }
    }

    private Brush Brush(string key, string fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
}
