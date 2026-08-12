# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`today` is a single-project .NET 10 console app packaged as a **dotnet global tool** (`ToolCommandName: today`). It tracks what you worked on during a day: `start`/`end` named tasks, `show` the day, `list` past days, `clear` state.

## Commands

```bash
dotnet build
dotnet run -- <args>               # e.g. dotnet run -- start coding, dotnet run -- show
dotnet pack -c Release             # produces ./nupkg/Today.<version>.nupkg
dotnet tool install --global --add-source ./nupkg Today
```

There are no tests and no test framework in the repo; verification is done by running the CLI.

`TreatWarningsAsErrors` is on and `Nullable` is enabled — any nullable warning fails the build.

## Architecture

**Persistence via `Taste`** (NuGet `Taste` 1.0.1, https://github.com/Zindre17/Taste). `Taste<T>.Bite()` loads a JSON file, `.Flavour` is the (nullable) deserialized state, `.Savor()` writes it back. Key consequences:

- Files live **next to the running executable** (`Taste` derives the directory from `Environment.ProcessPath`), named `<assembly>.<type>.json` — i.e. `today.today.json` and `today.history.json`. Under `dotnet run` that means `bin/Debug/net10.0/`; for the installed global tool it's next to the shim, `~/.dotnet/tools/today.today.json`, not the `.store` package directory. State is therefore per-executable, not per-user, and a local build never sees the installed tool's data.
- `Bite()` returns a cached singleton per type, so all call sites in a run share one instance. Nothing is written unless `Savor()` is called.
- `Program.cs` calls `today.Savor()` in a `finally` around the command dispatch, so today's state always persists. **`History` is not covered by that** — every code path that mutates `Taste<History>.Bite().Flavour` must call `Savor()` itself.

**Model** (`Today.cs`, `Doings.cs`, `History.cs`): a `Today` is a `Date` plus a `List<Doing>`; a `Doing` is `What` + `Start` + nullable `End` (null = still running). `History.Days` is a `Dictionary<DateTime, Today>` keyed by the day's midnight `Date`, which is why `show <date>` / `clear history <date>` only match when the parsed date has no time component.

**Day rollover** happens lazily and only inside `Start` (`Program.cs`): if the loaded `Today.Date` isn't today, the old day is pushed into `History` and a fresh `Today` is created. No other command rolls the day over, so `show`/`end` on a stale day operate on yesterday's record.

**Command dispatch** is a `switch` expression over `args[0]` in top-level statements in `Program.cs`; each command is a local function taking `args[1..]` and returning the process exit code. Add new commands there and to the help text in `NotACommand`.

Time arguments are positional: `start <what> [when]`, `end [what] [when]`, parsed with `DateTime.TryParse` and silently falling back to `DateTime.Now` when unparseable. `start` also accepts a `-c` flag that ends all running tasks first.

`Current.cs` is an empty placeholder file.
