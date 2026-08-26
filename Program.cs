using System.Globalization;
using System.Reflection;
using Taste;
using Taste.Savoring;
using Today;

// Settles where state is kept. Has to come first: the kitchen cannot be changed once a taste is served.
Storage.Arrange();

// Every command, described once. `complete commands` takes the names, and `help` prints the table,
// so adding a command here is the only place it has to be introduced.
(string Name, string Args, string Does)[] commands =
[
    ("start", "<what> [when]", "Begin a task. -c ends the others first."),
    ("end", "[what] [when]", "Finish a task, or the newest one."),
    ("did", "<what> <duration> [when]", "Log one already over: 15m, 1h30m."),
    ("rm", "<what>", "Delete a task logged by mistake."),
    ("on", "<date> <command>", "Run one of the above against a past day."),
    ("show", "[date]", "Draw the day as a Gantt chart."),
    ("list", "", "The days kept in history."),
    ("clear", "today | history [date]", "Forget today, or a day of history."),
    ("theme", "[show | set | reset]", "Color the output."),
    ("completion", "<shell>", "Print the shell completion script."),
    ("help", "", "What you are reading."),
    ("version", "", "Which version this is."),
];

// The shells `completion` can emit a script for. Kept in one place so the command and its own
// tab-completion cannot disagree about what is supported; each name pairs with an embedded
// resource called `completion.<shell>`.
string[] shells = ["bash"];

// What `on` will run against a past day. The rest are left out for want of a meaning rather than
// out of caution: `list` and `theme` are not about a day at all, `clear` and `completion` already
// say which day or shell they act on, and `help`/`version` would only be answering the same thing
// twice. `on` is absent from its own list, which is what refuses `on monday on tuesday ...`.
string[] amendable = ["start", "end", "did", "rm", "show"];

var current = RollOverIfNewDay(Cook.Serve<Day>());

return Dispatch(current, args);

// The day a command is given is passed to it rather than reached for, so that `on` can hand one
// command a different day without changing what any of the others mean.
int Dispatch(Target target, string[] arguments) => arguments switch
{
    ["start", .. var rest] => Start(target, rest),
    ["end", .. var rest] => End(target, rest),
    ["did", .. var rest] => Did(target, rest),
    ["rm", .. var rest] => Remove(target, rest),
    ["on", .. var rest] => On(target, rest),
    ["show", .. var rest] => Show(target, rest),
    ["clear", .. var rest] => Clear(target, rest),
    ["list", ..] => ListHistory(),
    ["theme", .. var rest] => ThemeCommand(rest),
    ["complete", .. var rest] => Complete(target, rest),
    ["completion", .. var rest] => Completion(rest),
    ["help" or "--help" or "-h", ..] => Help(),
    ["version" or "--version", ..] => Version(),
    [var c, ..] => NotACommand(c),
    [] => Help()
};

// Archives the previous day as soon as any command runs on a new day, so that
// history is complete even on days where nothing was ever started.
Target RollOverIfNewDay(Day day)
{
    if (day.Date == DateOnly.FromDateTime(DateTime.Now))
    {
        return Target.Current(day);
    }

    // A day without tasks is not worth remembering.
    if (day.Tasks.Count > 0)
    {
        var history = Cook.Serve<History>();
        history.Days[day.Date] = day;
        history.Savor();

        WarnAboutUnfinished(day);
    }

    // The fresh day is written here and not left to the command that follows. Nothing else will
    // do it — a command that only reads keeps nothing — and a stale day left in the jar would be
    // archived again, and warned about again, on every command until something happened to be
    // logged.
    var fresh = Target.Current(new Day());
    fresh.Keep();
    return fresh;
}

