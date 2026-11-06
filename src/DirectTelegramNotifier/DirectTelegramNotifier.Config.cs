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

// DirectTelegramNotifier: The config file (template, loading, validation) and the end-of-day schedule.
// One part of a partial class: see DirectTelegramNotifier.cs for the overview.

namespace NinjaTrader.NinjaScript.AddOns
{
    public partial class DirectTelegramNotifier
    {
        #region Configuration file

        private static string ConfigPath()
        {
            string dir;
            try { dir = NinjaTrader.Core.Globals.UserDataDir; }
            catch (Exception) { dir = null; }
            if (string.IsNullOrEmpty(dir))
                dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
            return System.IO.Path.Combine(dir, ConfigFileName);
        }

        // The config file written on first run: the common settings first, then every advanced setting at its default.
        private static string DefaultConfigText()
        {
            return ""
                 + "# DirectTelegramNotifier settings\r\n"
                 + "# After editing, press F5 in the NinjaScript Editor or restart NinjaTrader.\r\n"
                 + "# Anything after a # is a comment. A value the notifier cannot read is reported in Telegram at startup.\r\n"
                 + "# Common settings first; advanced ones at the bottom, at their defaults. All settings: docs/configuration.md\r\n"
                 + "\r\n"
                 + "# --- Telegram ---\r\n"
                 + "BotToken=                           # from @BotFather; blank = use the NT_TELEGRAM_BOT_TOKEN environment variable\r\n"
                 + "AuthorizedChatID=                   # your numeric Telegram user ID (ask @userinfobot); only you can send commands\r\n"
                 + "FlattenConfirm=false                # true = /flatten and /lock ask you to confirm first\r\n"
                 + "\r\n"
                 + "# --- Accounts ---\r\n"
                 + "AccountName=Sim101                  # accounts to watch: one, a comma-separated list, or * for all\r\n"
                 + "LeaderAccount=                      # copier users only: the account your copier copies from; blank = no copier\r\n"
                 + "\r\n"
                 + "# --- End of day ---\r\n"
                 + "EodReportTime=170000                # when your trading day ends (HHMMSS): daily report and new session\r\n"
                 + "EodTimeZoneId=Eastern Standard Time # Windows time zone for EodReportTime\r\n"
                 + "\r\n"
                 + "# --- Backup alerts (optional; treat both URLs like passwords) ---\r\n"
                 + "HeartbeatUrl=                       # dead-man's switch ping, e.g. Healthchecks.io: tells you when alerts stop\r\n"
                 + "BackupAlertUrl=                     # second channel for critical alerts, e.g. https://ntfy.sh/your-long-random-topic\r\n"
                 + "\r\n"
                 + "# --- Drawdown floor ---\r\n"
                 + "DrawdownMode=manual                 # off, manual, static, eod (end-of-day trailing) or intraday (intraday trailing)\r\n"
                 + "RiskFloor=                          # manual: the floor in net liquidation dollars; blank = set it later with /floor\r\n"
                 + "StartingBalance=                    # static, eod, intraday: account size\r\n"
                 + "DrawdownAmount=                     # static, eod, intraday: maximum drawdown in dollars\r\n"
                 + "FloorLock=                          # a trailing floor stops rising here; blank = never\r\n"
                 + "RiskBufferAlerts=1500,750,300       # alert when net liquidation gets this close to the floor (dollars)\r\n"
                 + "# Any risk setting can differ per account, e.g. DrawdownAmount:Acct002=2500\r\n"
                 + "\r\n"
                 + "# --- Daily loss and prop-firm rules (blank = off) ---\r\n"
                 + "DailyLossLimit=                     # dollars (1000) or % of the day's starting value (2%)\r\n"
                 + "ConsistencyPercent=                 # best-day limit as % of total profit, e.g. 30; needs StartingBalance\r\n"
                 + "InactivityDays=                     # days your firm allows without a trade\r\n"
                 + "\r\n"
                 + "# --- Automatic flatten (off by default) ---\r\n"
                 + "# On a breach the account is flattened and kept flat until /unlock or the session ends.\r\n"
                 + "# Strategies and copiers are NOT stopped. Test on Sim first, then check that /selftest says ARMED.\r\n"
                 + "RiskAutoFlatten=false               # when the drawdown floor is reached\r\n"
                 + "DailyLossAutoFlatten=false          # when the daily loss limit is reached\r\n"
                 + "ConsistencyAutoFlatten=false        # when today's profit reaches the consistency cap\r\n"
                 + "\r\n"
                 + "\r\n"
                 + "# ======================================================================\r\n"
                 + "# ADVANCED SETTINGS, at their defaults. Change one only if you need to.\r\n"
                 + "# ======================================================================\r\n"
                 + "\r\n"
                 + "# --- Alerts on or off ---\r\n"
                 + "EnableWatchdog=true                 # connection, NinjaTrader freezing, start/stop and restart alerts\r\n"
                 + "EnableExecutions=true               # fill alerts\r\n"
                 + "EnableRemoteCommands=true           # accept commands from Telegram\r\n"
                 + "EnableOrderRejectAlerts=true        # rejected-order alerts\r\n"
                 + "EnableStrategyWatch=true            # strategy enabled / disabled / removed alerts\r\n"
                 + "SilentRoutineAlerts=false           # true = routine messages arrive without a sound; problems still ring\r\n"
                 + "FillGroupMs=2000                    # fills within this many ms become one message (500-10000)\r\n"
                 + "\r\n"
                 + "# --- Orders and positions (0 = off) ---\r\n"
                 + "RunawayMaxOrders=30                 # alert when this many new orders appear...\r\n"
                 + "RunawayWindowSec=60                 # ...within this many seconds\r\n"
                 + "ProtectiveCheckSeconds=0            # position with no working stop this long (only if your strategy places real stops)\r\n"
                 + "OrphanOrderSeconds=20               # flat account with exit orders still working this long\r\n"
                 + "OrphanOrderNames=Stop,Target        # order names that count as exit orders\r\n"
                 + "StuckOrderSeconds=30                # order stuck in one of the states below this long\r\n"
                 + "StuckOrderStates=Submitted,ChangePending,ChangeSubmitted,CancelPending,CancelSubmitted  # states that should pass quickly\r\n"
                 + "MaxPositionContracts=0              # alert above this many contracts per account\r\n"
                 + "\r\n"
                 + "# --- Trade copier (only with LeaderAccount) ---\r\n"
                 + "FollowerRatio=1                     # follower size as a multiple of the leader's\r\n"
                 + "MismatchSeconds=20                  # alert when a follower stays out of step this long (0 = off)\r\n"
                 + "\r\n"
                 + "# --- Stale market data ---\r\n"
                 + "StaleDataSeconds=120                # alert when a connected feed sends no ticks this long (0 = off)\r\n"
                 + "StaleDataSchedule=cme               # when quiet is normal: cme, always or custom\r\n"
                 + "StaleDataQuietWindows=              # custom only, e.g. Weekdays 16:00-24:00; Weekends 00:00-24:00\r\n"
                 + "StaleDataTimeZone=Eastern Standard Time  # time zone for StaleDataQuietWindows\r\n"
                 + "StaleDataInstruments=               # extra instruments to watch, also for rolls; traded and held ones are automatic\r\n"
                 + "\r\n"
                 + "# --- Contract rolls ---\r\n"
                 + "RollWarnDays=10                     # warn this many days before a futures contract in use expires (0 = off)\r\n"
                 + "\r\n"
                 + "# --- Risk details ---\r\n"
                 + "HighWaterBasis=balance              # eod trailing measured on balance (no open P&L) or netliq\r\n"
                 + "RiskFlattenBuffer=150               # auto-flatten this many dollars above the floor\r\n"
                 + "RiskConfirmSeconds=2                # a breach must last this long before auto-flatten acts (1-30)\r\n"
                 + "FloorJumpConfirm=1000               # /floor asks you to confirm a change bigger than this\r\n"
                 + "UnknownFlattenSeconds=0             # flatten if risk data is missing this long while in a trade (0 = alert only)\r\n"
                 + "LockGroup=false                     # true = locking one account locks them all\r\n"
                 + "DailyLossAlerts=50,75,90            # % of DailyLossLimit that alert\r\n"
                 + "ConsistencyAlerts=75,90             # % of today's allowed profit that alert\r\n"
                 + "ConsistencyProfitTarget=            # leave blank unless your firm measures against the profit target\r\n"
                 + "InactivityAlerts=3,2,1              # days left that alert\r\n"
                 + "\r\n"
                 + "# --- /flatten ---\r\n"
                 + "FlattenHoldSeconds=30               # after /flatten, close anything that reopens this soon (0 = off)\r\n"
                 + "UseGlobalFlattenEverything=false    # true = /flatten closes every connected account, not just these\r\n"
                 + "\r\n"
                 + "# --- Backup channel and heartbeat ---\r\n"
                 + "BackupAlertDetail=minimal           # minimal (no amounts) or full (only for a service you run yourself)\r\n"
                 + "BackupAlertFormat=text              # text, or json for Slack and Discord webhooks\r\n"
                 + "HeartbeatIntervalSec=60             # how often to ping HeartbeatUrl\r\n"
                 + "RestartGapThresholdSec=45           # report downtime longer than this at startup\r\n"
                 + "\r\n"
                 + "# --- NinjaTrader health (0 = off) ---\r\n"
                 + "CpuAlertPercent=90                  # alert when NinjaTrader's CPU stays this high...\r\n"
                 + "CpuAlertSeconds=180                 # ...for this many seconds\r\n"
                 + "MemoryAlertMB=0                     # alert when NinjaTrader uses more memory than this\r\n"
                 + "DiskAlertMB=2000                    # alert when free disk space falls below this\r\n"
                 + "\r\n"
                 + "# --- Logs and reports ---\r\n"
                 + "EnableAlertLog=true                 # copy every alert to DirectTelegramNotifier.log\r\n"
                 + "EnableFillCsv=true                  # write every fill to DirectTelegramNotifier-fills.csv\r\n"
                 + "EnableRiskLog=true                  # record risk readings for replay (up to 50 MB)\r\n"
                 + "EstimatedCommissionPerSide=0        # commission per contract per side, used when NinjaTrader reports none\r\n";
        }

