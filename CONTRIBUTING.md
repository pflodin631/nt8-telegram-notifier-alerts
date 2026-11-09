# Contributing

Bug reports, fixes and ideas are welcome. This is a small project with one maintainer, so please keep changes small and explain what you tested.

## Reporting a bug

Open an issue and fill in the template. The useful parts are the add-on version (shown in the "Notifier online" message), your NinjaTrader version, your broker connection, the lines in the NinjaScript Output window that start with `[DirectTelegramNotifier]`, and the config settings involved.

**Redact before you post:** bot token, Telegram user ID, account numbers, heartbeat and backup URLs, and your Replikanto ID. Crop or blur screenshots too. If something leaked, revoke the bot token in @BotFather and replace the URLs.

**Security problems** go through GitHub's private vulnerability reporting (the **Security** tab), not a public issue. See [SECURITY.md](SECURITY.md).

## Suggesting a feature

Open an issue first for anything bigger than a small fix, so you don't spend time on something that won't fit. A few things the project keeps on purpose:

- It talks only to Telegram, plus the heartbeat and backup URLs you choose. No relay server, no extra DLLs, no NinjaTrader Share Services. Anything new that sends data somewhere must be listed in the README's Security section and in [SECURITY.md](SECURITY.md).
- No prop-firm presets. Rules differ between plans and change often, so users enter their own.
- It flattens and locks accounts but does not start or stop strategies or copiers.

## Running the tests

You don't need NinjaTrader. On Linux or macOS install Mono (`mcs` and `mono`), then:

```bash
bash tests/run-tests.sh         # unit tests, a few seconds
bash tests/run-behavioral.sh    # the whole add-on against a stand-in for NinjaTrader's API, a couple of minutes
bash tests/run-replay.sh        # recorded risk logs, a few seconds
```

On Windows the `.ps1` scripts in `tests\` do the same with the compiler that ships with .NET Framework. GitHub runs all three on every push and pull request.

The stand-in only copies the parts of NinjaTrader's API this project uses. It can drift from the real one, so the final check is a compile in NinjaTrader (F5). Say in your pull request whether you did that.

## Where things are

- `src/DirectTelegramNotifier/` is the add-on, split by topic. `DirectTelegramNotifier.Core.cs` holds the logic that doesn't touch NinjaTrader (risk rules, config parsing), so it can be unit-tested. Put new logic there when you can.
- `tests/CoreTests.cs` tests `Core.cs`. `tests/behavioral/` has six suites, named by topic, that run the add-on against the stand-in. `tests/replay/cases/` holds recorded risk logs with the output they must produce.
- `docs/` has the guides, `config/` the example config files.

## Writing code

- **C# 5.** NinjaTrader's compiler is old: no `$"..."` strings, `?.`, `nameof`, `out var` or expression-bodied members. The test scripts compile with `-langversion:5`, so they catch this.
- **Safety code needs a test that fails without your change.** This covers flatten, lock, risk and anything that could act on an account. The easiest check: break your fix on purpose and confirm the new test fails.
- **No real data in code or tests.** Use made-up values like `MFFUEVREOD000000001` and `123456:ABCdef...`.

## Common changes

**A new setting.** Add the key to `KnownKeys` in `Core.cs`, read it in `DirectTelegramNotifier.Config.cs`, add the field in `DirectTelegramNotifier.cs`, add one line with a short explanation to `config/DirectTelegramNotifier.config.example` and the same line to `DefaultConfigText` in `Config.cs`, and add a row to `docs/configuration.md`. Test suite 04 checks that the two templates match, that every setting appears once, and that every value in the template is the default.

**A new alert.** Add its text in `DirectTelegramNotifier.Messages.cs` and a sample to `/simulate`. That changes the message count, which `tests/behavioral/02-risk-and-commands.cs` checks, so update the number there.

**Wording.** Alert and doc text should be plain: say what happened and what to do. If a trader who isn't a programmer wouldn't know a word, use a simpler one.

## Docs and changelog

Add a line to `CHANGELOG.md` under a new `## Unreleased` heading at the top. Mark a feature ✅ in the table in `docs/TESTING.md` only if you saw it work inside NinjaTrader. Passing tests alone doesn't earn the ✅.

## Before you open a pull request

1. Run the three test scripts.
2. Check you haven't committed anything private:
   ```bash
   git grep -nE "[0-9]{8,10}:[A-Za-z0-9_-]{30,}"   # bot tokens: must print nothing
   git diff --cached --stat                         # nothing you didn't mean to add
   ```
3. Describe what changed and why, how you tested it, and whether you compiled it in NinjaTrader (which version).

One change per pull request. Code written with an AI assistant is fine if you've read it, understand it and tested it. Say so in the description. Parts of this project were written that way too.

## License

By contributing you agree that your work is released under the [MIT License](LICENSE).
