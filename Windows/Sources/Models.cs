using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace AntigravityAssistant
{
    // MARK: - Agent Session Phase

    public enum AgentPhaseKind
    {
        Idle,
        Thinking,
        RunningTool,
        WaitingUser,
        Completed,
        Interrupted,
        Paused,
        Error
    }

    public sealed class AgentPhase : IEquatable<AgentPhase>
    {
        public static readonly AgentPhase Idle = new AgentPhase(AgentPhaseKind.Idle, null, null);
        public static readonly AgentPhase Thinking = new AgentPhase(AgentPhaseKind.Thinking, null, null);
        public static readonly AgentPhase Completed = new AgentPhase(AgentPhaseKind.Completed, null, null);
        public static readonly AgentPhase Interrupted = new AgentPhase(AgentPhaseKind.Interrupted, null, null);
        public static readonly AgentPhase Paused = new AgentPhase(AgentPhaseKind.Paused, null, null);

        public AgentPhaseKind Kind { get; private set; }
        public string ToolName { get; private set; }
        public string Detail { get; private set; }

        private AgentPhase(AgentPhaseKind kind, string toolName, string detail)
        {
            Kind = kind;
            ToolName = toolName;
            Detail = detail;
        }

        public static AgentPhase RunningTool(string name, string detail)
        {
            return new AgentPhase(AgentPhaseKind.RunningTool, name ?? "tool", detail);
        }

        public static AgentPhase WaitingUser(string detail)
        {
            return new AgentPhase(AgentPhaseKind.WaitingUser, null, detail);
        }

        public static AgentPhase Error(string detail)
        {
            return new AgentPhase(AgentPhaseKind.Error, null, detail);
        }

        public bool IsWorking
        {
            get { return Kind == AgentPhaseKind.Thinking || Kind == AgentPhaseKind.RunningTool; }
        }

        public bool IsPriorityActive
        {
            get
            {
                return Kind == AgentPhaseKind.Thinking
                    || Kind == AgentPhaseKind.RunningTool
                    || Kind == AgentPhaseKind.WaitingUser;
            }
        }

        public string LiveDisplayLabel
        {
            get
            {
                switch (Kind)
                {
                    case AgentPhaseKind.Thinking:
                        return "思考中";
                    case AgentPhaseKind.RunningTool:
                        return "执行 · " + FriendlyToolName(ToolName ?? "tool");
                    case AgentPhaseKind.WaitingUser:
                        return "等待你确认";
                    case AgentPhaseKind.Error:
                        return "执行异常";
                    default:
                        return null;
                }
            }
        }

        public string ShortLabel
        {
            get
            {
                switch (Kind)
                {
                    case AgentPhaseKind.Idle: return "待命";
                    case AgentPhaseKind.Thinking: return "思考中";
                    case AgentPhaseKind.RunningTool: return "执行 · " + FriendlyToolName(ToolName ?? "tool");
                    case AgentPhaseKind.WaitingUser: return "等待你确认";
                    case AgentPhaseKind.Completed: return "已完成";
                    case AgentPhaseKind.Interrupted: return "已中断";
                    case AgentPhaseKind.Paused: return "已休眠";
                    case AgentPhaseKind.Error: return "执行异常";
                    default: return "待命";
                }
            }
        }

        public Color AccentColor
        {
            get
            {
                switch (Kind)
                {
                    case AgentPhaseKind.Idle:
                        return Color.FromRgb(112, 128, 153);
                    case AgentPhaseKind.Thinking:
                        return Color.FromRgb(26, 117, 240);
                    case AgentPhaseKind.RunningTool:
                        return Color.FromRgb(122, 71, 230);
                    case AgentPhaseKind.WaitingUser:
                        return Color.FromRgb(219, 128, 10);
                    case AgentPhaseKind.Completed:
                        return Color.FromRgb(20, 163, 87);
                    case AgentPhaseKind.Interrupted:
                        return Color.FromRgb(214, 107, 36);
                    case AgentPhaseKind.Paused:
                        return Color.FromRgb(122, 135, 153);
                    case AgentPhaseKind.Error:
                        return Color.FromRgb(219, 46, 56);
                    default:
                        return Color.FromRgb(112, 128, 153);
                }
            }
        }

        public static string FriendlyToolName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "tool";
            switch (raw)
            {
                case "run_command": return "终端命令";
                case "read_file":
                case "view_file": return "读取文件";
                case "write_file": return "创建文件";
                case "edit_file": return "修改代码";
                case "search_web": return "网页搜索";
                case "ask_question": return "提问确认";
                case "invoke_subagent": return "子 Agent";
                case "swarm": return "Swarm 协作";
                case "schedule": return "定时任务";
                case "manage_task": return "后台任务";
                default:
                    if (raw.StartsWith("mcp_", StringComparison.Ordinal) || raw.Contains("__"))
                    {
                        string[] parts = raw.Split(new string[] { "__" }, StringSplitOptions.None);
                        string tail = parts.Length > 0 ? parts[parts.Length - 1] : raw;
                        return "MCP·" + tail;
                    }
                    return raw;
            }
        }

        public bool Equals(AgentPhase other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Kind == other.Kind
                && string.Equals(ToolName, other.ToolName, StringComparison.Ordinal)
                && string.Equals(Detail, other.Detail, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AgentPhase);
        }

        public override int GetHashCode()
        {
            int h = (int)Kind * 397;
            if (ToolName != null) h ^= ToolName.GetHashCode();
            if (Detail != null) h ^= Detail.GetHashCode();
            return h;
        }

        public static bool operator ==(AgentPhase a, AgentPhase b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
            return a.Equals(b);
        }

        public static bool operator !=(AgentPhase a, AgentPhase b)
        {
            return !(a == b);
        }
    }

    // MARK: - Conversation & Invocation Records

    public sealed class InvocationRecord
    {
        public string Id { get; set; }
        public string ConversationId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string ModelName { get; set; }
        public int InputTokens { get; set; }
        public int CacheReadTokens { get; set; }
        public int OutputTokens { get; set; }

        public int TotalContextTokens
        {
            get { return InputTokens + CacheReadTokens; }
        }

        public int TotalTokens
        {
            get { return InputTokens + CacheReadTokens + OutputTokens; }
        }

        public bool IsGeminiFamily
        {
            get
            {
                string lower = (ModelName ?? string.Empty).ToLowerInvariant();
                if (lower.Contains("claude") || lower.Contains("gpt") || lower.Contains("sonnet") || lower.Contains("opus"))
                {
                    return false;
                }
                return true;
            }
        }

        public double CreditWeight
        {
            get
            {
                string lower = (ModelName ?? string.Empty).ToLowerInvariant();
                if (lower.Contains("flash") || lower.Contains("lite"))
                {
                    return 0.35;
                }
                if (lower.Contains("carbon") || lower.Contains("pro") || lower.Contains("ultra"))
                {
                    return 1.15;
                }
                return 1.0;
            }
        }
    }

    public sealed class ConversationSnapshot
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string ProjectName { get; set; }
        public string SearchQuery { get; set; }
        public string ModelName { get; set; }
        public AgentPhase Phase { get; set; }
        public DateTime LastUpdatedAtUtc { get; set; }
        public DateTime? ActiveTurnStartedAtUtc { get; set; }
        public int StepCount { get; set; }
        public int UserTurnCount { get; set; }
        public int InvocationCount { get; set; }
        public int ContextUsedTokens { get; set; }
        public int LatestInputTokens { get; set; }
        public int LatestCacheReadTokens { get; set; }
        public int TotalOutputTokens { get; set; }
        public bool BackgroundBusy { get; set; }

        public double ContextRemainingPercent(int maxTokens)
        {
            if (maxTokens <= 0) return 100.0;
            double usedRatio = (double)ContextUsedTokens / (double)maxTokens;
            return Math.Max(0.0, Math.Min(100.0, (1.0 - usedRatio) * 100.0));
        }
    }

    // MARK: - Synced Official Quota Anchor & Metrics

    public sealed class QuotaSyncAnchor
    {
        public DateTime SyncedAtUtc { get; set; }
        public double FiveHourRemainingPercent { get; set; }
        public DateTime FiveHourResetAtUtc { get; set; }
        public double WeeklyRemainingPercent { get; set; }
        public DateTime WeeklyResetAtUtc { get; set; }
    }

    public sealed class QuotaMetrics
    {
        public double FiveHourRemainingPercent { get; set; }
        public DateTime? FiveHourResetDateUtc { get; set; }
        public int FiveHourStepsUsed { get; set; }
        public int FiveHourTokensUsed { get; set; }
        public bool IsFiveHourSynced { get; set; }

        public double WeeklyRemainingPercent { get; set; }
        public DateTime? WeeklyResetDateUtc { get; set; }
        public int WeeklyStepsUsed { get; set; }
        public bool IsWeeklySynced { get; set; }

        public int TodayInvocations { get; set; }
        public int TodayUserTurns { get; set; }
        public int TodayTokens { get; set; }
        public int TodayConversations { get; set; }

        public static QuotaMetrics Empty()
        {
            QuotaMetrics q = new QuotaMetrics();
            q.FiveHourRemainingPercent = 100.0;
            q.WeeklyRemainingPercent = 100.0;
            return q;
        }

        public static Color StatusColor(double remainingPercent)
        {
            if (remainingPercent <= 18.0)
            {
                return Color.FromRgb(219, 51, 61);
            }
            if (remainingPercent <= 45.0)
            {
                return Color.FromRgb(219, 128, 10);
            }
            return Color.FromRgb(20, 163, 84);
        }
    }

    // MARK: - Optional Hook Event State

    public sealed class HookLiveEvent
    {
        public string EventName { get; set; }
        public double TimestampUnix { get; set; }
        public string ConversationId { get; set; }
        public string ModelName { get; set; }
        public string ToolName { get; set; }
        public string ToolDetail { get; set; }
        public string TerminationReason { get; set; }
        public bool? FullyIdle { get; set; }
        public string Error { get; set; }
    }

    public sealed class SessionGroupSection
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool IsPriority { get; set; }
        public List<ConversationSnapshot> Conversations { get; set; }
    }

    // MARK: - Plan Presets & Persistent Settings (`%LOCALAPPDATA%\AgyAssistant\settings.json`)

    public enum PlanPreset
    {
        Standard,
        Pro,
        Ultra,
        Custom
    }

    public sealed class AppSettings
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupRegistryValueName = "AntigravityAssistant";

        public event Action Changed;

        private readonly string _settingsFilePath;
        private bool _suppressSave;

        private PlanPreset _planPreset = PlanPreset.Pro;
        private int _capacity5hUnits = 6600;
        private int _capacityWeeklyUnits = 16600;
        private int _maxContextTokens = 1000000;
        private bool _notifyOnComplete = true;
        private double _minTurnDurationForAlert = 4.0;
        private bool _notifyOnWaitingOrError = true;
        private bool _notifyOnLowRemaining = true;
        private double _lowQuotaThreshold = 20.0;
        private double _lowContextThreshold = 25.0;
        private bool _soundEnabled = true;
        private bool _alwaysOnTop = true;
        private double _pillOpacity = 0.96;
        private QuotaSyncAnchor _syncAnchor;
        private double? _panelAnchorMaxX;
        private double? _panelAnchorMaxY;
        private List<string> _pinnedIds = new List<string>();
        private List<string> _unreadCompletedIds = new List<string>();
        private List<string> _deletedIds = new List<string>();
        private string _selectedAccountEmail;

        public static string AppDataFolder
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                {
                    local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
                }
                return Path.Combine(local, "AgyAssistant");
            }
        }

        public AppSettings()
        {
            _settingsFilePath = Path.Combine(AppDataFolder, "settings.json");
            Load();
        }

        public PlanPreset Preset
        {
            get { return _planPreset; }
            set
            {
                if (_planPreset == value) return;
                _planPreset = value;
                if (value != PlanPreset.Custom)
                {
                    _capacity5hUnits = Default5hCapacity(value);
                    _capacityWeeklyUnits = DefaultWeeklyCapacity(value);
                }
                SaveAndNotify();
            }
        }

        public int Capacity5hUnits
        {
            get { return _capacity5hUnits; }
            set { _capacity5hUnits = Math.Max(100, value); SaveAndNotify(); }
        }

        public int CapacityWeeklyUnits
        {
            get { return _capacityWeeklyUnits; }
            set { _capacityWeeklyUnits = Math.Max(300, value); SaveAndNotify(); }
        }

        public int MaxContextTokens
        {
            get { return _maxContextTokens; }
            set { _maxContextTokens = Math.Max(50000, value); SaveAndNotify(); }
        }

        public bool NotifyOnComplete
        {
            get { return _notifyOnComplete; }
            set { if (_notifyOnComplete != value) { _notifyOnComplete = value; SaveAndNotify(); } }
        }

        public double MinTurnDurationForAlert
        {
            get { return _minTurnDurationForAlert; }
            set
            {
                double clamped = Math.Max(0.0, Math.Min(60.0, value));
                if (Math.Abs(_minTurnDurationForAlert - clamped) > 0.01)
                {
                    _minTurnDurationForAlert = clamped;
                    SaveAndNotify();
                }
            }
        }

        public bool NotifyOnWaitingOrError
        {
            get { return _notifyOnWaitingOrError; }
            set { if (_notifyOnWaitingOrError != value) { _notifyOnWaitingOrError = value; SaveAndNotify(); } }
        }

        public bool NotifyOnLowRemaining
        {
            get { return _notifyOnLowRemaining; }
            set { if (_notifyOnLowRemaining != value) { _notifyOnLowRemaining = value; SaveAndNotify(); } }
        }

        public double LowQuotaThreshold
        {
            get { return _lowQuotaThreshold; }
            set { _lowQuotaThreshold = value; SaveAndNotify(); }
        }

        public double LowContextThreshold
        {
            get { return _lowContextThreshold; }
            set { _lowContextThreshold = value; SaveAndNotify(); }
        }

        public bool SoundEnabled
        {
            get { return _soundEnabled; }
            set { if (_soundEnabled != value) { _soundEnabled = value; SaveAndNotify(); } }
        }

        public bool AlwaysOnTop
        {
            get { return _alwaysOnTop; }
            set { if (_alwaysOnTop != value) { _alwaysOnTop = value; SaveAndNotify(); } }
        }

        public double PillOpacity
        {
            get { return _pillOpacity; }
            set
            {
                double clamped = Math.Max(0.65, Math.Min(1.0, value));
                if (Math.Abs(_pillOpacity - clamped) > 0.002)
                {
                    _pillOpacity = clamped;
                    SaveAndNotify();
                }
            }
        }

        public QuotaSyncAnchor SyncAnchor
        {
            get { return _syncAnchor; }
            set { _syncAnchor = value; SaveAndNotify(); }
        }

        public double? PanelAnchorMaxX
        {
            get { return _panelAnchorMaxX; }
        }

        public double? PanelAnchorMaxY
        {
            get { return _panelAnchorMaxY; }
        }

        public string SelectedAccountEmail
        {
            get { return _selectedAccountEmail; }
            set { if (_selectedAccountEmail != value) { _selectedAccountEmail = value; SaveAndNotify(); } }
        }

        public List<string> PinnedIds { get { return _pinnedIds; } }
        public List<string> UnreadCompletedIds { get { return _unreadCompletedIds; } }
        public List<string> DeletedIds { get { return _deletedIds; } }

        public void SavePanelAnchor(double maxX, double maxY)
        {
            _panelAnchorMaxX = maxX;
            _panelAnchorMaxY = maxY;
            SaveOnly();
        }

        public void UpdateConversationSets(IEnumerable<string> pinned, IEnumerable<string> unreadDone, IEnumerable<string> deleted)
        {
            _pinnedIds = TakeUpTo(pinned, 200);
            _unreadCompletedIds = TakeUpTo(unreadDone, 200);
            _deletedIds = TakeUpTo(deleted, 600);
            SaveOnly();
        }

        private static List<string> TakeUpTo(IEnumerable<string> src, int maxCount)
        {
            List<string> list = new List<string>();
            if (src == null) return list;
            foreach (string s in src)
            {
                if (!string.IsNullOrEmpty(s))
                {
                    list.Add(s);
                    if (list.Count >= maxCount) break;
                }
            }
            return list;
        }

        public bool LaunchAtStartup
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                    {
                        if (key == null) return false;
                        string val = key.GetValue(StartupRegistryValueName) as string;
                        return !string.IsNullOrEmpty(val);
                    }
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
                    {
                        if (key == null) return;
                        if (value)
                        {
                            string exePath = Assembly.GetExecutingAssembly().Location;
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                key.SetValue(StartupRegistryValueName, "\"" + exePath + "\"");
                            }
                        }
                        else
                        {
                            key.DeleteValue(StartupRegistryValueName, false);
                        }
                    }
                }
                catch
                {
                }
                Action h = Changed;
                if (h != null) h();
            }
        }

        public static int Default5hCapacity(PlanPreset p)
        {
            switch (p)
            {
                case PlanPreset.Standard: return 2200;
                case PlanPreset.Pro: return 6600;
                case PlanPreset.Ultra: return 16000;
                default: return 6600;
            }
        }

        public static int DefaultWeeklyCapacity(PlanPreset p)
        {
            switch (p)
            {
                case PlanPreset.Standard: return 6000;
                case PlanPreset.Pro: return 16600;
                case PlanPreset.Ultra: return 45000;
                default: return 16600;
            }
        }

        public void ApplyManualSync(
            double fiveHourPercent,
            double fiveHourRefreshInSeconds,
            double weeklyPercent,
            double weeklyRefreshInSeconds,
            DateTime nowUtc)
        {
            QuotaSyncAnchor anchor = new QuotaSyncAnchor();
            anchor.SyncedAtUtc = nowUtc;
            anchor.FiveHourRemainingPercent = Math.Max(0.0, Math.Min(100.0, fiveHourPercent));
            anchor.FiveHourResetAtUtc = nowUtc.AddSeconds(Math.Max(60.0, fiveHourRefreshInSeconds));
            anchor.WeeklyRemainingPercent = Math.Max(0.0, Math.Min(100.0, weeklyPercent));
            anchor.WeeklyResetAtUtc = nowUtc.AddSeconds(Math.Max(60.0, weeklyRefreshInSeconds));
            SyncAnchor = anchor;
        }

        public bool SyncFromOfficialUsageText(string rawText)
        {
            return SyncFromOfficialUsageText(rawText, DateTime.UtcNow);
        }

        public bool SyncFromOfficialUsageText(string rawText, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(rawText)) return false;
            string lower = rawText.ToLowerInvariant();
            if (!lower.Contains("%")) return false;

            // Isolate Gemini Models section if both Gemini and Claude sections were copied together
            int gemIdx = lower.IndexOf("gemini", StringComparison.Ordinal);
            int claudeIdx = lower.IndexOf("claude", StringComparison.Ordinal);
            if (gemIdx >= 0 && claudeIdx > gemIdx)
            {
                lower = lower.Substring(gemIdx, claudeIdx - gemIdx);
            }
            else if (claudeIdx > 0 && gemIdx < 0)
            {
                // If text begins with Gemini unnamed block before Claude block
                string beforeClaude = lower.Substring(0, claudeIdx);
                if (beforeClaude.Contains("%"))
                {
                    lower = beforeClaude;
                }
            }

            double? parsedWeeklyPct = null;
            double? parsedWeeklySec = null;
            double? parsed5hPct = null;
            double? parsed5hSec = null;

            int wIdx = IndexOfAnyPhrase(lower, new string[] { "weekly limit", "weekly", "7d limit", "7-day", "7d", "每周" });
            int fIdx = IndexOfAnyPhrase(lower, new string[] { "five hour limit", "5h limit", "5-hour", "five hour", "5 小时", "5小时", "5h" });

            if (wIdx >= 0 && fIdx >= 0)
            {
                if (wIdx < fIdx)
                {
                    string weeklyChunk = lower.Substring(wIdx, fIdx - wIdx);
                    string fiveChunk = lower.Substring(fIdx);
                    parsedWeeklyPct = FirstPercent(weeklyChunk);
                    parsedWeeklySec = ParseRefreshDuration(weeklyChunk);
                    parsed5hPct = FirstPercent(fiveChunk);
                    parsed5hSec = ParseRefreshDuration(fiveChunk);
                }
                else
                {
                    string fiveChunk = lower.Substring(fIdx, wIdx - fIdx);
                    string weeklyChunk = lower.Substring(wIdx);
                    parsed5hPct = FirstPercent(fiveChunk);
                    parsed5hSec = ParseRefreshDuration(fiveChunk);
                    parsedWeeklyPct = FirstPercent(weeklyChunk);
                    parsedWeeklySec = ParseRefreshDuration(weeklyChunk);
                }
            }
            else if (fIdx >= 0)
            {
                string fiveChunk = lower.Substring(fIdx);
                parsed5hPct = FirstPercent(fiveChunk);
                parsed5hSec = ParseRefreshDuration(fiveChunk);
            }
            else if (wIdx >= 0)
            {
                string weeklyChunk = lower.Substring(wIdx);
                parsedWeeklyPct = FirstPercent(weeklyChunk);
                parsedWeeklySec = ParseRefreshDuration(weeklyChunk);
            }
            else
            {
                // Fallback if user copied a standalone percentage line
                parsed5hPct = FirstPercent(lower);
            }

            if (!parsed5hPct.HasValue && !parsedWeeklyPct.HasValue) return false;

            double cur5hPct = parsed5hPct ?? (_syncAnchor != null ? _syncAnchor.FiveHourRemainingPercent : 95.0);
            double cur5hSec = parsed5hSec ?? (_syncAnchor != null ? Math.Max(300.0, (_syncAnchor.FiveHourResetAtUtc - nowUtc).TotalSeconds) : 5.0 * 3600.0);
            double curWkPct = parsedWeeklyPct ?? (_syncAnchor != null ? _syncAnchor.WeeklyRemainingPercent : 98.0);
            double curWkSec = parsedWeeklySec ?? (_syncAnchor != null ? Math.Max(3600.0, (_syncAnchor.WeeklyResetAtUtc - nowUtc).TotalSeconds) : 7.0 * 86400.0);

            ApplyManualSync(cur5hPct, cur5hSec, curWkPct, curWkSec, nowUtc);
            return true;
        }

        private static int IndexOfAnyPhrase(string text, string[] phrases)
        {
            for (int i = 0; i < phrases.Length; i++)
            {
                int idx = text.IndexOf(phrases[i], StringComparison.Ordinal);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        private static double? FirstPercent(string text)
        {
            Match m = Regex.Match(text, @"(\d{1,3}(?:\.\d+)?)\s*%");
            if (!m.Success) return null;
            double v;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return null;
        }

        private static double? ParseRefreshDuration(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string scope = text;
            int resetIdx = IndexOfAnyPhrase(scope, new string[] { "resets in", "reset in", "resets", "刷新" });
            if (resetIdx >= 0)
            {
                scope = scope.Substring(resetIdx);
            }
            else
            {
                // Strip leading label tokens like "5h limit" or "7d limit" so "5h" isn't mistaken for remaining duration
                scope = Regex.Replace(scope, @"\b(?:5h|5-hour|7d|7-day)\s*(?:limit)?\b", " ");
            }

            int days = ExtractUnit(scope, @"(\d+)\s*(?:days?|d|天)");
            int hours = ExtractUnit(scope, @"(\d+)\s*(?:hours?|hrs?|h|小时)");
            int mins = ExtractUnit(scope, @"(\d+)\s*(?:minutes?|mins?|m|分钟|分)");
            double total = days * 86400.0 + hours * 3600.0 + mins * 60.0;
            return total > 0 ? (double?)total : null;
        }

        private static int ExtractUnit(string text, string pattern)
        {
            Match m = Regex.Match(text, pattern);
            if (!m.Success) return 0;
            int v;
            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private void Load()
        {
            _suppressSave = true;
            try
            {
                if (!File.Exists(_settingsFilePath)) return;
                string raw = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                Dictionary<string, object> d = MiniJson.ParseObject(raw);
                if (d == null) return;

                string presetStr = MiniJson.GetString(d, "planPreset");
                PlanPreset parsedPreset;
                if (!string.IsNullOrEmpty(presetStr) && Enum.TryParse(presetStr, true, out parsedPreset))
                {
                    _planPreset = parsedPreset;
                }

                _capacity5hUnits = Math.Max(100, MiniJson.GetInt(d, "capacity5hUnits", Default5hCapacity(_planPreset)));
                _capacityWeeklyUnits = Math.Max(300, MiniJson.GetInt(d, "capacityWeeklyUnits", DefaultWeeklyCapacity(_planPreset)));
                _maxContextTokens = Math.Max(50000, MiniJson.GetInt(d, "maxContextTokens", 1000000));
                _notifyOnComplete = MiniJson.GetNullableBool(d, "notifyOnComplete") ?? true;
                _minTurnDurationForAlert = MiniJson.GetNullableDouble(d, "minTurnDurationForAlert") ?? 4.0;
                _notifyOnWaitingOrError = MiniJson.GetNullableBool(d, "notifyOnWaitingOrError") ?? true;
                _notifyOnLowRemaining = MiniJson.GetNullableBool(d, "notifyOnLowRemaining") ?? true;
                _lowQuotaThreshold = MiniJson.GetNullableDouble(d, "lowQuotaThreshold") ?? 20.0;
                _lowContextThreshold = MiniJson.GetNullableDouble(d, "lowContextThreshold") ?? 25.0;
                _soundEnabled = MiniJson.GetNullableBool(d, "soundEnabled") ?? true;
                _alwaysOnTop = MiniJson.GetNullableBool(d, "alwaysOnTop") ?? true;
                _pillOpacity = Math.Max(0.65, Math.Min(1.0, MiniJson.GetNullableDouble(d, "pillOpacity") ?? 0.96));
                _panelAnchorMaxX = MiniJson.GetNullableDouble(d, "panelAnchorMaxX");
                _panelAnchorMaxY = MiniJson.GetNullableDouble(d, "panelAnchorMaxY");
                _selectedAccountEmail = MiniJson.GetString(d, "selectedAccountEmail");

                _pinnedIds = ReadStringList(MiniJson.GetList(d, "pinnedConversationIds"));
                _unreadCompletedIds = ReadStringList(MiniJson.GetList(d, "unreadCompletedConversationIds"));
                _deletedIds = ReadStringList(MiniJson.GetList(d, "deletedConversationIds"));

                Dictionary<string, object> anchorDict = MiniJson.GetDict(d, "syncAnchor");
                if (anchorDict != null)
                {
                    DateTime? sAt = MagpieQuotaClient.ParseAnyIsoDateUtc(MiniJson.GetString(anchorDict, "syncedAt"));
                    DateTime? r5At = MagpieQuotaClient.ParseAnyIsoDateUtc(MiniJson.GetString(anchorDict, "fiveHourResetAt"));
                    DateTime? rwAt = MagpieQuotaClient.ParseAnyIsoDateUtc(MiniJson.GetString(anchorDict, "weeklyResetAt"));
                    if (sAt.HasValue && r5At.HasValue && rwAt.HasValue)
                    {
                        QuotaSyncAnchor a = new QuotaSyncAnchor();
                        a.SyncedAtUtc = sAt.Value;
                        a.FiveHourRemainingPercent = MiniJson.GetNullableDouble(anchorDict, "fiveHourRemainingPercent") ?? 100.0;
                        a.FiveHourResetAtUtc = r5At.Value;
                        a.WeeklyRemainingPercent = MiniJson.GetNullableDouble(anchorDict, "weeklyRemainingPercent") ?? 100.0;
                        a.WeeklyResetAtUtc = rwAt.Value;
                        _syncAnchor = a;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _suppressSave = false;
            }
        }

        private static List<string> ReadStringList(List<object> rawList)
        {
            List<string> res = new List<string>();
            if (rawList == null) return res;
            for (int i = 0; i < rawList.Count; i++)
            {
                string s = rawList[i] as string;
                if (!string.IsNullOrEmpty(s)) res.Add(s);
            }
            return res;
        }

        private void SaveAndNotify()
        {
            if (_suppressSave) return;
            SaveOnly();
            Action h = Changed;
            if (h != null) h();
        }

        private void SaveOnly()
        {
            if (_suppressSave) return;
            try
            {
                string dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["planPreset"] = _planPreset.ToString().ToLowerInvariant();
                root["capacity5hUnits"] = _capacity5hUnits;
                root["capacityWeeklyUnits"] = _capacityWeeklyUnits;
                root["maxContextTokens"] = _maxContextTokens;
                root["notifyOnComplete"] = _notifyOnComplete;
                root["minTurnDurationForAlert"] = _minTurnDurationForAlert;
                root["notifyOnWaitingOrError"] = _notifyOnWaitingOrError;
                root["notifyOnLowRemaining"] = _notifyOnLowRemaining;
                root["lowQuotaThreshold"] = _lowQuotaThreshold;
                root["lowContextThreshold"] = _lowContextThreshold;
                root["soundEnabled"] = _soundEnabled;
                root["alwaysOnTop"] = _alwaysOnTop;
                root["pillOpacity"] = _pillOpacity;
                if (_panelAnchorMaxX.HasValue) root["panelAnchorMaxX"] = _panelAnchorMaxX.Value;
                if (_panelAnchorMaxY.HasValue) root["panelAnchorMaxY"] = _panelAnchorMaxY.Value;
                if (!string.IsNullOrEmpty(_selectedAccountEmail)) root["selectedAccountEmail"] = _selectedAccountEmail;
                root["pinnedConversationIds"] = _pinnedIds;
                root["unreadCompletedConversationIds"] = _unreadCompletedIds;
                root["deletedConversationIds"] = _deletedIds;

                if (_syncAnchor != null)
                {
                    Dictionary<string, object> ad = new Dictionary<string, object>();
                    ad["syncedAt"] = _syncAnchor.SyncedAtUtc.ToString("o", CultureInfo.InvariantCulture);
                    ad["fiveHourRemainingPercent"] = _syncAnchor.FiveHourRemainingPercent;
                    ad["fiveHourResetAt"] = _syncAnchor.FiveHourResetAtUtc.ToString("o", CultureInfo.InvariantCulture);
                    ad["weeklyRemainingPercent"] = _syncAnchor.WeeklyRemainingPercent;
                    ad["weeklyResetAt"] = _syncAnchor.WeeklyResetAtUtc.ToString("o", CultureInfo.InvariantCulture);
                    root["syncAnchor"] = ad;
                }

                string json = MiniJson.Serialize(root);
                string tmp = _settingsFilePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(_settingsFilePath))
                {
                    File.Delete(_settingsFilePath);
                }
                File.Move(tmp, _settingsFilePath);
            }
            catch
            {
            }
        }
    }

    // MARK: - Formatting Helpers (UTC+8 Beijing Time)

    public static class TimeFormatters
    {
        public static DateTime ToUtc8(DateTime utcDate)
        {
            DateTime u = utcDate.Kind == DateTimeKind.Local ? utcDate.ToUniversalTime() : utcDate;
            return u.AddHours(8.0);
        }

        public static DateTime StartOfTodayUtc8AsUtc(DateTime nowUtc)
        {
            DateTime utc8Now = ToUtc8(nowUtc);
            DateTime utc8Midnight = new DateTime(utc8Now.Year, utc8Now.Month, utc8Now.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return DateTime.SpecifyKind(utc8Midnight.AddHours(-8.0), DateTimeKind.Utc);
        }

        public static string FormatUtc8(DateTime utcDate, bool includeSeconds)
        {
            DateTime d = ToUtc8(utcDate);
            return (includeSeconds ? d.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : d.ToString("HH:mm", CultureInfo.InvariantCulture)) + " (UTC+8)";
        }

        public static string FormatDateTimeUtc8(DateTime utcDate)
        {
            DateTime d = ToUtc8(utcDate);
            return d.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " (UTC+8)";
        }

        public static string ChineseWeekdayUtc8(DateTime utcDate)
        {
            DateTime d = ToUtc8(utcDate);
            switch (d.DayOfWeek)
            {
                case DayOfWeek.Sunday: return "星期日";
                case DayOfWeek.Monday: return "星期一";
                case DayOfWeek.Tuesday: return "星期二";
                case DayOfWeek.Wednesday: return "星期三";
                case DayOfWeek.Thursday: return "星期四";
                case DayOfWeek.Friday: return "星期五";
                case DayOfWeek.Saturday: return "星期六";
                default: return "星期一";
            }
        }

        public static void DayBucketUtc8(DateTime targetUtc, DateTime nowUtc, out string key, out string label)
        {
            DateTime todayDate = ToUtc8(nowUtc).Date;
            DateTime targetDate = ToUtc8(targetUtc).Date;
            int daysAgo = (int)(todayDate - targetDate).TotalDays;

            if (daysAgo <= 0)
            {
                key = "day-0";
                label = "今天";
            }
            else if (daysAgo == 1)
            {
                key = "day-1";
                label = "昨天";
            }
            else if (daysAgo >= 2 && daysAgo <= 6)
            {
                key = "day-" + daysAgo.ToString(CultureInfo.InvariantCulture);
                label = ChineseWeekdayUtc8(targetUtc);
            }
            else
            {
                key = "older";
                label = "更早";
            }
        }

        public static string FormatDuration(double seconds)
        {
            int total = Math.Max(0, (int)Math.Round(seconds));
            int d = total / 86400;
            int h = (total % 86400) / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (d > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}天 {1}小时", d, h);
            }
            if (h > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}小时 {1}分", h, m);
            }
            if (m > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s", m, s);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}s", s);
        }

        public static string FormatCompactTokens(int count)
        {
            if (count >= 1000000)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00}M", count / 1000000.0);
            }
            if (count >= 1000)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0}K", count / 1000.0);
            }
            return count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
