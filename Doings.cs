namespace Today;

public record Doing(string What, DateTime Start)
{
    public DateTime? End { get; set; }

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
