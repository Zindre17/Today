namespace Today;

public record History
{
    public Dictionary<DateOnly, Day> Days { get; init; } = [];
}
