using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Reflection; using System.Text.RegularExpressions;
using NinjaTrader.Cbi;
// Warnings raised by the same rule for several accounts go out as one message; critical alerts are never held.
class H7
{
    static readonly string Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtn-h7");
    static object O; static Type T;
    static BindingFlags B = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(string name, params object[] args)
    {
        foreach (var m in T.GetMethods(B)) if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(m.IsStatic ? null : O, args);
        throw new Exception("no method " + name);
    }
    static void Set(string f, object v) { var fi = T.GetField(f, B); if (fi == null) throw new Exception("no field " + f); fi.SetValue(O, v); }
    static object Get(string f) { var fi = T.GetField(f, B); if (fi == null) throw new Exception("no field " + f); return fi.GetValue(O); }
    static object F(object o, string f) { return o.GetType().GetField(f, B).GetValue(o); }
    static void SetF(object o, string f, object v) { o.GetType().GetField(f, B).SetValue(o, v); }
    static string Plain(string s) { return Regex.Replace(s, "<[^>]+>", "").Replace("&amp;", "&"); }
    class Msg { public string Text; public bool Priority; }
    static List<Msg> Drain()
    {
        var res = new List<Msg>();
        while (true)
        {
            object m = Call("PeekNextMessage"); if (m == null) break; Call("RemoveMessage", m);
            res.Add(new Msg { Text = Plain((string)F(m, "Text")), Priority = (bool)F(m, "Priority") });
        }
        return res;
    }
    static int Count(string text, string needle) { int n = 0, i = 0; while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; } return n; }
    static int fails = 0, passes = 0;
    static void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (ok) passes++; else fails++; }
    static object Rs(string acct) { return Call("RiskStateFor", acct); }
    static void Setting(string key, string acct, string v) { lock (Get("riskLock")) ((IDictionary)Get("accountSettings"))[key.ToLowerInvariant() + "|" + acct] = v; }
    static void Show(List<Msg> d) { foreach (var m in d) Console.WriteLine("      --- " + m.Text.Replace("\n", "\n          ")); }

    static void Main()
    {
        if (System.IO.Directory.Exists(Dir)) System.IO.Directory.Delete(Dir, true);
        System.IO.Directory.CreateDirectory(Dir);
        NinjaTrader.Core.Globals.UserDataDir = Dir;
        T = Type.GetType("NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifier, AddOn");
        O = Activator.CreateInstance(T, true);
        Check("the default wait is 10 s", (int)Get("alertGroupMs") == 10000);
        Set("cfgAccountName", "A1,A2,A3"); Set("authorizedUserId", 42L);
        Set("startedUtc", DateTime.UtcNow.AddMinutes(-30)); Set("started", true);
        Set("eodTimeZone", TimeZoneInfo.Utc); Set("eodTimeOfDay", new TimeSpan(21, 0, 0));
        Set("dailyLossAlerts", Call("ParseDollarList", "50,75,90"));
        Set("inactivityAlerts", Call("ParseDollarList", "2,1"));
        Connection.Connections.Clear();
        Connection.Connections.Add(new Connection { Status = ConnectionStatus.Connected, PriceStatus = ConnectionStatus.Connected, Options = new ConnectionOptions { Name = "Orders+Data" } });
        var A1 = new Account { Name = "A1", NetLiq = 50000 }; var A2 = new Account { Name = "A2", NetLiq = 50000 }; var A3 = new Account { Name = "A3", NetLiq = 50000 };
        var tracked = (IList)Get("trackedAccounts"); tracked.Add(A1); tracked.Add(A2); tracked.Add(A3);
        foreach (string a in new[] { "A1", "A2", "A3" }) Setting("DailyLossLimit", a, "600");
        List<Msg> d;

        DateTime t = DateTime.UtcNow;
        Call("RiskTick", t);
        foreach (string a in new[] { "A1", "A2", "A3" }) SetF(Rs(a), "Session", "2000-01-01");
        Call("RiskTick", t); Drain();
        Check("each account's day starts at 50,000", (double)F(Rs("A3"), "SessionStart") == 50000.0);

        Console.WriteLine("\n--- 1. three copied accounts cross 50% a few seconds apart: one message");
        A1.NetLiq = 49696.00; Call("RiskTick", t.AddSeconds(1)); d = Drain();
        Check("A1 at 50%: held, not sent yet", d.Count == 0);
        A2.NetLiq = 49694.00; Call("RiskTick", t.AddSeconds(4)); d = Drain();
        Check("A2 at 50% 3 s later: still held", d.Count == 0);
        A3.NetLiq = 49698.75; Call("RiskTick", t.AddSeconds(8)); d = Drain();
        Check("A3 at 50% 7 s later: still held", d.Count == 0);
        Call("RiskTick", t.AddSeconds(10.5)); Check("9.5 s after the first: still held", Drain().Count == 0);
        Call("RiskTick", t.AddSeconds(11)); d = Drain(); Show(d);
        Check("10 s after the first: ONE message for all three", d.Count == 1 && d[0].Text.Contains("DAILY LOSS 50% · 3 accounts"));
        Check("it names each account with its own numbers", d.Count == 1 && d[0].Text.Contains("A1 · down 304.00") && d[0].Text.Contains("A2 · down 306.00") && d[0].Text.Contains("A3 · down 301.25"));
        Check("it is still a loud alert (priority lane)", d.Count == 1 && d[0].Priority);
        Call("RiskTick", t.AddSeconds(30)); Check("and it is not sent again", Drain().Count == 0);

        Console.WriteLine("\n--- 2. one account alone: the message looks exactly as before");
        A1.NetLiq = 49740.00; Call("RiskTick", t.AddSeconds(31)); Check("A1 back under: held", Drain().Count == 0);
        Call("RiskTick", t.AddSeconds(41)); d = Drain(); Show(d);
        Check("sent alone, in the old single-account form", d.Count == 1 && d[0].Text.StartsWith("✅ DAILY LOSS BACK UNDER ALERT LEVELS\nA1 · down 260.00 today") && !d[0].Text.Contains("accounts"));
        Check("a recovery stays a routine message", d.Count == 1 && !d[0].Priority);

        Console.WriteLine("\n--- 3. critical alerts are never held, and waiting warnings go out before them");
        A2.NetLiq = 49540.00; Call("RiskTick", t.AddSeconds(50)); Check("A2 at 75%: held", Drain().Count == 0);
        A3.NetLiq = 49395.00; Call("RiskTick", t.AddSeconds(52)); d = Drain(); Show(d);
        Check("A3 reaches the limit: sent in the same second", Count(string.Join("|", d.Select(m => m.Text).ToArray()), "DAILY LOSS LIMIT REACHED") == 1);
        Check("A2's waiting 75% warning went out first, so the order is right", d.Count == 2 && d[0].Text.Contains("DAILY LOSS 75%") && d[0].Text.Contains("A2") && d[1].Text.Contains("LIMIT REACHED") && d[1].Text.Contains("A3"));
        A1.NetLiq = 49455.00; Call("RiskTick", t.AddSeconds(53)); d = Drain();
        Check("90% is sent at once, not held", d.Count == 1 && d[0].Text.Contains("DAILY LOSS 90%") && d[0].Text.Contains("A1"));

        Console.WriteLine("\n--- 4. auto-flatten: LOCKED is sent at once and the flatten is not delayed");
        Set("cfgDailyLossAutoFlatten", true); Set("cfgRiskConfirmSec", 0);
        A2.Positions.Add(new Position { Instrument = new Instrument { FullName = "MNQ DEC26", MasterInstrument = new MasterInstrument { PointValue = 2.0, TickSize = 0.25 } }, MarketPosition = MarketPosition.Long, Quantity = 1, AveragePrice = 29400 });
        int fc = A2.FlattenCount;
        A2.NetLiq = 49390.00; Call("RiskTick", t.AddSeconds(60)); Call("RiskTick", t.AddSeconds(63)); d = Drain(); Show(d);
        Check("A2 over the limit: flattened and LOCKED reported without waiting", A2.FlattenCount > fc && d.Any(m => m.Text.Contains("ACCOUNT LOCKED") && m.Text.Contains("A2")));
        Set("cfgDailyLossAutoFlatten", false); A2.Positions.Clear();

        Console.WriteLine("\n--- 5. inactivity clock: one message for the copied accounts");
        Set("cfgInactivityDays", 7.0);
        DateTime n0 = DateTime.UtcNow;
        lock (Get("riskLock"))
        {
            foreach (string a in new[] { "A1", "A2", "A3" }) { ((IDictionary)Get("lastActivity"))[a] = n0.AddDays(-5.1); ((HashSet<string>)Get("activityAssumed")).Remove(a); }
            ((IDictionary)Get("inactivityLevel")).Clear();
        }
        Set("lastInactivityCheckUtc", DateTime.MinValue); Call("CheckInactivity", n0); d = Drain();
        Check("1.9 days left on all three: held for the window", d.Count == 0);
        Call("CheckInactivity", n0.AddSeconds(11)); d = Drain(); Show(d);
        Check("then ONE INACTIVITY CLOCK message for 3 accounts", d.Count == 1 && d[0].Text.Contains("INACTIVITY CLOCK · 3 accounts") && d[0].Priority);
        Check("each account's line has its days left", d.Count == 1 && d[0].Text.Contains("A1 · last trade") && d[0].Text.Contains("A3 · last trade") && Count(d[0].Text, "1.9 days left of 7 days") == 3);
        Check("the advice line is shown once, not three times", d.Count == 1 && Count(d[0].Text, "The notifier never places trades") == 1);

        Console.WriteLine("\n--- 6. shutdown sends what is still waiting");
        A1.NetLiq = 49740.00; A2.NetLiq = 49740.00; A3.NetLiq = 49740.00;
        Call("RiskTick", t.AddSeconds(70)); Drain();
        Call("SendGroupedAlerts", null, t.AddSeconds(70), true); d = Drain();
        Check("pending recoveries are flushed on request", d.Count >= 1 && d.Any(m => m.Text.Contains("BACK UNDER ALERT LEVELS")));
        Check("nothing left waiting", (int)((object)Get("alertGrouper")).GetType().GetProperty("Count").GetValue(Get("alertGrouper"), null) == 0);

        Console.WriteLine("\n" + passes + " passed, " + fails + " failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
