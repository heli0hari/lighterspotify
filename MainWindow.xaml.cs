using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LighterSpotify
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const string PlayGlyph = "\uE768";
        const string PauseGlyph = "\uE769";
        const string PinGlyph = "\uE718";
        const string UnpinGlyph = "\uE77A";
        const string LockGlyph = "\uE72E";
        const string UnlockGlyph = "\uE785";

        Settings settings;
        SpotifyAuth auth;
        SpotifyApi api;
        LibrespotHost librespot = new LibrespotHost();
        MediaKeys mediaKeys = new MediaKeys();
        MediaSession mediaSession = new MediaSession();
        System.Windows.Forms.NotifyIcon tray;

        DispatcherTimer pollTimer = new DispatcherTimer();
        DispatcherTimer tickTimer = new DispatcherTimer();
        DispatcherTimer statusTimer = new DispatcherTimer();
        IntPtr hwnd;
        SolidColorBrush tintBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));

        PlaybackState current;
        DateTime stateTime;
        DateTime lastEndRefresh;
        string currentImageUrl;
        bool userSeeking;
        bool userVolume;
        bool isRefreshing;
        bool playerStarted;

        ObservableCollection<TrackItem> results = new ObservableCollection<TrackItem>();
        string lastQuery = "";
        int searchOffset;

        public MainWindow()
        {
            InitializeComponent();
            settings = Settings.Load();
            ResultsList.ItemsSource = results;

            // The slider marks mouse events as handled itself, so listen to handled events too
            ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Progress_MouseDown), true);
            ProgressSlider.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Progress_MouseUp), true);
            VolumeSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Volume_MouseDown), true);
            VolumeSlider.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Volume_MouseUp), true);

            pollTimer.Interval = TimeSpan.FromSeconds(6);
            pollTimer.Tick += async (s, e) => await RefreshNowAsync();
            tickTimer.Interval = TimeSpan.FromSeconds(1);
            tickTimer.Tick += Tick;
            statusTimer.Interval = TimeSpan.FromSeconds(5);
            statusTimer.Tick += (s, e) => { StatusText.Visibility = Visibility.Collapsed; statusTimer.Stop(); };

            if (settings.HasWindowPosition && IsOnScreen(settings.WindowLeft, settings.WindowTop))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            RootBorder.Background = tintBrush;

            Topmost = settings.AlwaysOnTop;

            IsVisibleChanged += (s, e) => UpdateTimers();
            StateChanged += (s, e) => UpdateTimers();
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            int roundCorners = 2;
            DwmSetWindowAttribute(hwnd, 33, ref roundCorners, sizeof(int));
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

            // Let the glass show through: WPF must clear to transparent instead of a solid colour
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

            CreateTray();
            ApplyPinState();

            mediaSession.PlayPressed += async () => await SetPlayingAsync(true);
            mediaSession.PausePressed += async () => await SetPlayingAsync(false);
            mediaSession.NextPressed += async () => await RunCommandAsync(() => api.NextAsync());
            mediaSession.PreviousPressed += async () => await RunCommandAsync(() => api.PreviousAsync());
            mediaSession.Start(hwnd);

            mediaKeys.PlayPausePressed += async () => await TogglePlayAsync();
            mediaKeys.NextPressed += async () => await RunCommandAsync(() => api.NextAsync());
            mediaKeys.PreviousPressed += async () => await RunCommandAsync(() => api.PreviousAsync());
            if (settings.CaptureMediaKeys) mediaKeys.Register(this);

            await ShowStartScreenAsync();
        }

        async Task ShowStartScreenAsync()
        {
            if (string.IsNullOrWhiteSpace(settings.ClientId))
            {
                OpenSettings();
                ShowStatus("Add your Spotify Client ID to start.");
                return;
            }

            auth = new SpotifyAuth(settings.ClientId);
            api = new SpotifyApi(auth);

            if (!auth.HasToken)
            {
                LoginPanel.Visibility = Visibility.Visible;
                return;
            }

            await StartPlayerAsync();
        }

        async Task StartPlayerAsync()
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            string error = librespot.Start(settings);
            if (error != null) ShowStatus(error);

            playerStarted = true;
            UpdateTimers();
            await RefreshNowAsync();
        }

        // ---------- Polling ----------

        // Polls every 6 s while visible, 15 s while hidden (keeps the Windows media flyout up to date)
        void UpdateTimers()
        {
            if (!playerStarted)
            {
                pollTimer.Stop();
                tickTimer.Stop();
                return;
            }

            bool visible = IsVisible && WindowState != WindowState.Minimized;
            TimeSpan interval = TimeSpan.FromSeconds(visible ? 6 : 15);
            bool becameVisible = visible && pollTimer.Interval != interval;
            pollTimer.Interval = interval;

            if (!pollTimer.IsEnabled)
            {
                pollTimer.Start();
                tickTimer.Start();
                _ = RefreshNowAsync();
            }
            else if (becameVisible)
            {
                _ = RefreshNowAsync();
            }
        }

        async Task RefreshNowAsync()
        {
            if (api == null || isRefreshing) return;

            isRefreshing = true;
            PlaybackState state = await api.GetPlaybackAsync();
            isRefreshing = false;

            if (state == null)
            {
                ShowStatus(api.LastError);
                if (!auth.HasToken) ShowLoggedOut();
                return;
            }

            current = state;
            stateTime = DateTime.Now;
            ShowState();
            mediaSession.Update(current);
        }

        void Tick(object sender, EventArgs e)
        {
            librespot.RestartIfLoginFinished(settings);

            if (current == null || !current.HasItem || !current.IsPlaying || userSeeking) return;

            int estimated = current.ProgressMs + (int)(DateTime.Now - stateTime).TotalMilliseconds;
            if (estimated >= current.DurationMs)
            {
                SetProgress(current.DurationMs);
                if (DateTime.Now - lastEndRefresh > TimeSpan.FromSeconds(3))
                {
                    lastEndRefresh = DateTime.Now;
                    _ = RefreshNowAsync();
                }
                return;
            }
            SetProgress(estimated);
        }

        // ---------- UI updates ----------

        void ShowState()
        {
            bool onThisPc = current.DeviceName != null && string.Equals(current.DeviceName, settings.DeviceName, StringComparison.OrdinalIgnoreCase);
            PlayHereButton.Foreground = onThisPc ? (Brush)FindResource("Accent") : (Brush)FindResource("TextSub");

            if (current.DeviceName == null) DeviceText.Text = "";
            else DeviceText.Text = onThisPc ? "On this PC" : "On " + current.DeviceName;

            if (!current.HasItem)
            {
                TitleText.Text = "Nothing playing";
                ArtistText.Text = "Pick something on your phone, or search";
                SetPlayIcon(false);
                SetArt(null);
                ProgressSlider.Maximum = 1;
                SetProgress(0);
                DurationText.Text = "0:00";
                SetTrayText("LighterSpotify");
                return;
            }

            TitleText.Text = current.Title;
            ArtistText.Text = current.Artists;
            SetPlayIcon(current.IsPlaying);
            ProgressSlider.Maximum = Math.Max(1, current.DurationMs);
            DurationText.Text = FormatTime(current.DurationMs);
            if (!userSeeking) SetProgress(current.ProgressMs);
            if (!userVolume && current.Volume >= 0) VolumeSlider.Value = current.Volume;
            SetVolumeEnabled(current.SupportsVolume);
            SetArt(current.ImageUrl);
            SetTrayText(current.Title + " - " + current.Artists);
        }

        void ShowLoggedOut()
        {
            mediaSession.Update(null);
            playerStarted = false;
            UpdateTimers();
            current = null;
            TitleText.Text = "Nothing playing";
            ArtistText.Text = "";
            DeviceText.Text = "";
            SetArt(null);
            LoginPanel.Visibility = Visibility.Visible;
        }

        // Some devices (e.g. phones) don't allow remote volume control
        void SetVolumeEnabled(bool enabled)
        {
            VolumeSlider.IsEnabled = enabled;
            VolumePanel.Opacity = enabled ? 1.0 : 0.35;
            VolumePanel.ToolTip = enabled ? null : "Volume can't be changed on this device from here";
        }

        void SetProgress(int ms)
        {
            ProgressSlider.Value = ms;
            PositionText.Text = FormatTime(ms);
        }

        void SetPlayIcon(bool playing)
        {
            PlayPauseButton.Content = playing ? PauseGlyph : PlayGlyph;
        }

        void SetArt(string url)
        {
            if (url == currentImageUrl) return;
            currentImageUrl = url;

            if (url == null)
            {
                ArtBorder.Background = (Brush)FindResource("Surface");
                ArtPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(url);
            image.DecodePixelWidth = 112;
            image.EndInit();
            ArtBorder.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            ArtPlaceholder.Visibility = Visibility.Collapsed;
        }

        void ShowStatus(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
            statusTimer.Stop();
            statusTimer.Start();
        }

        // ---------- Pin, lock & transparency ----------

        void ApplyPinState()
        {
            PinButton.Content = Topmost ? UnpinGlyph : PinGlyph;
            PinButton.Foreground = Topmost ? (Brush)FindResource("Accent") : (Brush)FindResource("TextSub");
            LockButton.Visibility = Topmost ? Visibility.Visible : Visibility.Collapsed;

            // Unpinning always unlocks, since the lock button only shows while pinned
            if (!Topmost) settings.Locked = false;
            ApplyLockState();
            UpdateGlass();
        }

        void ApplyLockState()
        {
            bool locked = settings.Locked;
            ProgressRow.IsEnabled = !locked;
            ControlsRow.IsEnabled = !locked;
            SearchPanel.IsEnabled = !locked;
            LockButton.Content = locked ? LockGlyph : UnlockGlyph;
            LockButton.ToolTip = locked ? "Unlock controls" : "Lock controls";
            LockButton.Foreground = locked ? (Brush)FindResource("Accent") : (Brush)FindResource("TextSub");
        }

        // Pinned: frosted glass (blur + light tint), denser tint while the mouse is over it
        void UpdateGlass()
        {
            if (hwnd == IntPtr.Zero) return;

            bool glass = Topmost;
            Glass.Set(hwnd, glass);
            RootBorder.BorderBrush = glass ? new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) : (Brush)FindResource("Line");

            double tint = 1.0;
            if (glass)
            {
                int percent = Math.Max(10, Math.Min(100, settings.GlassTintPercent));
                tint = IsMouseOver ? 0.85 : percent / 100.0;
            }

            Color target = Color.FromArgb((byte)(tint * 255), 0x10, 0x10, 0x10);
            ColorAnimation animation = new ColorAnimation(target, TimeSpan.FromMilliseconds(180));
            tintBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        void Window_MouseEnter(object sender, MouseEventArgs e) { UpdateGlass(); }
        void Window_MouseLeave(object sender, MouseEventArgs e) { UpdateGlass(); }

        void Lock_Click(object sender, RoutedEventArgs e)
        {
            settings.Locked = !settings.Locked;
            settings.Save();
            ApplyLockState();
        }

        static string FormatTime(int ms)
        {
            TimeSpan t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        static bool IsOnScreen(double left, double top)
        {
            return left >= SystemParameters.VirtualScreenLeft - 50
                && top >= SystemParameters.VirtualScreenTop - 50
                && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
        }

        // ---------- Commands ----------

        async Task RunCommandAsync(Func<Task<bool>> command)
        {
            if (api == null) return;
            bool ok = await command();
            if (!ok) ShowStatus(api.LastError);
            await Task.Delay(400);
            await RefreshNowAsync();
        }

        async Task TogglePlayAsync()
        {
            bool isPlaying = current != null && current.HasItem && current.IsPlaying;
            await SetPlayingAsync(!isPlaying);
        }

        async Task SetPlayingAsync(bool play)
        {
            if (api == null) return;
            if (current == null || !current.HasItem || current.DeviceId == null)
            {
                if (play) await PlayOnThisPcAsync();
                return;
            }
            if (current.IsPlaying == play) return;

            current.IsPlaying = play;
            current.ProgressMs = (int)ProgressSlider.Value;
            stateTime = DateTime.Now;
            SetPlayIcon(play);
            mediaSession.Update(current);

            if (play) await RunCommandAsync(() => api.ResumeAsync());
            else await RunCommandAsync(() => api.PauseAsync());
        }

        async Task PlayOnThisPcAsync()
        {
            string deviceId = await FindThisPcAsync();
            if (deviceId == null) return;
            await RunCommandAsync(() => api.TransferAsync(deviceId, true));
        }

        async Task<string> FindThisPcAsync()
        {
            if (!librespot.IsRunning)
            {
                string error = librespot.Start(settings);
                if (error != null)
                {
                    ShowStatus(error);
                    return null;
                }
            }

            ShowStatus("Looking for this PC...");
            for (int i = 0; i < 8; i++)
            {
                List<DeviceInfo> devices = await api.GetDevicesAsync();
                if (devices != null)
                {
                    foreach (DeviceInfo d in devices)
                    {
                        if (string.Equals(d.Name, settings.DeviceName, StringComparison.OrdinalIgnoreCase)) return d.Id;
                    }
                }
                await Task.Delay(1000);
            }

            ShowStatus("This PC isn't visible to Spotify yet. Make sure librespot is running and logged in.");
            return null;
        }

        async void PlayPause_Click(object sender, RoutedEventArgs e) { await TogglePlayAsync(); }
        async void Next_Click(object sender, RoutedEventArgs e) { await RunCommandAsync(() => api.NextAsync()); }
        async void Previous_Click(object sender, RoutedEventArgs e) { await RunCommandAsync(() => api.PreviousAsync()); }
        async void PlayHere_Click(object sender, RoutedEventArgs e) { if (api != null) await PlayOnThisPcAsync(); }

        void Progress_MouseDown(object sender, MouseButtonEventArgs e)
        {
            userSeeking = true;
        }

        async void Progress_MouseUp(object sender, MouseButtonEventArgs e)
        {
            userSeeking = false;
            if (current == null || !current.HasItem) return;

            int ms = (int)ProgressSlider.Value;
            current.ProgressMs = ms;
            stateTime = DateTime.Now;
            SetProgress(ms);
            await RunCommandAsync(() => api.SeekAsync(ms));
        }

        void Volume_MouseDown(object sender, MouseButtonEventArgs e)
        {
            userVolume = true;
        }

        async void Volume_MouseUp(object sender, MouseButtonEventArgs e)
        {
            userVolume = false;
            int volume = (int)VolumeSlider.Value;
            await RunCommandAsync(() => api.SetVolumeAsync(volume));
        }

        // ---------- Search ----------

        void SearchToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = SearchPanel.Visibility != Visibility.Visible;
            SearchPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            SearchToggle.Foreground = show ? (Brush)FindResource("Accent") : (Brush)FindResource("TextSub");
            if (show)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                SearchBox.Focus();
            }
        }

        void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchToggle_Click(null, null);
                return;
            }
            if (e.Key != Key.Enter || api == null) return;

            string query = SearchBox.Text.Trim();
            if (query.Length == 0) return;

            lastQuery = query;
            searchOffset = 0;
            results.Clear();
            await LoadResultsAsync();
        }

        async void LoadMore_Click(object sender, RoutedEventArgs e)
        {
            await LoadResultsAsync();
        }

        async Task LoadResultsAsync()
        {
            List<TrackItem> items = await api.SearchTracksAsync(lastQuery, searchOffset);
            if (items == null)
            {
                ShowStatus(api.LastError);
                return;
            }

            foreach (TrackItem item in items) results.Add(item);
            searchOffset += items.Count;
            LoadMoreButton.Visibility = items.Count == 10 && searchOffset < 100 ? Visibility.Visible : Visibility.Collapsed;
            if (results.Count == 0) ShowStatus("No songs found.");
        }

        async void Result_Click(object sender, RoutedEventArgs e)
        {
            TrackItem item = (TrackItem)((FrameworkElement)sender).DataContext;
            int index = results.IndexOf(item);

            // Play the clicked song, then the rest of the results after it
            List<string> uris = new List<string>();
            for (int i = index; i < results.Count; i++) uris.Add(results[i].Uri);

            string deviceId = current != null && current.DeviceId != null ? current.DeviceId : await FindThisPcAsync();
            if (deviceId == null) return;
            await RunCommandAsync(() => api.PlayUrisAsync(deviceId, uris));
        }

        async void Queue_Click(object sender, RoutedEventArgs e)
        {
            TrackItem item = (TrackItem)((FrameworkElement)sender).DataContext;
            bool ok = await api.AddToQueueAsync(item.Uri);
            ShowStatus(ok ? "Added to queue: " + item.Title : api.LastError);
        }

        // ---------- Settings & login ----------

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible) SettingsPanel.Visibility = Visibility.Collapsed;
            else OpenSettings();
        }

        void OpenSettings()
        {
            ClientIdBox.Text = settings.ClientId;
            LibrespotPathBox.Text = settings.LibrespotPath;
            DeviceNameBox.Text = settings.DeviceName;
            MediaKeysCheck.IsChecked = settings.CaptureMediaKeys;
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchToggle.Foreground = (Brush)FindResource("TextSub");
            SettingsPanel.Visibility = Visibility.Visible;
        }

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "librespot|librespot.exe|Programs|*.exe";
            if (dialog.ShowDialog(this) == true) LibrespotPathBox.Text = dialog.FileName;
        }

        async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string clientId = ClientIdBox.Text.Trim();
            string path = LibrespotPathBox.Text.Trim();
            string deviceName = DeviceNameBox.Text.Trim();
            if (deviceName.Length == 0) deviceName = "LighterSpotify";

            bool clientChanged = clientId != settings.ClientId;
            bool librespotChanged = path != settings.LibrespotPath || deviceName != settings.DeviceName;

            settings.ClientId = clientId;
            settings.LibrespotPath = path;
            settings.DeviceName = deviceName;
            settings.CaptureMediaKeys = MediaKeysCheck.IsChecked == true;
            settings.Save();

            if (settings.CaptureMediaKeys) mediaKeys.Register(this);
            else mediaKeys.Unregister();

            SettingsPanel.Visibility = Visibility.Collapsed;

            if (librespotChanged) librespot.Stop();
            if (clientChanged)
            {
                SpotifyAuth.DeleteSavedToken();
                ShowLoggedOut();
            }

            await ShowStartScreenAsync();
        }

        void Logout_Click(object sender, RoutedEventArgs e)
        {
            SpotifyAuth.DeleteSavedToken();
            SettingsPanel.Visibility = Visibility.Collapsed;
            ShowLoggedOut();
            if (!string.IsNullOrWhiteSpace(settings.ClientId))
            {
                auth = new SpotifyAuth(settings.ClientId);
                api = new SpotifyApi(auth);
            }
        }

        async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (auth == null) return;
            ShowStatus("Finish logging in in your browser...");
            bool ok = await auth.LoginAsync();
            if (!ok)
            {
                ShowStatus("Login failed. " + auth.LastError);
                return;
            }
            Activate();
            StatusText.Visibility = Visibility.Collapsed;
            await StartPlayerAsync();
        }

        // ---------- Window & tray ----------

        void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (settings.Locked) return;
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        void Pin_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            settings.AlwaysOnTop = Topmost;
            ApplyPinState();
            settings.Save();
        }

        void Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        void Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        void CreateTray()
        {
            tray = new System.Windows.Forms.NotifyIcon();
            try
            {
                tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            }
            catch
            {
                tray.Icon = System.Drawing.SystemIcons.Application;
            }
            tray.Text = "LighterSpotify";
            tray.Visible = true;
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ToggleWindow();
            };

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Show / hide", null, (s, e) => ToggleWindow());
            menu.Items.Add("Play / pause", null, async (s, e) => await TogglePlayAsync());
            menu.Items.Add("Next", null, async (s, e) => await RunCommandAsync(() => api.NextAsync()));
            menu.Items.Add("Quit", null, (s, e) => Close());
            tray.ContextMenuStrip = menu;
        }

        void SetTrayText(string text)
        {
            if (tray == null) return;
            tray.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text;
        }

        void ToggleWindow()
        {
            if (IsVisible)
            {
                Hide();
                return;
            }
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        void Window_Closing(object sender, CancelEventArgs e)
        {
            settings.HasWindowPosition = true;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.Save();

            pollTimer.Stop();
            tickTimer.Stop();
            mediaKeys.Unregister();
            mediaSession.Stop();
            librespot.Stop();

            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
        }
    }
}
