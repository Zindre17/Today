---
name: release
description: Cut and install a new version of the `today` global tool — bump, build, commit, pack, reinstall, verify the user's tracked day survived. Use when asked to release, ship, publish, bump the version, "commit and reinstall", or otherwise get a change into the installed `today` command.
---

# Releasing `today`

`today` is a dotnet global tool. A change is not real until it is packed and reinstalled,
because the thing the user runs is the shim in `~/.dotnet/tools`, not `bin/Debug`.

## Protect the user's day first

**The user's real tracked time lives beside the shim** — `Environment.ProcessPath` is what
`Taste` derives its directory from:

```
~/.dotnet/tools/today.today.json      # today, the one that matters
~/.dotnet/tools/today.history.json    # past days
~/.dotnet/tools/today.theme.json      # only exists once `theme set` has run
```

Losing or polluting these loses real records the user cannot reconstruct. Before running the
**installed** `today` for any reason — smoke test, demo, screenshot — checkpoint them:

```bash
for f in ~/.dotnet/tools/today.{today,history,theme}.json; do
  [ -e "$f" ] && cp "$f" "$CLAUDE_JOB_DIR/tmp/$(basename "$f").bak"
done
md5sum ~/.dotnet/tools/today.today.json
```

Restore afterwards and prove it with a second `md5sum` that matches. Every command in
`Program.cs` runs `today.Savor()` in a `finally`, so **even `today complete rm` rewrites the
state file** — there is no read-only invocation.

Prefer not to need the guard at all: exercise changes with `dotnet run -- <args>`, which keeps
its own state in `bin/Debug/net10.0/` and cannot touch the user's.

## Steps

1. **Verify the change** before packaging anything — see the `verify-output` skill for colors
   and tab completion. Cover the error paths and exit codes, not just the happy one.
2. **Update `CLAUDE.md`** if the change added a command, altered persistence, or changed how
   anything renders. It is the project memory; a stale line there costs every future session.
3. **Bump `<Version>`** in `Today.csproj`. Minor bump for a new command or visible behaviour,
   patch for a fix.
4. **`dotnet build`** — `TreatWarningsAsErrors` is on, so any nullable warning is a failure.
5. **Commit.** Explain why the behaviour is what it is, not just what moved.
6. **`dotnet pack -c Release`** → `./nupkg/Today.<version>.nupkg`. The readme warning is
   expected and harmless.
7. **Reinstall**, with the checkpoint from above already taken:

   ```bash
   dotnet tool uninstall --global Today
   dotnet tool install --global --add-source ./nupkg Today
   ```

   Uninstall leaves the JSON beside the shim alone — confirm with `md5sum` rather than
   assuming it.
8. **Sync the completion script if it changed.** It is installed as a *copy*, so editing the
   repo does nothing until:

   ```bash
   cp completions/today.bash ~/.local/share/bash-completion/completions/today
   ```

   Tell the user a new shell is needed to pick it up.
9. **Delete the superseded nupkg** so `--add-source ./nupkg` cannot resolve a stale version.
10. **Report** the version, the commit, and how far `main` is ahead of `origin/main`.

## Pushing

A PreToolUse hook (`.claude/hooks/block-push-to-main.sh`) blocks pushing to `main`, on purpose.
Do not work around it — not with `git -C`, not with a quoted refspec, not by any other route.
Finish the release locally and tell the user to run `! git push origin main` themselves.
