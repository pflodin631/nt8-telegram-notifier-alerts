# Alert channels: backup and heartbeat

Telegram carries every alert. Two optional extras cover the cases where Telegram, NinjaTrader or the whole machine fails.

## Second alert channel

Every alert reaches you through Telegram. If Telegram is down, or the bot is blocked, you find out only through the heartbeat, and only if its alert does not go through Telegram. `BackupAlertUrl` adds a second path for the alerts that matter most, so a floor breach is not lost with Telegram:

- the first **Risk UNKNOWN** for an account (not the reminders)
- the **tightest** floor-buffer level, and anything below the floor
- the **daily loss limit** reached
- an account **locked** or a lock **enforced**
- an account **still exposed** after a flatten
- **Telegram alerts are not getting through** (and when they are again), which Telegram cannot report about itself

Routine alerts, fills and reports never use it. The same text is not sent twice within a minute, and a failed post is retried once.

**What it sends, and why so little.** The push service is a third party, and ntfy.sh is a public one. By default (`BackupAlertDetail=minimal`) the message says only which kind of trouble and which account by its last three characters, with no balances, floors, positions or reasons, for example:

```
BELOW FLOOR: account ...001. Open NinjaTrader now.
STILL EXPOSED (2m 0s): account ...001 did not go flat. Close it by hand now.
```

The full details stay in Telegram. Even this tells whoever can read the topic that an account is in trouble, so keep the topic secret. `BackupAlertDetail=full` sends the whole alert, with account names and dollar amounts: use it only with a service you run yourself.

**Setting it up with [ntfy](https://ntfy.sh)** (free, no account): choose a long random topic name, install the ntfy phone app and subscribe to it, then set `BackupAlertUrl=https://ntfy.sh/your-long-random-topic` and send `/testbackup`. ntfy's own documentation says the topic is effectively a password, so make it unguessable and do not share it. The notifier sends `Title` and `Priority: urgent` headers, which ntfy understands and other services ignore. For Slack or Discord incoming webhooks set `BackupAlertFormat=json`. It is expected to work with them but I have only confirmed ntfy's request format from its documentation: use `/testbackup`. The URL is redacted from the Output window and logs.

It is a one-way alert channel, not a remote control: nothing can be sent to the notifier through it.

## Heartbeat (dead-man's switch)

The add-on cannot tell you that NinjaTrader or the whole machine died, because it dies with it. An external dead-man's switch can: the add-on pings a URL every minute, and the service alerts you when the pings stop.

1. Create a free check at [Healthchecks.io](https://healthchecks.io): period 1 minute, grace 5 minutes.
2. Add a notification integration (Telegram, email or push).
3. Paste the check's ping URL into `HeartbeatUrl=`, then press F5.

Pings are also held back when alerts can't reach you: while NinjaTrader's UI is frozen, Telegram is unreachable, **Telegram refuses the notifier's messages** (bot blocked, wrong chat, revoked token), or alerts have been stuck in the queue for 3 minutes. While messages are refused, a silent test message is tried every 5 minutes, so the heartbeat resumes by itself once you fix it. The dead-man's switch therefore covers "my alerts stopped arriving", not just "NinjaTrader died". A planned reboot longer than the grace time will alert too.

**Send the Healthchecks alert somewhere other than Telegram**: email, SMS or a push app. The case this detects is Telegram itself being broken, and an alert routed through Telegram would not reach you.