        // Values that were present but could not be read. They are reported in Telegram at startup with the other warnings.
        private List<string> parseWarnings = new List<string>();

        private bool LoadConfiguration()
        {
            string path = ConfigPath();
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            parseWarnings = new List<string>();

            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultConfigText(), new UTF8Encoding(false));
                    WriteOutput("Created config template at " + path + ". Fill in BotToken and AuthorizedChatID, then press F5 in the NinjaScript Editor or restart NinjaTrader.");
                    return false;
                }

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    kv[line.Substring(0, eq).Trim()] = Dtn.StripInlineComment(line.Substring(eq + 1));
                }
            }
            catch (Exception ex)
            {
                WriteError("Reading config " + path, ex);
                return false;
            }

            cfgBotToken                   = GetString(kv, "BotToken", string.Empty);
            cfgAuthorizedChatId           = GetString(kv, "AuthorizedChatID", string.Empty);
            cfgAccountName                = GetString(kv, "AccountName", "Sim101");
            cfgEnableWatchdog             = GetBool(kv, "EnableWatchdog", true);
            cfgEnableExecutions           = GetBool(kv, "EnableExecutions", true);
            cfgEnableRemoteCommands       = GetBool(kv, "EnableRemoteCommands", true);
            cfgUseGlobalFlattenEverything = GetBool(kv, "UseGlobalFlattenEverything", false);
            cfgEodReportTime              = GetInt(kv, "EodReportTime", 170000);
            cfgEodTimeZoneId              = GetString(kv, "EodTimeZoneId", "Eastern Standard Time");
            cfgEstimatedCommissionPerSide = Math.Max(0.0, GetDouble(kv, "EstimatedCommissionPerSide", 0.0));
            cfgHeartbeatUrl               = GetString(kv, "HeartbeatUrl", string.Empty);
            cfgHeartbeatIntervalSec       = Math.Max(15, GetInt(kv, "HeartbeatIntervalSec", 60));
            cfgRestartGapThresholdSec     = Math.Max(10, GetInt(kv, "RestartGapThresholdSec", 45));
            cfgEnableStrategyWatch        = GetBool(kv, "EnableStrategyWatch", true);
            cfgEnableAlertLog             = GetBool(kv, "EnableAlertLog", true);
            int staleSeconds              = GetInt(kv, "StaleDataSeconds", 120);
            cfgStaleDataSeconds           = staleSeconds <= 0 ? 0 : Math.Max(30, staleSeconds);
            cfgStaleDataSchedule          = GetString(kv, "StaleDataSchedule", "cme");
            cfgStaleDataInstruments       = GetString(kv, "StaleDataInstruments", string.Empty);
            cfgLeaderAccount              = GetString(kv, "LeaderAccount", string.Empty);
            cfgFollowerRatio              = Math.Max(0.0, GetDouble(kv, "FollowerRatio", 1.0));
            cfgMismatchSeconds            = Math.Max(0, GetInt(kv, "MismatchSeconds", 20));
            cfgFlattenConfirm             = GetBool(kv, "FlattenConfirm", false);
            cfgSilentRoutine              = GetBool(kv, "SilentRoutineAlerts", false);
            cfgEnableFillCsv              = GetBool(kv, "EnableFillCsv", true);
            cfgProtectiveCheckSec         = Math.Max(0, GetInt(kv, "ProtectiveCheckSeconds", 0));
            cfgOrphanOrderSec             = Math.Max(0, GetInt(kv, "OrphanOrderSeconds", 20));
            cfgOrphanOrderNames           = GetString(kv, "OrphanOrderNames", "Stop,Target");
            cfgStuckOrderSec              = Math.Max(0, GetInt(kv, "StuckOrderSeconds", 30));
            cfgMaxPositionContracts       = Math.Max(0, GetInt(kv, "MaxPositionContracts", 0));
            cfgRiskFloor                  = GetOptionalDouble(kv, "RiskFloor");
            riskBuffers                   = ParseDollarList(GetString(kv, "RiskBufferAlerts", "1500,750,300"));
            cfgRiskAutoFlatten            = GetBool(kv, "RiskAutoFlatten", false);
            cfgRiskFlattenBuffer          = Math.Max(0.0, GetDouble(kv, "RiskFlattenBuffer", 150.0));
            cfgCpuAlertPercent            = Math.Max(0, Math.Min(100, GetInt(kv, "CpuAlertPercent", 90)));
            cfgCpuAlertSeconds            = Math.Max(30, GetInt(kv, "CpuAlertSeconds", 180));
            cfgMemoryAlertMb              = Math.Max(0, GetInt(kv, "MemoryAlertMB", 0));
            cfgDiskAlertMb                = Math.Max(0, GetInt(kv, "DiskAlertMB", 2000));
            cfgFillGroupMs                = Math.Max(500, Math.Min(10000, GetInt(kv, "FillGroupMs", 2000)));
            cfgRollWarnDays               = Math.Max(0, Math.Min(90, GetInt(kv, "RollWarnDays", 10)));
            cfgStaleDataQuietWindows      = GetString(kv, "StaleDataQuietWindows", string.Empty);
            cfgStaleDataTimeZone          = GetString(kv, "StaleDataTimeZone", "Eastern Standard Time");
            cfgDrawdownMode               = GetString(kv, "DrawdownMode", "manual");
            cfgStartingBalance            = GetOptionalDouble(kv, "StartingBalance");
            cfgDrawdownAmount             = GetOptionalDouble(kv, "DrawdownAmount");
            cfgFloorLock                  = GetOptionalDouble(kv, "FloorLock");
            cfgDailyLossLimit             = GetString(kv, "DailyLossLimit", string.Empty);
            dailyLossAlerts               = ParseDollarList(GetString(kv, "DailyLossAlerts", "50,75,90"));
            cfgDailyLossAutoFlatten       = GetBool(kv, "DailyLossAutoFlatten", false);
            cfgRiskConfirmSec             = Math.Max(1, Math.Min(30, GetInt(kv, "RiskConfirmSeconds", 2)));
            cfgFlattenHoldSec             = Math.Max(0, Math.Min(600, GetInt(kv, "FlattenHoldSeconds", 30)));
            cfgFloorJumpConfirm           = Math.Max(0.0, GetDouble(kv, "FloorJumpConfirm", 1000.0));
            cfgHighWaterBasis             = GetString(kv, "HighWaterBasis", "balance");
            cfgUnknownFlattenSec          = Math.Max(0, Math.Min(3600, GetInt(kv, "UnknownFlattenSeconds", 0)));
            cfgLockGroup                  = GetBool(kv, "LockGroup", false);
            cfgConsistencyPercent         = GetOptionalDouble(kv, "ConsistencyPercent");
            cfgConsistencyTarget          = GetOptionalDouble(kv, "ConsistencyProfitTarget");
            consistencyAlerts             = ParseDollarList(GetString(kv, "ConsistencyAlerts", "75,90"));
            cfgConsistencyAutoFlatten     = GetBool(kv, "ConsistencyAutoFlatten", false);
            cfgEnableRiskLog              = GetBool(kv, "EnableRiskLog", true);
            cfgBackupUrl                  = GetString(kv, "BackupAlertUrl", string.Empty).Trim();
            cfgBackupFormat               = GetString(kv, "BackupAlertFormat", "text").Trim().ToLowerInvariant();
            cfgBackupDetail               = GetString(kv, "BackupAlertDetail", "minimal").Trim().ToLowerInvariant();
            cfgInactivityDays             = GetOptionalDouble(kv, "InactivityDays");
            inactivityAlerts              = ParseDollarList(GetString(kv, "InactivityAlerts", "3,2,1"));

            List<string> warnings = new List<string>();
            foreach (string unknown in Dtn.FindUnknownKeys(kv.Keys))
                warnings.Add("Unrecognized setting " + unknown);
            ApplyStaleSchedule(warnings);
            stuckStates = ParseStuckStates(GetString(kv, "StuckOrderStates", DefaultStuckStates), warnings);

            // Per-account overrides:  RiskFloor:Account=, DrawdownMode:Account=, StartingBalance:Account= ...
            lock (riskLock)
            {
                accountSettings.Clear();
                foreach (KeyValuePair<string, string> pair in kv)
                {
                    int colon = pair.Key.IndexOf(':');
                    if (colon <= 0)
                        continue;
                    string baseKey = pair.Key.Substring(0, colon).Trim();
                    string account = pair.Key.Substring(colon + 1).Trim();
                    if (account.Length > 0 && Dtn.IsPerAccountKey(baseKey))
                        accountSettings[baseKey.ToLowerInvariant() + "|" + account] = (pair.Value ?? string.Empty).Trim();
                }
            }
            if (Dtn.NormalizeMode(cfgDrawdownMode) == null)
                warnings.Add("DrawdownMode '" + cfgDrawdownMode + "' is not off, manual, static, eod or intraday; using manual");
            lock (riskLock)
            {
                foreach (KeyValuePair<string, string> pair in accountSettings)
                {
                    if (pair.Key.StartsWith("drawdownmode|", StringComparison.Ordinal) && Dtn.NormalizeMode(pair.Value) == null)
                        warnings.Add("DrawdownMode:" + pair.Key.Substring("drawdownmode|".Length) + " '" + pair.Value + "' is not recognized; using manual");
                }
            }
            if (Dtn.NormalizeBasis(cfgHighWaterBasis) == null)
                warnings.Add("HighWaterBasis '" + cfgHighWaterBasis + "' is not balance or netliq; using balance");
            lock (riskLock)
            {
                foreach (KeyValuePair<string, string> pair in accountSettings)
                {
                    if (pair.Key.StartsWith("highwaterbasis|", StringComparison.Ordinal) && Dtn.NormalizeBasis(pair.Value) == null)
                        warnings.Add("HighWaterBasis:" + pair.Key.Substring("highwaterbasis|".Length) + " '" + pair.Value + "' is not balance or netliq; using balance");
                    if (pair.Key.StartsWith("consistencypercent|", StringComparison.Ordinal))
                    {
                        double cp = ParseOptionalDouble(pair.Value);
                        if (!double.IsNaN(cp) && (cp < 0 || cp >= 100))
                            warnings.Add("ConsistencyPercent:" + pair.Key.Substring("consistencypercent|".Length) + " must be between 0 and 100 (0 = off)");
                    }
                }
            }
            if (!double.IsNaN(cfgConsistencyPercent) && (cfgConsistencyPercent < 0 || cfgConsistencyPercent >= 100))
                warnings.Add("ConsistencyPercent must be between 0 and 100 (0 = off)");
            if (cfgBackupUrl.Length > 0 && !(cfgBackupUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || cfgBackupUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("BackupAlertUrl must start with https:// (or http://); the backup channel is off");
                cfgBackupUrl = string.Empty;
            }
            else if (cfgBackupUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                warnings.Add("BackupAlertUrl uses http://: the alert text and any secret in the URL travel unencrypted");
            if (cfgBackupFormat != "text" && cfgBackupFormat != "json")
            {
                warnings.Add("BackupAlertFormat '" + cfgBackupFormat + "' is not text or json; using text");
                cfgBackupFormat = "text";
            }
            if (cfgBackupDetail != "minimal" && cfgBackupDetail != "full")
            {
                warnings.Add("BackupAlertDetail '" + cfgBackupDetail + "' is not minimal or full; using minimal");
                cfgBackupDetail = "minimal";
            }
            if (!double.IsNaN(cfgInactivityDays) && cfgInactivityDays < 0)
                warnings.Add("InactivityDays must be 0 or more (0 = off)");
            string leaderSetting = (cfgLeaderAccount ?? string.Empty).Trim();
            string accountFilter = (cfgAccountName ?? string.Empty).Trim();
            if (leaderSetting.Length > 0 && accountFilter != "*")
            {
                bool listed = false;
                foreach (string part in accountFilter.Split(','))
                {
                    if (part.Trim().Equals(leaderSetting, StringComparison.OrdinalIgnoreCase))
                        listed = true;
                }
                if (!listed)
                    warnings.Add("LeaderAccount '" + leaderSetting + "' is not in AccountName, so the copy check cannot run");
            }
            if (!Dtn.IsValidAmountOrPercent(cfgDailyLossLimit))
                warnings.Add("DailyLossLimit '" + cfgDailyLossLimit + "' is not an amount (1000) or a percentage (2%); there is no daily loss limit until you fix it");
            lock (riskLock)
            {
                foreach (KeyValuePair<string, string> pair in accountSettings)
                {
                    int bar = pair.Key.IndexOf('|');
                    string baseKey = pair.Key.Substring(0, bar);
                    string label = CanonicalKey(baseKey) + ":" + pair.Key.Substring(bar + 1);
                    if (baseKey == "dailylosslimit")
                    {
                        if (!Dtn.IsValidAmountOrPercent(pair.Value))
                            warnings.Add(label + " '" + pair.Value + "' is not an amount (1000) or a percentage (2%); this account has no daily loss limit until you fix it");
                    }
                    else if (baseKey != "drawdownmode" && baseKey != "highwaterbasis"
                             && pair.Value.Length > 0 && double.IsNaN(ParseOptionalDouble(pair.Value)))
                    {
                        warnings.Add(label + " '" + pair.Value + "' is not a number; it is treated as blank for this account");
                    }
                }
            }
            warnings.InsertRange(0, parseWarnings);
            configWarnings = warnings;
            foreach (string w in warnings)
                WriteOutput("Config warning: " + w);
            cfgEnableOrderRejectAlerts    = GetBool(kv, "EnableOrderRejectAlerts", true);
            cfgRunawayMaxOrders           = Math.Max(0, GetInt(kv, "RunawayMaxOrders", 30));
            cfgRunawayWindowSec           = Math.Max(10, GetInt(kv, "RunawayWindowSec", 60));

            string token = cfgBotToken;
            if (token.Length == 0)
            {
                try { token = (Environment.GetEnvironmentVariable(TokenEnvironmentVar) ?? string.Empty).Trim(); }
                catch (Exception) { token = string.Empty; }
            }

            long id = 0;
            string problem = null;
            if (token.Length == 0 || token.IndexOf(':') < 0)
                problem = "BotToken is empty or malformed (set it in the config file or the " + TokenEnvironmentVar + " environment variable).";
            else if (!long.TryParse(cfgAuthorizedChatId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0)
                problem = "AuthorizedChatID must be your numeric Telegram user ID (a positive integer).";

            if (problem != null)
            {
                WriteOutput("CONFIG ERROR: " + problem + " Config file: " + path + ". Notifier is inactive until you fix it and press F5 or restart.");
                return false;
            }

            resolvedToken    = token;
            chatIdText       = cfgAuthorizedChatId;
            authorizedUserId = id;
            return true;
        }

        private static string GetString(Dictionary<string, string> kv, string key, string fallback)
        {
            string v;
            return kv.TryGetValue(key, out v) && v != null ? v : fallback;
        }

        private bool GetBool(Dictionary<string, string> kv, string key, bool fallback)
        {
            string v;
            if (!kv.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                return fallback;
            bool b;
            if (bool.TryParse(v, out b))
                return b;
            if (v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            WriteOutput("Config: could not parse " + key + "='" + v + "'; using " + fallback + ".");
            if (key.EndsWith("AutoFlatten", StringComparison.OrdinalIgnoreCase) && !fallback)
                parseWarnings.Add(key + " '" + v + "' is not true or false, so automatic flatten for this rule is OFF");
            else
                parseWarnings.Add(key + " '" + v + "' is not true or false; using " + (fallback ? "true" : "false"));
            return fallback;
        }

        private int GetInt(Dictionary<string, string> kv, string key, int fallback)
        {
            string v;
            int i;
            if (!kv.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                return fallback;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                return i;
            WriteOutput("Config: could not parse " + key + "='" + v + "'; using " + fallback.ToString(CultureInfo.InvariantCulture) + ".");
            parseWarnings.Add(key + " '" + v + "' is not a number; using " + fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        private double GetDouble(Dictionary<string, string> kv, string key, double fallback)
        {
            string v;
            double d;
            if (!kv.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                return fallback;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;
            WriteOutput("Config: could not parse " + key + "='" + v + "'; using " + fallback.ToString(CultureInfo.InvariantCulture) + ".");
            parseWarnings.Add(key + " '" + v + "' is not a number; using " + fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        // Blank means "not set" (NaN). Anything else that is not a number is reported and also treated as not set.
        private double GetOptionalDouble(Dictionary<string, string> kv, string key)
        {
            string v = GetString(kv, key, string.Empty);
            double d = ParseOptionalDouble(v);
            if (double.IsNaN(d) && !string.IsNullOrWhiteSpace(v))
            {
                WriteOutput("Config: could not parse " + key + "='" + v + "'; treating it as blank.");
                parseWarnings.Add(key + " '" + v + "' is not a number; it is treated as blank (off)");
            }
            return d;
        }

        // "drawdownamount" -> "DrawdownAmount", for messages about per-account keys.
        private static string CanonicalKey(string lower)
        {
            foreach (string k in Dtn.PerAccountKeys)
            {
                if (k.Equals(lower, StringComparison.OrdinalIgnoreCase))
                    return k;
            }
            return lower;
        }

        #endregion

        #region Schedule helpers

        private void InitializeSchedule()
        {
            eodTimeZone  = ResolveTimeZone(cfgEodTimeZoneId);
            eodTimeOfDay = ParseHhmmss(cfgEodReportTime);

            DateTime nowTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eodTimeZone);
            DateTime boundaryTz = nowTz.Date + eodTimeOfDay;

            if (nowTz.TimeOfDay < eodTimeOfDay)
                boundaryTz = boundaryTz.AddDays(-1);
            else
                lastEodReportDate = nowTz.Date;                  // started after today's report time: don't fire immediately

            // Execution timestamps are in NinjaTrader's configured time zone, which may differ from the PC clock.
            sessionStartNt = ConvertTzToLocal(boundaryTz) + NtClockOffset();
        }

        private TimeZoneInfo ResolveTimeZone(string id)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(id))
                    return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            }
            catch (Exception)
            {
                WriteOutput("Unknown time zone id '" + id + "'. Falling back to the PC's local time zone.");
            }
            return TimeZoneInfo.Local;
        }

        private TimeSpan ParseHhmmss(int value)
        {
            int v = Math.Max(0, Math.Min(235959, value));
            int h = v / 10000;
            int m = (v / 100) % 100;
            int s = v % 100;
            if (h > 23 || m > 59 || s > 59 || v != value)
            {
                WriteOutput("Invalid EodReportTime " + value.ToString(CultureInfo.InvariantCulture) + " (expected HHMMSS). Using 170000.");
                return new TimeSpan(17, 0, 0);
            }
            return new TimeSpan(h, m, s);
        }

        private DateTime ConvertTzToLocal(DateTime tzTime)
        {
            try
            {
                return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(tzTime, DateTimeKind.Unspecified), eodTimeZone, TimeZoneInfo.Local);
            }
            catch (Exception)
            {
                return DateTime.Now.Date;
            }
        }

        private static DateTime NtNow()
        {
            try { return NinjaTrader.Core.Globals.Now; }
            catch (Exception) { return DateTime.Now; }
        }

        private static TimeSpan NtClockOffset()
        {
            double minutes = (NtNow() - DateTime.Now).TotalMinutes;
            return TimeSpan.FromMinutes(Math.Round(minutes / 15.0) * 15.0);
        }

        #endregion
    }
}
