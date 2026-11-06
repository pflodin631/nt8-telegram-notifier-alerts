# Changelog

## 0.6.0 (2026-11-05)

Fixes, cleanup, and a few new alerts.

**Upgrading:** delete the `AddOns\DirectTelegramNotifier` folder, then copy the new one in. `DirectTelegramNotifier.Assurance.cs` was split into `SelfTest.cs`, `RiskLog.cs` and `BackupChannel.cs`; copying over the old folder leaves it behind and NinjaTrader reports duplicate definitions. Config and state carry over.

**If you use a trade copier, set `LeaderAccount`.** Before this version, with `LeaderAccount` blank the first account in `AccountName` was treated as the copier leader. That guess is gone: with no `LeaderAccount` every account is tracked on its own, there are no copy checks or `(leader)` labels, and `/status` says the copy check is off. A copier setup that relied on list order needs one line added, for example `LeaderAccount=MyLeaderAccount`. Startup now warns if `LeaderAccount` is not in `AccountName`.

Fixed
- **A connection that kept dropping sent an alert every 5 minutes, per channel, for as long as it lasted, and could end in silence.** During a real MFFU outage it sent CONNECTION FLAPPING about every 5 minutes for over an hour, because the drop count still included drops made while the alert was muted. A connection that dropped repeatedly and then stayed down was never reported. Now orders and data of one connection are tracked together. You get one CONNECTION UNSTABLE alert (formerly CONNECTION FLAPPING) and no single drops after it; CONNECTION DOWN if it stays down for 60 seconds, STILL UNSTABLE every 30 minutes, and one CONNECTION STABLE summary after 10 quiet minutes. A sub-second outage reads "Down for under 1s" instead of "Down for 0s". This logic had no tests before; it has them now.
- **A broker disconnect was read as a total loss.** NinjaTrader reports 0.00 net liquidation for an account whose connection is gone. For an account holding no position, that was treated as a real reading: RISK BUFFER "BELOW the floor" and DAILY LOSS LIMIT REACHED alerts (49,915.40 down against a 600 limit), and `/status` showing an 8,319% loss. This happened on a real MFFU disconnect. With auto-flatten on, the same reading would have flattened and locked the accounts until `/unlock` or the end of the session. Now a flat account that reports no value is not checked: its connection is down, or net liquidation reads exactly 0.00, which also happens briefly after a reconnect. No alerts, no lock, no change to the day's starting value, and `/status` says "Risk not checked" with the last good value. It resumes by itself. A held position whose order connection is down raises RISK UNKNOWN, as a dropped data feed already did.
- **A comment after a value was read as part of the value.** `DailyLossAutoFlatten=true  # on` loaded as false, and the worked example's `EstimatedCommissionPerSide=0.95 #...` loaded as 0, both without a warning. A `#` after a space or tab now starts a comment.
- **Values that could not be read were reported only in the NinjaScript Output window.** They now appear in the startup CONFIG WARNING in Telegram. An unreadable auto-flatten switch says that auto-flatten is off. Unreadable amounts (`StartingBalance`, `DrawdownAmount`, `DailyLossLimit`, per-account overrides and the rest) are reported as well.
- **`tests/run-replay.sh` failed on a fresh clone.** `.gitignore` excluded `*.csv`, so the replay inputs were never committed. They are in the repo now, and each one says it is synthetic.
- **The network description was incomplete in two places.** The README's Security section and the source header did not mention `BackupAlertUrl`. Both do now.

