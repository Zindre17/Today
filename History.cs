namespace Today;

public record History
{
    public Dictionary<DateTime, Day> Days { get; init; } = [];
}
