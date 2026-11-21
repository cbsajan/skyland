using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

// PC stats: the idle pill, the row at the bottom of the main card, the System card gauges, the optional
// performance bubble, and the short alert under the capsule for heavy load or a low battery.
public partial class MainWindow
{
    // CPU / memory / GPU at or above this count as heavily loaded. SKYLAND_HOT_LEVEL is a test seam only.
    private static readonly double HotLevel =
        double.TryParse(Environment.GetEnvironmentVariable("SKYLAND_HOT_LEVEL"), out var level) && level is > 0 and <= 100 ? level : 85;
    private const double LowBatteryLevel = 20;   // battery below this (and not charging) is low
    private const int HotSamplesNeeded = 2;      // heavy load must last two readings in a row, so a spike never alerts
    private static readonly TimeSpan AlertSilence = TimeSpan.FromMinutes(2);   // after an alert, that kind stays quiet
    private static readonly TimeSpan AlertShown = TimeSpan.FromSeconds(5);

    private readonly SystemStatsService _stats = new();
    private readonly DispatcherTimer _statsTimer = new();
    private readonly DispatcherTimer _alertTimer = new() { Interval = AlertShown };
    private bool _sampling;
    private bool _numbersVisible;       // the idle pill or a card is showing the numbers: sample often
    private bool _rowVisibleInCard;
    private bool _perfShown;
    private PerfBubbleMode _perfBuilt = PerfBubbleMode.Off;
    private SystemStats? _lastStats;
    private int _gpuMisses;
    private int _hotSamples;
    private DateTime _loadAlertAt = DateTime.MinValue;
    private DateTime _batteryAlertAt = DateTime.MinValue;
    private (string Title, string Detail)? _alert;
    private bool _alertVisible;

