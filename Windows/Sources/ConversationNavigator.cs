using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using Microsoft.Win32;

namespace AntigravityAssistant
{
    public enum NavigationOutcomeKind
    {
        AlreadyActive,
        JumpedViaDirectLink,
        JumpedViaPickerSearch,
        ActivatedAppOnly,
        AppNotFound
    }

    public sealed class NavigationOutcome
    {
        public NavigationOutcomeKind Kind { get; set; }
        public string Reason { get; set; }
    }

    public sealed class LiveSidebarItem : IEquatable<LiveSidebarItem>
    {
        public string ConversationId { get; set; }
        public string Title { get; set; }
        public bool IsRunning { get; set; }
        public bool IsIdleInUI { get; set; }

        public bool Equals(LiveSidebarItem other)
        {
            if (other == null) return false;
            return string.Equals(ConversationId, other.ConversationId, StringComparison.Ordinal)
                && string.Equals(Title, other.Title, StringComparison.Ordinal)
                && IsRunning == other.IsRunning
                && IsIdleInUI == other.IsIdleInUI;
        }
    }

    public sealed class LiveAntigravityUIState : IEquatable<LiveAntigravityUIState>
    {
        public Dictionary<string, LiveSidebarItem> SidebarItems = new Dictionary<string, LiveSidebarItem>(StringComparer.Ordinal);
        public string OpenConversationId;
        public bool OpenConversationCancelledBanner;
        public bool OpenConversationHasStopButton;

        public bool Equals(LiveAntigravityUIState other)
        {
            if (other == null) return false;
            if (!string.Equals(OpenConversationId, other.OpenConversationId, StringComparison.Ordinal)) return false;
            if (OpenConversationCancelledBanner != other.OpenConversationCancelledBanner) return false;
            if (OpenConversationHasStopButton != other.OpenConversationHasStopButton) return false;
            if (SidebarItems.Count != other.SidebarItems.Count) return false;
            foreach (KeyValuePair<string, LiveSidebarItem> kv in SidebarItems)
            {
                LiveSidebarItem oItem;
                if (!other.SidebarItems.TryGetValue(kv.Key, out oItem) || !kv.Value.Equals(oItem))
                {
                    return false;
                }
            }
            return true;
        }
    }

    // MARK: - Windows UI Automation Navigator & Session Controller for `Antigravity.exe`

    public static class ConversationNavigator
    {
        public static bool IsUIAutomationReady
        {
            get { return true; }
        }

