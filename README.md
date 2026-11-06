# NT8 Direct Telegram Notifier

**Telegram alerts and an account risk guard for NinjaTrader 8.**

[![tests](https://github.com/pflodin631/nt8-telegram-notifier/actions/workflows/tests.yml/badge.svg)](https://github.com/pflodin631/nt8-telegram-notifier/actions/workflows/tests.yml)

An AddOn that sends fills, connection problems and risk alerts to a private Telegram chat, and lets you check and flatten your accounts from your phone. It talks straight to the Telegram Bot API: no NinjaTrader Share Services, no relay server, no extra DLLs. It works for personal and prop-firm accounts; you describe your own risk rules in the config, and there are no firm presets.

> **Status: beta (v0.6.0).** It can flatten and lock accounts on command and, if you switch it on, automatically. Read [Limitations](#limitations) and the [Disclaimer](#disclaimer) before running it anywhere near live money. Not affiliated with or endorsed by NinjaTrader or Telegram.

**Tested with:** NinjaTrader 8.1.8.4, a Tradovate connection (MFFU), Sim101 and MFFU evaluation accounts, and the Replikanto trade copier ([setup and notes](docs/replikanto.md)).

<!-- Add redacted screenshots here, e.g.: ![Fill alert](docs/images/fill-alert.png) -->

## What it does

**Alerts**
- **Fills.** Each account's fills, merged into one message when several accounts fill together. If you use a trade copier (`LeaderAccount` set), each follower also shows its price difference from the leader in ticks. Partial fills are merged.
- **Connections.** Orders and market data lost or restored, one message per connection. A connection that keeps dropping (4 drops in 5 minutes) gets one CONNECTION UNSTABLE alert, and its single drops are not announced after that. You still get CONNECTION DOWN if it stays down for a minute, STILL UNSTABLE every 30 minutes, and CONNECTION STABLE once it has been quiet for 10 minutes.
- **Problems.** NinjaTrader freezing, rejected orders, an unusual number of new orders, stale market data, a strategy enabled or disabled, followers out of sync with the leader, a position reopening after `/flatten`.
- **Contract roll.** ROLL SOON, ROLL NOW and CONTRACT EXPIRES TODAY for a futures contract that a running strategy, a position or order, or today's fills are using, going by the expiry date NinjaTrader gives it. `/status` lists those contracts with the days left.
- **Restart report** with how long NinjaTrader was down, an **end-of-day summary** per account, and a **software-change notice** when NinjaTrader or Replikanto changes between two starts.

**Risk checks** (each can be switched off)
- **Drawdown floor**, entered by hand or computed from a static, end-of-day trailing or intraday trailing rule, with alerts as net liquidation approaches it.
- **Daily loss limit**, in dollars or percent, and the prop-firm **consistency** and **inactivity** rules.
- **Order checks:** positions without a stop, exit orders left on a flat account, stuck orders, too many contracts.
- **Optional auto-flatten and lock.** At the floor or a limit, the account is flattened and anything that reopens on it is closed again until `/unlock` or the session ends.
- **Risk UNKNOWN** when you hold a position and the account's data is down or stale, so the numbers above can't be trusted.
- **Follow-up after every flatten:** FLAT CONFIRMED, or STILL EXPOSED repeated every minute until it is.
- **Self-test** at startup and on `/selftest`: whether it can read what it needs and whether auto-flatten is armed.

**Also:** a local alert log, a CSV of every fill, a risk log you can replay to see what the risk check decided, NinjaTrader CPU/memory/disk alerts, and two optional extra channels: a [backup push service](docs/alert-channels.md#second-alert-channel) for critical alerts and an [external heartbeat](docs/alert-channels.md#heartbeat-dead-mans-switch) that tells you when NinjaTrader or Telegram stops working.

**Commands** (accepted only from your Telegram user ID)

| Command | Does |
|---|---|
| `/status` | Connections, positions, P&L, risk and locks per account, copier status, strategies, data feeds |
| `/flatten [account]` | Closes positions and cancels working orders on the tracked accounts, or only on the account whose name contains the text you add (`/flatten 002`). Keeps checking until you are flat (up to 15 s) and reports how long it took, or **STILL EXPOSED**. Closes re-entries for `FlattenHoldSeconds` |
| `/cancel [account]` | Cancels working orders and leaves positions alone |
| `/lock [account]` | Flattens and keeps the account flat until `/unlock` or the session ends |
| `/unlock [account]` | Ends a lock (asks to confirm when `FlattenConfirm=true`) |
| `/selftest` | Whether it can read what it needs for each account, which data check is in use, and whether auto-flatten is armed |
| `/testbackup` | Sends a test through the backup alert channel and says whether it worked |
| `/risk` | Mode, net liquidation, high-water mark (the peak a trailing floor follows), floor, buffer, daily loss, consistency and lock for each account |
| `/risk seed ACCOUNT 51000` | Sets a trailing high-water mark to what your firm shows (button to confirm). `/risk seed ACCOUNT bestday 800` sets the best day for the consistency rule. `/risk reset [account]` restarts a trailing floor |
| `/activity [account days]` | Last trade and days left on the inactivity rule. `/activity 002 3` records that the last trade was 3 days ago |
| `/floor [account] [amount]` | Sets a floor by hand: `/floor 48250`, `/floor 002 48500`, `/floor 002 clear`, `/floor clear`. Suspicious values need a button press |
| `/leader [account]` | Shows or changes the copier leader (`/leader clear` returns to the config) |
| `/copy` | Follower fills compared with the leader today: price difference, dollars, lag |
| `/eod` | Preview the end-of-day report without resetting anything |
| `/diag` | Health: last Telegram send and poll, queue, clock offset, CPU, memory, disk, settings, recent log |
| `/simulate` | One sample of every alert type, built by the same code as the real ones |
| `/version`, `/help` | Versions (notifier, NinjaTrader, Replikanto) and the command list |

Example messages:

```
BUY MNQ DEC26 · 10:42:07
Account1 (leader) · 3 @ 29411.00
Account2 · 3 @ 29410.00 · 4 ticks better
Account3 · 3 @ 29410.00 · 4 ticks better

CONNECTION LOST
MFFU · orders + data
10:42:07

RISK BUFFER
Account2
Net liq 48,730.10 · floor 48,000.00
730.10 above the floor (alert level 750.00)
```

## Requirements

- Windows with NinjaTrader 8 (tested on 8.1.8.4)
- Optional: a trade copier. Replikanto 1.7 or later is documented below (tested on 1.7.1.0)
- A Telegram account and a bot token from [@BotFather](https://t.me/BotFather)
- Outbound HTTPS to `api.telegram.org`

## Install

1. **Telegram.** Message @BotFather, send `/newbot`, and copy the token. Open your new bot and press **Start**. Message @userinfobot to get your numeric user ID.
2. **Copy the code.** Copy the whole `src/DirectTelegramNotifier` folder into `Documents\NinjaTrader 8\bin\Custom\AddOns\`.
3. **Compile.** In NinjaTrader open **New > NinjaScript Editor** and press **F5**.
4. **Configure.** The first compile creates `Documents\NinjaTrader 8\DirectTelegramNotifier.config`. Set `AuthorizedChatID`, `AccountName` (the accounts to watch) and the bot token, then press **F5** again. The token can also go in the `NT_TELEGRAM_BOT_TOKEN` environment variable instead of the file.
5. **Check.** You should get a "Notifier online" message. Send `/status`, then `/simulate` to see every alert type.

**Upgrading:** delete the old `AddOns\DirectTelegramNotifier` folder, then copy the new one in. Copying over it can leave a renamed file behind, and NinjaTrader then reports the class as defined twice. From 0.2 or earlier, also delete `AddOns\DirectTelegramNotifierAddOn.cs`. Your config, floors and logs carry over.

## Configuration

Every setting has a default. You only need the bot token, `AuthorizedChatID` and `AccountName`. In the config file each setting has its explanation on the same line, with the common ones at the top. **Every account in `AccountName` is tracked on its own;** set `LeaderAccount` only if you use a trade copier. Anything after a `#` is a comment, and a value the notifier cannot read is reported in Telegram at startup.

- [All settings](docs/configuration.md), and a [worked example](config/DirectTelegramNotifier.mffu-rapid-eod-50k.config.example) for three prop-firm evaluation accounts.
- [Risk rules](docs/risk.md): drawdown floors, daily loss, locks, consistency, inactivity and the self-test.
- [Backup channel and heartbeat](docs/alert-channels.md).
- [Files the notifier creates](docs/files.md): what each one holds, how big it can get, what is safe to delete.
- [NinjaTrader settings and updates](docs/ninjatrader-settings.md).
- [Replikanto settings in use](docs/replikanto.md), and how its Follower Guard interacts with the copy check.
- [Troubleshooting](docs/troubleshooting.md).

The notifier does not know your broker's or firm's rules. You describe them, then check `/risk` against the numbers your firm shows: theirs are the ones that count. **Test on Sim first** ([docs/TESTING.md](docs/TESTING.md)) and never treat this as your only protection.

## Using it with Replikanto

The notifier works with any trade copier, because it only compares positions. This section is for [Replikanto](https://support.flowbots.ninja/hc/en-us/categories/25197842217236-Knowledge-Center) (FlowBots). Not affiliated; written against Replikanto 1.7.1.0 and NinjaTrader 8.1.8.4. Names and defaults can change between versions, so check FlowBots' documentation.

List every account Replikanto touches and name the leader:

```
AccountName=LeaderAccount,Follower1,Follower2
LeaderAccount=LeaderAccount
FollowerRatio=1                # 1 for Exact Quantity; your ratio otherwise
MismatchSeconds=20
```

The notifier does not read Replikanto's state (armed, out of sync, disarmed). It compares positions, so it raises a copy alert whenever a follower differs from the leader for any reason: disarmed, Replikanto closed, a rejected order. It does not rearm followers or switch Replikanto on or off.

**Replikanto settings.** The author runs with seven Follower Guard flatten actions on (everything except cancelling an unfilled entry) and the e-mail address blank. [docs/replikanto.md](docs/replikanto.md) lists every setting, the ones that deserve a deliberate choice, and how Follower Guard interacts with the copy alert. Test any change on Sim first; the right values depend on your firm's rules.

Replikanto 1.7.0.0 and later work with every NinjaTrader 8.1.6 or later, so a NinjaTrader update no longer needs a Replikanto reinstall; older builds do (FlowBots). Replikanto's ID is shown in its Remote Replikanto settings: crop it out of screenshots you post. For NinjaTrader's own settings and for keeping both up to date, see [docs/ninjatrader-settings.md](docs/ninjatrader-settings.md).

## Security

- Network traffic goes to `api.telegram.org` and, only if you set them, your `HeartbeatUrl` and `BackupAlertUrl`. There is no server of mine in between.
- Telegram carries the full alerts, including account names and amounts. The backup channel, if you use one, gets a short message without amounts by default.
- Commands and button taps are accepted only from the numeric user ID in `AuthorizedChatID`. Messages queued before startup are discarded and commands older than 120 s are ignored, so a stale `/flatten` never runs after a restart.
- The config file is plain text. Prefer the environment variable for the token, turn on two-step verification on your Telegram account, and in @BotFather run `/setjoingroups` and choose Disable.
- If a token leaks, revoke it in @BotFather (`/mybots`, API Token, Revoke). See [SECURITY.md](SECURITY.md).

## Limitations

The ones that matter most (full list in [docs/limitations.md](docs/limitations.md)):

- **It cannot report its own crash or power loss.** Use the external heartbeat for that.
- **It does not enable or disable strategies or copiers.** NinjaTrader offers no supported way. A lock closes new positions within about a second; it cannot prevent the order, and each blocked entry costs a round trip.
- **Its risk numbers are estimates.** They come from NinjaTrader's net liquidation and the rule you enter. Firms differ on closing balance, trailing and fees, so compare `/risk` with your firm's dashboard.
- **A flatten that does not complete is reported, not re-sent.** A second flatten while the first is working could reverse the position.
- **Commands and alerts depend on Telegram and your internet connection.**

## Verified so far

Automated tests run on every push. Which features have also been seen working in NinjaTrader is listed in [docs/TESTING.md](docs/TESTING.md#what-has-been-checked).

## Uninstall

Delete the `AddOns\DirectTelegramNotifier` folder, press F5, and delete `DirectTelegramNotifier.config`, `.log`, `.log.old`, `.heartbeat`, `.floors`, `.state`, `-risk.csv` and `-fills.csv` (plus the `.old` copy of each log and CSV; [what each file is](docs/files.md)) from `Documents\NinjaTrader 8`. Remove the `NT_TELEGRAM_BOT_TOKEN` environment variable and revoke the bot token if you no longer need it.

## Disclaimer

This software is provided as is, without warranty (see [LICENSE](LICENSE)). It is not trading advice. Automated trading and remote order control can lose money, and a bug or a missed alert could cost you. **Some prop firms and brokers restrict automation, trade copying or remote order management; checking the rules that apply to you is your responsibility.** Not affiliated with or endorsed by NinjaTrader, Telegram or any broker.

Parts of the code were written with AI assistance (Claude) and tested as described above. It is plain C#; read it before you run it.

## Contributing

Issues and pull requests are welcome. For bugs, include the add-on version, your NinjaTrader version and the Output window lines, with tokens, IDs, account numbers and URLs redacted. Run `bash tests/run-tests.sh`, `bash tests/run-behavioral.sh` and `bash tests/run-replay.sh` (Mono) before a pull request; CI runs the same.

## License

MIT, see [LICENSE](LICENSE).
