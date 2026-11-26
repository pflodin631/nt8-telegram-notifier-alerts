#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using CbiAccount = NinjaTrader.Cbi.Account;
using CbiConnection = NinjaTrader.Cbi.Connection;
using Dtn = NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore;
#endregion

// DirectTelegramNotifier: "can this setup place a trade right now?". Reads what NinjaTrader says, hands it to DtnTrade.Blockers
// (Core.cs) and shows the answer in /status and the end-of-day report. DtnTradeNag (Core.cs) turns a lasting blocker into NOT TRADING
// reminders, and /away silences exactly those reminders and nothing else.
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

        // NOT TRADING reminders and /away, guarded by tradeLock.
        private readonly DtnTradeNag tradeNag = new DtnTradeNag();
        private readonly HashSet<string> nagAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // accounts blocked in this episode
        private DateTime awayUntilUtc = DateTime.MinValue;
        private TimeSpan tradeClockSkew = TimeSpan.Zero;          // only the tests move the clock of /away with this (as connClockSkew does for connections)
        private DateTime TradeNow() { return DateTime.UtcNow + tradeClockSkew; }

        // Strategies on an account that can trade now (Realtime) and ones that are starting (Historical or Transition, for the first
        // DtnTrade.StartGraceSeconds after they were first seen like that: after a restart they load history before they trade).
        // Past the grace a strategy that is still not Realtime counts as neither. Takes strategyLock; sets the "not live since" time.
        private int StrategyCountsOn(string account, DateTime now, out int starting)
        {
            int running = 0;
            starting = 0;
            lock (strategyLock)
            {
                foreach (StrategyInfo si in strategies.Values)
                {
                    if (!string.Equals(si.AccountName, account, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (IsRunningState(si.State))
                    {
                        running++;
                        si.NotLiveSinceUtc = DateTime.MinValue;
                    }
                    else if (IsStartingState(si.State))
                    {
                        if (si.NotLiveSinceUtc == DateTime.MinValue)
                            si.NotLiveSinceUtc = now;
                        if ((now - si.NotLiveSinceUtc).TotalSeconds < DtnTrade.StartGraceSeconds)
                            starting++;
                    }
                    else
                        si.NotLiveSinceUtc = DateTime.MinValue;
                }
            }
            return running;
        }

        // Is this account's connection (or, when NinjaTrader does not say which one it uses, any connection) one that STILL UNSTABLE
        // is already reporting on? Takes connLock: call it with no other lock held.
        private bool AccountConnectionFlapping(CbiAccount a)
        {
            try
            {
                CbiConnection c = FindAccountConnection(a);
                lock (connLock)
                {
                    if (connFlapping.Count == 0)
                        return false;
                    return c == null || connFlapping.ContainsKey(ConnectionName(c));
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool DataCheckIsFallback(string account)
        {
            lock (dataCheckLock)
            {
                string path;
                return dataCheckPath.TryGetValue(account, out path) && path.StartsWith("ANY data connection", StringComparison.Ordinal);
            }
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

        // One entry per tracked account. Callers only ask while the market is open (stale stays set after it closes). Reads NinjaTrader,
        // so it must not be called with riskLock, tradeLock, strategyLock or connLock held.
        private List<DtnTradeFacts> ReadTradeFacts(DateTime now)
        {
            List<CbiAccount> accounts = SnapshotTrackedAccounts();
            string leader = ResolveLeaderName();
            bool known = StrategiesScanned();

            int leaderStarting = 0;
            int leaderRunning = leader.Length > 0 ? StrategyCountsOn(leader, now, out leaderStarting) : 0;
            bool leaderExpected;
            lock (tradeLock)
            {
                DtnExpectation le;
                leaderExpected = leader.Length > 0 && expectations.TryGetValue(leader, out le) && le != null && le.Expected;
            }

            List<DtnTradeFacts> result = new List<DtnTradeFacts>();
            foreach (CbiAccount a in accounts)
            {
                bool follower = leader.Length > 0 && !a.Name.Equals(leader, StringComparison.OrdinalIgnoreCase);
                bool dataDown = !DataUpFor(a);
                int starting;
                int running = StrategyCountsOn(a.Name, now, out starting);
                DtnTradeFacts f = new DtnTradeFacts
                {
                    Account = a.Name,
                    OrderConnectionDown = AccountConnectionDown(a),
                    DataConnectionDown = dataDown,
                    DataCheckFallback = dataDown && DataCheckIsFallback(a.Name),
                    ConnectionFlapping = AccountConnectionFlapping(a),
                    StaleInstrument = dataDown ? null : StaleFeedFor(a),
                    Locked = IsAccountLocked(a.Name),
                    StrategiesKnown = known,
                    RunningStrategies = running,
                    StartingStrategies = starting,
                    Leader = follower ? leader : string.Empty,
                    LeaderRunning = follower ? leaderRunning : 0,
                    LeaderStarting = follower ? leaderStarting : 0,
                    LeaderExpected = follower && leaderExpected
                };
                lock (tradeLock)
                {
                    DtnExpectation e;
                    f.Expected = expectations.TryGetValue(a.Name, out e) && e != null && e.Expected;
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
                    f.LeaderDataCheckFallback = lf.DataCheckFallback;
                    f.LeaderConnectionFlapping = lf.ConnectionFlapping;
                    f.LeaderStaleInstrument = lf.StaleInstrument;
                    f.LeaderLocked = lf.Locked;
                }
            }
            return result;
        }

        // Runs at the end of the housekeeping tick, after CheckStrategies (and after every check that existed before it, so nothing
        // here can hold one of them back). Keeps "expected to trade", the blocked time for the end-of-day report and the NOT TRADING
        // reminders. It never throws: a failure is logged and this tick is skipped. Everything goes by the `now` passed in.
        private void ObserveTrading(DateTime now)
        {
            try
            {
                ExpireAway(now);
                bool open = IsMarketOpen(now);
                bool scanned = StrategiesScanned();

                if (scanned)
                {
                    foreach (CbiAccount a in SnapshotTrackedAccounts())
                    {
                        int starting;
                        int running = StrategyCountsOn(a.Name, now, out starting);
                        lock (tradeLock)
                        {
                            DtnExpectation e;
                            if (!expectations.TryGetValue(a.Name, out e) || e == null)
                            {
                                e = new DtnExpectation();
                                expectations[a.Name] = e;
                            }
                            e.Observe(now, running + starting > 0, open);
                        }
                    }
                }

                double step = tradeLastUtc == DateTime.MinValue ? 0 : Math.Max(0, Math.Min((now - tradeLastUtc).TotalSeconds, DtnExpectation.MaxStepSeconds));
                tradeLastUtc = now;

                // Without a strategy scan nothing is known about who is expected to trade: no blocked time and no reminders.
                bool live = open && scanned;
                List<DtnTradeFacts> facts = live ? ReadTradeFacts(now) : null;
                if (facts != null && step > 0)
                {
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

                RunTradeNag(now, live, facts);
            }
            catch (Exception ex)
            {
                WriteError("ObserveTrading", ex);
            }
        }

        // ---- NOT TRADING reminders

        private void RunTradeNag(DateTime now, bool live, List<DtnTradeFacts> facts)
        {
            HashSet<string> episode;
            lock (tradeLock)
                episode = new HashSet<string>(nagAccounts, StringComparer.OrdinalIgnoreCase);

            // nagPer: expected accounts that have a blocker. lapsedPer: accounts of this episode that are no longer expected to trade.
            // An episode with such an account ends as "reminders stopped", never as "trading possible again", whether or not the account
            // still has a reason: the reason list of an account that is not expected is empty only because nobody expects anything of it.
            List<KeyValuePair<string, List<string>>> nagPer = new List<KeyValuePair<string, List<string>>>();
            List<KeyValuePair<string, List<string>>> lapsedPer = new List<KeyValuePair<string, List<string>>>();
            bool anyLapsed = false, anyUncovered = false;
            int total = 0;
            if (live && facts != null)
            {
                total = facts.Count;
                foreach (DtnTradeFacts f in facts)
                {
                    bool inEpisode = episode.Contains(f.Account);
                    bool expected = DtnTrade.IsExpected(f);
                    // an account of this episode is asked as if it were still expected, so that "no strategy enabled" keeps counting
                    List<string> reasons = DtnTrade.NagReasons(f, inEpisode);
                    if (inEpisode && !expected)
                    {
                        anyLapsed = true;
                        if (reasons.Count > 0)
                            lapsedPer.Add(new KeyValuePair<string, List<string>>(f.Account, reasons));
                        continue;
                    }
                    if (reasons.Count == 0 || !expected)
                        continue;
                    nagPer.Add(new KeyValuePair<string, List<string>>(f.Account, reasons));
                    if (!DtnTrade.CoveredByFlap(f))
                        anyUncovered = true;
                }
            }

            // Muted: /away, or every blocked account is blocked only by a connection that STILL UNSTABLE is already reporting.
            // The episode keeps running while muted, and its clock restarts when the mute ends. /away is read in the same lock as
            // the tick, so a /away that arrives while this runs cannot be followed by one more reminder.
            bool covered = nagPer.Count > 0 && !anyUncovered;
            DtnNagResult res;
            lock (tradeLock)
            {
                foreach (KeyValuePair<string, List<string>> kv in nagPer)
                    nagAccounts.Add(kv.Key);
                bool away = awayUntilUtc > now;
                res = tradeNag.Tick(now, nagPer.Count > 0, anyLapsed, live, away || covered);
                if (!tradeNag.InEpisode)
                    nagAccounts.Clear();
            }

            if (res.Event == DtnNagEvent.None)
                return;
            TimeSpan blockedFor = TimeSpan.FromSeconds(res.BlockedSeconds);
            if (res.Event == DtnNagEvent.NotTrading || res.Event == DtnNagEvent.StillNotTrading)
                EnqueueText(MsgNotTrading(DtnTrade.Group(nagPer), total, blockedFor, res.Event == DtnNagEvent.StillNotTrading));
            else if (res.Event == DtnNagEvent.PossibleAgain)
                EnqueueQuiet(MsgTradingPossibleAgain(blockedFor));
            else
                EnqueueQuiet(MsgNagLapsed(DtnTrade.Group(lapsedPer), total));
        }

        // ---- /away

        private void ExpireAway(DateTime now)
        {
            bool expired = false;
            lock (tradeLock)
            {
                if (awayUntilUtc != DateTime.MinValue && now >= awayUntilUtc)
                {
                    awayUntilUtc = DateTime.MinValue;
                    expired = true;
                }
            }
            if (!expired)
                return;
            lock (riskLock)
                riskStateDirty = true;
            EnqueueQuiet(MsgAwayEnded());
        }

        // The end time as a clock time in the PC's zone, worked out from the UTC instant so a clock change in between does not shift it.
        private static string AwayEndText(DateTime untilUtc, TimeZoneInfo zone)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(untilUtc, DateTimeKind.Utc), zone);
            return (local + NtClockOffset()).ToString("ddd HH:mm", CultureInfo.InvariantCulture);
        }

        private string HandleAwayCommand(string argument)
        {
            try
            {
                TimeSpan span;
                string error;
                if (!DtnTrade.ParseAway(argument, out span, out error))
                    return EmInfo + " " + Html(error);

                DateTime until = TradeNow() + span;
                lock (tradeLock)
                    awayUntilUtc = until;
                lock (riskLock)
                    riskStateDirty = true;
                return EmInfo + " <b>AWAY</b> for " + FormatDuration(span) + ", until " + AwayEndText(until, TimeZoneInfo.Local) + " (NinjaTrader time)\n"
                    + "NOT TRADING reminders are silenced. Risk, RISK UNKNOWN, position, lock and connection alerts still come.\n"
                    + "/back ends it. /away again changes the end time.";
            }
            catch (Exception ex)
            {
                WriteError("HandleAwayCommand", ex);
                return EmInfo + " /away failed: " + Html(Truncate(ex.Message, 150));
            }
        }

        private string HandleBackCommand()
        {
            try
            {
                DateTime now = TradeNow();
                bool was;
                lock (tradeLock)
                {
                    was = awayUntilUtc > now;
                    awayUntilUtc = DateTime.MinValue;
                }
                lock (riskLock)
                    riskStateDirty = true;
                return was ? EmInfo + " <b>BACK</b>. NOT TRADING reminders are on again." : EmInfo + " You were not away. Reminders are on.";
            }
            catch (Exception ex)
            {
                WriteError("HandleBackCommand", ex);
                return EmInfo + " /back failed: " + Html(Truncate(ex.Message, 150));
            }
        }

        private string AwayLine()
        {
            try
            {
                DateTime until;
                lock (tradeLock)
                    until = awayUntilUtc;
                if (until <= TradeNow())
                    return string.Empty;
                return "<b>Away</b> until " + AwayEndText(until, TimeZoneInfo.Local)
                    + " (NinjaTrader time). NOT TRADING reminders are silenced; other alerts are not. /back ends it.\n";
            }
            catch (Exception ex)
            {
                WriteError("AwayLine", ex);
                return string.Empty;
            }
        }

        // One line for /status. A failure here must never cost the rest of the reply.
        private string BuildTradeLine()
        {
            try
            {
                if (!IsMarketOpen(DateTime.UtcNow))
                    return "<b>Trade blockers:</b> not checked while the market is closed";

                List<DtnTradeFacts> facts = ReadTradeFacts(DateTime.UtcNow);
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
        //   away=endTicks                                  /away is on until then (no '|' in the key; an expired value is ignored on load)
        // A state file without these lines loads as before. An older version skips them (for strat| and blk| it only creates an empty
        // risk state for the account).

        // Built on the side: a fault here leaves the rest of the state file intact instead of losing the whole save.
        private void AppendTradeState(StringBuilder sb)
        {
            try
            {
                StringBuilder mine = new StringBuilder();
                lock (tradeLock)
                {
                    foreach (KeyValuePair<string, DtnExpectation> kv in expectations)
                        if (kv.Value != null && kv.Value.Seen)
                            mine.Append("strat|").Append(kv.Key).Append('=').Append(Math.Round(kv.Value.IdleSeconds).ToString("0", CultureInfo.InvariantCulture)).Append("\r\n");
                    if (awayUntilUtc > TradeNow())
                        mine.Append("away=").Append(awayUntilUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                    foreach (KeyValuePair<string, DtnBlockedTime> kv in blockedTime)
                        mine.Append("blk|").Append(kv.Key).Append('=').Append(kv.Value.Session).Append('|')
                          .Append(Math.Round(kv.Value.BlockedSeconds).ToString("0", CultureInfo.InvariantCulture)).Append('|')
                          .Append(Math.Round(kv.Value.TradingSeconds).ToString("0", CultureInfo.InvariantCulture)).Append('|')
                          .Append(string.Join(";", kv.Value.Reasons.ToArray())).Append("\r\n");
                }
                sb.Append(mine.ToString());
            }
            catch (Exception ex)
            {
                WriteThrottled("Saving the trade-blocker state failed: " + Describe(ex));
            }
        }

        // away=endTicks. Only a time in the future is accepted, cut to the 7 day maximum.
        private void LoadAwayLine(string value)
        {
            try
            {
                long ticks;
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
                    return;
                DateTime until = new DateTime(ticks, DateTimeKind.Utc);
                DateTime now = TradeNow();
                if (until <= now)
                    return;
                // A PC clock that was set back since the file was written can make a valid end time look a little too far ahead: keep it,
                // cut to the 7 day maximum. More than a day beyond the maximum is not something this program wrote: ignored.
                if ((until - now).TotalSeconds > DtnTrade.MaxAwaySeconds + 86400)
                    return;
                if ((until - now).TotalSeconds > DtnTrade.MaxAwaySeconds)
                    until = now.AddSeconds(DtnTrade.MaxAwaySeconds);
                lock (tradeLock)
                    awayUntilUtc = until;
            }
            catch (Exception ex)
            {
                WriteThrottled("Loading away= failed: " + Describe(ex));
            }
        }

        // Returns true when the line was one of ours (valid or not): the caller moves on to the next line.
        private bool LoadTradeStateLine(string field, string account, string value, string session)
        {
            try
            {
                return LoadTradeStateLineCore(field, account, value, session);
            }
            catch (Exception ex)
            {
                WriteThrottled("Loading a trade-blocker state line failed: " + Describe(ex));
                return field == "strat" || field == "blk";             // our line, unusable: the lines after it still load
            }
        }

        private bool LoadTradeStateLineCore(string field, string account, string value, string session)
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