        public static string ResolveAntigravityExePath()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("Antigravity");
                for (int i = 0; i < procs.Length; i++)
                {
                    try
                    {
                        string path = procs[i].MainModule != null ? procs[i].MainModule.FileName : null;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        procs[i].Dispose();
                    }
                }
            }
            catch
            {
            }

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] candidates = new string[]
            {
                Path.Combine(localApp, "Programs", "antigravity", "Antigravity.exe"),
                Path.Combine(localApp, "Programs", "Antigravity", "Antigravity.exe"),
                Path.Combine(progFiles, "Antigravity", "Antigravity.exe"),
                Path.Combine(progFilesX86, "Antigravity", "Antigravity.exe")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            // Check `antigravity://` URL protocol registry entry
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\antigravity\shell\open\command", false)
                    ?? Registry.ClassesRoot.OpenSubKey(@"antigravity\shell\open\command", false))
                {
                    if (key != null)
                    {
                        string cmd = key.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(cmd))
                        {
                            string extracted = ExtractQuotedExePath(cmd);
                            if (!string.IsNullOrEmpty(extracted) && File.Exists(extracted))
                            {
                                return extracted;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ExtractQuotedExePath(string cmd)
        {
            string trimmed = cmd.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = trimmed.IndexOf('"', 1);
                if (close > 1) return trimmed.Substring(1, close - 1);
            }
            int space = trimmed.IndexOf(' ');
            return space > 0 ? trimmed.Substring(0, space) : trimmed;
        }

        private static List<IntPtr> FindAntigravityWindowHandles()
        {
            List<IntPtr> handles = new List<IntPtr>();
            HashSet<uint> agyPids = new HashSet<uint>();

            try
            {
                Process[] procs = Process.GetProcessesByName("Antigravity");
                for (int i = 0; i < procs.Length; i++)
                {
                    agyPids.Add((uint)procs[i].Id);
                    procs[i].Dispose();
                }
            }
            catch
            {
            }

            if (agyPids.Count == 0) return handles;

            Win32Native.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint pid;
                Win32Native.GetWindowThreadProcessId(hWnd, out pid);
                if (!agyPids.Contains(pid)) return true;
                if (!Win32Native.IsWindowVisible(hWnd) && !Win32Native.IsIconic(hWnd)) return true;

                StringBuilder clsBuf = new StringBuilder(128);
                Win32Native.GetClassNameW(hWnd, clsBuf, clsBuf.Capacity);
                string cls = clsBuf.ToString();
                if (cls.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal))
                {
                    StringBuilder titleBuf = new StringBuilder(256);
                    Win32Native.GetWindowTextW(hWnd, titleBuf, titleBuf.Capacity);
                    if (titleBuf.Length > 0)
                    {
                        handles.Add(hWnd);
                    }
                }
                return true;
            }, IntPtr.Zero);

            return handles;
        }

        // MARK: - Live UI Polling via Windows UIAutomation

        public static LiveAntigravityUIState PollLiveUIState()
        {
            try
            {
                List<IntPtr> windows = FindAntigravityWindowHandles();
                if (windows.Count == 0) return null;

                LiveAntigravityUIState state = new LiveAntigravityUIState();
                for (int i = 0; i < windows.Count; i++)
                {
                    AutomationElement winEl = SafeFromHandle(windows[i]);
                    if (winEl == null) continue;

                    AutomationElement doc = FindHubDocument(winEl, 0);
                    if (doc == null) continue;

                    string docUrl = GetElementUrl(doc) ?? string.Empty;
                    string cid = ExtractConversationIdFromUrl(docUrl);
                    if (!string.IsNullOrEmpty(cid))
                    {
                        state.OpenConversationId = cid;
                    }

                    AutomationElement sidebar = FindSidebar(doc, 0);
                    if (sidebar != null)
                    {
                        CollectSidebarRows(sidebar, 0, state.SidebarItems);
                    }

                    if (!string.IsNullOrEmpty(state.OpenConversationId) && !state.SidebarItems.ContainsKey(state.OpenConversationId))
                    {
                        bool hasStop;
                        bool hasCancelled;
                        InspectOpenDocumentState(doc, out hasStop, out hasCancelled);
                        state.OpenConversationHasStopButton = hasStop;
                        state.OpenConversationCancelledBanner = hasCancelled;
                    }
                }

                return state;
            }
            catch
            {
                return null;
            }
        }

        // MARK: - Jump Directly to Conversation

        public static async Task<NavigationOutcome> JumpAsync(ConversationSnapshot conversation)
        {
            string exePath = ResolveAntigravityExePath();
            if (string.IsNullOrEmpty(exePath))
            {
                return new NavigationOutcome { Kind = NavigationOutcomeKind.AppNotFound };
            }

            IntPtr primaryHwnd = await ActivateOrLaunchAntigravityAsync(exePath);
            if (primaryHwnd == IntPtr.Zero)
            {
                return new NavigationOutcome
                {
                    Kind = NavigationOutcomeKind.ActivatedAppOnly,
                    Reason = "已唤醒 Antigravity，请在侧栏查看该会话"
                };
            }

            string targetPathFragment = "/c/" + conversation.Id;
            bool alreadyThere = await Task.Run(delegate { return IsViewingTargetConversation(targetPathFragment); });
            if (alreadyThere)
            {
                return new NavigationOutcome { Kind = NavigationOutcomeKind.AlreadyActive };
            }

            // Fast Path 1: Direct Sidebar Hyperlink scan (auto-uncollapses Sidebar if hidden)
            for (int attempt = 0; attempt < 3; attempt++)
            {
                TargetLinkScan scan = await Task.Run(delegate { return ScanForTargetLink(targetPathFragment); });
                if (scan.IsAlreadyViewingTarget)
                {
                    return new NavigationOutcome { Kind = NavigationOutcomeKind.AlreadyActive };
                }

                if (scan.MatchingLink != null)
                {
                    bool jumped = await InvokeAndVerifyAsync(scan.MatchingLink, targetPathFragment);
                    if (jumped)
                    {
                        return new NavigationOutcome { Kind = NavigationOutcomeKind.JumpedViaDirectLink };
                    }
                }

                if (scan.HasDocument && !scan.SidebarFound && attempt == 0)
                {
                    bool toggled = await Task.Run(delegate { return PressButtonInHub("Toggle Sidebar"); });
                    if (toggled)
                    {
                        await Task.Delay(150);
                        continue;
                    }
                }

                if (scan.SidebarFound)
                {
                    break;
                }
                await Task.Delay(80);
            }

            // Fast Path 2: Auto-expand collapsed Project / Conversations group in Sidebar (Antigravity 2.33+)
            bool expandedGroup = await ExpandSidebarSectionForProjectAsync(conversation.ProjectName);
            if (expandedGroup)
            {
                TargetLinkScan postExpandScan = await Task.Run(delegate { return ScanForTargetLink(targetPathFragment); });
                if (postExpandScan.IsAlreadyViewingTarget)
                {
                    return new NavigationOutcome { Kind = NavigationOutcomeKind.AlreadyActive };
                }
                if (postExpandScan.MatchingLink != null)
                {
                    bool jumped = await InvokeAndVerifyAsync(postExpandScan.MatchingLink, targetPathFragment);
                    if (jumped)
                    {
                        return new NavigationOutcome { Kind = NavigationOutcomeKind.JumpedViaDirectLink };
                    }
                }
            }

            // Fallback Path: Older conversation in deep history via Search Conversations / `Ctrl + K` FastPick
            bool pickerOk = await NavigateViaPickerAsync(primaryHwnd, conversation, targetPathFragment);
            if (pickerOk)
            {
                return new NavigationOutcome { Kind = NavigationOutcomeKind.JumpedViaPickerSearch };
            }

            return new NavigationOutcome
            {
                Kind = NavigationOutcomeKind.ActivatedAppOnly,
                Reason = "已切回 Antigravity，请在侧栏或 Ctrl+K 中查看该会话"
            };
        }

        public static async Task<IntPtr> ActivateOrLaunchAntigravityAsync(string exePath)
        {
            List<IntPtr> existing = FindAntigravityWindowHandles();
            if (existing.Count > 0)
            {
                Win32Native.ForceActivateWindow(existing[0]);
                return existing[0];
            }

            // Try `antigravity://focus` protocol first (wakes background tray instance cleanly)
            try
            {
                ProcessStartInfo psiUri = new ProcessStartInfo("antigravity://focus");
                psiUri.UseShellExecute = true;
                Process.Start(psiUri);
            }
            catch
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    try { Process.Start(exePath); } catch { }
                }
            }

            for (int i = 0; i < 18; i++)
            {
                await Task.Delay(140);
                List<IntPtr> wins = FindAntigravityWindowHandles();
                if (wins.Count > 0)
                {
                    Win32Native.ForceActivateWindow(wins[0]);
                    await Task.Delay(160);
                    return wins[0];
                }
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try { Process.Start(exePath); } catch { }
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(160);
                    List<IntPtr> wins = FindAntigravityWindowHandles();
                    if (wins.Count > 0)
                    {
                        Win32Native.ForceActivateWindow(wins[0]);
                        return wins[0];
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static async Task<bool> InvokeAndVerifyAsync(AutomationElement link, string targetPathFragment)
        {
            bool invoked = await Task.Run(delegate
            {
                TryScrollIntoView(link);
                return TryInvokeElement(link);
            });
            if (!invoked) return false;

            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(65);
                bool viewing = await Task.Run(delegate { return IsViewingTargetConversation(targetPathFragment); });
                if (viewing) return true;
            }
            return false;
        }

        private static async Task<bool> ExpandSidebarSectionForProjectAsync(string projectName)
        {
            List<IntPtr> windows = FindAntigravityWindowHandles();
            for (int w = 0; w < windows.Count; w++)
            {
                IntPtr winHandle = windows[w];
                AutomationElement sidebar = await Task.Run(delegate
                {
                    AutomationElement winEl = SafeFromHandle(winHandle);
                    if (winEl == null) return null;
                    AutomationElement doc = FindHubDocument(winEl, 0);
                    return doc != null ? FindSidebar(doc, 0) : null;
                });
                if (sidebar == null) continue;

                bool didExpandAny = false;
                List<KeyValuePair<AutomationElement, string>> buttons = await Task.Run(delegate { return FindSidebarButtons(sidebar); });

                if (string.IsNullOrEmpty(projectName) || string.Equals(projectName, "default", StringComparison.OrdinalIgnoreCase))
                {
                    AutomationElement convBtn = FindNamedButton(buttons, "Conversations", false);
                    if (convBtn != null && GetElementExpandedState(convBtn) == false)
                    {
                        bool ok = await Task.Run(delegate
                        {
                            TryScrollIntoView(convBtn);
                            return TryInvokeOrExpandElement(convBtn);
                        });
                        if (ok)
                        {
                            didExpandAny = true;
                            await Task.Delay(140);
                        }
                    }
                }
                else
                {
                    // 1. Ensure "Projects" top group is expanded
                    AutomationElement projRoot = FindNamedButton(buttons, "Projects", false);
                    if (projRoot != null && GetElementExpandedState(projRoot) == false)
                    {
                        bool ok = await Task.Run(delegate
                        {
                            TryScrollIntoView(projRoot);
                            return TryInvokeOrExpandElement(projRoot);
                        });
                        if (ok)
                        {
                            didExpandAny = true;
                            await Task.Delay(130);
                            buttons = await Task.Run(delegate { return FindSidebarButtons(sidebar); });
                        }
                    }

                    // 2. If target project isn't visible yet and "See all (...)" exists, expand project list
                    if (FindNamedButton(buttons, projectName, true) == null)
                    {
                        AutomationElement seeAll = FindPrefixButton(buttons, "See all");
                        if (seeAll != null)
                        {
                            bool ok = await Task.Run(delegate
                            {
                                TryScrollIntoView(seeAll);
                                return TryInvokeOrExpandElement(seeAll);
                            });
                            if (ok)
                            {
                                didExpandAny = true;
                                await Task.Delay(130);
                                buttons = await Task.Run(delegate { return FindSidebarButtons(sidebar); });
                            }
                        }
                    }

                    // 3. Expand the specific project folder
                    AutomationElement projBtn = FindNamedButton(buttons, projectName, true);
                    if (projBtn != null && GetElementExpandedState(projBtn) == false)
                    {
                        bool ok = await Task.Run(delegate
                        {
                            TryScrollIntoView(projBtn);
                            return TryInvokeOrExpandElement(projBtn);
                        });
                        if (ok)
                        {
                            didExpandAny = true;
                            await Task.Delay(150);
                        }
                    }
                }

                if (didExpandAny)
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task<bool> NavigateViaPickerAsync(IntPtr hwnd, ConversationSnapshot conv, string targetFragment)
        {
            if (Win32Native.GetForegroundWindow() != hwnd)
            {
                Win32Native.ForceActivateWindow(hwnd);
                await Task.Delay(70);
            }

            bool alreadyViewing = await Task.Run(delegate { return IsViewingTargetConversation(targetFragment); });
            if (alreadyViewing) return true;

            // Prefer clicking toolbar "Search Conversations" button if present, else Ctrl+K
            bool clickedSearchBtn = await Task.Run(delegate { return PressButtonInHub("Search Conversations"); });
            if (!clickedSearchBtn)
            {
                Win32Native.ForceActivateWindow(hwnd);
                Win32Native.SendKeyShortcut(Win32Native.VK_K, true);
            }
            await Task.Delay(200);

            // Check if the target conversation link is already directly visible in the opened picker list
            TargetLinkScan pickerScan = await Task.Run(delegate { return ScanForTargetLink(targetFragment); });
            if (pickerScan.IsAlreadyViewingTarget) return true;
            if (pickerScan.MatchingLink != null)
            {
                bool jumped = await InvokeAndVerifyAsync(pickerScan.MatchingLink, targetFragment);
                if (jumped) return true;
            }

            string query = CleanPickerQuery(conv);
            if (string.IsNullOrEmpty(query))
            {
                Win32Native.SendKeyShortcut(Win32Native.VK_ESCAPE, false);
                return false;
            }

            string pickerHint = await Task.Run(delegate { return GetActivePickerHint(); });
            if (pickerHint == null && !pickerScan.IsConvoPickerOpen)
            {
                return false;
            }

            Win32Native.SendKeyShortcut((ushort)Win32Native.VK_A, true);
            await Task.Delay(25);

            string fullQuery = (pickerHint != null && pickerHint.IndexOf("commands", StringComparison.OrdinalIgnoreCase) >= 0)
                ? ("conversations: " + query)
                : query;
            Win32Native.SendUnicodeString(fullQuery);
            await Task.Delay(240);

            Win32Native.SendKeyShortcut(Win32Native.VK_RETURN, false);
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(70);
                bool ok = await Task.Run(delegate { return IsViewingTargetConversation(targetFragment); });
                if (ok) return true;
            }

            TargetLinkScan postScan = await Task.Run(delegate { return ScanForTargetLink(targetFragment); });
            if (postScan.IsAlreadyViewingTarget) return true;
            if (postScan.IsConvoPickerOpen)
            {
                Win32Native.SendKeyShortcut(Win32Native.VK_ESCAPE, false);
            }
            return false;
        }

        private static string CleanPickerQuery(ConversationSnapshot conv)
        {
            string raw = !string.IsNullOrEmpty(conv.Title) ? conv.Title : (conv.SearchQuery ?? string.Empty);
            string stripped = raw.Replace("…", string.Empty).Trim();
            return stripped.Length > 28 ? stripped.Substring(0, 28) : stripped;
        }

        // MARK: - Delete Conversation Inside Antigravity UI

        public static async Task<bool> DeleteConversationInAntigravityAsync(string conversationId)
        {
            List<IntPtr> windows = FindAntigravityWindowHandles();
            if (windows.Count == 0) return false;

            for (int w = 0; w < windows.Count; w++)
            {
                IntPtr winHandle = windows[w];
                AutomationElement winEl = SafeFromHandle(winHandle);
                if (winEl == null) continue;

                AutomationElement doc = FindHubDocument(winEl, 0);
                if (doc == null) continue;
                AutomationElement sidebar = FindSidebar(doc, 0);
                if (sidebar == null) continue;

                AutomationElement rowGroup = FindSidebarRowContainer(sidebar, conversationId, 0);
                if (rowGroup == null) continue;

                // 1. Stop execution if running
                AutomationElement stopBtn = FindElementInSubtree(rowGroup, ControlType.Button, new HashSet<string> { "Stop execution" }, 6);
                if (stopBtn != null)
                {
                    TryInvokeElement(stopBtn);
                    await Task.Delay(220);
                }

                // 2. Open "More options" button
                AutomationElement moreBtn = FindElementInSubtree(rowGroup, null, new HashSet<string> { "More options" }, 6);
                if (moreBtn == null) continue;
                TryScrollIntoView(moreBtn);
                if (!TryInvokeOrExpandElement(moreBtn)) continue;

                // 3. Find Delete menu item
                AutomationElement deleteItem = null;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    await Task.Delay(70);
                    deleteItem = FindDeleteMenuItem(winEl);
                    if (deleteItem != null) break;
                }
                if (deleteItem == null || !TryInvokeElement(deleteItem))
                {
                    Win32Native.SendKeyShortcut(Win32Native.VK_ESCAPE, false);
                    continue;
                }

                // 4. Confirm modal Delete button
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    await Task.Delay(75);
                    AutomationElement confirmBtn = FindModalDeleteConfirmButton(doc) ?? FindModalDeleteConfirmButton(winEl);
                    if (confirmBtn != null && TryInvokeElement(confirmBtn))
                    {
                        return true;
                    }
                }
                return true;
            }
            return false;
        }

        // MARK: - Low-Level UIAutomation Tree Helpers

        private sealed class TargetLinkScan
        {
            public bool HasDocument;
            public bool SidebarFound;
            public bool IsAlreadyViewingTarget;
            public bool IsConvoPickerOpen;
            public AutomationElement MatchingLink;
        }

        private static TargetLinkScan ScanForTargetLink(string targetPathFragment)
        {
            TargetLinkScan res = new TargetLinkScan();
            List<IntPtr> windows = FindAntigravityWindowHandles();

            for (int w = 0; w < windows.Count; w++)
            {
                AutomationElement winEl = SafeFromHandle(windows[w]);
                if (winEl == null) continue;

                AutomationElement doc = FindHubDocument(winEl, 0);
                if (doc == null) continue;
                res.HasDocument = true;

                string docUrl = GetElementUrl(doc) ?? string.Empty;
                if (docUrl.IndexOf(targetPathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    res.IsAlreadyViewingTarget = true;
                    return res;
                }

                AutomationElement sidebar = FindSidebar(doc, 0);
                if (sidebar != null)
                {
                    res.SidebarFound = true;
                    AutomationElement link = FindLinkInSubtree(sidebar, targetPathFragment, 28);
                    if (link != null)
                    {
                        res.MatchingLink = link;
                        return res;
                    }
                }

                // Priority 2: Scan top-level Hub controls (open tabs, picker input) with tight depth cap
                Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
                queue.Enqueue(new KeyValuePair<AutomationElement, int>(doc, 0));
                int visited = 0;

                while (queue.Count > 0 && visited < 600)
                {
                    KeyValuePair<AutomationElement, int> item = queue.Dequeue();
                    visited++;
                    AutomationElement cur = item.Key;
                    int d = item.Value;

                    ControlType ct = SafeControlType(cur);
                    string name = SafeName(cur);
                    if (string.Equals(name, "Sidebar", StringComparison.Ordinal) || ct == ControlType.Header) continue;

                    if (ct == ControlType.Edit)
                    {
                        string hint = name + " " + SafeHelpText(cur);
                        if (IsConvoPickerPlaceholder(hint))
                        {
                            res.IsConvoPickerOpen = true;
                        }
                    }
                    else if (ct == ControlType.Hyperlink)
                    {
                        string u = GetElementUrl(cur) ?? string.Empty;
                        if (u.IndexOf(targetPathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            res.MatchingLink = cur;
                            return res;
                        }
                    }

                    if (d < 16)
                    {
                        EnqueueChildren(cur, d + 1, queue);
                    }
                }
            }
            return res;
        }

        private static bool IsViewingTargetConversation(string targetPathFragment)
        {
            List<IntPtr> windows = FindAntigravityWindowHandles();
            for (int w = 0; w < windows.Count; w++)
            {
                AutomationElement winEl = SafeFromHandle(windows[w]);
                if (winEl == null) continue;
                AutomationElement doc = FindHubDocument(winEl, 0);
                if (doc == null) continue;
                string docUrl = GetElementUrl(doc) ?? string.Empty;
                if (docUrl.IndexOf(targetPathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsConvoPickerPlaceholder(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("Search for conversations", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Search conversations", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetActivePickerHint()
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused != null && SafeControlType(focused) == ControlType.Edit)
                {
                    string hint = (SafeName(focused) + " " + SafeHelpText(focused)).Trim();
                    if (IsConvoPickerPlaceholder(hint) || hint.IndexOf("commands", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return hint;
                    }
                }
            }
            catch
            {
            }

            List<IntPtr> windows = FindAntigravityWindowHandles();
            for (int w = 0; w < windows.Count; w++)
            {
                AutomationElement winEl = SafeFromHandle(windows[w]);
                if (winEl == null) continue;
                AutomationElement doc = FindHubDocument(winEl, 0);
                if (doc == null) continue;

                Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
                queue.Enqueue(new KeyValuePair<AutomationElement, int>(doc, 0));
                int visited = 0;
                while (queue.Count > 0 && visited < 500)
                {
                    KeyValuePair<AutomationElement, int> item = queue.Dequeue();
                    visited++;
                    AutomationElement cur = item.Key;
                    int d = item.Value;
                    string nm = SafeName(cur);
                    if (string.Equals(nm, "Sidebar", StringComparison.Ordinal)) continue;
                    if (SafeControlType(cur) == ControlType.Edit)
                    {
                        string hint = (nm + " " + SafeHelpText(cur)).Trim();
                        if (IsConvoPickerPlaceholder(hint) || hint.IndexOf("commands", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return hint;
                        }
                    }
                    if (d < 14) EnqueueChildren(cur, d + 1, queue);
                }
            }
            return null;
        }

        private static bool PressButtonInHub(string matchingDescription)
        {
            List<IntPtr> windows = FindAntigravityWindowHandles();
            HashSet<string> targets = new HashSet<string> { matchingDescription };
            for (int w = 0; w < windows.Count; w++)
            {
                AutomationElement winEl = SafeFromHandle(windows[w]);
                if (winEl == null) continue;
                AutomationElement doc = FindHubDocument(winEl, 0);
                if (doc == null) continue;
                AutomationElement btn = FindElementInSubtree(doc, ControlType.Button, targets, 20);
                if (btn != null && TryInvokeElement(btn))
                {
                    return true;
                }
            }
            return false;
        }

        private static List<KeyValuePair<AutomationElement, string>> FindSidebarButtons(AutomationElement sidebar)
        {
            List<KeyValuePair<AutomationElement, string>> results = new List<KeyValuePair<AutomationElement, string>>();
            WalkSidebarButtons(sidebar, 0, results);
            return results;
        }

        private static void WalkSidebarButtons(AutomationElement el, int depth, List<KeyValuePair<AutomationElement, string>> results)
        {
            if (depth > 12 || el == null) return;
            if (SafeControlType(el) == ControlType.Button)
            {
                string title = SafeName(el).Trim();
                if (!string.IsNullOrEmpty(title))
                {
                    results.Add(new KeyValuePair<AutomationElement, string>(el, title));
                }
            }
            List<AutomationElement> children = GetChildrenList(el);
            for (int i = 0; i < children.Count; i++)
            {
                WalkSidebarButtons(children[i], depth + 1, results);
            }
        }

        private static AutomationElement FindNamedButton(List<KeyValuePair<AutomationElement, string>> buttons, string name, bool ignoreCase)
        {
            StringComparison cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (string.Equals(buttons[i].Value, name, cmp))
                {
                    return buttons[i].Key;
                }
            }
            return null;
        }

        private static AutomationElement FindPrefixButton(List<KeyValuePair<AutomationElement, string>> buttons, string prefix)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i].Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return buttons[i].Key;
                }
            }
            return null;
        }

        private static bool? GetElementExpandedState(AutomationElement el)
        {
            if (el == null) return null;
            try
            {
                object pat;
                if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pat))
                {
                    ExpandCollapseState st = ((ExpandCollapsePattern)pat).Current.ExpandCollapseState;
                    if (st == ExpandCollapseState.Collapsed) return false;
                    if (st == ExpandCollapseState.Expanded || st == ExpandCollapseState.PartiallyExpanded) return true;
                }
            }
            catch
            {
            }
            return null;
        }

        private static void TryScrollIntoView(AutomationElement el)
        {
            if (el == null) return;
            try
            {
                object pat;
                if (el.TryGetCurrentPattern(ScrollItemPattern.Pattern, out pat))
                {
                    ((ScrollItemPattern)pat).ScrollIntoView();
                }
            }
            catch
            {
            }
        }

        private static AutomationElement SafeFromHandle(IntPtr hwnd)
        {
            try { return AutomationElement.FromHandle(hwnd); }
            catch { return null; }
        }

        private static ControlType SafeControlType(AutomationElement el)
        {
            try { return el.Current.ControlType; }
            catch { return null; }
        }

        private static string SafeName(AutomationElement el)
        {
            try { return el.Current.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeHelpText(AutomationElement el)
        {
            try { return el.Current.HelpText ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string GetElementUrl(AutomationElement el)
        {
            try
            {
                object vpObj;
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out vpObj))
                {
                    ValuePattern vp = vpObj as ValuePattern;
                    if (vp != null) return vp.Current.Value;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool TryInvokeElement(AutomationElement el)
        {
            if (el == null) return false;
            try
            {
                object pat;
                if (el.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                {
                    ((InvokePattern)pat).Invoke();
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool TryInvokeOrExpandElement(AutomationElement el)
        {
            if (TryInvokeElement(el)) return true;
            try
            {
                object pat;
                if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pat))
                {
                    ((ExpandCollapsePattern)pat).Expand();
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static void EnqueueChildren(AutomationElement parent, int nextDepth, Queue<KeyValuePair<AutomationElement, int>> queue)
        {
            try
            {
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement child = walker.GetFirstChild(parent);
                while (child != null)
                {
                    queue.Enqueue(new KeyValuePair<AutomationElement, int>(child, nextDepth));
                    child = walker.GetNextSibling(child);
                }
            }
            catch
            {
            }
        }

        private static List<AutomationElement> GetChildrenList(AutomationElement parent)
        {
            List<AutomationElement> list = new List<AutomationElement>();
            try
            {
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement child = walker.GetFirstChild(parent);
                while (child != null)
                {
                    list.Add(child);
                    child = walker.GetNextSibling(child);
                }
            }
            catch
            {
            }
            return list;
        }

        private static AutomationElement FindHubDocument(AutomationElement el, int depth)
        {
            if (depth > 16 || el == null) return null;
            ControlType ct = SafeControlType(el);
            if (ct == ControlType.MenuBar) return null;
            if (ct == ControlType.Document)
            {
                string u = GetElementUrl(el) ?? string.Empty;
                if (!string.IsNullOrEmpty(u) && !u.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return el;
                }
                return null;
            }
            List<AutomationElement> children = GetChildrenList(el);
            for (int i = 0; i < children.Count; i++)
            {
                AutomationElement found = FindHubDocument(children[i], depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        private static AutomationElement FindSidebar(AutomationElement el, int depth)
        {
            if (depth > 16 || el == null) return null;
            if (string.Equals(SafeName(el), "Sidebar", StringComparison.Ordinal))
            {
                return el;
            }
            List<AutomationElement> children = GetChildrenList(el);
            for (int i = 0; i < children.Count; i++)
            {
                AutomationElement found = FindSidebar(children[i], depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        private static void CollectSidebarRows(AutomationElement el, int depth, Dictionary<string, LiveSidebarItem> map)
        {
            if (depth > 22 || el == null) return;
            List<AutomationElement> children = GetChildrenList(el);
            if (children.Count == 0) return;

            string rowConvId = null;
            string rowTitle = null;

            for (int i = 0; i < children.Count; i++)
            {
                AutomationElement child = children[i];
                if (SafeControlType(child) == ControlType.Hyperlink)
                {
                    string u = GetElementUrl(child);
                    string cid = ExtractConversationIdFromUrl(u);
                    if (!string.IsNullOrEmpty(cid))
                    {
                        rowConvId = cid;
                        string nm = SafeName(child).Trim();
                        if (!string.IsNullOrEmpty(nm)) rowTitle = nm;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(rowConvId))
            {
                bool hasStop = false;
                bool hasArchiveOrIdle = false;
                InspectRowButtons(el, 0, ref hasStop, ref hasArchiveOrIdle);

                LiveSidebarItem item = new LiveSidebarItem();
                item.ConversationId = rowConvId;
                item.Title = rowTitle;
                item.IsRunning = hasStop;
                item.IsIdleInUI = hasArchiveOrIdle && !hasStop;
                map[rowConvId] = item;
                return;
            }

            for (int i = 0; i < children.Count; i++)
            {
                CollectSidebarRows(children[i], depth + 1, map);
            }
        }

        private static void InspectRowButtons(AutomationElement node, int depth, ref bool hasStop, ref bool hasArchiveOrIdle)
        {
            if (depth > 6 || node == null) return;
            if (SafeControlType(node) == ControlType.Button)
            {
                string nm = SafeName(node);
                if (string.Equals(nm, "Stop execution", StringComparison.Ordinal))
                {
                    hasStop = true;
                }
                else if (string.Equals(nm, "Archive conversation", StringComparison.Ordinal) || string.Equals(nm, "Restore conversation", StringComparison.Ordinal))
                {
                    hasArchiveOrIdle = true;
                }
            }
            List<AutomationElement> subs = GetChildrenList(node);
            for (int i = 0; i < subs.Count; i++)
            {
                InspectRowButtons(subs[i], depth + 1, ref hasStop, ref hasArchiveOrIdle);
            }
        }

        private static void InspectOpenDocumentState(AutomationElement doc, out bool hasStop, out bool hasCancelled)
        {
            hasStop = false;
            hasCancelled = false;
            Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
            queue.Enqueue(new KeyValuePair<AutomationElement, int>(doc, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < 1500)
            {
                KeyValuePair<AutomationElement, int> item = queue.Dequeue();
                visited++;
                AutomationElement cur = item.Key;
                int d = item.Value;
                ControlType ct = SafeControlType(cur);
                string nm = SafeName(cur);

                if (ct == ControlType.Button && nm.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasStop = true;
                }
                else if (ct == ControlType.Text && nm.IndexOf("User cancelled agent execution", StringComparison.Ordinal) >= 0)
                {
                    hasCancelled = true;
                }

                if (d < 24)
                {
                    EnqueueChildren(cur, d + 1, queue);
                }
            }
        }

        private static AutomationElement FindLinkInSubtree(AutomationElement el, string targetFragment, int maxDepth)
        {
            if (maxDepth < 0 || el == null) return null;
            if (SafeControlType(el) == ControlType.Hyperlink)
            {
                string u = GetElementUrl(el) ?? string.Empty;
                if (u.IndexOf(targetFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return el;
                }
            }
            List<AutomationElement> children = GetChildrenList(el);
            for (int i = 0; i < children.Count; i++)
            {
                AutomationElement hit = FindLinkInSubtree(children[i], targetFragment, maxDepth - 1);
                if (hit != null) return hit;
            }
            return null;
        }

        private static AutomationElement FindSidebarRowContainer(AutomationElement el, string convId, int depth)
        {
            if (depth > 26 || el == null) return null;
            List<AutomationElement> children = GetChildrenList(el);
            for (int i = 0; i < children.Count; i++)
            {
                if (SafeControlType(children[i]) == ControlType.Hyperlink)
                {
                    string u = GetElementUrl(children[i]);
                    if (string.Equals(ExtractConversationIdFromUrl(u), convId, StringComparison.OrdinalIgnoreCase))
                    {
                        return el;
                    }
                }
            }
            for (int i = 0; i < children.Count; i++)
            {
                AutomationElement hit = FindSidebarRowContainer(children[i], convId, depth + 1);
                if (hit != null) return hit;
            }
            return null;
        }

        private static AutomationElement FindElementInSubtree(AutomationElement el, ControlType role, HashSet<string> names, int maxDepth)
        {
            if (maxDepth < 0 || el == null) return null;
            if (role == null || SafeControlType(el) == role)
            {
                string nm = SafeName(el).Trim();
                if (names.Contains(nm)) return el;
            }
            List<AutomationElement> children = GetChildrenList(el);
            for (int i = 0; i < children.Count; i++)
            {
                AutomationElement hit = FindElementInSubtree(children[i], role, names, maxDepth - 1);
                if (hit != null) return hit;
            }
            return null;
        }

        private static AutomationElement FindDeleteMenuItem(AutomationElement root)
        {
            HashSet<string> labels = new HashSet<string> { "Delete", "Delete Permanently", "删除", "永久删除" };
            Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
            queue.Enqueue(new KeyValuePair<AutomationElement, int>(root, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < 1200)
            {
                KeyValuePair<AutomationElement, int> item = queue.Dequeue();
                visited++;
                AutomationElement cur = item.Key;
                int d = item.Value;
                ControlType ct = SafeControlType(cur);
                if (ct == ControlType.MenuBar) continue;
                if (ct == ControlType.MenuItem && labels.Contains(SafeName(cur).Trim()))
                {
                    return cur;
                }
                if (d < 12) EnqueueChildren(cur, d + 1, queue);
            }
            return null;
        }

        private static AutomationElement FindModalDeleteConfirmButton(AutomationElement root)
        {
            if (root == null) return null;
            HashSet<string> labels = new HashSet<string> { "Delete", "Delete Permanently", "删除", "永久删除" };
            Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
            queue.Enqueue(new KeyValuePair<AutomationElement, int>(root, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < 1800)
            {
                KeyValuePair<AutomationElement, int> item = queue.Dequeue();
                visited++;
                AutomationElement cur = item.Key;
                int d = item.Value;
                string nm = SafeName(cur).Trim();
                if (string.Equals(nm, "Sidebar", StringComparison.Ordinal)) continue;
                if (SafeControlType(cur) == ControlType.Button && labels.Contains(nm))
                {
                    return cur;
                }
                if (d < 22) EnqueueChildren(cur, d + 1, queue);
            }
            return null;
        }

        private static string ExtractConversationIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int idx = url.IndexOf("/c/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            string tail = url.Substring(idx + 3);
            int end = tail.IndexOfAny(new char[] { '?', '/', '#' });
            string id = end >= 0 ? tail.Substring(0, end) : tail;
            return id.Length >= 32 ? id : null;
        }
    }
}
