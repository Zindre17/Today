using Taste;
using Today;

var today = Taste<Today.Today>.Bite();

try
{
    RollOverIfNewDay();

    return args switch
    {
        ["start", .. var rest] => Start(rest),
        ["end", .. var rest] => End(rest),
        ["show", .. var rest] => Show(rest),
        ["clear", .. var rest] => Clear(rest),
        ["list", ..] => ListHistory(),
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

    Console.WriteLine($"'{arg}' is not a valid date or time.");
    return false;
}

bool IsFlag(string arg) => arg.StartsWith('-');

int ListHistory()
{
    var history = Taste<History>.Bite().Flavour;
    Console.WriteLine();
    if (history is not null)
    {
        foreach (var entry in history.Days.Keys)
        {
            Console.WriteLine($"{entry:yyyy-MM-dd}");
        }
    }
    Console.WriteLine();
    return 0;
}

int Clear(string[] args)
{
    switch (args)
    {
        case []:
            Console.WriteLine("Specify whether to clear 'history' or 'today'.");
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
            Console.WriteLine($"'{args[0]}' is not something to clear. Specify 'history' or 'today'.");
            return 1;
    }
}

int Usage()
{
    Console.WriteLine("Available commands are 'start' 'end' 'show' 'clear' 'list'.");
    return 1;
}

int NotACommand(string command)
{
    Console.WriteLine($"'{command}' is not a command.");
    Console.WriteLine();
    return Usage();
}

int Start(string[] args)
{
    var flags = args.Where(IsFlag).ToArray();
    var positional = args.Where(a => !IsFlag(a)).ToArray();

    if (positional is [])
    {
        Console.WriteLine("You must specify what you want to start doing.");
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
        Console.WriteLine("No task started yet.");
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
            Console.WriteLine($"No history for {date:dd-MM-yyyy}.");
            return 1;
        }
    }

    if (day is null || day.Tasks.Count is 0)
    {
        Console.WriteLine("Nothing was done this day...");
        return 1;
    }

    Console.WriteLine(isToday ? $"Today {day.Date:dd MMM}" : $"{day.Date:dd MMM yyyy}");
    Console.WriteLine();
    foreach (var task in day.Tasks)
    {
        Console.WriteLine($"    {task}");
    }
    Console.WriteLine();
    return 0;
}
