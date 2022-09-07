namespace Today;

public record History
{
    public Dictionary<DateTime, Today> Days { get; init; } = new();
}
