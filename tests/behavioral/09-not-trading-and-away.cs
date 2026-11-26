using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Reflection; using System.Text.RegularExpressions;
using NinjaTrader.Cbi;
// NOT TRADING reminders and /away, and how they sit next to the existing alerts.
class H9
{
    static readonly string Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtn-h9");
    static object O; static Type T, SI;
    static BindingFlags B = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(string name, params object[] args)
    {
        foreach (var m in T.GetMethods(B)) if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(m.IsStatic ? null : O, args);
        throw new Exception("no method " + name);
    }
    static void Set(string f, object v) { var fi = T.GetField(f, B); if (fi == null) throw new Exception("no field " + f); fi.SetValue(O, v); }
    static object Get(string f) { var fi = T.GetField(f, B); if (fi == null) throw new Exception("no field " + f); return fi.GetValue(O); }
    static string Plain(string s) { return Regex.Replace(s, "<[^>]+>", "").Replace("&amp;", "&"); }
    class Msg { public string Text; public bool Silent, Priority; public bool Loud { get { return Priority && !Silent; } } }
    static List<Msg> Drain()
    {
        var res = new List<Msg>();
        while (true)
        {
            object m = Call("PeekNextMessage"); if (m == null) break; Call("RemoveMessage", m);
            var mt = m.GetType();
            res.Add(new Msg { Text = Plain((string)mt.GetField("Text", B).GetValue(m)), Silent = (bool)mt.GetField("Silent", B).GetValue(m), Priority = (bool)mt.GetField("Priority", B).GetValue(m) });
        }
        return res;
    }
    static bool Any(List<Msg> l, string needle) { return l.Any(x => x.Text.Contains(needle)); }
    static int fails = 0, passes = 0;
    static void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (ok) passes++; else fails++; }
    static object Strat(string name, string acct, string state)
    {
        object info = Activator.CreateInstance(SI);
        SI.GetField("Ref").SetValue(info, info); SI.GetField("Name").SetValue(info, name); SI.GetField("AccountName").SetValue(info, acct); SI.GetField("State").SetValue(info, state);
        ((IDictionary)Get("strategies"))[info] = info;
        return info;
    }
    static void Setting(string key, string acct, string v) { lock (Get("riskLock")) ((IDictionary)Get("accountSettings"))[key.ToLowerInvariant() + "|" + acct] = v; }
    static DateTime t;
    static object Away(string a) { Sync(); return Call("HandleAwayCommand", a); }
    static object Back() { Sync(); return Call("HandleBackCommand"); }
    static object Say(string m) { Sync(); return Call(m); }
    static void Save() { Sync(); Call("SaveRiskState"); }
    static void Load() { Sync(); Call("LoadRiskState"); }
    static void Sync() { Set("tradeClockSkew", t - DateTime.UtcNow); }          // the commands read the same simulated clock as the ticks
    // one tick every 30 s; returns the messages that came out of it
    static List<Msg> Tick() { t = t.AddSeconds(30); Call("ObserveTrading", t); return Drain(); }
    // runs for `seconds`, returns (elapsed seconds since the start, message) pairs
    static List<KeyValuePair<int, Msg>> Run(int seconds)
    {
        var res = new List<KeyValuePair<int, Msg>>(); DateTime s0 = t;
        for (int i = 0; i < seconds / 30; i++) foreach (var m in Tick()) res.Add(new KeyValuePair<int, Msg>((int)(t - s0).TotalSeconds, m));
        return res;
    }
    // the reminders themselves (AWAY ENDED and "reminders stopped" also contain the words NOT TRADING)
    static int Nag(List<KeyValuePair<int, Msg>> l) { return l.Count(x => x.Value.Text.Contains("Nothing can place a trade here")); }
    static int Count(List<KeyValuePair<int, Msg>> l, string needle) { return l.Count(x => x.Value.Text.Contains(needle)); }
    static List<Msg> Msgs(List<KeyValuePair<int, Msg>> l) { return l.Select(x => x.Value).ToList(); }
    static IDictionary Exp() { return (IDictionary)Get("expectations"); }
    static object NewExpectation(double idle) { object e = Activator.CreateInstance(Type.GetType("NinjaTrader.NinjaScript.AddOns.DtnExpectation, AddOn")); e.GetType().GetMethod("Restore").Invoke(e, new object[] { idle }); return e; }
    static double Limit { get { return (double)Type.GetType("NinjaTrader.NinjaScript.AddOns.DtnExpectation, AddOn").GetField("IdleLimitSeconds").GetValue(null); } }
    static Instrument mnq = new Instrument { FullName = "MNQ DEC26", MasterInstrument = new MasterInstrument { PointValue = 2.0, TickSize = 0.25 } };
    static Connection conn; static Account L, F1, F2;
    static Type FS;
    static string _x;
    static object Flap() { object f = Activator.CreateInstance(FS); FS.GetField("Since").SetValue(f, DateTime.UtcNow.AddHours(-1)); FS.GetField("LastDrop").SetValue(f, DateTime.UtcNow.AddSeconds(-60)); FS.GetField("LastReminder").SetValue(f, DateTime.UtcNow.AddHours(-3)); FS.GetField("Drops").SetValue(f, 7); return f; }
    static string EndText(DateTime untilUtc)
    {
        TimeZoneInfo ny = null;
        try { ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); } catch (Exception) { }
        if (ny == null) { try { ny = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); } catch (Exception) { } }
        if (ny == null) return "Sun 07:00";                                                   // no such zone on this machine: nothing to check
        return (string)Call("AwayEndText", untilUtc, ny);
    }

    // a fresh add-on instance, as after a NinjaTrader restart
    static void Setup()
    {
        O = Activator.CreateInstance(T, true);
        Set("cfgAccountName", "L,F1,F2"); Set("cfgLeaderAccount", "L"); Set("authorizedUserId", 42L);
        Set("staleScheduleAlways", true); Set("cfgSilentRoutine", true);
        Set("cfgEnableStrategyWatch", true); Set("strategyBaselined", true);
        Set("started", true); Set("startedUtc", DateTime.UtcNow.AddMinutes(-30));
        Set("eodTimeZone", TimeZoneInfo.Utc); Set("eodTimeOfDay", new TimeSpan(21, 0, 0));
        Set("dailyLossAlerts", Call("ParseDollarList", "50,75,90")); Set("riskBuffers", Call("ParseDollarList", "1500,750,300"));
        var tracked = (IList)Get("trackedAccounts"); tracked.Add(L); tracked.Add(F1); tracked.Add(F2);
    }

    static void Main()
    {
        if (System.IO.Directory.Exists(Dir)) System.IO.Directory.Delete(Dir, true);
        System.IO.Directory.CreateDirectory(Dir);
        NinjaTrader.Core.Globals.UserDataDir = Dir;
        T = Type.GetType("NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifier, AddOn");
        SI = T.GetNestedType("StrategyInfo", BindingFlags.NonPublic | BindingFlags.Public);
        FS = T.GetNestedType("FlapState", B);
        Connection.Connections.Clear();
        conn = new Connection { Status = ConnectionStatus.Connected, PriceStatus = ConnectionStatus.Connected, Options = new ConnectionOptions { Name = "Orders+Data" } };
        Connection.Connections.Add(conn);
        L = new Account { Name = "L", NetLiq = 50000, Connection = conn }; F1 = new Account { Name = "F1", NetLiq = 50000, Connection = conn }; F2 = new Account { Name = "F2", NetLiq = 50000, Connection = conn };
        Setup();
        t = DateTime.UtcNow;
        List<KeyValuePair<int, Msg>> r; List<Msg> d;

        Console.WriteLine("--- 1. the schedule: 5 minutes, then 30, then hourly; three accounts, one message");
        object bot = Strat("Bot", "L", "Realtime"); Tick();
        Check("the leader is expected to trade", Exp().Contains("L"));
        ((IDictionary)Get("strategies")).Remove(bot);
        r = Run(9420);
        var nt = r.Where(x => x.Value.Text.Contains("Nothing can place a trade here")).ToList();
        Console.WriteLine("      " + string.Join(", ", nt.Select(x => x.Key + "s " + x.Value.Text.Split('\n')[0]).ToArray()));
        Check("first reminder at 5 minutes", nt.Count > 0 && nt[0].Key >= 300 && nt[0].Key <= 330 && nt[0].Value.Text.StartsWith("⚠️ NOT TRADING"));
        Check("it is ONE message for the three accounts, loud, naming the reason", nt.Count > 0 && nt[0].Value.Text.Contains("all accounts: no strategy enabled") && nt[0].Value.Loud);
        Check("second reminder 30 minutes after the first, as STILL NOT TRADING", nt.Count > 1 && Math.Abs((nt[1].Key - nt[0].Key) - 1800) <= 30 && nt[1].Value.Text.Contains("STILL NOT TRADING"));
        Check("then hourly", nt.Count > 3 && Math.Abs((nt[2].Key - nt[1].Key) - 3600) <= 30 && Math.Abs((nt[3].Key - nt[2].Key) - 3600) <= 30);
        Check("nothing else was sent (no per-account copies)", r.Count == nt.Count && nt.All(x => Count(new List<KeyValuePair<int, Msg>> { x }, "F1:") == 0));
        Check("the message says how long, and that Replikanto is not visible", nt[0].Value.Text.Contains("for 5m") && nt[0].Value.Text.Contains("on/off state is not visible"));

        Console.WriteLine("\n--- 2. it clears: one quiet TRADING POSSIBLE AGAIN");
        Strat("Bot", "L", "Realtime");
        r = Run(180);
        Check("one TRADING POSSIBLE AGAIN, quiet, with how long it lasted", Count(r, "TRADING POSSIBLE AGAIN") == 1 && r.First(x => x.Value.Text.Contains("TRADING POSSIBLE AGAIN")).Value.Silent && r.First(x => x.Value.Text.Contains("TRADING POSSIBLE AGAIN")).Value.Text.Contains("blocked for"));
        Check("...and nothing after it", Count(Run(1200), "NOT TRADING") == 0);

        Console.WriteLine("\n--- 3. a short blip is silent");
        ((IDictionary)Get("strategies")).Clear(); r = Run(180); Strat("Bot", "L", "Realtime"); r.AddRange(Run(600));
        Check("no strategy for 3 minutes, then back: no NOT TRADING and no POSSIBLE AGAIN", r.Count == 0);
        conn.Status = ConnectionStatus.Disconnected; r = Run(120); conn.Status = ConnectionStatus.Connected; r.AddRange(Run(600));
        Check("a connection down for 2 minutes: nothing", r.Count == 0);

        Console.WriteLine("\n--- 4. a lock alone starts no reminders");
        lock (Get("riskLock")) ((IDictionary)Get("accountLocks"))["L"] = Activator.CreateInstance(T.GetNestedType("AccountLock", B));
        r = Run(900);
        Check("leader locked, strategy running: 15 minutes, nothing", r.Count == 0);
        ((IDictionary)Get("strategies")).Clear();
        r = Run(900);
        Check("leader locked and its strategy switched off (as the lock message asks): still nothing", r.Count == 0);
        conn.Status = ConnectionStatus.Disconnected; r = Run(420);
        Check("...but a down connection on a locked account is still reported, without the lock in it", Nag(r) == 1 && !Any(Msgs(r), "locked by the notifier"));
        conn.Status = ConnectionStatus.Connected; Run(180);
        lock (Get("riskLock")) ((IDictionary)Get("accountLocks")).Clear();
        Strat("Bot", "L", "Realtime"); Run(120);

        Console.WriteLine("\n--- 5. /away silences only the NOT TRADING reminders");
        string reply = Plain((string)Away("8h"));
        Check("/away 8h answers with the end time and what stays on", reply.Contains("AWAY for 8h") && reply.Contains("until") && reply.Contains("RISK UNKNOWN") && reply.Contains("/back"));
        Check("a clock time is refused and nothing is set", Plain((string)Away("18:00")).Contains("Clock times are not accepted") && ((DateTime)Get("awayUntilUtc")) > t.AddHours(7));
        ((IDictionary)Get("strategies")).Clear();
        Setting("RiskFloor", "L", "48000"); L.Positions.Clear(); L.Positions.Add(new Position { Instrument = mnq, MarketPosition = MarketPosition.Long, Quantity = 1, AveragePrice = 29400 });
        conn.PriceStatus = ConnectionStatus.Disconnected;                                     // data down while a position is held: RISK UNKNOWN
        DateTime rt = DateTime.UtcNow;
        Call("RiskTick", rt); d = Drain();
        Check("while away, RISK UNKNOWN still arrives, loud", Any(d, "RISK UNKNOWN") && d.First(m => m.Text.Contains("RISK UNKNOWN")).Loud);
        r = Run(1200);
        Check("while away, 20 minutes of no trading: no NOT TRADING", Nag(r) == 0);
        Call("RiskTick", rt.AddSeconds(61)); d = Drain();
        Check("...and the RISK UNKNOWN reminder still comes", Any(d, "RISK UNKNOWN"));
        conn.PriceStatus = ConnectionStatus.Connected; L.Positions.Clear();
        Setting("DailyLossLimit", "F1", "600"); Call("RiskTick", DateTime.UtcNow); ((IDictionary)Get("riskStates"))["F1"].GetType().GetField("Session").SetValue(((IDictionary)Get("riskStates"))["F1"], "2000-01-01"); Call("RiskTick", DateTime.UtcNow); Drain();
        F1.NetLiq = 49430;                                                                    // 95% of the daily limit
        Call("RiskTick", DateTime.UtcNow.AddSeconds(5)); Call("RiskTick", DateTime.UtcNow.AddSeconds(20)); d = Drain();
        Check("while away, the daily loss alert still arrives", Any(d, "DAILY LOSS"));
        F1.NetLiq = 50000;
        Back(); Strat("Bot", "L", "Realtime"); Run(180); Drain();

        Console.WriteLine("\n--- 6. a flapping connection is STILL UNSTABLE's to report; a steady outage is NOT TRADING's");
        ((IDictionary)Get("connFlapping"))["Orders+Data"] = Flap();
        conn.Status = ConnectionStatus.Disconnected;
        r = Run(1200);
        Check("the only reason is a connection that is flapping: 20 minutes, no NOT TRADING", Nag(r) == 0);
        Call("CheckFlappingConnections", DateTime.UtcNow); d = Drain();
        Check("STILL UNSTABLE is sent exactly as before", Any(d, "STILL UNSTABLE") && Any(d, "Orders+Data") && Any(d, "7 drops"));
        ((IDictionary)Get("connFlapping")).Clear();
        r = Run(420);
        Check("the connection stops flapping but stays down: NOT TRADING comes 5 minutes later", Nag(r) == 1);
        conn.Status = ConnectionStatus.Connected; Run(180); Drain();
        Strat("Bot", "L", "Realtime"); Run(60);
        ((IDictionary)Get("connFlapping"))["Orders+Data"] = Flap();
        conn.Status = ConnectionStatus.Disconnected; ((IDictionary)Get("strategies")).Clear();
        r = Run(420);
        Check("flapping connection AND no strategy enabled: two reasons, so NOT TRADING is sent", Nag(r) == 1 && Any(Msgs(r), "no strategy enabled"));
        ((IDictionary)Get("connFlapping"))["Orders+Data"] = Flap();
        Call("CheckFlappingConnections", DateTime.UtcNow); d = Drain();
        Check("while NOT TRADING reminders run, STILL UNSTABLE is still sent (it is not folded in)", Any(d, "STILL UNSTABLE"));
        Away("8h");
        ((IDictionary)Get("connFlapping"))["Orders+Data"] = Flap();
        Call("CheckFlappingConnections", DateTime.UtcNow); d = Drain();
        Check("and /away does not silence it", Any(d, "STILL UNSTABLE"));
        ((IDictionary)Get("connFlapping")).Clear();
        conn.Status = ConnectionStatus.Connected; Back(); Strat("Bot", "L", "Realtime"); Run(300); Drain();

        Console.WriteLine("\n--- 7. /back, expiry, and the clock restarting");
        Run(60); ((IDictionary)Get("strategies")).Clear();
        Away("90m");
        r = Run(7200);                                                                         // 2 h simulated: the 90 minutes run out inside
        Check("away expires by itself with one quiet AWAY ENDED", Count(r, "AWAY ENDED") == 1 && r.First(x => x.Value.Text.Contains("AWAY ENDED")).Value.Silent);
        var ended = r.First(x => x.Value.Text.Contains("AWAY ENDED")).Key;
        var firstNag = r.FirstOrDefault(x => x.Value.Text.Contains("Nothing can place a trade here"));
        Check("the first reminder after it comes about 5 minutes later, not at once (" + ended + "s, " + firstNag.Key + "s)", firstNag.Value != null && firstNag.Key - ended >= 270 && firstNag.Key - ended <= 360);
        Check("/back when not away says so", Plain((string)Back()).Contains("not away"));
        Away("2h"); DateTime until1 = (DateTime)Get("awayUntilUtc"); Away("4h"); DateTime until2 = (DateTime)Get("awayUntilUtc");
        Check("/away again replaces the end time", (until2 - until1).TotalMinutes > 110 && (until2 - until1).TotalMinutes < 130);
        Check("/back ends it", Plain((string)Back()).Contains("BACK") && (DateTime)Get("awayUntilUtc") == DateTime.MinValue);
        Check("/status shows the away line only while away", !Plain((string)Say("AwayLine")).Contains("Away") && (Away("1h") != null) && Plain((string)Say("AwayLine")).Contains("Away until") && Plain((string)Say("BuildStatusReport")).Contains("NOT TRADING reminders are silenced"));
        Back();
        Check("the end time is worked out from UTC: 8 h across the New York autumn clock change ends at 07:00, not 08:00",
            EndText(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)) == "Sun 07:00");

        Console.WriteLine("\n--- 8. nothing is sent while the market is closed; the clock restarts at the reopen");
        ((IDictionary)Get("strategies")).Clear(); Strat("Bot", "L", "Realtime"); Run(120); ((IDictionary)Get("strategies")).Clear();
        r = Run(420);
        Check("an episode is running: a reminder went out", Nag(r) == 1);
        Set("staleScheduleAlways", false);
        Set("staleQuietWindows", NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore.ParseQuietWindows("Daily 00:00-24:00", out _x));
        r = Run(7200);
        Check("market closed for 2 hours, still blocked: not a single message", r.Count == 0);
        Set("staleScheduleAlways", true);
        r = Run(420);
        Check("reopened and still blocked: the next reminder comes 5 minutes after the reopen", Nag(r) == 1 && r.First(x => x.Value.Text.Contains("Nothing can place a trade here")).Key >= 270 && r.First(x => x.Value.Text.Contains("Nothing can place a trade here")).Key <= 330);
        Strat("Bot", "L", "Realtime"); Run(180); Drain();
        ((IDictionary)Get("strategies")).Clear(); Run(420); Drain();
        Strat("Bot", "L", "Realtime");
        Set("staleScheduleAlways", false);
        r = Run(1800);
        Check("cleared while the market was closed: still silent until it reopens", r.Count == 0);
        Set("staleScheduleAlways", true);
        r = Run(180);
        Check("...then one TRADING POSSIBLE AGAIN", Count(r, "TRADING POSSIBLE AGAIN") == 1);

        Console.WriteLine("\n--- 9. an account stops being expected in the middle of an outage");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear();
        Exp()["L"] = NewExpectation(Limit - 420);                                             // 7 minutes of expectation left
        r = Run(1200);
        Check("a reminder went out while it was still expected", Nag(r) >= 1);
        Check("the only reason was 'no strategy enabled': when the expectation runs out, one quiet 'reminders stopped' naming it", Count(r, "NOT TRADING reminders stopped") == 1 && r.First(x => x.Value.Text.Contains("reminders stopped")).Value.Silent && Any(Msgs(r), "no strategy enabled"));
        Check("...and never TRADING POSSIBLE AGAIN while nothing is enabled", Count(r, "TRADING POSSIBLE AGAIN") == 0);
        Exp().Clear();
        conn.Status = ConnectionStatus.Disconnected; Strat("Bot", "L", "Realtime");
        Exp()["L"] = NewExpectation(0); Run(60); ((IDictionary)Get("strategies")).Clear(); Exp()["L"] = NewExpectation(Limit - 420);
        r = Run(1200);
        Check("same with a connection down: reminders stopped, naming the connection", Count(r, "NOT TRADING reminders stopped") == 1 && Any(Msgs(r), "order connection down") && Count(r, "TRADING POSSIBLE AGAIN") == 0);
        conn.Status = ConnectionStatus.Connected; Exp().Clear();

        Console.WriteLine("\n--- 9b. two independent accounts: one recovers, one runs out of expectation");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear(); Set("cfgLeaderAccount", "");
        Strat("A", "L", "Realtime"); Strat("B", "F1", "Realtime"); Run(60);
        ((IDictionary)Get("strategies")).Clear();
        r = Run(360);
        Check("both have no strategy: one NOT TRADING for the two accounts", Nag(r) == 1 && Any(Msgs(r), "L, F1: no strategy enabled"));
        Strat("A", "L", "Realtime"); Exp()["F1"] = NewExpectation(Limit + 60);
        r = Run(180);
        var stopped = r.FirstOrDefault(x => x.Value.Text.Contains("reminders stopped"));
        Check("L recovered but F1 ran out of expectation: the episode ends as 'reminders stopped' naming F1, not TRADING POSSIBLE AGAIN",
            stopped.Value != null && stopped.Value.Silent && stopped.Value.Text.Contains("F1: no strategy enabled") && Count(r, "TRADING POSSIBLE AGAIN") == 0);
        Set("cfgLeaderAccount", "L");

        Console.WriteLine("\n--- 10. strategies that were never scanned: no reminders");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear(); Strat("Bot", "L", "Realtime"); Run(60); ((IDictionary)Get("strategies")).Clear();
        Set("cfgEnableStrategyWatch", false);
        conn.Status = ConnectionStatus.Disconnected;
        r = Run(1800);
        Check("watch off with an expectation left over from before: 30 minutes, nothing (not even the connection)", r.Count == 0);
        Set("cfgEnableStrategyWatch", true); Set("strategyBaselined", false);
        r = Run(1200);
        Check("watch on but not scanned yet: nothing", r.Count == 0);
        Set("strategyBaselined", true); conn.Status = ConnectionStatus.Connected; Exp().Clear();

        Console.WriteLine("\n--- 11. an exception inside the new code stays inside it");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear();
        ((IDictionary)Get("strategies"))[new object()] = null;                                // makes the strategy read throw
        bool threw = false;
        try { Call("ObserveTrading", t.AddSeconds(30)); } catch (Exception) { threw = true; }
        Check("ObserveTrading swallows and logs the error", !threw);
        ((IDictionary)Get("strategies")).Clear();
        ((IDictionary)Get("blockedTime"))["X"] = null;                                        // makes saving the trade state throw
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "");
        object rsL = Call("RiskStateFor", "L");
        rsL.GetType().GetField("HighWater").SetValue(rsL, 51500.0);
        Save();
        string file = System.IO.File.ReadAllText(Dir + "/DirectTelegramNotifier.state");
        Check("a failure writing the trade lines does not lose the rest of the state file (the high-water mark is still saved)", file.Contains("hw|L=51500"));
        ((IDictionary)Get("blockedTime")).Clear();

        Console.WriteLine("\n--- 12. a restart: a fresh instance and the state file");
        ((IDictionary)Get("strategies")).Clear(); Exp().Clear(); Strat("Bot", "L", "Realtime"); Tick();
        Away("3h"); Save();
        string saved = System.IO.File.ReadAllText(Dir + "/DirectTelegramNotifier.state");
        Check("the state file has strat| and away= lines", saved.Contains("strat|L=") && saved.Contains("away="));
        DateTime wasUntil = (DateTime)Get("awayUntilUtc");
        Setup(); Load();
        Check("after a restart /away is still on, with the same end time", (DateTime)Get("awayUntilUtc") == wasUntil);
        Check("...and L is still expected to trade", Exp().Contains("L"));
        r = Run(600);
        Check("away after the restart: no reminder", r.Count == 0);
        Back(); Save(); Setup(); Load();
        Check("after /back and another restart it is not away", (DateTime)Get("awayUntilUtc") == DateTime.MinValue);
        string sessKey = (string)Call("CurrentSessionKey");
        Sync();
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "away=" + t.AddHours(-1).Ticks + "\r\nhw|L=51234\r\n");
        Setup(); Load();
        Check("an away= that already ended is ignored, and the line after it still loads", (DateTime)Get("awayUntilUtc") == DateTime.MinValue && ((IDictionary)Get("riskStates")).Contains("L"));
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "away=" + t.AddDays(30).Ticks + "\r\n");
        Setup(); Load();
        Check("an away= 30 days ahead is not something this program wrote: ignored", (DateTime)Get("awayUntilUtc") == DateTime.MinValue);
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "away=" + t.AddDays(7).AddHours(1).Ticks + "\r\n");
        Setup(); Load();
        Check("an away= a little beyond 7 days (PC clock set back) is kept and cut to 7 days", (DateTime)Get("awayUntilUtc") > t.AddDays(6.9) && (DateTime)Get("awayUntilUtc") <= t.AddDays(7).AddSeconds(5));
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "away=abc\r\naway=\r\naway=-5\r\nhw|F1=50500\r\n");
        Setup(); Load();
        Check("garbage in away= is ignored, and the line after it still loads", (DateTime)Get("awayUntilUtc") == DateTime.MinValue && ((IDictionary)Get("riskStates")).Contains("F1"));

        Console.WriteLine("\n--- 13. strategies still loading after a restart are not 'no strategy enabled'");
        Setup();
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "strat|L=60\r\n");
        Load();
        Strat("Bot", "L", "Historical");
        r = Run(14 * 60);
        Check("14 minutes with the strategy Historical: nothing, and /status finds nothing wrong", r.Count == 0 && Plain((string)Say("BuildTradeLine")).Contains("none found among the checks"));
        r = Run(30 * 60);
        Check("stuck in Historical for 44 minutes: after the 15 minute grace it counts as not running, and NOT TRADING follows 5 minutes later (at about minute 20)",
            Nag(r) == 1 && r.First(x => x.Value.Text.Contains("Nothing can place a trade here")).Key >= 300 && r.First(x => x.Value.Text.Contains("Nothing can place a trade here")).Key <= 420);
        Setup();
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "strat|L=60\r\n");
        Load();
        object bot2 = Strat("Bot", "L", "Historical");
        r = Run(10 * 60);
        bot2.GetType().GetField("State").SetValue(bot2, "Realtime");
        r.AddRange(Run(40 * 60));
        Check("Historical for 10 minutes, then Realtime: never a reminder", r.Count == 0);
        bot2.GetType().GetField("State").SetValue(bot2, "Historical");
        r = Run(10 * 60);
        Check("...and the grace starts again when a strategy goes back to loading", r.Count == 0);

        Setup();
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "strat|L=60\r\n");
        Load();
        Exp()["L"] = NewExpectation(Limit - 100);                                              // 100 seconds of expectation left
        Strat("Bot", "L", "Historical");
        r = Run(14 * 60);
        Check("a strategy that is starting keeps the expectation alive: nothing while it loads", r.Count == 0);
        r = Run(30 * 60);
        Check("...so when it never goes live the reminder still comes (the expectation did not run out while it loaded)", Nag(r) == 1);

        Console.WriteLine("\n--- 13b. a failure while loading one of the new state lines does not stop the lines after it");
        Setup();
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.state", "strat|L=300\r\nblk|L=x|1|1|a\r\nhw|L=51234\r\n");
        Set("tradeLock", null);                                                                // lock(null) throws inside the new loader
        Load();
        Set("tradeLock", new object());
        Check("hw| after the failing lines is still loaded", ((IDictionary)Get("riskStates")).Contains("L") && (double)((IDictionary)Get("riskStates"))["L"].GetType().GetField("HighWater").GetValue(((IDictionary)Get("riskStates"))["L"]) == 51234);

        Console.WriteLine("\n--- 14. /help");
        string help = Plain((string)Call("BuildHelpText"));
        Check("/help has /away and /back", help.Contains("/away") && help.Contains("/back"));

        Console.WriteLine("\n" + passes + " passed, " + fails + " failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
