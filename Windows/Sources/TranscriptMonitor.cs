using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AntigravityAssistant
{
    internal sealed class SummaryDbEntry
    {
        public string Title;
        public string ProjectName;
        public bool IsRunningInDb;
        public bool IsIdleInDb;
        public int NestingDepth;
        public bool Killed;
    }

    internal sealed class CachedTranscript
    {
        public long MtimeTicksUtc;
        public long FileSize;
        public ConversationSnapshot RawSnapshot;
        public List<InvocationRecord> Invocations;
    }

    // MARK: - Main Monitor ViewModel (Windows Port of TranscriptMonitor.swift)

    public sealed class TranscriptMonitor
    {
        public event Action StateChanged;
        public event Action ClockTicked;

        public List<ConversationSnapshot> Conversations { get; private set; }
        public HashSet<string> PinnedConversationIds { get; private set; }
        public HashSet<string> UnreadCompletedConversationIds { get; private set; }
        public HashSet<string> DeletedConversationIds { get; private set; }
        public QuotaMetrics Quota { get; private set; }
        public List<CloudAccountQuota> CloudAccounts { get; private set; }
        public bool IsRefreshingCloud { get; private set; }
        public bool IsSigningInGoogle { get; private set; }
        public DateTime LastRefreshDateUtc { get; private set; }
        public DateTime NowTickUtc { get; private set; }
        public bool IsHookPluginInstalled { get; private set; }
        public HookLiveEvent LatestHookEvent { get; private set; }
        public bool IsAntigravityInstalled { get; private set; }
        public string ToastMessage { get; private set; }

        public readonly AppSettings Settings;

        private readonly Dictionary<string, CachedTranscript> _cache = new Dictionary<string, CachedTranscript>(StringComparer.OrdinalIgnoreCase);
        private string _summariesCacheKey = string.Empty;
        private Dictionary<string, SummaryDbEntry> _summariesCache = new Dictionary<string, SummaryDbEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AgentPhase> _previousPhases = new Dictionary<string, AgentPhase>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _previousBackgroundBusy = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _previousUserTurnCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _deletingConversationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _hasCompletedInitialScan;
        private bool _isPollingLiveUI;
        private bool _isJumping;
        private LiveAntigravityUIState _liveUIState;
        private DateTime _lastCliFallbackPollAtUtc = DateTime.MinValue;
        private CancellationTokenSource _signInCts;

        private DispatcherTimer _scanTimer;
        private DispatcherTimer _liveUITimer;
        private DispatcherTimer _magpieTimer;
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _toastTimer;

        private readonly string _antigravityRootDir;
        private readonly string _brainDir;
        private readonly string _conversationsDbDir;
        private readonly string _summariesDbPath;
        private readonly string _hookStatePath;
        private readonly string _hookPluginDir;

        public TranscriptMonitor(AppSettings settings)
        {
            Settings = settings;
            Conversations = new List<ConversationSnapshot>();
            PinnedConversationIds = new HashSet<string>(settings.PinnedIds, StringComparer.OrdinalIgnoreCase);
            UnreadCompletedConversationIds = new HashSet<string>(settings.UnreadCompletedIds, StringComparer.OrdinalIgnoreCase);
            DeletedConversationIds = new HashSet<string>(settings.DeletedIds, StringComparer.OrdinalIgnoreCase);
            Quota = QuotaMetrics.Empty();
            CloudAccounts = new List<CloudAccountQuota>();
            LastRefreshDateUtc = DateTime.UtcNow;
            NowTickUtc = DateTime.UtcNow;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _antigravityRootDir = Path.Combine(home, ".gemini", "antigravity");
            _brainDir = Path.Combine(_antigravityRootDir, "brain");
            _conversationsDbDir = Path.Combine(_antigravityRootDir, "conversations");
            _summariesDbPath = Path.Combine(_antigravityRootDir, "conversation_summaries.db");
            _hookStatePath = Path.Combine(AppSettings.AppDataFolder, "hook_state.json");
            _hookPluginDir = Path.Combine(home, ".gemini", "config", "plugins", "agy-assistant-hooks");

            IsAntigravityInstalled = ConversationNavigator.ResolveAntigravityExePath() != null;
            CheckHookPluginInstalled();

            NotificationManager.Shared.OnNotificationJumpRequested = delegate(string cid)
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(delegate { JumpToConversationId(cid); }));
                }
            };

            Settings.Changed += delegate
            {
                RecomputeQuota(null, DateTime.UtcNow);
                NotifyStateChanged();
            };
        }

        public string SelectedAccountEmail
        {
            get { return Settings.SelectedAccountEmail; }
            set
            {
                Settings.SelectedAccountEmail = value;
                RecomputeQuota(null, DateTime.UtcNow);
                NotifyStateChanged();
            }
        }

        public ConversationSnapshot PrimaryConversation
        {
            get
            {
                for (int i = 0; i < Conversations.Count; i++)
                {
                    if (Conversations[i].Phase.IsWorking) return Conversations[i];
                }
                for (int i = 0; i < Conversations.Count; i++)
                {
                    if (Conversations[i].Phase.Kind == AgentPhaseKind.WaitingUser) return Conversations[i];
                }
                for (int i = 0; i < Conversations.Count; i++)
                {
                    if (PinnedConversationIds.Contains(Conversations[i].Id)) return Conversations[i];
                }
                return Conversations.Count > 0 ? Conversations[0] : null;
            }
        }

        public CloudAccountQuota ActiveCloudAccount
        {
            get
            {
                string email = Settings.SelectedAccountEmail;
                if (!string.IsNullOrEmpty(email))
                {
                    for (int i = 0; i < CloudAccounts.Count; i++)
                    {
                        if (string.Equals(CloudAccounts[i].UserEmail, email, StringComparison.OrdinalIgnoreCase))
                            return CloudAccounts[i];
                    }
                }
                for (int i = 0; i < CloudAccounts.Count; i++)
                {
                    if (CloudAccounts[i].IsLatestActive) return CloudAccounts[i];
                }
                return CloudAccounts.Count > 0 ? CloudAccounts[0] : null;
            }
        }

        public CloudPoolQuota ActivePoolForPrimarySession
        {
            get
            {
                CloudAccountQuota acct = ActiveCloudAccount;
                if (acct == null) return null;
                string modelLower = (PrimaryConversation != null ? PrimaryConversation.ModelName : "gemini").ToLowerInvariant();
                bool isClaudeOrGpt = modelLower.Contains("claude") || modelLower.Contains("gpt") || modelLower.Contains("sonnet") || modelLower.Contains("opus");
                if (isClaudeOrGpt && acct.ClaudeGptPool != null) return acct.ClaudeGptPool;
                return acct.GeminiPool ?? acct.ClaudeGptPool;
            }
        }

        public int WorkingConversationCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Conversations.Count; i++)
                {
                    if (Conversations[i].Phase.IsWorking) count++;
                }
                return count;
            }
        }

        private int PrioritySortRank(ConversationSnapshot conv)
        {
            if (conv.Phase.IsPriorityActive || conv.BackgroundBusy) return 0;
            if (UnreadCompletedConversationIds.Contains(conv.Id)) return 1;
            return 2;
        }

        public List<ConversationSnapshot> PriorityConversations
        {
            get
            {
                List<ConversationSnapshot> items = new List<ConversationSnapshot>();
                for (int i = 0; i < Conversations.Count; i++)
                {
                    ConversationSnapshot c = Conversations[i];
                    if (c.Phase.IsPriorityActive
                        || c.BackgroundBusy
                        || PinnedConversationIds.Contains(c.Id)
                        || UnreadCompletedConversationIds.Contains(c.Id))
                    {
                        items.Add(c);
                    }
                }
                items.Sort(ComparePriorityItems);
                return items;
            }
        }

        public List<ConversationSnapshot> RunningPriorityConversations
        {
            get
            {
                List<ConversationSnapshot> all = PriorityConversations;
                List<ConversationSnapshot> running = new List<ConversationSnapshot>();
                for (int i = 0; i < all.Count; i++)
                {
                    ConversationSnapshot c = all[i];
                    if (c.Phase.IsPriorityActive || c.BackgroundBusy || UnreadCompletedConversationIds.Contains(c.Id))
                    {
                        running.Add(c);
                    }
                }
                return running;
            }
        }

        private int ComparePriorityItems(ConversationSnapshot lhs, ConversationSnapshot rhs)
        {
            int lRank = PrioritySortRank(lhs);
            int rRank = PrioritySortRank(rhs);
            if (lRank != rRank) return lRank.CompareTo(rRank);
            return rhs.LastUpdatedAtUtc.CompareTo(lhs.LastUpdatedAtUtc);
        }

        public List<SessionGroupSection> GroupedSessionSections
        {
            get
            {
                List<ConversationSnapshot> priorityItems = new List<ConversationSnapshot>();
                List<string> bucketOrder = new List<string>();
                Dictionary<string, string> bucketTitles = new Dictionary<string, string>(StringComparer.Ordinal);
                Dictionary<string, List<ConversationSnapshot>> bucketItems = new Dictionary<string, List<ConversationSnapshot>>(StringComparer.Ordinal);

                for (int i = 0; i < Conversations.Count; i++)
                {
                    ConversationSnapshot conv = Conversations[i];
                    bool isPinned = PinnedConversationIds.Contains(conv.Id);
                    bool isUnreadDone = UnreadCompletedConversationIds.Contains(conv.Id);
                    if (conv.Phase.IsPriorityActive || conv.BackgroundBusy || isPinned || isUnreadDone)
                    {
                        priorityItems.Add(conv);
                    }
                    else
                    {
                        string bKey;
                        string bLabel;
                        TimeFormatters.DayBucketUtc8(conv.LastUpdatedAtUtc, NowTickUtc, out bKey, out bLabel);
                        List<ConversationSnapshot> list;
                        if (!bucketItems.TryGetValue(bKey, out list))
                        {
                            list = new List<ConversationSnapshot>();
                            bucketOrder.Add(bKey);
                            bucketTitles[bKey] = bLabel;
                            bucketItems[bKey] = list;
                        }
                        list.Add(conv);
                    }
                }

                priorityItems.Sort(ComparePriorityItems);

                List<SessionGroupSection> sections = new List<SessionGroupSection>();
                if (priorityItems.Count > 0)
                {
                    sections.Add(new SessionGroupSection
                    {
                        Id = "priority",
                        Title = "优先事项",
                        IsPriority = true,
                        Conversations = priorityItems
                    });
                }

                for (int i = 0; i < bucketOrder.Count; i++)
                {
                    string key = bucketOrder[i];
                    List<ConversationSnapshot> list = bucketItems[key];
                    if (list.Count > 0)
                    {
                        sections.Add(new SessionGroupSection
                        {
                            Id = key,
                            Title = bucketTitles[key],
                            IsPriority = false,
                            Conversations = list
                        });
                    }
                }
                return sections;
            }
        }

        public bool IsPinned(string convId)
        {
            return !string.IsNullOrEmpty(convId) && PinnedConversationIds.Contains(convId);
        }

        public bool IsUnreadCompleted(string convId)
        {
            return !string.IsNullOrEmpty(convId) && UnreadCompletedConversationIds.Contains(convId);
        }

        public void AcknowledgeUnreadCompleted(string convId)
        {
            if (!string.IsNullOrEmpty(convId) && UnreadCompletedConversationIds.Remove(convId))
            {
                PersistSets();
                NotifyStateChanged();
            }
        }

        public void TogglePin(ConversationSnapshot conv)
        {
            if (conv == null) return;
            UnreadCompletedConversationIds.Remove(conv.Id);
            if (PinnedConversationIds.Contains(conv.Id))
            {
                PinnedConversationIds.Remove(conv.Id);
                PersistSets();
                ShowToast("已取消固定该会话");
            }
            else
            {
                PinnedConversationIds.Add(conv.Id);
                PersistSets();
                ShowToast("📌 已固定到优先事项（共 " + PinnedConversationIds.Count + " 个固定）");
            }
            NotifyStateChanged();
        }

        public void ClearAllPins()
        {
            if (PinnedConversationIds.Count == 0) return;
            PinnedConversationIds.Clear();
            PersistSets();
            ShowToast("已取消全部固定会话");
            NotifyStateChanged();
        }

        private void PersistSets()
        {
            Settings.UpdateConversationSets(PinnedConversationIds, UnreadCompletedConversationIds, DeletedConversationIds);
        }

        // MARK: - Lifecycle Timers

        public void Start()
        {
            RefreshNow();
            Task _ = PollMagpieQuotaAsync(false);
            PollLiveAntigravityUI();

            Stop();

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            _scanTimer.Tick += delegate { RefreshNow(); };
            _scanTimer.Start();

            _liveUITimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
            _liveUITimer.Tick += delegate { PollLiveAntigravityUI(); };
            _liveUITimer.Start();

            _magpieTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10.0) };
            _magpieTimer.Tick += async delegate { await PollMagpieQuotaAsync(false); };
            _magpieTimer.Start();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
            _clockTimer.Tick += delegate
            {
                NowTickUtc = DateTime.UtcNow;
                Action h = ClockTicked;
                if (h != null) h();
            };
            _clockTimer.Start();
        }

        public void Stop()
        {
            if (_signInCts != null)
            {
                try { _signInCts.Cancel(); } catch { }
                _signInCts = null;
            }
            if (_scanTimer != null) _scanTimer.Stop();
            if (_liveUITimer != null) _liveUITimer.Stop();
            if (_magpieTimer != null) _magpieTimer.Stop();
            if (_clockTimer != null) _clockTimer.Stop();
        }

        private void PollLiveAntigravityUI()
        {
            if (_isPollingLiveUI) return;
            _isPollingLiveUI = true;
            Task.Run(delegate { return ConversationNavigator.PollLiveUIState(); })
                .ContinueWith(delegate(Task<LiveAntigravityUIState> t)
                {
                    _isPollingLiveUI = false;
                    if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                    {
                        if (_liveUIState == null || !_liveUIState.Equals(t.Result))
                        {
                            _liveUIState = t.Result;
                            RefreshNow();
                        }
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public async Task PollMagpieQuotaAsync(bool forceCloudRefresh)
        {
            if (forceCloudRefresh)
            {
                if (IsRefreshingCloud) return;
                IsRefreshingCloud = true;
                NotifyStateChanged();
                try
                {
                    List<CloudAccountQuota> refreshed = await MagpieQuotaClient.ForceRefreshFromCloudAsync();
                    if (refreshed != null && refreshed.Count > 0)
                    {
                        CloudAccounts = refreshed;
                        RecomputeQuota(null, DateTime.UtcNow);
                        CloudAccountQuota active = ActiveCloudAccount;
                        if (active != null && !string.IsNullOrEmpty(active.ErrorMessage))
                        {
                            ShowToast("⚠️ " + active.ErrorMessage);
                        }
                        else if (active != null && active.IsDirectGoogleAccount)
                        {
                            ShowToast("✅ 已从 Google 官方接口刷新实时配额");
                        }
                        else
                        {
                            ShowToast("✅ 已从云端刷新真实配额");
                        }
                        return;
                    }
                    List<CloudAccountQuota> fetched = await MagpieQuotaClient.FetchAntigravityAccountsAsync(null, true);
                    if (fetched != null && fetched.Count > 0)
                    {
                        CloudAccounts = fetched;
                        RecomputeQuota(null, DateTime.UtcNow);
                        ShowToast("已同步 Magpie 本地网关配额");
                        return;
                    }
                    ShowToast("尚未连接 Google 配额账号，请点击「Google 登录直连」");
                }
                finally
                {
                    IsRefreshingCloud = false;
                    NotifyStateChanged();
                }
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool allowCli = (now - _lastCliFallbackPollAtUtc).TotalSeconds >= 60.0;
            if (allowCli) _lastCliFallbackPollAtUtc = now;

            List<CloudAccountQuota> accounts = await MagpieQuotaClient.FetchAntigravityAccountsAsync(null, allowCli);
            if (accounts != null && !CloudAccountListsEqual(CloudAccounts, accounts))
            {
                CloudAccounts = accounts;
                RecomputeQuota(null, DateTime.UtcNow);
                NotifyStateChanged();
            }
            else if (accounts == null && CloudAccounts.Count > 0 && !MagpieQuotaClient.HasDirectGoogleAccounts)
            {
                CloudAccounts = new List<CloudAccountQuota>();
                RecomputeQuota(null, DateTime.UtcNow);
                NotifyStateChanged();
            }
        }

        public void CancelGoogleSignIn()
        {
            if (_signInCts != null)
            {
                try { _signInCts.Cancel(); } catch { }
                _signInCts = null;
            }
            if (IsSigningInGoogle)
            {
                IsSigningInGoogle = false;
                ShowToast("已取消浏览器授权等待");
                NotifyStateChanged();
            }
        }

        public async Task SignInGoogleDirectAsync()
        {
            if (IsSigningInGoogle)
            {
                CancelGoogleSignIn();
                return;
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            _signInCts = cts;
            IsSigningInGoogle = true;
            ShowToast("🌐 已打开浏览器，请完成 Google 账号授权…");
            NotifyStateChanged();

            try
            {
                AccountSignInOutcome outcome = await MagpieQuotaClient.SignInWithGoogleBrowserAsync(cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                if (outcome != null && outcome.Success)
                {
                    if (!string.IsNullOrEmpty(outcome.UserEmail))
                    {
                        Settings.SelectedAccountEmail = outcome.UserEmail;
                    }
                    List<CloudAccountQuota> refreshed = await MagpieQuotaClient.ForceRefreshFromCloudAsync();
                    if (refreshed != null)
                    {
                        CloudAccounts = refreshed;
                    }
                    RecomputeQuota(null, DateTime.UtcNow);
                    ShowToast("✅ 已连接 " + (outcome.UserEmail ?? "Google 账号") + " 实盘配额！");
                }
                else
                {
                    string err = outcome != null && !string.IsNullOrEmpty(outcome.ErrorMessage)
                        ? outcome.ErrorMessage
                        : "Google 登录未完成";
                    ShowToast("⚠️ " + err);
                }
            }
            finally
            {
                if (ReferenceEquals(_signInCts, cts))
                {
                    _signInCts = null;
                }
                IsSigningInGoogle = false;
                NotifyStateChanged();
            }
        }

        public async Task SignOutGoogleAccountAsync(string email)
        {
            if (string.IsNullOrEmpty(email)) return;
            bool removed = MagpieQuotaClient.RemoveDirectAccount(email);
            if (!removed) return;

            if (string.Equals(Settings.SelectedAccountEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                Settings.SelectedAccountEmail = string.Empty;
            }

            List<CloudAccountQuota> remaining = await MagpieQuotaClient.FetchAntigravityAccountsAsync(null, false);
            CloudAccounts = remaining ?? new List<CloudAccountQuota>();
            RecomputeQuota(null, DateTime.UtcNow);
            ShowToast("已退出账号 " + email);
            NotifyStateChanged();
        }

        private static bool CloudAccountListsEqual(List<CloudAccountQuota> a, List<CloudAccountQuota> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }
            return true;
        }

        public void ShowToast(string text)
        {
            ToastMessage = text;
            NotifyStateChanged();

            if (Application.Current != null && Application.Current.Dispatcher != null && Application.Current.Dispatcher.CheckAccess())
            {
                if (_toastTimer != null) _toastTimer.Stop();
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.3) };
                _toastTimer.Tick += delegate
                {
                    _toastTimer.Stop();
                    if (string.Equals(ToastMessage, text, StringComparison.Ordinal))
                    {
                        ToastMessage = null;
                        NotifyStateChanged();
                    }
                };
                _toastTimer.Start();
            }
        }

        // MARK: - Refresh Pipeline

        public void RefreshNow()
        {
            DateTime nowUtc = DateTime.UtcNow;
            NowTickUtc = nowUtc;
            DateTime sevenDaysAgoUtc = nowUtc.AddDays(-7.5);

            HookLiveEvent hookEvent = LoadHookState();
            LatestHookEvent = hookEvent;
            Dictionary<string, SummaryDbEntry> summaryMap = LoadConversationSummariesIfNeeded();

            if (!Directory.Exists(_brainDir))
            {
                LastRefreshDateUtc = nowUtc;
                NotifyStateChanged();
                return;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(_brainDir);
            }
            catch
            {
                return;
            }

            List<ConversationSnapshot> updatedSnapshots = new List<ConversationSnapshot>();
            List<InvocationRecord> allInvocations = new List<InvocationRecord>();

            for (int i = 0; i < folders.Length; i++)
            {
                string folder = folders[i];
                string convId = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(convId) || convId.Length < 32) continue;
                if (string.Equals(convId, "tempmediaStorage", StringComparison.OrdinalIgnoreCase)) continue;
                if (DeletedConversationIds.Contains(convId)) continue;

                SummaryDbEntry summaryEntry;
                summaryMap.TryGetValue(convId, out summaryEntry);
                if (summaryEntry != null && (summaryEntry.NestingDepth > 0 || summaryEntry.Killed))
                {
                    continue;
                }

                string transcriptPath = Path.Combine(folder, ".system_generated", "logs", "transcript.jsonl");
                FileInfo fi = new FileInfo(transcriptPath);
                if (!fi.Exists) continue;

                DateTime mtimeUtc = fi.LastWriteTimeUtc;
                if (mtimeUtc < sevenDaysAgoUtc) continue;

                long mtimeTicks = mtimeUtc.Ticks;
                long fileSize = fi.Length;

                ConversationSnapshot rawSnap;
                List<InvocationRecord> invList;

                CachedTranscript cached;
                if (_cache.TryGetValue(convId, out cached) && cached.MtimeTicksUtc == mtimeTicks && cached.FileSize == fileSize)
                {
                    rawSnap = cached.RawSnapshot;
                    invList = cached.Invocations;
                }
                else
                {
                    CachedTranscript newlyParsed = ParseTranscriptFile(convId, transcriptPath, mtimeUtc, fileSize);
                    if (newlyParsed == null) continue;
                    _cache[convId] = newlyParsed;
                    rawSnap = newlyParsed.RawSnapshot;
                    invList = newlyParsed.Invocations;
                }

                ConversationSnapshot effectiveSnap = AdjustPhaseForStaleness(
                    rawSnap,
                    mtimeUtc,
                    summaryEntry,
                    hookEvent,
                    _liveUIState,
                    nowUtc);

                updatedSnapshots.Add(effectiveSnap);
                allInvocations.AddRange(invList);
            }

            updatedSnapshots.Sort(delegate(ConversationSnapshot a, ConversationSnapshot b)
            {
                return b.LastUpdatedAtUtc.CompareTo(a.LastUpdatedAtUtc);
            });

            Conversations = updatedSnapshots;
            LastRefreshDateUtc = nowUtc;

            RecomputeQuota(allInvocations, nowUtc);
            EvaluatePhaseTransitions(updatedSnapshots, nowUtc);
            _hasCompletedInitialScan = true;
            NotifyStateChanged();
        }

        private void RecomputeQuota(List<InvocationRecord> explicitInvocations, DateTime nowUtc)
        {
            List<InvocationRecord> allInvocations = explicitInvocations;
            if (allInvocations == null)
            {
                allInvocations = new List<InvocationRecord>();
                foreach (CachedTranscript ct in _cache.Values)
                {
                    allInvocations.AddRange(ct.Invocations);
                }
            }

            List<InvocationRecord> geminiInvocations = new List<InvocationRecord>();
            for (int i = 0; i < allInvocations.Count; i++)
            {
                if (allInvocations[i].IsGeminiFamily) geminiInvocations.Add(allInvocations[i]);
            }
            geminiInvocations.Sort(delegate(InvocationRecord a, InvocationRecord b)
            {
                return a.TimestampUtc.CompareTo(b.TimestampUtc);
            });

            double cap5h = Math.Max(100, Settings.Capacity5hUnits);
            double capWeekly = Math.Max(300, Settings.CapacityWeeklyUnits);
            QuotaSyncAnchor anchor = Settings.SyncAnchor;

            double fiveHourPct;
            DateTime? fiveHourResetAt;
            int fiveHourSteps;
            int fiveHourTokens;
            bool is5hSynced;

            double weeklyPct;
            DateTime? weeklyResetAt;
            int weeklySteps;
            bool isWeeklySynced;

            CloudPoolQuota cloudPool = ActivePoolForPrimarySession;
            if (cloudPool != null)
            {
                DateTime bStart5h = (cloudPool.ResetsAt5hUtc ?? nowUtc).AddHours(-5.0);
                DateTime bStart7d = (cloudPool.ResetsAt7dUtc ?? nowUtc).AddDays(-7.0);

                fiveHourSteps = 0;
                fiveHourTokens = 0;
                weeklySteps = 0;
                for (int i = 0; i < geminiInvocations.Count; i++)
                {
                    InvocationRecord inv = geminiInvocations[i];
                    if (inv.TimestampUtc >= bStart5h && inv.TimestampUtc <= nowUtc)
                    {
                        fiveHourSteps++;
                        fiveHourTokens += inv.TotalTokens;
                    }
                    if (inv.TimestampUtc >= bStart7d && inv.TimestampUtc <= nowUtc)
                    {
                        weeklySteps++;
                    }
                }

                fiveHourPct = Math.Max(0.0, Math.Min(100.0, cloudPool.Remaining5h));
                fiveHourResetAt = cloudPool.ResetsAt5hUtc;
                is5hSynced = true;

                weeklyPct = Math.Max(0.0, Math.Min(100.0, cloudPool.Remaining7d));
                weeklyResetAt = cloudPool.ResetsAt7dUtc;
                isWeeklySynced = true;
            }
            else
            {
                if (anchor != null && nowUtc < anchor.FiveHourResetAtUtc)
                {
                    DateTime bStart = anchor.FiveHourResetAtUtc.AddHours(-5.0);
                    fiveHourSteps = 0;
                    fiveHourTokens = 0;
                    double deltaWeight = 0.0;
                    for (int i = 0; i < geminiInvocations.Count; i++)
                    {
                        InvocationRecord inv = geminiInvocations[i];
                        if (inv.TimestampUtc >= bStart && inv.TimestampUtc <= nowUtc)
                        {
                            fiveHourSteps++;
                            fiveHourTokens += inv.TotalTokens;
                        }
                        if (inv.TimestampUtc > anchor.SyncedAtUtc && inv.TimestampUtc <= nowUtc)
                        {
                            deltaWeight += inv.CreditWeight;
                        }
                    }
                    fiveHourPct = Math.Max(0.0, Math.Min(100.0, anchor.FiveHourRemainingPercent - (deltaWeight / cap5h) * 100.0));
                    fiveHourResetAt = anchor.FiveHourResetAtUtc;
                    is5hSynced = true;
                }
                else
                {
                    DateTime cutoff = anchor != null ? anchor.FiveHourResetAtUtc : nowUtc.AddDays(-7.0);
                    double usedW;
                    SimulateFixedBucket(geminiInvocations, cutoff, 5.0 * 3600.0, nowUtc, out usedW, out fiveHourSteps, out fiveHourTokens, out fiveHourResetAt);
                    fiveHourPct = Math.Max(0.0, Math.Min(100.0, 100.0 - (usedW / cap5h) * 100.0));
                    is5hSynced = false;
                }

                if (anchor != null && nowUtc < anchor.WeeklyResetAtUtc)
                {
                    DateTime bStart = anchor.WeeklyResetAtUtc.AddDays(-7.0);
                    weeklySteps = 0;
                    double deltaWeight = 0.0;
                    for (int i = 0; i < geminiInvocations.Count; i++)
                    {
                        InvocationRecord inv = geminiInvocations[i];
                        if (inv.TimestampUtc >= bStart && inv.TimestampUtc <= nowUtc) weeklySteps++;
                        if (inv.TimestampUtc > anchor.SyncedAtUtc && inv.TimestampUtc <= nowUtc) deltaWeight += inv.CreditWeight;
                    }
                    weeklyPct = Math.Max(0.0, Math.Min(100.0, anchor.WeeklyRemainingPercent - (deltaWeight / capWeekly) * 100.0));
                    weeklyResetAt = anchor.WeeklyResetAtUtc;
                    isWeeklySynced = true;
                }
                else
                {
                    DateTime cutoff = anchor != null ? anchor.WeeklyResetAtUtc : nowUtc.AddDays(-14.0);
                    double usedW;
                    int dummyTok;
                    SimulateFixedBucket(geminiInvocations, cutoff, 7.0 * 86400.0, nowUtc, out usedW, out weeklySteps, out dummyTok, out weeklyResetAt);
                    weeklyPct = Math.Max(0.0, Math.Min(100.0, 100.0 - (usedW / capWeekly) * 100.0));
                    isWeeklySynced = false;
                }
            }

            DateTime startOfTodayUtc = TimeFormatters.StartOfTodayUtc8AsUtc(nowUtc);
            int todayInvCount = 0;
            int todayTokens = 0;
            HashSet<string> todayConvIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allInvocations.Count; i++)
            {
                InvocationRecord inv = allInvocations[i];
                if (inv.TimestampUtc >= startOfTodayUtc)
                {
                    todayInvCount++;
                    todayTokens += inv.TotalTokens;
                    todayConvIds.Add(inv.ConversationId);
                }
            }

            int todayTurns = 0;
            for (int i = 0; i < Conversations.Count; i++)
            {
                if (Conversations[i].LastUpdatedAtUtc >= startOfTodayUtc)
                {
                    todayTurns += Conversations[i].UserTurnCount;
                }
            }

            Quota = new QuotaMetrics
            {
                FiveHourRemainingPercent = fiveHourPct,
                FiveHourResetDateUtc = fiveHourResetAt,
                FiveHourStepsUsed = fiveHourSteps,
                FiveHourTokensUsed = fiveHourTokens,
                IsFiveHourSynced = is5hSynced,
                WeeklyRemainingPercent = weeklyPct,
                WeeklyResetDateUtc = weeklyResetAt,
                WeeklyStepsUsed = weeklySteps,
                IsWeeklySynced = isWeeklySynced,
                TodayInvocations = todayInvCount,
                TodayUserTurns = todayTurns,
                TodayTokens = todayTokens,
                TodayConversations = todayConvIds.Count
            };

            if (_hasCompletedInitialScan)
            {
                NotificationManager.Shared.NotifyLowQuotaIfNeeded(Math.Min(fiveHourPct, weeklyPct), Settings);
            }
        }

        private static void SimulateFixedBucket(
            List<InvocationRecord> sortedInvocations,
            DateTime cutoffUtc,
            double windowSeconds,
            DateTime nowUtc,
            out double usedWeight,
            out int stepCount,
            out int tokenCount,
            out DateTime? resetDateUtc)
        {
            DateTime? bucketStart = null;
            double w = 0.0;
            int steps = 0;
            int tokens = 0;

            for (int i = 0; i < sortedInvocations.Count; i++)
            {
                InvocationRecord inv = sortedInvocations[i];
                if (inv.TimestampUtc < cutoffUtc || inv.TimestampUtc > nowUtc) continue;

                if (bucketStart.HasValue && inv.TimestampUtc < bucketStart.Value.AddSeconds(windowSeconds))
                {
                    w += inv.CreditWeight;
                    steps++;
                    tokens += inv.TotalTokens;
                }
                else
                {
                    bucketStart = inv.TimestampUtc;
                    w = inv.CreditWeight;
                    steps = 1;
                    tokens = inv.TotalTokens;
                }
            }

            if (bucketStart.HasValue)
            {
                DateTime end = bucketStart.Value.AddSeconds(windowSeconds);
                if (nowUtc < end)
                {
                    usedWeight = w;
                    stepCount = steps;
                    tokenCount = tokens;
                    resetDateUtc = end;
                    return;
                }
            }

            usedWeight = 0.0;
            stepCount = 0;
            tokenCount = 0;
            resetDateUtc = null;
        }

        public void SyncQuotaFromClipboard()
        {
            Task ignored = SyncOrImportFromClipboardAsync();
        }

        public async Task SyncOrImportFromClipboardAsync()
        {
            string str = null;
            try
            {
                if (Clipboard.ContainsText())
                {
                    str = Clipboard.GetText();
                }
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(str))
            {
                ShowToast("剪贴板为空，请先复制 Token/JSON 或 Usage 文本");
                return;
            }

            if (MagpieQuotaClient.LooksLikeAccountImportText(str))
            {
                if (IsRefreshingCloud) return;
                IsRefreshingCloud = true;
                ShowToast("⏳ 正在验证并导入剪贴板中的账号凭证…");
                NotifyStateChanged();
                try
                {
                    AccountSignInOutcome outcome = await MagpieQuotaClient.TryImportAccountFromTextAsync(str);
                    if (outcome != null && outcome.Success)
                    {
                        if (!string.IsNullOrEmpty(outcome.UserEmail))
                        {
                            Settings.SelectedAccountEmail = outcome.UserEmail;
                        }
                        List<CloudAccountQuota> refreshed = await MagpieQuotaClient.ForceRefreshFromCloudAsync();
                        if (refreshed != null)
                        {
                            CloudAccounts = refreshed;
                        }
                        RecomputeQuota(null, DateTime.UtcNow);
                        ShowToast("✅ 已导入 " + (outcome.UserEmail ?? "Google 账号") + " 实盘配额！");
                    }
                    else
                    {
                        ShowToast("⚠️ " + (outcome != null && !string.IsNullOrEmpty(outcome.ErrorMessage) ? outcome.ErrorMessage : "Token 导入失败"));
                    }
                }
                finally
                {
                    IsRefreshingCloud = false;
                    NotifyStateChanged();
                }
                return;
            }

            if (Settings.SyncFromOfficialUsageText(str))
            {
                RecomputeQuota(null, DateTime.UtcNow);
                ShowToast("✅ 已从剪贴板识别并对齐官方 Usage！");
                NotifyStateChanged();
            }
            else
            {
                ShowToast("未识别到百分比或 1// Token，请复制 Usage 文字或账号 JSON");
            }
        }

        private void EvaluatePhaseTransitions(List<ConversationSnapshot> snapshots, DateTime nowUtc)
        {
            bool unreadChanged = false;

            for (int i = 0; i < snapshots.Count; i++)
            {
                ConversationSnapshot snap = snapshots[i];
                AgentPhase prev;
                _previousPhases.TryGetValue(snap.Id, out prev);
                bool prevBgBusy;
                _previousBackgroundBusy.TryGetValue(snap.Id, out prevBgBusy);
                int prevTurns;
                bool hasPrevTurns = _previousUserTurnCounts.TryGetValue(snap.Id, out prevTurns);

                _previousPhases[snap.Id] = snap.Phase;
                _previousBackgroundBusy[snap.Id] = snap.BackgroundBusy;
                _previousUserTurnCounts[snap.Id] = snap.UserTurnCount;

                bool isNowActive = snap.Phase.IsPriorityActive || snap.BackgroundBusy;
                if (isNowActive && UnreadCompletedConversationIds.Remove(snap.Id))
                {
                    unreadChanged = true;
                }

                if (!_hasCompletedInitialScan) continue;

                bool wasActive = (prev != null && prev.IsPriorityActive) || prevBgBusy;
                bool fastTurnCompleted = hasPrevTurns && snap.UserTurnCount > prevTurns && snap.Phase.Kind == AgentPhaseKind.Completed && !snap.BackgroundBusy;

                if ((wasActive || fastTurnCompleted) && snap.Phase.Kind == AgentPhaseKind.Completed && !snap.BackgroundBusy)
                {
                    if (!PinnedConversationIds.Contains(snap.Id) && UnreadCompletedConversationIds.Add(snap.Id))
                    {
                        unreadChanged = true;
                    }
                }

                if (prev == null || prev == snap.Phase) continue;

                switch (snap.Phase.Kind)
                {
                    case AgentPhaseKind.Completed:
                        if (prev.IsWorking)
                        {
                            double? dur = snap.ActiveTurnStartedAtUtc.HasValue
                                ? (double?)(nowUtc - snap.ActiveTurnStartedAtUtc.Value).TotalSeconds
                                : null;
                            NotificationManager.Shared.NotifyTurnCompleted(snap, dur, Settings);
                        }
                        break;
                    case AgentPhaseKind.WaitingUser:
                        NotificationManager.Shared.NotifyWaitingForUser(snap, snap.Phase.Detail, Settings);
                        break;
                    case AgentPhaseKind.Error:
                        NotificationManager.Shared.NotifyError(snap, snap.Phase.Detail, Settings);
                        break;
                }
            }

            if (unreadChanged)
            {
                PersistSets();
            }
        }

        // MARK: - Transcript JSONL Parser (Lock-Free Shared File Read)

        private static CachedTranscript ParseTranscriptFile(string convId, string path, DateTime mtimeUtc, long fileSize)
        {
            string content;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    content = sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrEmpty(content)) return null;

            string title = null;
            string searchQuery = null;
            string detectedModel = "Gemini";
            int userTurns = 0;
            DateTime? lastUserTurnAtUtc = null;
            int stepCount = 0;
            int latestInTokens = 0;
            int latestCacheTokens = 0;
            int totalOutTokens = 0;
            List<InvocationRecord> invocations = new List<InvocationRecord>();

            string lastStepType = string.Empty;
            string lastStepStatus = string.Empty;
            List<KeyValuePair<string, string>> lastPlannerToolCalls = new List<KeyValuePair<string, string>>();
            int genericCountSinceLastPlanner = 0;
            string lastErrorText = null;

            using (StringReader reader = new StringReader(content))
            {
                string rawLine;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    Dictionary<string, object> json = MiniJson.ParseObject(rawLine);
                    if (json == null) continue;

                    stepCount++;
                    string type = MiniJson.GetString(json, "type") ?? string.Empty;
                    string status = MiniJson.GetString(json, "status") ?? string.Empty;
                    DateTime createdAtUtc = MagpieQuotaClient.ParseAnyIsoDateUtc(MiniJson.GetString(json, "created_at")) ?? mtimeUtc;
                    int stepIdx = MiniJson.GetInt(json, "step_index", stepCount);

                    lastStepType = type;
                    lastStepStatus = status;

                    if (string.Equals(status, "ERROR", StringComparison.Ordinal))
                    {
                        string errContent = MiniJson.GetString(json, "content");
                        if (!string.IsNullOrEmpty(errContent))
                        {
                            lastErrorText = errContent.Length > 80 ? errContent.Substring(0, 80) : errContent;
                        }
                    }

                    if (string.Equals(type, "USER_INPUT", StringComparison.Ordinal))
                    {
                        userTurns++;
                        lastUserTurnAtUtc = createdAtUtc;
                        lastPlannerToolCalls.Clear();
                        genericCountSinceLastPlanner = 0;

                        string text = MiniJson.GetString(json, "content");
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (title == null)
                            {
                                string extracted = ExtractUserRequestLine(text);
                                if (!string.IsNullOrEmpty(extracted))
                                {
                                    searchQuery = extracted.Length > 26 ? extracted.Substring(0, 26) : extracted;
                                    title = extracted.Length > 34 ? (extracted.Substring(0, 34) + "…") : extracted;
                                }
                            }
                            string newModel = ExtractModelSelection(text);
                            if (!string.IsNullOrEmpty(newModel))
                            {
                                detectedModel = newModel;
                            }
                        }
                    }
                    else if (string.Equals(type, "PLANNER_RESPONSE", StringComparison.Ordinal))
                    {
                        int inTok = MiniJson.GetInt(json, "input_tokens", 0);
                        int cacheTok = MiniJson.GetInt(json, "cache_read_tokens", 0);
                        int outTok = MiniJson.GetInt(json, "output_tokens", 0);

                        if (inTok > 0 || cacheTok > 0)
                        {
                            latestInTokens = inTok;
                            latestCacheTokens = cacheTok;
                        }
                        totalOutTokens += outTok;

                        invocations.Add(new InvocationRecord
                        {
                            Id = convId + "-" + stepIdx.ToString(CultureInfo.InvariantCulture),
                            ConversationId = convId,
                            TimestampUtc = createdAtUtc,
                            ModelName = detectedModel,
                            InputTokens = inTok,
                            CacheReadTokens = cacheTok,
                            OutputTokens = outTok
                        });

                        lastPlannerToolCalls.Clear();
                        List<object> toolArr = MiniJson.GetList(json, "tool_calls");
                        if (toolArr != null)
                        {
                            for (int t = 0; t < toolArr.Count; t++)
                            {
                                Dictionary<string, object> toolObj = toolArr[t] as Dictionary<string, object>;
                                if (toolObj == null) continue;
                                string tName = MiniJson.GetString(toolObj, "name") ?? "tool";
                                Dictionary<string, object> args = MiniJson.GetDict(toolObj, "args");
                                string detail = ExtractToolDetail(args);
                                lastPlannerToolCalls.Add(new KeyValuePair<string, string>(tName, detail));
                            }
                        }
                        genericCountSinceLastPlanner = 0;
                    }
                    else if (string.Equals(type, "GENERIC", StringComparison.Ordinal))
                    {
                        genericCountSinceLastPlanner++;
                    }
                }
            }

            if (stepCount == 0) return null;

            AgentPhase phase;
            if (string.Equals(lastStepStatus, "ERROR", StringComparison.Ordinal))
            {
                phase = AgentPhase.Error(lastErrorText);
            }
            else if (string.Equals(lastStepType, "USER_INPUT", StringComparison.Ordinal))
            {
                phase = AgentPhase.Thinking;
            }
            else if (string.Equals(lastStepType, "PLANNER_RESPONSE", StringComparison.Ordinal))
            {
                if (lastPlannerToolCalls.Count == 0)
                {
                    phase = AgentPhase.Completed;
                }
                else
                {
                    int askIdx = -1;
                    for (int i = 0; i < lastPlannerToolCalls.Count; i++)
                    {
                        if (string.Equals(lastPlannerToolCalls[i].Key, "ask_question", StringComparison.Ordinal))
                        {
                            askIdx = i;
                            break;
                        }
                    }
                    if (askIdx >= 0)
                    {
                        phase = AgentPhase.WaitingUser(lastPlannerToolCalls[askIdx].Value ?? "Agent 发起了问题确认");
                    }
                    else
                    {
                        phase = AgentPhase.RunningTool(lastPlannerToolCalls[0].Key, lastPlannerToolCalls[0].Value);
                    }
                }
            }
            else if (string.Equals(lastStepType, "GENERIC", StringComparison.Ordinal))
            {
                if (lastPlannerToolCalls.Count > 0 && genericCountSinceLastPlanner < lastPlannerToolCalls.Count)
                {
                    KeyValuePair<string, string> nextTool = lastPlannerToolCalls[genericCountSinceLastPlanner];
                    if (string.Equals(nextTool.Key, "ask_question", StringComparison.Ordinal))
                    {
                        phase = AgentPhase.WaitingUser(nextTool.Value ?? "Agent 发起了问题确认");
                    }
                    else
                    {
                        phase = AgentPhase.RunningTool(nextTool.Key, nextTool.Value);
                    }
                }
                else
                {
                    phase = AgentPhase.Thinking;
                }
            }
            else
            {
                phase = AgentPhase.Idle;
            }

            ConversationSnapshot baseSnap = new ConversationSnapshot
            {
                Id = convId,
                Title = title ?? ("会话 " + (convId.Length >= 8 ? convId.Substring(0, 8) : convId)),
                ProjectName = "default",
                SearchQuery = searchQuery ?? string.Empty,
                ModelName = detectedModel,
                Phase = phase,
                LastUpdatedAtUtc = mtimeUtc,
                ActiveTurnStartedAtUtc = lastUserTurnAtUtc,
                StepCount = stepCount,
                UserTurnCount = userTurns,
                InvocationCount = invocations.Count,
                ContextUsedTokens = latestInTokens + latestCacheTokens,
                LatestInputTokens = latestInTokens,
                LatestCacheReadTokens = latestCacheTokens,
                TotalOutputTokens = totalOutTokens,
                BackgroundBusy = false
            };

            return new CachedTranscript
            {
                MtimeTicksUtc = mtimeUtc.Ticks,
                FileSize = fileSize,
                RawSnapshot = baseSnap,
                Invocations = invocations
            };
        }

        private static ConversationSnapshot AdjustPhaseForStaleness(
            ConversationSnapshot snapshot,
            DateTime mtimeUtc,
            SummaryDbEntry summaryEntry,
            HookLiveEvent hookEvent,
            LiveAntigravityUIState liveUI,
            DateTime nowUtc)
        {
            AgentPhase phase = snapshot.Phase;
            string title = snapshot.Title;
            string projectName = summaryEntry != null ? summaryEntry.ProjectName : snapshot.ProjectName;
            string model = snapshot.ModelName;
            bool bgBusy = snapshot.BackgroundBusy;

            double mtimeUnix = (mtimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            double nowUnix = (nowUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

            if (summaryEntry != null)
            {
                if (!string.IsNullOrEmpty(summaryEntry.Title))
                {
                    title = summaryEntry.Title;
                }
                if (phase.IsWorking && summaryEntry.IsIdleInDb && !summaryEntry.IsRunningInDb && (nowUtc - mtimeUtc).TotalSeconds >= 2.0)
                {
                    phase = AgentPhase.Interrupted;
                    bgBusy = false;
                }
            }
            if (string.IsNullOrEmpty(projectName)) projectName = "default";

            if (hookEvent != null
                && string.Equals(hookEvent.ConversationId, snapshot.Id, StringComparison.OrdinalIgnoreCase)
                && hookEvent.TimestampUnix >= mtimeUnix - 2.0)
            {
                if (!string.IsNullOrEmpty(hookEvent.ModelName) && !string.Equals(hookEvent.ModelName, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    model = hookEvent.ModelName;
                }
                switch (hookEvent.EventName)
                {
                    case "PreInvocation":
                    case "PostInvocation":
                        if (nowUnix - hookEvent.TimestampUnix < 300.0) phase = AgentPhase.Thinking;
                        break;
                    case "PreToolUse":
                        if (!string.IsNullOrEmpty(hookEvent.ToolName) && nowUnix - hookEvent.TimestampUnix < 300.0)
                        {
                            if (string.Equals(hookEvent.ToolName, "ask_question", StringComparison.Ordinal))
                                phase = AgentPhase.WaitingUser(hookEvent.ToolDetail);
                            else
                                phase = AgentPhase.RunningTool(hookEvent.ToolName, hookEvent.ToolDetail);
                        }
                        break;
                    case "Stop":
                        bgBusy = (hookEvent.FullyIdle == false);
                        if (string.Equals(hookEvent.TerminationReason, "ERROR", StringComparison.Ordinal))
                            phase = AgentPhase.Error(hookEvent.Error);
                        else if (string.Equals(hookEvent.TerminationReason, "MAX_TOKEN_BUDGET_EXCEEDED", StringComparison.Ordinal))
                            phase = AgentPhase.Error("Token 预算或配额已达上限");
                        else if (string.Equals(hookEvent.TerminationReason, "USER_CANCELED", StringComparison.Ordinal))
                            phase = AgentPhase.Interrupted;
                        else
                            phase = AgentPhase.Completed;
                        break;
                }
            }

            if (liveUI != null)
            {
                LiveSidebarItem item;
                if (liveUI.SidebarItems.TryGetValue(snapshot.Id, out item))
                {
                    if (!string.IsNullOrEmpty(item.Title)) title = item.Title;
                    if (phase.IsWorking && item.IsIdleInUI && !item.IsRunning && (nowUtc - mtimeUtc).TotalSeconds >= 2.0)
                    {
                        phase = AgentPhase.Interrupted;
                        bgBusy = false;
                    }
                }
                else if (string.Equals(liveUI.OpenConversationId, snapshot.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (liveUI.OpenConversationCancelledBanner && phase.IsWorking)
                    {
                        phase = AgentPhase.Interrupted;
                        bgBusy = false;
                    }
                    else if (phase.IsWorking && !liveUI.OpenConversationHasStopButton && (nowUtc - mtimeUtc).TotalSeconds >= 2.5)
                    {
                        phase = AgentPhase.Interrupted;
                        bgBusy = false;
                    }
                }
            }

            if (phase.IsWorking && (nowUtc - mtimeUtc).TotalSeconds > 240.0)
            {
                LiveSidebarItem item = null;
                if (liveUI != null) liveUI.SidebarItems.TryGetValue(snapshot.Id, out item);
                if (item == null || !item.IsRunning)
                {
                    phase = AgentPhase.Interrupted;
                }
            }

            return new ConversationSnapshot
            {
                Id = snapshot.Id,
                Title = title,
                ProjectName = projectName,
                SearchQuery = snapshot.SearchQuery,
                ModelName = model,
                Phase = phase,
                LastUpdatedAtUtc = mtimeUtc,
                ActiveTurnStartedAtUtc = snapshot.ActiveTurnStartedAtUtc,
                StepCount = snapshot.StepCount,
                UserTurnCount = snapshot.UserTurnCount,
                InvocationCount = snapshot.InvocationCount,
                ContextUsedTokens = snapshot.ContextUsedTokens,
                LatestInputTokens = snapshot.LatestInputTokens,
                LatestCacheReadTokens = snapshot.LatestCacheReadTokens,
                TotalOutputTokens = snapshot.TotalOutputTokens,
                BackgroundBusy = bgBusy
            };
        }

        private static string ExtractUserRequestLine(string raw)
        {
            string body = raw;
            const string startTag = "<USER_REQUEST>";
            const string endTag = "</USER_REQUEST>";
            int sIdx = raw.IndexOf(startTag, StringComparison.Ordinal);
            int eIdx = raw.IndexOf(endTag, StringComparison.Ordinal);
            if (sIdx >= 0 && eIdx > sIdx + startTag.Length)
            {
                body = raw.Substring(sIdx + startTag.Length, eIdx - (sIdx + startTag.Length));
            }

            string[] lines = body.Trim().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!string.IsNullOrEmpty(line) && !line.StartsWith("<", StringComparison.Ordinal) && !line.StartsWith("![", StringComparison.Ordinal))
                {
                    return line;
                }
            }
            return null;
        }

        private static string ExtractModelSelection(string raw)
        {
            const string marker = "Model Selection` from ";
            int mIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            if (mIdx < 0) return null;
            string sub = raw.Substring(mIdx + marker.Length);
            int toIdx = sub.IndexOf(" to ", StringComparison.Ordinal);
            if (toIdx < 0) return null;
            string afterTo = sub.Substring(toIdx + 4);
            int dot = afterTo.IndexOf('.');
            if (dot > 0)
            {
                string name = afterTo.Substring(0, dot).Trim();
                return string.IsNullOrEmpty(name) ? null : name;
            }
            return null;
        }

        private static string ExtractToolDetail(Dictionary<string, object> args)
        {
            if (args == null) return null;
            string summary = CleanArg(MiniJson.GetString(args, "toolSummary"));
            if (!string.IsNullOrEmpty(summary)) return summary;

            string path = CleanArg(MiniJson.GetString(args, "Path"));
            if (!string.IsNullOrEmpty(path))
            {
                return Path.GetFileName(path.Replace('\\', '/'));
            }

            string query = CleanArg(MiniJson.GetString(args, "query"));
            if (!string.IsNullOrEmpty(query)) return query;

            string role = CleanArg(MiniJson.GetString(args, "Role"));
            if (!string.IsNullOrEmpty(role)) return role;

            return null;
        }

        private static string CleanArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Trim('"', '\'', ' ', '\r', '\n');
        }

        // MARK: - Native Read-Only `winsqlite3.dll` Reader for `conversation_summaries.db`

        private Dictionary<string, SummaryDbEntry> LoadConversationSummariesIfNeeded()
        {
            if (!File.Exists(_summariesDbPath)) return _summariesCache;

            string walPath = _summariesDbPath + "-wal";
            FileInfo dbFi = new FileInfo(_summariesDbPath);
            FileInfo walFi = new FileInfo(walPath);
            long dbTicks = dbFi.Exists ? dbFi.LastWriteTimeUtc.Ticks : 0;
            long walTicks = walFi.Exists ? walFi.LastWriteTimeUtc.Ticks : 0;
            long walLen = walFi.Exists ? walFi.Length : 0;
            string key = string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}", dbTicks, walTicks, walLen);

            if (string.Equals(key, _summariesCacheKey, StringComparison.Ordinal) && _summariesCache.Count > 0)
            {
                return _summariesCache;
            }

            IntPtr db = IntPtr.Zero;
            try
            {
                if (WinSqlite3.OpenReadOnly(_summariesDbPath, out db) != WinSqlite3.SQLITE_OK || db == IntPtr.Zero)
                {
                    return _summariesCache;
                }
                WinSqlite3.sqlite3_busy_timeout(db, 80);

                const string sql = "SELECT conversation_id, title, workspace_uris, status, nesting_depth, killed FROM conversation_summaries;";
                IntPtr stmt = IntPtr.Zero;
                if (WinSqlite3.Prepare(db, sql, out stmt) != WinSqlite3.SQLITE_OK || stmt == IntPtr.Zero)
                {
                    return _summariesCache;
                }

                try
                {
                    Dictionary<string, SummaryDbEntry> parsed = new Dictionary<string, SummaryDbEntry>(StringComparer.OrdinalIgnoreCase);
                    while (WinSqlite3.sqlite3_step(stmt) == WinSqlite3.SQLITE_ROW)
                    {
                        string cid = WinSqlite3.ColumnString(stmt, 0);
                        if (string.IsNullOrEmpty(cid)) continue;

                        string rawTitle = WinSqlite3.ColumnString(stmt, 1);
                        if (rawTitle != null) rawTitle = rawTitle.Trim();
                        string wsJson = WinSqlite3.ColumnString(stmt, 2) ?? string.Empty;
                        string status = WinSqlite3.ColumnString(stmt, 3) ?? string.Empty;
                        int nesting = WinSqlite3.sqlite3_column_int(stmt, 4);
                        bool killed = WinSqlite3.sqlite3_column_int(stmt, 5) != 0;

                        SummaryDbEntry entry = new SummaryDbEntry();
                        entry.Title = !string.IsNullOrEmpty(rawTitle) ? rawTitle : null;
                        entry.ProjectName = ExtractProjectNameFromWorkspaceUrisJson(wsJson);
                        entry.IsRunningInDb = string.Equals(status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.Ordinal);
                        entry.IsIdleInDb = string.Equals(status, "CASCADE_RUN_STATUS_IDLE", StringComparison.Ordinal);
                        entry.NestingDepth = nesting;
                        entry.Killed = killed;
                        parsed[cid] = entry;
                    }

                    _summariesCacheKey = key;
                    _summariesCache = parsed;
                    return parsed;
                }
                finally
                {
                    WinSqlite3.sqlite3_finalize(stmt);
                }
            }
            catch
            {
                return _summariesCache;
            }
            finally
            {
                if (db != IntPtr.Zero)
                {
                    WinSqlite3.sqlite3_close(db);
                }
            }
        }

        private static string ExtractProjectNameFromWorkspaceUrisJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "default";
            List<object> arr = MiniJson.Parse(raw.Trim()) as List<object>;
            if (arr != null && arr.Count > 0)
            {
                string firstUri = arr[0] as string;
                if (!string.IsNullOrEmpty(firstUri))
                {
                    string decoded = Uri.UnescapeDataString(firstUri);
                    string normalized = decoded.Replace('\\', '/').TrimEnd('/');
                    int slash = normalized.LastIndexOf('/');
                    string leaf = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
                    if (!string.IsNullOrEmpty(leaf) && !(leaf.Length == 2 && leaf[1] == ':'))
                    {
                        return leaf;
                    }
                }
            }
            return "default";
        }

        // MARK: - Windows Lifecycle Hooks Integration

        private HookLiveEvent LoadHookState()
        {
            try
            {
                if (!File.Exists(_hookStatePath)) return null;
                string raw;
                using (FileStream fs = new FileStream(_hookStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    raw = sr.ReadToEnd();
                }
                Dictionary<string, object> d = MiniJson.ParseObject(raw);
                if (d == null) return null;

                return new HookLiveEvent
                {
                    EventName = MiniJson.GetString(d, "event") ?? string.Empty,
                    TimestampUnix = MiniJson.GetNullableDouble(d, "timestamp") ?? 0.0,
                    ConversationId = MiniJson.GetString(d, "conversationId"),
                    ModelName = MiniJson.GetString(d, "modelName"),
                    ToolName = MiniJson.GetString(d, "toolName"),
                    ToolDetail = MiniJson.GetString(d, "toolDetail"),
                    TerminationReason = MiniJson.GetString(d, "terminationReason"),
                    FullyIdle = MiniJson.GetNullableBool(d, "fullyIdle"),
                    Error = MiniJson.GetString(d, "error")
                };
            }
            catch
            {
                return null;
            }
        }

        public void CheckHookPluginInstalled()
        {
            string manifest = Path.Combine(_hookPluginDir, "hooks.json");
            IsHookPluginInstalled = File.Exists(manifest);
        }

        public void ToggleHookPluginInstallation()
        {
            if (IsHookPluginInstalled)
            {
                try
                {
                    if (Directory.Exists(_hookPluginDir))
                    {
                        Directory.Delete(_hookPluginDir, true);
                    }
                }
                catch
                {
                }
                CheckHookPluginInstalled();
                ShowToast("已移除 Lifecycle Hooks 插件");
                NotifyStateChanged();
                return;
            }

            try
            {
                Directory.CreateDirectory(_hookPluginDir);

                string pluginJson = "{\r\n" +
                    "  \"name\": \"agy-assistant-hooks\",\r\n" +
                    "  \"description\": \"Pushes real-time Antigravity lifecycle status events to the Windows Floating Assistant.\"\r\n" +
                    "}\r\n";
                File.WriteAllText(Path.Combine(_hookPluginDir, "plugin.json"), pluginJson, Encoding.UTF8);

                string pyCmd = ResolvePythonCommandForHook();
                string hooksJson = "{\r\n" +
                    "  \"agy-floating-assistant\": {\r\n" +
                    "    \"SessionStart\": [\r\n" +
                    "      { \"type\": \"command\", \"command\": \"" + EscapeJson(pyCmd + " ./record_hook.py SessionStart") + "\", \"timeout\": 3 }\r\n" +
                    "    ],\r\n" +
                    "    \"PreInvocation\": [\r\n" +
                    "      { \"type\": \"command\", \"command\": \"" + EscapeJson(pyCmd + " ./record_hook.py PreInvocation") + "\", \"timeout\": 3 }\r\n" +
                    "    ],\r\n" +
                    "    \"PreToolUse\": [\r\n" +
                    "      {\r\n" +
                    "        \"matcher\": \"*\",\r\n" +
                    "        \"hooks\": [\r\n" +
                    "          { \"type\": \"command\", \"command\": \"" + EscapeJson(pyCmd + " ./record_hook.py PreToolUse") + "\", \"timeout\": 3 }\r\n" +
                    "        ]\r\n" +
                    "      }\r\n" +
                    "    ],\r\n" +
                    "    \"PostToolUse\": [\r\n" +
                    "      {\r\n" +
                    "        \"matcher\": \"*\",\r\n" +
                    "        \"hooks\": [\r\n" +
                    "          { \"type\": \"command\", \"command\": \"" + EscapeJson(pyCmd + " ./record_hook.py PostToolUse") + "\", \"timeout\": 3 }\r\n" +
                    "        ]\r\n" +
                    "      }\r\n" +
                    "    ],\r\n" +
                    "    \"Stop\": [\r\n" +
                    "      { \"type\": \"command\", \"command\": \"" + EscapeJson(pyCmd + " ./record_hook.py Stop") + "\", \"timeout\": 3 }\r\n" +
                    "    ]\r\n" +
                    "  }\r\n" +
                    "}\r\n";
                File.WriteAllText(Path.Combine(_hookPluginDir, "hooks.json"), hooksJson, Encoding.UTF8);

                string pyScript =
                    "#!/usr/bin/env python3\r\n" +
                    "import json, os, sys, time\r\n\r\n" +
                    "def main():\r\n" +
                    "    event = sys.argv[1] if len(sys.argv) > 1 else \"Unknown\"\r\n" +
                    "    try:\r\n" +
                    "        payload = json.loads(sys.stdin.read() or \"{}\")\r\n" +
                    "    except Exception:\r\n" +
                    "        payload = {}\r\n\r\n" +
                    "    tool_call = payload.get(\"toolCall\") or {}\r\n" +
                    "    tool_name = tool_call.get(\"name\")\r\n" +
                    "    args = tool_call.get(\"args\") or {}\r\n" +
                    "    detail = args.get(\"toolSummary\") or args.get(\"Path\") or args.get(\"query\")\r\n" +
                    "    if isinstance(detail, str) and (\"/\" in detail or \"\\\\\" in detail) and not args.get(\"toolSummary\"):\r\n" +
                    "        detail = os.path.basename(detail.replace(\"\\\\\", \"/\"))\r\n\r\n" +
                    "    state = {\r\n" +
                    "        \"event\": event,\r\n" +
                    "        \"timestamp\": time.time(),\r\n" +
                    "        \"conversationId\": payload.get(\"conversationId\"),\r\n" +
                    "        \"modelName\": payload.get(\"modelName\"),\r\n" +
                    "        \"toolName\": tool_name,\r\n" +
                    "        \"toolDetail\": detail,\r\n" +
                    "        \"terminationReason\": payload.get(\"terminationReason\"),\r\n" +
                    "        \"fullyIdle\": payload.get(\"fullyIdle\"),\r\n" +
                    "        \"error\": payload.get(\"error\"),\r\n" +
                    "    }\r\n\r\n" +
                    "    if sys.platform == \"win32\":\r\n" +
                    "        base_dir = os.environ.get(\"LOCALAPPDATA\") or os.path.expanduser(\"~\\\\AppData\\\\Local\")\r\n" +
                    "        out_dir = os.path.join(base_dir, \"AgyAssistant\")\r\n" +
                    "    else:\r\n" +
                    "        out_dir = os.path.expanduser(\"~/Library/Application Support/AgyAssistant\")\r\n\r\n" +
                    "    os.makedirs(out_dir, exist_ok=True)\r\n" +
                    "    tmp_path = os.path.join(out_dir, f\"hook_state.{os.getpid()}.tmp\")\r\n" +
                    "    final_path = os.path.join(out_dir, \"hook_state.json\")\r\n" +
                    "    with open(tmp_path, \"w\", encoding=\"utf-8\") as fp:\r\n" +
                    "        json.dump(state, fp, ensure_ascii=False)\r\n" +
                    "    for i in range(4):\r\n" +
                    "        try:\r\n" +
                    "            os.replace(tmp_path, final_path)\r\n" +
                    "            break\r\n" +
                    "        except OSError:\r\n" +
                    "            time.sleep(0.015)\r\n\r\n" +
                    "    if event == \"PreToolUse\":\r\n" +
                    "        print(json.dumps({\"decision\": \"allow\"}))\r\n" +
                    "    elif event == \"Stop\":\r\n" +
                    "        print(json.dumps({\"decision\": \"stop\"}))\r\n" +
                    "    else:\r\n" +
                    "        print(\"{}\")\r\n\r\n" +
                    "if __name__ == \"__main__\":\r\n" +
                    "    main()\r\n";

                File.WriteAllText(Path.Combine(_hookPluginDir, "record_hook.py"), pyScript, new UTF8Encoding(false));
                CheckHookPluginInstalled();
                ShowToast("已启用 Lifecycle Hooks 毫秒级同步");
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                ShowToast("安装失败: " + ex.Message);
            }
        }

        private static string ResolvePythonCommandForHook()
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] dirs = pathEnv.Split(Path.PathSeparator);
            for (int i = 0; i < dirs.Length; i++)
            {
                string d = dirs[i].Trim();
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    string py = Path.Combine(d, "python.exe");
                    if (File.Exists(py) && d.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return py.Contains(" ") ? ("\"" + py + "\"") : py;
                    }
                }
                catch
                {
                }
            }
            return "python";
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // MARK: - Conversation Jump & Dual-Side Delete

        public void JumpToPrimaryConversation()
        {
            ConversationSnapshot primary = PrimaryConversation;
            if (primary != null)
            {
                JumpToConversation(primary);
            }
            else
            {
                ActivateAntigravityApp();
            }
        }

        public void JumpToConversationId(string convId)
        {
            AcknowledgeUnreadCompleted(convId);
            for (int i = 0; i < Conversations.Count; i++)
            {
                if (string.Equals(Conversations[i].Id, convId, StringComparison.OrdinalIgnoreCase))
                {
                    JumpToConversation(Conversations[i]);
                    return;
                }
            }
            RefreshNow();
            for (int i = 0; i < Conversations.Count; i++)
            {
                if (string.Equals(Conversations[i].Id, convId, StringComparison.OrdinalIgnoreCase))
                {
                    JumpToConversation(Conversations[i]);
                    return;
                }
            }
            ActivateAntigravityApp();
        }

        public async void JumpToConversation(ConversationSnapshot conv)
        {
            if (conv == null) return;
            AcknowledgeUnreadCompleted(conv.Id);
            if (_isJumping) return;
            _isJumping = true;
            try
            {
                NavigationOutcome outcome = await ConversationNavigator.JumpAsync(conv);
                switch (outcome.Kind)
                {
                    case NavigationOutcomeKind.AlreadyActive:
                        ShowToast("✨ 当前已在该目标会话");
                        break;
                    case NavigationOutcomeKind.JumpedViaDirectLink:
                    case NavigationOutcomeKind.JumpedViaPickerSearch:
                        ShowToast("已打开会话：" + conv.Title);
                        break;
                    case NavigationOutcomeKind.ActivatedAppOnly:
                        ShowToast(outcome.Reason ?? "已切回 Antigravity");
                        break;
                    case NavigationOutcomeKind.AppNotFound:
                        ShowToast("未检测到已安装的 Antigravity.exe");
                        break;
                }
            }
            finally
            {
                _isJumping = false;
            }
        }

        public async void ActivateAntigravityApp()
        {
            string exePath = ConversationNavigator.ResolveAntigravityExePath();
            if (string.IsNullOrEmpty(exePath))
            {
                ShowToast("未检测到已安装的 Antigravity.exe");
                return;
            }
            await ConversationNavigator.ActivateOrLaunchAntigravityAsync(exePath);
        }

        public async void DeleteConversationBothSides(ConversationSnapshot conv)
        {
            if (conv == null) return;
            string convId = conv.Id;
            if (_deletingConversationIds.Contains(convId)) return;
            _deletingConversationIds.Add(convId);

            DeletedConversationIds.Add(convId);
            PinnedConversationIds.Remove(convId);
            UnreadCompletedConversationIds.Remove(convId);
            PersistSets();

            _cache.Remove(convId);
            _previousPhases.Remove(convId);
            _previousBackgroundBusy.Remove(convId);
            _previousUserTurnCounts.Remove(convId);
            Conversations.RemoveAll(delegate(ConversationSnapshot c) { return string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase); });
            NotifyStateChanged();

            try
            {
                bool uiDeleted = await ConversationNavigator.DeleteConversationInAntigravityAsync(convId);
                await Task.Delay(350);

                string brainDir = _brainDir;
                string convDbDir = _conversationsDbDir;
                string rootDir = _antigravityRootDir;
                await Task.Run(delegate
                {
                    RemoveLocalConversationArtifacts(convId, brainDir, convDbDir, rootDir);
                });

                ShowToast(uiDeleted ? "🗑 已同步删除两边会话" : "🗑 已删除会话及本地记录");
            }
            finally
            {
                _deletingConversationIds.Remove(convId);
            }
        }

        private static void RemoveLocalConversationArtifacts(string convId, string brainDir, string convDbDir, string rootDir)
        {
            if (string.IsNullOrEmpty(convId) || convId.Length < 32 || convId.Contains("/") || convId.Contains("\\") || convId.Contains(".."))
                return;

            string brainFolder = Path.Combine(brainDir, convId);
            if (Directory.Exists(brainFolder))
            {
                if (!Win32Native.MoveDirectoryToRecycleBin(brainFolder))
                {
                    try { Directory.Delete(brainFolder, true); } catch { }
                }
            }

            string[] suffixes = new string[] { ".db", ".db-shm", ".db-wal", ".pb" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                string f = Path.Combine(convDbDir, convId + suffixes[i]);
                if (File.Exists(f))
                {
                    try { File.Delete(f); } catch { }
                }
            }

            string annotationsDir = Path.Combine(rootDir, "annotations");
            if (Directory.Exists(annotationsDir))
            {
                try
                {
                    string[] items = Directory.GetFiles(annotationsDir, convId + "*");
                    for (int i = 0; i < items.Length; i++)
                    {
                        try { File.Delete(items[i]); } catch { }
                    }
                }
                catch
                {
                }
            }
        }

        // MARK: - Synthetic Open-Source Demo State (`--export-demo-images`)

        public void LoadSyntheticDemoState()
        {
            DateTime now = DateTime.UtcNow;
            NowTickUtc = now;
            LastRefreshDateUtc = now;
            IsAntigravityInstalled = true;
            IsHookPluginInstalled = true;

            Conversations = new List<ConversationSnapshot>
            {
                new ConversationSnapshot
                {
                    Id = "demo-conv-01",
                    Title = "Build Native Windows WPF Floating Mascot",
                    ProjectName = "antigravity-assistant",
                    SearchQuery = "Build Native Windows WPF",
                    ModelName = "Gemini",
                    Phase = AgentPhase.RunningTool("run_command", "Windows build verification"),
                    LastUpdatedAtUtc = now.AddSeconds(-12),
                    ActiveTurnStartedAtUtc = now.AddSeconds(-48),
                    StepCount = 24,
                    UserTurnCount = 4,
                    InvocationCount = 16,
                    ContextUsedTokens = 42000,
                    LatestInputTokens = 12000,
                    LatestCacheReadTokens = 30000,
                    TotalOutputTokens = 4800,
                    BackgroundBusy = false
                },
                new ConversationSnapshot
                {
                    Id = "demo-conv-02",
                    Title = "Implement PerMonitorV2 Click-Through Hit Mask",
                    ProjectName = "antigravity-assistant",
                    SearchQuery = "Implement PerMonitorV2 Click-Through",
                    ModelName = "Gemini",
                    Phase = AgentPhase.Completed,
                    LastUpdatedAtUtc = now.AddSeconds(-320),
                    ActiveTurnStartedAtUtc = now.AddSeconds(-390),
                    StepCount = 18,
                    UserTurnCount = 3,
                    InvocationCount = 12,
                    ContextUsedTokens = 31000,
                    LatestInputTokens = 8000,
                    LatestCacheReadTokens = 23000,
                    TotalOutputTokens = 3200,
                    BackgroundBusy = false
                },
                new ConversationSnapshot
                {
                    Id = "demo-conv-03",
                    Title = "Integrate Windows UIAutomation & winsqlite3",
                    ProjectName = "win32-bridge",
                    SearchQuery = "Integrate Windows UIAutomation",
                    ModelName = "Gemini",
                    Phase = AgentPhase.Completed,
                    LastUpdatedAtUtc = now.AddSeconds(-1800),
                    StepCount = 30,
                    UserTurnCount = 5,
                    InvocationCount = 20,
                    ContextUsedTokens = 54000,
                    LatestInputTokens = 14000,
                    LatestCacheReadTokens = 40000,
                    TotalOutputTokens = 6100,
                    BackgroundBusy = false
                },
                new ConversationSnapshot
                {
                    Id = "demo-conv-04",
                    Title = "Add GitHub Actions Windows Release Workflow",
                    ProjectName = "ci-automation",
                    SearchQuery = "Add GitHub Actions Windows",
                    ModelName = "Gemini",
                    Phase = AgentPhase.Completed,
                    LastUpdatedAtUtc = now.AddSeconds(-5400),
                    StepCount = 14,
                    UserTurnCount = 2,
                    InvocationCount = 9,
                    ContextUsedTokens = 19000,
                    LatestInputTokens = 6000,
                    LatestCacheReadTokens = 13000,
                    TotalOutputTokens = 2100,
                    BackgroundBusy = false
                }
            };

            PinnedConversationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "demo-conv-03" };
            UnreadCompletedConversationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "demo-conv-02" };
            DeletedConversationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CloudAccounts = new List<CloudAccountQuota>
            {
                new CloudAccountQuota
                {
                    UserEmail = "developer@example.com",
                    PlanName = "Google AI Pro",
                    SourceName = "Google 官方直连",
                    IsDirectGoogleAccount = true,
                    GeminiPool = new CloudPoolQuota
                    {
                        PoolName = "Gemini",
                        Remaining5h = 94.2,
                        ResetsAt5hUtc = now.AddMinutes(222),
                        Remaining7d = 97.6,
                        ResetsAt7dUtc = now.AddHours(134)
                    },
                    ClaudeGptPool = new CloudPoolQuota
                    {
                        PoolName = "Claude & GPT",
                        Remaining5h = 98.5,
                        ResetsAt5hUtc = now.AddMinutes(255),
                        Remaining7d = 99.1,
                        ResetsAt7dUtc = now.AddHours(152)
                    },
                    ReadAtUtc = now,
                    IsLatestActive = true
                }
            };

            Quota = new QuotaMetrics
            {
                FiveHourRemainingPercent = 94.2,
                FiveHourResetDateUtc = now.AddMinutes(222),
                FiveHourStepsUsed = 28,
                FiveHourTokensUsed = 92000,
                IsFiveHourSynced = true,
                WeeklyRemainingPercent = 97.6,
                WeeklyResetDateUtc = now.AddHours(134),
                WeeklyStepsUsed = 64,
                IsWeeklySynced = true,
                TodayInvocations = 45,
                TodayUserTurns = 12,
                TodayTokens = 146000,
                TodayConversations = 4
            };
        }

        private void NotifyStateChanged()
        {
            Action h = StateChanged;
            if (h != null) h();
        }
    }
}