/// <summary>
///     Says so when a day is archived with something still running.
/// </summary>
/// <remarks>
///     This is the only moment the tool knows a task is running on a day that is ending. Left
///     unsaid it goes into history with no end at all, and <see cref="Output.Chart" /> measures
///     an unfinished task only up to the last thing that happened on its day — which is usually
///     that task itself, being the one that was forgotten, so it draws as a single cell worth
///     nothing and the day totals short with nothing saying how much is missing.
///     Saying so is all that is done about it: an end that was invented — midnight, or the last
///     known moment — is a number that would later be read as something recorded. It fires once,
///     since the next rollover is a no-op, which is why it names the day, the task and the way to
///     put it right in one go.
/// </remarks>
void WarnAboutUnfinished(Day day)
{
    var unfinished = day.Tasks.Where(t => t.End is null).Select(t => t.What).Distinct().ToArray();

    if (unfinished is [])
    {
        return;
    }

    Output.Warn($"{day.Date:yyyy-MM-dd} was archived with {Names(unfinished)} still running.");
    Output.Warn("An unfinished task is measured only up to the last thing that happened that day, so that day's total is short.");
    Output.Warn($"Give it an end with: today on {day.Date:yyyy-MM-dd} end {Shell(unfinished[0])} <time>");

    // Names can contain spaces, so each is quoted rather than run together.
    static string Names(string[] names) =>
        names is [var only]
            ? $"'{only}'"
            : $"{string.Join(", ", names[..^1].Select(n => $"'{n}'"))} and '{names[^1]}'";

    // The suggested command is meant to be typed, so a name with a space in it is quoted the way
    // a shell needs rather than the way prose does.
    static string Shell(string name) => name.Any(char.IsWhiteSpace) ? $"\"{name}\"" : name;
}

/// <summary>
///     A moment on the day being worked on. Every task belongs to a single day —
///     <see cref="Day.Date" /> says so, <see cref="History" /> is keyed by it, and the
///     chart's window is drawn from it — so a moment outside that day is refused rather than
///     quietly breaking all three. A moment still to come is refused too: this records what
///     happened.
/// </summary>
bool TryParseWhen(Target target, string arg, out TimeOnly when)
{
    when = default;

    // NoCurrentDateDefault leaves a string that named no date sitting on 0001-01-01, which is the
    // only way to tell a bare `17:30` from one that spelled a date out. It matters because a bare
    // time means a time on the day being worked on, and that is only today when today is what is
    // being worked on.
    if (!DateTime.TryParse(arg, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var parsed))
    {
        Output.Error($"'{arg}' is not a valid time. Try 14:30, or 2:30pm.");
        return false;
    }

    // A date spelled out has to be the day being worked on. A bare time needs no such check —
    // it cannot name a day, which is the whole reason a task keeps only a TimeOnly.
    if (parsed.Date != default(DateTime).Date && DateOnly.FromDateTime(parsed) != target.Day.Date)
    {
        var named = target.IsToday ? "today" : $"{target.Day.Date:yyyy-MM-dd}";
        Output.Error($"{parsed:yyyy-MM-dd} is not {named}, and a day only holds its own tasks.");
        return false;
    }

    when = TimeOnly.FromTimeSpan(parsed.TimeOfDay);

    // Only today can hold a time that has not happened. On a past day every time already has,
    // and without a date to compare there is nothing that would say so -- 17:30 would read as
    // the future purely because it is later than the clock right now.
    if (target.IsToday)
    {
        // A time typed by hand is precise to the minute, so compare by the minute: the one in
        // progress counts as now, and only a later one is the future. Anything finer would
        // reject `start x 14:23` for the seconds it took to type it.
        var now = TimeOnly.FromDateTime(DateTime.Now);
        if (ToMinute(when) > ToMinute(now))
        {
            Output.Error($"{when:HH:mm} has not happened yet — it is {now:HH:mm}.");
            when = default;
            return false;
        }
    }

    return true;

    static TimeOnly ToMinute(TimeOnly moment) => new(moment.Hour, moment.Minute);
}

