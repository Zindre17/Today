namespace Today;

public record Doing
{
    public Doing(string what, DateTime start) => (What, Start) = (what, start);

    public DateTime Start { get; set; }
    public DateTime? End { get; set; }

    public string What { get; set; }

    public override string ToString()
    {
        var name = $"{What[0..Math.Min(20, What.Length)],-20}";
        if (End is null)
        {
            return $"{name}    {Start:HH:mm}";
        }

        var timeSpan = End.Value.Subtract(Start);
        return $"{name}    {Start:HH:mm} - {End:HH:mm}    {(int)timeSpan.TotalHours} Hours {timeSpan.Minutes} Minutes";
    }
}
