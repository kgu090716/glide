using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Globalization.NumberFormatting;
using WinRT.Interop;

namespace ThreeFingerWindowDrag;

public sealed partial class MainWindow : Window
{
    internal const string NativeTitle = "Glide · 세 손가락 창 이동";
    internal const string EnglishWindowTitle = "Glide · Three-finger window control";
    private static readonly string AppVersion = typeof(MainWindow).Assembly.GetName().Version.ToString(3);
    private static readonly ResourceManager TextResources = new("ThreeFingerWindowDrag.TextResources", typeof(MainWindow).Assembly);
    internal const uint ShowMessage = 0x8000 + 27;
    internal const uint QuitMessage = 0x8000 + 28;
    private readonly StartupRegistration startup = new();
    private readonly Settings settings;
    private readonly InputController controller;
    private readonly TrayIcon tray;
    private readonly DispatcherQueueTimer timer;
    private readonly Native.SubclassProcedure subclass;
    private readonly IntPtr hwnd;
    private readonly bool smoke;
    private bool ready, exiting, welcomeStarted, normalizingResizeHold;
    private CultureInfo uiCulture;
    private (string TitleKey, string Message, bool LocalizeMessage) noticeText;
    private ContentDialog welcomeDialog;
    private InputState renderedState;
    private bool renderedEnabled;
    internal bool NeedsWelcome { get; }