/// <summary>
///     The moment a command falls back to when no time was typed. Only today has one: a day
///     already past has no "now" on it, and defaulting to the real one would put the task on the
///     wrong day — which <see cref="TryParseWhen" /> exists to prevent.
/// </summary>
bool TryDefaultWhen(Target target, out TimeOnly when)
{
    when = TimeOnly.FromDateTime(DateTime.Now);

    if (target.IsToday)
    {
        return true;
    }

    Output.Error($"{target.Day.Date:yyyy-MM-dd} is not today and has no now, so say what time.");
    when = default;
    return false;
}

/// <summary>
///     A day named by <c>show</c>, <c>clear history</c> or <c>on</c>, which is a date rather than
///     a moment and may be any day. <see cref="History.Days" /> is keyed by midnight, so a time
///     typed alongside the date is dropped — carrying it through would silently match nothing.
/// </summary>
bool TryParseDay(string arg, out DateOnly day)
{
    var now = DateOnly.FromDateTime(DateTime.Now);

    if (string.Equals(arg, "yesterday", StringComparison.OrdinalIgnoreCase))
    {
        day = now.AddDays(-1);
        return true;
    }

    // A weekday names the most recent day it fell on, and never today: someone who types the day
    // they are standing in means the one a week back, since today is reachable without naming it
    // and has nothing in history to name.
    // The letters-only guard is load-bearing — Enum.TryParse also accepts the numbers behind the
    // names, which would quietly turn `show 3` into Wednesday.
    if (arg.Length > 0
        && arg.All(char.IsAsciiLetter)
        && Enum.TryParse<DayOfWeek>(arg, ignoreCase: true, out var weekday))
    {
        var back = ((int)now.DayOfWeek - (int)weekday + 7) % 7;
        day = now.AddDays(-(back is 0 ? 7 : back));
        return true;
    }

    if (!DateTime.TryParse(arg, out var parsed))
    {
        Output.Error($"'{arg}' is not a date. Try 2026-08-17, yesterday, or {now.AddDays(-1):dddd}.");
        day = default;
        return false;
    }

    day = DateOnly.FromDateTime(parsed);
    return true;
}

bool IsFlag(string arg) => arg.StartsWith('-');

