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

// DirectTelegramNotifier: the self-test (/selftest, and once after startup) and the follow-up that checks a flatten really went flat.
// One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        #region Self-test

        private bool startupSelfTestDone;
        private int exposureRepeatMs = 60000;                 // how often STILL EXPOSED is repeated; the tests shorten it
        private const int ExposureRepeatLockMinutes = 720;    // runaway guard for a locked account
        private const int ExposureRepeatFlattenMinutes = 10;  // after a /flatten that did not complete

        private readonly object pnlWatchLock = new object();
        private readonly Dictionary<string, DtnOpenPnlWatch> pnlWatches = new Dictionary<string, DtnOpenPnlWatch>(StringComparer.OrdinalIgnoreCase);

        private DtnOpenPnlWatch PnlWatchFor(string account)
        {
            lock (pnlWatchLock)
            {
                DtnOpenPnlWatch w;
                if (!pnlWatches.TryGetValue(account, out w))
                {
                    w = new DtnOpenPnlWatch();
                    pnlWatches[account] = w;
                }
                return w;
            }
        }

        private static string MsgOpenPnlSuspect(string account, double move)
        {
            return EmWarn + " <b>OPEN P&amp;L LOOKS WRONG</b>\n"
                + Html(account) + " holds a position and net liquidation has moved by " + N2(move) + ", but open P&amp;L has read exactly 0.00 the whole time.\n"
                + "If your connection does not report open P&amp;L, the notifier cannot tell the closing balance from net liquidation, and an end-of-day floor would be too HIGH while you hold a position over the close.\n"
                + "<i>End the day flat, or check /risk against your firm's figure and use /risk seed. A fee can cause a false alarm.</i>";
        }

        private sealed class SelfTestResult
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<string> Armed = new List<string>();
            public readonly List<string> NotArmed = new List<string>();
            public bool AnyAuto;
        }

        // Reads what the risk check depends on, for every tracked account, and says whether automatic flatten could act.
        // It does not gate anything: the risk check already does nothing without these inputs. It makes that visible.
        private SelfTestResult RunSelfTest()
        {
            SelfTestResult r = new SelfTestResult();
            foreach (CbiAccount a in SnapshotTrackedAccounts())
            {
                string name = a.Name;
                double netLiq = NetLiquidationOf(a);
                bool netLiqOk = !double.IsNaN(netLiq) && !double.IsInfinity(netLiq);
                double open = SafeAccountValue(a, AccountItem.UnrealizedProfitLoss);
                bool openOk = !double.IsNaN(open) && !double.IsInfinity(open);
                DataUpFor(a);
                string path;
                lock (dataCheckLock)
                {
                    if (!dataCheckPath.TryGetValue(name, out path))
                        path = "unknown";
                }

                DtnRiskSettings s = BuildRiskSettings(name);
                string source;
                double floor = FloorFor(name, out source);
                List<string> problems = Dtn.ArmingProblems(s, netLiqOk, openOk, floor);
                bool consistencyOn = !double.IsNaN(s.ConsistencyPercent) && s.ConsistencyPercent > 0 && s.ConsistencyPercent < 100;
                bool dailyOn = !string.IsNullOrWhiteSpace(s.DailyLimitText);

                List<string> rules = new List<string>();
                if (!double.IsNaN(floor))
                    rules.Add("floor " + N2(floor) + " (" + source + ")");
                if (dailyOn)
                    rules.Add("daily loss " + s.DailyLimitText);
                if (consistencyOn)
                    rules.Add("consistency " + s.ConsistencyPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%");
                double inactive = InactivityLimitFor(name);
                if (!double.IsNaN(inactive))
                    rules.Add("inactivity " + DaysText(inactive));

                List<string> auto = new List<string>();
                if (s.RiskAutoFlatten && !double.IsNaN(floor))
                    auto.Add("floor");
                if (s.DailyLossAutoFlatten && dailyOn)
                    auto.Add("daily loss");
                if (s.ConsistencyAutoFlatten && consistencyOn && !double.IsNaN(s.StartingBalance))
                    auto.Add("consistency");
                if (s.UnknownFlattenSeconds > 0 && (!double.IsNaN(floor) || dailyOn))
                    auto.Add("data outage");
                bool wantsAuto = s.RiskAutoFlatten || s.DailyLossAutoFlatten || s.ConsistencyAutoFlatten || s.UnknownFlattenSeconds > 0;
                if (wantsAuto && auto.Count == 0 && problems.Count == 0)
                    problems.Add("nothing is monitored on this account: no floor, daily loss limit or consistency rule");

                bool armed = wantsAuto && problems.Count == 0;
                if (wantsAuto)
                {
                    r.AnyAuto = true;
                    (armed ? r.Armed : r.NotArmed).Add(name);
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("<b>").Append(Html(name)).Append("</b> ").Append(problems.Count == 0 ? EmOk : EmWarn).Append("\n");
                sb.Append("  Net liq ").Append(netLiqOk ? N2(netLiq) : "UNREADABLE").Append(Sep)
                  .Append("open P&amp;L ").Append(openOk ? Money(open) : "UNREADABLE").Append("\n");
                sb.Append("  Data check: ").Append(Html(path)).Append("\n");
                sb.Append("  Rules: ").Append(rules.Count == 0 ? "none set" : Html(string.Join(" \u00B7 ", rules.ToArray()))).Append("\n");
                if (wantsAuto)
                    sb.Append("  Auto-flatten: ").Append(armed ? "<b>ARMED</b> (" + Html(string.Join(", ", auto.ToArray())) + ")" : "<b>NOT ARMED</b>").Append("\n");
                else
                    sb.Append("  Auto-flatten: off\n");
                foreach (string problem in problems)
                    sb.Append("  ").Append(EmWarn).Append(" ").Append(Html(problem)).Append("\n");
                bool holding = NetPositions(a).Count > 0;
                DtnOpenPnlWatch watch = PnlWatchFor(name);
                if (watch.Suspect)
                    sb.Append("  ").Append(EmWarn).Append(" Open P&amp;L has read exactly 0.00 while net liquidation moved by ").Append(N2(watch.Move)).Append(": it may not be reported.\n");
                else if (holding && openOk && open == 0.0)
                    sb.Append("  ").Append(EmWarn).Append(" Holding a position and open P&amp;L reads exactly 0.00: if the market has moved since you entered, it is not being reported.\n");
                r.Lines.Add(sb.ToString());
            }
            return r;
        }

        private static string AutoFlattenSummary(SelfTestResult r)
        {
            if (!r.AnyAuto)
                return "off";
            List<string> parts = new List<string>();
            if (r.Armed.Count > 0)
                parts.Add("<b>ARMED</b> for " + Html(string.Join(", ", r.Armed.ToArray())));
            if (r.NotArmed.Count > 0)
                parts.Add(EmWarn + " <b>NOT ARMED</b> for " + Html(string.Join(", ", r.NotArmed.ToArray())) + " (see /selftest)");
            return string.Join(Sep, parts.ToArray());
        }

        private string AutoFlattenLine()
        {
            return "Auto-flatten: " + AutoFlattenSummary(RunSelfTest());
        }

        private string BuildSelfTestText(SelfTestResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(EmInfo).Append(" <b>SELF-TEST</b>").Append(Sep).Append(TimeText(NtNow())).Append("\n");
            sb.Append("Auto-flatten: ").Append(AutoFlattenSummary(r)).Append("\n\n");
            if (r.Lines.Count == 0)
                sb.Append("No tracked accounts.\n");
            foreach (string line in r.Lines)
                sb.Append(line);
            sb.Append("\n<i>Open P&amp;L must be a real figure while you hold a position: a constant 0.00 would make the closing balance equal net liquidation. ")
              .Append("This checks the readings and settings the risk check depends on. It cannot prove a flatten will succeed: that needs the Sim checklist.</i>");
            return sb.ToString();
        }

        // Once, shortly after startup, when the accounts have attached and the first readings are in.
        private void RunStartupSelfTestOnce()
        {
            if (startupSelfTestDone || !started || (DateTime.UtcNow - startedUtc).TotalSeconds < 20)
                return;
            startupSelfTestDone = true;
            try
            {
                SelfTestResult r = RunSelfTest();
                string text = BuildSelfTestText(r);
                if (r.AnyAuto && r.NotArmed.Count > 0)
                    EnqueueText(text);                      // you switched auto-flatten on and it cannot act: say so loudly
                else
                    EnqueueQuiet(text);
            }
            catch (Exception ex)
            {
                WriteError("Self-test", ex);
            }
        }

        #endregion

        #region Flatten follow-up

        // After an automatic flatten or /lock nobody is waiting for a reply: check that the account really did go flat, and say so either way.
        // It sends nothing to the broker. A second flatten while the first is still working could reverse the position;
        // the lock already re-sends one when no close is working.
        private void WatchFlat(List<CbiAccount> accounts, string what)
        {
            if (autoFlattenWatchMs <= 0 || accounts == null || accounts.Count == 0)
                return;
            List<CbiAccount> watched = new List<CbiAccount>(accounts);
            int budget = autoFlattenWatchMs;
            RunInBackground(async () =>
            {
                DateTime start = DateTime.UtcNow;
                await Task.Delay(Math.Min(FlattenFirstCheckMs, budget));
                bool flat;
                string check = BuildPostFlattenCheck(watched, out flat);
                while (!flat && (DateTime.UtcNow - start).TotalMilliseconds < budget)
                {
                    await Task.Delay(FlattenPollMs);
                    check = BuildPostFlattenCheck(watched, out flat);
                }
                double took = (DateTime.UtcNow - start).TotalSeconds;
                string names = string.Join(", ", watched.Select(w => w.Name).ToArray());
                if (flat)
                {
                    EnqueueQuiet(MsgFlatConfirmed(names, what, took));
                }
                else
                {
                    EnqueueCritical(MsgStillExposed(names, what, took, check), StillExposedMinimal(watched, took));
                    RepeatExposureAlerts(watched, what, start, true, ExposureRepeatLockMinutes);
                }
            }, "Flat check");
        }

        private static string ElapsedText(double seconds)
        {
            return seconds < 60 ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s" : FormatDuration(TimeSpan.FromSeconds(seconds));
        }

        private static string MsgFlatConfirmed(string accounts, string what, double seconds)
        {
            return EmOk + " <b>FLAT CONFIRMED</b>" + Sep + Html(accounts) + "\n"
                + Html(what) + ": no open positions or working orders after " + ElapsedText(seconds);
        }

        private static string MsgStillExposed(string accounts, string what, double seconds, string check)
        {
            return EmSiren + " <b>STILL EXPOSED</b>" + Sep + Html(accounts) + "\n"
                + Html(what) + " was sent, but after " + ElapsedText(seconds) + " there is still exposure:\n"
                + check
                + "<i>A locked account has new positions closed and the flatten re-sent when no close is working, but check your broker's platform now.</i>";
        }

        // What the second channel says: which accounts, how long, and what to do. No positions, no amounts.
        private static string StillExposedMinimal(List<CbiAccount> accounts, double seconds)
        {
            string names = string.Join(", ", accounts.Select(a => Dtn.MaskAccount(a.Name)).ToArray());
            return "STILL EXPOSED (" + ElapsedText(seconds) + "): account " + names + " did not go flat. Close it by hand now.";
        }

        // Accounts that still hold a position or working orders, with their lines.
        private List<CbiAccount> ExposedAccounts(List<CbiAccount> accounts, out string check)
        {
            List<CbiAccount> exposed = new List<CbiAccount>();
            StringBuilder sb = new StringBuilder();
            foreach (CbiAccount a in accounts)
            {
                bool flat;
                string accountLines = BuildPositionLines(a, out flat);
                if (flat)
                    continue;
                exposed.Add(a);
                sb.Append("<b>").Append(Html(a.Name)).Append("</b>\n").Append(accountLines);
            }
            check = sb.ToString();
            return exposed;
        }

        // A single STILL EXPOSED is easy to miss. Say it again every minute, on both channels, until the account is flat,
        // unlocked (for a lock), or the cap runs out. It sends nothing to the broker.
        private void RepeatExposureAlerts(List<CbiAccount> accounts, string what, DateTime sinceUtc, bool whileLocked, int capMinutes)
        {
            List<CbiAccount> watched = new List<CbiAccount>(accounts);
            RunInBackground(async () =>
            {
                DateTime end = DateTime.UtcNow.AddMinutes(capMinutes);
                while (!terminated && DateTime.UtcNow < end)
                {
                    await Task.Delay(exposureRepeatMs);
                    if (terminated)
                        return;
                    if (whileLocked)
                    {
                        List<CbiAccount> stillLocked = watched.Where(w => IsAccountLocked(w.Name)).ToList();
                        if (stillLocked.Count == 0)
                        {
                            EnqueueQuiet(EmInfo + " <b>No longer watching</b>" + Sep + Html(string.Join(", ", watched.Select(w => w.Name).ToArray())) + ": unlocked.");
                            return;
                        }
                        watched = stillLocked;
                    }
                    string check;
                    List<CbiAccount> exposed = ExposedAccounts(watched, out check);
                    double seconds = (DateTime.UtcNow - sinceUtc).TotalSeconds;
                    if (exposed.Count == 0)
                    {
                        EnqueueQuiet(MsgFlatConfirmed(string.Join(", ", watched.Select(w => w.Name).ToArray()), what + ", late", seconds));
                        return;
                    }
                    watched = exposed;
                    EnqueueCritical(MsgStillExposed(string.Join(", ", exposed.Select(w => w.Name).ToArray()), what, seconds, check), StillExposedMinimal(exposed, seconds));
                }
            }, "Exposure repeat");
        }

        #endregion
    }
}
