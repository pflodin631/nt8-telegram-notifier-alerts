# Risk rules

## Floors, daily loss and locks

The notifier watches each account's **Net Liquidation** every second. It does not know your broker's or firm's rules: you describe them, and you check `/risk` against the numbers your broker or firm shows. Theirs are the ones that count.

**1. Pick how the floor is found** (`DrawdownMode`, per account if your accounts differ):

| Mode | Floor | Typical use |
|---|---|---|
| `off` | none | No floor checks |
| `manual` | `RiskFloor`, or whatever you set with `/floor` | You keep it current yourself |
| `static` | `StartingBalance - DrawdownAmount`, never moves | Static drawdown |
| `eod` | highest **end-of-day** balance - `DrawdownAmount`, capped at `FloorLock`, never moves down | End-of-day trailing drawdown |
| `intraday` | highest **live** Net Liquidation (open profit included) - `DrawdownAmount`, capped at `FloorLock` | Intraday trailing drawdown |

Example, a 50k account with a 2,000 end-of-day trailing drawdown that stops trailing at 50,100:

```
DrawdownMode=eod
StartingBalance=50000
DrawdownAmount=2000
FloorLock=50100
```

The end of day is `EodReportTime` in `EodTimeZoneId`. If your firm closes its day at a different time, set those to match.

**What the high-water mark is measured on.** For `eod`, `HighWaterBasis=balance` (the default) uses the **closing balance**: net liquidation minus open profit. Open profit would otherwise raise your floor above the firm's if you hold a position at the close. The floor is still compared with live net liquidation, because firms count open losses against it. If your firm measures on net liquidation, set `HighWaterBasis=netliq`. Sources disagree about firms' exact definitions, so **compare `/risk` with your firm's dashboard after the first session**.

**Accounts that already have profit.** A trailing mark starts at `StartingBalance`, which is too low if you have banked profit before installing. The notifier tells you when it starts below the account's balance. Send `/risk seed ACCOUNT 51000` with the highest closing balance your firm has counted; it shows the resulting floor and asks you to confirm. If the session ends while NinjaTrader could not see the account and the balance changed, the mark is not guessed: you are told, and `/risk seed` fixes it. High-water marks are saved in `DirectTelegramNotifier.state`. A `/floor` value always overrides the mode until `/floor clear`.

**2. Optional daily loss limit:** `DailyLossLimit=1000` or `2%`, measured from Net Liquidation at the start of each session. It alerts at `DailyLossAlerts` (50/75/90%) and at the limit.

**3. Optional enforcement:** `RiskAutoFlatten=true` and/or `DailyLossAutoFlatten=true`. When an account stays in breach for `RiskConfirmSeconds`, it is flattened, its orders are cancelled and it is **locked**. While locked, any new position on it is closed again at once, and you get **LOCK ENFORCED**. The lock ends with `/unlock` or at the session boundary. **Strategies and copiers are not disabled** (NinjaTrader offers no supported way). If one keeps trading a locked account, each entry is closed at a small loss, so turn it off.

**Safeguards:**
- **Frozen numbers:** while you hold a position and **that account's** data connection is down or the instrument's feed is stale, the account shows **Risk UNKNOWN**. Floor, daily-loss and consistency checks pause for it, and you are reminded every minute. Another connection dropping does not pause it. Pausing is not always what you want: flattening orders do not need market data, so `UnknownFlattenSeconds=120` flattens and locks the account if the data stays gone that long. It is off by default because a short data blip would then cost you the position.
- **Floor typos:** `/floor` asks for a button press when the value is above net liquidation, inside your alert levels, or moves the floor more than `FloorJumpConfirm`.

A wrong rule gives false comfort or false alarms. **Test on Sim first** ([TESTING.md](TESTING.md)), and never treat this as your only protection.

## Prop-firm rules: consistency and inactivity

Prop firms add rules beyond the drawdown. The notifier tracks two of them from numbers you enter. It does not know your firm's rules: they differ between plans and change over time. Enter the values from your own account's up-to-date rules.

