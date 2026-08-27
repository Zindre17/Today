using System.Text;
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

    // Wide enough for the longest span a day can hold, "23h59m".
    private const int TimeWidth = 6;

    // The widest "command [args]" in the help, so the descriptions line up beneath each other.
    private const int SyntaxWidth = 30;

    // Four spaces, the name column, the duration column, then two spaces before the chart area.
    private static readonly string Indent = new(' ', 4 + NameWidth + 2 + TimeWidth + 2);

    // Fansi always emits escape sequences, so the usual opt-outs are our job. An
    // OutputFormat with no colors set emits none, which is what plain mode relies on.
    private static readonly bool NoColor = Environment.GetEnvironmentVariable("NO_COLOR") is not (null or "");

    // The two streams are redirected independently -- `today show > day.txt` leaves stderr on
    // the terminal -- so each decides its own styling rather than sharing one answer.
    private static bool Plain => NoColor || Console.IsOutputRedirected;

    private static bool PlainError => NoColor || Console.IsErrorRedirected;

    public static Theme Current => Cook.Serve<Theme>();

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

    /// <summary>
    ///     Complaints go to stderr, so `today show > day.txt` captures the chart and not the
    ///     reason there wasn't one, and `2>/dev/null` silences the reason and not the chart.
    /// </summary>
    public static void Error(string text) =>
        Console.Error.WriteLine(Format(Current.Error, plain: PlainError).ApplyToText(text));

    /// <summary>
    ///     Something worth knowing that is not a failure: the command carries on and its exit
    ///     code is unchanged. Shares stderr with <see cref="Error" /> for the same reason —
    ///     it is commentary, not part of what the command was asked to produce — but styles
    ///     itself apart, so it is not mistaken for the command having gone wrong.
    /// </summary>
    public static void Warn(string text) =>
        Console.Error.WriteLine(Format(Current.Warning, plain: PlainError).ApplyToText(text));

    /// <summary>
    ///     One line of `today help`: the command with its arguments, then what it does.
    /// </summary>
    public static void Command(string syntax, string does) =>
        Console.WriteLine($"    {Format(Current.Task, SyntaxWidth).ApplyToText(syntax)}  {does}");

    public static void Date(DateOnly date) => Console.WriteLine(Apply(Current.Date, $"{date:yyyy-MM-dd}"));

    /// <summary>
    ///     Draws the day as a horizontal Gantt chart: one row per task, time along the x axis.
    /// </summary>
    /// <param name="tasks">The tasks to plot.</param>
    /// <param name="reference">
    ///     The moment an unfinished task is drawn up to — now for the current day, and the last
    ///     thing that happened for a day out of history.
    /// </param>
    /// <remarks>
    ///     Every measurement below is a <see cref="TimeSpan" /> since midnight rather than a
    ///     <see cref="TimeOnly" />, for two reasons that both bite. The window's closing edge
    ///     rounds up to the next whole hour and so can land on 24:00, which is a real position on
    ///     the axis and not a <c>TimeOnly</c>; and <c>TimeOnly</c> subtraction is circular, so a
    ///     span measured backwards comes back as roughly 23 hours instead of a negative — turning
    ///     an obviously broken number into a plausible one. A <c>TimeSpan</c> holds both, and
    ///     subtracts in a straight line.
    /// </remarks>
    public static void Chart(IReadOnlyList<Doing> tasks, TimeOnly reference)
    {
        var theme = Current;
        var width = ChartWidth;
        var (from, to) = Window(tasks, reference);
        var span = (to - from).TotalMinutes;

        // Time -> column. The window is whole hours, so both edges land cleanly.
        int Column(TimeSpan moment) =>
            Math.Clamp((int)Math.Round((moment - from).TotalMinutes / span * width), 0, width);

        Console.WriteLine($"{Indent}{Apply(theme.Axis, Axis(from, to, width, Column))}");

        // One row per name, not per entry: `did` allows a name that is already running, and a
        // task picked up again through the day is the same task. Every stretch of it is drawn on
        // that one row, and the times add up.
        foreach (var task in tasks.GroupBy(t => t.What))
        {
            var running = task.Any(t => t.End is null);
            var took = Took(task, reference);

            var bar = new char[width];
            Array.Fill(bar, ' ');

            foreach (var stretch in task)
            {
                var finish = Finish(stretch, reference);

                // A stretch shorter than one column still deserves to be visible, including one
                // that starts on the closing edge of the window.
                var start = Math.Min(Column(stretch.Start.ToTimeSpan()), width - 1);
                var end = Column(finish);
                for (var i = start; i < Math.Max(end, start + 1) && i < width; i++)
                {
                    // Stretches of one name can overlap -- `did` does not mind logging over
                    // something still running. Where they do, the running one wins: that time is
                    // still accruing, and the row should say so.
                    if (bar[i] is not '▓')
                    {
                        bar[i] = stretch.End is null ? '▓' : '█';
                    }
                }
            }

            var name = Format(running ? theme.Running : theme.Task, NameWidth).ApplyToText(task.Key);

            Console.WriteLine($"    {name}  {Time(theme.Duration, took)}  {Bar(theme, bar)}");
        }

        Total(theme, Took(tasks, reference));
    }

    /// <summary>
    ///     The same day without the bars: one row per name with what it came to, and the total.
    ///     What `show --no-chart` prints, for when the question is how long rather than when.
    /// </summary>
    public static void Rows(IReadOnlyList<Doing> tasks, TimeOnly reference)
    {
        var theme = Current;

        foreach (var task in tasks.GroupBy(t => t.What))
        {
            Row(theme, task.Key, Took(task, reference), task.Any(t => t.End is null));
        }

        // Summed over every entry rather than over the rows above. It is the same number, and
        // stays the same number if the grouping ever changes.
        Total(theme, Took(tasks, reference));
    }

    /// <summary>
    ///     Several days at once: one row per task with what it came to across all of them, and
    ///     what they came to together. Takes totals rather than tasks, because a stretch only
    ///     means something measured against the day it happened on, and those days have already
    ///     been reckoned with by the time this is called.
    /// </summary>
    public static void Summary(IEnumerable<(string Name, TimeSpan Took, bool Running)> rows)
    {
        var theme = Current;
        var total = TimeSpan.Zero;

        // One pass: rows may be a lazy projection, and enumerating it twice to total it would
        // reckon every day up a second time.
        foreach (var (name, took, running) in rows)
        {
            Row(theme, name, took, running);
            total += took;
        }

        Total(theme, total);
    }

    /// <summary>
    ///     What a set of stretches came to. The one definition, so no two renderings of the same
    ///     work can disagree about a number.
    /// </summary>
    public static TimeSpan Took(IEnumerable<Doing> stretches, TimeOnly reference) =>
        stretches.Aggregate(TimeSpan.Zero, (sum, s) => sum + (Finish(s, reference) - s.Start.ToTimeSpan()));

    /// <summary>
    ///     One named row, in the columns everything else lines up in.
    /// </summary>
    private static void Row(Theme theme, string name, TimeSpan took, bool running) =>
        Console.WriteLine($"    {Format(running ? theme.Running : theme.Task, NameWidth).ApplyToText(name)}  {Time(theme.Duration, took)}");

    /// <summary>
    ///     The closing row. It sits in the same column as the times above it, so it reads as
    ///     their sum.
    /// </summary>
    private static void Total(Theme theme, TimeSpan total) =>
        Console.WriteLine($"    {Format(new ThemeStyle(), NameWidth).ApplyToText("total")}  {Time(theme.Duration, total)}");

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
    ///     One row's bar. A row can hold finished and running stretches at once, so it is emitted a
    ///     run at a time and each keeps its own style; the gaps between them stay unstyled.
    /// </summary>
    private static string Bar(Theme theme, char[] cells)
    {
        var last = Array.FindLastIndex(cells, c => c is not ' ') + 1;
        var bar = new StringBuilder();

        for (var i = 0; i < last;)
        {
            var run = i;
            while (run < last && cells[run] == cells[i])
            {
                run++;
            }

            var text = new string(cells[i], run - i);
            bar.Append(cells[i] is ' ' ? text : Apply(cells[i] is '▓' ? theme.Running : theme.Bar, text));
            i = run;
        }

        return bar.ToString();
    }

    /// <summary>
    ///     A span in the duration column: right-aligned, and spelled the way `did` accepts it, so
    ///     what the chart reports can be typed straight back in.
    /// </summary>
    private static string Time(ThemeStyle style, TimeSpan span) =>
        Format(style, TimeWidth, TextAlignment.Right).ApplyToText(Spell(span));

    private static string Spell(TimeSpan span) => span switch
    {
        { TotalMinutes: < 1 } => $"{span.Seconds}s",
        { TotalHours: < 1 } => $"{span.Minutes}m",
        _ => $"{(int)span.TotalHours}h{span.Minutes:00}m",
    };

    /// <summary>
    ///     When a task stops. An unfinished one runs up to <paramref name="reference" />, but never
    ///     backwards: a task started after that moment (clock change, or a time given by hand)
    ///     would otherwise measure as negative.
    /// </summary>
    private static TimeSpan Finish(Doing task, TimeOnly reference)
    {
        if (task.End is { } end)
        {
            return end.ToTimeSpan();
        }

        var start = task.Start.ToTimeSpan();
        var upTo = reference.ToTimeSpan();
        return upTo > start ? upTo : start;
    }

    /// <summary>
    ///     A moment on the day as HH:mm. Written out by hand rather than formatted, because the
    ///     window's closing edge can be 24:00 and a <see cref="TimeSpan" /> of exactly one day
    ///     formats its hours component as 00.
    /// </summary>
    private static string Clock(TimeSpan moment) => $"{(int)moment.TotalHours:00}:{moment.Minutes:00}";

    /// <summary>
    ///     The whole hours spanning every task, so the axis labels land on the hour.
    /// </summary>
    private static (TimeSpan From, TimeSpan To) Window(IReadOnlyList<Doing> tasks, TimeOnly reference)
    {
        var earliest = tasks.Min(t => t.Start.ToTimeSpan());
        var latest = tasks.Max(t => Finish(t, reference));

        // Both sit inside the day, so the hours component is the whole hour they fall in.
        var from = new TimeSpan(earliest.Hours, 0, 0);
        var to = new TimeSpan(latest.Hours, 0, 0);
        if (to < latest)
        {
            // A day whose last moment is past 23:00 rounds up to 24:00, which is why the window
            // is measured rather than clocked.
            to += TimeSpan.FromHours(1);
        }

        // A day where everything happened inside one hour still needs a scale.
        return (from, to > from ? to : from + TimeSpan.FromHours(1));
    }

    /// <summary>
    ///     The x axis: HH:mm labels at whole-hour ticks, spaced so they never collide.
    /// </summary>
    private static string Axis(TimeSpan from, TimeSpan to, int width, Func<TimeSpan, int> column)
    {
        // Labels are left-aligned on their own tick, so the closing one overhangs the chart.
        // ChartWidth reserves the room for it.
        var axis = new char[width + LabelWidth];
        Array.Fill(axis, ' ');

        // The window end anchors the axis: it states where the chart stops, so it is placed
        // first and intermediate ticks give way to it rather than the other way round.
        var end = column(to);
        Clock(to).CopyTo(0, axis, end, LabelWidth);

        var step = TickStep((to - from).TotalMinutes, width);
        var occupiedUntil = -1;

        for (var tick = from; tick < to; tick += TimeSpan.FromMinutes(step))
        {
            var at = column(tick);

            if (at <= occupiedUntil || at + LabelWidth > end)
            {
                continue;
            }

            Clock(tick).CopyTo(0, axis, at, LabelWidth);
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

    /// <param name="plain">Which stream's answer to use; null means stdout's.</param>
    private static OutputFormat Format(
        ThemeStyle style, int? width = null, TextAlignment? alignment = null, bool? plain = null)
    {
        var format = new OutputFormat
        {
            Width = width,
            Alignment = alignment,
            AddEllipsisToOverflow = width is not null,
        };

        if (plain ?? Plain)
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
