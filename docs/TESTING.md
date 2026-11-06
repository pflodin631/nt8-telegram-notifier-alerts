# Testing

There are five levels of testing, and they prove different things. The table at the end of this page says which level each feature has reached. The first three run on GitHub Actions on every push.

| Level | What it proves | How |
|---|---|---|
| **Unit tests** | The pure logic is right, including all of the risk logic: floors, session rollover (weekend, overnight restart, an unwatched close), data outages while holding a position, daily loss, consistency, inactivity days, arming checks, the risk-log format, config keys and values, JSON. Boundary cases are tested (exactly 10 minutes unwatched, exactly 2.000 s at the limit, a 1.00 tolerance) | `bash tests/run-tests.sh` (Mono) or `tests\run-tests.ps1` (Windows); no NinjaTrader needed |
| **Behavioural tests** | The whole add-on is wired correctly: commands, buttons, locks, queue, heartbeat, config loading (including the example files and unreadable values), the flatten follow-up, the self-test, the backup channel, the risk log, `/simulate` | `bash tests/run-behavioral.sh` (Mono): compiles the add-on against a stand-in for the NinjaTrader API and drives it through its real entry points |
| **Replay tests** | A recorded session produces the decisions it should: real readings, fed back through the risk logic | `bash tests/run-replay.sh` (Mono) |
| **Seen on Sim** | It behaves correctly inside real NinjaTrader with real events and your broker connection | The checklist below |
| **Seen live** | The same, on a funded or evaluation account | Only after Sim |

The first three levels catch logic and wiring mistakes. **None of them can prove how NinjaTrader or your broker behaves** (what `Account.Connection` returns, whether open P&L is reported, order names, timing, whether a flatten fills), which is why the Sim checklist matters. The stand-in API can also drift from the real one: the first compile in NinjaTrader (F5) is the check for that.

## Unit tests

```bash
bash tests/run-tests.sh
```

On Windows: `powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1`, which uses the C# compiler built into Windows. Both compile `DirectTelegramNotifier.Core.cs` with `tests/CoreTests.cs` and print `N passed, 0 failed`. The risk rules live in that file as plain inputs and outputs (`EvaluateRisk`), so they can be tested without NinjaTrader.

## Behavioural tests

```bash
bash tests/run-behavioral.sh
```

Needs Mono (`mcs`, `mono`). Six suites run against `tests/behavioral/NinjaTraderStandIn.cs`, a minimal copy of the NinjaTrader API surface the add-on uses, and write only to a temporary folder (never to your real NinjaTrader folder). `tests\run-behavioral.ps1` does the same with the compiler built into Windows; it has not been run on Windows yet.

## Replay tests

With `EnableRiskLog=true` (the default) the add-on writes every reading the risk check sees to `Documents\NinjaTrader 8\DirectTelegramNotifier-risk.csv`, plus each account's rules when they change. `bash tests/run-replay.sh` runs every case in `tests/replay/cases` (a `risk.csv` and the `expected.txt` timeline it must produce). The three cases in the repo are synthetic: one comes from a behavioural-test run against the stand-in API and two were written by hand to check one rule; each `risk.csv` says which at the top. A case recorded on a real Sim session is worth more (see below). To look at a log you recorded:

```bash
bash tests/run-replay.sh path/to/DirectTelegramNotifier-risk.csv --account MFFUEVREOD000000002
```

