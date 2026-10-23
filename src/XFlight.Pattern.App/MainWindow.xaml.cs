using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using XFlight.Pattern.App.Controls;

namespace XFlight.Pattern.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ContentRendered += ShowFlightStrips;
    }

    private void ShowFlightStrips(object? sender, EventArgs args)
    {
        ContentRendered -= ShowFlightStrips;

        // Separate passive surfaces preserve the A3 panel and diagram space at minimum size.
        // No data source is attached: every field starts unavailable.
        var placedStrips = new List<Rect>();
        ShowStrip(FlightStripVariant.Compact, placedStrips);
        ShowStrip(FlightStripVariant.UltraMinimal, placedStrips);
    }

    private void ShowStrip(FlightStripVariant variant, List<Rect> placedStrips)
    {
        var window = new Window
        {
            Title = variant == FlightStripVariant.Compact
                ? "XFlight Pattern — Flight Strip Compact"
                : "XFlight Pattern — Flight Strip Ultra Minimal",
            Owner = this,
            Content = new FlightStrip { Variant = variant },
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            // Load on the owner's monitor before measuring and converting screen bounds to DIPs.
            Left = this.Left,
            Top = this.Top
        };
        window.PreviewMouseLeftButtonDown += DragFlightStrip;
        window.Loaded += PositionOnFirstLoad;
        window.Show();

        void PositionOnFirstLoad(object sender, RoutedEventArgs args)
        {
            window.Loaded -= PositionOnFirstLoad;
            var (workArea, shellBounds) = GetPlacementBounds(window);
            var bounds = FindInitialStripBounds(workArea, shellBounds,
                new Size(window.ActualWidth, window.ActualHeight), placedStrips);
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            placedStrips.Add(bounds);
        }
    }

    private static void DragFlightStrip(object sender, MouseButtonEventArgs args)
    {
        if (sender is Window window && args.ChangedButton == MouseButton.Left &&
            args.LeftButton == MouseButtonState.Pressed)
        {
            args.Handled = true;
            window.DragMove();
        }
    }

    private static Rect FindInitialStripBounds(Rect workArea, Rect shell, Size size, List<Rect> placedStrips)
    {
        const double gap = 12;
        var right = Math.Max(workArea.Left, workArea.Right - size.Width);
        var bottom = Math.Max(workArea.Top, workArea.Bottom - size.Height);
        var xs = new List<double>
        {
            shell.Right + gap, shell.Left - size.Width - gap,
            Math.Clamp(right - gap, workArea.Left, right), workArea.Left, right,
            shell.Right, shell.Left - size.Width
        };
        var ys = new List<double>
        {
            Math.Clamp(shell.Top + 80, workArea.Top, bottom),
            shell.Top - size.Height - gap, shell.Bottom + gap,
            workArea.Top, bottom, shell.Top - size.Height, shell.Bottom
        };
        foreach (var strip in placedStrips)
        {
            xs.Add(strip.Right + gap);
            xs.Add(strip.Left - size.Width - gap);
            ys.Add(strip.Bottom + gap);
            ys.Add(strip.Top - size.Height - gap);
        }

        Rect? fallback = null;
        foreach (var x in xs)
        foreach (var y in ys)
        {
            var candidate = new Rect(x, y, size.Width, size.Height);
            if (!workArea.Contains(candidate) || placedStrips.Any(strip => Overlaps(candidate, strip)))
                continue;
            fallback ??= candidate;
            if (!Overlaps(candidate, shell))
                return candidate;
        }

        // A maximized shell may leave no free area. Keep the strip visible; overlap is then possible.
        // This placement runs once and never overrides a later manual drag.
        return fallback ?? new Rect(right, workArea.Top, size.Width, size.Height);
    }

    private static bool Overlaps(Rect first, Rect second) =>
        first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;

    private (Rect WorkArea, Rect Shell) GetPlacementBounds(Window strip)
    {
        var ownerHandle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(ownerHandle, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info) && GetWindowRect(ownerHandle, out var shell))
        {
            var fromDevice = new MatrixTransform(
                PresentationSource.FromVisual(strip)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity);
            return (fromDevice.TransformBounds(info.WorkArea.ToRect()),
                fromDevice.TransformBounds(shell.ToRect()));
        }

        var workArea = SystemParameters.WorkArea;
        return (workArea, WindowState == WindowState.Maximized
            ? workArea : new Rect(Left, Top, ActualWidth, ActualHeight));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
}

