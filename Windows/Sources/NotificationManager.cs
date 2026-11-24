using System;
using System.Collections.Generic;
using System.Media;
using System.Windows.Forms;

namespace AntigravityAssistant
{
    // MARK: - Windows Native Tray Notification & Sound Manager

    public sealed class NotificationManager
    {
        public static readonly NotificationManager Shared = new NotificationManager();

        public Action<string> OnNotificationJumpRequested;

        private NotifyIcon _notifyIcon;
        private string _lastBalloonConversationId;
        private DateTime? _lastLowQuotaAlertAtUtc;
        private readonly Dictionary<string, DateTime> _lastLowContextAlertByConvUtc = new Dictionary<string, DateTime>();

        private NotificationManager()
        {
        }

        public void AttachNotifyIcon(NotifyIcon notifyIcon)
        {
            _notifyIcon = notifyIcon;
            if (_notifyIcon != null)
            {
                _notifyIcon.BalloonTipClicked += delegate
                {
                    string cid = _lastBalloonConversationId;
                    if (!string.IsNullOrEmpty(cid) && OnNotificationJumpRequested != null)
                    {
                        OnNotificationJumpRequested(cid);
                    }
                };
            }
        }

        public void PlaySound(string kind, bool enabled)
        {
            if (!enabled) return;
            try
            {
                switch (kind)
                {
                    case "Error":
                        SystemSounds.Hand.Play();
                        break;
                    case "Waiting":
                    case "Warn":
                        SystemSounds.Exclamation.Play();
                        break;
                    default:
                        SystemSounds.Asterisk.Play();
                        break;
                }
            }
            catch
            {
            }
        }

        public void NotifyTurnCompleted(ConversationSnapshot conv, double? durationSeconds, AppSettings settings)
        {
            if (durationSeconds.HasValue && durationSeconds.Value < settings.MinTurnDurationForAlert)
            {
                return;
            }
            PlaySound("Glass", settings.SoundEnabled);
            if (!settings.NotifyOnComplete) return;

            string durText = durationSeconds.HasValue ? (" · 耗时 " + TimeFormatters.FormatDuration(durationSeconds.Value)) : string.Empty;
            SendBanner(
                "✨ Antigravity 已完成回复" + durText,
                conv.Title + "（点击通知直接定位到此会话）",
                conv.Id,
                ToolTipIcon.Info);
        }

        public void NotifyWaitingForUser(ConversationSnapshot conv, string detail, AppSettings settings)
        {
            PlaySound("Waiting", settings.SoundEnabled);
            if (!settings.NotifyOnWaitingOrError) return;
            SendBanner(
                "🙋 Antigravity 等待你的确认",
                !string.IsNullOrEmpty(detail) ? detail : conv.Title,
                conv.Id,
                ToolTipIcon.Warning);
        }

        public void NotifyError(ConversationSnapshot conv, string detail, AppSettings settings)
        {
            PlaySound("Error", settings.SoundEnabled);
            if (!settings.NotifyOnWaitingOrError) return;
            SendBanner(
                "⚠️ Antigravity 会话执行异常",
                !string.IsNullOrEmpty(detail) ? detail : conv.Title,
                conv.Id,
                ToolTipIcon.Error);
        }

        public void NotifyLowQuotaIfNeeded(double remainingPercent, AppSettings settings)
        {
            if (!settings.NotifyOnLowRemaining || remainingPercent > settings.LowQuotaThreshold) return;
            DateTime now = DateTime.UtcNow;
            if (_lastLowQuotaAlertAtUtc.HasValue && (now - _lastLowQuotaAlertAtUtc.Value).TotalSeconds < 1800.0)
            {
                return;
            }
            _lastLowQuotaAlertAtUtc = now;
            PlaySound("Warn", settings.SoundEnabled);
            int pct = (int)Math.Round(remainingPercent);
            SendBanner(
                "⚡ 5h 配额 Remaining 预警 (" + pct + "%)",
                "当前配额剩余 " + pct + "%，建议合理规划高消耗任务。",
                null,
                ToolTipIcon.Warning);
        }

        private void SendBanner(string title, string body, string conversationId, ToolTipIcon icon)
        {
            _lastBalloonConversationId = conversationId;
            if (_notifyIcon != null && _notifyIcon.Visible)
            {
                try
                {
                    _notifyIcon.ShowBalloonTip(4500, title, string.IsNullOrEmpty(body) ? " " : body, icon);
                }
                catch
                {
                }
            }
        }
    }
}
