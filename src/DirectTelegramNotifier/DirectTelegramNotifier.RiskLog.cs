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

// DirectTelegramNotifier: the risk log, which records every reading the risk check sees so a session can be replayed.
// One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        #region Risk log (record what the risk check saw, so a real session can be replayed)

        private string riskLogPath;
        private int riskLogWrites;
        private readonly object riskLogLock = new object();
        private readonly Dictionary<string, string> riskLogSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> riskLogKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> riskLogLast = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // One row when a reading changes (and at least every RiskLogKeepaliveSec): enough to hold the reading between rows and
        // tick once a second, which is what the live check does. Settings are written when they change.
        private void RecordRiskReading(string account, DateTime now, DtnRiskInput input)
        {
            if (!cfgEnableRiskLog)
                return;
            try
            {
                string settingsText = Dtn.SettingsToText(input.Settings);
                string key = Dtn.FormatReadingRow(DateTime.MinValue, account, input);
                lock (riskLogLock)
                {
                    string previousSettings, previousKey;
                    DateTime previousAt;
                    bool settingsChanged = !riskLogSettings.TryGetValue(account, out previousSettings) || previousSettings != settingsText;
                    bool changed = !riskLogKey.TryGetValue(account, out previousKey) || previousKey != key;
                    bool due = !riskLogLast.TryGetValue(account, out previousAt) || (now - previousAt).TotalSeconds >= RiskLogKeepaliveSec;
                    if (!settingsChanged && !changed && !due)
                        return;

                    StringBuilder rows = new StringBuilder();
                    if (settingsChanged)
                    {
                        rows.Append(Dtn.FormatSettingsRow(now, account, input.Settings)).Append("\r\n");
                        riskLogSettings[account] = settingsText;
                    }
                    rows.Append(Dtn.FormatReadingRow(now, account, input)).Append("\r\n");
                    riskLogKey[account] = key;
                    riskLogLast[account] = now;
                    AppendRiskLogLocked(rows.ToString());
                }
            }
            catch (Exception)
            {
                // Recording must never interfere with the risk check.
            }
        }

        private void AppendRiskLogLocked(string text)
        {
            if (riskLogPath == null)
                riskLogPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ConfigPath()), "DirectTelegramNotifier-risk.csv");
            FileInfo info = new FileInfo(riskLogPath);
            if (info.Exists && ++riskLogWrites % 200 == 0 && info.Length > RiskLogMaxBytes)
            {
                string old = riskLogPath + ".old";
                if (File.Exists(old))
                    File.Delete(old);
                File.Move(riskLogPath, old);
                info = new FileInfo(riskLogPath);
            }
            if (!info.Exists)
                File.AppendAllText(riskLogPath, "# DirectTelegramNotifier risk log. S = an account's rules, R = one reading. Replay it with tests/run-replay.sh (see docs/TESTING.md)." + "\r\n", new UTF8Encoding(false));
            File.AppendAllText(riskLogPath, text, new UTF8Encoding(false));
        }

        #endregion
    }
}
