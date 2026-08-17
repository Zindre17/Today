#!/usr/bin/env python3
"""Drive `today`'s bash completion with real TAB keypresses in a pty.

Ordinary `compgen` calls do not exercise readline's dequoting, the `printf %q`
re-escaping of task names with spaces, or the argument-position logic. This does:
it forks a pty, runs an interactive bash with completions/today.bash sourced,
types the line, sends TAB, and captures what readline puts on screen.

Nothing is ever executed -- each line ends with Ctrl-C.

Usage:
    tab-test.py 'today rm \\t\\t' 'today rm st\\t'
    tab-test.py --installed 'today end \\t\\t'

`\\t` in an argument is a TAB. Two list all candidates, one completes.

By default `today` is shimmed to `dotnet run` against the repo, so the user's real
tracked day cannot be touched. With --installed the real global tool is used and its
JSON state is checkpointed and restored around the run -- every `today` command
Savors state in a `finally`, so even `complete` rewrites the file.
"""

import os
import pty
import select
import shutil
import subprocess
import sys
import tempfile
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
COMPLETIONS = os.path.join(REPO, "completions", "today.bash")
STATE = [
    os.path.expanduser(f"~/.dotnet/tools/today.{n}.json")
    for n in ("today", "history", "theme")
]

# Long enough for bash to start and for `dotnet run` to answer a completion query.
STARTUP = 2.5
SETTLE = 4.0


def make_rcfile(directory, installed):
    path = os.path.join(directory, "rcfile")
    shim = (
        ""
        if installed
        else f'today() {{ dotnet run --project {REPO} --no-build -- "$@"; }}\n'
    )
    with open(path, "w") as f:
        f.write(shim)
        if installed:
            f.write('export PATH="$HOME/.dotnet/tools:$PATH"\n')
        f.write(f"source {COMPLETIONS}\n")
        f.write("PS1='$ '\n")
    return path


def drive(rcfile, line):
    """Type `line` into an interactive bash on a pty and return what came back."""
    pid, fd = pty.fork()
    if pid == 0:
        os.execvp("bash", ["bash", "--noprofile", "--rcfile", rcfile, "-i"])

    try:
        time.sleep(STARTUP)
        drain(fd, 0.2)  # discard the prompt
        os.write(fd, line.encode())
        time.sleep(SETTLE)
        out = drain(fd, 1.0)
        os.write(fd, b"\x03")  # Ctrl-C: the completed line must never run
        time.sleep(0.4)
        try:
            os.write(fd, b"exit\n")
        except OSError:
            pass
    finally:
        os.close(fd)
        os.waitpid(pid, 0)

    return out


def drain(fd, timeout):
    out = b""
    while select.select([fd], [], [], timeout)[0]:
        try:
            chunk = os.read(fd, 65536)
        except OSError:
            break
        if not chunk:
            break
        out += chunk
    return out


def clean(raw):
    """Strip the terminal noise that obscures what readline printed."""
    text = raw.decode(errors="replace")
    for junk in ("\x1b[?2004h", "\x1b[?2004l", "\x1b[?1h", "\x1b=", "\x07"):
        text = text.replace(junk, "")
    return text.replace("\r\n", "\n").replace("\r", "\n").strip()


def main(argv):
    installed = "--installed" in argv
    lines = [a for a in argv if a != "--installed"]
    if not lines:
        print(__doc__)
        return 2

    if not installed:
        build = subprocess.run(
            ["dotnet", "build", REPO], capture_output=True, text=True
        )
        if build.returncode != 0:
            print(build.stdout[-2000:] or build.stderr[-2000:])
            return build.returncode

    workdir = tempfile.mkdtemp(prefix="today-tab-")
    saved = {}
    try:
        if installed:
            for path in STATE:
                if os.path.exists(path):
                    saved[path] = os.path.join(workdir, os.path.basename(path))
                    shutil.copy2(path, saved[path])
            print(f"checkpointed {len(saved)} state file(s)\n")

        rcfile = make_rcfile(workdir, installed)
        for line in lines:
            typed = line.encode().decode("unicode_escape")
            print(f"=== {line} ===")
            print(clean(drive(rcfile, typed)))
            print()
    finally:
        for original, backup in saved.items():
            shutil.copy2(backup, original)
        if saved:
            print(f"restored {len(saved)} state file(s)")
        shutil.rmtree(workdir, ignore_errors=True)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
