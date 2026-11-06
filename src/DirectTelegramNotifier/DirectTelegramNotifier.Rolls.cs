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

// DirectTelegramNotifier: contract rolls. Warns before a futures contract that is in use expires, and lists contracts in /status.
// One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        #region Contract rolls

        private sealed class RollEntry
        {
            public string Name;
            public DateTime Expiry;
            public string UsedBy;
        }

        private readonly object rollLock = new object();
        private List<RollEntry> rollEntries = new List<RollEntry>();
        private readonly Dictionary<string, int> rollAlerted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private DateTime rollRefreshedUtc = DateTime.MinValue;

        // The expiry date NinjaTrader gives a contract, or null when it gives none (stocks, forex, anything unknown).
        // Read by name so a NinjaTrader without this property means "no information" and not a failed compile.
        private static DateTime? ExpiryOf(CbiInstrument inst)
        {
            try
            {
                if (inst == null)
                    return null;
                object value = null;
                System.Reflection.PropertyInfo property = typeof(CbiInstrument).GetProperty("Expiry");
                if (property != null)
                    value = property.GetValue(inst, null);
                else
                {
                    System.Reflection.FieldInfo field = typeof(CbiInstrument).GetField("Expiry");
                    if (field != null)
                        value = field.GetValue(inst);
                }
                if (!(value is DateTime))
                    return null;
                DateTime date = (DateTime)value;
                int year = DateTime.Now.Year;
                if (date.Year < year - 1 || date.Year > year + 5)    // "never expires" is a far-future placeholder
                    return null;
                return date;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Rebuilds the list of contracts in use, at most once a minute: running strategies, open positions and working orders,
        // anything traded since the last end-of-day report, and StaleDataInstruments.
        private void RefreshRolls(bool force)
        {
            DateTime now = DateTime.UtcNow;
            lock (rollLock)
            {
                if (!force && (now - rollRefreshedUtc).TotalSeconds < 60)
                    return;
                rollRefreshedUtc = now;
            }

            Dictionary<string, CbiInstrument> wanted = WantedInstruments();
            Dictionary<string, List<string>> usedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> note = delegate(string instrument, string who)
            {
                List<string> list;
                if (!usedBy.TryGetValue(instrument, out list))
                {
                    list = new List<string>();
                    usedBy[instrument] = list;
                }
                if (!list.Contains(who))
                    list.Add(who);
            };

            lock (strategyLock)
            {
                foreach (StrategyInfo s in strategies.Values)
                {
                    if (s.Instrument == null || IsTerminatedState(s.State))
                        continue;
                    wanted[s.Instrument.FullName] = s.Instrument;
                    note(s.Instrument.FullName, "strategy " + s.Name + " (" + s.AccountName + ")");
                }
            }
            foreach (CbiAccount a in SnapshotTrackedAccounts())
            {
                foreach (CbiInstrument inst in CollectExposedInstruments(a))
                    note(inst.FullName, "position or order on " + a.Name);
            }

            List<RollEntry> entries = new List<RollEntry>();
            foreach (KeyValuePair<string, CbiInstrument> kv in wanted)
            {
                DateTime? expiry = ExpiryOf(kv.Value);
                if (!expiry.HasValue)
                    continue;
                List<string> who;
                string text = string.Empty;
                if (usedBy.TryGetValue(kv.Key, out who))
                    text = string.Join("; ", who.Take(3).ToArray()) + (who.Count > 3 ? " and " + Num(who.Count - 3) + " more" : string.Empty);
                entries.Add(new RollEntry { Name = kv.Key, Expiry = expiry.Value.Date, UsedBy = text });
            }

            lock (rollLock)
                rollEntries = entries.OrderBy(e => e.Expiry).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Runs on the watchdog tick. One message each time a contract in use reaches a more urgent level; none after that for the same level.
        private void CheckContractRolls()
        {
            try
            {
                RefreshRolls(false);
                if (cfgRollWarnDays <= 0)
                    return;

                DateTime today = NtNow().Date;
                List<string> quiet = new List<string>();
                List<string> loud = new List<string>();
                lock (rollLock)
                {
                    foreach (RollEntry e in rollEntries)
                    {
                        int days = (int)(e.Expiry - today).TotalDays;
                        int level = Dtn.RollLevel(days, cfgRollWarnDays);
                        int done;
                        rollAlerted.TryGetValue(e.Name, out done);
                        if (level <= done)
                            continue;
                        rollAlerted[e.Name] = level;
                        (level == 1 ? quiet : loud).Add(MsgRoll(level, e.Name, e.Expiry, days, e.UsedBy));
                    }
                }
                foreach (string m in quiet)
                    EnqueueQuiet(m);
                foreach (string m in loud)
                    EnqueueText(m);
            }
            catch (Exception ex)
            {
                WriteError("CheckContractRolls", ex);
            }
        }

        // The "Contracts" block of /status: each contract in use, its expiry and the days left.
        private string BuildContractLines()
        {
            try
            {
                RefreshRolls(false);
                List<RollEntry> list;
                lock (rollLock)
                    list = rollEntries.ToList();
                if (list.Count == 0)
                    return string.Empty;

                DateTime today = NtNow().Date;
                StringBuilder sb = new StringBuilder();
                sb.Append("\n<b>Contracts</b>\n");
                foreach (RollEntry e in list)
                {
                    int days = (int)(e.Expiry - today).TotalDays;
                    sb.Append("  <code>").Append(Html(e.Name)).Append("</code> ").Append(days < 0 ? "expired " : "expires ")
                      .Append(e.Expiry.ToString("ddd d MMM", CultureInfo.InvariantCulture)).Append(Sep).Append(Dtn.DaysLeftText(days));
                    int level = Dtn.RollLevel(days, cfgRollWarnDays);
                    if (level == 1)
                        sb.Append(" ").Append(EmWarn).Append(" roll soon");
                    else if (level == 2)
                        sb.Append(" ").Append(EmSiren).Append(" <b>roll now</b>");
                    else if (level >= 3)
                        sb.Append(" ").Append(EmStop).Append(days < 0 ? " <b>expired</b>" : " <b>expires today</b>");
                    sb.Append("\n");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                WriteError("BuildContractLines", ex);
                return string.Empty;
            }
        }

        // One line for the end-of-day report, only while a contract is inside the warning window. Reads the last list; takes no other lock.
        private string BuildRollEodLine()
        {
            if (cfgRollWarnDays <= 0)
                return string.Empty;
            List<RollEntry> list;
            lock (rollLock)
                list = rollEntries.ToList();

            DateTime today = NtNow().Date;
            List<string> parts = new List<string>();
            foreach (RollEntry e in list)
            {
                int days = (int)(e.Expiry - today).TotalDays;
                if (Dtn.RollLevel(days, cfgRollWarnDays) > 0)
                    parts.Add("<code>" + Html(e.Name) + "</code> " + (days < 0 ? "expired " : "expires ")
                        + e.Expiry.ToString("ddd d MMM", CultureInfo.InvariantCulture) + " (" + Dtn.DaysLeftText(days) + ")");
            }
            return parts.Count == 0 ? string.Empty : EmWarn + " <b>Roll</b>" + Sep + string.Join(Sep, parts.ToArray());
        }

        #endregion
    }
}
