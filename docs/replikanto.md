# Using the notifier with Replikanto

This page records one working setup: Replikanto copying from a leader account to two followers, all three accounts in the same NinjaTrader, with the notifier watching all of them. It describes what one person runs on 50k evaluation accounts. It is not a recommendation. Written against Replikanto 1.7.1.0 and NinjaTrader 8.1.8.4. Replikanto is a separate product by FlowBots and this project is not affiliated with it. Its behaviour can change between versions, so check the vendor pages linked at the end before relying on anything here.

## Settings in use

Taken from the Properties window of the Replikanto tab for MNQ DEC26, in November 2026. Only the part of the window that was captured is listed. The copy method, follower sizing and the Remote Replikanto group are not.

### Follower Guard: switched on, against the vendor's defaults

The vendor ships Follower Guard with every action off except Disarm Followers. These are on here:

| Setting | Value |
|---|---|
| Flatten the follower if it becomes out of sync | on, after 10 s |
| Flatten the follower if its position is in the opposite direction | on, after 5 s |
| Flatten follower if any submitted order is rejected | on |
| Flatten follower if it was unable to submit order | on |
| Flatten follower if the changing order submitted returns in error | on |
| Flatten follower if the cancellation order submitted returns in error | on |
| Flatten follower if an unspecified error is returned | on |
| Flatten follower in the event of an error after X seconds | 3 s, for the five error options |

### Follower Guard: at the vendor's defaults

| Setting | Value |
|---|---|
| Disarm Followers | on |
| Cancel the follower entry order if it is not filled | off. Its 5 s delay does nothing while the box is off |
| E-mail to receive follower disarm alerts | blank, so no e-mail is sent |
| E-mail Subject | Replikanto Follower Disarm Alert |

### General

| Setting | Value |
|---|---|
| Prevent copying in an inactive workspace | on |
| Refresh a Rithmic account's position before acting on it | on |
| Lock follower settings while in a trade | on |
| Block copying of submitted trades | off |
| ScalperTicket product integration | on |
| Followers PnL total | on |
| Group followers by connection | off |
| Replikanto Window Title | blank |

### Network Replikanto

Network Adapter Ethernet, Port 4547, Communication timeout 1200, Instrument propagation off. These matter only when copying to another computer. Here the leader and both followers are accounts in the same NinjaTrader, so they are not used.

## Settings worth a deliberate choice

| Setting | What it does | Suggestion |
|---|---|---|
| Follower Guard actions: flatten if out of sync, if in the opposite direction, or on submit and order errors | With them off, an out-of-sync follower turns yellow and keeps receiving copies. With one on, Replikanto flattens the follower after the delay and, if Disarm Followers is on, stops copying to it until you rearm | Your call. On is protective but reactive: after any action the follower stops copying until you rearm it. Opposite direction only fires when leader and follower hold opposite sides, so it is the least likely to fire by accident. Keep the delays at the vendor's defaults (10 s out of sync, 5 s opposite direction, 3 s for order errors): the vendor warns that very short delays can close a follower that is still catching up |
| Disarm Followers | After a Guard action fires, the follower is disarmed and receives no copies until you rearm it (Rearm, Flatten All, or its own flatten button) | Leave on (the default). It only matters once an action above is on |
| E-mail to receive follower disarm alerts | Empty in the author's setup, even with the actions on, so a disarm is silent apart from the notifier's copy alert, which only fires at the next trade | Fill it in if you turn Guard actions on. NinjaTrader needs a default e-mail share service for it to send |
| Prevent copying in an inactive workspace | As the name says: no copying while another workspace is active | Keep Replikanto in the one workspace you leave active. If copying stops after you open another one, this is why |
| Copy method and ratio | Exact Quantity at ratio 1 in the author's setup | Match `FollowerRatio` in the notifier config |
| Instrument of the tab | Each Replikanto tab is tied to one leader account and one instrument (MNQ DEC26 in the author's setup) | Plan the switch to the next contract. The lock below blocks the change while a trade is open, so do it when flat |

## How it fits with the notifier

Seen live: `/status` shows the copy check as "in sync" with the leader and two followers. Everything below comes from Replikanto's documentation and has not been exercised against the notifier.

- **Replikanto acts first and the notifier reports afterwards.** The guard flattens an out-of-sync follower after 10 s. The notifier's copy alert waits `MismatchSeconds` (default 20). If the guard flattens a follower while the leader still holds a position, the notifier raises a COPY MISMATCH about 10 s later, and that is your sign the guard fired. Keep `MismatchSeconds` above the guard's delay, or you are alerted about gaps Replikanto is about to close.
- **The notifier cannot see that a follower was disarmed.** A disarmed follower copies no new orders, but it is still flattened when the leader closes. Replikanto shows a message in its own window, and sends an e-mail only if you set an address (NinjaTrader needs a default e-mail share service for that). With no address set, the notifier finds out at the next leader trade, when the leader holds a position and the follower does not. You rearm in Replikanto.
- **The notifier's `/flatten` and locks do not rearm followers.** They flatten accounts directly. Replikanto's own Flatten All also rearms flat followers. After a flatten from the notifier, look for red followers in Replikanto. How the guard reacts when the notifier flattens or locks a follower while the leader still holds a position has not been tested: try it on Sim before turning on auto-flatten. With a copier, also read `LockGroup` in [configuration.md](configuration.md).
- **Replikanto lives in a workspace and the notifier does not.** With "Prevent copying in an inactive workspace" on, copying stops while the Replikanto window's workspace is in the background. The notifier keeps tracking all accounts, and the copy check is what tells you.
- **A tab is tied to one instrument.** The vendor describes each tab as having its own leader account and instrument, and Follower Guard acts only on that instrument. This tab is MNQ DEC26, which expires in December. Plan the switch to the next contract: the vendor's settings lock does not allow changing the instrument while a trade is open.

## Vendor pages

- [Follower Guard](https://flowbots.ninja/blog/follower-guard/)
- [Prevent copying in an inactive workspace](https://support.flowbots.ninja/hc/en-us/articles/25905130576148-How-do-I-keep-Replikanto-running-on-an-inactive-workspace)
- [Copying over a local network](https://support.flowbots.ninja/hc/en-us/articles/25727887398292-How-to-copy-trades-between-machines-on-a-local-network)
