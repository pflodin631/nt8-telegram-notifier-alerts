#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using CbiAccount = NinjaTrader.Cbi.Account;
using CbiConnection = NinjaTrader.Cbi.Connection;
using CbiExecution = NinjaTrader.Cbi.Execution;
using CbiInstrument = NinjaTrader.Cbi.Instrument;
using CbiOrder = NinjaTrader.Cbi.Order;
using CbiPosition = NinjaTrader.Cbi.Position;
using Dtn = NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore;
#endregion

// DirectTelegramNotifier: the optional second alert channel (BackupAlertUrl) for critical alerts.
// One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        // An alert that also goes to the second channel: the full text for Telegram, and what the third-party service gets.
        private sealed class CriticalAlert
        {
            public string Full;
            public string Minimal;
        }

        #region Second alert channel (optional)

        private readonly object backupLock = new object();
        private readonly Dictionary<string, DateTime> backupRecent = new Dictionary<string, DateTime>();
        private Func<string, string, string, Task> backupPost;           // url, body, content type; the tests replace it
        private bool backupDeliveryDown;
        private DateTime lastDeliveryBackupCheckUtc = DateTime.MinValue;

        // To Telegram, and to the backup channel when there is one.
        private void EnqueueCritical(string full, string minimal)
        {
            EnqueueText(full);
            SendBackup(full, minimal);
        }

        private static string JsonText(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private string BackupBody(string plain, out string contentType)
        {
            string line = "[NT8 notifier] " + plain;
            if (cfgBackupFormat == "json")
            {
                contentType = "application/json";
                string t = JsonText(line);
                return "{\"text\":" + t + ",\"content\":" + t + ",\"message\":" + t + "}";
            }
            contentType = "text/plain";
            return line;
        }

        // Fire and forget, with one retry. The same text is not sent twice within a minute.
        // The channel is a third-party service: by default it gets the minimal text (no amounts), not the full alert.
        private void SendBackup(string full, string minimal)
        {
            if (string.IsNullOrEmpty(cfgBackupUrl) || terminated)
                return;
            string plain = ToPlainText(cfgBackupDetail == "full" ? full : minimal).Replace("\r", string.Empty).Trim();
            if (plain.Length == 0)
                return;
            DateTime now = DateTime.UtcNow;
            lock (backupLock)
            {
                DateTime last;
                if (backupRecent.TryGetValue(plain, out last) && (now - last).TotalSeconds < BackupDedupeSec)
                    return;
                backupRecent[plain] = now;
                if (backupRecent.Count > 40)
                {
                    foreach (string old in backupRecent.Where(kv => (now - kv.Value).TotalSeconds > 600).Select(kv => kv.Key).ToList())
                        backupRecent.Remove(old);
                }
            }

            string type;
            string body = BackupBody(plain, out type);
            string url = cfgBackupUrl;
            Task.Run(async () =>
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        await PostBackupAsync(url, body, type);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 2)
                            WriteThrottled("Backup alert failed: " + Describe(ex));
                        else
                            await Task.Delay(3000);
                    }
                }
            });
        }

        private async Task PostBackupAsync(string url, string body, string contentType)
        {
            Func<string, string, string, Task> custom = backupPost;
            if (custom != null)
            {
                await custom(url, body, contentType);
                return;
            }
            using (CancellationTokenSource cts = new CancellationTokenSource(10000))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
                request.Headers.TryAddWithoutValidation("Title", "NT8 notifier");
                request.Headers.TryAddWithoutValidation("Priority", "urgent");
                using (HttpResponseMessage response = await Http.SendAsync(request, cts.Token))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private async Task<string> TestBackupAsync()
        {
            if (string.IsNullOrEmpty(cfgBackupUrl))
                return EmInfo + " BackupAlertUrl is not set, so there is no backup channel. See the config file.";
            string type;
            string body = BackupBody("Test: the backup alert channel works (" + TimeText(NtNow()) + ").", out type);
            try
            {
                await PostBackupAsync(cfgBackupUrl, body, type);
                return EmOk + " Test sent to the backup channel (" + cfgBackupFormat + "). Check that it arrived on your phone.";
            }
            catch (Exception ex)
            {
                return EmWarn + " The backup channel FAILED: " + Html(Redact(Describe(ex)));
            }
        }

        // The case Telegram cannot report on itself: its alerts are not getting through. Say so through the other channel.
        private void CheckDeliveryBackup(DateTime now)
        {
            if (string.IsNullOrEmpty(cfgBackupUrl) || (now - lastDeliveryBackupCheckUtc).TotalSeconds < 30)
                return;
            lastDeliveryBackupCheckUtc = now;
            string problem = DeliveryProblem(now);
            if (problem != null && !backupDeliveryDown)
            {
                backupDeliveryDown = true;
                int priority;
                string down = "Telegram alerts are NOT getting through: " + problem + ". " + Num(QueuedCount(out priority)) + " waiting. "
                    + "Critical alerts still come through this channel. Check NinjaTrader and your positions.";
                SendBackup(down, down);
            }
            else if (problem == null && backupDeliveryDown)
            {
                backupDeliveryDown = false;
                SendBackup("Telegram alerts are getting through again.", "Telegram alerts are getting through again.");
            }
        }

        #endregion
    }
}
