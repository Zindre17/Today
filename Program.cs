/*
today
    - start x
    - end x
    - show
*/

using Taste;
using Today;

if (args.Length < 1)
{
    return 0;
}

var today = Taste<Today.Today>.Bite();

try
{
    return args[0] switch
    {
        "start" => Start(args[1..]),
        "end" => End(args[1..]),
        "show" => Show(args[1..]),
        "clear" => Clear(args[1..]),
        "list" => ListHistory(args[1..]),
        var c => NotACommand(c)
    };
}
finally
{
    today.Savor();
}

int ListHistory(string[] strings)
{
    var history = Taste<History>.Bite().Flavour;
    Console.WriteLine();
    foreach (var entry in history?.Days.Keys ?? Enumerable.Empty<DateTime>())
    {
        Console.WriteLine($"{entry:dd MM}");
    }
    Console.WriteLine();
    return 0;
}

int Clear(string[] args)
{
    if (args.Length < 1)
    {
        Console.WriteLine("Specify wather to clear 'history' or 'today'.");
        return 1;
    }
    switch (args[0])
    {
        case "today" or "t":
            today.Flavour?.Tasks.Clear();
            break;
        case "history" or "h":
            var history = Taste<History>.Bite();

            if (args.Length > 1)
            {
                if (DateTime.TryParse(args[1], out var date))
                {
                    history.Flavour?.Days.Remove(date);
                }
            }
            else
            {
                history.Flavour?.Days.Clear();
            }
            history.Savor();
            break;
    }
    return 0;
}

int NotACommand(string command)
{
    Console.WriteLine($"'{command}' is not a command.");
    Console.WriteLine();
    Console.WriteLine("Available commands are 'start' 'end' 'show'.");
    return 0;
}

int Start(string[] args)
{
    if (args.Length < 1)
    {
        Console.WriteLine("You must specify what you want to start doing.");
        return 1;
    }

    var what = args[0];
    var when = DateTime.Now;
    if (args.Length > 1)
    {
        if (DateTime.TryParse(args[1], out var time))
        {
            when = time;
        }
    }

    if (today.Flavour is null)
    {
        today.Flavour = new Today.Today();
    }
    else if (today.Flavour.Date.Date != DateTime.Now.Date)
    {
        var history = Taste<History>.Bite();
        if (history.Flavour is null)
        {
            history.Flavour = new History();
        }
        history.Flavour.Days.Add(today.Flavour.Date, today.Flavour);
        history.Savor();

        today.Flavour = new Today.Today();
    }

    if (args.Contains("-c"))
    {
        today.Flavour.EndAll(when);
    }

    today.Flavour.Start(what, when);
    return 0;
}

int End(string[] args)
{
    if (today.Flavour is null)
    {
        Console.WriteLine("No task started yet.");
        return 1;
    }

    string? what = null;

    if (args.Length > 0)
    {
        what = args[0];
    }

    var when = DateTime.Now;
    if (args.Length > 1)
    {
        if (DateTime.TryParse(args[1], out var time))
        {
            when = time;
        }
    }

    today.Flavour.End(what, when);
    return 0;
}

int Show(string[] args)
{
    var day = today.Flavour;
    if (args.Length > 0)
    {
        if (DateTime.TryParse(args[0], out var date))
        {
            if (Taste<History>.Bite().Flavour?.Days.TryGetValue(date, out var historicalDay) ?? false)
            {
                day = historicalDay;
            }
            else
            {
                Console.WriteLine($"No history for {date:dd-MM-yyyy}.");
                return 1;
            }
        }
    }
    if (day is null || day.Tasks.Count is 0)
    {
        Console.WriteLine("Nothing was done this day...");
        return 1;
    }

    Console.WriteLine($"Today {day.Date:dd MMM}");
    Console.WriteLine();
    foreach (var task in day.Tasks ?? Enumerable.Empty<Doing>())
    {
        Console.WriteLine($"    {task}");
    }
    Console.WriteLine();
    return 0;
}

