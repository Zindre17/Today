using System.Reflection;
using Taste;
using Today;

var today = Taste<Today.Today>.Bite();

// Every command, described once. `complete commands` takes the names, `help` prints the table
// and Usage lists them, so adding a command here is the only place it has to be introduced.
(string Name, string Args, string Does)[] commands =
[
    ("start", "<what> [when]", "Begin a task. -c ends the others first."),
    ("end", "[what] [when]", "Finish a task, or the newest one."),
    ("did", "<what> <duration>", "Log one already over: 15m, 1h30m."),
    ("rm", "<what>", "Delete a task logged by mistake."),
    ("show", "[date]", "Draw the day as a Gantt chart."),
    ("list", "", "The days kept in history."),
    ("clear", "today | history [date]", "Forget today, or a day of history."),
    ("theme", "[show | set | reset]", "Color the output."),
    ("help", "", "What you are reading."),
    ("version", "", "Which version this is."),
];

try
{
    RollOverIfNewDay();

    return args switch
    {
        ["start", .. var rest] => Start(rest),
        ["end", .. var rest] => End(rest),
        ["did", .. var rest] => Did(rest),
        ["rm", .. var rest] => Remove(rest),
        ["show", .. var rest] => Show(rest),
        ["clear", .. var rest] => Clear(rest),
        ["list", ..] => ListHistory(),
        ["theme", .. var rest] => ThemeCommand(rest),
        ["complete", .. var rest] => Complete(rest),
        ["help" or "--help" or "-h", ..] => Help(),
        ["version" or "--version", ..] => Version(),
        [var c, ..] => NotACommand(c),
        [] => Usage()
    };
}
finally
{
    today.Savor();
}

// Archives the previous day as soon as any command runs on a new day, so that
// history is complete even on days where nothing was ever started.
void RollOverIfNewDay()
{
    if (today.Flavour is null || today.Flavour.Date.Date == DateTime.Now.Date)
    {
        return;
    }

    // A day without tasks is not worth remembering.
    if (today.Flavour.Tasks.Count > 0)
    {
        var history = Taste<History>.Bite();
        history.Flavour ??= new History();
        history.Flavour.Days[today.Flavour.Date] = today.Flavour;
        history.Savor();
    }

    today.Flavour = new Today.Today();
}

/// <summary>
///     A moment on the day being tracked. Every task belongs to the day it is logged on —
///     <see cref="Today.Today.Date" /> says so, <see cref="History" /> is keyed by it, and the
///     chart's window is drawn from it — so a moment outside today is refused rather than
///     quietly breaking all three. A moment still to come is refused too: this records what
///     happened.
/// </summary>
bool TryParseWhen(string arg, out DateTime when)
{
    if (!DateTime.TryParse(arg, out when))
    {
        Output.Error($"'{arg}' is not a valid time. Try 14:30, or 2:30pm.");
        return false;
    }

    var now = DateTime.Now;

    if (when.Date != now.Date)
    {
        Output.Error($"{when:yyyy-MM-dd} is not today, and a day only holds its own tasks.");
        when = default;
        return false;
    }

    // A time typed by hand is precise to the minute, so compare by the minute: the one in
    // progress counts as now, and only a later one is the future. Anything finer would reject
    // `start x 14:23` for the seconds it took to type it.
    if (ToMinute(when) > ToMinute(now))
    {
        Output.Error($"{when:HH:mm} has not happened yet — it is {now:HH:mm}.");
        when = default;
        return false;
    }

    return true;

    static DateTime ToMinute(DateTime moment) => moment.AddTicks(-(moment.Ticks % TimeSpan.TicksPerMinute));
}

/// <summary>
///     A day named by <c>show</c> or <c>clear history</c>, which is a date rather than a moment
///     and may be any day. <see cref="History.Days" /> is keyed by midnight, so a time typed
///     alongside the date is dropped — carrying it through would silently match nothing.
/// </summary>
bool TryParseDay(string arg, out DateTime day)
{
    if (!DateTime.TryParse(arg, out var parsed))
    {
        Output.Error($"'{arg}' is not a valid date. Try 2026-08-17.");
        day = default;
        return false;
    }

    day = parsed.Date;
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
    var history = Taste<History>.Bite().Flavour;
    Output.Blank();
    if (history is not null)
    {
        foreach (var entry in history.Days.Keys)
        {
            Output.Date(entry);
        }
    }
    Output.Blank();
    return 0;
}

int Clear(string[] args)
{
    switch (args)
    {
        case []:
            Output.Error("Specify whether to clear 'history' or 'today'.");
            return 1;

        case ["today" or "t", ..]:
            today.Flavour?.Tasks.Clear();
            return 0;

        case ["history" or "h", .. var rest]:
            var history = Taste<History>.Bite();

            if (rest is [var day, ..])
            {
                if (!TryParseDay(day, out var date))
                {
                    return 1;
                }
                history.Flavour?.Days.Remove(date);
            }
            else
            {
                history.Flavour?.Days.Clear();
            }
            history.Savor();
            return 0;

        default:
            Output.Error($"'{args[0]}' is not something to clear. Specify 'history' or 'today'.");
            return 1;
    }
}

