using Fansi;
using Taste;

namespace Today;

/// <summary>
///     Every write to the console goes through here, so the theme is applied in one place.
/// </summary>
public static class Output
{
    private const int NameWidth = 20;

    private const int LabelWidth = 5;

    // Four spaces, the name column, then two spaces before the chart area.
    private static readonly string Indent = new(' ', 4 + NameWidth + 2);

    // Fansi always emits escape sequences, so the usual opt-outs are our job. An
    // OutputFormat with no colors set emits none, which is what plain mode relies on.
    private static readonly bool Plain =
        Environment.GetEnvironmentVariable("NO_COLOR") is not (null or "") || Console.IsOutputRedirected;

    private static Theme? current;

    public static Theme Current => current ??= Taste<Theme>.Bite().Flavour ?? new Theme();

    public static string ColorNames => string.Join(", ", Enum.GetNames<BasicColor>());

    /// <summary>
    ///     Resolves a color name, accepting the correctly spelled BrightBlack as well as
    ///     Fansi's BrigthBlack.
    /// </summary>
    public static bool TryGetColor(string name, out BasicColor color) =>
        Enum.TryParse(
            string.Equals(name, "BrightBlack", StringComparison.OrdinalIgnoreCase) ? nameof(BasicColor.BrigthBlack) : name,
            ignoreCase: true,
            out color);

    public static void Blank() => Console.WriteLine();

    public static void Header(string text) => Console.WriteLine(Apply(Current.Header, text));

    public static void Success(string text) => Console.WriteLine(Apply(Current.Success, text));

    public static void Error(string text) => Console.WriteLine(Apply(Current.Error, text));

    public static void Date(DateTime date) => Console.WriteLine(Apply(Current.Date, $"{date:yyyy-MM-dd}"));

    /// <summary>
    ///     Draws the day as a horizontal Gantt chart: one row per task, time along the x axis.
    /// </summary>
    /// <param name="tasks">The tasks to plot.</param>
    /// <param name="reference">
    ///     The moment an unfinished task is drawn up to — now for the current day, and the last
    ///     thing that happened for a day out of history.
    /// </param>
    public static void Chart(IReadOnlyList<Doing> tasks, DateTime reference)
    {
        var theme = Current;
        var width = ChartWidth;
        var (from, to) = Window(tasks, reference);
        var span = (to - from).TotalMinutes;

        // Time -> column. The window is whole hours, so both edges land cleanly.
        int Column(DateTime moment) =>
            Math.Clamp((int)Math.Round((moment - from).TotalMinutes / span * width), 0, width);

        Console.WriteLine($"{Indent}{Apply(theme.Axis, Axis(from, to, width, Column))}");

        foreach (var task in tasks)
        {
            var running = task.End is null;
            var style = running ? theme.Running : theme.Bar;

            var start = Column(task.Start);
            var end = Column(Finish(task, reference));

            var bar = new char[width];
            Array.Fill(bar, ' ');
            // A task shorter than one column still deserves to be visible, including one that
            // starts on the closing edge of the window.
            var first = Math.Min(start, width - 1);
            for (var i = first; i < Math.Max(end, first + 1) && i < width; i++)
            {
                bar[i] = running ? '▓' : '█';
            }

            var name = Format(running ? theme.Running : theme.Task, NameWidth).ApplyToText(task.What);
            Console.WriteLine($"    {name}  {Apply(style, new string(bar).TrimEnd())}");
        }

        var total = tasks.Aggregate(TimeSpan.Zero, (sum, task) => sum + (Finish(task, reference) - task.Start));
        Console.WriteLine($"    {Format(new ThemeStyle(), NameWidth).ApplyToText("total")}  " +
            $"{Apply(theme.Duration, $"{(int)total.TotalHours} Hours {total.Minutes} Minutes")}");
    }

