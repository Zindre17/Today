---
name: verify-output
description: Verify `today`'s terminal output — themed colors, the Gantt chart, and bash tab completion — through a pty. Use when checking colors or theme changes, confirming chart rendering, or testing shell completion, since a normal captured run silently proves nothing about any of them.
---

# Verifying what `today` actually prints

Two things about this project make ordinary verification lie to you. Both have a fix.

## Colors: a captured run is a false negative

`Output.Plain` is true when `NO_COLOR` is set **or `Console.IsOutputRedirected`**. Every tool
call captures stdout, so a plain `dotnet run -- show` renders with no escape sequences at all.
Seeing no colors that way says nothing — the theme could be perfect or entirely broken.

Allocate a pty with `script`:

```bash
# see the colors as the user sees them
script -qec "dotnet run --no-build -- show" /dev/null | sed 's/\r$//'

# inspect the escape sequences themselves
script -qec "dotnet run --no-build -- show" /dev/null | cat -v
```

`script` emits CRLF, hence the `sed`. Use `cat -v` when the question is *which* sequences are
emitted (checking a `theme set`, or that plain mode emits none); use `sed` when the question is
whether it looks right.

Both modes deserve a check when touching `Output`: plain mode must keep the padding while
emitting no escapes, which is why `Format` returns an `OutputFormat` with the widths set and
the colors left null rather than skipping formatting altogether.

Chart width comes from `Console.WindowWidth`, which **throws** without a terminal and falls back
to 80. Under `script` you get the real terminal width, so the layout you see is the real one.

## Tab completion: it needs a real terminal and a real TAB

`compgen` run by hand does not exercise the escaping, the dequoting, or the argument-position
logic. Use the harness in this directory, which forks a pty, runs `bash -i` with the completion
script sourced, sends actual TAB bytes, and presses Ctrl-C so the completed line never executes:

```bash
# against the dev build (default) -- cannot touch the user's state
python3 .claude/skills/verify-output/tab-test.py 'today rm \t\t' 'today rm st\t'

# against the installed tool -- checkpoints and restores the user's JSON automatically
python3 .claude/skills/verify-output/tab-test.py --installed 'today end \t\t'
```

`\t` in an argument is a TAB. Two of them list all candidates; one completes the common prefix.

What to actually check:

- **The candidate set matches the command.** `end` offers only running tasks, `rm` offers every
  distinct name on the day. They come from different `Complete` cases and can drift apart.
- **Names with spaces survive.** `reviewing the PR` must come back as `reviewing\ the\ PR`. This
  is the `printf %q` re-escaping, and it is the part most likely to break.
- **Prefixes work**, including a prefix of a multi-word name (`rev<TAB>`).
- **Position is respected.** `end <what> [when]` takes a task name only in the first positional
  slot; a second TAB there should offer nothing.

Completion candidates come from the binary (`today complete ...`), so the shell script holds no
command list of its own. When testing the dev build, the harness shims `today` to
`dotnet run --project <repo> --no-build --`, which means you are testing your unbuilt changes
only if you built first — the harness runs `dotnet build` for you unless `--installed`.

## The standing rule

Never exercise the **installed** `today` without checkpointing `~/.dotnet/tools/today.*.json`
first. Every command Savors state in a `finally`, so even `complete` writes the file. Prefer
`dotnet run`, which keeps its state in `bin/Debug/net10.0/`. See the `release` skill for the
checkpoint procedure.