// Feeds the shell completion script. Deliberately absent from Usage: it is for
// scripts, not people. Output is raw, one candidate per line, never themed.
int Complete(string[] args)
{
    switch (args)
    {
        case ["commands", ..]:
            foreach (var (name, _, _) in commands)
            {
                Console.WriteLine(name);
            }
            return 0;

        // The tasks `today end` would accept right now: the ones still running.
        case ["end", ..]:
            if (today.Flavour is { } running)
            {
                foreach (var task in running.Tasks.Where(t => t.End is null))
                {
                    Console.WriteLine(task.What);
                }
            }
            return 0;

        // `today rm` accepts anything on the day, finished or not. A name can repeat, so
        // offer each one once.
        case ["rm", ..]:
            if (today.Flavour is { } day)
            {
                foreach (var name in day.Tasks.Select(t => t.What).Distinct())
                {
                    Console.WriteLine(name);
                }
            }
            return 0;

        default:
            return 1;
    }
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

// Printed when there is nothing to act on, so it goes to stderr at exit 1 -- unlike `help`,
// which is what the user asked for.
int Usage()
{
    Output.Error($"Available commands are {string.Join(" ", commands.Select(c => $"\'{c.Name}\'"))}.");
    Output.Error("Run 'today help' for what each one takes.");
    return 1;
}

int NotACommand(string command)
{
    Output.Error($"'{command}' is not a command.");
    Output.Blank();
    return Usage();
}

int Start(string[] args)
{
    var flags = args.Where(IsFlag).ToArray();
    var positional = args.Where(a => !IsFlag(a)).ToArray();

    if (positional is [])
    {
        Output.Error("You must specify what you want to start doing.");
        return 1;
    }

    var what = positional[0];
    var when = DateTime.Now;
    if (positional is [_, var time, ..] && !TryParseWhen(time, out when))
    {
        return 1;
    }

    today.Flavour ??= new Today.Today();

    if (flags.Contains("-c"))
    {
        today.Flavour.EndAll(when);
    }

    return today.Flavour.Start(what, when) ? 0 : 1;
}

int End(string[] args)
{
    if (today.Flavour is null)
    {
        Output.Error("No task started yet.");
        return 1;
    }

    var positional = args.Where(a => !IsFlag(a)).ToArray();

    var what = positional is [var first, ..] ? first : null;

    var when = DateTime.Now;
    if (positional is [_, var time, ..] && !TryParseWhen(time, out when))
    {
        return 1;
    }

    return today.Flavour.End(what, when) ? 0 : 1;
}

// `did <what> <duration>` records something that ran for that long and ended now.
int Did(string[] args)
{
    var positional = args.Where(a => !IsFlag(a)).ToArray();

    if (positional is not [var what, var length, ..])
    {
        Output.Error("Say what you did and how long it took, as in: today did standup 15m");
        return 1;
    }

    if (!TryParseDuration(length, out var duration))
    {
        return 1;
    }

    var now = DateTime.Now;
    var start = now - duration;

    // The other way a task can land outside the day it is logged on. It hides better than a
    // date typed by hand does, since the times reported back are only ever HH:mm.
    if (start.Date != now.Date)
    {
        Output.Error($"{length} reaches back past midnight, and a day only holds its own tasks.");
        return 1;
    }

    today.Flavour ??= new Today.Today();

    return today.Flavour.Did(what, start, now) ? 0 : 1;
}

// `rm <what>` deletes a task from today, for the ones logged by mistake.
int Remove(string[] args)
{
    var positional = args.Where(a => !IsFlag(a)).ToArray();

    if (positional is not [var what, ..])
    {
        Output.Error("Say what to remove, as in: today rm standup");
        return 1;
    }

    if (today.Flavour is null)
    {
        Output.Error($"You have not done {what} today.");
        return 1;
    }

    return today.Flavour.Remove(what) ? 0 : 1;
}

int Show(string[] args)
{
    var day = today.Flavour;
    var isToday = true;

    if (args is [var wanted, ..])
    {
        if (!TryParseDay(wanted, out var date))
        {
            return 1;
        }

        if (Taste<History>.Bite().Flavour?.Days.TryGetValue(date, out var historicalDay) ?? false)
        {
            day = historicalDay;
            isToday = false;
        }
        else
        {
            Output.Error($"No history for {date:dd-MM-yyyy}.");
            return 1;
        }
    }

    if (day is null || day.Tasks.Count is 0)
    {
        Output.Error("Nothing was done this day...");
        return 1;
    }

    Output.Header(isToday ? $"Today {day.Date:dd MMM}" : $"{day.Date:dd MMM yyyy}");
    Output.Blank();
    // A day out of history has no "now" to draw an unfinished task up to.
    Output.Chart(day.Tasks, isToday ? DateTime.Now : day.Tasks.Max(t => t.End ?? t.Start));
    Output.Blank();
    return 0;
}

int ThemeCommand(string[] args)
{
    var taste = Taste<Theme>.Bite();
    var theme = taste.Flavour ??= new Theme();

    var flags = args.Where(IsFlag).ToArray();
    var positional = args.Where(a => !IsFlag(a)).ToArray();

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
            taste.Savor();
            Output.Success($"Set {element} to {theme.Get(element)}.");
            return 0;

        case ["reset"]:
            taste.Flavour = new Theme();
            taste.Savor();
            Output.Success("Theme reset to the defaults.");
            return 0;

        case ["reset", var element]:
            if (new Theme().Get(element) is not { } fallback)
            {
                Output.Error($"'{element}' cannot be themed. Try: {string.Join(", ", Theme.ElementNames)}");
                return 1;
            }

            theme.Set(element, fallback);
            taste.Savor();
            Output.Success($"Reset {element} to {fallback}.");
            return 0;

        default:
            Output.Error("Usage: today theme [show | set <element> <color> [--bold] [--dim] [--italics] [--underline] | reset [element]]");
            return 1;
    }
}
