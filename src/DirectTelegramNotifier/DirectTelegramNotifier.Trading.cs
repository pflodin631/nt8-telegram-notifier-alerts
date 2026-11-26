#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using CbiAccount = NinjaTrader.Cbi.Account;
using Dtn = NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore;
#endregion

// DirectTelegramNotifier: "can this setup place a trade right now?". Reads what NinjaTrader says, hands it to DtnTrade.Blockers
// (Core.cs) and shows the answer in /status and the end-of-day report. It sends no alert of its own.
// One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        #region Can it trade

        // Lock order: riskLock may be held when tradeLock is taken (the state file is read and written under riskLock), and the end-of-day
        // report holds statsLock when it takes tradeLock. tradeLock is never held while another lock is taken or NinjaTrader is read.
        private readonly object tradeLock = new object();
        private readonly Dictionary<string, DtnExpectation> expectations = new Dictionary<string, DtnExpectation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DtnBlockedTime> blockedTime = new Dictionary<string, DtnBlockedTime>(StringComparer.OrdinalIgnoreCase);
        private DateTime tradeLastUtc = DateTime.MinValue;

        private int RunningStrategiesOn(string account)
        {
            lock (strategyLock)
                return strategies.Values.Count(x => IsRunningState(x.State) && string.Equals(x.AccountName, account, StringComparison.OrdinalIgnoreCase));
        }

        // A connected feed that sends no ticks, for an instrument this account holds or runs a strategy on (the same scope as the risk
        // check: a feed nobody here uses is not this account's problem). Stale stays set until ticks come back, so the caller only asks
        // while the market is open. Reads NinjaTrader: no lock held.
        private string StaleFeedFor(CbiAccount a)
        {
            HashSet<string> mine = new HashSet<string>(NetPositions(a).Keys, StringComparer.OrdinalIgnoreCase);
            lock (strategyLock)
            {
                foreach (StrategyInfo si in strategies.Values)
                {
                    if (IsRunningState(si.State) && string.Equals(si.AccountName, a.Name, StringComparison.OrdinalIgnoreCase)
                        && si.Instrument != null && !string.IsNullOrEmpty(si.Instrument.FullName))
                        mine.Add(si.Instrument.FullName);
                }
            }
            if (mine.Count == 0)
                return null;
            lock (feedLock)
            {
                foreach (FeedWatch w in feeds.Values)
                    if (w.Subscribed && w.Stale && mine.Contains(w.Name))
                        return w.Name;
            }
            return null;
        }

        private bool StrategiesScanned()
        {
            lock (strategyLock)
                return cfgEnableStrategyWatch && strategyBaselined;
        }

        // One entry per tracked account. Callers only ask while the market is open (stale stays set after it closes). Reads NinjaTrader, so it must not be called with riskLock, tradeLock or strategyLock held.
        private List<DtnTradeFacts> ReadTradeFacts()
        {
            List<CbiAccount> accounts = SnapshotTrackedAccounts();
            string leader = ResolveLeaderName();
            bool known = StrategiesScanned();

            int leaderRunning = leader.Length > 0 ? RunningStrategiesOn(leader) : 0;
            bool leaderExpected;
            lock (tradeLock)
            {
                DtnExpectation le;
                leaderExpected = leader.Length > 0 && expectations.TryGetValue(leader, out le) && le.Expected;
            }

            List<DtnTradeFacts> result = new List<DtnTradeFacts>();
            foreach (CbiAccount a in accounts)
            {
                bool follower = leader.Length > 0 && !a.Name.Equals(leader, StringComparison.OrdinalIgnoreCase);
                bool dataDown = !DataUpFor(a);
                DtnTradeFacts f = new DtnTradeFacts
                {
                    Account = a.Name,
                    OrderConnectionDown = AccountConnectionDown(a),
                    DataConnectionDown = dataDown,
                    StaleInstrument = dataDown ? null : StaleFeedFor(a),
                    Locked = IsAccountLocked(a.Name),
                    StrategiesKnown = known,
                    RunningStrategies = RunningStrategiesOn(a.Name),
                    Leader = follower ? leader : string.Empty,
                    LeaderRunning = follower ? leaderRunning : 0,
                    LeaderExpected = follower && leaderExpected
                };
                lock (tradeLock)
                {
                    DtnExpectation e;
                    f.Expected = expectations.TryGetValue(a.Name, out e) && e.Expected;
                }
                result.Add(f);
            }

            // followers also carry what stops the leader (when the leader is one of the tracked accounts)
            DtnTradeFacts lf = result.FirstOrDefault(x => x.Account.Equals(leader, StringComparison.OrdinalIgnoreCase));
            if (lf != null)
            {
                foreach (DtnTradeFacts f in result)
                {
                    if (f.Leader.Length == 0)
                        continue;
                    f.LeaderOrderConnectionDown = lf.OrderConnectionDown;
                    f.LeaderDataConnectionDown = lf.DataConnectionDown;
                    f.LeaderStaleInstrument = lf.StaleInstrument;
                    f.LeaderLocked = lf.Locked;
                }
            }
            return result;
        }

        // Runs on the housekeeping tick, after CheckStrategies. Keeps "expected to trade" and the blocked time for the end-of-day report.
        private void ObserveTrading(DateTime now)
        {
            try
            {
                bool open = IsMarketOpen(now);
                if (!StrategiesScanned())
                {
                    tradeLastUtc = now;
                    return;
                }

                foreach (CbiAccount a in SnapshotTrackedAccounts())
                {
                    int running = RunningStrategiesOn(a.Name);
                    lock (tradeLock)
                    {
                        DtnExpectation e;
                        if (!expectations.TryGetValue(a.Name, out e))
                        {
                            e = new DtnExpectation();
                            expectations[a.Name] = e;
                        }
                        e.Observe(now, running > 0, open);
                    }
                }

                double step = tradeLastUtc == DateTime.MinValue ? 0 : Math.Max(0, Math.Min((now - tradeLastUtc).TotalSeconds, DtnExpectation.MaxStepSeconds));
                tradeLastUtc = now;
                if (!open || step <= 0)
                    return;

                List<DtnTradeFacts> facts = ReadTradeFacts();
                string session = CurrentSessionKey();
                lock (tradeLock)
                {
                    foreach (DtnTradeFacts f in facts)
                    {
                        if (!DtnTrade.IsExpected(f))
                            continue;
                        DtnBlockedTime b;
                        if (!blockedTime.TryGetValue(f.Account, out b))
                        {
                            b = new DtnBlockedTime();
                            blockedTime[f.Account] = b;
                        }
                        b.Add(step, DtnTrade.Blockers(f));
                        b.Session = session;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError("ObserveTrading", ex);
            }
        }

        // One line for /status. A failure here must never cost the rest of the reply.
        private string BuildTradeLine()
        {
            try
            {
                if (!IsMarketOpen(DateTime.UtcNow))
                    return "<b>Trade blockers:</b> not checked while the market is closed";

                List<DtnTradeFacts> facts = ReadTradeFacts();
                if (facts.Count == 0)
                    return "<b>Trade blockers:</b> no accounts tracked yet";

                List<KeyValuePair<string, List<string>>> per = new List<KeyValuePair<string, List<string>>>();
                foreach (DtnTradeFacts f in facts)
                    per.Add(new KeyValuePair<string, List<string>>(f.Account, DtnTrade.Blockers(f)));
                List<DtnTradeGroup> groups = DtnTrade.Group(per);

                StringBuilder sb = new StringBuilder();
                sb.Append("<b>Trade blockers:</b> ");
                if (groups.Count == 0)
                    sb.Append("none found among the checks");
                else
                {
                    sb.Append(EmWarn).Append(" ");
                    for (int i = 0; i < groups.Count; i++)
                    {
                        DtnTradeGroup g = groups[i];
                        if (i > 0)
                            sb.Append("; ");
                        sb.Append(facts.Count > 1 && g.Accounts.Count == facts.Count ? "all accounts" : Html(string.Join(", ", g.Accounts.ToArray())));
                        sb.Append(": ").Append(Html(string.Join(", ", g.Reasons.ToArray())));
                    }
                }

                bool watchOn;
                lock (strategyLock)
                    watchOn = cfgEnableStrategyWatch;
                if (!watchOn)
                    sb.Append(" <i>(EnableStrategyWatch is off, so \"no strategy enabled\" is not checked.)</i>");
                else if (!StrategiesScanned())
                    sb.Append(" <i>(strategies not scanned yet, so \"no strategy enabled\" is not checked.)</i>");
                sb.Append(" <i>Replikanto's on/off state is not visible to these checks.</i>");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                WriteError("BuildTradeLine", ex);
                return string.Empty;
            }
        }

        // End-of-day block: accounts that could not trade for a while during open market. Empty when there was nothing to say,
        // and on an error: it must never cost the report.
        private string BuildBlockedEodBlock()
        {
            try
            {
                List<KeyValuePair<string, List<string>>> per = new List<KeyValuePair<string, List<string>>>();
                Dictionary<string, double[]> secs = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                lock (tradeLock)
                {
                    foreach (KeyValuePair<string, DtnBlockedTime> kv in blockedTime)
                    {
                        if (kv.Value.BlockedSeconds < 60)
                            continue;
                        secs[kv.Key] = new[] { kv.Value.BlockedSeconds, kv.Value.TradingSeconds };
                        per.Add(new KeyValuePair<string, List<string>>(kv.Key, kv.Value.Reasons.OrderBy(r => r, StringComparer.Ordinal).ToList()));
                    }
                }
                List<DtnTradeGroup> groups = DtnTrade.Group(per);
                if (groups.Count == 0)
                    return string.Empty;

                StringBuilder sb = new StringBuilder();
                sb.Append(EmWarn).Append(" <b>Could not trade</b>\n");
                foreach (DtnTradeGroup g in groups)
                {
                    double blocked = 0, trading = 0;
                    foreach (string acct in g.Accounts)
                    {
                        blocked = Math.Max(blocked, secs[acct][0]);
                        trading = Math.Max(trading, secs[acct][1]);
                    }
                    sb.Append(Html(string.Join(", ", g.Accounts.ToArray()))).Append(": ")
                      .Append(FormatDuration(TimeSpan.FromSeconds(blocked))).Append(" of ").Append(FormatDuration(TimeSpan.FromSeconds(trading)))
                      .Append(" open market, ").Append(Html(string.Join(", ", g.Reasons.ToArray()))).Append("\n");
                }
                sb.Append("<i>Seen from this PC while the market was open. Time NinjaTrader or the PC was not running is not counted. Replikanto's on/off state is not visible.</i>");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                WriteError("BuildBlockedEodBlock", ex);
                return string.Empty;
            }
        }

        private void ResetBlockedTime()
        {
            lock (tradeLock)
                blockedTime.Clear();
        }

        // ---- state file lines (read and written under riskLock)
        //   strat|account=idleSeconds                      a strategy was seen on this account; open-market time since, in seconds
        //   blk|account=session|blockedSec|tradingSec|reason;reason     blocked time this session, for the end-of-day report
        // A state file without these lines loads as before. An older version skips them (it only creates an empty risk state for the account).

        private void AppendTradeState(StringBuilder sb)
        {
            lock (tradeLock)
            {
                foreach (KeyValuePair<string, DtnExpectation> kv in expectations)
                    if (kv.Value.Seen)
                        sb.Append("strat|").Append(kv.Key).Append('=').Append(Math.Round(kv.Value.IdleSeconds).ToString("0", CultureInfo.InvariantCulture)).Append("\r\n");
                foreach (KeyValuePair<string, DtnBlockedTime> kv in blockedTime)
                    sb.Append("blk|").Append(kv.Key).Append('=').Append(kv.Value.Session).Append('|')
                      .Append(Math.Round(kv.Value.BlockedSeconds).ToString("0", CultureInfo.InvariantCulture)).Append('|')
                      .Append(Math.Round(kv.Value.TradingSeconds).ToString("0", CultureInfo.InvariantCulture)).Append('|')
                      .Append(string.Join(";", kv.Value.Reasons.ToArray())).Append("\r\n");
            }
        }

        // Returns true when the line was one of ours (valid or not): the caller moves on to the next line.
        private bool LoadTradeStateLine(string field, string account, string value, string session)
        {
            if (field == "strat")
            {
                double idle;
                if (DtnTrade.TryParseIdle(value, out idle))
                    lock (tradeLock)
                    {
                        DtnExpectation e = new DtnExpectation();
                        e.Restore(idle);
                        expectations[account] = e;
                    }
                return true;
            }
            if (field == "blk")
            {
                // only this session's blocked time survives a restart
                string[] p = value.Split(new[] { '|' }, 4);
                double blocked, trading;
                if (p.Length == 4 && p[0] == session && DtnTrade.TryParseIdle(p[1], out blocked) && DtnTrade.TryParseIdle(p[2], out trading))
                    lock (tradeLock)
                    {
                        DtnBlockedTime b = new DtnBlockedTime();
                        b.BlockedSeconds = blocked;
                        b.TradingSeconds = trading;
                        b.Session = p[0];
                        foreach (string r in p[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            b.Reasons.Add(r);
                        blockedTime[account] = b;
                    }
                return true;
            }
            return false;
        }

        #endregion
    }
}
