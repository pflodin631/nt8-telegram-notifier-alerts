using System; using System.Collections.Generic; using System.Reflection;
// Config loading: the built-in template, the example files in config/, inline comments, and values that cannot be read.
class H6 {
    static readonly string Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dtn-h6");
    static readonly BindingFlags B = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Type T = Type.GetType("NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifier, AddOn");
    static int passed, failed;
    static void Check(string name, bool ok) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + name); } }
    const string Token = "BotToken=123456:ABCdefGHIjklMNOpqrSTUvwxYZ123456789";

    // Writes the text as the config file, loads it into a fresh instance, returns the instance and its warnings.
    static object Load(string text, out List<string> warnings) {
        if (System.IO.Directory.Exists(Dir)) System.IO.Directory.Delete(Dir, true);
        System.IO.Directory.CreateDirectory(Dir);
        NinjaTrader.Core.Globals.UserDataDir = Dir;
        System.IO.File.WriteAllText(Dir + "/DirectTelegramNotifier.config", text);
        object o = Activator.CreateInstance(T, true);
        object ok = T.GetMethod("LoadConfiguration", B).Invoke(o, null);
        warnings = (List<string>)T.GetField("configWarnings", B).GetValue(o);
        foreach (var w in warnings) Console.WriteLine("    warning: " + w);
        Check("config loads", ok.ToString() == "True");
        return o;
    }
    static object Get(object o, string field) { return T.GetField(field, B).GetValue(o); }
    static bool Any(List<string> w, string a, string b) { foreach (var x in w) if (x.Contains(a) && x.Contains(b)) return true; return false; }
    static string Fill(string text) {
        text = text.Replace("\r\n", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(?m)^BotToken=.*$", Token);
        return System.Text.RegularExpressions.Regex.Replace(text, "(?m)^AuthorizedChatID=.*$", "AuthorizedChatID=42");
    }

    static void Main() {
        List<string> w;
        Console.WriteLine("--- 1. the built-in template");
        string template = (string)T.GetMethod("DefaultConfigText", B).Invoke(null, null);
        object o = Load(Fill(template), out w);
        Check("the template loads without warnings", w.Count == 0);
        Check("the template's defaults", (string)Get(o, "cfgDrawdownMode") == "manual" && (int)Get(o, "cfgFlattenHoldSec") == 30
            && ((HashSet<string>)Get(o, "stuckStates")).Count == 5);
        string[] known = (string[])Type.GetType("NinjaTrader.NinjaScript.AddOns.DirectTelegramNotifierCore, AddOn").GetField("KnownKeys").GetValue(null);
        List<string> notOnce = new List<string>();
        foreach (string k in known)
            if (System.Text.RegularExpressions.Regex.Matches(template, "(?m)^" + k + "=").Count != 1) notOnce.Add(k);
        foreach (string x in notOnce) Console.WriteLine("    not exactly once in the template: " + x);
        Check("every setting appears in the template exactly once", notOnce.Count == 0);
        // Every value the template writes must equal the built-in default: loading it changes nothing.
        object bare = Load(Token + "\nAuthorizedChatID=42\n", out w);
        List<string> differ = new List<string>();
        foreach (FieldInfo f in T.GetFields(B))
        {
            if (!f.Name.StartsWith("cfg") || f.IsStatic) continue;
            object a = f.GetValue(o), b = f.GetValue(bare);
            if (!object.Equals(a, b) && !(a is double && double.IsNaN((double)a) && double.IsNaN((double)b))) differ.Add(f.Name + " " + a + " vs " + b);
        }
        foreach (string x in differ) Console.WriteLine("    differs from default: " + x);
        Check("the template's values are the defaults", differ.Count == 0);
        string root = System.IO.Directory.GetCurrentDirectory() + "/";   // the runner scripts start from the repository root
        string example = System.IO.File.ReadAllText(root + "config/DirectTelegramNotifier.config.example");
        Check("config/DirectTelegramNotifier.config.example is the built-in template", example.Replace("\r\n", "\n") == template.Replace("\r\n", "\n"));

        Console.WriteLine("--- 2. the worked example (config/DirectTelegramNotifier.mffu-rapid-eod-50k.config.example)");
        string worked = System.IO.File.ReadAllText(root + "config/DirectTelegramNotifier.mffu-rapid-eod-50k.config.example");
        o = Load(Fill(worked), out w);
        Check("the worked example loads without warnings", w.Count == 0);
        Check("its commission line with a trailing comment reads as 0.95", Math.Abs((double)Get(o, "cfgEstimatedCommissionPerSide") - 0.95) < 1e-9);

        Console.WriteLine("--- 3. inline comments");
        string basic = Token + "\nAuthorizedChatID=42\n";
        o = Load(basic + "DailyLossAutoFlatten=true   # on for the evaluation\nDailyLossLimit=600 # dollars\nRiskFlattenBuffer=200\t# tab before the hash\n"
                 + "HeartbeatUrl=https://example.com/ping#frag\nStartingBalance= # not set yet\n", out w);
        Check("no warnings", w.Count == 0);
        Check("a switch with a trailing comment is on", (bool)Get(o, "cfgDailyLossAutoFlatten"));
        Check("an amount with a trailing comment", (string)Get(o, "cfgDailyLossLimit") == "600");
        Check("a tab before # also starts a comment", (double)Get(o, "cfgRiskFlattenBuffer") == 200.0);
        Check("a # inside a URL is kept", (string)Get(o, "cfgHeartbeatUrl") == "https://example.com/ping#frag");
        Check("a value that is only a comment is blank", double.IsNaN((double)Get(o, "cfgStartingBalance")));

        Console.WriteLine("--- 4. values that cannot be read are reported in Telegram, not only in the Output window");
        o = Load(basic + "RiskAutoFlatten=yes please\nConsistencyAutoFlatten=enabled\nEnableFillCsv=sure\nFillGroupMs=2s\nRiskFlattenBuffer=150 dollars\n"
                 + "DrawdownAmount=2k\nDailyLossLimit=six hundred\nDrawdownAmount:Acct002=lots\nDailyLossLimit:Acct002=2 pct\n", out w);
        Check("an unreadable auto-flatten switch says auto-flatten is OFF", Any(w, "RiskAutoFlatten 'yes please'", "automatic flatten for this rule is OFF"));
        Check("...for each such switch", Any(w, "ConsistencyAutoFlatten 'enabled'", "OFF"));
        Check("...and the switch really is off", !(bool)Get(o, "cfgRiskAutoFlatten"));
        Check("another unreadable switch names its fallback", Any(w, "EnableFillCsv 'sure'", "using true"));
        Check("an unreadable whole number", Any(w, "FillGroupMs '2s'", "not a number"));
        Check("an unreadable amount", Any(w, "RiskFlattenBuffer '150 dollars'", "using 150"));
        Check("an unreadable optional amount is treated as blank", Any(w, "DrawdownAmount '2k'", "blank") && double.IsNaN((double)Get(o, "cfgDrawdownAmount")));
        Check("an unreadable daily loss limit", Any(w, "DailyLossLimit 'six hundred'", "no daily loss limit"));
        Check("an unreadable per-account amount", Any(w, "DrawdownAmount:Acct002 'lots'", "not a number"));
        Check("an unreadable per-account daily loss limit", Any(w, "DailyLossLimit:Acct002 '2 pct'", "no daily loss limit"));
        o = Load(basic + "DailyLossLimit=2%\nDailyLossLimit:Acct002=$1,000\nStartingBalance=$50,000\n", out w);
        Check("percentages, $ and thousands separators are fine", w.Count == 0 && (double)Get(o, "cfgStartingBalance") == 50000.0);

        Console.WriteLine("--- 5. LeaderAccount is explicit");
        o = Load(basic + "AccountName=A,B\n", out w);
        Check("with no LeaderAccount there is no leader and no warning", w.Count == 0 && (string)Get(o, "cfgLeaderAccount") == "");
        o = Load(basic + "AccountName=A,B\nLeaderAccount=b\n", out w);
        Check("a listed leader is accepted, in any letter case", w.Count == 0);
        o = Load(basic + "AccountName=A,B\nLeaderAccount=C\n", out w);
        Check("a leader that is not in AccountName is reported", Any(w, "LeaderAccount 'C'", "not in AccountName"));
        o = Load(basic + "AccountName=*\nLeaderAccount=C\n", out w);
        Check("with AccountName=* any leader is accepted", w.Count == 0);
        Check("the template leaves LeaderAccount blank", System.Text.RegularExpressions.Regex.IsMatch(template, "(?m)^LeaderAccount=[ \\t]*(#|\\r?$)"));

        Console.WriteLine("\n" + passed + " passed, " + failed + " failed");
        Environment.Exit(failed == 0 ? 0 : 1);
    }
}
