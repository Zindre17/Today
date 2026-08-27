# today

What you worked on, while you work on it.

A small command-line day tracker. Start a task when you begin it, end it when you stop, and
draw the day as a Gantt chart when you want to know where it went.

```
$ today show

Today 26 Aug

                                  07:00        08:00         09:00        10:00
    sandbox write-up         45m         ██████████
    review Storage.cs      1h04m                    ▓▓▓▓▓▓▓▓▓▓▓▓▓▓
    morning email            25m                             █████
    total                  2h14m
```

Solid bars are finished, shaded ones are still running. A name logged more than once gets one
row with its stretches drawn on it and its time summed, because picking work back up is the
same work.

## Install

`today` is a .NET global tool and needs the .NET 10 SDK. Build and install it from source:

```bash
git clone https://github.com/Zindre17/Today.git
cd Today
dotnet pack -c Release
dotnet tool install --global --add-source ./nupkg Today
```

To upgrade later, `dotnet tool uninstall --global Today` and install again.

> The `Today` package on nuget.org is an old 1.0.0 and is not this version — build from source
> until it is republished.

## Use

```bash
today start writing            # begin a task
today start standup -c         # begin one, ending whatever else was running
today end                      # finish the most recent
today end writing 14:30        # or a named one, at a given time
today did "code review" 45m    # log something already over
today did lunch 1h 12:30       # or one that ended earlier
today rm standup               # remove one logged by mistake
today on yesterday end writing 17:00   # fix a day you already filed away
today show                     # the chart above
today show yesterday           # a day out of history
today list                     # which days are kept
```

| Command | Takes | Does |
|---|---|---|
| `start` | `<what> [when]` | Begin a task. `-c` ends the others first. |
| `end` | `[what] [when]` | Finish a task, or the newest one. |
| `did` | `<what> <duration> [when]` | Log one already over: `15m`, `1h30m`. |
| `rm` | `<what>` | Delete a task logged by mistake. |
| `on` | `<date> <command>` | Run one of the above against a past day. |
| `show` | `[date]` | Draw the day as a Gantt chart. |
| `list` | | The days kept in history. |
| `clear` | `today \| history [date]` | Forget today, or a day of history. |
| `theme` | `[show \| set \| reset]` | Color the output. |
| `completion` | `<shell>` | Print the shell completion script. |
| `help` | | The list above. |
| `version` | | Which version this is. |

A time is `14:30` or anything `DateTime.Parse` accepts, and has to be a moment on the day you
are working on that has already happened — this records what you did, not what you plan to. A
duration is a run of number-and-unit pairs: `45s`, `15m`, `2h`, `1h30m`.

A date is `2026-08-24`, or `yesterday`, or a weekday name. A weekday means the most recent one it
fell on, and never today — `wednesday` on a Wednesday is the one a week back, since today is
already where you are.

Days roll over on their own. The first command you run on a new day files yesterday into history
and starts you fresh.

## Fixing a day you already filed away

If a day ended with a task still running, the first command you run the next morning tells you so,
and tells you how to put it right:

```
2026-08-25 was archived with 'writing' still running.
An unfinished task is measured only up to the last thing that happened that day, so that day's total is short.
Give it an end with: today on 2026-08-25 end writing <time>
```

Nothing is invented on your behalf — an end time `today` made up would be indistinguishable from
one you recorded. `on <date>` puts `start`, `end`, `did`, `rm` and `show` to work on a day that has
already gone into history:

```bash
today on yesterday end writing 17:00     # the one you forgot to close
today on tuesday did "code review" 45m 15:00
today on friday rm typo
today on yesterday show
```

Because a past day has no "now", the time is not optional there — `today on yesterday start x`
asks you for one rather than guessing.

## Where your day is kept

State lives in the standard OS locations — records in the data directory, the theme in the
config directory, since one is something you could not reconstruct and the other is something
you could retype:

The two directories are .NET's `LocalApplicationData` and `ApplicationData`, with `today/` under
each — so they follow whatever the platform's convention is:

| | Days and history | Theme |
|---|---|---|
| Linux, macOS | `~/.local/share/today/` | `~/.config/today/` |
| Windows | `%LOCALAPPDATA%\today\` | `%APPDATA%\today\` |

Set `TODAY_DATA_DIR` and `TODAY_CONFIG_DIR` to put them somewhere else. Both, if either — they
are independent, and setting one leaves the other where it was.

## Upgrading from 1.x

**2.x moved your state and changed how it is written, and nothing does either for you.** Up to
1.11.0 the files sat next to the `today` binary — `~/.dotnet/tools/` for an installed tool. 2.x
puts them in the directories above under different names, and stores a day as a plain date with
plain times (`"2026-08-25"`, `"09:00:00"`) rather than as timestamps carrying a UTC offset. The
offset was not decoration: it was read back, so opening your day in a different timezone shifted
every time in it and could file the day under the wrong date entirely.

Nothing looks in the old place any more, so after upgrading `today show` reports an empty day and
`today list` an empty history, with your records still sitting where they always were. Move them
by hand, once:

```bash
mkdir -p ~/.local/share/today ~/.config/today
cd ~/.dotnet/tools
mv today.today.json    ~/.local/share/today/today.today.day.json
mv today.history.json  ~/.local/share/today/today.today.history.json
mv today.theme.json    ~/.config/today/today.today.theme.json
```

Then convert the two day files to the new shape. `today` will not read the old one — it stops
with a parse error rather than guessing at your records, so do this before running it again:

```bash
cd ~/.local/share/today
python3 - today.today.day.json today.today.history.json <<'EOF'
import json, sys
d = lambda v: v.split("T")[0]
t = lambda v: v and v.split("T")[1].split("+")[0].split("Z")[0]
def day(x):
    x["Date"] = d(x["Date"])
    for k in x["Tasks"]: k["Start"], k["End"] = t(k["Start"]), t(k["End"])
    return x
for p in sys.argv[1:]:
    j = json.load(open(p))
    j = {"Days": {d(k): day(v) for k, v in j["Days"].items()}} if "Days" in j else day(j)
    json.dump(j, open(p, "w"))
EOF
```

The theme file needs neither step's attention beyond the move — it holds no dates — and only
exists if you ever ran `today theme set`. Nothing is lost if you skip all of this and change your
mind later: the old files are only ignored, never deleted.

## Shell completion

Bash completion asks the binary what to offer, so it always matches what the command accepts —
`today end <TAB>` gives the tasks running right now, `today did <TAB>` the ones already finished,
`today rm <TAB>` everything on the day, and `today show <TAB>` the days in history.

The script ships inside the tool, so there is nothing to fetch. Add one line to `~/.bashrc`:

```bash
eval "$(today completion bash)"
```

Or write it out once, if you would rather not pay for the process at every shell start:

```bash
mkdir -p ~/.local/share/bash-completion/completions
today completion bash > ~/.local/share/bash-completion/completions/today
```

The `eval` form is always in step with the installed version. The written-out copy is not —
re-run it after an upgrade.

## Color

`today theme show` lists every element with its current style. `today theme set bar --color
Magenta --bold` changes one, `today theme reset` puts them all back. Colors are stored by name,
so the file stays readable and a name it does not know falls back to the default rather than
failing to load.

Styling turns itself off when stdout is redirected, and when `NO_COLOR` is set, so
`today show > day.txt` gives you plain text.

## License

MIT. See [LICENSE](LICENSE).
