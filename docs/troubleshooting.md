# Troubleshooting

| Symptom | Fix |
|---|---|
| Compile error mentioning `System.Net.Http` | In the NinjaScript Editor: right-click, **References > Add > Framework**, tick `System.Net.Http`, recompile |
| Output shows `Unauthorized Check BotToken` (401) | The token is wrong, revoked, or an old environment variable is overriding what you expect. F5 does not reload environment variables |
| `chat not found` (400) | Open your bot in Telegram and press **Start**, and check `AuthorizedChatID` is your user ID, not a group ID |
| `Ignored message from unauthorized Telegram user id N` | That is the ID Telegram sees for you: put N in `AuthorizedChatID` |
| Nothing happens, no output | The folder must be in `bin\Custom\AddOns`, not `Indicators`. Check the NinjaScript Output window for `[DirectTelegramNotifier]` lines |
| Compile error: type or member defined twice (CS0101 / CS0111) | An old file is still in `AddOns`. Delete the `DirectTelegramNotifier` folder and copy the new one in. From 0.2 or earlier, also delete `DirectTelegramNotifierAddOn.cs` |
| `CONFIG WARNING` in Telegram | A key is misspelled or a value is invalid. The message names it and suggests the correct key |
| A follower keeps getting `LOCK ENFORCED` | The copier keeps re-syncing it. Disable that follower in the copier, or `/unlock` it |
| Copier check does nothing | Set `LeaderAccount` to the account your copier copies from, and list it and its followers in `AccountName`. With no `LeaderAccount` the copy check is off by design |
| A check alerts too often | Set its seconds or percent setting to `0` ([configuration.md](configuration.md)), or raise it |
| `getUpdates failed ... 409` right after F5 | The old instance's poll is still ending. It clears in a few seconds. If it persists, another program uses the same bot token |
| Many CONNECTION LOST and RECONNECTED alerts | After 4 drops in 5 minutes you get one CONNECTION UNSTABLE alert and single drops are not announced after that. CONNECTION DOWN follows if it stays down for a minute, STILL UNSTABLE every 30 minutes, and CONNECTION STABLE once it has been quiet for 10 minutes. The cause is the connection (your internet, a VPN, the broker), not the notifier |

Telegram allows only one program to read a bot's messages: give each program its own bot.

The NinjaScript Output window (lines starting with `[DirectTelegramNotifier]`) and `/diag` are the first places to look.
