# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`today` is a single-project .NET 10 console app packaged as a **dotnet global tool** (`ToolCommandName: today`). It tracks what you worked on during a day: `start`/`end` named tasks, `did` for one already finished, `rm` for one logged by mistake, `show` the day as a Gantt chart, `list` past days, `clear` state, `theme` the output, `help` and `version`.

## Commands

```bash
dotnet build
dotnet run -- <args>               # e.g. dotnet run -- start coding, dotnet run -- show
dotnet pack -c Release             # produces ./nupkg/Today.<version>.nupkg
dotnet tool install --global --add-source ./nupkg Today
```

**`dotnet run` writes the user's real day.** State moved out of `bin/Debug/net10.0/` in 2.0,
so a development run is no longer isolated from the installed tool. Point both overrides at a
scratch directory before running anything:

```bash
export TODAY_DATA_DIR=/tmp/today-data TODAY_CONFIG_DIR=/tmp/today-config
```

There are no tests and no test framework in the repo; verification is done by running the CLI.

Styling is suppressed whenever stdout is redirected, so piping into a file or `cat` shows plain text. To see the colors from a tool call, allocate a pty: `script -qec "dotnet run -- show" /dev/null | cat -v`.

`TreatWarningsAsErrors` is on and `Nullable` is enabled — any nullable warning fails the build.

`README.md` is the user-facing counterpart to this file, and is packed into the nupkg via
`PackageReadmeFile`, so it doubles as the nuget.org listing — a change visible to someone
*using* `today` belongs there as well as here. `Today.csproj` also sets `RepositoryUrl`
explicitly rather than leaving it to SourceLink, so the package still says where it came from
when the git query is skipped (see the `release` skill on the sandbox and `.gitmodules`).

`Taste` (the persistence library) is a separate repository, `~/code/Taste`, by the same author. A
change here that needs one there is two sessions, not one — ask before working in it. A `PreToolUse`
guard enforces that.

## Architecture

