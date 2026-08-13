using Fansi;
using Taste;

namespace Today;

/// <summary>
///     Every write to the console goes through here, so the theme is applied in one place.
/// </summary>
public static class Output
{
    private const int NameWidth = 20;

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

    public static void Task(Doing task)
    {
        var theme = Current;
        var running = task.End is null;

        var name = Format(running ? theme.Running : theme.Task, NameWidth).ApplyToText(task.What);
        var time = Apply(theme.Time, running ? $"{task.Start:HH:mm}" : $"{task.Start:HH:mm} - {task.End:HH:mm}");

        var duration = "";
        if (task.End is { } end)
        {
            var span = end.Subtract(task.Start);
            duration = "    " + Apply(theme.Duration, $"{(int)span.TotalHours} Hours {span.Minutes} Minutes");
        }

        Console.WriteLine($"    {name}    {time}{duration}");
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
