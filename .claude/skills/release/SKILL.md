---
name: release
description: Cut and install a new version of the `today` global tool — bump, build, commit, pack, reinstall, verify the user's tracked day survived. Use when asked to release, ship, publish, bump the version, "commit and reinstall", or otherwise get a change into the installed `today` command.
---

# Releasing `today`

`today` is a dotnet global tool. A change is not real until it is packed and reinstalled,
because the thing the user runs is the shim in `~/.dotnet/tools`, not `bin/Debug`.

## Protect the user's day first

**The user's real tracked time lives in the OS data directory** — `Storage` decides, not
`Taste`'s default:

```
~/.local/share/today/today.today.day.json        # today, the one that matters
~/.local/share/today/today.today.history.json    # past days
~/.config/today/today.today.theme.json           # only exists once `theme set` has run
```

**`dotnet run` is no longer a sandbox.** Before 2.0 it kept its own state in
`bin/Debug/net10.0/`; now it reads and writes the same files the installed tool does. Point it
somewhere disposable instead — this is the guard, and it works for both binaries:

```bash
export TODAY_DATA_DIR="$CLAUDE_JOB_DIR/tmp/today-data"
export TODAY_CONFIG_DIR="$CLAUDE_JOB_DIR/tmp/today-config"
```

Set both. Setting one leaves the other pointing at the user's real directory.

For the rare case that has to run **unsandboxed** — confirming the installed tool reads the
user's own day, say — checkpoint first:

```bash
for f in ~/.local/share/today/*.json ~/.config/today/*.json ~/.dotnet/tools/today.*.json; do
  [ -e "$f" ] && cp "$f" "$CLAUDE_JOB_DIR/tmp/$(basename "$f").bak"
done
md5sum ~/.local/share/today/*.json ~/.config/today/*.json 2>/dev/null
```

Restore afterwards and prove it with a second `md5sum` that matches, line for line.

Since 2.1 a command writes only when it changes something: `show`, `list`, `complete` and every
failed command leave the files alone, where the old `finally` had all of them rewriting the day.
That makes a read-only invocation genuinely read-only — but it is not a reason to skip the
checkpoint. What you are guarding against is a command that turns out to mutate when you thought
it would not, and that is precisely the case you cannot predict in advance.

**Pre-2.0 files may still sit beside the shim** — `~/.dotnet/tools/today.{today,history,theme}.json`.
There is no migration code: the move into the OS directories was done by hand, once. Nothing
reads those files any more, so they are harmless where they are — but they are also the only
copy of anything that was not carried across, which is why the glob above still covers them.
Do not delete them on the user's behalf.

## Steps

1. **Verify the change** before packaging anything — see the `verify-output` skill for colors
   and tab completion. Cover the error paths and exit codes, not just the happy one.
2. **Update `CLAUDE.md`** if the change added a command, altered persistence, or changed how
   anything renders. It is the project memory; a stale line there costs every future session.
   **And `README.md`** if the change is visible to someone *using* the tool — it is packed into
   the nupkg via `PackageReadmeFile`, so it is also the package's listing on nuget.org. The two
   have different readers: `CLAUDE.md` explains why the code is shaped the way it is, `README.md`
   tells a user what to type.
3. **Bump `<Version>`** in `Today.csproj`. Minor bump for a new command or visible behaviour,
   patch for a fix.
4. **`dotnet build`** — `TreatWarningsAsErrors` is on, so any nullable warning is a failure.

   Inside a sandboxed session the build fails on `Error reading git repository information:
   Access to the path '.gitmodules' is denied`. The sandbox masks `.gitmodules` with an
   unreadable `/dev/null` bind-mount, and SourceLink's git task cannot tell that apart from a
   real file. Build with `EnableSourceControlManagerQueries=false` in the environment — MSBuild
   reads env vars as properties, so this needs no change to the csproj.

   **Step 3 makes this build the first one that restores**, and restore then fails a second way:
   `NU1900: Error occurred while getting package vulnerability data: Read-only file system` for
   `~/.local/share/NuGet/http-cache`. The sandbox allows `~/.nuget` but not that path, and
   `TreatWarningsAsErrors` promotes NU1900 to an error. Point the cache somewhere writable:

   ```bash
   export NUGET_HTTP_CACHE_PATH="$CLAUDE_JOB_DIR/tmp/nuget-http"
   ```

   It only bites after a version or package change, so a build that worked before the bump is no
   evidence it will work after one.
5. **Commit.** Explain why the behaviour is what it is, not just what moved.
6. **`dotnet pack -c Release`** → `./nupkg/Today.<version>.nupkg`. This should now be warning-free;
   the readme warning that used to be "expected and harmless" went away when `README.md` and the
   rest of the package metadata were added to the csproj. A new warning is a real one.

   **Ask the user to run this one from their own terminal.** The workaround from step 4 is not
   safe here: with the git query off, `pack` silently drops the `commit` attribute from the
   nuspec's `<repository>` element, so the published package loses its link back to the source
   commit. The csproj sets `RepositoryUrl` and `RepositoryType` explicitly, so the element
   itself survives — it is the commit hash, and only that, which comes from the git query.
   Compare `1.11.0` (packed outside a sandbox, has it) against `2.0.0` (packed inside one, does
   not) if you need to see the difference. Verify what you shipped:

   ```bash
   unzip -p ./nupkg/Today.<version>.nupkg Today.nuspec | grep -i repository
   ```

   A `<repository>` line with no `commit="…"` means the stamp is missing and the package should
   be repacked outside the sandbox. Pack from a commit, too — a stamp pointing at a commit that
   predates the code in the package is worse than none.
7. **Reinstall**, with the checkpoint from above already taken. Afterwards check that
   `today show` still has the user's day — the shim is replaced, but the state it reads lives
   in the OS directories and should be untouched by the reinstall:

   ```bash
   dotnet tool uninstall --global Today
   dotnet tool install --global --add-source ./nupkg Today
   ```

   Uninstall leaves the JSON beside the shim alone — confirm with `md5sum` rather than
   assuming it.
8. **Check how the user set completion up, if the script changed.** It is an embedded resource
   now, so the reinstall in step 7 already carries the new version — but only for the `eval
   "$(today completion bash)"` form, which re-reads it at every shell start. Someone who wrote
   it out to a file instead still has the old copy:

   ```bash
   today completion bash > ~/.local/share/bash-completion/completions/today
   ```

   Either way a new shell is needed to pick it up.
9. **Delete the superseded nupkg** so `--add-source ./nupkg` cannot resolve a stale version.
10. **Report** the version, the commit, and how far `main` is ahead of `origin/main`.

## Pushing

A PreToolUse hook (`.claude/hooks/block-push-to-main.sh`) blocks pushing to `main`, on purpose.
Do not work around it — not with `git -C`, not with a quoted refspec, not by any other route.
Finish the release locally and tell the user to run `! git push origin main` themselves.
