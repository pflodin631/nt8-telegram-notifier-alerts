# Configuration

The config file is `Documents\NinjaTrader 8\DirectTelegramNotifier.config`. The first compile creates it (the same as `config/DirectTelegramNotifier.config.example`): the settings most people change come first, and every advanced setting follows at the bottom with its default.

- One `Key=value` per line. A `#` starts a comment, at the start of a line or after a value: `RiskFloor=48000  # from the firm's dashboard`.
- Keys you leave out use their defaults, so an older config file keeps working after an upgrade.
- At startup, unrecognized keys (with a "did you mean" suggestion) and values that cannot be read are sent to Telegram as **CONFIG WARNING**. An unreadable auto-flatten switch says that auto-flatten is off.
- The file is read at startup only. After editing it, press F5 in the NinjaScript Editor or restart NinjaTrader.

## Bot token

Either put it after `BotToken=`, or leave that blank and set the environment variable `NT_TELEGRAM_BOT_TOKEN` (`setx NT_TELEGRAM_BOT_TOKEN "123456:ABC..."`, then fully restart NinjaTrader). The environment variable keeps the token out of a plain-text file. If both are set, the config file wins.

## All keys

| Key | Default | Meaning |
|---|---|---|
| `BotToken` | blank | Bot token. Blank = use `NT_TELEGRAM_BOT_TOKEN` |
| `AuthorizedChatID` | none | Your numeric Telegram user ID. Alerts go here; only this user can send commands |
| `AccountName` | `Sim101` | One account, a comma-separated list, or `*`. Each is tracked on its own; the order does not matter |
| `EnableWatchdog` | `true` | Connection, UI-freeze, online/stopped and restart alerts |
| `EnableExecutions` | `true` | Fill alerts |
| `EnableRemoteCommands` | `true` | Poll Telegram for commands |
| `EnableOrderRejectAlerts` | `true` | Rejected-order alerts |
| `RunawayMaxOrders` | `30` | Alert at this many new orders within `RunawayWindowSec`. `0` = off |
| `RunawayWindowSec` | `60` | Window for the order-rate alert |
| `UseGlobalFlattenEverything` | `false` | `true` makes `/flatten` hit every connected account, not only the tracked ones |
| `EodReportTime` | `170000` | End-of-day report time, HHMMSS |
| `EodTimeZoneId` | `Eastern Standard Time` | Windows time zone id for the report and session boundary |
| `EstimatedCommissionPerSide` | `0` | Used only when NinjaTrader reports zero commission |
| `HeartbeatUrl` | blank | Optional dead-man's-switch ping URL. Treat it like a secret |
| `HeartbeatIntervalSec` | `60` | Ping interval (minimum 15) |
| `RestartGapThresholdSec` | `45` | Report downtime longer than this at startup |
| `EnableStrategyWatch` | `true` | Strategy enabled/disabled/removed alerts |
| `StaleDataSeconds` | `120` | No-tick threshold for stale-data alerts. `0` = off, minimum otherwise 30 |
| `StaleDataSchedule` | `cme` | When silence is expected: `cme` (CME futures week, US Eastern), `always` (around the clock), or `custom` |
| `StaleDataQuietWindows` | blank | For `custom`: e.g. `Weekdays 00:00-09:30; Weekdays 16:00-24:00; Weekends 00:00-24:00`. Windows can't cross midnight |
| `StaleDataTimeZone` | `Eastern Standard Time` | Windows time zone for the custom windows |
| `StaleDataInstruments` | blank | Extra instruments to watch. Held and traded instruments are watched automatically |
| `RollWarnDays` | `10` | Days before a futures contract's expiry to warn that it needs rolling. Applies to contracts in use: a running strategy, a position or working order, anything traded since the last end-of-day report, and instruments listed in `StaleDataInstruments`. Sends ROLL SOON, then ROLL NOW at 3 days or less, then CONTRACT EXPIRES TODAY, once each. `/status` lists every such contract with its expiry either way. 0 = off |
| `EnableAlertLog` | `true` | Append alerts to `DirectTelegramNotifier.log` |
| `LeaderAccount` | blank | Trade copier users only: the account the copier copies from. Blank = no copier, no account is a leader and there are no copy checks. When set, the other accounts in `AccountName` are its followers |
| `FollowerRatio` | `1` | Expected follower size as a multiple of the leader's |
| `MismatchSeconds` | `20` | Alert when followers stay out of sync this long. `0` = off |
| `ProtectiveCheckSeconds` | `0` | Alert when a position has no working stop this long. `0` = off |
| `OrphanOrderSeconds` | `20` | Alert when a flat account still has exit-type orders working this long. `0` = off |
| `OrphanOrderNames` | `Stop,Target` | Order names containing any of these count as exit-type |
| `StuckOrderSeconds` | `30` | Alert when an order stays in one of `StuckOrderStates` this long. `0` = off |
| `StuckOrderStates` | `Submitted,ChangePending,ChangeSubmitted,CancelPending,CancelSubmitted` | Order states that should pass quickly |
| `MaxPositionContracts` | `0` | Alert when an account holds more contracts than this in total. `0` = off |
| `HighWaterBasis` * | `balance` | What the end-of-day high-water mark is measured on: `balance` (net liquidation minus open profit) or `netliq`. Intraday trailing always uses net liquidation |
| `DrawdownMode` * | `manual` | `off`, `manual` (`RiskFloor` or `/floor`), `static`, `eod` or `intraday`. See [risk.md](risk.md) |
| `RiskFloor` * | blank | The floor for `manual` mode, in Net Liquidation dollars |
| `StartingBalance` * | blank | Account size. Needed for `static`; trailing modes start from it |
| `DrawdownAmount` * | blank | Maximum drawdown in dollars, for `static`, `eod` and `intraday` |
| `FloorLock` * | blank | A trailing floor stops rising at this value. Blank = never locks |
| `RiskBufferAlerts` | `1500,750,300` | Alert as Net Liquidation comes within these dollars of the floor |
| `RiskAutoFlatten` | `false` | `true` flattens **and locks** an account that stays within `RiskFlattenBuffer` of its floor for `RiskConfirmSeconds` |
| `RiskFlattenBuffer` | `150` | Dollar distance from the floor that triggers auto-flatten |
| `RiskConfirmSeconds` | `2` | How long a breach must last before auto-flatten acts (1-30) |
| `FloorJumpConfirm` | `1000` | `/floor` asks for confirmation when it moves a floor more than this. `0` = only when the floor is inside your alert levels |
| `UnknownFlattenSeconds` | `0` | Flatten and lock an account whose risk data has been unavailable this long while it holds a position. `0` = only alert |
| `LockGroup` | `false` | `true` locks every tracked account when one is locked, so a copier cannot keep re-opening trades on a locked follower |
| `DailyLossLimit` * | blank | Maximum loss per session: dollars (`1000`) or percent of the session-start value (`2%`) |
| `DailyLossAlerts` | `50,75,90` | Percentages of the limit that alert |
| `DailyLossAutoFlatten` | `false` | `true` flattens and locks an account at its daily loss limit |
| `ConsistencyPercent` * | blank | Your firm's best-day limit as a percent of total profit (for example `30`, `40`, `50`). Needs `StartingBalance`. Blank or `0` = off |
| `ConsistencyProfitTarget` * | blank | **Leave blank** unless your firm measures against the larger of total profit and the profit target: setting it raises the allowed daily profit to at least the percentage of the target |
| `ConsistencyAlerts` | `75,90` | Percentages of today's allowed profit that alert |
| `ConsistencyAutoFlatten` | `false` | `true` flattens and locks an account when today's profit reaches the cap |
| `InactivityDays` * | blank | Days without a trade your firm allows. Blank or `0` = off. Per account: `InactivityDays:Acct001=7` |
| `InactivityAlerts` | `3,2,1` | Alert when this many days (or fewer) are left |
| `BackupAlertUrl` | blank | A second channel for critical alerts: the alert is POSTed as plain text to this URL (for example `https://ntfy.sh/your-long-random-topic`). Treat the URL like a password. Check with `/testbackup` |
| `BackupAlertDetail` | `minimal` | `minimal` = a short message with the kind of trouble and the last 3 characters of the account name, no amounts. `full` = the whole alert, only for a service you run yourself |
| `BackupAlertFormat` | `text` | `text` (request body is the alert) or `json` (`{"text":...,"content":...,"message":...}`, for Slack and Discord incoming webhooks) |
| `EnableRiskLog` | `true` | Record every risk reading to `DirectTelegramNotifier-risk.csv` for replay (about 7 MB a day per account while the market moves; capped at 50 MB) |
| `CpuAlertPercent` | `90` | NinjaTrader CPU level that triggers an alert. `0` = off |
| `CpuAlertSeconds` | `180` | How long CPU must stay at that level (minimum 30) |
| `MemoryAlertMB` | `0` | NinjaTrader memory level that triggers an alert. `0` = off |
| `DiskAlertMB` | `2000` | Free space on the config folder's drive below which you are alerted. `0` = off |
| `FlattenConfirm` | `false` | `true` makes `/flatten` and `/lock` show Confirm / Cancel buttons first (they expire after 30 s) |
| `FlattenHoldSeconds` | `30` | After `/flatten`, anything that opens on those accounts this soon is closed again. `0` = off |
| `FillGroupMs` | `2000` | Fills of the same instrument and side within this window become one message (500-10000) |
| `SilentRoutineAlerts` | `false` | `true` sends fills, reconnects, reports and command replies without a notification sound |
| `EnableFillCsv` | `true` | Append every fill and leader-vs-follower comparison to `DirectTelegramNotifier-fills.csv` |

\* Can also be set for one account: `Key:AccountName=value`, for example `DrawdownAmount:Acct002=2500`. Unrecognized keys are reported in Telegram at startup.

## Worked example

`config/DirectTelegramNotifier.mffu-rapid-eod-50k.config.example` shows one real setup: three 50k end-of-day evaluation accounts with limits also set in the broker. It is an example, not a preset. Firm rules change, so check your own plan. Placeholders mark what you must replace.
