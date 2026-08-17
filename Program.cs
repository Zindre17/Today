using Taste;
using Today;

var today = Taste<Today.Today>.Bite();

string[] commands = ["start", "end", "did", "show", "clear", "list", "theme"];

try
{
    RollOverIfNewDay();

    return args switch
    {
        ["start", .. var rest] => Start(rest),
        ["end", .. var rest] => End(rest),
        ["did", .. var rest] => Did(rest),
        ["show", .. var rest] => Show(rest),
        ["clear", .. var rest] => Clear(rest),
        ["list", ..] => ListHistory(),
        ["theme", .. var rest] => ThemeCommand(rest),
        ["complete", .. var rest] => Complete(rest),
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

bool TryParseWhen(string arg, out DateTime when)
{
    if (DateTime.TryParse(arg, out when))
    {
        return true;
    }

    Output.Error($"'{arg}' is not a valid date or time.");
    return false;
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
                if (!TryParseWhen(day, out var date))
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
            foreach (var command in commands)
            {
                Console.WriteLine(command);
            }
            return 0;

        // The tasks `today end` would accept right now: the ones still running.
        case ["end", ..]:
            if (today.Flavour is { } day)
            {
                foreach (var task in day.Tasks.Where(t => t.End is null))
                {
                    Console.WriteLine(task.What);
                }
            }
            return 0;

        default:
            return 1;
    }
}

int Usage()
{
    Output.Error($"Available commands are {string.Join(" ", commands.Select(c => $"\'{c}\'"))}.");
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
    today.Flavour ??= new Today.Today();

    return today.Flavour.Did(what, now - duration, now) ? 0 : 1;
}

int Show(string[] args)
{
    var day = today.Flavour;
    var isToday = true;

    if (args is [var wanted, ..])
    {
        if (!TryParseWhen(wanted, out var date))
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
