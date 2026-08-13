namespace Today;

public record Doing(string What, DateTime Start)
{
    public DateTime? End { get; set; }

    // Rendering lives in Output, which owns the theme and the column widths.
}
