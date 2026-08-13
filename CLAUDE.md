# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`today` is a single-project .NET 10 console app packaged as a **dotnet global tool** (`ToolCommandName: today`). It tracks what you worked on during a day: `start`/`end` named tasks, `show` the day, `list` past days, `clear` state, `theme` the output.

## Commands

```bash
dotnet build
dotnet run -- <args>               # e.g. dotnet run -- start coding, dotnet run -- show
dotnet pack -c Release             # produces ./nupkg/Today.<version>.nupkg
dotnet tool install --global --add-source ./nupkg Today
```

There are no tests and no test framework in the repo; verification is done by running the CLI.

Styling is suppressed whenever stdout is redirected, so piping into a file or `cat` shows plain text. To see the colors from a tool call, allocate a pty: `script -qec "dotnet run -- show" /dev/null | cat -v`.

`TreatWarningsAsErrors` is on and `Nullable` is enabled — any nullable warning fails the build.

## Architecture

**Persistence via `Taste`** (NuGet `Taste` 1.0.1, https://github.com/Zindre17/Taste). `Taste<T>.Bite()` loads a JSON file, `.Flavour` is the (nullable) deserialized state, `.Savor()` writes it back. Key consequences:

- Files live **next to the running executable** (`Taste` derives the directory from `Environment.ProcessPath`), named `<assembly>.<type>.json` — i.e. `today.today.json`, `today.history.json` and `today.theme.json`. Under `dotnet run` that means `bin/Debug/net10.0/`; for the installed global tool it's next to the shim, `~/.dotnet/tools/today.today.json`, not the `.store` package directory. State is therefore per-executable, not per-user, and a local build never sees the installed tool's data.
- `Bite()` returns a cached singleton per type, so all call sites in a run share one instance. Nothing is written unless `Savor()` is called.
- `Program.cs` calls `today.Savor()` in a `finally` around the command dispatch, so today's state always persists. **`History` and `Theme` are not covered by that** — every code path that mutates them must call `Savor()` itself. `today.theme.json` is therefore only written once `theme set`/`theme reset` runs; until then the defaults are in-memory.

**Model** (`Today.cs`, `Doings.cs`, `History.cs`): a `Today` is a `Date` plus a `List<Doing>`; a `Doing` is `What` + `Start` + nullable `End` (null = still running). `History.Days` is a `Dictionary<DateTime, Today>` keyed by the day's midnight `Date`, which is why `show <date>` / `clear history <date>` only match when the parsed date has no time component.

**Output and theming** (`Output.cs`, `Theme.cs`, NuGet `Fansi` 0.0.3 by the same author as `Taste`). Every console write goes through `Output`, which is the only place that touches `Fansi`. `Theme` holds one `ThemeStyle` per output element (header, task, running, time, duration, date, success, error) and stores colors **by name** so the JSON stays readable and an unknown name degrades to the default rather than failing to load; `Theme.Get`/`Set` look elements up reflectively, so adding a property to `Theme` extends `theme set` automatically. `Output.Format` builds a `Fansi.OutputFormat` per style and is also where column width and ellipsis truncation live — `Doing` has no `ToString` override, so this is the single renderer. In plain mode (`NO_COLOR` set, or `Console.IsOutputRedirected`) the same format is built without colors, which emits no escape sequences while keeping the padding.

**Day rollover** runs in `RollOverIfNewDay` (`Program.cs`) once before dispatch, so every command sees a current day: if the loaded `Today.Date` isn't today, the old day is pushed into `History` (and `Savor`ed) and a fresh `Today` replaces it. A stale day with no tasks is discarded rather than archived, so `list` doesn't fill with blanks.

**Command dispatch** is a list-pattern `switch` expression over `args` in top-level statements in `Program.cs`; each command is a local function taking the remaining arguments and returning the process exit code. Add new commands there and to `Usage`, which `NotACommand` and the no-argument path both print.

**Exit codes**: 0 on success, 1 on any user error — unknown command, missing or unparseable argument, "already doing X", "not started doing X". `Today.Start`/`Today.End` return `bool` for this reason.

Arguments are positional after flags are filtered out (`IsFlag`), so `start -c x` and `start x -c` behave the same: `start <what> [when]`, `end [what] [when]`. Times go through the `TryParseWhen` helper, which prints a message and fails the command rather than falling back to `DateTime.Now`.
