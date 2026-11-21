using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.Native;
using DynamicIsland.Services;

namespace DynamicIsland;

// The island is a main capsule with an optional small round bubble beside it (like the iOS Dynamic Island):
//   main capsule  = the most important thing that is on (Claude > Focus > Music), or the PC stats when idle
//   bubble        = the second most important thing
//   hover capsule = its card; hover the bubble = that activity's card (the main one shrinks into the bubble)
//   alert strip   = a short line under the capsule for heavy PC load / low battery
public partial class MainWindow : Window
{
    private enum IslandState { Idle, Capsule, Card, Privacy, Notice, Update, Intro }
    private enum Kind { Claude, Focus, Music }

    // Sizes. The idle pill shows only the PC stats. Capsules are 44 px high; cards are 340 px wide.
    // Pop-ups (charging, update prompt, intro) share one standard size so they never jump around.
    private static readonly (double W, double H, double R) IdleSize = (360, 32, 16);
    private static readonly (double W, double H, double R) StandardSize = (270, 60, 28);
    private const double CapsuleHeight = 44, CapsuleRadius = 22;
    private const double CardWidth = 360, CardRadius = 38;
    private const double StatsBlock = 44;      // room under a card for the PC row
    private const double CardBottomPad = 16;   // bottom padding of a card that has no PC row

    private static double CapsuleWidth(Kind kind, double claudeWidth) => kind switch
    {
        Kind.Claude => claudeWidth,
        Kind.Focus => 240,
        _ => 252
    };

