namespace Today;

/// <summary>
///     One stretch of work. The times are <see cref="TimeOnly" /> because a task belongs to the
///     day it is logged on — that day is <see cref="Day.Date" />, so a date carried here as well
///     would be a second copy of it with nothing keeping the two in step.
/// </summary>
public record Doing(string What, TimeOnly Start)
{
    public TimeOnly? End { get; set; }

    // Rendering lives in Output, which owns the theme and the column widths.
}
