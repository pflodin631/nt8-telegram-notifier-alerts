# Limitations

The most important of these are also in the README.

- **It cannot report its own crash or power loss.** On the next start it reports the gap; for "never came back" use the heartbeat.
- **The "Notifier stopped" message when NinjaTrader closes is best effort** and may not arrive.
- **It does not enable or disable strategies.** NinjaTrader exposes no supported way to start or stop a strategy from an add-on. A lock *reacts* to new positions within about a second; it cannot prevent the order. Each blocked entry costs a round trip.
- **Risk:** it uses NinjaTrader's Net Liquidation and the rule you enter. Firms differ in details it cannot know, such as what counts as the closing balance, intraday versus closing-only trailing, and fees. Treat `/risk` as an estimate to compare with your firm's numbers.
- **Daily loss and consistency:** measured from the values at the session boundary. If tracking starts mid-session, `/risk` says so.
- **The closing-balance basis assumes NinjaTrader reports open P&L.** The balance is net liquidation minus open P&L. If your connection reported open P&L as 0 instead of failing, the "balance" would silently equal net liquidation. `/risk` shows the open P&L it used (`Net liq ... · open P&L ...`): after your first close with a position held, compare it and the resulting floor with your firm's number.
- **The data check is per account, but approximate.** NinjaTrader does not say which connection serves an instrument's data, so the notifier uses the account's own connection plus the stale-feed check. If your build does not expose an account's connection, it falls back to "any data connection down". Which of the two is in use is written to the NinjaScript Output window for each account, and shown in `/diag`. A lock or flatten still needs a working order connection.
- **A flatten is not re-sent by the follow-up.** If a flatten does not complete you are told (STILL EXPOSED, through both channels if set up) and reminded every minute: for a locked account until it is flat or unlocked (capped at 12 hours), after a `/flatten` for up to 10 minutes. A second flatten while the first is still working could reverse the position, so the follow-up only watches. A locked account does re-send when no close is working, from the lock check.
- **Replay is faithful to within a second.** The live check ticks about once a second with some jitter; a replay ticks exactly once a second between recorded readings, so timers can differ by up to a second.
- **The behavioural tests use a stand-in for NinjaTrader's API.** If the real API differs from what it assumes, only the first compile in NinjaTrader (F5) will show it.
- **Inactivity** counts any fill on a tracked account from NinjaTrader's execution list, counted in 24-hour days. It cannot see trades made outside NinjaTrader, and your firm's definition of "a trade" may be narrower.
- **Stop check:** it sees real working stop orders only. Strategies that manage stops internally look unprotected, which is why it is off by default.
- **Orphan check:** it matches order names (`OrphanOrderNames`), not intent. Stuck-order and orphan checks can be noisy on some connections; set them to `0` if so.
- **Copier checks** assume followers trade the same instrument at a fixed ratio of the leader's size. Copy-quality figures need fills within about 2 seconds of each other to be paired.
- **Stale-data alerts are a best guess.** Schedules ignore holidays and early closes, so those can alert. Use `custom` windows for other markets.
- **End-of-day P&L** comes from the fills this add-on has seen since its last report. NinjaTrader's execution records often show zero commission, so reports say "fees not in fills"; set `EstimatedCommissionPerSide` for an estimate. The broker-reported figure is printed too.
- Amounts are shown without a currency symbol, as reported by NinjaTrader. Messages are in English.
- One notifier per NinjaTrader process, one bot and chat. Commands and alerts depend on Telegram and your internet connection being up.
- **It cannot see your copier's own state.** For example, a follower that Replikanto disarmed is noticed only when the copy check finds it out of step with the leader, which means at the next trade. See [replikanto.md](replikanto.md).
- **Roll warnings go by the expiry date, not by volume.** NinjaTrader gives each contract's expiry, so the warning is "N days before it", which can differ from the day your market's volume moves. It checks contracts that a running strategy, a position or order, or today's fills use, plus `StaleDataInstruments`. It cannot see which contract a Replikanto tab or a chart is on.
