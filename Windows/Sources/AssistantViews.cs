using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace AntigravityAssistant
{
    // MARK: - Unified Pure-White Light Theme Palette (Matched 1:1 with AssistantViews.swift)

    internal static class LightPalette
    {
        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Display, Microsoft YaHei UI, Segoe UI");
        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI, Segoe UI");

        public static readonly Color SurfaceTopColor = Color.FromArgb(251, 255, 255, 255);
        public static readonly Color SurfaceBottomColor = Color.FromArgb(251, 251, 252, 254);
        public static readonly Brush PanelSurfaceBrush = MakeVerticalGradient(SurfaceTopColor, SurfaceBottomColor);
        public static readonly Brush PanelBorderBrush = FrozenBrush(Color.FromArgb(26, 0, 0, 0));
        public static readonly Brush HitBoxBrush = FrozenBrush(Color.FromArgb(1, 255, 255, 255));

        public static readonly Brush CardFillBrush = FrozenBrush(Color.FromRgb(245, 247, 250));
        public static readonly Brush CardBorderBrush = FrozenBrush(Color.FromArgb(18, 0, 0, 0));

        public static readonly Brush RowIdleFillBrush = FrozenBrush(Color.FromRgb(247, 248, 251));
        public static readonly Brush RowIdleBorderBrush = FrozenBrush(Color.FromArgb(17, 0, 0, 0));
        public static readonly Brush RowActiveFillBrush = FrozenBrush(Color.FromRgb(238, 245, 255));
        public static readonly Brush RowDoneFillBrush = FrozenBrush(Color.FromRgb(236, 250, 242));

        public static readonly Brush InkPrimary = FrozenBrush(Color.FromRgb(23, 31, 46));
        public static readonly Brush InkSecondary = FrozenBrush(Color.FromRgb(71, 84, 107));
        public static readonly Brush InkMuted = FrozenBrush(Color.FromRgb(115, 128, 148));
        public static readonly Brush DividerBrush = FrozenBrush(Color.FromArgb(20, 0, 0, 0));

        public static readonly Color BrandBlueColor = Color.FromRgb(23, 115, 232);
        public static readonly Brush BrandBlue = FrozenBrush(BrandBlueColor);
        public static readonly Color PinBlueColor = Color.FromRgb(18, 125, 227);
        public static readonly Brush PinBlue = FrozenBrush(PinBlueColor);
        public static readonly Color DoneGreenColor = Color.FromRgb(23, 166, 89);
        public static readonly Brush DoneGreen = FrozenBrush(DoneGreenColor);
        public static readonly Color WarnOrangeColor = Color.FromRgb(217, 119, 6);
        public static readonly Brush WarnOrange = FrozenBrush(WarnOrangeColor);
        public static readonly Color DangerRedColor = Color.FromRgb(224, 51, 61);
        public static readonly Brush DangerRed = FrozenBrush(DangerRedColor);
        public static readonly Brush IconCircleFill = FrozenBrush(Color.FromArgb(14, 0, 0, 0));

        public static SolidColorBrush FrozenBrush(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush MakeVerticalGradient(Color top, Color bottom)
        {
            LinearGradientBrush b = new LinearGradientBrush(top, bottom, 90.0);
            b.Freeze();
            return b;
        }
    }

    // MARK: - Crisp Vector Icon Geometries (Consistent Across Windows 10 & Windows 11)

    internal static class VectorIcons
    {
        public const string Bolt = "M 9.2,1 L 3.2,9.2 L 7.6,9.2 L 6.6,15 L 12.8,6.8 L 8.4,6.8 Z";
        public const string ChevronRight = "M 5.5,2.5 L 11,8 L 5.5,13.5";
        public const string ChevronDown = "M 2.5,5.5 L 8,11 L 13.5,5.5";
        public const string ChevronUp = "M 2.5,10.5 L 8,5 L 13.5,10.5";
        public const string ChevronLeft = "M 10.5,2.5 L 5,8 L 10.5,13.5";
        public const string CheckMark = "M 3.8,8.3 L 6.7,11.2 L 12.3,5.2";
        public const string PinFill = "M 5.5,2 L 10.5,2 L 10.5,3.5 L 9.6,4.2 L 9.6,7.4 L 11.8,9.4 L 11.8,10.6 L 8.7,10.6 L 8.7,14.8 L 7.3,14.8 L 7.3,10.6 L 4.2,10.6 L 4.2,9.4 L 6.4,7.4 L 6.4,4.2 L 5.5,3.5 Z";
        public const string RefreshCw = "M 13.2,8 A 5.2,5.2 0 1 1 11.7,4.3 M 13.5,2.2 L 13.5,5.4 L 10.3,5.4";
        public const string Gear = "M 6.8,1.5 L 9.2,1.5 L 9.6,3.2 L 11.1,3.9 L 12.6,2.9 L 14.2,4.5 L 13.2,6.0 L 13.9,7.5 L 15.5,7.9 L 15.5,10.1 L 13.9,10.5 L 13.2,12.0 L 14.2,13.5 L 12.6,15.1 L 11.1,14.1 L 9.6,14.8 L 9.2,16.5 L 6.8,16.5 L 6.4,14.8 L 4.9,14.1 L 3.4,15.1 L 1.8,13.5 L 2.8,12.0 L 2.1,10.5 L 0.5,10.1 L 0.5,7.9 L 2.1,7.5 L 2.8,6.0 L 1.8,4.5 L 3.4,2.9 L 4.9,3.9 L 6.4,3.2 Z M 8,6.3 A 2.7,2.7 0 1 0 8,11.7 A 2.7,2.7 0 1 0 8,6.3 Z";
        public const string Trash = "M 3,4.2 L 13,4.2 M 6,4.2 L 6,2.5 L 10,2.5 L 10,4.2 M 4.4,4.2 L 5.1,13.8 L 10.9,13.8 L 11.6,4.2 M 6.8,6.8 L 6.8,11.5 M 9.2,6.8 L 9.2,11.5";

        public static UIElement FilledPath(string data, Brush fill, double size)
        {
            Path p = new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            return p;
        }

        public static UIElement StrokedPath(string data, Brush stroke, double size, double thickness)
        {
            Path p = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            return p;
        }

        public static UIElement CheckCircleFill(Brush fill, double size)
        {
            Grid g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Fill = fill, Width = size, Height = size });
            g.Children.Add(StrokedPath(CheckMark, Brushes.White, size * 0.56, Math.Max(1.4, size * 0.13)));
            return g;
        }

        public static UIElement BoltCircleFill(Brush fill, double size)
        {
            Grid g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Fill = fill, Width = size, Height = size });
            g.Children.Add(FilledPath(Bolt, Brushes.White, size * 0.58));
            return g;
        }

        public static UIElement CollapseUpCircleFill(Brush fill, double size)
        {
            Grid g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Fill = fill, Width = size, Height = size });
            g.Children.Add(StrokedPath(ChevronUp, Brushes.White, size * 0.52, 1.7));
            return g;
        }
    }

    // MARK: - Mini Circular Progress Gauge (`MiniRingGauge`)

    internal sealed class MiniRingGaugeControl : FrameworkElement
    {
        private readonly double _percent;
        private readonly Color _color;

        public MiniRingGaugeControl(double percent, Color color, double size)
        {
            _percent = percent;
            _color = color;
            Width = size;
            Height = size;
            VerticalAlignment = VerticalAlignment.Center;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Width;
            double stroke = 2.2;
            double radius = Math.Max(2.0, (s - stroke) * 0.5);
            Point center = new Point(s * 0.5, s * 0.5);

            Pen trackPen = new Pen(LightPalette.FrozenBrush(Color.FromArgb(26, 0, 0, 0)), stroke);
            dc.DrawEllipse(null, trackPen, center, radius, radius);

            double ratio = Math.Max(0.03, Math.Min(0.999, _percent / 100.0));
            double angleRad = (ratio * 360.0 - 90.0) * Math.PI / 180.0;
            Point startPt = new Point(center.X, center.Y - radius);
            Point endPt = new Point(center.X + radius * Math.Cos(angleRad), center.Y + radius * Math.Sin(angleRad));

            StreamGeometry geom = new StreamGeometry();
            using (StreamGeometryContext ctx = geom.Open())
            {
                ctx.BeginFigure(startPt, false, false);
                ctx.ArcTo(endPt, new Size(radius, radius), 0.0, ratio > 0.5, SweepDirection.Clockwise, true, false);
            }
            geom.Freeze();

            Pen arcPen = new Pen(LightPalette.FrozenBrush(_color), stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, arcPen, geom);
        }
    }

    // MARK: - Modern Apple/Fluent Pill Toggle Switch (`ToggleSwitchControl`)

    internal sealed class ToggleSwitchControl : Border
    {
        private bool _isOn;
        private readonly Ellipse _thumb;
        private readonly Action<bool> _onChanged;

        public ToggleSwitchControl(bool initialValue, Action<bool> onChanged)
        {
            _isOn = initialValue;
            _onChanged = onChanged;
            Width = 34.0;
            Height = 19.0;
            CornerRadius = new CornerRadius(9.5);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;

            _thumb = new Ellipse
            {
                Width = 15.0,
                Height = 15.0,
                Fill = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0.8, Opacity = 0.22, Color = Colors.Black }
            };

            Grid track = new Grid();
            track.Children.Add(_thumb);
            Child = track;

            UpdateVisualState();

            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _isOn = !_isOn;
                UpdateVisualState();
                if (_onChanged != null) _onChanged(_isOn);
            };
        }

        private void UpdateVisualState()
        {
            Background = _isOn ? LightPalette.DoneGreen : LightPalette.FrozenBrush(Color.FromArgb(42, 0, 0, 0));
            _thumb.HorizontalAlignment = _isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _thumb.Margin = _isOn ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
        }
    }

    // MARK: - Smooth Win32 Screen-Space Drag + Tap Helper

    internal static class DragTapBehavior
    {
        private static Point GetScreenPoint(UIElement target, MouseEventArgs e)
        {
            try
            {
                return target.PointToScreen(e.GetPosition(target));
            }
            catch
            {
                Win32Native.POINT pt;
                if (Win32Native.GetCursorPos(out pt))
                {
                    return new Point(pt.X, pt.Y);
                }
                return new Point(0.0, 0.0);
            }
        }

        public static void Attach(
            UIElement target,
            PanelWindowController controller,
            Action onTap)
        {
            Point startPx = default(Point);
            Point lastPx = default(Point);
            bool tracking = false;
            bool didDrag = false;

            target.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                startPx = GetScreenPoint(target, e);
                lastPx = startPx;
                tracking = true;
                didDrag = false;
                e.Handled = true;
            };

            target.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!tracking) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    tracking = false;
                    if (didDrag)
                    {
                        target.ReleaseMouseCapture();
                        controller.FinishDrag();
                    }
                    return;
                }

                Point curPx = GetScreenPoint(target, e);
                double totalDist = Math.Sqrt((curPx.X - startPx.X) * (curPx.X - startPx.X) + (curPx.Y - startPx.Y) * (curPx.Y - startPx.Y));
                if (totalDist > 4.0)
                {
                    if (!didDrag)
                    {
                        didDrag = true;
                        target.CaptureMouse();
                        controller.BeginDrag();
                    }
                }

                if (didDrag)
                {
                    PresentationSource src = PresentationSource.FromVisual(target);
                    double scaleX = (src != null && src.CompositionTarget != null) ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
                    double scaleY = (src != null && src.CompositionTarget != null) ? src.CompositionTarget.TransformToDevice.M22 : 1.0;
                    double dxDip = (curPx.X - lastPx.X) / (scaleX > 0 ? scaleX : 1.0);
                    double dyDip = (curPx.Y - lastPx.Y) / (scaleY > 0 ? scaleY : 1.0);
                    lastPx = curPx;
                    controller.MoveByDip(dxDip, dyDip);
                }
                e.Handled = true;
            };

            target.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!tracking) return;
                tracking = false;
                if (didDrag)
                {
                    target.ReleaseMouseCapture();
                    controller.FinishDrag();
                }
                else if (onTap != null)
                {
                    onTap();
                }
                e.Handled = true;
            };
        }
    }

    // MARK: - Root Assistant View (Switches Between Compact Floating Mascot & Expanded HUD)

    public sealed class AssistantRootView : Grid
    {
        private readonly TranscriptMonitor _monitor;
        private readonly AppSettings _settings;
        private readonly PanelWindowController _panelController;

        private readonly PixelRobotMascotControl _compactMascot;
        private readonly PixelRobotMascotControl _headerMascot;
        private readonly List<KeyValuePair<TextBlock, DateTime>> _liveElapsedLabels = new List<KeyValuePair<TextBlock, DateTime>>();
        private TextBlock _headerUpdateTimeLabel;
        private Border _expandedRootCard;
        private string _lastStructuralSignature = string.Empty;

        public AssistantRootView(TranscriptMonitor monitor, AppSettings settings, PanelWindowController panelController)
        {
            _monitor = monitor;
            _settings = settings;
            _panelController = panelController;

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _compactMascot = new PixelRobotMascotControl(AgentPhase.Idle, 74.0, true);
            _headerMascot = new PixelRobotMascotControl(AgentPhase.Idle, 37.0, false);

            _monitor.StateChanged += OnAnyStateChanged;
            _panelController.StateChanged += OnAnyStateChanged;
            _monitor.ClockTicked += OnClockTicked;

            RebuildIfNeeded(true);
        }

        public void ForceRebuildForExport(bool animateMascot)
        {
            RebuildIfNeeded(true);
        }

        private void OnAnyStateChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnAnyStateChanged));
                return;
            }

            // Fast-path opacity update without rebuilding Settings Slider during drag
            if (_expandedRootCard != null && _panelController.IsExpanded)
            {
                _expandedRootCard.Opacity = _settings.PillOpacity;
            }

            RebuildIfNeeded(false);
        }

        private void OnClockTicked()
        {
            if (!Dispatcher.CheckAccess()) return;
            DateTime nowUtc = _monitor.NowTickUtc;
            for (int i = 0; i < _liveElapsedLabels.Count; i++)
            {
                KeyValuePair<TextBlock, DateTime> pair = _liveElapsedLabels[i];
                double sec = Math.Max(0.0, (nowUtc - pair.Value).TotalSeconds);
                string prefix = pair.Key.Tag as string ?? string.Empty;
                pair.Key.Text = prefix + TimeFormatters.FormatDuration(sec);
            }
        }

        private AgentPhase EffectiveOrbPhase
        {
            get
            {
                ConversationSnapshot active = _monitor.PrimaryConversation;
                if (active != null && active.Phase != null && active.Phase.LiveDisplayLabel != null)
                {
                    return active.Phase;
                }
                return AgentPhase.Idle;
            }
        }

        private string ComputeSignature()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append(_panelController.IsExpanded ? "E1|" : "E0|");
            sb.Append(_panelController.ShowSettings ? "S1|" : "S0|");
            sb.Append(_panelController.IsQuotaExpanded ? "Q1|" : "Q0|");
            sb.Append(_panelController.IsCompactPriorityExpanded ? "CP1|" : "CP0|");
            sb.Append(_panelController.ConfirmingDeleteConvId ?? "-").Append('|');
            sb.Append(_monitor.ToastMessage ?? "-").Append('|');
            sb.Append(_settings.AlwaysOnTop ? "T1|" : "T0|");
            if (!_panelController.IsExpanded)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "OP:{0:0.00}|", _settings.PillOpacity);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, "Q:{0:0.0}:{1:0.0}|", _monitor.Quota.FiveHourRemainingPercent, _monitor.Quota.WeeklyRemainingPercent);
            sb.Append(_monitor.Quota.IsFiveHourSynced ? "SY1|" : "SY0|");
            sb.Append(_monitor.IsRefreshingCloud ? "RC1|" : "RC0|");
            sb.Append(_monitor.IsSigningInGoogle ? "SG1|" : "SG0|");
            sb.Append(MagpieQuotaClient.HasDirectGoogleAccounts ? "DG1|" : "DG0|");
            sb.Append("AC:").Append(_monitor.CloudAccounts.Count).Append('|');
            sb.Append(_monitor.IsHookPluginInstalled ? "HK1|" : "HK0|");
            sb.Append(_settings.NotifyOnComplete).Append(_settings.MinTurnDurationForAlert).Append(_settings.NotifyOnWaitingOrError)
              .Append(_settings.NotifyOnLowRemaining).Append(_settings.SoundEnabled).Append('|');

            CloudAccountQuota acct = _monitor.ActiveCloudAccount;
            if (acct != null)
            {
                sb.Append(acct.UserEmail).Append(':').Append(acct.PlanName).Append(':')
                  .Append(acct.SourceName ?? string.Empty).Append(':')
                  .Append(acct.ErrorMessage ?? string.Empty).Append('|');
                if (acct.GeminiPool != null) sb.AppendFormat(CultureInfo.InvariantCulture, "G:{0:0.0}:{1:0.0}|", acct.GeminiPool.Remaining5h, acct.GeminiPool.Remaining7d);
                if (acct.ClaudeGptPool != null) sb.AppendFormat(CultureInfo.InvariantCulture, "C:{0:0.0}:{1:0.0}|", acct.ClaudeGptPool.Remaining5h, acct.ClaudeGptPool.Remaining7d);
            }

            for (int i = 0; i < _monitor.Conversations.Count; i++)
            {
                ConversationSnapshot c = _monitor.Conversations[i];
                sb.Append(c.Id).Append(':')
                  .Append(c.Title).Append(':')
                  .Append(c.ProjectName).Append(':')
                  .Append((int)c.Phase.Kind).Append(':')
                  .Append(c.Phase.ToolName ?? string.Empty).Append(':')
                  .Append(_monitor.IsPinned(c.Id) ? '1' : '0')
                  .Append(_monitor.IsUnreadCompleted(c.Id) ? '1' : '0')
                  .Append('|');
            }
            return sb.ToString();
        }

        private void RebuildIfNeeded(bool force)
        {
            AgentPhase orbPhase = EffectiveOrbPhase;
            _compactMascot.Phase = orbPhase;
            _headerMascot.Phase = orbPhase;

            if (_headerUpdateTimeLabel != null)
            {
                _headerUpdateTimeLabel.Text = "更新于 " + TimeFormatters.FormatUtc8(_monitor.LastRefreshDateUtc, true);
            }

            string sig = ComputeSignature();
            if (!force && string.Equals(sig, _lastStructuralSignature, StringComparison.Ordinal))
            {
                return;
            }
            _lastStructuralSignature = sig;

            DetachParent(_compactMascot);
            DetachParent(_headerMascot);
            _liveElapsedLabels.Clear();
            _expandedRootCard = null;
            _headerUpdateTimeLabel = null;
            Children.Clear();

            if (_panelController.IsExpanded)
            {
                UIElement hud = BuildExpandedHud();
                Children.Add(hud);

                if (!string.IsNullOrEmpty(_monitor.ToastMessage))
                {
                    Children.Add(BuildToastOverlay(_monitor.ToastMessage));
                }
            }
            else
            {
                Children.Add(BuildCompactPill());
            }
        }

        private static void DetachParent(FrameworkElement el)
        {
            if (el == null) return;
            Panel p = el.Parent as Panel;
            if (p != null) p.Children.Remove(el);
            Decorator d = el.Parent as Decorator;
            if (d != null) d.Child = null;
        }

        // MARK: - 1. Compact Floating Mascot + Pure-White Quota Capsule + Folded Priority Tier

        private UIElement BuildCompactPill()
        {
            StackPanel outer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = PanelWindowController.CompactWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            };

            AgentPhase orbPhase = EffectiveOrbPhase;

            // 1A. Mascot Slot (80pt tall)
            Grid mascotSlot = new Grid
            {
                Width = 112.0,
                Height = 80.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent
            };

            Border mascotHitBox = new Border
            {
                Width = 66.0,
                Height = 78.0,
                Background = LightPalette.HitBoxBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Opacity = _settings.PillOpacity,
                ToolTip = "点击机器人展开详情面板 · 按住可跨屏幕拖动",
                ContextMenu = BuildCompactContextMenu(),
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2.5, Opacity = 0.24, Color = Colors.Black }
            };
            mascotHitBox.Child = _compactMascot;
            DragTapBehavior.Attach(mascotHitBox, _panelController, delegate { _panelController.ToggleExpanded(); });
            mascotSlot.Children.Add(mascotHitBox);

            string activeTag = GetCompactActiveTag(orbPhase);
            if (!string.IsNullOrEmpty(activeTag))
            {
                Border badge = new Border
                {
                    Background = LightPalette.FrozenBrush(orbPhase.AccentColor),
                    BorderBrush = LightPalette.FrozenBrush(Color.FromArgb(150, 255, 255, 255)),
                    BorderThickness = new Thickness(0.8),
                    CornerRadius = new CornerRadius(8.0),
                    Padding = new Thickness(5.5, 1.8, 5.5, 1.8),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0),
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1.5, Opacity = 0.20, Color = Colors.Black }
                };
                badge.Child = new TextBlock
                {
                    Text = activeTag,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.0,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                mascotSlot.Children.Add(badge);
            }

            outer.Children.Add(mascotSlot);

            // 1B. Pure-White Mini Quota Capsule (136x29)
            double rem5h = _monitor.Quota.FiveHourRemainingPercent;
            double remWk = _monitor.Quota.WeeklyRemainingPercent;

            Border capsule = new Border
            {
                Width = 136.0,
                Height = 29.0,
                Margin = new Thickness(0, 5, 0, 0),
                CornerRadius = new CornerRadius(14.5),
                Background = LightPalette.PanelSurfaceBrush,
                BorderBrush = LightPalette.PanelBorderBrush,
                BorderThickness = new Thickness(0.9),
                Opacity = _settings.PillOpacity,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.SizeAll,
                ToolTip = "按住可跨屏幕拖动（点击上方机器人图标展开详情）",
                ContextMenu = BuildCompactContextMenu(),
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2.0, Opacity = 0.14, Color = Colors.Black }
            };

            Grid capGrid = new Grid { Margin = new Thickness(6, 0, 6, 0) };
            capGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            capGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            capGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            UIElement left5h = MakeCapsuleHalf(string.Format(CultureInfo.InvariantCulture, "5h {0:0}%", rem5h), QuotaMetrics.StatusColor(rem5h));
            Grid.SetColumn(left5h, 0);
            capGrid.Children.Add(left5h);

            Border sep = new Border
            {
                Width = 1.0,
                Height = 12.0,
                Background = LightPalette.FrozenBrush(Color.FromArgb(30, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sep, 1);
            capGrid.Children.Add(sep);

            UIElement right7d = MakeCapsuleHalf(string.Format(CultureInfo.InvariantCulture, "7d {0:0}%", remWk), QuotaMetrics.StatusColor(remWk));
            Grid.SetColumn(right7d, 2);
            capGrid.Children.Add(right7d);

            capsule.Child = capGrid;
            DragTapBehavior.Attach(capsule, _panelController, null);
            outer.Children.Add(capsule);

            // 1C. Priority Conversations Card (`⚡ 优先事项`)
            List<ConversationSnapshot> allPriority = _monitor.PriorityConversations;
            if (allPriority.Count > 0)
            {
                UIElement priCard = BuildCompactPriorityCard(allPriority, _monitor.RunningPriorityConversations);
                outer.Children.Add(priCard);
            }

            return outer;
        }

        private static UIElement MakeCapsuleHalf(string label, Color dotColor)
        {
            StackPanel sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(new Ellipse
            {
                Width = 5.5,
                Height = 5.5,
                Fill = LightPalette.FrozenBrush(dotColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3.5, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = LightPalette.MonoFont,
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private string GetCompactActiveTag(AgentPhase orbPhase)
        {
            int workingCount = _monitor.WorkingConversationCount;
            if (workingCount > 1) return workingCount.ToString(CultureInfo.InvariantCulture) + "并行";
            switch (orbPhase.Kind)
            {
                case AgentPhaseKind.Thinking: return "思考中";
                case AgentPhaseKind.RunningTool: return "执行中";
                case AgentPhaseKind.WaitingUser: return "待确认";
                case AgentPhaseKind.Error: return "异常";
                default: return null;
            }
        }

        private ContextMenu BuildCompactContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem expand = new MenuItem { Header = "展开会话与配额面板" };
            expand.Click += delegate { _panelController.ShowAndExpand(); };
            menu.Items.Add(expand);

            MenuItem openAgy = new MenuItem { Header = "打开当前 Antigravity 会话" };
            openAgy.Click += delegate { _monitor.JumpToPrimaryConversation(); };
            menu.Items.Add(openAgy);

            MenuItem refresh = new MenuItem { Header = "立即刷新配额" };
            refresh.Click += async delegate
            {
                _monitor.RefreshNow();
                await _monitor.PollMagpieQuotaAsync(false);
            };
            menu.Items.Add(refresh);

            menu.Items.Add(new Separator());

            MenuItem resetPos = new MenuItem { Header = "重置挂件到当前屏幕右上角" };
            resetPos.Click += delegate { _panelController.ResetPositionToDefault(); };
            menu.Items.Add(resetPos);

            return menu;
        }

        private UIElement BuildCompactPriorityCard(List<ConversationSnapshot> allItems, List<ConversationSnapshot> runningItems)
        {
            List<ConversationSnapshot> displayed = _panelController.IsCompactPriorityExpanded ? allItems : runningItems;
            string countText = (!_panelController.IsCompactPriorityExpanded && runningItems.Count != allItems.Count)
                ? (runningItems.Count + "/" + allItems.Count)
                : allItems.Count.ToString(CultureInfo.InvariantCulture);

            Border card = new Border
            {
                Width = 196.0,
                Margin = new Thickness(0, 6, 0, 0),
                CornerRadius = new CornerRadius(13.0),
                Background = LightPalette.PanelSurfaceBrush,
                BorderBrush = LightPalette.PanelBorderBrush,
                BorderThickness = new Thickness(0.9),
                Padding = new Thickness(7, 7, 7, 7),
                Opacity = _settings.PillOpacity,
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2.5, Opacity = 0.15, Color = Colors.Black }
            };

            StackPanel vstack = new StackPanel { Orientation = Orientation.Vertical };

            // Header bar (Click to fold/unfold, hold to drag)
            Border headerHit = new Border
            {
                Height = 18.0,
                Padding = new Thickness(3, 0, 3, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = _panelController.IsCompactPriorityExpanded ? "点击折叠（仅展示运行中与待查看项目）" : "点击展开查看全部优先事项"
            };

            DockPanel headerRow = new DockPanel { LastChildFill = false };
            UIElement chev = VectorIcons.StrokedPath(
                _panelController.IsCompactPriorityExpanded ? VectorIcons.ChevronDown : VectorIcons.ChevronRight,
                LightPalette.InkMuted,
                8.5,
                1.6);
            DockPanel.SetDock(chev, Dock.Right);
            headerRow.Children.Add(chev);

            StackPanel leftGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftGroup.Children.Add(VectorIcons.FilledPath(VectorIcons.Bolt, LightPalette.BrandBlue, 9.0));
            leftGroup.Children.Add(new TextBlock
            {
                Text = "优先事项",
                FontFamily = LightPalette.UiFont,
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.BrandBlue,
                Margin = new Thickness(3.5, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            leftGroup.Children.Add(new TextBlock
            {
                Text = countText,
                FontFamily = LightPalette.MonoFont,
                FontSize = 9.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkMuted,
                VerticalAlignment = VerticalAlignment.Center
            });
            DockPanel.SetDock(leftGroup, Dock.Left);
            headerRow.Children.Add(leftGroup);

            headerHit.Child = headerRow;
            DragTapBehavior.Attach(headerHit, _panelController, delegate { _panelController.ToggleCompactPriorityExpanded(); });
            vstack.Children.Add(headerHit);

            if (displayed.Count > 0)
            {
                StackPanel rowsStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 0) };
                for (int i = 0; i < displayed.Count; i++)
                {
                    UIElement row = BuildCompactPriorityRow(displayed[i]);
                    if (i > 0) ((FrameworkElement)row).Margin = new Thickness(0, 4, 0, 0);
                    rowsStack.Children.Add(row);
                }

                if (displayed.Count > 4)
                {
                    ScrollViewer sv = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        MaxHeight = 4 * 27.0 + 3 * 4.0,
                        Content = rowsStack
                    };
                    vstack.Children.Add(sv);
                }
                else
                {
                    vstack.Children.Add(rowsStack);
                }
            }

            card.Child = vstack;
            return card;
        }

        private UIElement BuildCompactPriorityRow(ConversationSnapshot conv)
        {
            string liveLabel = conv.Phase.LiveDisplayLabel;
            bool isUnreadDone = _monitor.IsUnreadCompleted(conv.Id);

            Brush rowFill = liveLabel != null
                ? LightPalette.RowActiveFillBrush
                : (isUnreadDone ? LightPalette.RowDoneFillBrush : LightPalette.RowIdleFillBrush);

            Brush rowStroke = liveLabel != null
                ? LightPalette.FrozenBrush(Color.FromArgb(78, conv.Phase.AccentColor.R, conv.Phase.AccentColor.G, conv.Phase.AccentColor.B))
                : (isUnreadDone ? LightPalette.FrozenBrush(Color.FromArgb(88, LightPalette.DoneGreenColor.R, LightPalette.DoneGreenColor.G, LightPalette.DoneGreenColor.B)) : LightPalette.RowIdleBorderBrush);

            Border rowBorder = new Border
            {
                Height = 27.0,
                CornerRadius = new CornerRadius(7.5),
                Background = rowFill,
                BorderBrush = rowStroke,
                BorderThickness = new Thickness(0.8),
                Padding = new Thickness(7.5, 0, 7.5, 0),
                Cursor = Cursors.Hand,
                ToolTip = isUnreadDone
                    ? ("刚完成（点击查看并移出优先事项）：" + conv.Title + " (" + conv.ProjectName + ")")
                    : ("点击在 Antigravity 中打开：" + conv.Title + " (" + conv.ProjectName + ")")
            };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            UIElement icon;
            if (liveLabel != null)
            {
                icon = new Ellipse
                {
                    Width = 6.0,
                    Height = 6.0,
                    Fill = LightPalette.FrozenBrush(conv.Phase.AccentColor),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else if (isUnreadDone)
            {
                icon = VectorIcons.CheckCircleFill(LightPalette.DoneGreen, 10.0);
            }
            else
            {
                icon = VectorIcons.FilledPath(VectorIcons.PinFill, LightPalette.PinBlue, 9.5);
            }
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            TextBlock titleTb = new TextBlock
            {
                Text = conv.Title,
                FontFamily = LightPalette.UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                Foreground = LightPalette.InkPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5.5, 0, 4, 0)
            };
            Grid.SetColumn(titleTb, 1);
            g.Children.Add(titleTb);

            if (liveLabel != null)
            {
                if (conv.Phase.IsWorking && conv.ActiveTurnStartedAtUtc.HasValue)
                {
                    double elapsed = Math.Max(0.0, (_monitor.NowTickUtc - conv.ActiveTurnStartedAtUtc.Value).TotalSeconds);
                    TextBlock durTb = new TextBlock
                    {
                        Text = TimeFormatters.FormatDuration(elapsed),
                        Tag = string.Empty,
                        FontFamily = LightPalette.MonoFont,
                        FontSize = 9.0,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = LightPalette.FrozenBrush(conv.Phase.AccentColor),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _liveElapsedLabels.Add(new KeyValuePair<TextBlock, DateTime>(durTb, conv.ActiveTurnStartedAtUtc.Value));
                    Grid.SetColumn(durTb, 2);
                    g.Children.Add(durTb);
                }
                else if (conv.Phase.Kind == AgentPhaseKind.WaitingUser || conv.Phase.Kind == AgentPhaseKind.Error)
                {
                    TextBlock tagTb = new TextBlock
                    {
                        Text = conv.Phase.Kind == AgentPhaseKind.WaitingUser ? "待确认" : "异常",
                        FontFamily = LightPalette.UiFont,
                        FontSize = 9.0,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = LightPalette.FrozenBrush(conv.Phase.AccentColor),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(tagTb, 2);
                    g.Children.Add(tagTb);
                }
            }

            rowBorder.Child = g;
            ConversationSnapshot captured = conv;
            rowBorder.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _monitor.JumpToConversation(captured);
            };
            return rowBorder;
        }

        // MARK: - 2. Expanded Dashboard Card (408 x 456, Pure-White Light Surface)

        private UIElement BuildExpandedHud()
        {
            Border outer = new Border
            {
                Width = PanelWindowController.ExpandedSize.Width - 12.0,
                Height = PanelWindowController.ExpandedSize.Height - 12.0,
                Margin = new Thickness(6.0),
                CornerRadius = new CornerRadius(22.0),
                Background = LightPalette.PanelSurfaceBrush,
                BorderBrush = LightPalette.PanelBorderBrush,
                BorderThickness = new Thickness(1.0),
                Padding = new Thickness(14.0),
                Opacity = _settings.PillOpacity,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3.0, Opacity = 0.16, Color = Colors.Black }
            };
            _expandedRootCard = outer;

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10.0) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            UIElement header = BuildExpandedHeaderBar();
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            UIElement body = _panelController.ShowSettings ? BuildSettingsView() : BuildMainDashboardContent();
            Grid.SetRow(body, 2);
            layout.Children.Add(body);

            outer.Child = layout;
            return outer;
        }

        private UIElement BuildExpandedHeaderBar()
        {
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left draggable/collapsible title area
            Border dragArea = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "点击收起为桌面小机器人 · 按住可跨屏幕拖动"
            };

            StackPanel leftRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftRow.Children.Add(_headerMascot);

            StackPanel titleCol = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            StackPanel nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = "Antigravity 使用助手",
                FontFamily = LightPalette.UiFont,
                FontSize = 13.0,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });

            Border hkBadge = new Border
            {
                Background = LightPalette.FrozenBrush(Color.FromArgb(14, 0, 0, 0)),
                CornerRadius = new CornerRadius(4.0),
                Padding = new Thickness(4.5, 1.0, 4.5, 1.0),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            hkBadge.Child = new TextBlock
            {
                Text = "Alt+A",
                FontFamily = LightPalette.MonoFont,
                FontSize = 9.2,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkSecondary
            };
            nameRow.Children.Add(hkBadge);
            titleCol.Children.Add(nameRow);

            _headerUpdateTimeLabel = new TextBlock
            {
                Text = "更新于 " + TimeFormatters.FormatUtc8(_monitor.LastRefreshDateUtc, true),
                FontFamily = LightPalette.UiFont,
                FontSize = 9.5,
                Foreground = LightPalette.InkMuted,
                Margin = new Thickness(0, 1, 0, 0)
            };
            titleCol.Children.Add(_headerUpdateTimeLabel);
            leftRow.Children.Add(titleCol);
            dragArea.Child = leftRow;

            DragTapBehavior.Attach(dragArea, _panelController, delegate { _panelController.SetExpanded(false); });
            Grid.SetColumn(dragArea, 0);
            header.Children.Add(dragArea);

            // Right 4 Circular Action Buttons
            StackPanel btnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // 1. Always on Top Pin
            bool topOn = _settings.AlwaysOnTop;
            UIElement pinBtn = MakeHeaderCircleButton(
                VectorIcons.FilledPath(VectorIcons.PinFill, topOn ? LightPalette.BrandBlue : LightPalette.InkMuted, 11.5),
                topOn ? LightPalette.FrozenBrush(Color.FromArgb(32, LightPalette.BrandBlueColor.R, LightPalette.BrandBlueColor.G, LightPalette.BrandBlueColor.B)) : LightPalette.IconCircleFill,
                topOn ? "已置顶悬浮（点击取消置顶）" : "点击开启置顶悬浮",
                delegate
                {
                    _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
                    _panelController.UpdateAlwaysOnTop(_settings.AlwaysOnTop);
                });
            btnRow.Children.Add(pinBtn);

            // 2. Refresh
            UIElement refreshBtn = MakeHeaderCircleButton(
                VectorIcons.StrokedPath(VectorIcons.RefreshCw, LightPalette.InkSecondary, 11.5, 1.5),
                LightPalette.IconCircleFill,
                "立即刷新会话与云端配额",
                async delegate
                {
                    _monitor.RefreshNow();
                    bool hasCloud = MagpieQuotaClient.HasDirectGoogleAccounts || _monitor.CloudAccounts.Count > 0;
                    await _monitor.PollMagpieQuotaAsync(hasCloud);
                    if (!hasCloud)
                    {
                        _monitor.ShowToast("已刷新本地会话（点击 Google 登录可同步实盘配额）");
                    }
                });
            ((FrameworkElement)refreshBtn).Margin = new Thickness(6, 0, 0, 0);
            btnRow.Children.Add(refreshBtn);

            // 3. Settings Gear
            bool inSettings = _panelController.ShowSettings;
            UIElement gearBtn = MakeHeaderCircleButton(
                VectorIcons.FilledPath(VectorIcons.Gear, inSettings ? LightPalette.BrandBlue : LightPalette.InkSecondary, 12.0),
                inSettings ? LightPalette.FrozenBrush(Color.FromArgb(36, LightPalette.BrandBlueColor.R, LightPalette.BrandBlueColor.G, LightPalette.BrandBlueColor.B)) : LightPalette.IconCircleFill,
                inSettings ? "返回会话与配额面板" : "提醒与设置",
                delegate { _panelController.ShowSettings = !_panelController.ShowSettings; });
            ((FrameworkElement)gearBtn).Margin = new Thickness(6, 0, 0, 0);
            btnRow.Children.Add(gearBtn);

            // 4. Collapse Circle
            Border collapseBtn = new Border
            {
                Width = 22.0,
                Height = 22.0,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "收起为桌面小机器人",
                Child = VectorIcons.CollapseUpCircleFill(LightPalette.FrozenBrush(Color.FromArgb(215, 71, 84, 107)), 17.5)
            };
            collapseBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _panelController.SetExpanded(false);
            };
            btnRow.Children.Add(collapseBtn);

            Grid.SetColumn(btnRow, 1);
            header.Children.Add(btnRow);

            return header;
        }

        private static UIElement MakeHeaderCircleButton(UIElement icon, Brush bg, string tooltip, Action onClick)
        {
            Border b = new Border
            {
                Width = 24.0,
                Height = 24.0,
                CornerRadius = new CornerRadius(12.0),
                Background = bg,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = icon
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) onClick();
            };
            return b;
        }

        // MARK: - Main Dashboard Content (Collapsible Quota Card + Grouped Sessions)

        private UIElement BuildMainDashboardContent()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(9.0) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            UIElement quotaCard = _monitor.ActiveCloudAccount != null
                ? BuildMagpieQuotaCard(_monitor.ActiveCloudAccount)
                : BuildFallbackQuotaCard();
            Grid.SetRow(quotaCard, 0);
            g.Children.Add(quotaCard);

            UIElement sessionsArea = BuildSessionsListSection();
            Grid.SetRow(sessionsArea, 2);
            g.Children.Add(sessionsArea);

            return g;
        }

        private UIElement BuildMagpieQuotaCard(CloudAccountQuota acct)
        {
            CloudPoolQuota primaryPool = _monitor.ActivePoolForPrimarySession ?? acct.GeminiPool ?? acct.ClaudeGptPool;
            double rem5h = primaryPool != null ? primaryPool.Remaining5h : _monitor.Quota.FiveHourRemainingPercent;
            double rem7d = primaryPool != null ? primaryPool.Remaining7d : _monitor.Quota.WeeklyRemainingPercent;
            string poolLabel = primaryPool != null ? primaryPool.PoolName : "Gemini";
            Color c5 = QuotaMetrics.StatusColor(rem5h);
            Color c7 = QuotaMetrics.StatusColor(rem7d);

            Border card = MakeSectionCardContainer(new Thickness(10.5, 8.5, 10.5, 8.5));
            StackPanel col = new StackPanel { Orientation = Orientation.Vertical };

            // Summary Row
            Border summaryHit = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = _panelController.IsQuotaExpanded ? "点击收起配额详情" : "点击展开查看完整账号与各模型池配额明细"
            };

            Grid sumRow = new Grid();
            sumRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sumRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sumRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            UIElement checkIcon = VectorIcons.CheckCircleFill(LightPalette.DoneGreen, 13.0);
            Grid.SetColumn(checkIcon, 0);
            sumRow.Children.Add(checkIcon);

            TextBlock titleTb = new TextBlock
            {
                Text = "配额概览 · " + poolLabel,
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(6.5, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleTb, 1);
            sumRow.Children.Add(titleTb);

            StackPanel rightMetrics = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rightMetrics.Children.Add(new MiniRingGaugeControl(rem5h, c5, 12.5));
            rightMetrics.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "5h {0:0.0}%", rem5h),
                FontFamily = LightPalette.MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.FrozenBrush(c5),
                Margin = new Thickness(4, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            rightMetrics.Children.Add(new TextBlock
            {
                Text = "·",
                Foreground = LightPalette.InkMuted,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            rightMetrics.Children.Add(new MiniRingGaugeControl(rem7d, c7, 12.5));
            rightMetrics.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "7d {0:0.0}%", rem7d),
                FontFamily = LightPalette.MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.FrozenBrush(c7),
                Margin = new Thickness(4, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            rightMetrics.Children.Add(VectorIcons.StrokedPath(
                _panelController.IsQuotaExpanded ? VectorIcons.ChevronUp : VectorIcons.ChevronDown,
                LightPalette.InkMuted,
                9.5,
                1.6));

            Grid.SetColumn(rightMetrics, 2);
            sumRow.Children.Add(rightMetrics);

            summaryHit.Child = sumRow;
            summaryHit.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _panelController.IsQuotaExpanded = !_panelController.IsQuotaExpanded;
            };
            col.Children.Add(summaryHit);

            if (_panelController.IsQuotaExpanded)
            {
                col.Children.Add(MakeDivider(new Thickness(0, 8, 0, 8)));

                // Account + Plan + Cloud Refresh Button
                DockPanel acctBar = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 7) };

                Border cloudRefreshBtn = new Border
                {
                    Background = LightPalette.FrozenBrush(Color.FromArgb(28, LightPalette.BrandBlueColor.R, LightPalette.BrandBlueColor.G, LightPalette.BrandBlueColor.B)),
                    CornerRadius = new CornerRadius(10.0),
                    Padding = new Thickness(7, 2.5, 7, 2.5),
                    Cursor = _monitor.IsRefreshingCloud ? Cursors.Wait : Cursors.Hand
                };
                StackPanel crContent = new StackPanel { Orientation = Orientation.Horizontal };
                crContent.Children.Add(VectorIcons.StrokedPath(VectorIcons.RefreshCw, LightPalette.BrandBlue, 9.5, 1.4));
                crContent.Children.Add(new TextBlock
                {
                    Text = _monitor.IsRefreshingCloud ? "刷新中" : "云端刷新",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LightPalette.BrandBlue,
                    Margin = new Thickness(3.5, 0, 0, 0)
                });
                cloudRefreshBtn.Child = crContent;
                cloudRefreshBtn.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (!_monitor.IsRefreshingCloud)
                    {
                        await _monitor.PollMagpieQuotaAsync(true);
                    }
                };
                DockPanel.SetDock(cloudRefreshBtn, Dock.Right);
                acctBar.Children.Add(cloudRefreshBtn);

                StackPanel acctLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                TextBlock emailTb = new TextBlock
                {
                    Text = acct.UserEmail,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 11.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LightPalette.InkPrimary,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (_monitor.CloudAccounts.Count > 1 || acct.IsDirectGoogleAccount)
                {
                    emailTb.Cursor = Cursors.Hand;
                    emailTb.ToolTip = "点击切换或管理云端账号";
                    emailTb.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        ContextMenu cm = new ContextMenu();
                        for (int i = 0; i < _monitor.CloudAccounts.Count; i++)
                        {
                            CloudAccountQuota item = _monitor.CloudAccounts[i];
                            MenuItem mi = new MenuItem
                            {
                                Header = item.UserEmail + " (" + item.PlanName + ")",
                                IsChecked = string.Equals(item.UserEmail, acct.UserEmail, StringComparison.OrdinalIgnoreCase)
                            };
                            mi.Click += delegate { _monitor.SelectedAccountEmail = item.UserEmail; };
                            cm.Items.Add(mi);
                        }
                        cm.Items.Add(new Separator());
                        MenuItem addMi = new MenuItem { Header = "🌐 登录添加 Google 账号…" };
                        addMi.Click += async delegate { await _monitor.SignInGoogleDirectAsync(); };
                        cm.Items.Add(addMi);

                        if (acct.IsDirectGoogleAccount)
                        {
                            string targetEmail = acct.UserEmail;
                            MenuItem outMi = new MenuItem { Header = "退出当前账号 (" + targetEmail + ")" };
                            outMi.Click += async delegate { await _monitor.SignOutGoogleAccountAsync(targetEmail); };
                            cm.Items.Add(outMi);
                        }
                        cm.PlacementTarget = emailTb;
                        cm.IsOpen = true;
                    };
                }
                acctLeft.Children.Add(emailTb);

                Border planBadge = new Border
                {
                    Background = LightPalette.FrozenBrush(Color.FromArgb(16, 0, 0, 0)),
                    CornerRadius = new CornerRadius(4.0),
                    Padding = new Thickness(5.5, 1.5, 5.5, 1.5),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                planBadge.Child = new TextBlock
                {
                    Text = acct.PlanName,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LightPalette.InkSecondary
                };
                acctLeft.Children.Add(planBadge);

                DockPanel.SetDock(acctLeft, Dock.Left);
                acctBar.Children.Add(acctLeft);
                col.Children.Add(acctBar);

                if (!string.IsNullOrEmpty(acct.ErrorMessage))
                {
                    Border errBox = new Border
                    {
                        Background = LightPalette.FrozenBrush(Color.FromArgb(24, 232, 68, 56)),
                        BorderBrush = LightPalette.FrozenBrush(Color.FromArgb(60, 232, 68, 56)),
                        BorderThickness = new Thickness(0.8),
                        CornerRadius = new CornerRadius(7.5),
                        Padding = new Thickness(8, 5.5, 8, 5.5),
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    DockPanel errRow = new DockPanel { LastChildFill = false };
                    TextBlock reloginLink = new TextBlock
                    {
                        Text = _monitor.IsSigningInGoogle ? "取消等待" : "重新授权",
                        FontFamily = LightPalette.UiFont,
                        FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = LightPalette.BrandBlue,
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    reloginLink.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        await _monitor.SignInGoogleDirectAsync();
                    };
                    DockPanel.SetDock(reloginLink, Dock.Right);
                    errRow.Children.Add(reloginLink);

                    TextBlock errTb = new TextBlock
                    {
                        Text = "⚠️ " + acct.ErrorMessage,
                        FontFamily = LightPalette.UiFont,
                        FontSize = 9.5,
                        Foreground = LightPalette.DangerRed,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 240.0
                    };
                    DockPanel.SetDock(errTb, Dock.Left);
                    errRow.Children.Add(errTb);
                    errBox.Child = errRow;
                    col.Children.Add(errBox);
                }

                if (acct.GeminiPool != null)
                {
                    col.Children.Add(BuildMagpiePoolRow("Gemini", acct.GeminiPool, new Thickness(0, 0, 0, 6)));
                }
                if (acct.ClaudeGptPool != null)
                {
                    col.Children.Add(BuildMagpiePoolRow("Claude & GPT", acct.ClaudeGptPool, new Thickness(0, 0, 0, 6)));
                }

                DockPanel footerRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 1, 0, 0) };
                StackPanel footerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                TextBlock addAcctBtn = new TextBlock
                {
                    Text = _monitor.IsSigningInGoogle ? "取消登录" : "+ 账号",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LightPalette.BrandBlue,
                    Cursor = Cursors.Hand
                };
                addAcctBtn.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    await _monitor.SignInGoogleDirectAsync();
                };
                footerActions.Children.Add(addAcctBtn);

                if (acct.IsDirectGoogleAccount)
                {
                    string signOutEmail = acct.UserEmail;
                    TextBlock signOutBtn = new TextBlock
                    {
                        Text = "退出",
                        FontFamily = LightPalette.UiFont,
                        FontSize = 9.0,
                        Foreground = LightPalette.InkMuted,
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(7, 0, 0, 0)
                    };
                    signOutBtn.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        await _monitor.SignOutGoogleAccountAsync(signOutEmail);
                    };
                    footerActions.Children.Add(signOutBtn);
                }

                DockPanel.SetDock(footerActions, Dock.Right);
                footerRow.Children.Add(footerActions);

                string srcLabel = !string.IsNullOrEmpty(acct.SourceName) ? acct.SourceName : "Google 官方直连";
                string readStr = acct.ReadAtUtc.HasValue
                    ? ("实时同步自 " + srcLabel + " · 读取于 " + TimeFormatters.FormatUtc8(acct.ReadAtUtc.Value, true))
                    : ("数据源: " + srcLabel);
                TextBlock footerLeft = new TextBlock
                {
                    Text = readStr,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.0,
                    Foreground = LightPalette.InkMuted,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(footerLeft, Dock.Left);
                footerRow.Children.Add(footerLeft);
                col.Children.Add(footerRow);
            }

            card.Child = col;
            return card;
        }

        private UIElement BuildMagpiePoolRow(string title, CloudPoolQuota pool, Thickness margin)
        {
            Color c5 = QuotaMetrics.StatusColor(pool.Remaining5h);
            Color c7 = QuotaMetrics.StatusColor(pool.Remaining7d);

            Border subCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = LightPalette.FrozenBrush(Color.FromArgb(16, 0, 0, 0)),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(9.0),
                Padding = new Thickness(8, 7, 8, 7),
                Margin = margin
            };

            StackPanel v = new StackPanel { Orientation = Orientation.Vertical };

            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82.0) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock nameTb = new TextBlock
            {
                Text = title,
                FontFamily = LightPalette.UiFont,
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameTb, 0);
            topRow.Children.Add(nameTb);

            UIElement meter5h = BuildMiniPoolMeter("5h", pool.Remaining5h, c5);
            Grid.SetColumn(meter5h, 1);
            topRow.Children.Add(meter5h);

            UIElement meter7d = BuildMiniPoolMeter("7d", pool.Remaining7d, c7);
            Grid.SetColumn(meter7d, 3);
            topRow.Children.Add(meter7d);

            v.Children.Add(topRow);

            DockPanel resetRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
            if (pool.ResetsAt5hUtc.HasValue)
            {
                double wait5 = Math.Max(0.0, (pool.ResetsAt5hUtc.Value - _monitor.NowTickUtc).TotalSeconds);
                TextBlock r5Tb = new TextBlock
                {
                    Text = "5h 刷新: " + TimeFormatters.FormatDuration(wait5) + " (" + TimeFormatters.FormatUtc8(pool.ResetsAt5hUtc.Value, false) + ")",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.0,
                    Foreground = LightPalette.InkMuted
                };
                DockPanel.SetDock(r5Tb, Dock.Left);
                resetRow.Children.Add(r5Tb);
            }
            if (pool.ResetsAt7dUtc.HasValue)
            {
                double wait7 = Math.Max(0.0, (pool.ResetsAt7dUtc.Value - _monitor.NowTickUtc).TotalSeconds);
                TextBlock r7Tb = new TextBlock
                {
                    Text = "7d 刷新: " + TimeFormatters.FormatDuration(wait7),
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.0,
                    Foreground = LightPalette.InkMuted
                };
                DockPanel.SetDock(r7Tb, Dock.Right);
                resetRow.Children.Add(r7Tb);
            }
            v.Children.Add(resetRow);

            subCard.Child = v;
            return subCard;
        }

        private static UIElement BuildMiniPoolMeter(string label, double pct, Color barColor)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = LightPalette.MonoFont,
                FontSize = 9.5,
                Foreground = LightPalette.InkMuted,
                VerticalAlignment = VerticalAlignment.Center
            });

            Border track = new Border
            {
                Width = 36.0,
                Height = 4.5,
                CornerRadius = new CornerRadius(2.25),
                Background = LightPalette.FrozenBrush(Color.FromArgb(24, 0, 0, 0)),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            double fillW = Math.Max(3.0, 36.0 * (Math.Max(0.0, Math.Min(100.0, pct)) / 100.0));
            track.Child = new Border
            {
                Width = fillW,
                Height = 4.5,
                CornerRadius = new CornerRadius(2.25),
                Background = LightPalette.FrozenBrush(barColor),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            sp.Children.Add(track);

            sp.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "{0:0.0}%", pct),
                FontFamily = LightPalette.MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private UIElement BuildFallbackQuotaCard()
        {
            QuotaMetrics q = _monitor.Quota;
            double rem5h = q.FiveHourRemainingPercent;
            double remWk = q.WeeklyRemainingPercent;
            Color c5 = QuotaMetrics.StatusColor(rem5h);
            Color c7 = QuotaMetrics.StatusColor(remWk);

            Border card = MakeSectionCardContainer(new Thickness(10.5, 8.5, 10.5, 8.5));
            StackPanel col = new StackPanel { Orientation = Orientation.Vertical };

            Border sumHit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            DockPanel row = new DockPanel { LastChildFill = false };

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "5h {0:0}% · 7d {1:0}%", rem5h, remWk),
                FontFamily = LightPalette.MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.FrozenBrush(c5),
                Margin = new Thickness(0, 0, 6, 0)
            });
            right.Children.Add(VectorIcons.StrokedPath(
                _panelController.IsQuotaExpanded ? VectorIcons.ChevronUp : VectorIcons.ChevronDown,
                LightPalette.InkMuted,
                9.5,
                1.6));
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(VectorIcons.BoltCircleFill(LightPalette.FrozenBrush(c5), 13.0));
            left.Children.Add(new TextBlock
            {
                Text = "配额概览 · Gemini",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(6.5, 0, 0, 0)
            });
            Border modeBadge = new Border
            {
                Background = LightPalette.FrozenBrush(q.IsFiveHourSynced ? Color.FromArgb(24, 24, 119, 242) : Color.FromArgb(26, 245, 158, 11)),
                CornerRadius = new CornerRadius(4.0),
                Padding = new Thickness(4.5, 1.0, 4.5, 1.0),
                Margin = new Thickness(5.5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            modeBadge.Child = new TextBlock
            {
                Text = q.IsFiveHourSynced ? "剪贴板校准" : "本地估算",
                FontFamily = LightPalette.UiFont,
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = q.IsFiveHourSynced ? LightPalette.BrandBlue : LightPalette.WarnOrange
            };
            left.Children.Add(modeBadge);
            DockPanel.SetDock(left, Dock.Left);
            row.Children.Add(left);

            sumHit.Child = row;
            sumHit.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _panelController.IsQuotaExpanded = !_panelController.IsQuotaExpanded;
            };
            col.Children.Add(sumHit);

            if (_panelController.IsQuotaExpanded)
            {
                col.Children.Add(MakeDivider(new Thickness(0, 8, 0, 7)));

                string hintText;
                if (q.IsFiveHourSynced && q.FiveHourResetDateUtc.HasValue)
                {
                    double wait5 = Math.Max(0.0, (q.FiveHourResetDateUtc.Value - _monitor.NowTickUtc).TotalSeconds);
                    hintText = "5h 刷新: " + TimeFormatters.FormatDuration(wait5) + " (" + TimeFormatters.FormatUtc8(q.FiveHourResetDateUtc.Value, false) + ") · 登录 Google 可全自动实时同步";
                }
                else
                {
                    hintText = "尚未连接云端账号（当前为本地日志推算值，连接 Google 即刻同步官方真实 5h / 7d 配额）";
                }

                col.Children.Add(new TextBlock
                {
                    Text = hintText,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.5,
                    Foreground = LightPalette.InkSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 7)
                });

                DockPanel actionBar = new DockPanel { LastChildFill = false };

                Border pasteBtn = new Border
                {
                    Background = LightPalette.FrozenBrush(Color.FromArgb(15, 0, 0, 0)),
                    BorderBrush = LightPalette.FrozenBrush(Color.FromArgb(24, 0, 0, 0)),
                    BorderThickness = new Thickness(0.8),
                    CornerRadius = new CornerRadius(7.0),
                    Padding = new Thickness(8.5, 4.5, 8.5, 4.5),
                    Cursor = Cursors.Hand,
                    ToolTip = "支持导入 1// Refresh Token、Magpie/CLIProxyAPI JSON，或复制官方 Usage 弹窗文字同步百分比"
                };
                pasteBtn.Child = new TextBlock
                {
                    Text = "📋 粘贴 Token / Usage",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 10.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LightPalette.InkPrimary
                };
                pasteBtn.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    await _monitor.SyncOrImportFromClipboardAsync();
                };
                DockPanel.SetDock(pasteBtn, Dock.Right);
                actionBar.Children.Add(pasteBtn);

                Border googleLoginBtn = new Border
                {
                    Background = _monitor.IsSigningInGoogle
                        ? LightPalette.WarnOrange
                        : LightPalette.BrandBlue,
                    CornerRadius = new CornerRadius(7.0),
                    Padding = new Thickness(10, 4.5, 10, 4.5),
                    Cursor = Cursors.Hand,
                    ToolTip = "使用 Magpie 同源 Google OAuth PKCE 协议一键授权，实时读取 Gemini 与 Claude/GPT 官方 5h/7d 配额"
                };
                googleLoginBtn.Child = new TextBlock
                {
                    Text = _monitor.IsSigningInGoogle ? "⏳ 等待浏览器授权… (取消)" : "🌐 Google 登录直连实盘配额",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 10.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };
                googleLoginBtn.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    await _monitor.SignInGoogleDirectAsync();
                };
                DockPanel.SetDock(googleLoginBtn, Dock.Left);
                actionBar.Children.Add(googleLoginBtn);

                col.Children.Add(actionBar);
            }

            card.Child = col;
            return card;
        }

        // MARK: - Grouped Sessions List (`优先事项` / `今天` / `昨天` / `星期x`)

        private UIElement BuildSessionsListSection()
        {
            List<SessionGroupSection> sections = _monitor.GroupedSessionSections;
            if (sections.Count == 0)
            {
                Border emptyBox = new Border
                {
                    CornerRadius = new CornerRadius(12.0),
                    Background = LightPalette.RowIdleFillBrush,
                    BorderBrush = LightPalette.RowIdleBorderBrush,
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(16.0)
                };
                emptyBox.Child = new TextBlock
                {
                    Text = "最近 7 天暂无本地 Antigravity 会话记录",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 11.0,
                    Foreground = LightPalette.InkMuted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return emptyBox;
            }

            StackPanel listStack = new StackPanel { Orientation = Orientation.Vertical };

            for (int s = 0; s < sections.Count; s++)
            {
                SessionGroupSection section = sections[s];
                StackPanel secStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, s == 0 ? 0 : 10, 0, 0)
                };

                // Section Header
                DockPanel secHeader = new DockPanel { LastChildFill = false, Margin = new Thickness(3, 0, 3, 5.5) };
                if (section.IsPriority && _monitor.PinnedConversationIds.Count > 0)
                {
                    TextBlock clearPinsBtn = new TextBlock
                    {
                        Text = "取消全部固定 (" + _monitor.PinnedConversationIds.Count + ")",
                        FontFamily = LightPalette.UiFont,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Medium,
                        Foreground = LightPalette.BrandBlue,
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    clearPinsBtn.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        _monitor.ClearAllPins();
                    };
                    DockPanel.SetDock(clearPinsBtn, Dock.Right);
                    secHeader.Children.Add(clearPinsBtn);
                }

                StackPanel secLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (section.IsPriority)
                {
                    secLeft.Children.Add(VectorIcons.FilledPath(VectorIcons.Bolt, LightPalette.BrandBlue, 9.5));
                }
                secLeft.Children.Add(new TextBlock
                {
                    Text = section.Title,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = section.IsPriority ? LightPalette.BrandBlue : LightPalette.InkMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(section.IsPriority ? 4 : 0, 0, 5, 0)
                });
                secLeft.Children.Add(new TextBlock
                {
                    Text = section.Conversations.Count.ToString(CultureInfo.InvariantCulture),
                    FontFamily = LightPalette.MonoFont,
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = section.IsPriority ? LightPalette.BrandBlue : LightPalette.InkMuted,
                    VerticalAlignment = VerticalAlignment.Center
                });
                DockPanel.SetDock(secLeft, Dock.Left);
                secHeader.Children.Add(secLeft);
                secStack.Children.Add(secHeader);

                for (int c = 0; c < section.Conversations.Count; c++)
                {
                    UIElement row = BuildSessionRow(section.Conversations[c]);
                    if (c > 0) ((FrameworkElement)row).Margin = new Thickness(0, 5.5, 0, 0);
                    secStack.Children.Add(row);
                }

                listStack.Children.Add(secStack);
            }

            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = listStack
            };
            return sv;
        }

        private UIElement BuildSessionRow(ConversationSnapshot conv)
        {
            bool isPinned = _monitor.IsPinned(conv.Id);
            bool isUnreadDone = _monitor.IsUnreadCompleted(conv.Id);
            bool isConfirmingDelete = string.Equals(_panelController.ConfirmingDeleteConvId, conv.Id, StringComparison.Ordinal);
            string liveLabel = conv.Phase.LiveDisplayLabel;
            bool isDefaultProj = string.Equals(conv.ProjectName, "default", StringComparison.OrdinalIgnoreCase);

            Brush rowFill = liveLabel != null
                ? LightPalette.RowActiveFillBrush
                : (isUnreadDone ? LightPalette.RowDoneFillBrush : LightPalette.RowIdleFillBrush);

            Brush rowStroke;
            if (isPinned)
                rowStroke = LightPalette.FrozenBrush(Color.FromArgb(84, LightPalette.PinBlueColor.R, LightPalette.PinBlueColor.G, LightPalette.PinBlueColor.B));
            else if (liveLabel != null)
                rowStroke = LightPalette.FrozenBrush(Color.FromArgb(84, conv.Phase.AccentColor.R, conv.Phase.AccentColor.G, conv.Phase.AccentColor.B));
            else if (isUnreadDone)
                rowStroke = LightPalette.FrozenBrush(Color.FromArgb(90, LightPalette.DoneGreenColor.R, LightPalette.DoneGreenColor.G, LightPalette.DoneGreenColor.B));
            else
                rowStroke = LightPalette.RowIdleBorderBrush;

            Border card = new Border
            {
                CornerRadius = new CornerRadius(10.5),
                Background = rowFill,
                BorderBrush = rowStroke,
                BorderThickness = new Thickness(0.85),
                Padding = new Thickness(10.0, 7.5, 10.0, 7.5)
            };

            Grid rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Main Jump Button Area
            Border jumpHit = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "点击在 Antigravity 中打开此会话"
            };

            Grid leftContent = new Grid();
            leftContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            leftContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (liveLabel != null)
            {
                Ellipse dot = new Ellipse
                {
                    Width = 7.5,
                    Height = 7.5,
                    Fill = LightPalette.FrozenBrush(conv.Phase.AccentColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(dot, 0);
                leftContent.Children.Add(dot);
            }
            else if (isUnreadDone)
            {
                UIElement chk = VectorIcons.CheckCircleFill(LightPalette.DoneGreen, 11.5);
                ((FrameworkElement)chk).Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(chk, 0);
                leftContent.Children.Add(chk);
            }

            StackPanel textCol = new StackPanel { Orientation = Orientation.Vertical };
            textCol.Children.Add(new TextBlock
            {
                Text = conv.Title,
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            WrapPanel metaRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2.5, 0, 0) };
            if (liveLabel != null)
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = liveLabel,
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.8,
                    FontWeight = FontWeights.Medium,
                    Foreground = LightPalette.FrozenBrush(conv.Phase.AccentColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                });
                if (conv.Phase.IsWorking && conv.ActiveTurnStartedAtUtc.HasValue)
                {
                    double elapsed = Math.Max(0.0, (_monitor.NowTickUtc - conv.ActiveTurnStartedAtUtc.Value).TotalSeconds);
                    TextBlock durTb = new TextBlock
                    {
                        Text = "· " + TimeFormatters.FormatDuration(elapsed),
                        Tag = "· ",
                        FontFamily = LightPalette.MonoFont,
                        FontSize = 9.5,
                        Foreground = LightPalette.InkSecondary,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0)
                    };
                    _liveElapsedLabels.Add(new KeyValuePair<TextBlock, DateTime>(durTb, conv.ActiveTurnStartedAtUtc.Value));
                    metaRow.Children.Add(durTb);
                }
                metaRow.Children.Add(new TextBlock
                {
                    Text = "· " + TimeFormatters.FormatUtc8(conv.LastUpdatedAtUtc, false),
                    FontFamily = LightPalette.MonoFont,
                    FontSize = 9.5,
                    Foreground = LightPalette.InkMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
            }
            else
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = TimeFormatters.FormatUtc8(conv.LastUpdatedAtUtc, false),
                    FontFamily = LightPalette.MonoFont,
                    FontSize = 9.5,
                    Foreground = LightPalette.InkMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
            }

            // Project Badge
            Border projBadge = new Border
            {
                Background = isDefaultProj
                    ? LightPalette.FrozenBrush(Color.FromArgb(14, 0, 0, 0))
                    : LightPalette.FrozenBrush(Color.FromArgb(28, LightPalette.BrandBlueColor.R, LightPalette.BrandBlueColor.G, LightPalette.BrandBlueColor.B)),
                CornerRadius = new CornerRadius(4.0),
                Padding = new Thickness(5.5, 1.0, 5.5, 1.0),
                MaxWidth = 135.0,
                VerticalAlignment = VerticalAlignment.Center
            };
            projBadge.Child = new TextBlock
            {
                Text = conv.ProjectName,
                FontFamily = LightPalette.MonoFont,
                FontSize = 9.2,
                FontWeight = FontWeights.Medium,
                Foreground = isDefaultProj ? LightPalette.InkMuted : LightPalette.BrandBlue,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            metaRow.Children.Add(projBadge);

            textCol.Children.Add(metaRow);
            Grid.SetColumn(textCol, 1);
            leftContent.Children.Add(textCol);

            jumpHit.Child = leftContent;
            ConversationSnapshot captured = conv;
            jumpHit.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _panelController.ClearDeleteConfirmation();
                _monitor.JumpToConversation(captured);
            };
            Grid.SetColumn(jumpHit, 0);
            rowGrid.Children.Add(jumpHit);

            // Pin Button
            Border pinBtn = new Border
            {
                Width = 22.0,
                Height = 22.0,
                CornerRadius = new CornerRadius(11.0),
                Background = isPinned
                    ? LightPalette.FrozenBrush(Color.FromArgb(34, LightPalette.PinBlueColor.R, LightPalette.PinBlueColor.G, LightPalette.PinBlueColor.B))
                    : LightPalette.FrozenBrush(Color.FromArgb(12, 0, 0, 0)),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = isPinned ? "取消固定此会话" : "固定到优先事项（支持同时固定多个）",
                Child = VectorIcons.FilledPath(VectorIcons.PinFill, isPinned ? LightPalette.PinBlue : LightPalette.InkMuted, 10.0)
            };
            pinBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _panelController.ClearDeleteConfirmation();
                _monitor.TogglePin(captured);
            };
            Grid.SetColumn(pinBtn, 1);
            rowGrid.Children.Add(pinBtn);

            // 2-Tap Delete Button
            Border delBtn;
            if (isConfirmingDelete)
            {
                delBtn = new Border
                {
                    Height = 22.0,
                    CornerRadius = new CornerRadius(11.0),
                    Background = LightPalette.DangerRed,
                    Padding = new Thickness(6.5, 0, 6.5, 0),
                    Margin = new Thickness(6, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "再次点击同步删除 Antigravity 与助手两边会话"
                };
                StackPanel delContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                delContent.Children.Add(VectorIcons.StrokedPath(VectorIcons.Trash, Brushes.White, 9.5, 1.4));
                delContent.Children.Add(new TextBlock
                {
                    Text = "删?",
                    FontFamily = LightPalette.UiFont,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(2.5, 0, 0, 0)
                });
                delBtn.Child = delContent;
            }
            else
            {
                delBtn = new Border
                {
                    Width = 22.0,
                    Height = 22.0,
                    CornerRadius = new CornerRadius(11.0),
                    Background = LightPalette.FrozenBrush(Color.FromArgb(12, 0, 0, 0)),
                    Margin = new Thickness(6, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "删除两边会话",
                    Child = VectorIcons.StrokedPath(VectorIcons.Trash, LightPalette.InkMuted, 10.5, 1.3)
                };
            }
            delBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (isConfirmingDelete)
                {
                    _panelController.ClearDeleteConfirmation();
                    _monitor.DeleteConversationBothSides(captured);
                }
                else
                {
                    _panelController.RequestDeleteConfirmation(captured.Id);
                }
            };
            Grid.SetColumn(delBtn, 2);
            rowGrid.Children.Add(delBtn);

            card.Child = rowGrid;
            return card;
        }

        // MARK: - 3. Settings View (`提醒与设置`)

        private UIElement BuildSettingsView()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8.0) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top Nav Row
            DockPanel navRow = new DockPanel { LastChildFill = false, Margin = new Thickness(2, 0, 2, 0) };
            TextBlock rightTitle = new TextBlock
            {
                Text = "提醒与设置",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(rightTitle, Dock.Right);
            navRow.Children.Add(rightTitle);

            Border backBtn = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            StackPanel backSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            backSp.Children.Add(VectorIcons.StrokedPath(VectorIcons.ChevronLeft, LightPalette.BrandBlue, 10.0, 1.8));
            backSp.Children.Add(new TextBlock
            {
                Text = "返回主面板",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.BrandBlue,
                Margin = new Thickness(4, 0, 0, 0)
            });
            backBtn.Child = backSp;
            backBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _panelController.ShowSettings = false;
            };
            DockPanel.SetDock(backBtn, Dock.Left);
            navRow.Children.Add(backBtn);

            Grid.SetRow(navRow, 0);
            g.Children.Add(navRow);

            // Scrollable Cards
            StackPanel cardsCol = new StackPanel { Orientation = Orientation.Vertical };

            // Card 0: Direct Google Cloud Quota (Magpie Protocol)
            Border card0 = MakeSectionCardContainer(new Thickness(11.0));
            card0.Margin = new Thickness(0, 0, 0, 10);
            StackPanel c0 = new StackPanel { Orientation = Orientation.Vertical };
            c0.Children.Add(new TextBlock
            {
                Text = "实盘配额直连 (Magpie 同源 Google 协议)",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(0, 0, 0, 4)
            });
            c0.Children.Add(new TextBlock
            {
                Text = "内置 Google Code Assist 官方配额引擎（同时兼容本地 Magpie 网关），无需中转即可每分钟自动同步 Gemini 与 Claude/GPT 的 5h / 7d 精确剩余额度。凭证经 Windows DPAPI 本机加密保存。",
                FontFamily = LightPalette.UiFont,
                FontSize = 9.5,
                Foreground = LightPalette.InkMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            if (_monitor.CloudAccounts.Count > 0)
            {
                for (int i = 0; i < _monitor.CloudAccounts.Count; i++)
                {
                    CloudAccountQuota item = _monitor.CloudAccounts[i];
                    Border acctRowBox = new Border
                    {
                        Background = Brushes.White,
                        BorderBrush = LightPalette.FrozenBrush(Color.FromArgb(18, 0, 0, 0)),
                        BorderThickness = new Thickness(0.8),
                        CornerRadius = new CornerRadius(8.0),
                        Padding = new Thickness(8, 5.5, 8, 5.5),
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    DockPanel acctRow = new DockPanel { LastChildFill = false };
                    if (item.IsDirectGoogleAccount)
                    {
                        string emailToRemove = item.UserEmail;
                        Border removeBtn = MakePillOutlineButton("退出", delegate
                        {
                            Task ignored = _monitor.SignOutGoogleAccountAsync(emailToRemove);
                        });
                        removeBtn.Padding = new Thickness(7, 2, 7, 2);
                        DockPanel.SetDock(removeBtn, Dock.Right);
                        acctRow.Children.Add(removeBtn);
                    }

                    StackPanel acctInfo = new StackPanel { Orientation = Orientation.Vertical };
                    acctInfo.Children.Add(new TextBlock
                    {
                        Text = item.UserEmail + " · " + item.PlanName,
                        FontFamily = LightPalette.UiFont,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = LightPalette.InkPrimary
                    });
                    acctInfo.Children.Add(new TextBlock
                    {
                        Text = "来源: " + (!string.IsNullOrEmpty(item.SourceName) ? item.SourceName : "Google 官方直连"),
                        FontFamily = LightPalette.UiFont,
                        FontSize = 9.0,
                        Foreground = LightPalette.InkMuted
                    });
                    DockPanel.SetDock(acctInfo, Dock.Left);
                    acctRow.Children.Add(acctInfo);
                    acctRowBox.Child = acctRow;
                    c0.Children.Add(acctRowBox);
                }
            }

            StackPanel authBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            Border loginPrimaryBtn = new Border
            {
                Background = _monitor.IsSigningInGoogle ? LightPalette.WarnOrange : LightPalette.BrandBlue,
                CornerRadius = new CornerRadius(7.0),
                Padding = new Thickness(10, 4.5, 10, 4.5),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            loginPrimaryBtn.Child = new TextBlock
            {
                Text = _monitor.IsSigningInGoogle ? "⏳ 取消授权等待" : "🌐 Google 浏览器登录",
                FontFamily = LightPalette.UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            loginPrimaryBtn.MouseLeftButtonUp += async delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                await _monitor.SignInGoogleDirectAsync();
            };
            authBtnRow.Children.Add(loginPrimaryBtn);

            Border importClipBtn = MakePillOutlineButton("📋 粘贴 Token / Usage", delegate
            {
                Task ignored = _monitor.SyncOrImportFromClipboardAsync();
            });
            authBtnRow.Children.Add(importClipBtn);

            c0.Children.Add(authBtnRow);
            card0.Child = c0;
            cardsCol.Children.Add(card0);

            // Card 1: Alerts & Sounds
            Border card1 = MakeSectionCardContainer(new Thickness(11.0));
            StackPanel c1 = new StackPanel { Orientation = Orientation.Vertical };
            c1.Children.Add(new TextBlock
            {
                Text = "状态与 Remaining 提醒",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });

            c1.Children.Add(MakeSettingToggleRow("任务回复完成时横幅提醒", _settings.NotifyOnComplete, delegate(bool v) { _settings.NotifyOnComplete = v; }));
            c1.Children.Add(MakeDurationStepperRow());
            c1.Children.Add(MakeSettingToggleRow("等待确认 / 执行异常立即提醒", _settings.NotifyOnWaitingOrError, delegate(bool v) { _settings.NotifyOnWaitingOrError = v; }));
            c1.Children.Add(MakeSettingToggleRow("低 Remaining 配额预警", _settings.NotifyOnLowRemaining, delegate(bool v) { _settings.NotifyOnLowRemaining = v; }));

            // Sound row with Preview button
            DockPanel soundRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
            Border previewBtn = MakePillOutlineButton("🔊 试听", delegate { NotificationManager.Shared.PlaySound("Glass", true); });
            DockPanel.SetDock(previewBtn, Dock.Right);
            soundRow.Children.Add(previewBtn);

            StackPanel soundLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            soundLeft.Children.Add(new TextBlock
            {
                Text = "提示音效",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.2,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            soundLeft.Children.Add(new ToggleSwitchControl(_settings.SoundEnabled, delegate(bool v) { _settings.SoundEnabled = v; }));
            DockPanel.SetDock(soundLeft, Dock.Left);
            soundRow.Children.Add(soundLeft);
            c1.Children.Add(soundRow);

            card1.Child = c1;
            cardsCol.Children.Add(card1);

            // Card 2: UI Automation, Lifecycle Hooks, Startup & Window Controls
            Border card2 = MakeSectionCardContainer(new Thickness(11.0));
            card2.Margin = new Thickness(0, 10, 0, 0);
            StackPanel c2 = new StackPanel { Orientation = Orientation.Vertical };
            c2.Children.Add(new TextBlock
            {
                Text = "会话精准跳转、Hooks 增强 & 窗口控制",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });

            StackPanel uiaTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            uiaTitleRow.Children.Add(VectorIcons.CheckCircleFill(LightPalette.DoneGreen, 13.0));
            uiaTitleRow.Children.Add(new TextBlock
            {
                Text = "会话定位与同步删除已就绪 (Windows UI Automation)",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(5.5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            c2.Children.Add(uiaTitleRow);
            c2.Children.Add(new TextBlock
            {
                Text = "点击任意会话可在 Antigravity.exe 内精准定位，点击删除可同步清理两边会话。",
                FontFamily = LightPalette.UiFont,
                FontSize = 9.5,
                Foreground = LightPalette.InkMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            c2.Children.Add(MakeDivider(new Thickness(0, 7, 0, 7)));

            // Lifecycle Hooks Row
            Grid hookRow = new Grid();
            hookRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hookRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel hookText = new StackPanel { Orientation = Orientation.Vertical };
            StackPanel hookTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (_monitor.IsHookPluginInstalled)
            {
                hookTitleRow.Children.Add(VectorIcons.CheckCircleFill(LightPalette.DoneGreen, 13.0));
            }
            hookTitleRow.Children.Add(new TextBlock
            {
                Text = _monitor.IsHookPluginInstalled ? "Lifecycle Hooks 已启用" : "Lifecycle Hooks 毫秒级直连 (可选)",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                Margin = new Thickness(_monitor.IsHookPluginInstalled ? 5.5 : 0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            hookText.Children.Add(hookTitleRow);
            hookText.Children.Add(new TextBlock
            {
                Text = "开启后自动写入 %LOCALAPPDATA%\\AgyAssistant，毫秒级捕获工具状态。",
                FontFamily = LightPalette.UiFont,
                FontSize = 9.5,
                Foreground = LightPalette.InkMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 8, 0)
            });
            Grid.SetColumn(hookText, 0);
            hookRow.Children.Add(hookText);

            Border hookBtn = MakePillOutlineButton(_monitor.IsHookPluginInstalled ? "移除" : "一键开启", delegate
            {
                _monitor.ToggleHookPluginInstallation();
            });
            hookBtn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hookBtn, 1);
            hookRow.Children.Add(hookBtn);
            c2.Children.Add(hookRow);

            c2.Children.Add(MakeDivider(new Thickness(0, 7, 0, 7)));

            // Launch at Windows Login Toggle
            c2.Children.Add(MakeSettingToggleRow("开机自动启动 (Windows 登录自启)", _settings.LaunchAtStartup, delegate(bool v)
            {
                _settings.LaunchAtStartup = v;
                _monitor.ShowToast(v ? "✅ 已开启 Windows 开机自启" : "已关闭开机自启");
            }));

            // Opacity Slider Row
            Grid opRow = new Grid { Margin = new Thickness(0, 6, 0, 8) };
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock opLabel = new TextBlock
            {
                Text = "面板不透明度",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.0,
                Foreground = LightPalette.InkSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(opLabel, 0);
            opRow.Children.Add(opLabel);

            Slider opSlider = new Slider
            {
                Minimum = 0.65,
                Maximum = 1.0,
                Value = _settings.PillOpacity,
                VerticalAlignment = VerticalAlignment.Center
            };
            opSlider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
            {
                _settings.PillOpacity = e.NewValue;
                if (_expandedRootCard != null) _expandedRootCard.Opacity = e.NewValue;
            };
            Grid.SetColumn(opSlider, 1);
            opRow.Children.Add(opSlider);
            c2.Children.Add(opRow);

            // Bottom Action Buttons: Reset Position & Quit
            DockPanel bottomBtns = new DockPanel { LastChildFill = false };
            Border quitBtn = new Border
            {
                Background = LightPalette.DangerRed,
                CornerRadius = new CornerRadius(7.0),
                Padding = new Thickness(12, 4.5, 12, 4.5),
                Cursor = Cursors.Hand
            };
            quitBtn.Child = new TextBlock
            {
                Text = "退出助手",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            quitBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (Application.Current != null) Application.Current.Shutdown();
            };
            DockPanel.SetDock(quitBtn, Dock.Right);
            bottomBtns.Children.Add(quitBtn);

            Border resetPosBtn = MakePillOutlineButton("重置悬浮位置", delegate { _panelController.ResetPositionToDefault(); });
            DockPanel.SetDock(resetPosBtn, Dock.Left);
            bottomBtns.Children.Add(resetPosBtn);
            c2.Children.Add(bottomBtns);

            card2.Child = c2;
            cardsCol.Children.Add(card2);

            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = cardsCol
            };
            Grid.SetRow(sv, 2);
            g.Children.Add(sv);

            return g;
        }

        private static UIElement MakeSettingToggleRow(string title, bool initialValue, Action<bool> onChanged)
        {
            DockPanel row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 7) };
            ToggleSwitchControl sw = new ToggleSwitchControl(initialValue, onChanged);
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);

            TextBlock tb = new TextBlock
            {
                Text = title,
                FontFamily = LightPalette.UiFont,
                FontSize = 11.2,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(tb, Dock.Left);
            row.Children.Add(tb);
            return row;
        }

        private UIElement MakeDurationStepperRow()
        {
            DockPanel row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 7) };

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock valTb = new TextBlock
            {
                Text = ((int)_settings.MinTurnDurationForAlert).ToString(CultureInfo.InvariantCulture) + "s",
                FontFamily = LightPalette.MonoFont,
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = LightPalette.InkPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            right.Children.Add(valTb);

            right.Children.Add(MakeTinyStepButton("-", delegate
            {
                _settings.MinTurnDurationForAlert = Math.Max(0.0, _settings.MinTurnDurationForAlert - 2.0);
                valTb.Text = ((int)_settings.MinTurnDurationForAlert).ToString(CultureInfo.InvariantCulture) + "s";
            }));
            Border plusBtn = MakeTinyStepButton("+", delegate
            {
                _settings.MinTurnDurationForAlert = Math.Min(60.0, _settings.MinTurnDurationForAlert + 2.0);
                valTb.Text = ((int)_settings.MinTurnDurationForAlert).ToString(CultureInfo.InvariantCulture) + "s";
            });
            plusBtn.Margin = new Thickness(3, 0, 0, 0);
            right.Children.Add(plusBtn);

            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            TextBlock label = new TextBlock
            {
                Text = "完成提醒最小时长过滤",
                FontFamily = LightPalette.UiFont,
                FontSize = 11.0,
                Foreground = LightPalette.InkSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            return row;
        }

        private static Border MakeTinyStepButton(string symbol, Action onClick)
        {
            Border b = new Border
            {
                Width = 20.0,
                Height = 18.0,
                CornerRadius = new CornerRadius(4.5),
                Background = LightPalette.FrozenBrush(Color.FromArgb(18, 0, 0, 0)),
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = symbol,
                FontFamily = LightPalette.MonoFont,
                FontSize = 11.0,
                FontWeight = FontWeights.Bold,
                Foreground = LightPalette.InkPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) onClick();
            };
            return b;
        }

        private static Border MakePillOutlineButton(string text, Action onClick)
        {
            Border b = new Border
            {
                Background = LightPalette.FrozenBrush(Color.FromArgb(16, 0, 0, 0)),
                BorderBrush = LightPalette.FrozenBrush(Color.FromArgb(24, 0, 0, 0)),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(7.0),
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = text,
                FontFamily = LightPalette.UiFont,
                FontSize = 10.8,
                FontWeight = FontWeights.Medium,
                Foreground = LightPalette.InkPrimary
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) onClick();
            };
            return b;
        }

        private static Border MakeSectionCardContainer(Thickness padding)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(13.0),
                Background = LightPalette.CardFillBrush,
                BorderBrush = LightPalette.CardBorderBrush,
                BorderThickness = new Thickness(0.8),
                Padding = padding
            };
        }

        private static Border MakeDivider(Thickness margin)
        {
            return new Border
            {
                Height = 1.0,
                Background = LightPalette.DividerBrush,
                Margin = margin
            };
        }

        private static UIElement BuildToastOverlay(string toast)
        {
            Border pill = new Border
            {
                Background = LightPalette.FrozenBrush(Color.FromArgb(230, 31, 38, 56)),
                CornerRadius = new CornerRadius(14.0),
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 18),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.22, Color = Colors.Black }
            };
            pill.Child = new TextBlock
            {
                Text = toast,
                FontFamily = LightPalette.UiFont,
                FontSize = 11.2,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White
            };
            return pill;
        }
    }
}
