using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AntigravityAssistant
{
    // MARK: - Non-Activating Transparent Floating WPF Window

    public sealed class FloatingAssistantWindow : Window
    {
        public bool AllowKeyFocus { get; set; }
        public Action OnHotKeyPressed;
        public Action OnWakeUpRequested;

        private const int HotKeyId = 0x4147; // 'AG'
        private IntPtr _hwnd;
        private HwndSource _hwndSource;
        private bool _isClickThroughEnabled;

        public IntPtr Handle
        {
            get { return _hwnd; }
        }

        public FloatingAssistantWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            SourceInitialized += OnSourceInitialized;
            Closed += OnWindowClosed;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
            }

            ApplyExtendedWindowStyles();
            Win32Native.RegisterHotKey(_hwnd, HotKeyId, Win32Native.MOD_ALT | Win32Native.MOD_NOREPEAT, Win32Native.VK_A);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            if (_hwnd != IntPtr.Zero)
            {
                Win32Native.UnregisterHotKey(_hwnd, HotKeyId);
            }
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
            }
        }

        public void ApplyExtendedWindowStyles()
        {
            if (_hwnd == IntPtr.Zero) return;
            long ex = Win32Native.GetExStyle(_hwnd);
            ex |= Win32Native.WS_EX_TOOLWINDOW;

            if (AllowKeyFocus)
            {
                ex &= ~Win32Native.WS_EX_NOACTIVATE;
            }
            else
            {
                ex |= Win32Native.WS_EX_NOACTIVATE;
            }

            if (_isClickThroughEnabled && !AllowKeyFocus)
            {
                ex |= Win32Native.WS_EX_TRANSPARENT;
            }
            else
            {
                ex &= ~Win32Native.WS_EX_TRANSPARENT;
            }

            Win32Native.SetExStyle(_hwnd, ex);
        }

        public void SetClickThrough(bool ignoreMouseEvents)
        {
            if (_isClickThroughEnabled == ignoreMouseEvents) return;
            _isClickThroughEnabled = ignoreMouseEvents;
            ApplyExtendedWindowStyles();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Native.WM_HOTKEY && wParam.ToInt32() == HotKeyId)
            {
                handled = true;
                Action cb = OnHotKeyPressed;
                if (cb != null) cb();
                return IntPtr.Zero;
            }

            if (Win32Native.WM_WAKEUP_INSTANCE != 0 && (uint)msg == Win32Native.WM_WAKEUP_INSTANCE)
            {
                handled = true;
                Action wake = OnWakeUpRequested;
                if (wake != null) wake();
                return IntPtr.Zero;
            }

            if (msg == Win32Native.WM_MOUSEACTIVATE && !AllowKeyFocus)
            {
                handled = true;
                return new IntPtr(Win32Native.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }
    }

    // MARK: - Panel Window Controller, Hit-Mask Passthrough & Multi-Monitor Drag (`Alt + A`)

    public sealed class PanelWindowController
    {
        public const double CompactWidth = 208.0;
        public static readonly Size ExpandedSize = new Size(416.0, 540.0);

        public event Action StateChanged;

        public bool IsExpanded { get; private set; }
        public bool IsVisible { get; private set; }
        public bool IsCompactPriorityExpanded { get; private set; }
        public int CompactTotalPriorityCount { get; private set; }
        public int CompactRunningPriorityCount { get; private set; }

        private bool _showSettings;
        private bool _isQuotaExpanded;
        private string _confirmingDeleteConvId;

        private FloatingAssistantWindow _window;
        private AppSettings _settings;
        private DispatcherTimer _hitMaskTimer;
        private DispatcherTimer _deleteConfirmTimer;
        private bool _isDraggingWindow;
        private double? _savedMascotRightX;
        private double? _savedMascotTopY;

        public PanelWindowController()
        {
            IsVisible = true;
            _isQuotaExpanded = true;
        }

        public bool ShowSettings
        {
            get { return _showSettings; }
            set
            {
                if (_showSettings != value)
                {
                    _showSettings = value;
                    NotifyChanged();
                }
            }
        }

        public bool IsQuotaExpanded
        {
            get { return _isQuotaExpanded; }
            set
            {
                if (_isQuotaExpanded != value)
                {
                    _isQuotaExpanded = value;
                    NotifyChanged();
                }
            }
        }

        public string ConfirmingDeleteConvId
        {
            get { return _confirmingDeleteConvId; }
        }

        public static Size CompactSize(bool hasPriorityCard, int visibleRowCount)
        {
            if (!hasPriorityCard)
            {
                return new Size(CompactWidth, 126.0);
            }
            int clamped = Math.Max(0, Math.Min(visibleRowCount, 4));
            return new Size(CompactWidth, 166.0 + clamped * 31.0);
        }

        public Size CurrentCompactSize
        {
            get
            {
                bool hasCard = CompactTotalPriorityCount > 0;
                int rows = IsCompactPriorityExpanded ? CompactTotalPriorityCount : CompactRunningPriorityCount;
                return CompactSize(hasCard, rows);
            }
        }

        public void SetupWindow(UIElement rootContent, AppSettings settings, int initialTotalPriority, int initialRunningPriority)
        {
            _settings = settings;
            CompactTotalPriorityCount = initialTotalPriority;
            CompactRunningPriorityCount = initialRunningPriority;

            Size initSize = CurrentCompactSize;
            Point initOrigin = LoadSavedTopRightOrigin(initSize);
            _savedMascotRightX = initOrigin.X + initSize.Width;
            _savedMascotTopY = initOrigin.Y;

            FloatingAssistantWindow win = new FloatingAssistantWindow();
            win.Width = initSize.Width;
            win.Height = initSize.Height;
            win.Left = initOrigin.X;
            win.Top = initOrigin.Y;
            win.Topmost = settings.AlwaysOnTop;
            win.AllowKeyFocus = false;
            win.Content = rootContent;
            win.OnHotKeyPressed = ToggleVisibility;
            win.OnWakeUpRequested = ShowAndExpand;

            _window = win;
            win.Show();
            IsVisible = true;

            StartHitMaskMonitoring();
            UpdateCompactMousePassthrough();
        }

        private void StartHitMaskMonitoring()
        {
            if (_hitMaskTimer != null) _hitMaskTimer.Stop();
            _hitMaskTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60.0) };
            _hitMaskTimer.Tick += delegate { UpdateCompactMousePassthrough(); };
            _hitMaskTimer.Start();
        }

        /// Ensures blank regions around the Pixel Robot & 5h/7d capsule pass 100% of clicks to underlying windows.
        public void UpdateCompactMousePassthrough()
        {
            if (_window == null || _window.Handle == IntPtr.Zero || !IsVisible) return;

            if (IsExpanded || _isDraggingWindow)
            {
                _window.SetClickThrough(false);
                return;
            }

            Win32Native.POINT cursorPx;
            if (!Win32Native.GetCursorPos(out cursorPx)) return;

            Win32Native.RECT winPx;
            if (!Win32Native.GetWindowRect(_window.Handle, out winPx)) return;

            if (cursorPx.X < winPx.Left || cursorPx.X > winPx.Right || cursorPx.Y < winPx.Top || cursorPx.Y > winPx.Bottom)
            {
                _window.SetClickThrough(true);
                return;
            }

            double scale = Win32Native.GetWindowDpiScale(_window.Handle);
            double localX = (cursorPx.X - winPx.Left) / scale;
            double localY = (cursorPx.Y - winPx.Top) / scale;

            bool insideInteractive = IsPointInsideCompactControls(localX, localY, _window.Width, _window.Height);
            _window.SetClickThrough(!insideInteractive);
        }

        private bool IsPointInsideCompactControls(double x, double y, double w, double h)
        {
            double midX = w * 0.5;

            // 1. Pixel Robot Mascot (64x80 centered near top)
            Rect robotRect = new Rect(midX - 32.0, 2.0, 64.0, 82.0);
            if (robotRect.Contains(x, y)) return true;

            // 2. 5h | 7d Quota Capsule (138x31 centered below mascot)
            Rect capsuleRect = new Rect(midX - 69.0, 85.0, 138.0, 33.0);
            if (capsuleRect.Contains(x, y)) return true;

            // 3. Priority Card (`优先事项`, 196pt wide)
            if (CompactTotalPriorityCount > 0)
            {
                double cardH = Math.Max(32.0, h - 126.0);
                Rect cardRect = new Rect(midX - 98.0, 122.0, 196.0, cardH);
                if (cardRect.Contains(x, y)) return true;
            }

            return false;
        }

        public void ToggleCompactPriorityExpanded()
        {
            IsCompactPriorityExpanded = !IsCompactPriorityExpanded;
            ResizeCompactKeepingTopRightAnchor();
            NotifyChanged();
        }

        public void UpdateCompactPriorityState(int totalCount, int runningCount)
        {
            if (CompactTotalPriorityCount == totalCount && CompactRunningPriorityCount == runningCount)
            {
                return;
            }
            CompactTotalPriorityCount = totalCount;
            CompactRunningPriorityCount = runningCount;
            ResizeCompactKeepingTopRightAnchor();
            NotifyChanged();
        }

        private void ResizeCompactKeepingTopRightAnchor()
        {
            if (_window == null || IsExpanded) return;

            Size newSize = CurrentCompactSize;
            double rightX = _savedMascotRightX ?? (_window.Left + _window.Width);
            double topY = _savedMascotTopY ?? _window.Top;
            Rect workArea = GetWorkAreaDip(rightX - 24.0, topY + 24.0);

            double newLeft = Clamp(rightX - newSize.Width, workArea.Left + 8.0, workArea.Right - newSize.Width - 8.0);
            double newTop = Clamp(topY, workArea.Top + 8.0, workArea.Bottom - newSize.Height - 8.0);

            _window.Width = newSize.Width;
            _window.Height = newSize.Height;
            _window.Left = newLeft;
            _window.Top = newTop;
        }

        public void UpdateAlwaysOnTop(bool enabled)
        {
            if (_window != null)
            {
                _window.Topmost = enabled;
            }
        }

        public void RequestDeleteConfirmation(string convId)
        {
            _confirmingDeleteConvId = convId;
            NotifyChanged();

            if (_deleteConfirmTimer != null) _deleteConfirmTimer.Stop();
            _deleteConfirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
            _deleteConfirmTimer.Tick += delegate
            {
                _deleteConfirmTimer.Stop();
                if (string.Equals(_confirmingDeleteConvId, convId, StringComparison.Ordinal))
                {
                    _confirmingDeleteConvId = null;
                    NotifyChanged();
                }
            };
            _deleteConfirmTimer.Start();
        }

        public void ClearDeleteConfirmation()
        {
            if (_confirmingDeleteConvId != null)
            {
                _confirmingDeleteConvId = null;
                NotifyChanged();
            }
        }

        public void ToggleVisibility()
        {
            if (_window == null) return;
            if (IsVisible)
            {
                _window.Hide();
                IsVisible = false;
            }
            else
            {
                _window.Show();
                IsVisible = true;
                UpdateCompactMousePassthrough();
            }
            NotifyChanged();
        }

        public void ShowAndExpand()
        {
            if (_window == null) return;
            if (!IsVisible)
            {
                _window.Show();
                IsVisible = true;
            }
            if (!IsExpanded)
            {
                SetExpanded(true);
            }
            else
            {
                NotifyChanged();
            }
        }

        public void ToggleExpanded()
        {
            SetExpanded(!IsExpanded);
        }

        public void SetExpanded(bool expand)
        {
            if (_window == null || IsExpanded == expand) return;

            if (expand)
            {
                _savedMascotRightX = _window.Left + _window.Width;
                _savedMascotTopY = _window.Top;
            }

            IsExpanded = expand;
            _window.AllowKeyFocus = expand;
            if (expand)
            {
                _window.SetClickThrough(false);
            }
            _window.ApplyExtendedWindowStyles();

            Size targetSize = expand ? ExpandedSize : CurrentCompactSize;
            double anchorRightX = expand ? (_window.Left + _window.Width) : (_savedMascotRightX ?? (_window.Left + _window.Width));
            double anchorTopY = expand ? _window.Top : (_savedMascotTopY ?? _window.Top);
            Rect workArea = GetWorkAreaDip(anchorRightX - 24.0, anchorTopY + 24.0);

            double newLeft = Clamp(anchorRightX - targetSize.Width, workArea.Left + 10.0, workArea.Right - targetSize.Width - 10.0);
            double newTop = Clamp(anchorTopY, workArea.Top + 10.0, workArea.Bottom - targetSize.Height - 10.0);

            _window.Width = targetSize.Width;
            _window.Height = targetSize.Height;
            _window.Left = newLeft;
            _window.Top = newTop;

            if (!IsExpanded)
            {
                SaveCurrentAnchor();
            }
            UpdateCompactMousePassthrough();
            NotifyChanged();
        }

        public void BeginDrag()
        {
            _isDraggingWindow = true;
            if (_window != null)
            {
                _window.SetClickThrough(false);
            }
        }

        public void MoveByDip(double dxDip, double dyDip)
        {
            if (_window == null) return;
            _isDraggingWindow = true;
            _window.Left += dxDip;
            _window.Top += dyDip;
        }

        public void FinishDrag()
        {
            _isDraggingWindow = false;
            if (_window == null) return;

            double centerX = _window.Left + _window.Width * 0.5;
            double centerY = _window.Top + _window.Height * 0.5;
            Rect workArea = GetWorkAreaDip(centerX, centerY);

            _window.Left = Clamp(_window.Left, workArea.Left + 8.0, workArea.Right - _window.Width - 8.0);
            _window.Top = Clamp(_window.Top, workArea.Top + 8.0, workArea.Bottom - _window.Height - 8.0);

            _savedMascotRightX = _window.Left + _window.Width;
            _savedMascotTopY = _window.Top;
            SaveCurrentAnchor();
            UpdateCompactMousePassthrough();
        }

        public void ResetPositionToDefault()
        {
            if (_window == null) return;
            if (!IsVisible)
            {
                _window.Show();
                IsVisible = true;
            }
            Rect workArea = GetPrimaryWorkAreaDip();
            _window.Left = workArea.Right - _window.Width - 24.0;
            _window.Top = workArea.Top + 32.0;
            _savedMascotRightX = _window.Left + _window.Width;
            _savedMascotTopY = _window.Top;
            SaveCurrentAnchor();
            UpdateCompactMousePassthrough();
            NotifyChanged();
        }

        private void SaveCurrentAnchor()
        {
            if (_window == null || _settings == null) return;
            _settings.SavePanelAnchor(_window.Left + _window.Width, _window.Top);
        }

        private Point LoadSavedTopRightOrigin(Size initSize)
        {
            if (_settings != null && _settings.PanelAnchorMaxX.HasValue && _settings.PanelAnchorMaxY.HasValue)
            {
                double rightX = _settings.PanelAnchorMaxX.Value;
                double topY = _settings.PanelAnchorMaxY.Value;
                Rect area = GetWorkAreaDip(rightX - 24.0, topY + 24.0);
                double x = Clamp(rightX - initSize.Width, area.Left + 8.0, area.Right - initSize.Width - 8.0);
                double y = Clamp(topY, area.Top + 8.0, area.Bottom - initSize.Height - 8.0);
                return new Point(x, y);
            }

            Rect primary = GetPrimaryWorkAreaDip();
            return new Point(primary.Right - initSize.Width - 24.0, primary.Top + 32.0);
        }

        private Rect GetPrimaryWorkAreaDip()
        {
            return SystemParameters.WorkArea;
        }

        private Rect GetWorkAreaDip(double dipX, double dipY)
        {
            double scale = _window != null ? Win32Native.GetWindowDpiScale(_window.Handle) : 1.0;
            int px = (int)Math.Round(dipX * scale);
            int py = (int)Math.Round(dipY * scale);

            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                return SystemParameters.WorkArea;
            }

            System.Windows.Forms.Screen best = screens[0];
            long bestDist = long.MaxValue;
            for (int i = 0; i < screens.Length; i++)
            {
                System.Drawing.Rectangle b = screens[i].Bounds;
                long dx = px < b.Left ? (b.Left - px) : (px > b.Right ? (px - b.Right) : 0);
                long dy = py < b.Top ? (b.Top - py) : (py > b.Bottom ? (py - b.Bottom) : 0);
                long dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = screens[i];
                }
            }

            System.Drawing.Rectangle wa = best.WorkingArea;
            return new Rect(wa.Left / scale, wa.Top / scale, wa.Width / scale, wa.Height / scale);
        }

        private static double Clamp(double val, double min, double max)
        {
            if (max < min) return min;
            return Math.Max(min, Math.Min(max, val));
        }

        private void NotifyChanged()
        {
            Action h = StateChanged;
            if (h != null) h();
        }
    }
}