    private static readonly Brush GaugeBlue = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)));
    private static readonly Brush GaugeYellow = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)));
    private static readonly Brush GaugeGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)));
    private static readonly Brush GaugeBattery = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));

    private void InitStats()
    {
        _statsTimer.Tick += async (_, _) => await UpdateStatsAsync();
        _alertTimer.Tick += (_, _) =>
        {
            _alertTimer.Stop();
            _alert = null;
            UpdateAlertStrip();
        };
    }

    private void StartStats()
    {
        _stats.Baseline();
        _statsTimer.Interval = TimeSpan.FromMilliseconds(600); // first reading soon, then every 2 s (5 s when nobody looks)
        _statsTimer.Start();
    }

    // Looked at: every 2 s. Otherwise just often enough to catch heavy load and a low battery.
    private TimeSpan StatsPace() => _numbersVisible || _perfShown ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5);

    private void UpdateStatsPace()
    {
        if (_lastStats is not null) _statsTimer.Interval = StatsPace();
    }

    private async Task UpdateStatsAsync()
    {
        _statsTimer.Interval = StatsPace();
        if (_sampling) return;
        _sampling = true;

        try
        {
            // Reading the GPU counters takes a few ms; keep it off the UI thread.
            var s = await Task.Run(_stats.Sample);
            _lastStats = s;
            ShowStats(s);
            EvaluateAlerts(s);
        }
        finally
        {
            _sampling = false;
        }
    }

    // High load turns the value red so a glance is enough.
    private static void SetValue(TextBlock text, double? value)
    {
        if (value is null) { text.Text = "--"; text.Foreground = NormalBrush; return; }
        text.Text = $"{value:0}%";
        text.Foreground = value >= HotLevel ? WarnBrush : NormalBrush;
    }

    /// <summary>Formats bytes/s as a compact human-readable speed string (e.g. "1.2 KB/s").</summary>
    private static string FormatSpeed(long bytesPerSec) => bytesPerSec switch
    {
        < 1024              => $"{bytesPerSec} B/s",
        < 1024 * 1024       => bytesPerSec < 1024 * 10
                                ? $"{bytesPerSec / 1024.0:0.#} KB/s"
                                : $"{bytesPerSec / 1024.0:0} KB/s",
        < 1024 * 1024 * 100 => $"{bytesPerSec / (1024.0 * 1024):0.#} MB/s",
        _                   => $"{bytesPerSec / (1024.0 * 1024):0} MB/s"
    };

    private void ShowStats(SystemStats s)
    {
        SetValue(CpuValue, s.Cpu);
        SetValue(RamValue, s.Ram);

        // The GPU counters occasionally return nothing for a single sample. Keep showing the last value
        // through those gaps, and hide the item only if several samples in a row have nothing.
        if (s.Gpu is null)
        {
            if (++_gpuMisses >= 5) GpuItem.Visibility = Visibility.Collapsed;
        }
        else
        {
            _gpuMisses = 0;
            SetValue(GpuValue, s.Gpu);
            GpuItem.Visibility = Visibility.Visible;
        }

        BatteryItem.Visibility = s.BatteryPercent is null ? Visibility.Collapsed : Visibility.Visible;
        if (s.BatteryPercent is int pct)
        {
            BoltIcon.Visibility = s.PluggedIn ? Visibility.Visible : Visibility.Collapsed;
            BatteryValue.Text = $"{pct}%";
            BatteryValue.Foreground = pct < LowBatteryLevel && !s.PluggedIn ? WarnBrush : NormalBrush;
        }

        // Network speed in the bottom row of the pill
        bool hasNet = s.DownloadBytesPerSec is not null;
        DownloadItem.Visibility = hasNet ? Visibility.Visible : Visibility.Collapsed;
        UploadItem.Visibility = hasNet ? Visibility.Visible : Visibility.Collapsed;
        if (s.DownloadBytesPerSec is long down) DownloadValue.Text = FormatSpeed(down);
        if (s.UploadBytesPerSec is long up) UploadValue.Text = FormatSpeed(up);

        if (_state == IslandState.Card && _cardKind is null) ShowGauges(s);
        if (_perfShown) ShowPerf(s);
    }

    // ----- System card: four ring gauges ---------------------------------

    private readonly List<(Ellipse Arc, TextBlock Value, Brush Normal)> _gauges = new();

    private void BuildGauges()
    {
        var items = new (string Label, Brush Color)[]
        {
            ("CPU", GaugeBlue), ("RAM", GaugeYellow), ("GPU", GaugeGreen), ("BATTERY", GaugeBattery)
        };

        foreach (var (label, color) in items)
        {
            var arc = new Ellipse
            {
                Stroke = color,
                StrokeThickness = 5,
                StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = Dash(54, 5, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-90)
            };
            var value = new TextBlock
            {
                Text = "--",
                Foreground = NormalBrush,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var ring = new Grid { Width = 54, Height = 54 };
            ring.Children.Add(new Ellipse { Stroke = TrackBrush, StrokeThickness = 5 });
            ring.Children.Add(arc);
            ring.Children.Add(value);

            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cell.Children.Add(ring);
            cell.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = DimBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            });
            SystemGauges.Children.Add(cell);
            _gauges.Add((arc, value, color));
        }
    }

    private void ShowGauges(SystemStats s)
    {
        double?[] values = [s.Cpu, s.Ram, s.Gpu, s.BatteryPercent];
        for (int i = 0; i < _gauges.Count; i++)
        {
            var (arc, text, normal) = _gauges[i];
            double? v = values[i];
            bool hot = i == 3 ? v < LowBatteryLevel && !s.PluggedIn : v >= HotLevel;

            arc.StrokeDashArray = Dash(54, 5, (v ?? 0) / 100);
            arc.Stroke = hot ? WarnBrush : normal;
            text.Text = v is null ? "--" : $"{v:0}";
            text.Foreground = hot ? WarnBrush : NormalBrush;
        }

        PowerChipText.Text = s.BatteryPercent is null ? "" : s.PluggedIn ? "Plugged in" : "On battery";
        PowerChip.Visibility = s.BatteryPercent is null ? Visibility.Collapsed : Visibility.Visible;

        // Network speed row under the gauges
        NetDownValue.Text = s.DownloadBytesPerSec is long d ? FormatSpeed(d) : "--";
        NetUpValue.Text = s.UploadBytesPerSec is long u ? FormatSpeed(u) : "--";
    }

    // ----- Performance bubble (optional) ---------------------------------

    private readonly List<(Ellipse Arc, double Outer, double Thickness)> _perfRings = new();
    private TextBlock? _perfCpuText;

    private void ShowPerfBubble(bool shown)
    {
        _perfShown = shown;

        // Hiding keeps the rings until the shrink animation ends, so the bubble does not flash empty.
        if (shown)
        {
            var mode = SettingsService.Current.PerfBubble;
            if (mode != _perfBuilt) BuildPerf(mode);
            if (_lastStats is not null) ShowPerf(_lastStats);
        }

        SetBubble(PerfBubble, PerfBubbleScale, shown);
    }

    // Rings: four concentric rings, CPU on the outside. Cpu: one thick ring with the percentage inside.
    private void BuildPerf(PerfBubbleMode mode)
    {
        PerfBubbleHost.Children.Clear();
        _perfRings.Clear();
        _perfCpuText = null;
        _perfBuilt = mode;

        double[] outers = mode == PerfBubbleMode.Rings ? [42, 33.6, 25.2, 16.8] : [40];
        double thickness = mode == PerfBubbleMode.Rings ? 2.6 : 4;
        foreach (double outer in outers)
        {
            var arc = new Ellipse
            {
                Width = outer,
                Height = outer,
                StrokeThickness = thickness,
                StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = Dash(outer, thickness, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-90)
            };
            PerfBubbleHost.Children.Add(new Ellipse { Width = outer, Height = outer, Stroke = TrackBrush, StrokeThickness = thickness });
            PerfBubbleHost.Children.Add(arc);
            _perfRings.Add((arc, outer, thickness));
        }

        if (mode == PerfBubbleMode.Cpu)
        {
            _perfCpuText = new TextBlock
            {
                Foreground = NormalBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(_perfCpuText, FontNumeralAlignment.Tabular);
            PerfBubbleHost.Children.Add(_perfCpuText);
        }
    }

    private void ShowPerf(SystemStats s)
    {
        double?[] values = [s.Cpu, s.Ram, s.Gpu, s.BatteryPercent];
        Brush[] normal = [GaugeBlue, GaugeYellow, GaugeGreen, GaugeBattery];

        for (int i = 0; i < _perfRings.Count; i++)
        {
            var (arc, outer, thickness) = _perfRings[i];
            double? v = values[i];
            bool hot = i == 3 ? v < LowBatteryLevel && !s.PluggedIn : v >= HotLevel;

            arc.StrokeDashArray = Dash(outer, thickness, (v ?? 0) / 100);
            arc.Stroke = hot ? WarnBrush : normal[i];
        }

        if (_perfCpuText is not null)
        {
            _perfCpuText.Text = s.Cpu is null ? "--" : $"{s.Cpu:0}";
            _perfCpuText.Foreground = s.Cpu >= HotLevel ? WarnBrush : NormalBrush;
        }
    }

    // ----- Alerts: heavy load, low battery -------------------------------

    // Each kind of alert shows once for a few seconds, then stays quiet for two minutes.
    private void EvaluateAlerts(SystemStats s)
    {
        if (!SettingsService.Current.ShowLoadAlerts) { _hotSamples = 0; return; }

        var loads = new (string Name, double? Value)[] { ("CPU", s.Cpu), ("Memory", s.Ram), ("GPU", s.Gpu) };
        var worst = loads.Where(l => l.Value >= HotLevel).OrderByDescending(l => l.Value).FirstOrDefault();
        _hotSamples = worst.Value is null ? 0 : _hotSamples + 1;

        var now = DateTime.UtcNow;
        if (_hotSamples >= HotSamplesNeeded && now - _loadAlertAt >= AlertSilence)
        {
            _loadAlertAt = now;
            ShowAlert($"High {worst.Name} load {worst.Value:0}%",
                      $"CPU {s.Cpu:0}% · RAM {s.Ram:0}%" + (s.Gpu is { } g ? $" · GPU {g:0}%" : ""));
        }
        else if (s.BatteryPercent is int pct && pct < LowBatteryLevel && !s.PluggedIn && now - _batteryAlertAt >= AlertSilence)
        {
            _batteryAlertAt = now;
            ShowAlert($"Battery low {pct}%", "Plug in the charger");
        }
    }

    private void ShowAlert(string title, string detail)
    {
        _alert = (title, detail);
        _alertTimer.Stop();
        _alertTimer.Start();
        UpdateAlertStrip();
    }

    // The strip sits under the capsule. It waits while a card, pop-up or the intro is open, and shows when they close.
    private void UpdateAlertStrip()
    {
        bool want = _alert is not null && _state is IslandState.Idle or IslandState.Capsule;

        if (want && _alert is { } a)
        {
            AlertTitle.Text = a.Title;
            AlertDetail.Text = a.Detail;
            if (_alertVisible) return;

            _alertVisible = true;
            AlertStrip.Visibility = Visibility.Visible;
            AlertStrip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
            AlertShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
            });
        }
        else if (_alertVisible)
        {
            _alertVisible = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) =>
            {
                if (!_alertVisible) AlertStrip.Visibility = Visibility.Collapsed;
            };
            AlertStrip.BeginAnimation(OpacityProperty, fade);
            AlertShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, TimeSpan.FromMilliseconds(180)));
        }
    }
}
