using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AntigravityAssistant
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _wakeUpEvent;
        private static RegisteredWaitHandle _wakeUpWaitReg;
        private static System.Windows.Forms.NotifyIcon _trayIcon;
        private static System.Drawing.Icon _currentTrayIconHandle;
        private static AgentPhase _lastRenderedTrayPhase;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                AttachParentConsoleSafely();

                int exportIcoIdx = IndexOfArg(args, "--export-ico");
                if (exportIcoIdx >= 0 && exportIcoIdx + 1 < args.Length)
                {
                    try
                    {
                        string icoOut = Path.GetFullPath(args[exportIcoIdx + 1]);
                        PixelRobotSprite.ExportAppIco(icoOut);
                        Console.WriteLine("Exported Windows AppIcon.ico to " + icoOut);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Failed to export .ico: " + ex.Message);
                        return 1;
                    }
                }

                int demoIdx = IndexOfArg(args, "--export-demo-images");
                if (demoIdx >= 0 && demoIdx + 1 < args.Length)
                {
                    string outDir = Path.GetFullPath(args[demoIdx + 1]);
                    ExportDemoImages(outDir);
                    Console.WriteLine("Exported synthetic Windows demo images to " + outDir);
                    return 0;
                }

                int syncIdx = IndexOfArg(args, "--sync-text");
                if (syncIdx >= 0 && syncIdx + 1 < args.Length)
                {
                    AppSettings s = new AppSettings();
                    bool ok = s.SyncFromOfficialUsageText(args[syncIdx + 1]);
                    Console.WriteLine("Sync text result: " + ok);
                    return 0;
                }

                int jumpIdx = IndexOfArg(args, "--test-jump");
                if (jumpIdx >= 0 && jumpIdx + 1 < args.Length)
                {
                    string needle = args[jumpIdx + 1];
                    AppSettings s = new AppSettings();
                    TranscriptMonitor mon = new TranscriptMonitor(s);
                    mon.RefreshNow();
                    ConversationSnapshot target = null;
                    for (int i = 0; i < mon.Conversations.Count; i++)
                    {
                        ConversationSnapshot c = mon.Conversations[i];
                        if (c.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                            || (!string.IsNullOrEmpty(c.Title) && c.Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            target = c;
                            break;
                        }
                    }
                    if (target == null)
                    {
                        Console.WriteLine("No matching conversation for: " + needle);
                        return 1;
                    }
                    DateTime t0 = DateTime.UtcNow;
                    NavigationOutcome outcome = ConversationNavigator.JumpAsync(target).GetAwaiter().GetResult();
                    double sec = (DateTime.UtcNow - t0).TotalSeconds;
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "Jump to '{0}' [{1}] -> {2} in {3:0.000}s",
                        target.Title,
                        target.ProjectName,
                        outcome != null ? outcome.Kind.ToString() : "null",
                        sec));
                    return 0;
                }

                if (IndexOfArg(args, "--self-test") >= 0)
                {
                    RunSelfTest();
                    return 0;
                }
            }

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, @"Local\AntigravityFloatingAssistant_Singleton", out createdNew);
            if (!createdNew)
            {
                try
                {
                    using (EventWaitHandle existingWake = EventWaitHandle.OpenExisting(@"Local\AntigravityFloatingAssistant_WakeEvent"))
                    {
                        existingWake.Set();
                    }
                }
                catch
                {
                }
                if (Win32Native.WM_WAKEUP_INSTANCE != 0)
                {
                    Win32Native.PostMessageW(Win32Native.HWND_BROADCAST, Win32Native.WM_WAKEUP_INSTANCE, IntPtr.Zero, IntPtr.Zero);
                }
                return 0;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogUnhandledException("AppDomain", e.ExceptionObject as Exception);
            };

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                LogUnhandledException("Dispatcher", e.Exception);
                e.Handled = true;
            };

            AppSettings settings = new AppSettings();
            TranscriptMonitor monitor = new TranscriptMonitor(settings);
            PanelWindowController panelController = new PanelWindowController();

            monitor.RefreshNow();
            AssistantRootView rootView = new AssistantRootView(monitor, settings, panelController);

            panelController.SetupWindow(
                rootView,
                settings,
                monitor.PriorityConversations.Count,
                monitor.RunningPriorityConversations.Count);

            SetupSystemTrayIcon(monitor, settings, panelController);

            try
            {
                _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AntigravityFloatingAssistant_WakeEvent");
                _wakeUpWaitReg = ThreadPool.RegisterWaitForSingleObject(
                    _wakeUpEvent,
                    delegate(object state, bool timedOut)
                    {
                        if (!timedOut)
                        {
                            app.Dispatcher.BeginInvoke(new Action(delegate { panelController.ShowAndExpand(); }));
                        }
                    },
                    null,
                    -1,
                    false);
            }
            catch
            {
            }

            monitor.StateChanged += delegate
            {
                if (app.Dispatcher.CheckAccess())
                {
                    OnMonitorUpdated(monitor, panelController);
                }
                else
                {
                    app.Dispatcher.BeginInvoke(new Action(delegate { OnMonitorUpdated(monitor, panelController); }));
                }
            };

            OnMonitorUpdated(monitor, panelController);
            monitor.Start();

            app.Exit += delegate
            {
                monitor.Stop();
                if (_wakeUpWaitReg != null)
                {
                    try { _wakeUpWaitReg.Unregister(null); } catch { }
                }
                if (_wakeUpEvent != null)
                {
                    try { _wakeUpEvent.Dispose(); } catch { }
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_currentTrayIconHandle != null)
                {
                    _currentTrayIconHandle.Dispose();
                }
                if (_singleInstanceMutex != null)
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                }
            };

            return app.Run();
        }

        private static void OnMonitorUpdated(TranscriptMonitor monitor, PanelWindowController panelController)
        {
            panelController.UpdateCompactPriorityState(
                monitor.PriorityConversations.Count,
                monitor.RunningPriorityConversations.Count);

            UpdateTrayStatus(monitor);
        }

        private static void SetupSystemTrayIcon(
            TranscriptMonitor monitor,
            AppSettings settings,
            PanelWindowController panelController)
        {
            System.Windows.Forms.NotifyIcon tray = new System.Windows.Forms.NotifyIcon();
            _currentTrayIconHandle = PixelRobotSprite.MakeTrayIcon(AgentPhase.Idle);
            _lastRenderedTrayPhase = AgentPhase.Idle;
            tray.Icon = _currentTrayIconHandle;
            tray.Text = "Antigravity 使用助手";
            tray.Visible = true;

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();

            System.Windows.Forms.ToolStripMenuItem toggleItem = new System.Windows.Forms.ToolStripMenuItem("显示 / 隐藏桌面悬浮窗 (Alt+A)");
            toggleItem.Click += delegate { panelController.ToggleVisibility(); };
            menu.Items.Add(toggleItem);

            System.Windows.Forms.ToolStripMenuItem expandItem = new System.Windows.Forms.ToolStripMenuItem("展开详细 Remaining 面板");
            expandItem.Click += delegate { panelController.ShowAndExpand(); };
            menu.Items.Add(expandItem);

            System.Windows.Forms.ToolStripMenuItem googleLoginItem = new System.Windows.Forms.ToolStripMenuItem("🌐 Google 登录直连实盘配额");
            googleLoginItem.Click += async delegate
            {
                panelController.ShowAndExpand();
                await monitor.SignInGoogleDirectAsync();
            };
            menu.Items.Add(googleLoginItem);

            System.Windows.Forms.ToolStripMenuItem syncClipItem = new System.Windows.Forms.ToolStripMenuItem("📋 从剪贴板导入 Token / 同步 Usage");
            syncClipItem.Click += async delegate { await monitor.SyncOrImportFromClipboardAsync(); };
            menu.Items.Add(syncClipItem);

            System.Windows.Forms.ToolStripMenuItem openAgyItem = new System.Windows.Forms.ToolStripMenuItem("打开 Antigravity 当前会话");
            openAgyItem.Click += delegate { monitor.JumpToPrimaryConversation(); };
            menu.Items.Add(openAgyItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            System.Windows.Forms.ToolStripMenuItem startupItem = new System.Windows.Forms.ToolStripMenuItem("开机自动启动");
            startupItem.Checked = settings.LaunchAtStartup;
            startupItem.Click += delegate
            {
                bool next = !settings.LaunchAtStartup;
                settings.LaunchAtStartup = next;
                startupItem.Checked = next;
            };
            menu.Opening += delegate { startupItem.Checked = settings.LaunchAtStartup; };
            menu.Items.Add(startupItem);

            System.Windows.Forms.ToolStripMenuItem resetPosItem = new System.Windows.Forms.ToolStripMenuItem("重置悬浮窗到右上角");
            resetPosItem.Click += delegate { panelController.ResetPositionToDefault(); };
            menu.Items.Add(resetPosItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            System.Windows.Forms.ToolStripMenuItem quitItem = new System.Windows.Forms.ToolStripMenuItem("退出 Antigravity 使用助手");
            quitItem.Click += delegate
            {
                if (Application.Current != null) Application.Current.Shutdown();
            };
            menu.Items.Add(quitItem);

            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e)
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    if (!panelController.IsVisible)
                    {
                        panelController.ShowAndExpand();
                    }
                    else
                    {
                        panelController.ToggleExpanded();
                    }
                }
            };

            _trayIcon = tray;
            NotificationManager.Shared.AttachNotifyIcon(tray);
        }

        private static void UpdateTrayStatus(TranscriptMonitor monitor)
        {
            if (_trayIcon == null) return;
            ConversationSnapshot primary = monitor.PrimaryConversation;
            AgentPhase rawPhase = primary != null ? primary.Phase : AgentPhase.Idle;
            AgentPhase effectivePhase = (rawPhase != null && rawPhase.LiveDisplayLabel != null) ? rawPhase : AgentPhase.Idle;

            if (_lastRenderedTrayPhase != effectivePhase)
            {
                _lastRenderedTrayPhase = effectivePhase;
                System.Drawing.Icon nextIcon = PixelRobotSprite.MakeTrayIcon(effectivePhase);
                System.Drawing.Icon oldIcon = _currentTrayIconHandle;
                _currentTrayIconHandle = nextIcon;
                _trayIcon.Icon = nextIcon;
                if (oldIcon != null) oldIcon.Dispose();
            }

            string tip = string.Format(
                CultureInfo.InvariantCulture,
                "Antigravity 助手 · 5h {0:0.0}% · 7d {1:0.0}%",
                monitor.Quota.FiveHourRemainingPercent,
                monitor.Quota.WeeklyRemainingPercent);
            // NotifyIcon.Text max length on Windows is 63 chars
            if (tip.Length > 62) tip = tip.Substring(0, 62);
            try { _trayIcon.Text = tip; } catch { }
        }

        private static void LogUnhandledException(string source, Exception ex)
        {
            if (ex == null) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgyAssistant");
                Directory.CreateDirectory(dir);
                string logPath = Path.Combine(dir, "crash.log");
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] [" + source + "] " + ex.ToString() + Environment.NewLine;
                File.AppendAllText(logPath, line);
            }
            catch
            {
            }
        }

        // MARK: - CLI Self-Test & Demo PNG Renderer

        private static void RunSelfTest()
        {
            AppSettings settings = new AppSettings();
            TranscriptMonitor monitor = new TranscriptMonitor(settings);
            monitor.RefreshNow();
            monitor.PollMagpieQuotaAsync(false).GetAwaiter().GetResult();

            LiveAntigravityUIState liveState = ConversationNavigator.PollLiveUIState();
            if (liveState != null)
            {
                Console.WriteLine("Live Sidebar Items: " + liveState.SidebarItems.Count + " | Open Conv: " + (liveState.OpenConversationId ?? "none"));
            }

            List<SessionGroupSection> sections = monitor.GroupedSessionSections;
            for (int i = 0; i < sections.Count; i++)
            {
                SessionGroupSection sec = sections[i];
                List<string> rows = new List<string>();
                for (int j = 0; j < sec.Conversations.Count; j++)
                {
                    ConversationSnapshot c = sec.Conversations[j];
                    string live = c.Phase.LiveDisplayLabel;
                    if (!string.IsNullOrEmpty(live))
                        rows.Add(c.Title + " {" + c.ProjectName + "} [" + live + "]");
                    else
                        rows.Add(c.Title + " {" + c.ProjectName + "}");
                }
                Console.WriteLine("Group [" + sec.Title + "] (" + sec.Conversations.Count + "): " + string.Join(" | ", rows));
            }

            QuotaMetrics q = monitor.Quota;
            DateTime now = DateTime.UtcNow;
            string r5Str = q.FiveHourResetDateUtc.HasValue
                ? (TimeFormatters.FormatDuration((q.FiveHourResetDateUtc.Value - now).TotalSeconds) + " (" + TimeFormatters.FormatUtc8(q.FiveHourResetDateUtc.Value, false) + ")")
                : "idle";
            string rwStr = q.WeeklyResetDateUtc.HasValue
                ? (TimeFormatters.FormatDuration((q.WeeklyResetDateUtc.Value - now).TotalSeconds) + " (" + TimeFormatters.FormatDateTimeUtc8(q.WeeklyResetDateUtc.Value) + ")")
                : "idle";

            CloudAccountQuota activeAcct = monitor.ActiveCloudAccount;
            Console.WriteLine(
                "Self-Test Windows v1 | AntigravityExe: " + (ConversationNavigator.ResolveAntigravityExePath() ?? "not-found") +
                " | Cloud Accounts: " + monitor.CloudAccounts.Count +
                " | Active: " + (activeAcct != null ? activeAcct.UserEmail : "none") +
                " (" + (activeAcct != null ? activeAcct.PlanName : "-") + ")");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Gemini 5h Remaining: {0:0.0}% | Refresh: {1}", q.FiveHourRemainingPercent, r5Str));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Gemini 7d Remaining: {0:0.0}% | Refresh: {1}", q.WeeklyRemainingPercent, rwStr));
            if (activeAcct != null && activeAcct.ClaudeGptPool != null)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Claude & GPT: 5h {0:0.0}% | 7d {1:0.0}%",
                    activeAcct.ClaudeGptPool.Remaining5h,
                    activeAcct.ClaudeGptPool.Remaining7d));
            }
        }

        private static void ExportDemoImages(string outDir)
        {
            Directory.CreateDirectory(outDir);

            AppSettings settings = new AppSettings();
            double savedOp = settings.PillOpacity;
            settings.PillOpacity = 1.0;
            try
            {
                TranscriptMonitor monitor = new TranscriptMonitor(settings);
                monitor.LoadSyntheticDemoState();
                PanelWindowController panelCtrl = new PanelWindowController();
                panelCtrl.UpdateCompactPriorityState(
                    monitor.PriorityConversations.Count,
                    monitor.RunningPriorityConversations.Count);

                // 1. Compact Mascot
                AssistantRootView compactView = new AssistantRootView(monitor, settings, panelCtrl);
                SaveElementPng(compactView, 216, 240, Path.Combine(outDir, "demo-mascot-win.png"));

                // 2. Expanded Dashboard (Simulate IsExpanded via hidden setup window)
                FloatingAssistantWindow dummyWin = new FloatingAssistantWindow();
                panelCtrl.SetupWindow(new Border(), settings, monitor.PriorityConversations.Count, monitor.RunningPriorityConversations.Count);
                panelCtrl.SetExpanded(true);
                panelCtrl.IsQuotaExpanded = true;
                panelCtrl.ShowSettings = false;

                AssistantRootView dashView = new AssistantRootView(monitor, settings, panelCtrl);
                SaveElementPng(dashView, PanelWindowController.ExpandedSize.Width, PanelWindowController.ExpandedSize.Height, Path.Combine(outDir, "demo-dashboard-win.png"));

                // 3. Settings View
                panelCtrl.ShowSettings = true;
                AssistantRootView settingsView = new AssistantRootView(monitor, settings, panelCtrl);
                SaveElementPng(settingsView, PanelWindowController.ExpandedSize.Width, PanelWindowController.ExpandedSize.Height, Path.Combine(outDir, "demo-settings-win.png"));

                panelCtrl.ToggleVisibility();
                dummyWin.Close();
            }
            finally
            {
                settings.PillOpacity = savedOp;
            }
        }

        private static void SaveElementPng(FrameworkElement view, double width, double height, string filePath)
        {
            const double pad = 16.0;
            double totalW = width + pad * 2.0;
            double totalH = height + pad * 2.0;

            Border wrapper = new Border
            {
                Width = totalW,
                Height = totalH,
                Padding = new Thickness(pad),
                Background = LightPalette.FrozenBrush(Color.FromRgb(241, 244, 249)),
                Child = view
            };
            view.Width = width;
            view.Height = height;

            wrapper.Measure(new Size(totalW, totalH));
            wrapper.Arrange(new Rect(0, 0, totalW, totalH));
            wrapper.UpdateLayout();

            int pxW = (int)Math.Round(totalW * 2.0);
            int pxH = (int)Math.Round(totalH * 2.0);
            RenderTargetBitmap rtb = new RenderTargetBitmap(pxW, pxH, 192.0, 192.0, PixelFormats.Pbgra32);
            rtb.Render(wrapper);

            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }

        private static int IndexOfArg(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static void AttachParentConsoleSafely()
        {
            try
            {
                Win32Native.AttachConsole(Win32Native.ATTACH_PARENT_PROCESS);
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
            }
        }
    }
}