    private static readonly Brush FocusOrange = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x3D)));
    private static readonly Brush TimerBlue = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)));
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)));
    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)));
    private static readonly Brush WarnAmber = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x40)));
    private static readonly Brush ChargingGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));
    private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x7A)));
    private static readonly Brush PlaceholderArt = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)));
    private const string PlayGlyph = "";    // Segoe Fluent Icons: Play
    private const string PauseGlyph = "";   // Segoe Fluent Icons: Pause

    // Test seam for screenshots only: SKYLAND_HIDE_MEDIA=1 ignores the media player, so no song or artwork shows.
    private static readonly bool HideMedia = Environment.GetEnvironmentVariable("SKYLAND_HIDE_MEDIA") == "1";

    private readonly MediaService _media = new();
    // How long the pointer may be outside the island before a card collapses (the collapse itself is animated).
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _leaveTimer = new() { Interval = CollapseDelay };
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _popTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    // Charging / unplugged pop-up
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _notice;
    private bool _lastPluggedIn = SystemStatsService.ReadBattery().PluggedIn;

    // Microsoft Clock mirror (Focus sessions and timers)
    private readonly ClockSyncService _clock = new();
    private ClockSnapshot? _clockSnap;

    private IslandState _state = IslandState.Idle;
    private (double W, double H, double R) _size = IdleSize;   // size the pill is animating to
    private string _viewKey = "";                              // what is on screen; views are re-animated only when it changes
    private Kind? _main, _side;                                // capsule and bubble, in priority order
    private Kind? _cardKind;                                   // what the open card shows (null = system card)
    private Kind? _bubbleKind;                                 // what the bubble shows right now
    private bool _cardSwapped;                                 // the card shows the bubble's activity (no PC row)
    private MediaSnapshot? _current;
    private bool _hovering;          // expanded by the mouse (hover or click, per settings)
    private bool _hoverSide;         // the pointer is on the bubble, so the card shows the bubble's activity
    private bool _popping;           // expanded briefly because the track changed
    private string? _lastTrackKey;
    private bool _equaliserRunning;
    private bool _mediaPlaying;
    private int _refreshVersion;
    private double _progressRatio;

    public static readonly DependencyProperty PillRadiusProperty = DependencyProperty.Register(
        nameof(PillRadius), typeof(double), typeof(MainWindow),
        new PropertyMetadata(IdleSize.R, (d, e) =>
            ((MainWindow)d).Pill.CornerRadius = new CornerRadius((double)e.NewValue)));

    public double PillRadius
    {
        get => (double)GetValue(PillRadiusProperty);
        set => SetValue(PillRadiusProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();

        // Where the user left it, or centred on the primary monitor flush with the top edge.
        InitPosition();
        ApplyPosition();
        PrepareIntro();

        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            _hovering = false;
            _hoverSide = false;
            ApplyState();
        };
        _progressTimer.Tick += (_, _) =>
        {
            UpdateProgress();
            UpdateAgentTimes();
        };
        _popTimer.Tick += (_, _) =>
        {
            _popTimer.Stop();
            _popping = false;
            ApplyState();
        };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice = false;
            ApplyState();
        };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _clock.Updated += (_, snapshot) => Dispatcher.InvokeAsync(() => OnClockUpdated(snapshot));
        SettingsService.Changed += ApplyClockSetting;
        SettingsService.Changed += OnSettingsChanged;
        InitAgent();
        InitUpdates();
        InitStats();
        InitPrivacy();
        InitDevices();
        InitDock();

        Loaded += async (_, _) =>
        {
            BuildGauges();
            if (_startDocked) EnterDockedAtStart();
            ApplyClockSetting();
            ApplyAgentSetting();
            StartStats();
            ApplyPrivacySetting();
            _ = StartUpdatesAsync();
            _ = StartIntroAsync();
            if (Environment.GetEnvironmentVariable("SKYLAND_POWER_FAKE") is { Length: > 0 } fake)   // test seam: screenshots only
            {
                await Task.Delay(8000);
                ShowPowerNotice(fake == "charging", 76);
            }

            _media.Changed += (_, _) => Dispatcher.InvokeAsync(RefreshAsync);
            await _media.InitializeAsync();
            await RefreshAsync();
            _progressTimer.Start();
            ApplyState();
        };
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeOverlay(handle);
        HookDevices(handle);
    }

    // Settings such as the performance bubble change what is on screen.
    private void OnSettingsChanged()
    {
        ApplyPrivacySetting();
        ApplyDeviceSetting();
        ApplyDockSetting();
        ApplyState();
    }

    // ----- Media refresh -------------------------------------------------

    private async Task RefreshAsync()
    {
        // Events can overlap; only the latest refresh may update the UI.
        int version = ++_refreshVersion;
        var snapshot = HideMedia ? null : await _media.GetSnapshotAsync();
        if (version != _refreshVersion) return;

        _current = snapshot;

        // Pop up only when the track changes while something was already known,
        // not on app start or when playback merely pauses/resumes.
        string? key = snapshot is null ? null : $"{snapshot.Title}|{snapshot.Artist}";
        bool trackChanged = key is not null && _lastTrackKey is not null && key != _lastTrackKey;
        _lastTrackKey = key;
        if (trackChanged && SettingsService.Current.AutoPopOnTrackChange)
        {
            _popping = true;
            _popTimer.Stop();
            _popTimer.Start();
        }

        if (snapshot is not null)
        {
            var art = snapshot.Art is null ? PlaceholderArt : Freeze(new ImageBrush(snapshot.Art) { Stretch = Stretch.UniformToFill });
            ArtSmall.Background = art;
            ArtLarge.Background = art;
            BubbleArt.Fill = art;
            TitleSmall.Text = snapshot.Title;
            ArtistSmall.Text = snapshot.Artist;
            TitleLarge.Text = snapshot.Title;
            ArtistLarge.Text = snapshot.Artist;
            PlayButton.Content = snapshot.IsPlaying ? PauseGlyph : PlayGlyph;
            SetEqualiser(snapshot.IsPlaying);
        }
        else
        {
            SetEqualiser(false);
        }

        ApplyState();
        UpdateProgress();
    }

    // ----- State & animation ---------------------------------------------

    private double CardContentHeight(Kind? kind) => kind switch
    {
        Kind.Claude => 18 + 28 + 8 + ClaudeRowCount() * ClaudeRowHeight,
        Kind.Music => 164,
        Kind.Focus => 106,
        _ => 0
    };

    private (double W, double H, double R) SizeOf(IslandState target, Kind? cardKind, bool statsRow) => target switch
    {
        IslandState.Intro or IslandState.Notice => StandardSize,
        IslandState.Update => (306, CapsuleHeight, CapsuleRadius),
        IslandState.Privacy => (270, CapsuleHeight, CapsuleRadius),
        IslandState.Capsule when _main is Kind main => (CapsuleWidth(main, ClaudeCapsuleWidth()) + CollapsedDotExtraWidth(), CapsuleHeight, CapsuleRadius),
        IslandState.Card when cardKind is null => (CardWidth, 174, CardRadius),
        IslandState.Card => (CardWidth, CardContentHeight(cardKind) + (statsRow ? StatsBlock : CardBottomPad), CardRadius),
        _ => (IdleSize.W + CollapsedDotExtraWidth(), IdleSize.H, IdleSize.R)
    };

    private void ApplyState()
    {
        // Minimized (or on its way there): only the round icon is on screen, so only its ring needs updating.
        if (_docked || _dockBusy)
        {
            UpdateDockStatus();
            return;
        }

        bool hasMedia = _current is not null;
        bool hasClock = _clockSnap is not null;
        bool hasAgent = _agentSessions.Count > 0;
        bool agentActive = _agentSessions.Any(s => s.State != AgentState.Idle);

        // What is on, in priority order: Claude, then Focus, then music. An open but idle Claude session
        // only counts when nothing else is going on, so it never pushes music or Focus aside.
        var activities = new List<Kind>();
        if (agentActive) activities.Add(Kind.Claude);
        if (hasClock) activities.Add(Kind.Focus);
        if (hasMedia) activities.Add(Kind.Music);
        if (activities.Count == 0 && hasAgent) activities.Add(Kind.Claude);
        _main = activities.Count > 0 ? activities[0] : null;
        _side = activities.Count > 1 ? activities[1] : null;

        // The card shows the capsule's activity, or the bubble's when the pointer is on the bubble
        // (or briefly the music when the track changed).
        bool wantCard = _hovering || _popping;
        Kind? cardKind = _main;
        if (_popping && !_hovering && hasMedia) cardKind = Kind.Music;
        else if (_hovering && _hoverSide && _side is not null) cardKind = _side;
        bool swapped = _main is not null && cardKind != _main;

        var target = _intro ? IslandState.Intro
            : _notice ? IslandState.Notice
            : _updatePrompt ? IslandState.Update
            : _privacyPopup ? IslandState.Privacy
            : wantCard ? IslandState.Card
            : _main is not null ? IslandState.Capsule
            : IslandState.Idle;

        // The PC row belongs to the main card only, not to the bubble's card.
        bool statsRow = target == IslandState.Card && !swapped && cardKind is not null;
        var size = SizeOf(target, cardKind, statsRow);

        _bubbleKind = target switch
        {
            IslandState.Capsule => _side,
            IslandState.Card when swapped => _main,
            _ => null
        };
        var perf = SettingsService.Current.PerfBubble;
        bool perfShown = perf != PerfBubbleMode.Off && target == IslandState.Capsule;

        var previous = _state;
        _state = target;
        _cardKind = cardKind;
        _cardSwapped = swapped;
        _numbersVisible = target == IslandState.Idle || target == IslandState.Card;
        _rowVisibleInCard = statsRow;

        // Which Claude views are on screen (they are built only while visible, because their rings animate forever).
        _claudeInCapsule = target == IslandState.Capsule && _main == Kind.Claude;
        _claudeInBubble = _bubbleKind == Kind.Claude;
        _claudeInCard = target == IslandState.Card && cardKind == Kind.Claude;

        string key = $"{target}|{_main}|{_side}|{cardKind}|{swapped}|{_bubbleKind}|{perf}|{perfShown}|{statsRow}";
        bool sizeChanged = size != _size;

        if (sizeChanged)
        {
            bool growing = size.W * size.H > _size.W * _size.H;
            _size = size;

            IEasingFunction ease = growing
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
                : new CubicEase { EasingMode = EasingMode.EaseInOut };
            var duration = TimeSpan.FromMilliseconds(growing ? 500 : 520);

            Pill.BeginAnimation(WidthProperty, new DoubleAnimation(size.W, duration) { EasingFunction = ease });
            Pill.BeginAnimation(HeightProperty, new DoubleAnimation(size.H, duration) { EasingFunction = ease });
            BeginAnimation(PillRadiusProperty, new DoubleAnimation(size.R, duration) { EasingFunction = ease });
        }

        if (key != _viewKey)
        {
            _viewKey = key;

            // Closing a card: the card content fades out first, the pill shrinks, and the capsule content
            // fades in as the shrink finishes, so it reads as one smooth motion.
            bool collapsing = previous == IslandState.Card && target is IslandState.Capsule or IslandState.Idle;
            int arriveDelay = collapsing ? 300 : 120;

            FadeView(ClaudeCapsule, target == IslandState.Capsule && _main == Kind.Claude, arriveDelay);
            FadeView(FocusCompactView, target == IslandState.Capsule && _main == Kind.Focus, arriveDelay);
            FadeView(CompactView, target == IslandState.Capsule && _main == Kind.Music, arriveDelay);

            FadeView(ClaudeCard, target == IslandState.Card && cardKind == Kind.Claude);
            FadeView(ExpandedView, target == IslandState.Card && cardKind == Kind.Music);
            FadeView(MusicCardBg, target == IslandState.Card && cardKind == Kind.Music);
            FadeView(FocusPageView, target == IslandState.Card && cardKind == Kind.Focus);
            FadeView(SystemCard, target == IslandState.Card && cardKind is null);
            FadeView(CardSettingsButton, target == IslandState.Card);
            FadeView(CardMinimizeButton, target == IslandState.Card);

            FadeView(PrivacyView, target == IslandState.Privacy);
            FadeView(NoticeView, target == IslandState.Notice);
            FadeView(UpdateView, target == IslandState.Update);
            FadeView(IntroView, target == IslandState.Intro);
            if (target == IslandState.Intro && previous != IslandState.Intro) PlayIntro();

            // PC stats: the whole idle pill, or the bottom row of the main card.
            FadeView(StatsView, target == IslandState.Idle || statsRow, arriveDelay);
            FadeView(StatsDivider, statsRow);
            StatsView.Margin = new Thickness(0, 0, 0, statsRow ? 12 : 8);

            ShowBubble();
            ShowPerfBubble(perfShown);
            SetEqualiser(_mediaPlaying);
            UpdateAlertStrip();
            UpdateStatsPace();
        }

        UpdatePrivacyDots();
        UpdatePrivacyStrip();
        RefreshClaude();
        UpdateProgress();
        UpdateBubbleContent();
    }

    // ----- Bubbles -------------------------------------------------------

    private static void PopIn(ScaleTransform scale)
    {
        var spring = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
    }

    // The activity bubble to the right of the capsule: shows the second activity, or (while its card is
    // open) the one that shrank out of the capsule.
    private void ShowBubble()
    {
        bool show = _bubbleKind is not null;

        // While it shrinks away it keeps its content, so it does not flash empty.
        if (show)
        {
            BubbleClaudeHost.Visibility = _bubbleKind == Kind.Claude ? Visibility.Visible : Visibility.Collapsed;
            BubbleMusic.Visibility = _bubbleKind == Kind.Music ? Visibility.Visible : Visibility.Collapsed;
            BubbleFocus.Visibility = _bubbleKind == Kind.Focus ? Visibility.Visible : Visibility.Collapsed;
        }

        SetBubble(Bubble, BubbleScale, show);
    }

    // A bubble springs in when it appears and shrinks and fades out when it goes, instead of snapping.
    private static void SetBubble(Border bubble, ScaleTransform scale, bool show)
    {
        bool visible = bubble.Visibility == Visibility.Visible && !Equals(bubble.Tag, "hiding");

        if (show)
        {
            bubble.Tag = null;
            bubble.BeginAnimation(OpacityProperty, null);
            bubble.Opacity = 1;
            if (!visible)
            {
                bubble.Visibility = Visibility.Visible;
                PopIn(scale);
            }
            return;
        }

        if (!visible) return;   // already hidden, or already on its way out

        bubble.Tag = "hiding";
        var duration = TimeSpan.FromMilliseconds(260);
        var shrink = new DoubleAnimation(1, 0.3, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var fade = new DoubleAnimation(1, 0, duration);
        fade.Completed += (_, _) =>
        {
            if (!Equals(bubble.Tag, "hiding")) return;   // it came back in the meantime

            bubble.Tag = null;
            bubble.Visibility = Visibility.Collapsed;
            bubble.BeginAnimation(OpacityProperty, null);
            bubble.Opacity = 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        bubble.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateBubbleContent()
    {
        if (_bubbleKind == Kind.Focus) UpdateFocusBubble();
    }

    private void Bubble_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _leaveTimer.Stop();
        if (SettingsService.Current.ExpandTrigger != ExpandTrigger.Hover) return;

        // Entering the bubble opens its activity; while a card is open, the shrunken one swaps back.
        _hoverSide = _hovering ? !_hoverSide : true;
        _hovering = true;
        ApplyState();
    }

    private void Bubble_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SettingsService.Current.ExpandTrigger != ExpandTrigger.Click) return;

        _leaveTimer.Stop();
        _hoverSide = _hovering ? !_hoverSide : true;
        _hovering = true;
        ApplyState();
    }

    // ----- Microsoft Clock (Focus sessions and timers) -------------------

    private void ApplyClockSetting()
    {
        if (SettingsService.Current.SyncMicrosoftClock)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
            if (_clockSnap is not null)
            {
                _clockSnap = null;
                ApplyState();
            }
        }
    }

    // Raised about once a second while a session runs (and once when it appears or ends).
    private void OnClockUpdated(ClockSnapshot? snapshot)
    {
        if (!SettingsService.Current.SyncMicrosoftClock) return; // late event after being switched off

        _clockSnap = snapshot;
        UpdateClockUi();
        ApplyState();
    }

    // Ring dash for a circle of the given outer size: dash lengths are measured in stroke widths.
    private static DoubleCollection Dash(double size, double thickness, double ratio) =>
        new() { Math.Clamp(ratio, 0, 1) * Math.PI * (size - thickness) / thickness, 100 };

    private void UpdateClockUi()
    {
        if (_clockSnap is not { } snap) return;

        var s = snap.Session;
        bool live = snap.Link == ClockLink.Live;
        Brush accent = s.Kind == ClockKind.Timer ? TimerBlue : s.IsBreak ? ChargingGreen : FocusOrange;
        double ratio = s.Total > TimeSpan.Zero ? Math.Clamp(1 - s.Remaining / s.Total, 0, 1) : 0;
        string countdown = FormatClock(s.Remaining);
        var ringBrush = live ? accent : DimBrush;
        string minutes = Math.Max(0, (int)Math.Ceiling(s.Remaining.TotalMinutes)).ToString();

        // Capsule
        FocusCountdownSmall.Text = countdown;
        FocusCountdownSmall.Foreground = live ? accent : DimBrush;
        FocusRing.Stroke = ringBrush;
        FocusRing.StrokeDashArray = Dash(28, 3, ratio);
        FocusRingMinutes.Text = minutes;
        FocusTitleSmall.Text = snap.Link switch
        {
            ClockLink.Minimized => "Clock minimized",
            ClockLink.PageHidden => "Open Focus in Clock",
            _ => s.Kind == ClockKind.Timer ? s.Label : s.IsBreak ? "Break" : "Focus"
        };
        FocusSubSmall.Text = s.Kind == ClockKind.Focus && s.Index is int idx && s.Count is int cnt
            ? $"session {idx} of {cnt}"
            : s.Kind == ClockKind.Timer ? "timer" : "";
        FocusSubSmall.Visibility = FocusSubSmall.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Bubble
        _focusBubbleRatio = ratio;
        _focusBubbleMinutes = minutes;
        _focusBubbleBrush = ringBrush;
        UpdateFocusBubble();

        // Card: ring + chip + title + subtitle + session segments
        FocusBigRing.Stroke = ringBrush;
        FocusBigRing.StrokeDashArray = Dash(80, 7, Math.Max(ratio, 0.002));
        FocusGlow.Color = ((SolidColorBrush)ringBrush).Color;
        FocusGlow.Opacity = live ? 0.45 : 0;

        FocusCountdown.Text = countdown;
        FocusCountdown.Foreground = live ? NormalBrush : DimBrush;

        var accentColor = ((SolidColorBrush)accent).Color;
        FocusChip.Background = new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B));
        FocusPhase.Foreground = accent;
        FocusPhase.Text = s.Kind == ClockKind.Timer ? "TIMER" : s.IsBreak ? "BREAK" : "FOCUS";

        FocusTitle.Text = s.Kind == ClockKind.Timer ? s.Label
            : s.IsBreak ? "Take a break"
            : s.Index is int i && s.Count is int c ? $"Session {i} of {c}" : s.Label;

        FocusSub.Text = snap.Link switch
        {
            ClockLink.Minimized => "Clock minimized, last known time",
            ClockLink.PageHidden => "Open Focus or Timer in Clock",
            _ when !s.Running => "Paused in Clock",
            _ when !string.IsNullOrWhiteSpace(s.UpNext) => s.UpNext!,
            _ => $"Ends at {DateTime.Now + s.Remaining:t}"
        };
        FocusSub.Foreground = live && s.Running ? DimBrush : WarnAmber;

        BuildSegments(FocusDots, s, accent);
    }

    private double _focusBubbleRatio;
    private string _focusBubbleMinutes = "";
    private Brush _focusBubbleBrush = FocusOrange;

    private void UpdateFocusBubble()
    {
        BubbleFocusRing.Stroke = _focusBubbleBrush;
        BubbleFocusRing.StrokeDashArray = Dash(38, 4, _focusBubbleRatio);
        BubbleFocusText.Text = _focusBubbleMinutes;
    }

    // Wide bars instead of dots: solid = finished, dimmed accent = current, dark = still to come.
    private static void BuildSegments(Panel panel, ClockSession s, Brush accent)
    {
        panel.Children.Clear();
        if (s.Kind != ClockKind.Focus || s.Count is not int count || s.Index is not int index) return;
        if (count < 1 || count > 12) return;

        int done = s.IsBreak ? index : index - 1;
        for (int i = 0; i < count; i++)
        {
            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = count > 6 ? 12 : 22,
                Height = 5,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                Fill = i < done || (i == done && !s.IsBreak) ? accent : TrackBrush,
                Opacity = i == done && !s.IsBreak ? 0.5 : 1
            };
            panel.Children.Add(bar);
        }
    }

    private static string FormatClock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    private static void FadeView(UIElement view, bool show, int showDelayMs = 120)
    {
        view.IsHitTestVisible = show;
        var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 240 : 140))
        {
            BeginTime = show ? TimeSpan.FromMilliseconds(showDelayMs) : TimeSpan.Zero
        };
        view.BeginAnimation(OpacityProperty, fade);
    }

    // The equaliser loops forever, so it only runs while the music capsule is actually on screen.
    private void SetEqualiser(bool playing)
    {
        _mediaPlaying = playing;
        bool run = playing && _state == IslandState.Capsule && _main == Kind.Music;
        if (run == _equaliserRunning) return;
        _equaliserRunning = run;

        var bars = new[] { Bar1, Bar2, Bar3, Bar4, Bar5 };
        for (int i = 0; i < bars.Length; i++)
        {
            if (run)
            {
                var bounce = new DoubleAnimation(4, 16, TimeSpan.FromMilliseconds(450))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 120),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                Timeline.SetDesiredFrameRate(bounce, 20);
                bars[i].BeginAnimation(HeightProperty, bounce);
            }
            else
            {
                bars[i].BeginAnimation(HeightProperty, null);
                bars[i].Height = 4;
            }
        }
    }

    // ----- Power pop-up --------------------------------------------------

    // Raised by Windows on a background thread whenever AC/battery status changes.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;

        Dispatcher.InvokeAsync(() =>
        {
            var (percent, pluggedIn) = SystemStatsService.ReadBattery();

            // Only react to a real plug/unplug on a PC that has a battery.
            if (percent is null || pluggedIn == _lastPluggedIn) return;
            _lastPluggedIn = pluggedIn;

            ShowPowerNotice(pluggedIn, percent.Value);
        });
    }

    // Charging and unplugging use the same capsule pop-up as the Bluetooth and USB devices:
    // icon on the left, "Charging / Charger connected", the battery level on the right.
    private void ShowPowerNotice(bool pluggedIn, int percent)
    {
        _privacyQueue.Enqueue(new PopupItem(
            pluggedIn ? ChargingBoltGlyph : BatteryOutlineGlyph,
            pluggedIn ? ChargingGreen : NormalBrush,
            pluggedIn ? "Charging" : "Unplugged",
            pluggedIn ? "Charger connected" : "Running on battery",
            $"{percent}%", Live: false, FillIcon: pluggedIn));

        if (!_privacyPopup) ShowNextPrivacyPopup();
    }

    private static readonly Geometry ChargingBoltGlyph = Freeze(Geometry.Parse("M13.5,2 L5,13.5 H11 L10,22 L19,10 H12.8 Z"));
    private static readonly Geometry BatteryOutlineGlyph = Freeze(Geometry.Parse("M2.5,7 H18.5 V17 H2.5 Z M18.5,10 H21.5 V14 H18.5"));

    private enum NoticeIcon { Bolt, Battery, Dot }

    // The shared 3-second pop-up: used for power changes and for coding-agent events.
    private void ShowNotice(string title, string detail, Brush color, NoticeIcon icon)
    {
        NoticeBolt.Visibility = icon == NoticeIcon.Bolt ? Visibility.Visible : Visibility.Collapsed;
        NoticeBattery.Visibility = icon == NoticeIcon.Battery ? Visibility.Visible : Visibility.Collapsed;
        NoticeDot.Visibility = icon == NoticeIcon.Dot ? Visibility.Visible : Visibility.Collapsed;
        NoticeDot.Fill = color;
        NoticeTitle.Text = title;
        NoticeTitle.Foreground = color;
        NoticePercent.Text = detail;

        _notice = true;
        _noticeTimer.Stop();
        _noticeTimer.Start(); // restarts the 3 s countdown if it was already showing
        ApplyState();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged; // static events: would otherwise keep the window alive
        SettingsService.Changed -= ApplyClockSetting;
        SettingsService.Changed -= ApplyAgentSetting;
        SettingsService.Changed -= ApplyPosition;
        SettingsService.Changed -= OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _clock.Stop();
        _agent.Stop();
        _devices.Stop();
        base.OnClosed(e);
    }

    // ----- Progress ------------------------------------------------------

    private void UpdateProgress()
    {
        bool inCard = _state == IslandState.Card && _cardKind == Kind.Music;
        bool inBubble = _bubbleKind == Kind.Music;
        if (!inCard && !inBubble) return;

        var timeline = _media.GetTimeline();
        if (timeline is null || timeline.Duration.TotalSeconds <= 0)
        {
            _progressRatio = 0;
            CurrentTime.Text = "0:00";
            TotalTime.Text = "0:00";
        }
        else
        {
            _progressRatio = Math.Clamp(timeline.Position.TotalSeconds / timeline.Duration.TotalSeconds, 0, 1);
            CurrentTime.Text = FormatTime(timeline.Position);
            TotalTime.Text = "-" + FormatTime(timeline.Duration - timeline.Position);
        }

        if (inCard) ProgressFill.Width = ProgressTrack.ActualWidth * _progressRatio;
        if (inBubble) BubbleMusicRing.StrokeDashArray = Dash(42, 2.4, _progressRatio);
    }

    private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ProgressFill.Width = e.NewSize.Width * _progressRatio;

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    // ----- Input ---------------------------------------------------------

    private void Pill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Always cancel a pending collapse; only Hover mode expands on entry.
        _leaveTimer.Stop();
        if (SettingsService.Current.ExpandTrigger != ExpandTrigger.Hover) return;

        // Already open (for example showing the bubble's card): the card must not flip back under the pointer.
        if (_hovering) return;
        _hovering = true;
        _hoverSide = false;
        ApplyState();
    }

    private void Pill_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Releasing after a drag moves the island; it must not also count as a click.
        if (FinishPressAsDrag()) { e.Handled = true; return; }

        // In Click mode a click on the capsule expands it (buttons are not
        // clickable until expanded, so this never fires for them).
        if (SettingsService.Current.ExpandTrigger != ExpandTrigger.Click) return;

        _leaveTimer.Stop();
        if (_hovering) return;
        _hovering = true;
        _hoverSide = false;
        ApplyState();
    }

    private void Pill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Short delay so a quick wobble out of the island (or across the gap to the bubble) doesn't collapse it.
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    // The gear in the corner of every open card: the way to Settings without hunting for the tray icon.
    private void CardSettings_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowSettings();

    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