**Turning something you saw on Sim into a permanent test:**
1. Copy the rows around the incident (the account's `S` row, then its `R` rows) into `tests/replay/cases/<name>/risk.csv`, with a `#` line saying what happened.
2. Run `bash tests/run-replay.sh tests/replay/cases/<name>/risk.csv` and read the timeline. Does it show what you saw live (or what you believe should have happened)? If it does not, you have found a bug: fix it first.
3. Save the output as `expected.txt` in the same folder. From then on it is checked with every run.

A replay ticks once a second between recorded readings, so a timer (the 2 s confirm, a 60 s reminder) can differ by up to a second from the live run. Gaps over two minutes are treated as "NinjaTrader was not running": nothing is invented across them.

## Sim checklist (run before trusting a safety feature)

Use Sim accounts, with the copier running if you use one. Tick each box only after you have seen the result yourself.

### Auto-flatten and the account lock
Settings: `RiskAutoFlatten=true`, `RiskFlattenBuffer=150`, `RiskConfirmSeconds=2`.
- [ ] Open a small position on a Sim follower. Send `/floor <follower> <net liq - 100>`, then press **Set floor** on the confirmation. Within about 3 s you get **ACCOUNT LOCKED** and the account is flat.
- [ ] Let the copier re-sync the follower, or enter manually. You get **LOCK ENFORCED** and the position closes again within a few seconds, without overshooting into the opposite side.
- [ ] `/risk` shows **LOCKED**. `/unlock <follower>` releases it.
- [ ] Restart NinjaTrader while an account is locked: it is still locked afterwards (same session).

### /flatten with the copier actively copying
Settings: `FlattenHoldSeconds=30`.
- [ ] With the leader and followers in a copied position, send `/flatten`. Afterwards every account is flat.
- [ ] Open `DirectTelegramNotifier-fills.csv` and check that no follower briefly flipped to the other side. If one did, you should also have received **RE-ENTRY CLOSED** for it.

### Floor validation
- [ ] `/floor` with a value above net liquidation asks for confirmation, and **Cancel** changes nothing.
- [ ] A floor that jumps more than `FloorJumpConfirm` asks for confirmation.

### Drawdown modes (pick the one matching your rules)
- [ ] `DrawdownMode=static` with `StartingBalance` and `DrawdownAmount`: `/risk` shows the expected floor.
- [ ] `eod`: after the session boundary (`EodReportTime`), a profitable day gives **FLOOR TRAILED** and the new floor matches your firm's dashboard.
- [ ] `intraday`: `/risk` shows the high-water mark following net liquidation upward.

### Daily loss limit
Settings: `DailyLossLimit=` small (for example 100), `DailyLossAutoFlatten=true`.
- [ ] Losing trades give **DAILY LOSS 50% / 75% / 90%**, then **DAILY LOSS LIMIT REACHED**, then **ACCOUNT LOCKED**.

### Risk UNKNOWN
- [ ] Holding a Sim position with a floor set, disconnect the data connection. You get **RISK UNKNOWN** once, and nothing is auto-flattened. Reconnect: **RISK DATA BACK**.

### Self-test
- [ ] `/selftest` shows a real **net liq** and **open P&L** for every account. While you hold a position, open P&L is not `0.00`.
- [ ] The **Data check** line for each account says what you expect: its own connection, or ANY. (If it says ANY, your NinjaTrader build did not expose `Account.Connection`.)
- [ ] With `RiskAutoFlatten=true` and no floor on one account, startup sends a loud **NOT ARMED** report for it, and `/status` says so.

### Flatten follow-up
- [ ] `/flatten` on a position reports **Flat after N.Ns** with a real N (not a fixed 2.5), and `/lock` or an auto-flatten later sends **FLAT CONFIRMED**.
- [ ] Block a flatten somehow (for example disconnect the order connection while the data stays up): you get **STILL EXPOSED** loudly after 15 s, and the notifier does not send a second flatten while the first is working.

### Second alert channel
- [ ] `/testbackup` arrives on your phone, and the alert is readable (no HTML tags).
- [ ] Block Telegram (firewall, or block the bot) while NinjaTrader runs: within about 3 minutes the backup channel says **Telegram alerts are NOT getting through**, and says when they are again.
- [ ] Drive an account to the tightest floor level: the alert arrives both ways. Lower levels (1,500 and 750) arrive only on Telegram.

### Minimal backup message, repeating alerts, open P&L
- [ ] A floor alert to your phone through the backup channel says only "NEAR FLOOR" or "BELOW FLOOR", the last three characters of the account, and "Open NinjaTrader now": no balance, floor or position anywhere in it. (With `BackupAlertDetail=full` it has all of it.)
- [ ] After a `/flatten` or a lock that cannot complete, **STILL EXPOSED** arrives again every minute on Telegram and on the backup channel, each with the new elapsed time, and stops with a quiet **FLAT CONFIRMED** once the position is closed by hand, or a quiet note when you `/unlock`.
- [ ] Hold a Sim position while the market moves: `/risk` shows a **non-zero open P&L** that changes, and you do not get **OPEN P&L LOOKS WRONG**. If you do get it, your connection is not reporting open P&L: say so, it changes how the closing balance is computed.

### Risk log and replay
- [ ] `DirectTelegramNotifier-risk.csv` exists and grows. After a session, replay one account (command above) and check the timeline matches what happened. Anything that does not is a bug worth a replay case.

### Risk UNKNOWN, scoped and optional flatten
Settings: `UnknownFlattenSeconds=0` first, then `120`.
- [ ] After startup, the NinjaScript Output window has a **Risk data check for ACCOUNT: using ...** line for each account. Note whether it says the account's own connection or ANY data connection; `/diag` shows the same. If it says ANY, your NinjaTrader build did not expose the account's connection.
- [ ] Hold a Sim position with a floor set. Disconnect **only the connection that account uses**: you get **RISK UNKNOWN**, repeated every minute with how long it has lasted.
- [ ] With two connections, disconnect the one this account does **not** use: no alert for it. (If you only have one connection, skip this.)
- [ ] With `UnknownFlattenSeconds=120`, after 2 minutes the account is flattened and locked. Check the flatten went through with the data connection down.

### Closing balance, seed and trailing
- [ ] Before the close, hold a profitable position and send `/risk`: the **open P&L** shown must be the real figure, not `+0.00`. (The balance is net liquidation minus open P&L: if your connection reports 0, the balance silently equals net liquidation.)
- [ ] Hold that position across `EodReportTime` with `DrawdownMode=eod`. After the boundary, `/risk` shows the high-water mark at your **closing balance**, not net liquidation, and it matches your firm's dashboard.
- [ ] On an account that already has profit, the first start sends **HIGH-WATER STARTED AT STARTING BALANCE**. `/risk seed ACCOUNT <value>` shows the resulting floor, asks for a button press, and the floor then matches your firm.
- [ ] Quit NinjaTrader before `EodReportTime`, make a trade elsewhere or on another platform, restart after: you get **CLOSING BALANCE UNCERTAIN** and the mark is left alone.

### Consistency rule
Settings: `ConsistencyPercent` and `StartingBalance`.
- [ ] `/risk` shows today's profit against the cap. A winning trade that crosses 75% of it alerts.
- [ ] The number matches your firm's own consistency calculation. If it does not, your firm measures differently: report it.
- [ ] With a best day already over the limit (`/risk seed ACCOUNT bestday ...`), `/risk` shows "Over the limit: X more total profit". Check X against your own arithmetic: best day ÷ the percentage − total profit.

### Inactivity clock
Settings: `InactivityDays=7`, `InactivityAlerts=2,1`.
- [ ] `/activity` shows the right last-trade time for each account (trade, then check). After a restart it still does.
- [ ] `/activity ACCOUNT 5.5` makes the alert for the remaining 1.5 days arrive within a minute; a real trade then sends **INACTIVITY CLOCK RESET**.
- [ ] Compare the day count with your firm's own wording (calendar days or 24-hour days). The notifier counts 24-hour days from the last fill, so plan to trade by day 5 of a 7-day rule.

### Several confirmations
- [ ] With `FlattenConfirm=true`, send `/flatten`, then `/floor` with a suspicious value. Both sets of buttons work, in either order.

### Delivery
- [ ] With `HeartbeatUrl` set, unplug the network for longer than the Healthchecks grace time. The check goes down; reconnect and it recovers.
- [ ] Block the bot in Telegram (or set a wrong `AuthorizedChatID`) while NinjaTrader is running and polling works. After about 3 minutes your Healthchecks check goes down. Fix it: within about 5 minutes **Delivery check** arrives and the check recovers. Route the Healthchecks alert to email or SMS, not Telegram.
- [ ] Put a misspelled key in the config and press F5: you get **CONFIG WARNING** naming it.
- [ ] Set `RiskAutoFlatten=maybe` and press F5: the CONFIG WARNING says automatic flatten is OFF. Then set `RiskAutoFlatten=true  # on` and check `/selftest` shows it switched on.
- [ ] `/leader <account>` changes the leader shown in `/status`, and it survives a restart.

Report anything that differs as an issue, with the Output window lines (redact tokens, IDs and account numbers).

## What has been checked

Unit tests cover the pure logic, including the risk rules. Behavioural tests compile the whole add-on against a stand-in for the NinjaTrader API and drive it through its real entry points. Replay tests feed recorded risk logs through the risk logic. All three run on every push ([docs/TESTING.md](docs/TESTING.md)). None of them can prove how NinjaTrader or your broker behaves, which is what the Sim column is for.

✅ seen working inside NinjaTrader (Sim or evaluation account); ⬜ not yet. For rules that play out over days, ✅ means seen with seeded values (`/activity`, `/risk seed`); the last row is about real multi-day sessions.

| Feature | Automated tests | Seen in NinjaTrader |
|---|---|---|
| Compiles in NinjaTrader 8.1.8.4 | C# 5 compile | ✅ 0.6.0 |
| Fill alerts, partial fills, grouping | behavioural | ✅ |
| `/flatten` (leader and followers), hold after `/flatten` | behavioural | ✅ |
| Connection, restart, rejected-order, order-rate and strategy alerts | partly | ✅ |
| Unstable connection: one alert, a lasting outage still reported, a summary when stable | behavioural | ⬜ (an earlier version of this was seen live and was too noisy; this one has not been) |
| `/status`, `/copy`, `/simulate`, `FlattenConfirm` buttons | behavioural | ✅ |
| Problem alerts sent before routine ones; routine ones merged | behavioural | ✅ |
| Heartbeat ping | — | ✅ |
| Heartbeat held when Telegram refuses messages | behavioural | ⬜ |
| Stale-data alerts, custom schedules | unit (windows) | ⬜ |
| Contract roll warnings and the Contracts block in `/status` | unit, behavioural | ⬜ |
| Copier mismatch alerts, `/leader` | behavioural | ✅ |
| Unprotected, orphan, stuck and position-limit alerts | behavioural | ✅ |
| Floors, `/floor` validation, static and intraday modes | unit, behavioural | ✅ |
| Daily loss limit, auto-flatten and account lock | unit, behavioural | ✅ |
| Risk UNKNOWN, reminders, optional flatten | unit, behavioural, replay | ✅ |
| A flat account is not risk-checked while its connection is down or it reads 0.00 net liq | behavioural | ⬜ (the problem was seen live; the fix has not been) |
| End-of-day trailing, `/risk seed`, closing-uncertain warning | unit, behavioural, replay | ✅ |
| Consistency cap and alerts, inactivity clock and `/activity` | unit, behavioural | ✅ |
| Several confirmations pending at once, `LockGroup` | behavioural | ⬜ |
| Self-test, flatten follow-up, `/unlock` confirmation | unit, behavioural | ✅ |
| SOFTWARE CHANGED when NinjaTrader or Replikanto changes between starts; versions in `/version` | behavioural | ⬜ (needs NinjaTrader to confirm the versions can be read) |
| Backup channel (`/testbackup`, routing, Telegram-down alert) | unit, behavioural | ⬜ |
| Risk log and replay of a real session | unit, replay | ⬜ |
| Config warnings, including unreadable values and a `LeaderAccount` that is not in `AccountName` | unit, behavioural | ⬜ |
| No account assumed to be a copier leader; copy check on only when `LeaderAccount` is set | behavioural | ⬜ |
| CPU, memory and disk alerts; end-of-day summary | behavioural | ✅ |
| End-of-day trailing, best day and inactivity count matching the firm over several real sessions | — | ⬜ |