    /// <summary>
    ///     One row of `today theme`: the element, its settings, and a sample rendered in it.
    /// </summary>
    public static void Sample(string element, ThemeStyle style)
    {
        // Padding is measured on unstyled text; the styled sample goes last so the escape
        // sequences can never be counted as width.
        var name = Format(new ThemeStyle(), NameWidth).ApplyToText(element);
        var settings = Format(new ThemeStyle(), 26).ApplyToText(style.ToString());

        Console.WriteLine($"    {name}  {settings}  {Apply(style, "The quick brown fox")}");
    }

    /// <summary>
    ///     When a task stops. An unfinished one runs up to <paramref name="reference" />, but never
    ///     backwards: a task started after that moment (clock change, or a time given by hand)
    ///     would otherwise measure as negative.
    /// </summary>
    private static DateTime Finish(Doing task, DateTime reference) =>
        task.End ?? (reference > task.Start ? reference : task.Start);

    /// <summary>
    ///     The whole hours spanning every task, so the axis labels land on the hour.
    /// </summary>
    private static (DateTime From, DateTime To) Window(IReadOnlyList<Doing> tasks, DateTime reference)
    {
        var earliest = tasks.Min(t => t.Start);
        var latest = tasks.Max(t => Finish(t, reference));

        var from = earliest.Date.AddHours(earliest.Hour);
        var to = latest.Date.AddHours(latest.Hour);
        if (to < latest)
        {
            to = to.AddHours(1);
        }

        // A day where everything happened inside one hour still needs a scale.
        return (from, to > from ? to : from.AddHours(1));
    }

    /// <summary>
    ///     The x axis: HH:mm labels at whole-hour ticks, spaced so they never collide.
    /// </summary>
    private static string Axis(DateTime from, DateTime to, int width, Func<DateTime, int> column)
    {
        // Labels are left-aligned on their own tick, so the closing one overhangs the chart.
        // ChartWidth reserves the room for it.
        var axis = new char[width + LabelWidth];
        Array.Fill(axis, ' ');

        // The window end anchors the axis: it states where the chart stops, so it is placed
        // first and intermediate ticks give way to it rather than the other way round.
        var end = column(to);
        $"{to:HH:mm}".CopyTo(0, axis, end, LabelWidth);

        var step = TickStep((to - from).TotalMinutes, width);
        var occupiedUntil = -1;

        for (var tick = from; tick < to; tick = tick.AddMinutes(step))
        {
            var at = column(tick);

            if (at <= occupiedUntil || at + LabelWidth > end)
            {
                continue;
            }

            $"{tick:HH:mm}".CopyTo(0, axis, at, LabelWidth);
            occupiedUntil = at + LabelWidth;
        }

        return new string(axis).TrimEnd();
    }

    /// <summary>
    ///     The smallest whole-hour-friendly interval whose labels still fit side by side.
    /// </summary>
    private static int TickStep(double totalMinutes, int width)
    {
        int[] candidates = [15, 30, 60, 120, 180, 240, 360, 720];

        foreach (var step in candidates)
        {
            if (step / totalMinutes * width >= 7)
            {
                return step;
            }
        }

        return 1440;
    }

    // The trailing axis label overhangs the chart, and the last column of a terminal is left
    // alone so nothing wraps.
    private static int ChartWidth => Math.Clamp(TerminalWidth - Indent.Length - LabelWidth - 1, 20, 120);

    private static int TerminalWidth
    {
        get
        {
            try
            {
                return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            }
            catch (IOException)
            {
                // No terminal attached (piped or redirected).
                return 80;
            }
        }
    }

    private static string Apply(ThemeStyle style, string text) => Format(style).ApplyToText(text);

    private static OutputFormat Format(ThemeStyle style, int? width = null)
    {
        var format = new OutputFormat
        {
            Width = width,
            AddEllipsisToOverflow = width is not null,
        };

        if (Plain)
        {
            return format;
        }

        return format with
        {
            Foreground = TryGetColor(style.Color, out var color) ? color : null,
            Bold = style.Bold ? true : null,
            Dim = style.Dim ? true : null,
            Italics = style.Italics ? true : null,
            Underline = style.Underline ? true : null,
        };
    }
}