/// <summary>
///     Parses a compact duration such as 15m, 2h or 1h30m.
/// </summary>
bool TryParseDuration(string arg, out TimeSpan duration)
{
    duration = TimeSpan.Zero;
    var rest = arg.AsSpan();

    while (!rest.IsEmpty)
    {
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        var letters = digits;
        while (letters < rest.Length && char.IsAsciiLetter(rest[letters]))
        {
            letters++;
        }

        // A number, then its unit. Anything else -- no digits, no unit, a stray
        // character -- means this is not a duration at all.
        if (digits is 0 || letters == digits || !long.TryParse(rest[..digits], out var value))
        {
            break;
        }

        var unit = rest[digits..letters];
        rest = rest[letters..];

        // Beyond a week the arithmetic stops being meaningful for a day tracker, and
        // TimeSpan.From* throws long before that.
        if (value > 10_080)
        {
            break;
        }

        if (unit.Equals("s", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("sec", StringComparison.OrdinalIgnoreCase))
        {
            duration += TimeSpan.FromSeconds(value);
        }
        else if (unit.Equals("m", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("min", StringComparison.OrdinalIgnoreCase))
        {
            duration += TimeSpan.FromMinutes(value);
        }
        else if (unit.Equals("h", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("hr", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("hour", StringComparison.OrdinalIgnoreCase))
        {
            duration += TimeSpan.FromHours(value);
        }
        else
        {
            break;
        }

        if (rest.IsEmpty)
        {
            return true;
        }
    }

    Output.Error($"'{arg}' is not a duration. Try 15m, 2h or 1h30m.");
    duration = TimeSpan.Zero;
    return false;
}

int ListHistory()
{
    var history = Cook.Serve<History>();
    Output.Blank();
    foreach (var entry in history.Days.Keys)
    {
        Output.Date(entry);
    }
    Output.Blank();
    return 0;
}

int Clear(Target target, string[] arguments)
{
    switch (arguments)
    {
        case []:
            Output.Error("Specify whether to clear 'history' or 'today'.");
            return 1;

        case ["today" or "t", ..]:
            target.Day.Tasks.Clear();
            target.Keep();
            return 0;

        case ["history" or "h", .. var rest]:
            var history = Cook.Serve<History>();

            if (rest is [var day, ..])
            {
                if (!TryParseDay(day, out var date))
                {
                    return 1;
                }
                history.Days.Remove(date);
            }
            else
            {
                history.Days.Clear();
            }
            history.Savor();
            return 0;

        default:
            Output.Error($"'{arguments[0]}' is not something to clear. Specify 'history' or 'today'.");
            return 1;
    }
}

// Feeds the shell completion script. Deliberately absent from Help: it is for
// scripts, not people. Output is raw, one candidate per line, never themed.
int Complete(Target target, string[] arguments)
{
    switch (arguments)
    {
        case ["commands", ..]:
            foreach (var (name, _, _) in commands)
            {
                Console.WriteLine(name);
            }
            return 0;

        // The tasks `today end` would accept right now: the ones still running.
        case ["end", ..]:
            foreach (var task in target.Day.Tasks.Where(t => t.End is null))
            {
                Console.WriteLine(task.What);
            }
            return 0;

        // `today rm` accepts anything on the day, finished or not. A name can repeat, so
        // offer each one once.
        case ["rm", ..]:
            foreach (var name in target.Day.Tasks.Select(t => t.What).Distinct())
            {
                Console.WriteLine(name);
            }
            return 0;

        // Both take a day out of history, and neither takes today: `show` with no argument is
        // already today, and `on` reaches the day in progress without needing to name it.
        case ["show" or "on", ..]:
            var history = Cook.Serve<History>();
            foreach (var key in history.Days.Keys)
            {
                Console.WriteLine($"{key:yyyy-MM-dd}");
            }
            return 0;

        case ["completion", ..]:
            foreach (var shell in shells)
            {
                Console.WriteLine(shell);
            }
            return 0;

        default:
            return 1;
    }
}

// `today completion <shell>`: prints the completion script that ships inside the binary. The
// script is an embedded resource rather than a file beside the tool, because someone who
// installed with `dotnet tool install` has no checkout to copy one out of. Written raw to
// stdout like Complete -- it is meant to be redirected or eval'd, not read.
int Completion(string[] arguments)
{
    var positional = arguments.Where(a => !IsFlag(a)).ToArray();

    if (positional is not [var shell, ..])
    {
        Output.Error($"Say which shell, as in: today completion {shells[0]}. Available: {string.Join(", ", shells)}");
        return 1;
    }

    if (!shells.Contains(shell))
    {
        Output.Error($"There is no completion script for '{shell}'. Available: {string.Join(", ", shells)}.");
        return 1;
    }

    using var script = Assembly.GetExecutingAssembly().GetManifestResourceStream($"completion.{shell}");

    if (script is null)
    {
        Output.Error($"The {shell} completion script is missing from this build.");
        return 1;
    }

    // Copied bytes-as-they-are rather than read as text: this is a script, and re-encoding it
    // is a way to change it.
    using var stdout = Console.OpenStandardOutput();
    script.CopyTo(stdout);
    return 0;
}

// `today help`: every command, what it takes and what it does. Goes to stdout and exits 0 --
// asking for help is not a failure, and the answer should be pipeable.
int Help()
{
    Output.Blank();
    Output.Header("today -- what you worked on, while you work on it");
    Output.Blank();

    foreach (var (name, arguments, does) in commands)
    {
        Output.Command(arguments is "" ? name : $"{name} {arguments}", does);
    }

    Output.Blank();
    return 0;
}

// Raw and unthemed like Complete: a version is something a script reads, not something to look at.
int Version()
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown");
    return 0;
}

int NotACommand(string command)
{
    Output.Error($"'{command}' is not a command.");
    Output.Blank();
    Help();
    return 1;
}

int Start(Target target, string[] arguments)
{
    var flags = arguments.Where(IsFlag).ToArray();
    var positional = arguments.Where(a => !IsFlag(a)).ToArray();

    if (positional is [])
    {
        Output.Error("You must specify what you want to start doing.");
        return 1;
    }

    var what = positional[0];

    if (!TryWhen(target, positional, 1, out var when))
    {
        return 1;
    }

    if (flags.Contains("-c"))
    {
        target.Day.EndAll(when);
    }

    if (!target.Day.Start(what, when))
    {
        return 1;
    }

    target.Keep();
    return 0;
}

int End(Target target, string[] arguments)
{
    if (target.Day.Tasks.Count is 0)
    {
        Output.Error(target.IsToday
            ? "No task started yet."
            : $"Nothing was logged on {target.Day.Date:yyyy-MM-dd}.");
        return 1;
    }

    var positional = arguments.Where(a => !IsFlag(a)).ToArray();

    var what = positional is [var first, ..] ? first : null;

    if (!TryWhen(target, positional, 1, out var when))
    {
        return 1;
    }

    if (!target.Day.End(what, when))
    {
        return 1;
    }

    target.Keep();
    return 0;
}

// `did <what> <duration> [when]` records something that ran for that long and ended at that time,
// or now if none was given. The end can be said out loud because "now" is not always available --
// on a past day there is none -- and because a run that finished earlier was never expressible.
int Did(Target target, string[] arguments)
{
    var positional = arguments.Where(a => !IsFlag(a)).ToArray();

    if (positional is not [var what, var length, ..])
    {
        Output.Error("Say what you did and how long it took, as in: today did standup 15m");
        return 1;
    }

    if (!TryParseDuration(length, out var duration))
    {
        return 1;
    }

    if (!TryWhen(target, positional, 2, out var end))
    {
        return 1;
    }

    // The other way a task can land outside the day it is logged on. It hides better than a
    // date typed by hand does, since the times reported back are only ever HH:mm. Asked of the
    // duration rather than of the result, because backing a TimeOnly past midnight wraps it to
    // the far end of the same day instead of failing.
    var sinceMidnight = end.ToTimeSpan();
    if (duration > sinceMidnight)
    {
        Output.Error($"{length} reaches back past midnight, and a day only holds its own tasks.");
        return 1;
    }

    var start = TimeOnly.FromTimeSpan(sinceMidnight - duration);

    if (!target.Day.Did(what, start, end))
    {
        return 1;
    }

    target.Keep();
    return 0;
}

// The optional time argument, wherever it sits in a command's positional list. Given, it has to
// be a moment on the day being worked on; left out, it is now -- which only today has.
bool TryWhen(Target target, string[] positional, int index, out TimeOnly when) =>
    positional.Length > index
        ? TryParseWhen(target, positional[index], out when)
        : TryDefaultWhen(target, out when);

// `rm <what>` deletes a task from the day, for the ones logged by mistake.
int Remove(Target target, string[] arguments)
{
    var positional = arguments.Where(a => !IsFlag(a)).ToArray();

    if (positional is not [var what, ..])
    {
        Output.Error("Say what to remove, as in: today rm standup");
        return 1;
    }

    if (!target.Day.Remove(what))
    {
        return 1;
    }

    target.Keep();
    return 0;
}

/// <summary>
///     <c>on &lt;date&gt; &lt;command&gt;</c>: runs one command against a day already filed away.
/// </summary>
/// <remarks>
///     History is otherwise write-once — rollover puts days in, <c>show</c> reads them, and the
///     only way to change one is <c>clear history</c>, which deletes the lot. That leaves the day
///     you forgot to close with no repair short of editing the jar by hand.
///     A day that turns out to be today is sent to the day in progress rather than looked up:
///     today is not in <see cref="History" /> yet, so a lookup would report no such day.
/// </remarks>
int On(Target current, string[] arguments)
{
    if (arguments is not [var wanted, var command, .. var rest])
    {
        Output.Error("Say which day and what to do, as in: today on yesterday end coding 17:00");
        return 1;
    }

    if (!TryParseDay(wanted, out var date))
    {
        return 1;
    }

    if (!amendable.Contains(command))
    {
        Output.Error($"'{command}' cannot be run against another day. Try: {string.Join(", ", amendable)}");
        return 1;
    }

    if (date == DateOnly.FromDateTime(DateTime.Now))
    {
        return Dispatch(current, [command, .. rest]);
    }

    if (!Cook.Serve<History>().Days.TryGetValue(date, out var day))
    {
        Output.Error($"No history for {date:yyyy-MM-dd}.");
        return 1;
    }

    return Dispatch(Target.Past(day), [command, .. rest]);
}

int Show(Target target, string[] arguments)
{
    if (arguments is [var wanted, ..])
    {
        if (!TryParseDay(wanted, out var date))
        {
            return 1;
        }

        if (date == DateOnly.FromDateTime(DateTime.Now))
        {
            target = Target.Current(target.Day);
        }
        else if (Cook.Serve<History>().Days.TryGetValue(date, out var historicalDay))
        {
            target = Target.Past(historicalDay);
        }
        else
        {
            Output.Error($"No history for {date:dd-MM-yyyy}.");
            return 1;
        }
    }

    var day = target.Day;

    if (day.Tasks.Count is 0)
    {
        Output.Error("Nothing was done this day...");
        return 1;
    }

    Output.Header(target.IsToday ? $"Today {day.Date:dd MMM}" : $"{day.Date:dd MMM yyyy}");
    Output.Blank();
    // A day out of history has no "now" to draw an unfinished task up to.
    Output.Chart(day.Tasks, target.IsToday ? TimeOnly.FromDateTime(DateTime.Now) : day.Tasks.Max(t => t.End ?? t.Start));
    Output.Blank();
    return 0;
}

int ThemeCommand(string[] arguments)
{
    var theme = Cook.Serve<Theme>();

    var flags = arguments.Where(IsFlag).ToArray();
    var positional = arguments.Where(a => !IsFlag(a)).ToArray();

    switch (positional)
    {
        case [] or ["show"]:
            Output.Blank();
            foreach (var element in Theme.ElementNames)
            {
                Output.Sample(element, theme.Get(element)!);
            }
            Output.Blank();
            return 0;

        case ["set", var element, var color, ..]:
            if (theme.Get(element) is null)
            {
                Output.Error($"'{element}' cannot be themed. Try: {string.Join(", ", Theme.ElementNames)}");
                return 1;
            }
            if (!Output.TryGetColor(color, out _))
            {
                Output.Error($"'{color}' is not a color. Try: {Output.ColorNames}");
                return 1;
            }

            theme.Set(element, new ThemeStyle
            {
                Color = color,
                Bold = flags.Contains("--bold"),
                Dim = flags.Contains("--dim"),
                Italics = flags.Contains("--italics"),
                Underline = flags.Contains("--underline"),
            });
            theme.Savor();
            Output.Success($"Set {element} to {theme.Get(element)}.");
            return 0;

        case ["reset"]:
            theme = new Theme();
            theme.Savor();
            Output.Success("Theme reset to the defaults.");
            return 0;

        case ["reset", var element]:
            if (new Theme().Get(element) is not { } fallback)
            {
                Output.Error($"'{element}' cannot be themed. Try: {string.Join(", ", Theme.ElementNames)}");
                return 1;
            }

            theme.Set(element, fallback);
            theme.Savor();
            Output.Success($"Reset {element} to {fallback}.");
            return 0;

        default:
            Output.Error("Usage: today theme [show | set <element> <color> [--bold] [--dim] [--italics] [--underline] | reset [element]]");
            return 1;
    }
}
