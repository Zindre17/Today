using System.Reflection;

namespace Today;

/// <summary>
///     The styles applied to each part of the output. Persisted as today.theme.json.
/// </summary>
/// <remarks>
///     Colors are stored by name rather than as a <c>Fansi.BasicColor</c> so the file stays
///     readable, and so an unknown name degrades to the default instead of failing to load.
///     Every element here must be read by <see cref="Output" />: `theme show` lists them
///     reflectively, so one that nothing renders is a setting the user can change to no effect.
///     Dropping one is safe — its now-unknown key in an existing file is ignored on load.
/// </remarks>
public record Theme
{
    public ThemeStyle Header { get; set; } = new() { Color = "BrightWhite", Bold = true };

    public ThemeStyle Task { get; set; } = new();

    public ThemeStyle Running { get; set; } = new() { Color = "BrightGreen", Bold = true };

    public ThemeStyle Bar { get; set; } = new() { Color = "Cyan" };

    public ThemeStyle Axis { get; set; } = new() { Color = "BrightBlack" };

    public ThemeStyle Duration { get; set; } = new() { Color = "BrightBlack" };

    public ThemeStyle Date { get; set; } = new() { Color = "Yellow" };

    public ThemeStyle Success { get; set; } = new() { Color = "Green" };

    public ThemeStyle Error { get; set; } = new() { Color = "BrightRed" };

    public static IEnumerable<string> ElementNames => Elements.Select(p => p.Name.ToLowerInvariant());

    private static IEnumerable<PropertyInfo> Elements =>
        typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(ThemeStyle));

    public ThemeStyle? Get(string element) => Find(element)?.GetValue(this) as ThemeStyle;

    public bool Set(string element, ThemeStyle style)
    {
        if (Find(element) is not { } property)
        {
            return false;
        }

        property.SetValue(this, style);
        return true;
    }

    private static PropertyInfo? Find(string element) =>
        Elements.FirstOrDefault(p => string.Equals(p.Name, element, StringComparison.OrdinalIgnoreCase));
}

public record ThemeStyle
{
    public string Color { get; set; } = "Default";

    public bool Bold { get; set; }

    public bool Dim { get; set; }

    public bool Italics { get; set; }

    public bool Underline { get; set; }

    public override string ToString()
    {
        string?[] parts = [Color, Bold ? "bold" : null, Dim ? "dim" : null, Italics ? "italics" : null, Underline ? "underline" : null];
        return string.Join(" ", parts.Where(p => p is not null));
    }
}
