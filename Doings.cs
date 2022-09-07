namespace Today;

public record Doing
{
    public Doing(string what, DateTime start) => (What, Start) = (what, start);

    public DateTime Start { get; set; }
    public DateTime? End { get; set; }

    public string What { get; set; }

    public override string ToString()
    {
        var timeSpan = End?.Subtract(Start);
        return $"{What[0..Math.Min(20, What.Length)],-20}    {Start:HH:mm}{(End is not null ? $" - {End:HH:mm}    {timeSpan!.Value.Hours} Hours {timeSpan!.Value.Minutes} Minutes" : "")}";
    }
}