**Persistence via `Taste`** (NuGet `Taste` 2.1.0, https://github.com/Zindre17/Taste — the same author's, and per-taste pantries were added to it for this app). `Cook.Serve<T>()` hands back a `T` — what was kept last time, or a fresh one — and `.Savor()` (the extension form of `Cook.Preserve`, from `using Taste.Savoring;`) writes it back. Key consequences:

- **`Serve` never returns null**, so nothing here checks for it or writes `??= new`. A taste therefore has to be able to make itself: parameterless constructor, defaults as property initializers — which is why `Today`, `History` and `Theme` all initialize their properties and none of them is a positional record.
- **`Savor` takes the taste itself**, not a handle, so it keeps whichever object it is called on. `RollOverIfNewDay` relies on that: it assigns a brand new `Today` over the captured `today` local, and the `finally` savors the replacement rather than the day just archived.
- A malformed file makes `Serve` **throw** rather than fall back to a fresh taste — state that cannot be read is a thing to go and look at, not to quietly overwrite. Nothing catches it, so a hand-edited jar surfaces as a `JsonException` stack trace.
- Files are named `<assembly>.<full type name>.json` — namespace and all, so with everything in namespace `Today` that is `today.today.today.json`, `today.today.history.json` and `today.today.theme.json`. `Taste` builds that name privately; nothing here reproduces it, deliberately (see `Storage`).
- **Where they live is `Storage`'s decision, not `Taste`'s default.** `Storage.Arrange()` calls `Cook.UseKitchen` with two pantries: `Pantry` (the data directory) for `Today` and `History`, and a per-taste pantry for `Theme` (the config directory). Days are records the user cannot reconstruct; the theme is a preference they could retype — that is the same split the OS makes, so it is the one used. On Linux that lands at `~/.local/share/today/` and `~/.config/today/`.
- `Serve` returns a cached instance per type, so all call sites in a run share one object and there is no handle to pass around. Nothing is written unless `Savor()` is called.
- **A kitchen is settled once per process and cannot be changed after the first `Serve`**, which is why `Storage.Arrange()` is the first statement in `Program.cs`, ahead of `Cook.Serve<Today.Today>()`. Serving first would settle the default kitchen — state beside the executable — and `UseKitchen` would then throw rather than move it.
- `Program.cs` calls `today.Savor()` in a `finally` around the command dispatch, so today's state always persists — including on read-only commands, which is why even `today complete rm` rewrites the file. **`History` and `Theme` are not covered by that** — every code path that mutates them must call `Savor()` itself. `today.today.theme.json` is therefore only written once `theme set`/`theme reset` runs; until then the defaults are in-memory.

**`Storage` owns the locations** (`Storage.cs`). `TODAY_DATA_DIR` and `TODAY_CONFIG_DIR` override the OS defaults, and they are what makes a development run safe: state no longer lives in `bin/Debug/net10.0/`, so a bare `dotnet run` writes the **real** day. Set both to a scratch directory when exercising anything (the `release` and `verify-output` skills do). An OS location that comes back empty throws rather than quietly putting the day in the working directory.

**Moving off the executable directory was done by hand, once.** Up to 1.11.0 state sat beside the running binary (`~/.dotnet/tools/` for the shim, `bin/Debug/net10.0/` under `dotnet run`) under the pre-2.0 names `today.today.json`, `today.history.json`, `today.theme.json`; 2.0 both moved the directory and — because `Taste` 2.0 names a jar by the taste's *full* type name — added a segment to every file name. There is **no migration code**: the author moved and renamed their own three files and that was the whole population. Nothing looks beside the executable any more, so a leftover pre-2.0 file there is invisible rather than dangerous. Anyone else arriving with 1.x state renames by hand:

```
today.today.json    -> ~/.local/share/today/today.today.today.json
today.history.json  -> ~/.local/share/today/today.today.history.json
today.theme.json    -> ~/.config/today/today.today.theme.json
```

**Model** (`Today.cs`, `Doings.cs`, `History.cs`): a `Today` is a `Date` plus a `List<Doing>`; a `Doing` is `What` + `Start` + nullable `End` (null = still running). `History.Days` is a `Dictionary<DateTime, Today>` keyed by the day's midnight `Date`, so `show <date>` / `clear history <date>` go through `TryParseDay`, which drops any time typed alongside the date — carrying it through made `show "2026-08-12 10:00"` report no history and `clear history "…10:00"` succeed at doing nothing.

**A task belongs to the day it is logged on.** `Today.Date` says so, `History` is keyed by it, and the chart's window is drawn from it, so a task outside it breaks all three at once — the axis degenerates and the duration column overflows. Two entry points could put one there and both now refuse to: an explicit `when` outside today (`TryParseWhen`), and a `did` duration long enough to reach back past midnight (checked in `Did`, since `TryParseDuration` sees no clock). The second hid better, because every time reported back is `HH:mm` — `did x 100h` used to answer "from 17:10 to 21:10" without mentioning that the 17:10 was four days ago.

**Output and theming** (`Output.cs`, `Theme.cs`, NuGet `Fansi` 0.0.3 by the same author as `Taste`). Every console write goes through `Output`, which is the only place that touches `Fansi`. `Theme` holds one `ThemeStyle` per output element (header, task, running, bar, axis, duration, date, success, error) and stores colors **by name** so the JSON stays readable and an unknown name degrades to the default rather than failing to load; `Theme.Get`/`Set` look elements up reflectively, so adding a property to `Theme` extends `theme set` automatically — and equally, an element nothing in `Output` reads is a setting the user can change to no effect, which is what `time` was until it was dropped. Removing one needs no migration: the now-unknown key in an existing `today.today.theme.json` (in the config directory) is ignored on load and disappears on the next write. Errors go to **stderr** and everything else to stdout, so `today show > day.txt` captures the chart and not the reason there wasn't one; the two streams are redirected independently, so `Plain` and `PlainError` each ask about their own and `Format` takes which one to use. `Output.Format` builds a `Fansi.OutputFormat` per style and is also where column width and ellipsis truncation live — `Doing` has no `ToString` override, so this is the single renderer.

**`show` draws a Gantt chart** (`Output.Chart`): one row per *name* — name, how long it took, then the bar — with time on the x axis. Tasks are grouped by `What`, so a name logged more than once (`did` allows it, and work picked up again is the same work) draws every stretch of itself on one row and sums their times; `GroupBy` keeps first-appearance order, which is start order because `Insert` sorts. Grouping is ordinal, matching how `end` and `rm` match a name, so `Alpha` and `alpha` stay separate rows. The total is unaffected — it was always the sum over every entry. The duration column is right-aligned and `Spell` writes it the way `did` accepts it (`45s`, `15m`, `1h30m`), so a time the chart reports can be typed straight back in; the total sits in that same column, which is why it reads as their sum rather than as the sentence it used to be. Both are styled with the `duration` theme element, including on a running row, so the column stays a column. `Window` rounds out to whole hours so labels land on the hour; a local `Column` function maps a moment to a chart column; `Axis` places the closing label first and lets intermediate ticks yield to it, so the label at the chart's edge is always truthful; `TickStep` picks the smallest interval whose `HH:mm` labels still fit. Bars use `█`, unfinished ones `▓` drawn up to `Finish` (now for the current day, the last thing that happened for a day out of history — never backwards, or a task started ahead of that moment would measure negative). Any stretch, even a zero-length one on the closing edge, gets at least one cell. Because one row can now hold both finished and running stretches, `Bar` emits the line a run at a time so each keeps its own style and the gaps stay unstyled; where stretches of a name overlap the running one wins the cell, so a row that is still going says so all the way to its edge. `ChartWidth` derives from `Console.WindowWidth` (80 when redirected, since it throws without a terminal) and reserves room for the overhanging final label so nothing wraps; it is measured against `Indent`, so widening the name or duration column narrows the chart automatically. Below about 59 columns the chart hits its minimum of 20 and the row is wider than the terminal. In plain mode (`NO_COLOR` set, or `Console.IsOutputRedirected`) the same format is built without colors, which emits no escape sequences while keeping the padding.

**Day rollover** runs in `RollOverIfNewDay` (`Program.cs`) once before dispatch, so every command sees a current day: if the loaded `Today.Date` isn't today, the old day is pushed into `History` (and `Savor`ed) and a fresh `Today` replaces it. A stale day with no tasks is discarded rather than archived, so `list` doesn't fill with blanks.

**Command dispatch** is a list-pattern `switch` expression over `args` in top-level statements in `Program.cs`; each command is a local function taking the remaining arguments and returning the process exit code. The `commands` table at the top holds each one's name, argument syntax and one-line description, and is the single source for both readers of it: `complete commands` takes the names, and `help` prints the table. So a new command means a dispatch arm, a local function, a row in `commands` — plus a `Complete` case and a name in `today.bash`'s `end|rm|show` test if its arguments are completable.

Bare `today` and an unknown command both fall through to `help`, so the command list has one renderer rather than a second terse copy. An unknown command prints its complaint to stderr first, then the table to stdout.

**Shell completion** (`completions/today.bash`, installed to `~/.local/share/bash-completion/completions/today`). The script holds no knowledge of its own: it asks the binary via the hidden `complete` command — `today complete commands` for the command list, `today complete end` for the tasks currently running, `today complete rm` for every distinct name on the day, `today complete show` for the days in history — so `today end <TAB>`, `today rm <TAB>` and `today show <TAB>` each offer exactly what that command would accept. `complete show` offers history only, never today's own date: `show` with no argument is today, and `show <today>` would look in `History` and find nothing. The script passes `$cmd` straight through, so a new completable command needs a `case` in `Complete` and its name in the `end|rm|show` test. `Complete` writes raw lines with `Console.WriteLine` rather than through `Output`, and is deliberately left out of the `commands` table since it is for scripts. Task names contain spaces, so the script dequotes the word readline gives it before matching and re-escapes each candidate with `printf %q` (unless the user opened a quote, where raw names complete inside it). Changing the command list means editing `commands` in `Program.cs` only.

**Exit codes**: 0 on success, 1 on any user error — unknown command, missing or unparseable argument, "already doing X", "not started doing X". `Today.Start`/`Today.End` return `bool` for this reason. `help` (also `--help`, `-h`) and `version` (also `--version`) are the exception in the other direction: they print to stdout and exit 0, because being asked for them is not a failure. Bare `today` prints the same help at exit 0 — showing someone the commands is not a failure either, and there is nothing to report as one. `version` reads the assembly version, so `<Version>` in the csproj is what it reports.

Arguments are positional after flags are filtered out (`IsFlag`), so `start -c x` and `start x -c` behave the same: `start <what> [when]`, `end [what] [when]`, `did <what> <duration>`. Times go through the `TryParseWhen` helper, which prints a message and fails the command rather than falling back to `DateTime.Now`. It takes a *moment on today* — not today, or still to come, and it is refused — while `TryParseDay` takes *a date*, any date, for the commands that name a past day. The future check compares by the minute, so the minute in progress counts as now; anything finer would reject `start x 14:23` over the seconds spent typing it.

`did` records a task that ran for `<duration>` and ended now, for the things you only remember to log afterwards. `TryParseDuration` accepts a run of number+unit pairs (`15m`, `2h`, `1h30m`; s/sec, m/min, h/hr/hour, case-insensitive) and rejects everything else rather than guessing — a bare number has no unit, so it fails. Values are capped at a week by the parser, and then `Did` refuses any duration that would put the start before midnight, so the effective cap is the time since. Note that a leading `-` makes an argument a flag, so a negative duration never reaches the parser. Unlike `start`, `Today.Did` does not refuse a name that is already running: logging what you just did says nothing about what you are doing.

`rm <what>` deletes a task from today — the mistyped `start`, the `did` with the wrong duration. `Today.Remove` uses `FindLastIndex`, and since `Insert` keeps `Tasks` in start order that is the latest-starting match, which is the one just logged; running or finished makes no difference. It only touches today, never `History` — clearing a past day is `clear history <date>`.