    public MainWindow()
    {
        smoke = Environment.GetCommandLineArgs().Contains("--smoke-test");
        settings = smoke ? new Settings { SetupDone = !Environment.GetCommandLineArgs().Contains("--first-run"), Language = AppLanguage.Korean } : Settings.Load();
        NeedsWelcome = settings.NeedsWelcome;
        uiCulture = ResolveCulture(settings.Language);
        InitializeComponent();
        VersionText.Text = $"Glide {AppVersion} · WinUI 3";
        ApplyWindowLanguage();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };
        hwnd = WindowNative.GetWindowHandle(this);
        if (ElevationSupport.IsElevated)
        {
            // Only pointer-free show/quit messages are accepted from the normal installer/launcher.
            Native.ChangeWindowMessageFilterEx(hwnd, ShowMessage, 1, IntPtr.Zero);
            Native.ChangeWindowMessageFilterEx(hwnd, QuitMessage, 1, IntPtr.Zero);
            RestartElevatedButton.IsEnabled = false;
        }
        subclass = WindowMessage;
        Native.SetWindowSubclass(hwnd, subclass, new UIntPtr(1), IntPtr.Zero);
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        var area = MonitorLayout.ForWindow(hwnd, true);
        int width = Math.Min((int)(800 * scale), area.Width - 48);
        int height = Math.Min((int)(880 * scale), area.Height - 48);
        AppWindow.MoveAndResize(new RectInt32(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Glide.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        AppWindow.Closing += (_, e) => { if (!exiting) { e.Cancel = true; HideToTray(); } };
        Closed += (_, _) => DisposeInput();

        controller = new InputController(!NeedsWelcome && !smoke, settings.Speed, settings.FollowCursor, settings.RestoreMaximized,
            settings.MaximizeAtTop, settings.BringToFront, settings.AllowTopOverflow, settings.PinchResize,
            settings.SnapAtSides, settings.FlickGestures, settings.FlickSensitivity, settings.ResizeOptions);
        tray = new TrayIcon(hwnd, Localize);
        tray.Open += ShowWindow;
        tray.Toggle += ToggleEnabled;
        tray.Exit += ExitApp;
        controller.ToggleRequested += () => DispatcherQueue.TryEnqueue(ToggleEnabled);
        FollowSwitch.IsOn = settings.FollowCursor;
        RestoreSwitch.IsOn = settings.RestoreMaximized;
        TopEdgeSwitch.IsOn = settings.MaximizeAtTop;
        RaiseSwitch.IsOn = settings.BringToFront;
        OverflowSwitch.IsOn = settings.AllowTopOverflow;
        PinchSwitch.IsOn = settings.PinchResize;
        SideSnapSwitch.IsOn = settings.SnapAtSides;
        FlickSwitch.IsOn = settings.FlickGestures;
        FlickSensitivitySlider.Value = settings.FlickSensitivity;
        FlickSensitivityValue.Text = settings.FlickSensitivity + " / 10";
        FlickSensitivitySlider.IsEnabled = settings.FlickGestures;
        FlickSensitivityPanel.Opacity = settings.FlickGestures ? 1 : 0.5;
        DirectionalSwitch.IsOn = settings.DirectionalResize;
        ResizeHoldBox.Minimum = 0.2; ResizeHoldBox.Maximum = 10;
        ResizeHoldBox.SmallChange = 0.1; ResizeHoldBox.LargeChange = 0.5;
        ResizeHoldBox.NumberFormatter = new DecimalFormatter { IntegerDigits = 1, FractionDigits = 1, IsGrouped = false,
            NumberRounder = new IncrementNumberRounder { Increment = 0.1, RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero } };
        settings.ResizeHoldSeconds = ResizeTiming.NormalizeSeconds(settings.ResizeHoldSeconds);
        ResizeHoldBox.Value = settings.ResizeHoldSeconds;
        ResizeBehaviorCombo.SelectedIndex = (int)settings.ResizeBehavior;
        UpdateResizeControls();
        try { StartupSwitch.IsOn = !smoke && startup.IsEnabled(Environment.ProcessPath); }
        catch (Exception ex) { ShowNotice("StartupReadFailed", ex.Message, InfoBarSeverity.Warning); }
        SpeedSlider.Value = settings.Speed;
        SpeedValue.Text = settings.Speed + " px/mm";
        EnabledSwitch.IsOn = !NeedsWelcome;
        ready = true;
        if (!controller.State.Hotkey) ShowNotice("HotkeyFailed", "HotkeyHelp", InfoBarSeverity.Informational, localizeMessage: true);
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(150);
        timer.Tick += (_, _) => { if (timer.IsRunning) UpdateStatus(); };
        AppWindow.Changed += (_, _) => SynchronizeStatusTimer();
        Root.ActualThemeChanged += (_, _) => { renderedState = null; UpdateStatus(); };
        SynchronizeStatusTimer();
        UpdateStatus();
        if (smoke) Root.Loaded += async (_, _) => await RunSmokeTestAsync();
        else if (NeedsWelcome) Root.Loaded += async (_, _) => await ShowWelcomeAsync();
    }

    private static CultureInfo ResolveCulture(AppLanguage language) =>
        CultureInfo.GetCultureInfo(language == AppLanguage.Korean ? "ko-KR" : "en-US");

    private string Localize(string key) => TextResources.GetString(key, uiCulture);

    internal static IntPtr FindRunningWindow()
    {
        IntPtr englishWindow = Native.FindWindow(null, EnglishWindowTitle);
        return englishWindow != IntPtr.Zero ? englishWindow : Native.FindWindow(null, NativeTitle);
    }

    private void ApplyWindowLanguage()
    {
        Root.Language = uiCulture.Name;
        bool korean = settings.Language == AppLanguage.Korean;
        string title = korean ? NativeTitle : EnglishWindowTitle;
        Title = smoke ? title + " (Test)" : title;
        AdministratorStatus.Text = Localize(ElevationSupport.IsElevated ? "AdministratorActive" : "AdministratorHelp");
        EnglishLanguageItem.IsChecked = !korean;
        KoreanLanguageItem.IsChecked = korean;
        LanguageButton.Content = korean ? KoreanLanguageItem.Text : EnglishLanguageItem.Text;
    }

    private void LanguageItemClicked(object sender, RoutedEventArgs e)
    {
        var language = ReferenceEquals(sender, EnglishLanguageItem) ? AppLanguage.English : AppLanguage.Korean;
        if (!ready || settings.Language == language) return;
        settings.Language = language;
        uiCulture = ResolveCulture(language);
        ApplyWindowLanguage();
        Bindings.Update();
        tray.RefreshLanguage();
        UpdateResizeControls();
        if (Notice.IsOpen) UpdateNoticeText();
        renderedState = null;
        UpdateStatus();
        SaveSettings();
    }
    private IntPtr WindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
    {
        if (message == ShowMessage) { ShowWindow(); return IntPtr.Zero; }
        if (message == QuitMessage) { ExitApp(); return IntPtr.Zero; }
        return Native.DefSubclassProc(window, message, wParam, lParam);
    }
    internal void HideToTray() { AppWindow.Hide(); timer?.Stop(); }
    private void ShowWindow() { AppWindow.Show(); Activate(); Native.SetForegroundWindow(hwnd); SynchronizeStatusTimer(); UpdateStatus(); }
    private void SynchronizeStatusTimer()
    {
        if (timer == null) return;
        if (exiting || !AppWindow.IsVisible || Native.IsIconic(hwnd)) { timer.Stop(); return; }
        if (!timer.IsRunning) { timer.Start(); UpdateStatus(); }
    }
    private void SettingsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SettingsPage == null) return;
        double width = Math.Min(SettingsPage.MaxWidth, e.NewSize.Width);
        if (width > 0 && SettingsPage.Width != width) SettingsPage.Width = width;
    }
    private void ToggleEnabled()
    {
        EnabledSwitch.IsOn = !EnabledSwitch.IsOn;
    }
    private void ExitApp() { exiting = true; Close(); }
    private async void RestartElevatedClicked(object sender, RoutedEventArgs e)
    {
        if (smoke || ElevationSupport.IsElevated) return;
        RestartElevatedButton.IsEnabled = ElevationNoticeButton.IsEnabled = false;
        SaveSettings();
        try
        {
            using Process replacement = await Task.Run(() => Process.Start(ElevationSupport.RestartInfo(Environment.ProcessPath, Environment.ProcessId)));
            if (replacement == null) throw new InvalidOperationException(Localize("RestartProcessFailed"));
            await Task.Delay(1500);
            if (!exiting && replacement.HasExited) ShowNotice("RestartFailed", "RestartStillRunning", InfoBarSeverity.Warning, localizeMessage: true);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { ShowNotice("RestartCancelled", "ElevationCancelled", InfoBarSeverity.Informational, localizeMessage: true); }
        catch (Exception ex) { ShowNotice("ElevationFailed", ex.Message, InfoBarSeverity.Error); }
        finally { if (!exiting) RestartElevatedButton.IsEnabled = ElevationNoticeButton.IsEnabled = true; }
    }
    private void DisposeInput()
    {
        timer?.Stop(); controller?.Dispose(); tray?.Dispose();
        Native.RemoveWindowSubclass(hwnd, subclass, new UIntPtr(1));
    }
    private void EnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        ConfigureInput(); UpdateStatus();
    }
    private void FollowChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.FollowCursor = FollowSwitch.IsOn;
        ConfigureInput(); SaveSettings();
    }
    private void RestoreChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.RestoreMaximized = RestoreSwitch.IsOn;
        ConfigureInput(); SaveSettings();
    }
    private void TopEdgeChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.MaximizeAtTop = TopEdgeSwitch.IsOn;
        ConfigureInput(); SaveSettings();
    }
    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || smoke) return;
        try { startup.SetEnabled(Environment.ProcessPath, StartupSwitch.IsOn); }
        catch (Exception ex)
        {
            ready = false;
            StartupSwitch.IsOn = !StartupSwitch.IsOn;
            ready = true;
            ShowNotice("StartupChangeFailed", ex.Message, InfoBarSeverity.Error);
        }
    }
    private void SideSnapChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.SnapAtSides = SideSnapSwitch.IsOn; ConfigureInput(); SaveSettings();
    }
    private void FlickChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.FlickGestures = FlickSwitch.IsOn;
        FlickSensitivitySlider.IsEnabled = settings.FlickGestures;
        FlickSensitivityPanel.Opacity = settings.FlickGestures ? 1 : 0.5;
        ConfigureInput(); SaveSettings();
    }
    private void FlickSensitivityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!ready) return;
        settings.FlickSensitivity = (int)Math.Round(e.NewValue);
        FlickSensitivityValue.Text = settings.FlickSensitivity + " / 10";
        ConfigureInput(); SaveSettings();
    }
    private void RaiseChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.BringToFront = RaiseSwitch.IsOn; ConfigureInput(); SaveSettings();
    }
    private void OverflowChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.AllowTopOverflow = OverflowSwitch.IsOn; ConfigureInput(); SaveSettings();
    }
    private void PinchChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.PinchResize = PinchSwitch.IsOn; ConfigureInput(); SaveSettings();
    }
    private void SpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!ready) return;
        settings.Speed = (int)Math.Round(e.NewValue);
        SpeedValue.Text = settings.Speed + " px/mm";
        ConfigureInput(); SaveSettings();
    }
    private void DirectionalChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.DirectionalResize = DirectionalSwitch.IsOn;
        UpdateResizeControls(); ConfigureInput(); SaveSettings();
    }
    private void ResizeHoldChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (!ready || normalizingResizeHold || !double.IsFinite(e.NewValue)) return;
        double value = ResizeTiming.NormalizeSeconds(e.NewValue);
        if (sender.Value != value)
        {
            normalizingResizeHold = true;
            try { sender.Value = value; }
            finally { normalizingResizeHold = false; }
        }
        if (settings.ResizeHoldSeconds == value) return;
        settings.ResizeHoldSeconds = value;
        ConfigureInput(); SaveSettings();
    }
    private void UpdateResizeControls()
    {
        ResizeHoldBox.IsEnabled = settings.DirectionalResize;
        ResizeBehaviorCombo.IsEnabled = settings.DirectionalResize;
        DirectionalSettings.Opacity = settings.DirectionalResize ? 1 : 0.5;
        ResizeBehaviorDescription.Text = settings.ResizeBehavior == ResizeBehavior.LowerCorners
            ? Localize("LowerCornersHelp")
            : Localize("GrowInDirectionHelp");
    }
    private void ResizeBehaviorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || ResizeBehaviorCombo.SelectedIndex < 0) return;
        settings.ResizeBehavior = (ResizeBehavior)ResizeBehaviorCombo.SelectedIndex;
        UpdateResizeControls(); ConfigureInput(); SaveSettings();
    }
    private ContentDialog CreateWelcomeDialog()
    {
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = Localize("WelcomeIntro"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = Localize("WelcomeSteps"), TextWrapping = TextWrapping.Wrap });
        var button = new Button { Content = Localize("OpenTouchpadSettings") }; button.Click += OpenSettingsClicked; content.Children.Add(button);
        content.Children.Add(new TextBlock { Text = Localize("WelcomeOnce"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        return new ContentDialog { XamlRoot = Root.XamlRoot, Language = uiCulture.Name, Title = Localize("WelcomeTitle"), Content = content,
            PrimaryButtonText = Localize("GetStarted"), CloseButtonText = Localize("Close"), DefaultButton = ContentDialogButton.Primary };
    }
    private async Task ShowWelcomeAsync()
    {
        if (welcomeStarted || !NeedsWelcome) return;
        welcomeStarted = true;
        settings.MarkWelcomeShown(); SaveSettings();
        welcomeDialog = CreateWelcomeDialog();
        await welcomeDialog.ShowAsync();
        welcomeDialog = null;
        if (!exiting) { EnabledSwitch.IsOn = true; ConfigureInput(); UpdateStatus(); }
    }
    private void ConfigureInput() => controller.Configure(EnabledSwitch.IsOn && !smoke, settings.Speed, settings.FollowCursor,
        settings.RestoreMaximized, settings.MaximizeAtTop, settings.BringToFront, settings.AllowTopOverflow, settings.PinchResize,
        settings.SnapAtSides, settings.FlickGestures, settings.FlickSensitivity, settings.ResizeOptions);
    private void SaveSettings()
    {
        if (smoke) return;
        try { settings.Save(); }
        catch (Exception ex) { ShowNotice("SaveSettingsFailed", ex.Message, InfoBarSeverity.Error); }
    }
    private void ShowNotice(string titleKey, string message, InfoBarSeverity severity, bool localizeMessage = false)
    {
        noticeText = (titleKey, message, localizeMessage);
        UpdateNoticeText();
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    private void UpdateNoticeText()
    {
        Notice.Title = Localize(noticeText.TitleKey);
        Notice.Message = noticeText.LocalizeMessage ? Localize(noticeText.Message) : LocalizeInputError(noticeText.Message);
    }

    private string LocalizeInputError(string error) => error switch
    {
        ElevationSupport.RequiredMessage => Localize("InputElevationRequired"),
        HidField.OutOfRangeMessage => Localize("InputOutOfRange"),
        TouchpadDevice.UnsupportedReportMessage => Localize("InputUnsupportedReport"),
        TouchpadDevice.ReportTooLongMessage => Localize("InputReportTooLong"),
        TouchpadDevice.NotPrecisionTouchpadMessage => Localize("InputNotPrecisionTouchpad"),
        RawTouchpad.RegistrationErrorMessage => Localize("InputRegistrationFailed"),
        RawTouchpad.ReadDevicesErrorMessage => Localize("InputReadDevicesFailed"),
        RawTouchpad.DeviceListChangedMessage => Localize("InputDeviceListChanged"),
        _ when error?.StartsWith(WindowMover.FailurePrefix, StringComparison.Ordinal) == true =>
            Localize("InputMoveFailed") + ": " + error[WindowMover.FailurePrefix.Length..],
        _ => error
    };

    private void UpdateStatus()
    {
        if (!ready) return;
        InputState state = controller.State;
        bool enabled = EnabledSwitch.IsOn;
        InputState before = renderedState;
        if (ReferenceEquals(before, state) && renderedEnabled == enabled) return;
        if (before == null || before.ElevationRequired != state.ElevationRequired)
            ElevationNotice.IsOpen = state.ElevationRequired && !ElevationSupport.IsElevated;
        if (before == null || renderedEnabled != enabled || before.Resizing != state.Resizing || before.DirectionalResizing != state.DirectionalResizing ||
            before.ResizeReady != state.ResizeReady || before.Dragging != state.Dragging || before.EdgeAction != state.EdgeAction || before.ResizeBehavior != state.ResizeBehavior)
        {
            StateTitle.Text = Localize(state.Resizing ? "StateResizing" : state.ResizeReady ? "StateResizeReady" :
                state.Dragging ? "StateDragging" : enabled ? "StateReady" : "StatePaused");
            StateDescription.Text = Localize(state switch
            {
                { DirectionalResizing: true, ResizeBehavior: ResizeBehavior.LowerCorners } => "StateLowerCorners",
                { DirectionalResizing: true } => "StateGrowInDirection",
                { ResizeReady: true } => "StateResizeHint",
                { EdgeAction: WindowLayoutAction.LeftHalf } => "StateSnapLeft",
                { EdgeAction: WindowLayoutAction.RightHalf } => "StateSnapRight",
                { TopEdgeArmed: true } => "StateMaximize",
                { Resizing: true } => "StatePinchHint",
                { Dragging: true } => "StateDragHint",
                _ => enabled ? "StateReadyHint" : "StatePausedHint"
            });
        }
        if (before == null || renderedEnabled != enabled)
        {
            StateDot.Fill = (Brush)Application.Current.Resources[enabled ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush"];
            tray.Enabled = enabled;
        }
        if (before == null || before.DeviceCount != state.DeviceCount)
            DeviceTitle.Text = state.DeviceCount > 0 ? Localize("TouchpadConnected") : Localize("TouchpadMissing");
        int contacts = state.Contacts;
        if (before == null || before.Error != state.Error || before.DeviceCount != state.DeviceCount || before.Contacts != contacts || before.Reports != state.Reports)
            DeviceDetail.Text = LocalizeInputError(state.Error) ?? (state.DeviceCount > 0
                ? string.Format(uiCulture, Localize("TouchpadCounts"), state.DeviceCount, contacts, state.Reports)
                : Localize("TouchpadMissingHelp"));
        if (before == null || before.Contacts != contacts)
        {
            Finger1.Opacity = contacts > 0 ? 1 : 0.24;
            Finger2.Opacity = contacts > 1 ? 1 : 0.24;
            Finger3.Opacity = contacts > 2 ? 1 : 0.24;
        }
        renderedState = state; renderedEnabled = enabled;
    }
    private void RefreshClicked(object sender, RoutedEventArgs e) { controller.RefreshDevices(); UpdateStatus(); }
    private async void OpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:devices-touchpad")); }
        catch (Exception ex) { ShowNotice("OpenSettingsFailed", ex.Message, InfoBarSeverity.Error); }
    }
    private void HideClicked(object sender, RoutedEventArgs e) => HideToTray();
    private async void DiagnosticClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "Glide-diagnostic" };
            picker.FileTypeChoices.Add(Localize("TextFile"), new List<string> { ".txt" });
            InitializeWithWindow.Initialize(picker, hwnd);
            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null) { await File.WriteAllTextAsync(file.Path, DescribeInput()); ShowNotice("DiagnosticsSaved", file.Path, InfoBarSeverity.Success); }
        }
        catch (Exception ex) { ShowNotice("DiagnosticsFailed", ex.Message, InfoBarSeverity.Error); }
    }
    private string DescribeInput()
    {
        StringBuilder text = new StringBuilder();
        InputState state = controller.State;
        text.AppendLine($"Glide {AppVersion} / WinUI 3 / Windows App SDK 2.5.1 / .NET {Environment.Version}");
        text.AppendLine("Elevated: " + ElevationSupport.IsElevated);
        text.AppendLine("OS: " + Environment.OSVersion.Version);
        text.AppendLine("Settings: " + Settings.FilePath);
        text.AppendLine("Touchpads: " + state.DeviceCount + "; hotkey: " + state.Hotkey);
        text.AppendLine("Follow cursor: " + settings.FollowCursor + "; speed: " + settings.Speed);
        text.AppendLine("Raise window: " + settings.BringToFront + "; top overflow: " + settings.AllowTopOverflow + "; pinch resize: " + settings.PinchResize);
        text.AppendLine("Side snap: " + settings.SnapAtSides + "; flick gestures: " + settings.FlickGestures + "; flick sensitivity: " + settings.FlickSensitivity);
        text.AppendLine("Directional resize: " + settings.DirectionalResize + "; hold seconds: " + settings.ResizeHoldSeconds);
        text.AppendLine("Resize behavior: " + settings.ResizeBehavior);
        text.AppendLine("Input error: " + (state.Error ?? "none"));
        text.AppendLine("Native moves: " + state.NativeMoves + "; superseded targets: " + state.Coalesced);
        text.AppendLine("Max gesture processing: " + state.MaxFrameMs.ToString("F3") + " ms; input thread timer ticks: " + state.TimerTicks);
        text.AppendLine("Input watchdog interval: " + state.TimerIntervalMilliseconds + " ms; UI timer running: " + timer.IsRunning);
        text.AppendLine(state.DeviceDetails);
        return text.ToString();
    }
    private async Task SavePreviewAsync(string directory, string name, ElementTheme theme, FrameworkElement element = null)
    {
        Root.RequestedTheme = theme;
        Root.Background = new SolidColorBrush(theme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 30, 32, 39) : Windows.UI.Color.FromArgb(255, 245, 246, 250));
        await Task.Delay(250);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element ?? Root);
        var pixels = await bitmap.GetPixelsAsync();
        using var fileStream = File.Open(Path.Combine(directory, name), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = fileStream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
        Root.Background = null;
    }
    private async Task CheckHorizontalLayoutAsync(string directory, bool assertCentered)
    {
        var original = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        double oldOffset = SettingsScroll.VerticalOffset, scale = Native.GetDpiForWindow(hwnd) / 96.0;
        var area = MonitorLayout.ForWindow(hwnd, true);
        var report = new StringBuilder(); bool centered = true;
        try
        {
            SettingsScroll.ChangeView(null, 0, null, true);
            foreach (int dip in new[] { 800, 1200, 1600, 900, 800 })
            {
                int width = Math.Min((int)(dip * scale), area.Width - 32);
                AppWindow.MoveAndResize(new RectInt32(area.Left + (area.Width - width) / 2, original.Y, width, original.Height));
                await Task.Delay(160); Root.UpdateLayout();
                var page = (FrameworkElement)SettingsScroll.Content;
                double left = page.TransformToVisual(SettingsScroll).TransformPoint(new Windows.Foundation.Point()).X;
                double right = SettingsScroll.ActualWidth - left - page.ActualWidth;
                bool valid = Math.Abs(left - right) <= 2 && left >= -1 && right >= -1 &&
                    Math.Abs(page.ActualWidth - Math.Min(1000, SettingsScroll.ActualWidth)) <= 2;
                centered &= valid;
                report.AppendLine($"Requested {dip} DIP: viewport={SettingsScroll.ActualWidth:F1}, content={page.ActualWidth:F1}, left={left:F1}, right={right:F1}, centered={valid}");
                if (dip == 1600) await SavePreviewAsync(directory, "glide-wide.png", ElementTheme.Dark);
            }
        }
        finally
        {
            AppWindow.MoveAndResize(original);
            SettingsScroll.ChangeView(null, oldOffset, null, true);
            await Task.Delay(160);
        }
        File.WriteAllText(Path.Combine(directory, "horizontal-layout.txt"), report.ToString());
        if (assertCentered && !centered) throw new Exception("Settings content is not centered after resizing.\r\n" + report);
    }
    private async Task CheckResizeTimeFormattingAsync(string directory)
    {
        var report = new StringBuilder();
        ResizeHoldBox.Value = 0.5;
        for (int step = 1; step <= 12; step++)
        {
            ResizeHoldBox.Value += ResizeHoldBox.SmallChange; await Task.Delay(5);
            double expected = (double)(0.5m + step * 0.1m);
            string text = ResizeHoldBox.NumberFormatter.FormatDouble(expected);
            if (ResizeHoldBox.Value != expected || ResizeHoldBox.Text != text || settings.ResizeHoldSeconds != expected)
                throw new Exception("Resize wait spinner displayed fractional noise: " + ResizeHoldBox.Text);
            report.AppendLine("Increase: " + ResizeHoldBox.Text);
        }
        for (int step = 11; step >= 0; step--)
        {
            ResizeHoldBox.Value -= ResizeHoldBox.SmallChange; await Task.Delay(5);
            double expected = (double)(0.5m + step * 0.1m);
            if (ResizeHoldBox.Value != expected || ResizeHoldBox.Text != ResizeHoldBox.NumberFormatter.FormatDouble(expected))
                throw new Exception("Resize wait spinner drifted while decreasing.");
            report.AppendLine("Decrease: " + ResizeHoldBox.Text);
        }
        ResizeHoldBox.Value = 0.6000000015; await Task.Delay(60);
        if (ResizeHoldBox.Value != 0.6 || controller.State.ResizeHoldMilliseconds != 600) throw new Exception("Resize wait was only formatted, not normalized.");
        report.AppendLine("Noisy input: " + ResizeHoldBox.Text);
        ResizeHoldBox.Value = 0.2; ResizeHoldBox.Value -= ResizeHoldBox.SmallChange;
        if (ResizeHoldBox.Value != 0.2) throw new Exception("Resize wait minimum drifted.");
        File.WriteAllText(Path.Combine(directory, "resize-time-format.txt"), report.ToString());
        ResizeHoldBox.Value = 1;
    }
    private async Task RunSmokeTestAsync()
    {
        string[] args = Environment.GetCommandLineArgs();
        string directory = Path.GetFullPath(args[Array.IndexOf(args, "--smoke-test") + 1]);
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Delay(400);
            if (args.Contains("--layout-only"))
            {
                await CheckHorizontalLayoutAsync(directory, !args.Contains("--record-only"));
                File.WriteAllText(Path.Combine(directory, "winui-smoke-test.txt"), "PASS: horizontal resize check.\r\n" + DescribeInput());
                return;
            }
            await CheckLanguageAsync(directory);
            if (NeedsWelcome)
            {
                Task firstRun = ShowWelcomeAsync();
                await Task.Delay(180);
                if (welcomeDialog == null || !settings.OnboardingShown || EnabledSwitch.IsOn) throw new Exception("First-run welcome did not open once with input paused.");
                await SavePreviewAsync(directory, "welcome-first-run.png", ElementTheme.Light, welcomeDialog);
                welcomeDialog.Hide(); await firstRun;
                if (!EnabledSwitch.IsOn || settings.NeedsWelcome || welcomeDialog != null) throw new Exception("First-run completion did not enable the app.");
                await ShowWelcomeAsync();
                if (welcomeDialog != null) throw new Exception("Welcome appeared twice.");
            }
            InputState hiddenRender = renderedState; long ticksBeforeHide = controller.State.TimerTicks;
            HideToTray(); await Task.Delay(300);
            if (timer.IsRunning || !ReferenceEquals(hiddenRender, renderedState)) throw new Exception("Hidden window continued refreshing the UI.");
            if (controller.State.TimerTicks <= ticksBeforeHide) throw new Exception("Hiding the UI also stopped the input thread.");
            ShowWindow(); await Task.Delay(100);
            if (!timer.IsRunning) throw new Exception("Showing the window did not resume UI updates.");
            Native.ShowWindow(hwnd, 6); await Task.Delay(150);
            if (timer.IsRunning) throw new Exception("Minimized window continued refreshing the UI.");
            Native.ShowWindow(hwnd, 9); ShowWindow(); await Task.Delay(100);
            if (!timer.IsRunning) throw new Exception("Restoring the window did not resume UI updates.");
            FollowSwitch.IsOn = false;
            await Task.Delay(60);
            if (controller.State.FollowCursor) throw new Exception("Follow toggle did not update the motion engine.");
            FollowSwitch.IsOn = true;
            await Task.Delay(60);
            if (!controller.State.FollowCursor) throw new Exception("Follow toggle did not re-enable cursor tracking.");
            SpeedSlider.Value = 18;
            await Task.Delay(60);
            if (controller.State.Speed != 18) throw new Exception("Speed slider did not update the gesture engine.");
            SpeedSlider.Value = 12;
            TopEdgeSwitch.IsOn = false;
            await Task.Delay(60);
            if (controller.State.MaximizeAtTop) throw new Exception("Top-edge option did not turn off.");
            TopEdgeSwitch.IsOn = true;
            await Task.Delay(60);
            if (!controller.State.MaximizeAtTop) throw new Exception("Top-edge option did not turn on.");
            RaiseSwitch.IsOn = false; OverflowSwitch.IsOn = false; PinchSwitch.IsOn = false;
            await Task.Delay(60);
            if (controller.State.BringToFront || controller.State.AllowTopOverflow || controller.State.PinchResize) throw new Exception("New options did not turn off.");
            RaiseSwitch.IsOn = true; OverflowSwitch.IsOn = true; PinchSwitch.IsOn = true;
            await Task.Delay(60);
            if (!controller.State.BringToFront || !controller.State.AllowTopOverflow || !controller.State.PinchResize) throw new Exception("New options did not turn on.");
            SideSnapSwitch.IsOn = false; FlickSwitch.IsOn = false;
            await Task.Delay(60);
            if (controller.State.SnapAtSides || controller.State.FlickGestures || FlickSensitivitySlider.IsEnabled) throw new Exception("Layout options did not turn off.");
            SideSnapSwitch.IsOn = true; FlickSwitch.IsOn = true; FlickSensitivitySlider.Value = 9;
            await Task.Delay(60);
            if (!controller.State.SnapAtSides || !controller.State.FlickGestures || controller.State.FlickSensitivity != 9 || !FlickSensitivitySlider.IsEnabled) throw new Exception("Layout options did not turn on or sensitivity did not update.");
            FlickSensitivitySlider.Value = 5;
            DirectionalSwitch.IsOn = false;
            await Task.Delay(60);
            if (controller.State.DirectionalResize || ResizeHoldBox.IsEnabled || ResizeBehaviorCombo.IsEnabled)
                throw new Exception("Directional resize did not turn off.");
            DirectionalSwitch.IsOn = true; ResizeHoldBox.Value = 2.3;
            await Task.Delay(60);
            if (!controller.State.DirectionalResize || controller.State.ResizeHoldMilliseconds != 2300 ||
                !ResizeHoldBox.IsEnabled || ResizeHoldBox.Visibility != Visibility.Visible || !ResizeBehaviorCombo.IsEnabled)
                throw new Exception("Hold resize settings did not reach the engine.");
            ResizeHoldBox.Value = 1;
            await CheckResizeTimeFormattingAsync(directory);
            ResizeBehaviorCombo.SelectedIndex = 1; await Task.Delay(60);
            if (controller.State.ResizeBehavior != ResizeBehavior.LowerCorners || !ResizeBehaviorDescription.Text.Contains("우하단") || !ResizeBehaviorDescription.Text.Contains("좌하단"))
                throw new Exception("Lower corner mode did not reach the engine or explain all four directions.");
            foreach (ComboBoxItem item in ResizeBehaviorCombo.Items)
                if (((string)item.Content).Contains("기존") || ((string)item.Content).Contains("반대")) throw new Exception("Resize names still contain comparison labels.");
            ResizeBehaviorCombo.SelectedIndex = 0; await Task.Delay(60);
            if (controller.State.ResizeBehavior != ResizeBehavior.Grow) throw new Exception("Grow mode did not reach the engine.");
            if (!EnabledSwitch.IsEnabled || Root.FindName("SetupExpander") != null) throw new Exception("The old persistent setup guide still exists.");
            Notice.IsOpen = false;
            await SavePreviewAsync(directory, "glide-light.png", ElementTheme.Light);
            await SavePreviewAsync(directory, "glide-dark.png", ElementTheme.Dark);
            var resizePosition = DirectionalOptions.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            SettingsScroll.ChangeView(null, SettingsScroll.VerticalOffset + resizePosition.Y - 70, null, true);
            await Task.Delay(150);
            await SavePreviewAsync(directory, "resize-light.png", ElementTheme.Light);
            await SavePreviewAsync(directory, "resize-dark.png", ElementTheme.Dark);
            ResizeBehaviorCombo.SelectedIndex = 1; ResizeHoldBox.Value = 0.6; await Task.Delay(60);
            await SavePreviewAsync(directory, "resize-lower-corners.png", ElementTheme.Dark);
            ResizeBehaviorCombo.SelectedIndex = 0; ResizeHoldBox.Value = 1;
            var position = LayoutOptions.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            SettingsScroll.ChangeView(null, SettingsScroll.VerticalOffset + position.Y - 70, null, true);
            await Task.Delay(150);
            await SavePreviewAsync(directory, "layout-light.png", ElementTheme.Light);
            await SavePreviewAsync(directory, "layout-dark.png", ElementTheme.Dark);
            if (RestartElevatedButton.IsEnabled == ElevationSupport.IsElevated || !AdministratorStatus.Text.Contains("관리자 권한"))
                throw new Exception("Administrator restart controls do not match the process permission level.");
            ElevationNotice.IsOpen = true;
            SettingsScroll.ChangeView(null, 0, null, true); await Task.Delay(150);
            await SavePreviewAsync(directory, "administrator-notice.png", ElementTheme.Light);
            ElevationNotice.IsOpen = false;
            var adminPosition = RestartElevatedButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            SettingsScroll.ChangeView(null, SettingsScroll.VerticalOffset + adminPosition.Y - 200, null, true); await Task.Delay(150);
            await SavePreviewAsync(directory, "administrator-settings.png", ElementTheme.Dark);
            var welcome = CreateWelcomeDialog(); var showing = welcome.ShowAsync();
            await Task.Delay(150);
            await SavePreviewAsync(directory, "welcome.png", ElementTheme.Light, welcome);
            welcome.Hide(); await showing;
            await CheckHorizontalLayoutAsync(directory, true);
            File.WriteAllText(Path.Combine(directory, "winui-smoke-test.txt"), "PASS: WinUI 3 startup, live language switching, administrator restart controls, decimal spinner formatting, lower-corner resize settings and names, first-run dialog, horizontal resizing, hidden/minimized UI suspension, light/dark rendering.\r\n" + DescribeInput());
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "winui-smoke-test.txt"), "FAIL\r\n" + ex); }
        finally { ExitApp(); }
    }

    private async Task CheckLanguageAsync(string directory)
    {
        var menu = (MenuFlyout)LanguageButton.Flyout;
        if (menu.Items.Count != 2 || !KoreanLanguageItem.IsChecked || !Equals(LanguageButton.Content, "한국어"))
            throw new Exception("The language picker did not start with two choices and the current language selected.");
        menu.ShowAt(LanguageButton);
        try
        {
            await Task.Delay(220);
            var anchor = LanguageButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            var item = EnglishLanguageItem.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            if (item.Y < anchor.Y + LanguageButton.ActualHeight - 2 || item.X > anchor.X + LanguageButton.ActualWidth)
                throw new Exception($"The language menu did not open below the button. Button {anchor}, item {item}.");
        }
        finally { menu.Hide(); }

        ShowNotice("HotkeyFailed", "HotkeyHelp", InfoBarSeverity.Informational, localizeMessage: true);
        LanguageItemClicked(EnglishLanguageItem, new RoutedEventArgs());
        await Task.Delay(80);
        if (settings.Language != AppLanguage.English || Root.Language != "en-US" ||
            !Title.StartsWith(EnglishWindowTitle, StringComparison.Ordinal) || !Equals(FollowSwitch.OnContent, "On") ||
            !Equals(ResizeHoldBox.Header, "Hold time (seconds)") || Notice.Title != "Could not register the shortcut" ||
            CreateWelcomeDialog().PrimaryButtonText != "Get started" || !EnglishLanguageItem.IsChecked ||
            KoreanLanguageItem.IsChecked || !Equals(LanguageButton.Content, "English"))
            throw new Exception("Changing the language did not update the English interface.");
        Notice.IsOpen = false;
        await SavePreviewAsync(directory, "language-english.png", ElementTheme.Light);

        LanguageItemClicked(KoreanLanguageItem, new RoutedEventArgs());
        if (settings.Language != AppLanguage.Korean || Root.Language != "ko-KR" || !Equals(FollowSwitch.OnContent, "켬") ||
            CreateWelcomeDialog().PrimaryButtonText != "시작하기" || EnglishLanguageItem.IsChecked ||
            !KoreanLanguageItem.IsChecked || !Equals(LanguageButton.Content, "한국어"))
            throw new Exception("Changing the language did not restore the Korean interface.");
    }
}
