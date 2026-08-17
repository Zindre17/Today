# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`today` is a single-project .NET 10 console app packaged as a **dotnet global tool** (`ToolCommandName: today`). It tracks what you worked on during a day: `start`/`end` named tasks, `did` for one already finished, `rm` for one logged by mistake, `show` the day as a Gantt chart, `list` past days, `clear` state, `theme` the output.

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

**Output and theming** (`Output.cs`, `Theme.cs`, NuGet `Fansi` 0.0.3 by the same author as `Taste`). Every console write goes through `Output`, which is the only place that touches `Fansi`. `Theme` holds one `ThemeStyle` per output element (header, task, running, time, duration, date, success, error) and stores colors **by name** so the JSON stays readable and an unknown name degrades to the default rather than failing to load; `Theme.Get`/`Set` look elements up reflectively, so adding a property to `Theme` extends `theme set` automatically. `Output.Format` builds a `Fansi.OutputFormat` per style and is also where column width and ellipsis truncation live — `Doing` has no `ToString` override, so this is the single renderer.

**`show` draws a Gantt chart** (`Output.Chart`): one row per *name* — name, how long it took, then the bar — with time on the x axis. Tasks are grouped by `What`, so a name logged more than once (`did` allows it, and work picked up again is the same work) draws every stretch of itself on one row and sums their times; `GroupBy` keeps first-appearance order, which is start order because `Insert` sorts. Grouping is ordinal, matching how `end` and `rm` match a name, so `Alpha` and `alpha` stay separate rows. The total is unaffected — it was always the sum over every entry. The duration column is right-aligned and `Spell` writes it the way `did` accepts it (`45s`, `15m`, `1h30m`), so a time the chart reports can be typed straight back in; the total sits in that same column, which is why it reads as their sum rather than as the sentence it used to be. Both are styled with the `duration` theme element, including on a running row, so the column stays a column. `Window` rounds out to whole hours so labels land on the hour; a local `Column` function maps a moment to a chart column; `Axis` places the closing label first and lets intermediate ticks yield to it, so the label at the chart's edge is always truthful; `TickStep` picks the smallest interval whose `HH:mm` labels still fit. Bars use `█`, unfinished ones `▓` drawn up to `Finish` (now for the current day, the last thing that happened for a day out of history — never backwards, or a task started ahead of that moment would measure negative). Any stretch, even a zero-length one on the closing edge, gets at least one cell. Because one row can now hold both finished and running stretches, `Bar` emits the line a run at a time so each keeps its own style and the gaps stay unstyled; where stretches of a name overlap the running one wins the cell, so a row that is still going says so all the way to its edge. `ChartWidth` derives from `Console.WindowWidth` (80 when redirected, since it throws without a terminal) and reserves room for the overhanging final label so nothing wraps; it is measured against `Indent`, so widening the name or duration column narrows the chart automatically. Below about 59 columns the chart hits its minimum of 20 and the row is wider than the terminal. In plain mode (`NO_COLOR` set, or `Console.IsOutputRedirected`) the same format is built without colors, which emits no escape sequences while keeping the padding.

**Day rollover** runs in `RollOverIfNewDay` (`Program.cs`) once before dispatch, so every command sees a current day: if the loaded `Today.Date` isn't today, the old day is pushed into `History` (and `Savor`ed) and a fresh `Today` replaces it. A stale day with no tasks is discarded rather than archived, so `list` doesn't fill with blanks.

**Command dispatch** is a list-pattern `switch` expression over `args` in top-level statements in `Program.cs`; each command is a local function taking the remaining arguments and returning the process exit code. Add new commands there and to `Usage`, which `NotACommand` and the no-argument path both print.

**Shell completion** (`completions/today.bash`, installed to `~/.local/share/bash-completion/completions/today`). The script holds no knowledge of its own: it asks the binary via the hidden `complete` command — `today complete commands` for the command list, `today complete end` for the tasks currently running, `today complete rm` for every distinct name on the day — so `today end <TAB>` and `today rm <TAB>` each offer exactly what that command would accept. The script passes `$cmd` straight through, so a new completable command needs a `case` in `Complete` and its name in the `end|rm` test. `Complete` writes raw lines with `Console.WriteLine` rather than through `Output`, and is deliberately left out of `Usage` since it is for scripts. Task names contain spaces, so the script dequotes the word readline gives it before matching and re-escapes each candidate with `printf %q` (unless the user opened a quote, where raw names complete inside it). Changing the command list means editing `commands` in `Program.cs` only.

**Exit codes**: 0 on success, 1 on any user error — unknown command, missing or unparseable argument, "already doing X", "not started doing X". `Today.Start`/`Today.End` return `bool` for this reason.

Arguments are positional after flags are filtered out (`IsFlag`), so `start -c x` and `start x -c` behave the same: `start <what> [when]`, `end [what] [when]`, `did <what> <duration>`. Times go through the `TryParseWhen` helper, which prints a message and fails the command rather than falling back to `DateTime.Now`.

`did` records a task that ran for `<duration>` and ended now, for the things you only remember to log afterwards. `TryParseDuration` accepts a run of number+unit pairs (`15m`, `2h`, `1h30m`; s/sec, m/min, h/hr/hour, case-insensitive) and rejects everything else rather than guessing — a bare number has no unit, so it fails. Values are capped at a week. Note that a leading `-` makes an argument a flag, so a negative duration never reaches the parser. Unlike `start`, `Today.Did` does not refuse a name that is already running: logging what you just did says nothing about what you are doing.

`rm <what>` deletes a task from today — the mistyped `start`, the `did` with the wrong duration. `Today.Remove` uses `FindLastIndex`, and since `Insert` keeps `Tasks` in start order that is the latest-starting match, which is the one just logged; running or finished makes no difference. It only touches today, never `History` — clearing a past day is `clear history <date>`.
