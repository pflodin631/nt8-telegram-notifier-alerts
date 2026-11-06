# Files the notifier creates

Everything is in `Documents\NinjaTrader 8`, next to NinjaTrader's own files. Recompiling (F5), restarting NinjaTrader or upgrading the add-on never creates a second copy of anything: the same files are appended to or rewritten. Your config file is never overwritten.

| File | What it holds | Size |
|---|---|---|
| `DirectTelegramNotifier.config` | Your settings. Created once, on first compile, if it does not exist | a few KB |
| `DirectTelegramNotifier.log` and `.log.old` | A text copy of every alert sent | At 2 MB the log becomes `.log.old`, replacing the previous `.old`. About 4 MB at most |
| `DirectTelegramNotifier-fills.csv` and `.old` | One row per account per fill. `role`, `ticks_worse_than_leader` and `lag_ms_vs_leader` are filled only when `LeaderAccount` is set | Rolls at 10 MB, about 20 MB at most |
| `DirectTelegramNotifier-risk.csv` and `.old` | Every risk reading, for replaying a session (`EnableRiskLog`) | Rolls at 50 MB, about 100 MB at most. Set `EnableRiskLog=false` if you do not want it |
| `DirectTelegramNotifier.state` | Trailing high-water marks, daily baselines, locks, activity clocks, a `/leader` choice and the last NinjaTrader and Replikanto versions seen | About 1 KB per account. Rewritten in place |
| `DirectTelegramNotifier.floors` | Floors set with `/floor`. Only exists while at least one is set | Tiny |
| `DirectTelegramNotifier.heartbeat` | A timestamp the heartbeat reads | Under 1 KB, rewritten in place |
| `DirectTelegramNotifier.state.tmp` | A temporary copy while the state file is saved | Deleted right after |

The `tmp`, `trace` and `log` folders and the `.xml` files in that directory belong to NinjaTrader.

## Old accounts stay in the state file

The state file keeps an entry for every account it has seen, including ones you no longer use, such as prop-firm evaluation accounts that were replaced after a reset. It costs about 1 KB each and does nothing: only accounts that are currently tracked are checked or alerted on. The notifier does not delete these entries on its own, because deleting a trailing high-water mark by mistake would make a risk floor wrong.

To clear them, delete `DirectTelegramNotifier.state` while NinjaTrader is closed. Trailing high-water marks, daily baselines, locks and activity clocks then start over; re-enter them with `/risk seed` and `/activity`. `DirectTelegramNotifier.floors` may keep a line per old account too; delete it the same way.

## Starting over

Delete any of these files while NinjaTrader is closed. The add-on creates what it needs again. To use the newest config layout after an upgrade, rename your config file, press F5 so a fresh one is written, then copy your values across. Your existing config keeps working as it is.