Added
- **Contract roll warnings.** For every futures contract in use (a running strategy, a position or working order, today's fills, `StaleDataInstruments`) the notifier reads NinjaTrader's expiry date and sends ROLL SOON (`RollWarnDays`, default 10 days out), ROLL NOW (3 days or less) and CONTRACT EXPIRES TODAY, once each. `/status` has a Contracts block with each expiry and the days left, and the end-of-day report adds a line while one is inside the window. By date, not by volume.
- **SOFTWARE CHANGED.** The notifier remembers the NinjaTrader and Replikanto versions it sees and, about 20 seconds after startup, tells you if either changed since the last start, or if Replikanto is no longer detected (it waits 90 seconds for Replikanto to load). NinjaTrader updates itself, so this is how you find out. It looks at the installed files only and checks nothing online. `/version` shows both versions.
- A Replikanto section in the README and `docs/ninjatrader-settings.md`, with NinjaTrader's settings that matter here and how updates work.

Changed
- **Config files have one setting per line, with its explanation after it.** The file created on first run has the common settings at the top and the advanced ones, at their defaults, at the bottom. The worked MFFU example keeps its longer reasoning in numbered notes at the top. Existing config files are not touched.
- Added `docs/replikanto.md`: the copier settings in use, and how Replikanto's Follower Guard fits with the copy check. The README's Replikanto section is corrected: it said the Follower Guard actions were off, and described the out-of-sync timer as able to fire during a slow partial fill, which the vendor's documentation says it does not.
- **`LeaderAccount` is explicit.** See above. The fills CSV leaves `role`, `ticks_worse_than_leader` and `lag_ms_vs_leader` blank when no leader is set.
- Files and tests renamed to say what they hold. The behavioural suites are named by feature instead of by version.
- The README is about a third as long. Settings, risk rules, alert channels, limitations and troubleshooting moved to `docs/`.
- Removed the two `-clean` config examples, which duplicated the commented ones. Tests check that `config/DirectTelegramNotifier.config.example` matches the file the add-on writes, that it lists every setting exactly once, and that every value in it is the default.
- Example account numbers replaced with placeholders.
- The Verified table moved to `docs/TESTING.md`. It separates rules seen with test values from rules followed over several real sessions; the latter are not yet verified.
- Tests run on GitHub Actions with Mono: unit tests, six behavioural suites and three replay cases. `tests/run-tests.sh` added for running the unit tests without Windows.

## 0.5.1 (2026-10-27)

- The backup channel sends a short message by default: the kind of trouble and the last three characters of the account name, with no amounts. `BackupAlertDetail=full` sends the whole alert, for a service you run yourself.
- STILL EXPOSED repeats every minute, on both channels, until the account is flat or unlocked.
- **OPEN P&L LOOKS WRONG:** a warning when open P&L reads exactly 0.00 on a held position while net liquidation moves.

## 0.5.0 (2026-10-27)

**Upgrading from 0.4:** copy the folder over the old one. New keys: `BackupAlertUrl`, `BackupAlertFormat`, `EnableRiskLog`.

- Optional second alert channel for critical alerts (`BackupAlertUrl`, checked with `/testbackup`), including "Telegram alerts are not getting through".
- After `/flatten`, an automatic flatten or `/lock`, the notifier checks until the account is flat and reports FLAT CONFIRMED or STILL EXPOSED.
- `/selftest`, a self-test 20 s after startup, and a line in `/status`: can it read what it needs, and is auto-flatten armed.
- `/unlock` asks for confirmation when `FlattenConfirm=true`. Every command and button press is written to the log.
- Risk log (`DirectTelegramNotifier-risk.csv`) and replay (`tests/run-replay.sh`).
- More Telegram errors are recognized as permanent (bot blocked, chat not found, deactivated user and similar).

## 0.4.0 (2026-10-27)

**Upgrading from 0.3:** copy the folder over the old one. New keys use their defaults.

- The external heartbeat is held while Telegram refuses the notifier's messages, and a silent test message is tried every 5 minutes.
- Risk UNKNOWN is scoped to the account's own data connection, repeats every minute, and can flatten after `UnknownFlattenSeconds`.
- The end-of-day high-water mark uses the closing balance by default (`HighWaterBasis`). A close NinjaTrader did not see is reported as CLOSING BALANCE UNCERTAIN instead of guessed. `/risk seed` sets the mark for an account that already has profit.
- Several confirmation prompts can be pending at once. `LockGroup` locks every tracked account when one is locked.
- Consistency-rule and inactivity trackers (`ConsistencyPercent`, `InactivityDays` and related keys).
- The risk logic moved into `Core.cs` so it can be unit-tested; the behavioural tests are in the repo.

## 0.3.0 (2026-10-27)

**Upgrading from 0.2:** delete `AddOns\DirectTelegramNotifierAddOn.cs` before copying the new `DirectTelegramNotifier` folder into `AddOns`.

- Account lock: after an auto-flatten, anything that opens on the account is closed again until `/unlock` or the session ends. `/lock` and `/unlock`. A hold after `/flatten` closes copier or strategy re-entries.
- Drawdown modes (`static`, `eod`, `intraday`), a daily loss limit, Risk UNKNOWN, `/floor` validation, per-account overrides and `/risk`.
- Problem alerts and command replies are sent before routine messages, and a backlog of routine messages is merged into one.
- Unrecognized config keys are reported. `/leader`, custom stale-data schedules, configurable stuck-order states and fill grouping.
- Split into several files, with the first unit tests.

## 0.2.0 (2026-10-26)

Safety checks, account-level risk and targeted commands.

- Unprotected-position alert (position without a working stop; off by default).
- Orphan exit orders alert (account flat, stop/target-type orders still working).
- Stuck-order alert (Submitted / change pending / cancel pending too long).
- Position limit alert (`MaxPositionContracts`).
- Risk buffer: alerts as Net Liquidation approaches a drawdown floor, "below floor" and recovery notices, optional per-account auto-flatten (`RiskAutoFlatten`, off by default).
- `/floor` command to show and set floors (saved across restarts); `/status` shows net liquidation, floor and buffer.
- `/flatten [account]` and `/cancel [account]`; the confirm buttons name the account.
- NinjaTrader CPU, memory and disk alerts; CPU, memory and disk shown in `/diag`.
- Fixed: falling below the floor is now its own, tightest alert level.

## 0.1.2 (2026-10-26)

- Commands arrive almost instantly: Telegram long polling replaces polling every few seconds.
- Per-account statistics in `/status` and the end-of-day report (previously summed across accounts).
- Copy quality: follower fills compared with the leader (ticks, dollars, lag), in the end-of-day report and `/copy`.
- `EnableFillCsv`: every fill and leader-vs-follower comparison is appended to `DirectTelegramNotifier-fills.csv`.
- `SilentRoutineAlerts`: routine messages arrive without a notification sound; problems still ring.
- `FlattenConfirm`: optional Confirm / Cancel buttons for `/flatten`.
- New commands: `/copy`, `/eod`, `/diag`, `/version`, `/simulate` (a sample of every alert, built by the real code).
- New alert: a position opened shortly after `/flatten` (a strategy or the copier is trading again).
- New alert: PC clock differs from Telegram's by more than 15 seconds.
- Memory use is bounded: tracking sets are pruned by order lifetime and by age.
- Alerts that Telegram cannot parse are re-sent as plain text instead of being lost.
- Every network call now has its own timeout.

## 0.1.1 (2026-10-26)

- Fills of one instrument and side are grouped into one message, with each follower's price difference from the leader in ticks.
- Internal copier order IDs are hidden from fill alerts.
- All times use the NinjaTrader clock, including the restart report and the alert log.
- `/status` is easier to read: Accounts header, spacing, leader marked.
- "Connecting" shows an hourglass instead of a warning sign.

## 0.1.0 (2026-10-26) - first public beta

- Fill alerts with partial-fill merging.
- Connection lost / disconnected / restored alerts for orders and market data, merged per connection, with flap muting.
- NinjaTrader UI-freeze detection.
- Rejected-order alerts and an order-rate alert.
- Strategy enabled / disabled / removed alerts.
- Stale market-data alerts.
- Trade-copier mismatch alerts (leader vs followers).
- Restart / crash report on startup, plus an optional external heartbeat ping.
- End-of-day summary, `/status`, `/flatten`, `/help`.
- Local alert log.
