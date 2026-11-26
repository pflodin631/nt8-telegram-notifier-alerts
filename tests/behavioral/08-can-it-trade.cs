using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Reflection; using System.Text.RegularExpressions;
using NinjaTrader.Cbi;
// "Can this setup place a trade right now?": the /status line, the end-of-day block, followers going by the leader, and the state file.
class H8
{
    static readonly string Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtn-h8");
    static object O; static Type T;
    static BindingFlags B = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(string name, params object[] args)
    {
        foreach (var m in T.GetMethods(B)) if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(m.IsStatic ? null : O, args);
        throw new Exception("no method " + name);
    }
    static void Set(string f, object v) { var fi = T.GetField(f, B); if (fi == null) throw new Exception("no field " + f); fi.SetValue(O, v); }
    static object Get(string f) { var fi = T.GetField(f, B); if (fi == null) throw new Exception("no field " + f); return fi.GetValue(O); }
    static string Plain(string s) { return Regex.Replace(s, "<[^>]+>", "").Replace("&amp;", "&"); }
    static int fails = 0, passes = 0;
    static void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (ok) passes++; else fails++; }
    static Type SI;
    static object Strat(string name, string acct, string state)
    {
        object info = Activator.CreateInstance(SI);
        SI.GetField("Ref").SetValue(info, info); SI.GetField("Name").SetValue(info, name); SI.GetField("AccountName").SetValue(info, acct); SI.GetField("State").SetValue(info, state);
        ((IDictionary)Get("strategies"))[info] = info;
        return info;
    }
    static string Status() { return Plain((string)Call("BuildTradeLine")); }
    static string Eod() { return Plain((string)Call("BuildBlockedEodBlock")); }
    static List<string> Drain()
    {
        var res = new List<string>();
        while (true)
        {
            object m = Call("PeekNextMessage"); if (m == null) break; Call("RemoveMessage", m);
            res.Add(Plain((string)m.GetType().GetField("Text", B).GetValue(m)));
        }
        return res;
    }
    static IDictionary Exp() { return (IDictionary)Get("expectations"); }
    static bool Expected(string a) { return Exp().Contains(a) && (bool)Exp()[a].GetType().GetProperty("Expected").GetValue(Exp()[a], null); }

    static void Main()
    {
        if (System.IO.Directory.Exists(Dir)) System.IO.Directory.Delete(Dir, true);
        System.IO.Directory.CreateDirectory(Dir);
        NinjaTrader.Core.Globals.UserDataDir = Dir;
        T = Type.GetType("NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifier, AddOn");
        SI = T.GetNestedType("StrategyInfo", BindingFlags.NonPublic | BindingFlags.Public);
        O = Activator.CreateInstance(T, true);
        Set("cfgAccountName", "L,F1,F2"); Set("cfgLeaderAccount", "L"); Set("authorizedUserId", 42L);
        Set("staleScheduleAlways", true);                   // the market is always open for this test
        Set("cfgEnableStrategyWatch", true); Set("strategyBaselined", true);
        Set("started", true); Set("startedUtc", DateTime.UtcNow.AddMinutes(-30));
        Connection.Connections.Clear();
        var conn = new Connection { Status = ConnectionStatus.Connected, PriceStatus = ConnectionStatus.Connected, Options = new ConnectionOptions { Name = "Orders+Data" } };
        Connection.Connections.Add(conn);
        var L = new Account { Name = "L", NetLiq = 50000, Connection = conn }; var F1 = new Account { Name = "F1", NetLiq = 50000, Connection = conn }; var F2 = new Account { Name = "F2", NetLiq = 50000, Connection = conn };
        var tracked = (IList)Get("trackedAccounts"); tracked.Add(L); tracked.Add(F1); tracked.Add(F2);
        DateTime t = DateTime.UtcNow;

        Console.WriteLine("--- 1. /status line");
        Check("nothing wrong, no strategy ever seen: no blocker found", Status().Contains("Trade blockers: none found among the checks"));
        object s = Strat("Bot", "L", "Realtime");
        Call("ObserveTrading", t);
        Check("a strategy on the leader makes the leader expected; the follower has no expectation of its own", Expected("L") && !Expected("F1"));
        var facts0 = (IList)Call("ReadTradeFacts", DateTime.UtcNow);
        object ff = null; foreach (object x in facts0) if ((string)x.GetType().GetField("Account").GetValue(x) == "F1") ff = x;
        Check("...but it inherits the leader's expectation", (bool)Type.GetType("NinjaTrader.NinjaScript.AddOns.DtnTrade, AddOn").GetMethod("IsExpected").Invoke(null, new object[] { ff }));
        Check("the line says Replikanto is not visible", Status().Contains("on/off state is not visible"));
        Check("still no blocker with the strategy running", Status().Contains("none found among the checks"));

        conn.Status = ConnectionStatus.Disconnected;
        string line = Status();
        Check("order connection down: NO, all three accounts, one line", line.Contains("Trade blockers: ") && !line.Contains("Can trade") && line.Contains("all accounts: order connection down") && !line.Contains("\n"));
        conn.Status = ConnectionStatus.Connected; conn.PriceStatus = ConnectionStatus.Disconnected;
        Check("data connection down", Status().Contains("all accounts: data connection down"));
        conn.PriceStatus = ConnectionStatus.Connected;

        ((IDictionary)Get("strategies")).Remove(s);
        line = Status();
        Check("leader's strategy gone: all three accounts say so, in one group", line.Contains("all accounts: no strategy enabled"));
        s = Strat("Bot", "L", "Terminated");
        Check("a Terminated strategy counts as disabled", Status().Contains("no strategy enabled"));
        ((IDictionary)Get("strategies")).Remove(s);
        s = Strat("Bot", "L", "Historical");
        Check("a Historical (loading) strategy inside the start-up grace is not 'no strategy enabled'", Status().Contains("none found among the checks"));
        SI.GetField("NotLiveSinceUtc").SetValue(s, DateTime.UtcNow.AddMinutes(-20));
        Check("only Realtime counts as running: a strategy still Historical after the 15 minute grace is not enough", Status().Contains("all accounts: no strategy enabled"));
        ((IDictionary)Get("strategies")).Remove(s);
        s = Strat("Bot2", "L", "Realtime");
        Check("a strategy running again clears it", Status().Contains("none found among the checks"));

        lock (Get("riskLock")) ((IDictionary)Get("accountLocks"))["F1"] = Activator.CreateInstance(T.GetNestedType("AccountLock", B));
        Check("a locked account shows the lock, the others do not", Status().Contains("F1: locked by the notifier") && !Status().Contains("F2:"));
        lock (Get("riskLock")) ((IDictionary)Get("accountLocks")).Clear();

        Set("strategyBaselined", false);
        conn.Status = ConnectionStatus.Disconnected;
        Check("strategies not scanned yet: connection reasons still shown, no-strategy is not checked, and it says why",
            Status().Contains("order connection down") && Status().Contains("not scanned yet") && !Status().Contains("EnableStrategyWatch"));
        Set("cfgEnableStrategyWatch", false);
        Check("EnableStrategyWatch=false says that, not not-scanned-yet", Status().Contains("EnableStrategyWatch is off") && !Status().Contains("not scanned yet"));
        conn.Status = ConnectionStatus.Connected; Set("strategyBaselined", true); Set("cfgEnableStrategyWatch", true);

        Set("staleScheduleAlways", false);
        Set("staleQuietWindows", NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore.ParseQuietWindows("Daily 00:00-24:00", out _x));
        Check("market closed: no none-found claim, it says it is not checked", Status().Contains("not checked while the market is closed") && !Status().Contains("none found"));
        Set("staleScheduleAlways", true);

        Console.WriteLine("\n--- 2. end-of-day block");
        Check("nothing blocked so far: no block", Eod().Length == 0);
        ((IDictionary)Get("strategies")).Remove(s);
        for (int i = 0; i < 40; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }      // 20 minutes with no strategy
        string e = Eod(); Console.WriteLine("      " + e.Replace("\n", "\n      "));
        Check("20 minutes of no strategy: one block, all three accounts, with the duration and the reason",
            e.Contains("Could not trade") && e.Contains("L, F1, F2: ") && e.Contains("no strategy enabled") && Regex.IsMatch(e, @"(19|20)m \d+s of "));
        s = Strat("Bot", "L", "Realtime");
        for (int i = 0; i < 10; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        double before = ((IDictionary)Get("blockedTime"))["L"] is object ? (double)((IDictionary)Get("blockedTime"))["L"].GetType().GetField("BlockedSeconds").GetValue(((IDictionary)Get("blockedTime"))["L"]) : 0;
        for (int i = 0; i < 10; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        double after = (double)((IDictionary)Get("blockedTime"))["L"].GetType().GetField("BlockedSeconds").GetValue(((IDictionary)Get("blockedTime"))["L"]);
        Check("while it can trade, blocked time does not grow", before == after);

        conn.Status = ConnectionStatus.Disconnected;
        for (int i = 0; i < 10; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        conn.Status = ConnectionStatus.Connected;
        e = Eod();
        Check("a connection outage adds its reason to the same block", e.Contains("order connection down") && e.Contains("no strategy enabled"));
        Check("the footer says time with NinjaTrader or the PC off is not counted", e.Contains("not running is not counted"));

        Set("staleScheduleAlways", false);
        Set("staleQuietWindows", NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore.ParseQuietWindows("Daily 00:00-24:00", out _x));
        double c0 = (double)((IDictionary)Get("blockedTime"))["L"].GetType().GetField("BlockedSeconds").GetValue(((IDictionary)Get("blockedTime"))["L"]);
        conn.Status = ConnectionStatus.Disconnected;
        for (int i = 0; i < 10; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        conn.Status = ConnectionStatus.Connected;
        double c1 = (double)((IDictionary)Get("blockedTime"))["L"].GetType().GetField("BlockedSeconds").GetValue(((IDictionary)Get("blockedTime"))["L"]);
        Check("market closed: an outage adds nothing to the blocked time", c0 == c1);
        Set("staleScheduleAlways", true);

        Check("the real end-of-day report carries the block", Plain((string)Call("BuildEodPreview")).Contains("Could not trade"));

        Console.WriteLine("\n--- 3. manual trader: no strategy ever, no complaint");
        var M = new Account { Name = "M", NetLiq = 50000, Connection = conn }; tracked.Clear(); tracked.Add(M);
        Set("cfgLeaderAccount", ""); Set("cfgAccountName", "M");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear(); ((IDictionary)Get("blockedTime")).Clear();
        for (int i = 0; i < 40; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        Check("no strategy ever seen: status clean, no end-of-day block", Status().Contains("none found among the checks") && Eod().Length == 0);
        conn.Status = ConnectionStatus.Disconnected;
        for (int i = 0; i < 10; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        Check("a connection outage shows in /status for them, but not in the end-of-day block", Status().Contains("M: order connection down") && Eod().Length == 0);
        conn.Status = ConnectionStatus.Connected;

        Console.WriteLine("\n--- 4. state file");
        tracked.Clear(); tracked.Add(L); tracked.Add(F1); tracked.Add(F2); Set("cfgLeaderAccount", "L"); Set("cfgAccountName", "L,F1,F2");
        Exp().Clear(); ((IDictionary)Get("blockedTime")).Clear();
        Strat("Bot", "L", "Realtime"); Call("ObserveTrading", t);
        ((IDictionary)Get("strategies")).Clear();
        for (int i = 0; i < 10; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        Call("SaveRiskState");
        string file = System.IO.File.ReadAllText(Dir + "/DirectTelegramNotifier.state");
        Check("the state file has a strat| and a blk| line for the leader", Regex.IsMatch(file, @"^strat\|L=\d+", RegexOptions.Multiline) && Regex.IsMatch(file, @"^blk\|L=.+\|\d+\|\d+\|no strategy enabled", RegexOptions.Multiline));
        Exp().Clear(); ((IDictionary)Get("blockedTime")).Clear();
        Call("LoadRiskState");
        Check("after a restart L is still expected and the blocked time is back", Expected("L") && Eod().Contains("no strategy enabled"));

        string old = Regex.Replace(file, @"^(blk\|[^=]+=)" + (string)Call("CurrentSessionKey"), "${1}2000-01-01", RegexOptions.Multiline);
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", old);
        ((IDictionary)Get("blockedTime")).Clear(); Call("LoadRiskState");
        Check("blocked time from another session is dropped", Eod().Length == 0);

        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", string.Join("\r\n", file.Split(new[] { "\r\n" }, StringSplitOptions.None).Where(l => !l.StartsWith("strat|") && !l.StartsWith("blk|")).ToArray()));
        Exp().Clear(); ((IDictionary)Get("blockedTime")).Clear(); Call("LoadRiskState");
        Check("a state file without the new lines loads as before: nobody expected, nothing blocked", !Expected("L") && Eod().Length == 0);
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "strat|L=abc\r\nblk|L=x|y\r\nstrat|F1=-4\r\n");
        Call("LoadRiskState");
        Check("garbage in the new lines is ignored", !Expected("L") && !Expected("F1") && Eod().Length == 0);

        Console.WriteLine("\n--- 5. followers carry the leader's blockers (own connections are fine)");
        var connL = new Connection { Status = ConnectionStatus.Connected, PriceStatus = ConnectionStatus.Connected, Options = new ConnectionOptions { Name = "Leader" } };
        var connF = new Connection { Status = ConnectionStatus.Connected, PriceStatus = ConnectionStatus.Connected, Options = new ConnectionOptions { Name = "Follower" } };
        Connection.Connections.Clear(); Connection.Connections.Add(connL); Connection.Connections.Add(connF);
        L.Connection = connL; F1.Connection = connF; F2.Connection = connF;
        ((IDictionary)Get("strategies")).Clear(); Strat("Bot", "L", "Realtime"); Exp().Clear(); Call("ObserveTrading", t);
        Check("all well: nothing found", Status().Contains("none found among the checks"));
        connL.Status = ConnectionStatus.Disconnected;
        line = Status();
        Check("leader's order connection down: the leader says so, the followers name the leader",
            line.Contains("L: order connection down") && line.Contains("F1, F2: leader L: order connection down"));
        connL.Status = ConnectionStatus.Connected; connL.PriceStatus = ConnectionStatus.Disconnected;
        Check("leader's data connection down reaches the followers", Status().Contains("F1, F2: leader L: data connection down"));
        connL.PriceStatus = ConnectionStatus.Connected;
        lock (Get("riskLock")) ((IDictionary)Get("accountLocks"))["L"] = Activator.CreateInstance(T.GetNestedType("AccountLock", B));
        Check("leader locked: the followers say so", Status().Contains("F1, F2: leader L: locked by the notifier") && Status().Contains("L: locked by the notifier"));
        lock (Get("riskLock")) ((IDictionary)Get("accountLocks")).Clear();

        Console.WriteLine("\n--- 6. a stale feed only counts for an account that holds or runs a strategy on it");
        Type fw = T.GetNestedType("FeedWatch", B);
        object feed = Activator.CreateInstance(fw);
        fw.GetField("Name").SetValue(feed, "MNQ DEC26"); fw.GetField("Subscribed").SetValue(feed, true); fw.GetField("Stale").SetValue(feed, true);
        ((IDictionary)Get("feeds"))["MNQ DEC26"] = feed;
        Check("nobody holds or runs anything on it: not a blocker", Status().Contains("none found among the checks"));
        L.Positions.Add(new Position { Instrument = new Instrument { FullName = "MNQ DEC26", MasterInstrument = new MasterInstrument() }, MarketPosition = MarketPosition.Long, Quantity = 1, AveragePrice = 1 });
        line = Status();
        Check("L holds it: stale for L, and the followers name the leader", line.Contains("L: market data for MNQ DEC26 is stale") && line.Contains("F1, F2: leader L: market data for MNQ DEC26 is stale"));
        L.Positions.Clear();
        ((IDictionary)Get("strategies")).Clear(); object ms = Strat("Bot", "L", "Realtime");
        SI.GetField("Instrument").SetValue(ms, new Instrument { FullName = "MNQ DEC26", MasterInstrument = new MasterInstrument() });
        Check("L runs a strategy on it: stale for L", Status().Contains("L: market data for MNQ DEC26 is stale"));
        SI.GetField("Instrument").SetValue(ms, new Instrument { FullName = "MES DEC26", MasterInstrument = new MasterInstrument() });
        Check("the strategy runs on another instrument: not a blocker", Status().Contains("none found among the checks"));
        SI.GetField("Instrument").SetValue(ms, new Instrument { FullName = "MNQ DEC26", MasterInstrument = new MasterInstrument() });
        Set("staleScheduleAlways", false);
        Set("staleQuietWindows", NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore.ParseQuietWindows("Daily 00:00-24:00", out _x));
        Check("market closed: the stale feed is not reported as a blocker", !Status().Contains("is stale"));
        Set("staleScheduleAlways", true);
        ((IDictionary)Get("blockedTime")).Clear();
        for (int i = 0; i < 4; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        Check("market open: the stale feed adds blocked time", ((IDictionary)Get("blockedTime")).Contains("L"));
        ((IDictionary)Get("blockedTime")).Clear();
        Set("staleScheduleAlways", false);
        for (int i = 0; i < 4; i++) { t = t.AddSeconds(30); Call("ObserveTrading", t); }
        Check("market closed: a stale feed adds no blocked time", !((IDictionary)Get("blockedTime")).Contains("L"));
        Set("staleScheduleAlways", true);
        ((IDictionary)Get("feeds")).Clear(); ((IDictionary)Get("blockedTime")).Clear();

        Console.WriteLine("\n--- 7. blocked time grows by at most 60 s per tick");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear();
        Strat("Bot", "L", "Realtime"); Call("ObserveTrading", t);
        ((IDictionary)Get("strategies")).Clear();
        Call("ObserveTrading", t.AddSeconds(30));
        Call("ObserveTrading", t.AddHours(10));
        object lb = ((IDictionary)Get("blockedTime"))["L"];
        double bs = (double)lb.GetType().GetField("BlockedSeconds").GetValue(lb);
        Check("a 10 hour gap between two ticks adds at most one 60 s step (" + bs + " s)", bs > 0 && bs <= 90.001);
        t = t.AddHours(10);

        Console.WriteLine("\n--- 8. the end-of-day report resets the blocked time, and a failing block never loses the report");
        Check("there is blocked time before the report", ((IDictionary)Get("blockedTime")).Count > 0);
        Set("eodTimeZone", TimeZoneInfo.Utc); Set("eodTimeOfDay", new TimeSpan(21, 0, 0)); Set("lastEodReportDate", DateTime.MinValue);
        Call("CheckEndOfDay", new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc));
        Check("before the report time: nothing is reset", ((IDictionary)Get("blockedTime")).Count > 0);
        Call("CheckEndOfDay", new DateTime(2026, 11, 25, 21, 5, 0, DateTimeKind.Utc));
        Check("the weekday report empties the blocked time", ((IDictionary)Get("blockedTime")).Count == 0);
        ((IDictionary)Get("blockedTime"))["X"] = null;
        Check("BuildBlockedEodBlock returns an empty string on an error", (string)Call("BuildBlockedEodBlock") == "");
        Check("the /eod report is still produced", Plain((string)Call("BuildEodPreview")).Contains("END OF DAY"));
        ((IDictionary)Get("blockedTime")).Clear();
        ((IDictionary)Get("feeds"))["bad"] = null;
        ((IDictionary)Get("strategies")).Clear(); object bs2 = Strat("Bot", "L", "Realtime");
        SI.GetField("Instrument").SetValue(bs2, new Instrument { FullName = "MNQ DEC26", MasterInstrument = new MasterInstrument() });
        Check("BuildTradeLine returns an empty string on an error", (string)Call("BuildTradeLine") == "");
        ((IDictionary)Get("feeds")).Clear();

        Console.WriteLine("\n--- 9. state file: garbage lines do not stop the valid lines after them");
        string sess = (string)Call("CurrentSessionKey");
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state",
            "strat|L=abc\r\nblk|L=x|y\r\nstrat|F1=-4\r\nblk|\r\nstrat|\r\ngarbage\r\n=novalue\r\nblk|F2=" + sess + "|a|b|c\r\n"
            + "hw|L=51234\r\nlock|F1=" + sess + "|" + DateTime.UtcNow.Ticks + "|test lock\r\n");
        ((IDictionary)Get("riskStates")).Clear(); lock (Get("riskLock")) ((IDictionary)Get("accountLocks")).Clear(); Exp().Clear();
        Call("LoadRiskState");
        var rsd = (IDictionary)Get("riskStates");
        Check("hw| after the garbage is loaded", rsd.Contains("L") && (double)rsd["L"].GetType().GetField("HighWater").GetValue(rsd["L"]) == 51234);
        Check("lock| after the garbage is loaded", ((IDictionary)Get("accountLocks")).Contains("F1"));
        Check("none of the garbage became state", !Expected("L") && !Expected("F1") && Eod().Length == 0);

        Console.WriteLine("\n--- 10. blocked time keeps the session it was recorded in");
        ((IDictionary)Get("blockedTime")).Clear(); ((IDictionary)Get("accountLocks")).Clear();
        Type bt = Type.GetType("NinjaTrader.NinjaScript.AddOns.DtnBlockedTime, AddOn");
        object b1 = Activator.CreateInstance(bt); object b2 = Activator.CreateInstance(bt);
        bt.GetField("BlockedSeconds").SetValue(b1, 600.0); bt.GetField("TradingSeconds").SetValue(b1, 900.0); bt.GetField("Session").SetValue(b1, sess);
        bt.GetField("BlockedSeconds").SetValue(b2, 600.0); bt.GetField("TradingSeconds").SetValue(b2, 900.0); bt.GetField("Session").SetValue(b2, "2000-01-01");
        ((List<string>)bt.GetField("Reasons").GetValue(b1)).Add("order connection down"); ((List<string>)bt.GetField("Reasons").GetValue(b2)).Add("order connection down");
        ((IDictionary)Get("blockedTime"))["L"] = b1; ((IDictionary)Get("blockedTime"))["F1"] = b2;
        Call("SaveRiskState"); ((IDictionary)Get("blockedTime")).Clear(); Call("LoadRiskState");
        Check("an entry from this session comes back, one from an older session does not", ((IDictionary)Get("blockedTime")).Contains("L") && !((IDictionary)Get("blockedTime")).Contains("F1"));

        Console.WriteLine("\n--- 11. /status Strategies block agrees with the blocker check (display only)");
        ((IDictionary)Get("strategies")).Clear();
        Strat("RunBot", "L", "Realtime"); Strat("LoadBot", "L", "Historical"); Strat("OffBot", "L", "Terminated");
        string blk = Plain((string)Call("BuildStrategiesBlock"));
        Check("a Realtime strategy is listed with its state, no label", blk.Contains("RunBot · L · Realtime") && !blk.Contains("RunBot · L · Realtime (not live)"));
        Check("a Historical strategy shows its state and is labelled not live", blk.Contains("LoadBot · L · Historical (not live)"));
        Check("a Terminated strategy is counted as disabled, not listed", blk.Contains("1 disabled") && !blk.Contains("OffBot"));
        Check("it is not reported as 'none running' while one is Realtime", !blk.Contains("none running"));
        ((IDictionary)Get("strategies")).Clear(); Strat("LoadBot", "L", "Historical");
        blk = Plain((string)Call("BuildStrategiesBlock"));
        Check("only a not-live strategy: 'none running', same as the blocker line", blk.Contains("none running") && blk.Contains("(not live)"));
        Check("/diag counts the same way", Plain((string)Call("BuildDiagReport")).Contains("strategies running 0"));
        ((IDictionary)Get("strategies")).Clear(); Strat("RunBot", "L", "Realtime");
        Check("/diag counts a Realtime one", Plain((string)Call("BuildDiagReport")).Contains("strategies running 1"));
        // the alerts are unchanged: a strategy going Realtime -> Historical is silent, going to Terminated is STRATEGY DISABLED
        Drain();
        ((IDictionary)Get("strategies")).Clear(); Set("strategyBaselined", false);
        var altSt = new NinjaTrader.NinjaScript.StrategyBase { Name = "AlertBot", State = NinjaTrader.NinjaScript.State.Realtime };
        L.Strategies.Add(altSt);
        Call("CheckStrategies"); Drain();
        altSt.State = NinjaTrader.NinjaScript.State.Historical; Call("CheckStrategies");
        Check("alerts: Realtime to Historical sends nothing", Drain().Count == 0);
        altSt.State = NinjaTrader.NinjaScript.State.Terminated; Call("CheckStrategies");
        Check("alerts: Historical to Terminated still sends STRATEGY DISABLED", Drain().Any(m => m.Contains("STRATEGY DISABLED")));
        L.Strategies.Clear();

        Console.WriteLine("\n--- 12. NinjaTrader does not say which connection an account uses: the reason says so");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear();
        tracked.Clear(); var X = new Account { Name = "X", NetLiq = 50000 }; tracked.Add(X);            // no Connection: the check falls back to "any data connection"
        Set("cfgLeaderAccount", ""); Set("cfgAccountName", "X");
        Connection.Connections.Clear();
        var connA = new Connection { Status = ConnectionStatus.Connected, PriceStatus = ConnectionStatus.Disconnected, Options = new ConnectionOptions { Name = "Spare" } };
        Connection.Connections.Add(connA);
        line = Status();
        Check("the fallback reason is labelled, not a bare 'data connection down'", line.Contains("a data connection is down (NinjaTrader does not say which one this account uses)") && !line.Contains("X: data connection down"));
        X.Connection = connA;
        Check("an account whose own connection is known keeps the plain wording", Status().Contains("X: data connection down"));

        Console.WriteLine("\n" + passes + " passed, " + fails + " failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
    static string _x;
}
