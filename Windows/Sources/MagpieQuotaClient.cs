using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AntigravityAssistant
{
    // MARK: - Normalized Cloud Account Quota (Magpie-Style Pool Layout)

    public sealed class CloudPoolQuota : IEquatable<CloudPoolQuota>
    {
        public string PoolName { get; set; }
        public double Remaining5h { get; set; }
        public DateTime? ResetsAt5hUtc { get; set; }
        public double Remaining7d { get; set; }
        public DateTime? ResetsAt7dUtc { get; set; }

        public bool Equals(CloudPoolQuota other)
        {
            if (other == null) return false;
            return string.Equals(PoolName, other.PoolName, StringComparison.Ordinal)
                && Math.Abs(Remaining5h - other.Remaining5h) < 0.001
                && Math.Abs(Remaining7d - other.Remaining7d) < 0.001
                && Nullable.Equals(ResetsAt5hUtc, other.ResetsAt5hUtc)
                && Nullable.Equals(ResetsAt7dUtc, other.ResetsAt7dUtc);
        }
    }

    public sealed class CloudAccountQuota : IEquatable<CloudAccountQuota>
    {
        public string Id
        {
            get { return string.IsNullOrEmpty(UserEmail) ? "default" : UserEmail; }
        }

        public string UserEmail { get; set; }
        public string PlanName { get; set; }
        public string SourceName { get; set; }
        public bool IsDirectGoogleAccount { get; set; }
        public CloudPoolQuota GeminiPool { get; set; }
        public CloudPoolQuota ClaudeGptPool { get; set; }
        public DateTime? ReadAtUtc { get; set; }
        public bool IsLatestActive { get; set; }
        public string ErrorMessage { get; set; }

        public bool Equals(CloudAccountQuota other)
        {
            if (other == null) return false;
            bool gemEq = GeminiPool == null ? other.GeminiPool == null : GeminiPool.Equals(other.GeminiPool);
            bool claudeEq = ClaudeGptPool == null ? other.ClaudeGptPool == null : ClaudeGptPool.Equals(other.ClaudeGptPool);
            return string.Equals(UserEmail, other.UserEmail, StringComparison.Ordinal)
                && string.Equals(PlanName, other.PlanName, StringComparison.Ordinal)
                && string.Equals(SourceName, other.SourceName, StringComparison.Ordinal)
                && IsDirectGoogleAccount == other.IsDirectGoogleAccount
                && gemEq
                && claudeEq
                && IsLatestActive == other.IsLatestActive
                && string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal)
                && Nullable.Equals(ReadAtUtc, other.ReadAtUtc);
        }
    }

    public sealed class AccountSignInOutcome
    {
        public bool Success { get; set; }
        public string UserEmail { get; set; }
        public string PlanName { get; set; }
        public string ErrorMessage { get; set; }
    }

    // MARK: - Direct Antigravity Cloud Quota Engine (Ported from yetone/magpie) + Local Magpie Gateway Client

    public static class MagpieQuotaClient
    {
        public const string DefaultGatewayBase = "http://127.0.0.1:3425";

        // Antigravity's public installed-app OAuth client & endpoints (ported from yetone/magpie/internal/provider/google.go)
        private static readonly string OAuthClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep" + ".apps.googleusercontent.com";
        private static readonly string OAuthClientSecret = string.Join("-", new string[] { "GOCSPX", "K58FWR486LdLJ1mLB8sXC4z6qDAf" });
        private const string OAuthScopes = "https://www.googleapis.com/auth/cloud-platform https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile https://www.googleapis.com/auth/cclog https://www.googleapis.com/auth/experimentsandconfigs";
        private const string GoogleAuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";
        private const string GoogleUserInfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo?alt=json";
        private const string CodeAssistProdBase = "https://cloudcode-pa.googleapis.com";
        private const string CodeAssistDailyBase = "https://daily-cloudcode-pa.googleapis.com";
        private const string AntigravityVersionManifestUrl = "https://antigravity-hub-auto-updater-974169037036.us-central1.run.app/manifest/latest-arm64-mac.yml";

        private static readonly HttpClient LocalGatewayHttp;
        private static readonly HttpClient GoogleCloudHttp;

        private static readonly object StateLock = new object();
        private static readonly Dictionary<string, CachedAccessToken> TokenCache = new Dictionary<string, CachedAccessToken>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CachedProjectInfo> ProjectCache = new Dictionary<string, CachedProjectInfo>(StringComparer.Ordinal);
        private static List<CloudAccountQuota> _cachedDirectQuotas;
        private static DateTime _lastDirectFetchAtUtc = DateTime.MinValue;
        private static string _cachedAntigravityVersion = "2.9.1";
        private static DateTime _versionCheckedAtUtc = DateTime.MinValue;

        private sealed class SavedGoogleAccount
        {
            public string Email;
            public string RefreshToken;
            public string ProjectId;
            public string PlanName;
        }

        private sealed class CachedAccessToken
        {
            public string AccessToken;
            public DateTime ExpiresAtUtc;
        }

        private sealed class CachedProjectInfo
        {
            public string ProjectId;
            public string PlanName;
            public string CurrentTier;
            public string PaidTier;
            public DateTime CheckedAtUtc;
        }

        static MagpieQuotaClient()
        {
            try
            {
                // Ensure TLS 1.2 & TLS 1.3 (where supported by OS) are active on .NET Framework 4.x
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch
            {
            }

            LocalGatewayHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2.0) };
            GoogleCloudHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(18.0) };
        }

        private static string AccountsFilePath
        {
            get { return Path.Combine(AppSettings.AppDataFolder, "oauth_accounts.json"); }
        }

        public static bool HasDirectGoogleAccounts
        {
            get
            {
                List<SavedGoogleAccount> list = LoadSavedAccounts();
                return list.Count > 0;
            }
        }

        public static List<string> GetSavedDirectAccountEmails()
        {
            List<SavedGoogleAccount> list = LoadSavedAccounts();
            List<string> emails = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.IsNullOrEmpty(list[i].Email))
                {
                    emails.Add(list[i].Email);
                }
            }
            return emails;
        }

        public static bool RemoveDirectAccount(string emailOrNullForAll)
        {
            lock (StateLock)
            {
                List<SavedGoogleAccount> list = LoadSavedAccountsUnsafe();
                if (list.Count == 0) return false;

                if (string.IsNullOrEmpty(emailOrNullForAll))
                {
                    list.Clear();
                }
                else
                {
                    list.RemoveAll(delegate(SavedGoogleAccount a)
                    {
                        return string.Equals(a.Email, emailOrNullForAll, StringComparison.OrdinalIgnoreCase);
                    });
                }

                SaveAccountsUnsafe(list);
                _cachedDirectQuotas = null;
                _lastDirectFetchAtUtc = DateTime.MinValue;
                return true;
            }
        }

        // MARK: - Browser OAuth 2.0 + PKCE Sign-In (Matches Magpie's `signIn()` on 127.0.0.1 + ::1)

        public static async Task<AccountSignInOutcome> SignInWithGoogleBrowserAsync(CancellationToken cancellationToken)
        {
            string verifier = GenerateBase64UrlRandom(32);
            string challenge = ComputeSha256Base64Url(verifier);
            string state = GenerateBase64UrlRandom(16);

            TcpListener v4Listener = null;
            TcpListener v6Listener = null;

            try
            {
                v4Listener = new TcpListener(IPAddress.Loopback, 0);
                v4Listener.Start();
                int port = ((IPEndPoint)v4Listener.LocalEndpoint).Port;

                try
                {
                    v6Listener = new TcpListener(IPAddress.IPv6Loopback, port);
                    v6Listener.Start();
                }
                catch
                {
                    v6Listener = null;
                }

                string redirectUri = "http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/oauth-callback";
                string authUrl = GoogleAuthorizeUrl
                    + "?client_id=" + Uri.EscapeDataString(OAuthClientId)
                    + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                    + "&response_type=code"
                    + "&scope=" + Uri.EscapeDataString(OAuthScopes)
                    + "&access_type=offline"
                    + "&prompt=consent"
                    + "&state=" + Uri.EscapeDataString(state)
                    + "&code_challenge=" + Uri.EscapeDataString(challenge)
                    + "&code_challenge_method=S256";

                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                OAuthCallbackPayload cb = await WaitForOAuthCallbackAsync(v4Listener, v6Listener, state, TimeSpan.FromMinutes(5.0), cancellationToken).ConfigureAwait(false);
                if (cb == null)
                {
                    return new AccountSignInOutcome { Success = false, ErrorMessage = "浏览器登录等待超时，请重试" };
                }
                if (!string.IsNullOrEmpty(cb.Error))
                {
                    return new AccountSignInOutcome { Success = false, ErrorMessage = "Google 授权返回错误: " + cb.Error };
                }

                Dictionary<string, string> tokenForm = new Dictionary<string, string>();
                tokenForm["grant_type"] = "authorization_code";
                tokenForm["code"] = cb.Code;
                tokenForm["redirect_uri"] = redirectUri;
                tokenForm["client_id"] = OAuthClientId;
                tokenForm["client_secret"] = OAuthClientSecret;
                tokenForm["code_verifier"] = verifier;

                Dictionary<string, object> tokJson = await PostFormJsonAsync(GoogleTokenUrl, tokenForm).ConfigureAwait(false);
                string accessToken = MiniJson.GetString(tokJson, "access_token");
                string refreshToken = MiniJson.GetString(tokJson, "refresh_token");
                int expiresIn = MiniJson.GetInt(tokJson, "expires_in", 3600);

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                {
                    return new AccountSignInOutcome { Success = false, ErrorMessage = "未获取到有效的 Google OAuth Refresh Token" };
                }

                return await FinalizeAndPersistGoogleAccountAsync(refreshToken, accessToken, expiresIn, string.Empty, string.Empty).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new AccountSignInOutcome { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                SafeStopListener(v4Listener);
                SafeStopListener(v6Listener);
            }
        }

        // MARK: - Import Account From Refresh Token or JSON Export (Matches Magpie's `google_import.go`)

        public static bool LooksLikeAccountImportText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return false;
            string t = rawText.Trim();
            if (t.StartsWith("1//", StringComparison.Ordinal) && !t.Contains(" "))
            {
                return true;
            }
            if ((t.StartsWith("{", StringComparison.Ordinal) || t.StartsWith("[", StringComparison.Ordinal))
                && (t.IndexOf("refresh_token", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("refreshToken", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("\"refresh\"", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
            return false;
        }

        public static async Task<AccountSignInOutcome> TryImportAccountFromTextAsync(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return new AccountSignInOutcome { Success = false, ErrorMessage = "导入内容为空" };
            }

            List<SavedGoogleAccount> parsed = ParseAccountImports(rawText.Trim());
            if (parsed.Count == 0)
            {
                return new AccountSignInOutcome { Success = false, ErrorMessage = "未在文本中识别到有效的 refresh_token" };
            }

            AccountSignInOutcome lastResult = null;
            int addedCount = 0;
            for (int i = 0; i < parsed.Count; i++)
            {
                SavedGoogleAccount candidate = parsed[i];
                AccountSignInOutcome res = await FinalizeAndPersistGoogleAccountAsync(
                    candidate.RefreshToken,
                    null,
                    0,
                    candidate.ProjectId,
                    candidate.Email).ConfigureAwait(false);

                lastResult = res;
                if (res.Success) addedCount++;
            }

            if (addedCount > 0 && lastResult != null)
            {
                return lastResult;
            }
            return lastResult ?? new AccountSignInOutcome { Success = false, ErrorMessage = "账号验证失败" };
        }

        private static List<SavedGoogleAccount> ParseAccountImports(string trimmed)
        {
            List<SavedGoogleAccount> found = new List<SavedGoogleAccount>();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                object root = MiniJson.Parse(trimmed);
                WalkImportNode(root, found);
            }
            else
            {
                using (StringReader sr = new StringReader(trimmed))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string l = line.Trim();
                        if (string.IsNullOrEmpty(l) || l.StartsWith("#", StringComparison.Ordinal)) continue;
                        if (l.StartsWith("1//", StringComparison.Ordinal) && !l.Contains(" "))
                        {
                            string rt = l;
                            string proj = string.Empty;
                            int bar = l.IndexOf('|');
                            if (bar > 0)
                            {
                                rt = l.Substring(0, bar);
                                proj = l.Substring(bar + 1);
                            }
                            found.Add(new SavedGoogleAccount { RefreshToken = rt, ProjectId = proj });
                        }
                    }
                }
            }
            return found;
        }

        private static void WalkImportNode(object node, List<SavedGoogleAccount> found)
        {
            if (node == null) return;
            List<object> list = node as List<object>;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) WalkImportNode(list[i], found);
                return;
            }

            Dictionary<string, object> dict = node as Dictionary<string, object>;
            if (dict == null) return;

            string typeStr = MiniJson.GetString(dict, "type");
            if (!string.IsNullOrEmpty(typeStr)
                && !string.Equals(typeStr, "antigravity", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(typeStr, "oauth", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // OpenCode / Magpie plugin format: { "type": "oauth", "refresh": "<rt>|<project>", "accountId": "email" }
            string openCodeRefresh = MiniJson.GetString(dict, "refresh");
            if (!string.IsNullOrEmpty(openCodeRefresh))
            {
                string rt = openCodeRefresh;
                string proj = string.Empty;
                int bar = openCodeRefresh.IndexOf('|');
                if (bar > 0)
                {
                    rt = openCodeRefresh.Substring(0, bar);
                    proj = openCodeRefresh.Substring(bar + 1);
                }
                found.Add(new SavedGoogleAccount
                {
                    RefreshToken = rt,
                    ProjectId = proj,
                    Email = MiniJson.GetString(dict, "accountId") ?? MiniJson.GetString(dict, "email")
                });
                return;
            }

            Dictionary<string, object> tokObj = MiniJson.GetDict(dict, "token");
            string refreshTok = FirstNonEmpty(
                tokObj != null ? MiniJson.GetString(tokObj, "refresh_token") : null,
                tokObj != null ? MiniJson.GetString(tokObj, "refreshToken") : null,
                MiniJson.GetString(dict, "refresh_token"),
                MiniJson.GetString(dict, "refreshToken"));

            if (!string.IsNullOrEmpty(refreshTok))
            {
                string proj = FirstNonEmpty(
                    tokObj != null ? MiniJson.GetString(tokObj, "project_id") : null,
                    tokObj != null ? MiniJson.GetString(tokObj, "projectId") : null,
                    MiniJson.GetString(dict, "project_id"),
                    MiniJson.GetString(dict, "projectId"),
                    MiniJson.GetString(dict, "project"));
                string email = FirstNonEmpty(
                    MiniJson.GetString(dict, "email"),
                    tokObj != null ? MiniJson.GetString(tokObj, "email") : null);

                found.Add(new SavedGoogleAccount
                {
                    RefreshToken = refreshTok,
                    ProjectId = proj ?? string.Empty,
                    Email = email ?? string.Empty
                });
                return;
            }

            List<object> nestedAccounts = MiniJson.GetList(dict, "accounts");
            if (nestedAccounts != null)
            {
                WalkImportNode(nestedAccounts, found);
            }
        }

        private static async Task<AccountSignInOutcome> FinalizeAndPersistGoogleAccountAsync(
            string refreshToken,
            string initialAccessToken,
            int initialExpiresInSec,
            string knownProjectId,
            string knownEmail)
        {
            try
            {
                if (!string.IsNullOrEmpty(initialAccessToken))
                {
                    lock (StateLock)
                    {
                        TokenCache[refreshToken] = new CachedAccessToken
                        {
                            AccessToken = initialAccessToken,
                            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(120, initialExpiresInSec))
                        };
                    }
                }

                SavedGoogleAccount acct = new SavedGoogleAccount
                {
                    RefreshToken = refreshToken,
                    ProjectId = knownProjectId ?? string.Empty,
                    Email = knownEmail ?? string.Empty
                };

                string accessToken = await GetLiveAccessTokenAsync(acct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(acct.Email))
                {
                    acct.Email = await FetchGoogleAccountEmailAsync(accessToken).ConfigureAwait(false);
                }

                CachedProjectInfo proj = await ResolveCodeAssistProjectAsync(acct, true).ConfigureAwait(false);
                if (proj != null)
                {
                    if (!string.IsNullOrEmpty(proj.ProjectId)) acct.ProjectId = proj.ProjectId;
                    if (!string.IsNullOrEmpty(proj.PlanName)) acct.PlanName = proj.PlanName;
                }

                UpsertSavedAccount(acct);

                // Immediately prime live quota cache
                await FetchDirectGoogleAccountsAsync(true).ConfigureAwait(false);

                return new AccountSignInOutcome
                {
                    Success = true,
                    UserEmail = acct.Email,
                    PlanName = string.IsNullOrEmpty(acct.PlanName) ? "Antigravity" : acct.PlanName
                };
            }
            catch (Exception ex)
            {
                return new AccountSignInOutcome
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        // MARK: - Unified Quota Fetch Pipeline (1. Direct Google Code Assist -> 2. Magpie Local Gateway/CLI)

        public static async Task<List<CloudAccountQuota>> FetchAntigravityAccountsAsync(string gatewayBase, bool allowCliFallback)
        {
            List<CloudAccountQuota> combined = new List<CloudAccountQuota>();

            if (HasDirectGoogleAccounts)
            {
                List<CloudAccountQuota> direct = await FetchDirectGoogleAccountsAsync(false).ConfigureAwait(false);
                if (direct != null && direct.Count > 0)
                {
                    combined.AddRange(direct);
                }
            }

            List<Dictionary<string, object>> httpCards = await FetchViaHttpAsync(gatewayBase ?? DefaultGatewayBase).ConfigureAwait(false);
            if (httpCards != null)
            {
                List<CloudAccountQuota> magpieMapped = MapCards(httpCards);
                MergeUniqueAccounts(combined, magpieMapped);
            }
            else if (combined.Count == 0 && allowCliFallback && IsMagpieInstalled())
            {
                List<Dictionary<string, object>> cliCards = await RunMagpieCliAsync("quota --json").ConfigureAwait(false);
                if (cliCards != null)
                {
                    List<CloudAccountQuota> cliMapped = MapCards(cliCards);
                    MergeUniqueAccounts(combined, cliMapped);
                }
            }

            return combined.Count > 0 ? combined : null;
        }

        public static async Task<List<CloudAccountQuota>> ForceRefreshFromCloudAsync()
        {
            List<CloudAccountQuota> combined = new List<CloudAccountQuota>();

            if (HasDirectGoogleAccounts)
            {
                List<CloudAccountQuota> direct = await FetchDirectGoogleAccountsAsync(true).ConfigureAwait(false);
                if (direct != null && direct.Count > 0)
                {
                    combined.AddRange(direct);
                }
            }

            if (IsMagpieInstalled())
            {
                List<Dictionary<string, object>> cliCards = await RunMagpieCliAsync("quota refresh antigravity --json").ConfigureAwait(false);
                if (cliCards != null)
                {
                    MergeUniqueAccounts(combined, MapCards(cliCards));
                }
            }
            else
            {
                List<Dictionary<string, object>> httpCards = await FetchViaHttpAsync(DefaultGatewayBase).ConfigureAwait(false);
                if (httpCards != null)
                {
                    MergeUniqueAccounts(combined, MapCards(httpCards));
                }
            }

            return combined.Count > 0 ? combined : null;
        }

        private static void MergeUniqueAccounts(List<CloudAccountQuota> target, List<CloudAccountQuota> incoming)
        {
            if (incoming == null) return;
            for (int i = 0; i < incoming.Count; i++)
            {
                CloudAccountQuota cand = incoming[i];
                bool exists = false;
                for (int j = 0; j < target.Count; j++)
                {
                    if (string.Equals(target[j].UserEmail, cand.UserEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    target.Add(cand);
                }
            }
        }

        // MARK: - Direct Google Code Assist Quota RPC (`retrieveUserQuotaSummary` & `fetchAvailableModels`)

        private static async Task<List<CloudAccountQuota>> FetchDirectGoogleAccountsAsync(bool forceRefresh)
        {
            DateTime nowUtc = DateTime.UtcNow;
            lock (StateLock)
            {
                if (!forceRefresh && _cachedDirectQuotas != null && (nowUtc - _lastDirectFetchAtUtc).TotalSeconds < 45.0)
                {
                    return new List<CloudAccountQuota>(_cachedDirectQuotas);
                }
            }

            List<SavedGoogleAccount> saved = LoadSavedAccounts();
            if (saved.Count == 0) return null;

            List<CloudAccountQuota> results = new List<CloudAccountQuota>();
            for (int i = 0; i < saved.Count; i++)
            {
                CloudAccountQuota q = await FetchSingleDirectAccountQuotaAsync(saved[i], i == 0, forceRefresh).ConfigureAwait(false);
                if (q != null)
                {
                    results.Add(q);
                }
            }

            if (results.Count > 0)
            {
                lock (StateLock)
                {
                    _cachedDirectQuotas = new List<CloudAccountQuota>(results);
                    _lastDirectFetchAtUtc = DateTime.UtcNow;
                }
            }
            return results;
        }

        private static async Task<CloudAccountQuota> FetchSingleDirectAccountQuotaAsync(SavedGoogleAccount acct, bool isFirst, bool forceProjectRefresh)
        {
            CloudAccountQuota quotaCard = new CloudAccountQuota
            {
                UserEmail = string.IsNullOrEmpty(acct.Email) ? "Google Account" : acct.Email,
                PlanName = string.IsNullOrEmpty(acct.PlanName) ? "Antigravity" : acct.PlanName,
                SourceName = "Google 官方直连",
                IsDirectGoogleAccount = true,
                IsLatestActive = isFirst,
                ReadAtUtc = DateTime.UtcNow
            };

            try
            {
                CachedProjectInfo proj = await ResolveCodeAssistProjectAsync(acct, forceProjectRefresh).ConfigureAwait(false);
                if (proj == null || string.IsNullOrEmpty(proj.ProjectId))
                {
                    quotaCard.ErrorMessage = "未能解析当前 Google 账号的 Antigravity 项目 ID";
                    return quotaCard;
                }

                if (!string.IsNullOrEmpty(proj.PlanName))
                {
                    quotaCard.PlanName = proj.PlanName;
                }

                // 1. Primary RPC: `v1internal:retrieveUserQuotaSummary` (returns exact 5h + weekly buckets per group!)
                Dictionary<string, object> summaryBody = new Dictionary<string, object>();
                summaryBody["project"] = proj.ProjectId;

                Dictionary<string, object> summaryJson = null;
                try
                {
                    summaryJson = await CallCodeAssistMethodAsync(acct, CodeAssistDailyBase, "retrieveUserQuotaSummary", summaryBody, null).ConfigureAwait(false);
                }
                catch
                {
                    summaryJson = null;
                }

                CloudPoolQuota geminiPool = null;
                CloudPoolQuota claudePool = null;

                if (summaryJson != null)
                {
                    List<object> groups = MiniJson.GetList(summaryJson, "groups");
                    if (groups != null)
                    {
                        for (int g = 0; g < groups.Count; g++)
                        {
                            Dictionary<string, object> gr = groups[g] as Dictionary<string, object>;
                            if (gr == null) continue;
                            string disp = MiniJson.GetString(gr, "displayName") ?? string.Empty;
                            string normPool = NormalizeMagpiePoolName(disp);
                            if (string.IsNullOrEmpty(normPool)) continue;

                            // Skip model-specific versioned pools like "Gemini 4 Argon" when filling the main family pools
                            bool isVersionedPool = ContainsAsciiDigit(normPool);

                            List<object> buckets = MiniJson.GetList(gr, "buckets");
                            if (buckets == null || buckets.Count == 0) continue;

                            double? rem5h = null;
                            DateTime? reset5h = null;
                            double? rem7d = null;
                            DateTime? reset7d = null;

                            for (int b = 0; b < buckets.Count; b++)
                            {
                                Dictionary<string, object> bkt = buckets[b] as Dictionary<string, object>;
                                if (bkt == null) continue;
                                if (MiniJson.GetNullableBool(bkt, "disabled") == true) continue;

                                string win = (MiniJson.GetString(bkt, "window") ?? string.Empty).ToLowerInvariant();
                                double? frac = MiniJson.GetNullableDouble(bkt, "remainingFraction");
                                // Per Magpie: Antigravity omits remainingFraction once 0% is left on an active bucket
                                double pct = frac.HasValue ? Math.Max(0.0, Math.Min(100.0, frac.Value * 100.0)) : 0.0;
                                DateTime? resetUtc = ParseAnyIsoDateUtc(MiniJson.GetString(bkt, "resetTime"));

                                if (win == "5h" || win.Contains("5h") || win.Contains("hour"))
                                {
                                    rem5h = pct;
                                    reset5h = resetUtc;
                                }
                                else if (win == "weekly" || win.Contains("week") || win.Contains("7d") || win.Contains("day"))
                                {
                                    rem7d = pct;
                                    reset7d = resetUtc;
                                }
                            }

                            if (!isVersionedPool && normPool.IndexOf("gemini", StringComparison.OrdinalIgnoreCase) >= 0 && geminiPool == null)
                            {
                                geminiPool = new CloudPoolQuota
                                {
                                    PoolName = "Gemini",
                                    Remaining5h = rem5h ?? 100.0,
                                    ResetsAt5hUtc = reset5h,
                                    Remaining7d = rem7d ?? 100.0,
                                    ResetsAt7dUtc = reset7d
                                };
                            }
                            else if (!isVersionedPool && (normPool.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0 || normPool.IndexOf("gpt", StringComparison.OrdinalIgnoreCase) >= 0) && claudePool == null)
                            {
                                claudePool = new CloudPoolQuota
                                {
                                    PoolName = "Claude & GPT",
                                    Remaining5h = rem5h ?? 100.0,
                                    ResetsAt5hUtc = reset5h,
                                    Remaining7d = rem7d ?? 100.0,
                                    ResetsAt7dUtc = reset7d
                                };
                            }
                        }
                    }
                }

                // 2. Fallback RPC: `v1internal:fetchAvailableModels` if summary omitted pools
                if (geminiPool == null)
                {
                    Dictionary<string, object> modelsJson = await CallCodeAssistMethodAsync(acct, CodeAssistDailyBase, "fetchAvailableModels", summaryBody, null).ConfigureAwait(false);
                    Dictionary<string, object> modelsMap = MiniJson.GetDict(modelsJson, "models");
                    if (modelsMap != null)
                    {
                        foreach (KeyValuePair<string, object> kv in modelsMap)
                        {
                            string modelId = kv.Key ?? string.Empty;
                            if (modelId.StartsWith("chat_", StringComparison.Ordinal) || modelId.StartsWith("tab_", StringComparison.Ordinal)) continue;
                            Dictionary<string, object> mObj = kv.Value as Dictionary<string, object>;
                            if (mObj == null || MiniJson.GetNullableBool(mObj, "disabled") == true) continue;
                            Dictionary<string, object> qInfo = MiniJson.GetDict(mObj, "quotaInfo");
                            if (qInfo == null) continue;

                            double? frac = MiniJson.GetNullableDouble(qInfo, "remainingFraction");
                            double pct = frac.HasValue ? Math.Max(0.0, Math.Min(100.0, frac.Value * 100.0)) : 0.0;
                            DateTime? rUtc = ParseAnyIsoDateUtc(MiniJson.GetString(qInfo, "resetTime"));

                            if (modelId.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) && geminiPool == null)
                            {
                                geminiPool = new CloudPoolQuota
                                {
                                    PoolName = "Gemini",
                                    Remaining5h = pct,
                                    ResetsAt5hUtc = rUtc,
                                    Remaining7d = pct,
                                    ResetsAt7dUtc = rUtc
                                };
                            }
                            else if ((modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase) || modelId.StartsWith("gpt", StringComparison.OrdinalIgnoreCase)) && claudePool == null)
                            {
                                claudePool = new CloudPoolQuota
                                {
                                    PoolName = "Claude & GPT",
                                    Remaining5h = pct,
                                    ResetsAt5hUtc = rUtc,
                                    Remaining7d = pct,
                                    ResetsAt7dUtc = rUtc
                                };
                            }
                        }
                    }
                }

                quotaCard.GeminiPool = geminiPool;
                quotaCard.ClaudeGptPool = claudePool;
                quotaCard.ReadAtUtc = DateTime.UtcNow;
                return quotaCard;
            }
            catch (Exception ex)
            {
                quotaCard.ErrorMessage = ex.Message;
                return quotaCard;
            }
        }

        private static string NormalizeMagpiePoolName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
            string s = displayName.Trim();
            if (s.EndsWith(" models", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 7).Trim();
            }
            return s.Replace(" and ", " & ");
        }

        private static bool ContainsAsciiDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= '0' && s[i] <= '9') return true;
            }
            return false;
        }

        private static async Task<CachedProjectInfo> ResolveCodeAssistProjectAsync(SavedGoogleAccount acct, bool forceRefresh)
        {
            lock (StateLock)
            {
                CachedProjectInfo cached;
                if (!forceRefresh && ProjectCache.TryGetValue(acct.RefreshToken, out cached)
                    && (DateTime.UtcNow - cached.CheckedAtUtc).TotalMinutes < 60.0)
                {
                    return cached;
                }
            }

            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["ideType"] = "ANTIGRAVITY";
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["metadata"] = meta;

            Dictionary<string, object> load = null;
            Exception loadErr = null;
            try
            {
                load = await CallCodeAssistMethodAsync(acct, CodeAssistProdBase, "loadCodeAssist", body, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                loadErr = ex;
            }

            if (load == null)
            {
                if (!string.IsNullOrEmpty(acct.ProjectId))
                {
                    return new CachedProjectInfo
                    {
                        ProjectId = acct.ProjectId,
                        PlanName = acct.PlanName,
                        CheckedAtUtc = DateTime.UtcNow
                    };
                }
                if (loadErr != null) throw loadErr;
                return null;
            }

            Dictionary<string, object> curTier = MiniJson.GetDict(load, "currentTier");
            Dictionary<string, object> paidTier = MiniJson.GetDict(load, "paidTier");

            string planName = FirstNonEmpty(
                paidTier != null ? MiniJson.GetString(paidTier, "name") : null,
                curTier != null ? MiniJson.GetString(curTier, "name") : null,
                paidTier != null ? MiniJson.GetString(paidTier, "id") : null,
                curTier != null ? MiniJson.GetString(curTier, "id") : null,
                acct.PlanName);

            string projId = !string.IsNullOrEmpty(acct.ProjectId)
                ? acct.ProjectId
                : ExtractCompanionProjectId(load.ContainsKey("cloudaicompanionProject") ? load["cloudaicompanionProject"] : null);

            if (string.IsNullOrEmpty(projId))
            {
                // First-time onboarding fallback (matches Magpie's onboardUser)
                string defaultTierId = ExtractDefaultAllowedTierId(load);
                string ver = await GetAntigravityVersionAsync().ConfigureAwait(false);
                Dictionary<string, object> obMeta = new Dictionary<string, object>();
                obMeta["ide_type"] = "ANTIGRAVITY";
                obMeta["ide_version"] = ver;
                obMeta["ide_name"] = "antigravity";

                Dictionary<string, object> obReq = new Dictionary<string, object>();
                obReq["tier_id"] = defaultTierId;
                obReq["metadata"] = obMeta;

                Dictionary<string, string> extraHdr = new Dictionary<string, string>();
                extraHdr["X-Goog-Api-Client"] = "gl-node/22.21.1";

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    Dictionary<string, object> lro = await CallCodeAssistMethodAsync(acct, CodeAssistDailyBase, "onboardUser", obReq, extraHdr).ConfigureAwait(false);
                    if (MiniJson.GetNullableBool(lro, "done") == true)
                    {
                        Dictionary<string, object> respObj = MiniJson.GetDict(lro, "response");
                        if (respObj != null && respObj.ContainsKey("cloudaicompanionProject"))
                        {
                            projId = ExtractCompanionProjectId(respObj["cloudaicompanionProject"]);
                        }
                        break;
                    }
                    await Task.Delay(1800).ConfigureAwait(false);
                }
            }

            CachedProjectInfo resolved = new CachedProjectInfo
            {
                ProjectId = projId,
                PlanName = planName,
                CurrentTier = curTier != null ? MiniJson.GetString(curTier, "id") : string.Empty,
                PaidTier = paidTier != null ? MiniJson.GetString(paidTier, "id") : string.Empty,
                CheckedAtUtc = DateTime.UtcNow
            };

            lock (StateLock)
            {
                ProjectCache[acct.RefreshToken] = resolved;
            }

            if ((!string.IsNullOrEmpty(projId) && !string.Equals(acct.ProjectId, projId, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(planName) && !string.Equals(acct.PlanName, planName, StringComparison.Ordinal)))
            {
                acct.ProjectId = projId;
                acct.PlanName = planName;
                UpsertSavedAccount(acct);
            }

            return resolved;
        }

        private static string ExtractCompanionProjectId(object val)
        {
            if (val == null) return string.Empty;
            string s = val as string;
            if (s != null) return s.Trim();
            Dictionary<string, object> d = val as Dictionary<string, object>;
            if (d != null) return MiniJson.GetString(d, "id") ?? string.Empty;
            return string.Empty;
        }

        private static string ExtractDefaultAllowedTierId(Dictionary<string, object> load)
        {
            List<object> allowed = MiniJson.GetList(load, "allowedTiers");
            string firstId = null;
            if (allowed != null)
            {
                for (int i = 0; i < allowed.Count; i++)
                {
                    Dictionary<string, object> t = allowed[i] as Dictionary<string, object>;
                    if (t == null) continue;
                    string id = MiniJson.GetString(t, "id");
                    if (firstId == null && !string.IsNullOrEmpty(id)) firstId = id;
                    if (MiniJson.GetNullableBool(t, "isDefault") == true && !string.IsNullOrEmpty(id))
                    {
                        return id == "legacy-tier" ? "free-tier" : id;
                    }
                }
            }
            if (string.IsNullOrEmpty(firstId) || firstId == "legacy-tier") return "free-tier";
            return firstId;
        }

        private static async Task<Dictionary<string, object>> CallCodeAssistMethodAsync(
            SavedGoogleAccount acct,
            string baseUrl,
            string method,
            Dictionary<string, object> body,
            Dictionary<string, string> extraHeaders)
        {
            string accessToken = await GetLiveAccessTokenAsync(acct).ConfigureAwait(false);
            string ver = await GetAntigravityVersionAsync().ConfigureAwait(false);
            string ua = "antigravity/hub/" + ver + " windows/amd64";

            string url = baseUrl.TrimEnd('/') + "/v1internal:" + method;
            string jsonBody = MiniJson.Serialize(body ?? new Dictionary<string, object>());

            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
                req.Headers.TryAddWithoutValidation("User-Agent", ua);
                if (extraHeaders != null)
                {
                    foreach (KeyValuePair<string, string> kv in extraHeaders)
                    {
                        req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                using (HttpResponseMessage resp = await GoogleCloudHttp.SendAsync(req).ConfigureAwait(false))
                {
                    string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("CodeAssist (" + ((int)resp.StatusCode).ToString(CultureInfo.InvariantCulture) + "): " + ExtractGoogleApiError(raw, resp.StatusCode.ToString()));
                    }
                    return MiniJson.ParseObject(raw) ?? new Dictionary<string, object>();
                }
            }
        }

        private static async Task<string> GetLiveAccessTokenAsync(SavedGoogleAccount acct)
        {
            lock (StateLock)
            {
                CachedAccessToken cached;
                if (TokenCache.TryGetValue(acct.RefreshToken, out cached)
                    && !string.IsNullOrEmpty(cached.AccessToken)
                    && (cached.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes > 2.0)
                {
                    return cached.AccessToken;
                }
            }

            Dictionary<string, string> form = new Dictionary<string, string>();
            form["grant_type"] = "refresh_token";
            form["refresh_token"] = acct.RefreshToken;
            form["client_id"] = OAuthClientId;
            form["client_secret"] = OAuthClientSecret;

            Dictionary<string, object> json = await PostFormJsonAsync(GoogleTokenUrl, form).ConfigureAwait(false);
            string freshToken = MiniJson.GetString(json, "access_token");
            int expiresIn = MiniJson.GetInt(json, "expires_in", 3600);

            if (string.IsNullOrEmpty(freshToken))
            {
                throw new InvalidOperationException("Google OAuth 令牌已过期，请重新登录");
            }

            lock (StateLock)
            {
                TokenCache[acct.RefreshToken] = new CachedAccessToken
                {
                    AccessToken = freshToken,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(120, expiresIn))
                };
            }
            return freshToken;
        }

        private static async Task<string> FetchGoogleAccountEmailAsync(string accessToken)
        {
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, GoogleUserInfoUrl))
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
                using (HttpResponseMessage resp = await GoogleCloudHttp.SendAsync(req).ConfigureAwait(false))
                {
                    string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Dictionary<string, object> obj = MiniJson.ParseObject(raw);
                    string email = MiniJson.GetString(obj, "email");
                    if (!string.IsNullOrEmpty(email)) return email;
                    throw new InvalidOperationException("无法读取 Google 账号邮箱");
                }
            }
        }

        private static async Task<Dictionary<string, object>> PostFormJsonAsync(string url, Dictionary<string, string> formFields)
        {
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new FormUrlEncodedContent(formFields);
                using (HttpResponseMessage resp = await GoogleCloudHttp.SendAsync(req).ConfigureAwait(false))
                {
                    string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("OAuth (" + ((int)resp.StatusCode).ToString(CultureInfo.InvariantCulture) + "): " + ExtractGoogleApiError(raw, resp.StatusCode.ToString()));
                    }
                    return MiniJson.ParseObject(raw) ?? new Dictionary<string, object>();
                }
            }
        }

        private static string ExtractGoogleApiError(string body, string fallbackStatus)
        {
            Dictionary<string, object> obj = MiniJson.ParseObject(body);
            if (obj != null)
            {
                Dictionary<string, object> errObj = MiniJson.GetDict(obj, "error");
                if (errObj != null)
                {
                    string m = MiniJson.GetString(errObj, "message");
                    if (!string.IsNullOrEmpty(m)) return m;
                }
                string errDesc = MiniJson.GetString(obj, "error_description") ?? MiniJson.GetString(obj, "error") ?? MiniJson.GetString(obj, "message");
                if (!string.IsNullOrEmpty(errDesc)) return errDesc;
            }
            if (!string.IsNullOrWhiteSpace(body))
            {
                string t = body.Trim();
                return t.Length > 180 ? t.Substring(0, 180) : t;
            }
            return fallbackStatus;
        }

        private static async Task<string> GetAntigravityVersionAsync()
        {
            lock (StateLock)
            {
                if ((DateTime.UtcNow - _versionCheckedAtUtc).TotalHours < 6.0 && !string.IsNullOrEmpty(_cachedAntigravityVersion))
                {
                    return _cachedAntigravityVersion;
                }
                _versionCheckedAtUtc = DateTime.UtcNow;
            }

            // 1. Check local installed Antigravity.exe product version first
            try
            {
                string exe = ConversationNavigator.ResolveAntigravityExePath();
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exe);
                    if (fvi != null && !string.IsNullOrEmpty(fvi.ProductVersion))
                    {
                        string pv = fvi.ProductVersion.Trim();
                        int dash = pv.IndexOf('-');
                        if (dash > 0) pv = pv.Substring(0, dash);
                        if (!string.IsNullOrEmpty(pv))
                        {
                            lock (StateLock) { _cachedAntigravityVersion = pv; }
                            return pv;
                        }
                    }
                }
            }
            catch
            {
            }

            // 2. Fallback to Antigravity updater manifest (same as Magpie)
            try
            {
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, AntigravityVersionManifestUrl))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", "electron-builder");
                    using (HttpResponseMessage resp = await LocalGatewayHttp.SendAsync(req).ConfigureAwait(false))
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            string yml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            using (StringReader sr = new StringReader(yml))
                            {
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (line.StartsWith("version:", StringComparison.Ordinal))
                                    {
                                        string v = line.Substring(8).Trim().Trim('\'', '"');
                                        if (!string.IsNullOrEmpty(v))
                                        {
                                            lock (StateLock) { _cachedAntigravityVersion = v; }
                                            return v;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return _cachedAntigravityVersion;
        }

        // MARK: - DPAPI Encrypted Account Persistence (`%LOCALAPPDATA%\AgyAssistant\oauth_accounts.json`)

        private static List<SavedGoogleAccount> LoadSavedAccounts()
        {
            lock (StateLock)
            {
                return LoadSavedAccountsUnsafe();
            }
        }

        private static List<SavedGoogleAccount> LoadSavedAccountsUnsafe()
        {
            List<SavedGoogleAccount> list = new List<SavedGoogleAccount>();
            string path = AccountsFilePath;
            if (!File.Exists(path)) return list;

            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                Dictionary<string, object> root = MiniJson.ParseObject(raw);
                List<object> arr = MiniJson.GetList(root, "accounts");
                if (arr == null) return list;

                for (int i = 0; i < arr.Count; i++)
                {
                    Dictionary<string, object> item = arr[i] as Dictionary<string, object>;
                    if (item == null) continue;
                    string enc = MiniJson.GetString(item, "dpapiRefreshToken");
                    string rt = UnprotectString(enc);
                    if (string.IsNullOrEmpty(rt)) continue;

                    list.Add(new SavedGoogleAccount
                    {
                        Email = MiniJson.GetString(item, "email") ?? string.Empty,
                        RefreshToken = rt,
                        ProjectId = MiniJson.GetString(item, "projectId") ?? string.Empty,
                        PlanName = MiniJson.GetString(item, "planName") ?? string.Empty
                    });
                }
            }
            catch
            {
            }
            return list;
        }

        private static void UpsertSavedAccount(SavedGoogleAccount acct)
        {
            if (acct == null || string.IsNullOrEmpty(acct.RefreshToken)) return;
            lock (StateLock)
            {
                List<SavedGoogleAccount> list = LoadSavedAccountsUnsafe();
                int existingIdx = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if ((!string.IsNullOrEmpty(acct.Email) && string.Equals(list[i].Email, acct.Email, StringComparison.OrdinalIgnoreCase))
                        || string.Equals(list[i].RefreshToken, acct.RefreshToken, StringComparison.Ordinal))
                    {
                        existingIdx = i;
                        break;
                    }
                }

                if (existingIdx >= 0)
                {
                    list[existingIdx] = acct;
                }
                else
                {
                    list.Insert(0, acct);
                }

                SaveAccountsUnsafe(list);
            }
        }

        private static void SaveAccountsUnsafe(List<SavedGoogleAccount> list)
        {
            try
            {
                string dir = AppSettings.AppDataFolder;
                Directory.CreateDirectory(dir);
                List<object> serialized = new List<object>();
                for (int i = 0; i < list.Count; i++)
                {
                    SavedGoogleAccount a = list[i];
                    if (string.IsNullOrEmpty(a.RefreshToken)) continue;
                    string enc = ProtectString(a.RefreshToken);
                    if (string.IsNullOrEmpty(enc)) continue;

                    Dictionary<string, object> obj = new Dictionary<string, object>();
                    obj["email"] = a.Email ?? string.Empty;
                    obj["dpapiRefreshToken"] = enc;
                    obj["projectId"] = a.ProjectId ?? string.Empty;
                    obj["planName"] = a.PlanName ?? string.Empty;
                    serialized.Add(obj);
                }

                Dictionary<string, object> root = new Dictionary<string, object>();
                root["version"] = 1;
                root["accounts"] = serialized;
                File.WriteAllText(AccountsFilePath, MiniJson.Serialize(root), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string ProtectString(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch
            {
                return null;
            }
        }

        private static string UnprotectString(string base64Encrypted)
        {
            if (string.IsNullOrEmpty(base64Encrypted)) return null;
            try
            {
                byte[] enc = Convert.FromBase64String(base64Encrypted);
                byte[] dec = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch
            {
                return null;
            }
        }

        // MARK: - Loopback OAuth Callback HTTP Helper (`127.0.0.1` + `[::1]`, No UAC Required)

        private sealed class OAuthCallbackPayload
        {
            public string Code;
            public string Error;
        }

        private static async Task<OAuthCallbackPayload> WaitForOAuthCallbackAsync(
            TcpListener v4,
            TcpListener v6,
            string expectedState,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            Task<TcpClient> t4 = v4 != null ? v4.AcceptTcpClientAsync() : null;
            Task<TcpClient> t6 = v6 != null ? v6.AcceptTcpClientAsync() : null;

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                TimeSpan rem = deadline - DateTime.UtcNow;
                if (rem <= TimeSpan.Zero) break;

                List<Task> waits = new List<Task>();
                if (t4 != null) waits.Add(t4);
                if (t6 != null) waits.Add(t6);
                Task delayTask = Task.Delay(rem);
                waits.Add(delayTask);

                Task finished = await Task.WhenAny(waits).ConfigureAwait(false);
                if (finished == delayTask) return null;

                TcpClient client = null;
                if (finished == t4)
                {
                    try { client = await t4.ConfigureAwait(false); } catch { return null; }
                    t4 = v4.AcceptTcpClientAsync();
                }
                else if (finished == t6)
                {
                    try { client = await t6.ConfigureAwait(false); } catch { return null; }
                    t6 = v6.AcceptTcpClientAsync();
                }

                if (client != null)
                {
                    OAuthCallbackPayload parsed = HandleLoopbackHttpClient(client, expectedState);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
            }
            return null;
        }

        private static OAuthCallbackPayload HandleLoopbackHttpClient(TcpClient client, string expectedState)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] buf = new byte[8192];
                        int read = stream.Read(buf, 0, buf.Length);
                        if (read <= 0) return null;

                        string reqText = Encoding.ASCII.GetString(buf, 0, read);
                        int lineEnd = reqText.IndexOf("\r\n", StringComparison.Ordinal);
                        string firstLine = lineEnd > 0 ? reqText.Substring(0, lineEnd) : reqText;
                        string[] parts = firstLine.Split(' ');
                        if (parts.Length < 2) return null;

                        string pathAndQuery = parts[1];
                        if (!pathAndQuery.StartsWith("/oauth-callback", StringComparison.OrdinalIgnoreCase))
                        {
                            WriteHttpHtmlResponse(stream, 404, "Not Found");
                            return null;
                        }

                        Dictionary<string, string> query = ParseQueryString(pathAndQuery);
                        string st;
                        query.TryGetValue("state", out st);
                        string code;
                        query.TryGetValue("code", out code);
                        string err;
                        query.TryGetValue("error", out err);

                        if (!string.Equals(st, expectedState, StringComparison.Ordinal) || (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(err)))
                        {
                            WriteHttpHtmlResponse(stream, 400, "<h3>登录校验状态不匹配，请返回 Antigravity 使用助手重试。</h3>");
                            return null;
                        }

                        if (!string.IsNullOrEmpty(err))
                        {
                            WriteHttpHtmlResponse(stream, 200, "<h3>登录未完成 (" + WebUtility.HtmlEncode(err) + ")，您可以关闭此页面。</h3>");
                            return new OAuthCallbackPayload { Error = err };
                        }

                        WriteHttpHtmlResponse(
                            stream,
                            200,
                            "<div style='max-width:440px;margin:60px auto;padding:28px;border-radius:16px;background:#f8fafc;border:1px solid #e2e8f0;font-family:system-ui,sans-serif;text-align:center'>" +
                            "<h2 style='color:#16a34a;margin:0 0 10px'>✅ Antigravity 使用助手授权成功</h2>" +
                            "<p style='color:#475569;margin:0;font-size:14px'>正在同步 Google 实时 5h / 7d 配额，您可以直接关闭此浏览器标签页。</p></div>");

                        return new OAuthCallbackPayload { Code = code };
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private static void WriteHttpHtmlResponse(NetworkStream stream, int statusCode, string bodyInnerHtml)
        {
            string statusText = statusCode == 200 ? "200 OK" : (statusCode == 400 ? "400 Bad Request" : "404 Not Found");
            string html = "<!doctype html><html><head><meta charset='utf-8'><title>Antigravity 使用助手</title></head><body>" + bodyInnerHtml + "</body></html>";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(html);
            string header = "HTTP/1.1 " + statusText + "\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Content-Length: " + bodyBytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        private static Dictionary<string, string> ParseQueryString(string pathAndQuery)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.Ordinal);
            int qIdx = pathAndQuery.IndexOf('?');
            if (qIdx < 0 || qIdx + 1 >= pathAndQuery.Length) return dict;

            string qs = pathAndQuery.Substring(qIdx + 1);
            string[] pairs = qs.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i];
                if (string.IsNullOrEmpty(p)) continue;
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = Uri.UnescapeDataString(p.Substring(0, eq).Replace('+', ' '));
                    string v = Uri.UnescapeDataString(p.Substring(eq + 1).Replace('+', ' '));
                    dict[k] = v;
                }
            }
            return dict;
        }

        private static void SafeStopListener(TcpListener listener)
        {
            if (listener == null) return;
            try { listener.Stop(); } catch { }
        }

        private static string GenerateBase64UrlRandom(int byteCount)
        {
            byte[] buf = new byte[byteCount];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(buf);
            }
            return ToBase64Url(buf);
        }

        private static string ComputeSha256Base64Url(string asciiInput)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.ASCII.GetBytes(asciiInput));
                return ToBase64Url(digest);
            }
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])) return values[i].Trim();
            }
            return string.Empty;
        }

        // MARK: - Secondary Source: Local Magpie Gateway (`127.0.0.1:3425` or `magpie*.exe` CLI)

        public static string ResolvedCliPath
        {
            get
            {
                // 1. Check running magpie process path first (e.g. `magpie-windows-amd64.exe` in Downloads)
                try
                {
                    Process[] procs = Process.GetProcesses();
                    for (int i = 0; i < procs.Length; i++)
                    {
                        try
                        {
                            string pn = procs[i].ProcessName;
                            if (!string.IsNullOrEmpty(pn)
                                && pn.StartsWith("magpie", StringComparison.OrdinalIgnoreCase)
                                && pn.IndexOf("bun", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                string modPath = procs[i].MainModule != null ? procs[i].MainModule.FileName : null;
                                if (!string.IsNullOrEmpty(modPath) && File.Exists(modPath))
                                {
                                    return modPath;
                                }
                            }
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

                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                string[] candidates = new string[]
                {
                    Path.Combine(localApp, "Programs", "Magpie", "magpie.exe"),
                    Path.Combine(localApp, "Magpie", "magpie.exe"),
                    Path.Combine(home, ".local", "bin", "magpie.exe"),
                    Path.Combine(home, "Downloads", "magpie-windows-amd64.exe"),
                    Path.Combine(home, "Downloads", "magpie.exe"),
                    Path.Combine(progFiles, "Magpie", "magpie.exe")
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                    {
                        return candidates[i];
                    }
                }

                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                string[] dirs = pathEnv.Split(Path.PathSeparator);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string dir = dirs[i].Trim();
                    if (string.IsNullOrEmpty(dir)) continue;
                    try
                    {
                        string exe = Path.Combine(dir, "magpie.exe");
                        if (File.Exists(exe)) return exe;
                    }
                    catch
                    {
                    }
                }
                return null;
            }
        }

        public static bool IsMagpieInstalled()
        {
            return ResolvedCliPath != null;
        }

        private static async Task<List<Dictionary<string, object>>> FetchViaHttpAsync(string gatewayBase)
        {
            string trimmed = (gatewayBase ?? DefaultGatewayBase).TrimEnd('/', ' ');
            string url = trimmed + "/v1/magpie/quotas";
            try
            {
                using (HttpResponseMessage resp = await LocalGatewayHttp.GetAsync(url).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ExtractAntigravityProviderCards(raw);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Task<List<Dictionary<string, object>>> RunMagpieCliAsync(string arguments)
        {
            string cliPath = ResolvedCliPath;
            if (string.IsNullOrEmpty(cliPath))
            {
                return Task.FromResult<List<Dictionary<string, object>>>(null);
            }

            return Task.Run(delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = cliPath;
                    psi.Arguments = arguments;
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;

                    using (Process proc = Process.Start(psi))
                    {
                        if (proc == null) return null;
                        string output = proc.StandardOutput.ReadToEnd();
                        if (!proc.WaitForExit(8000))
                        {
                            try { proc.Kill(); } catch { }
                            return null;
                        }
                        if (proc.ExitCode != 0 || string.IsNullOrEmpty(output)) return null;
                        return ExtractAntigravityProviderCards(output);
                    }
                }
                catch
                {
                    return null;
                }
            });
        }

        private static List<Dictionary<string, object>> ExtractAntigravityProviderCards(string json)
        {
            object root = MiniJson.Parse(json);
            if (root == null) return null;

            List<object> arr = root as List<object>;
            if (arr == null)
            {
                Dictionary<string, object> env = root as Dictionary<string, object>;
                if (env != null)
                {
                    arr = MiniJson.GetList(env, "data");
                }
            }
            if (arr == null) return null;

            List<Dictionary<string, object>> filtered = new List<Dictionary<string, object>>();
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> card = arr[i] as Dictionary<string, object>;
                if (card == null) continue;
                string provider = MiniJson.GetString(card, "provider") ?? string.Empty;
                if (string.Equals(provider, "antigravity", StringComparison.OrdinalIgnoreCase)
                    || provider.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(card);
                }
            }
            return filtered;
        }

        private static List<CloudAccountQuota> MapCards(List<Dictionary<string, object>> cards)
        {
            List<CloudAccountQuota> result = new List<CloudAccountQuota>();
            for (int i = 0; i < cards.Count; i++)
            {
                Dictionary<string, object> card = cards[i];
                List<object> rawWins = MiniJson.GetList(card, "windows") ?? new List<object>();

                List<Dictionary<string, object>> geminiWins = new List<Dictionary<string, object>>();
                List<Dictionary<string, object>> claudeWins = new List<Dictionary<string, object>>();

                for (int w = 0; w < rawWins.Count; w++)
                {
                    Dictionary<string, object> win = rawWins[w] as Dictionary<string, object>;
                    if (win == null) continue;
                    string pool = (MiniJson.GetString(win, "pool") ?? string.Empty).ToLowerInvariant();
                    string name = (MiniJson.GetString(win, "name") ?? string.Empty).ToLowerInvariant();
                    if (pool.Contains("gemini") || name.Contains("gemini"))
                    {
                        geminiWins.Add(win);
                    }
                    if (pool.Contains("claude") || name.Contains("claude") || pool.Contains("gpt") || name.Contains("gpt"))
                    {
                        claudeWins.Add(win);
                    }
                }

                CloudAccountQuota acct = new CloudAccountQuota();
                acct.UserEmail = MiniJson.GetString(card, "user") ?? "Antigravity Account";
                acct.PlanName = MiniJson.GetString(card, "plan") ?? "Antigravity";
                acct.SourceName = "Magpie";
                acct.IsDirectGoogleAccount = false;
                acct.GeminiPool = BuildPool("Gemini", geminiWins);
                acct.ClaudeGptPool = BuildPool("Claude & GPT", claudeWins);
                acct.ReadAtUtc = ParseAnyIsoDateUtc(MiniJson.GetString(card, "readAt"));
                acct.IsLatestActive = MiniJson.GetNullableBool(card, "last") ?? false;
                acct.ErrorMessage = MiniJson.GetString(card, "error");
                result.Add(acct);
            }
            return result;
        }

        private static CloudPoolQuota BuildPool(string poolLabel, List<Dictionary<string, object>> windows)
        {
            if (windows == null || windows.Count == 0) return null;
            Dictionary<string, object> win5h = null;
            Dictionary<string, object> win7d = null;

            for (int i = 0; i < windows.Count; i++)
            {
                Dictionary<string, object> w = windows[i];
                string n = (MiniJson.GetString(w, "name") ?? string.Empty).ToLowerInvariant();
                if (win5h == null && (n.Contains("5 hour") || n.Contains("5h")))
                {
                    win5h = w;
                }
                if (win7d == null && (n.Contains("7 day") || n.Contains("7d") || n.Contains("week")))
                {
                    win7d = w;
                }
            }

            CloudPoolQuota pool = new CloudPoolQuota();
            pool.PoolName = poolLabel;
            pool.Remaining5h = win5h != null ? (MiniJson.GetNullableDouble(win5h, "remaining") ?? 100.0) : 100.0;
            pool.ResetsAt5hUtc = win5h != null ? ParseAnyIsoDateUtc(MiniJson.GetString(win5h, "resetsAt")) : null;
            pool.Remaining7d = win7d != null ? (MiniJson.GetNullableDouble(win7d, "remaining") ?? 100.0) : 100.0;
            pool.ResetsAt7dUtc = win7d != null ? ParseAnyIsoDateUtc(MiniJson.GetString(win7d, "resetsAt")) : null;
            return pool;
        }

        public static DateTime? ParseAnyIsoDateUtc(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
            {
                return dto.UtcDateTime;
            }
            return null;
        }
    }
}
