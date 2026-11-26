# nt8-telegram-notifier

NinjaTrader 8 AddOn in C# 5: Telegram alerts, remote commands, risk guard. MIT. Maintainer: Peter (algo trader, MFFU $50K Rapid EOD evaluation accounts, three accounts copied by Replikanto, NinjaTrader on Windows, development on CachyOS Linux).

## How to work with me
- Be direct and skeptical. No yes-man answers. Say when a change is a bad idea and why.
- Keep alert and doc wording plain. No AI jargon, no filler.
- Never put real bot tokens, chat IDs, MFFU account numbers or private URLs in the repo. Use `MFFUEVREOD000000001` and `123456:ABCdef...`.

## Code rules
- C# 5 only: no `$""`, `?.`, `nameof`, `out var`, expression-bodied members.
- Partial class `DirectTelegramNotifier`, split by topic in `src/DirectTelegramNotifier/`. Logic that does not touch NinjaTrader lives in `DirectTelegramNotifier.Core.cs` (class `Dtn`) so it can be unit-tested. Put new logic there.
- Risk engine: `EvaluateRisk` with per-account `DtnRiskState`. Alert levels are latched with a margin (`HeldLevel`, `HeldRiskLevel`) so a value hovering around a level does not repeat the alert (0.6.1). Auto-flatten must use the real level, never the held one.
- A new setting touches: `KnownKeys` (Core.cs), parse in `Config.cs`, field in `DirectTelegramNotifier.cs`, `config/DirectTelegramNotifier.config.example` plus `DefaultConfigText` in `Config.cs` (must be identical), and `docs/configuration.md`. Suite 04 enforces this.
- `/simulate` sends 50 sample messages; adding a sample means updating the count and index in `tests/behavioral/02-risk-and-commands.cs`.
- Safety code (flatten, lock, risk) needs a test that fails without the change. Check by breaking the fix on purpose.

## Tests (need Mono: `mcs`, `mono`)
```
bash tests/run-tests.sh        # unit, seconds (458 checks at 0.6.1)
bash tests/run-behavioral.sh   # whole add-on against a NinjaTrader stand-in, ~90 s
bash tests/run-replay.sh       # recorded risk logs
```
The stand-in can drift from the real API: the final check is an F5 compile in NinjaTrader. Do not claim a feature works in NinjaTrader unless Peter saw it.

## Release
Add a section to `CHANGELOG.md` (plain language), bump `AddOnVersion` and the header comment in `DirectTelegramNotifier.cs` and the README status line, run all three suites, commit, tag `vX.Y.Z`, `gh release create ... --prerelease`.

## Open items
- Peter should confirm 0.6.1 compiles in NinjaTrader and that /status shows the Contracts block (MNQ DEC26).
- Real MFFU account numbers are still in old public git history; `git filter-repo` offered, no decision yet.
- Idea not done: merge the per-account alerts for the three accounts into one message during a trade.
- README needs a redacted screenshot.
