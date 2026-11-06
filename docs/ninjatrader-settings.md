# NinjaTrader settings and updates

Written against NinjaTrader 8.1.8.4. These are the author's values for the settings that change what the notifier reports or how a restart behaves (Tools > Settings). If a bot vendor tells you to set a value, follow the vendor; this page does not replace their instructions.

| Setting | Author's value | Why it matters here |
|---|---|---|
| General > Time zone | (UTC-05:00) Eastern Time | The times in alerts are NinjaTrader's clock, so they follow this. Set `EodTimeZoneId` to your firm's trading day |
| General > Reopen all previously open workspaces at startup | On | After a restart NinjaTrader reopens the workspace with Replikanto and your strategies. The restart report tells you it was down |
| Trading > Start in global simulation mode | Off | If on, NinjaTrader starts with orders sent to the simulator, so a restarted bot can look active while nothing reaches the broker. Leave it off unless you mean it |
| Trading > Auto close position (Enable) | Off | If on, NinjaTrader flattens the chosen instruments at a set time. Those flattens would look unexplained in your alerts |
| Strategies > Cancel entry orders when a strategy is disabled | On | Disabling a strategy cancels its working entry orders |
| Strategies > Cancel exit orders when a strategy is disabled | On | It also cancels working stop and target orders. A position left open when a strategy is disabled then has no protective orders from the strategy. The notifier's unprotected-position alert (`ProtectiveCheckSeconds`) can flag it if your strategy places real stop orders |
| Strategies > On connection loss | Keep running; disconnect delay 30 s; 999 restart attempts within 1 minute | What NinjaTrader does with strategies when a connection drops. The notifier reports the result (STRATEGY REMOVED, STRATEGY ENABLED); it does not change it |

## Windows

- Keep the PC awake. The notifier cannot report a computer that is asleep or off.
- If OneDrive syncs your Documents folder, exclude `NinjaTrader 8`. FlowBots notes that OneDrive can silently restore old add-on files after an update.

## Updates

As of 3 November 2026 the newest NinjaTrader is 8.1.8.5 and the newest Replikanto this was written against is 1.7.1.0.

- **NinjaTrader updates itself.** The `NTAutoUpdateLog_*` and `NTInstall_*` files in `Documents\NinjaTrader 8` are its record. Your version is under Help > About. NinjaTrader publishes release notes per series, for example [8.1.8](https://ninjatrader.com/support/helpGuides/nt8/8_1_8.htm).
- **Replikanto** shows its version at the bottom of its Properties window. The newest download is on FlowBots' downloads page, which needs a login. One zip installs and updates it (Tools > Import > NinjaScript Add-On). From 1.7.0.0 it works with every NinjaTrader 8.1.6 or later ([FlowBots](https://support.flowbots.ninja/hc/en-us/articles/40941611755668-Which-Replikanto-zip-file-matches-my-NinjaTrader-version)).
- **The notifier does not check for updates online.** Neither vendor publishes a feed it could rely on: Replikanto's downloads need a login and its release notes are a JavaScript-only page, and NinjaTrader's page for the next release can exist before the release (8.1.9 currently shows a placeholder date). A checker built on those would send false alerts or go quiet.
- **It tells you what changed instead.** About 20 seconds after startup it compares the NinjaTrader and Replikanto versions with the ones it saw last time and sends SOFTWARE CHANGED if either differs, or if Replikanto is no longer detected after 90 seconds. `/version` shows both. The first start only records them.
- **A habit that fits:** update when every account is flat, then check `/version`, `/status` and `/selftest`. During an evaluation, try it on Sim first, and read the release notes: 8.1.8.5, for example, only fixes a start-up problem with a CFTC data format.