**Consistency rule.** "No single day may be more than X% of total profit." Set `ConsistencyPercent` and `StartingBalance`. With profit banked before today, the most today can add is `X% / (100% - X%)` of that profit: for a 30% rule and 1,000 banked, 428.57. `/risk` shows today's profit against that cap and your best day's share of total profit, and you are alerted at 75% and 90% of the cap. The cap only limits **today**: if an earlier day is already over the limit, `/risk` also shows how much more total profit brings it back (best day ÷ the percentage − total profit), to be earned on smaller days. `ConsistencyAutoFlatten=true` flattens and locks the account when it reaches the cap. Notes:
- Today's profit includes open profit, so it warns early rather than late.
- Early in an evaluation the cap is tiny and the alert is noise. Enable it per account when you near the target. Leave `ConsistencyProfitTarget` blank unless your firm really measures against the profit target; set otherwise it overstates your room.
- The best day is learned as sessions end. For an account with history, send `/risk seed ACCOUNT bestday 800`.

**Inactivity rule.** "No trade for N days closes the account." Set `InactivityDays=7`, or per account (`InactivityDays:Acct001=7`), because some firms apply it only to some account types. Any fill counts as a trade. You are warned with 3, 2 and 1 days left (`InactivityAlerts`), told when the limit passes, and told when a trade resets the clock. `/activity` shows every account's last trade and days left. The time is counted in 24-hour days from your last fill. A firm that counts **calendar days** can end the window sooner than that, so treat the last alert day as your deadline: with a 7-day rule, plan to trade by day 5. If NinjaTrader has no earlier trade on record, the clock starts when the notifier first sees the account and is marked *assumed*: correct it with `/activity 002 3`. **The notifier never places trades.** A planned break is usually something to tell your firm about in advance.

## Self-test: is auto-flatten armed?

`/selftest` reads, for each tracked account: net liquidation, open P&L, which data check Risk UNKNOWN uses (the account's own connection, or any connection), the rules in force (floor, daily loss, consistency, inactivity) and whether each automatic flatten you switched on has what it needs. It runs by itself once, 20 seconds after startup, and **loudly** when you have switched auto-flatten on and it cannot act. `/status` carries a one-line summary.

**NOT ARMED** means the risk check has nothing to act on, for a reason it names: net liquidation unreadable, a trailing mode without `DrawdownAmount`, `RiskAutoFlatten` with no floor, `DailyLossAutoFlatten` with no limit, `ConsistencyPercent` without `StartingBalance`, or open P&L unreadable on the closing-balance basis. It only reports. The risk check cannot act without those inputs anyway; the self-test tells you so instead of leaving you to find out.

**Open P&L.** The closing balance is net liquidation minus open P&L. A flat account legitimately reads 0, so the self-test cannot prove the read works. What it can catch is the impossible case: you hold a position, net liquidation keeps moving (several changes and a range of at least 5.00 over 30 seconds), and open P&L stays exactly 0.00. Then you get **OPEN P&L LOOKS WRONG** (Telegram only), `/selftest` repeats it, and `/selftest` also hints whenever it sees exactly 0.00 on a held position. It is a rule of thumb: a fee can set it off, and a connection that reports a wrong but non-zero figure is not caught. After your first close with a position held, compare the result with your firm's number.

**What it does not prove:** that a flatten will succeed. That needs the Sim checklist.

## When an account's connection drops

NinjaTrader reports 0.00 net liquidation for an account whose connection is gone, and for a moment after it comes back. The notifier does not treat that as a reading.

- **Flat account:** not risk-checked until it reports a value again. No floor or daily loss alerts, no auto-flatten, no lock, and the day's starting value is left alone. `/status` and `/risk` say "Risk not checked" and show the last good net liq. It resumes by itself.
- **Account holding a position:** RISK UNKNOWN, repeated every minute, and a flatten after `UnknownFlattenSeconds` if you set it. This applies when the data feed is down and also when the order connection is down.
- **A lock already in place stays in place.**

The CONNECTION LOST and RECONNECTED alerts still tell you the connection dropped; the risk check does not repeat them.
