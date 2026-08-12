namespace Today;

public class Today
{
    public DateTime Date { get; set; } = DateTime.Now.Date;

    public List<Doing> Tasks { get; set; } = new();

    public bool Start(string what, DateTime? when)
    {
        if (Tasks.Any(IsAlreadyDoing))
        {
            Console.WriteLine($"You are already doing {what}.");
            return false;
        }
        var doing = new Doing(what, when ?? DateTime.Now);
        Tasks.Add(doing);
        Tasks.Sort((a, b) => a.Start == b.Start ? 0 : (a.Start < b.Start ? -1 : 1));
        Console.WriteLine($"Started doing {doing.What} at {doing.Start:HH:mm}");
        return true;

        bool IsAlreadyDoing(Doing task)
            => task.What == what && task.End is null;
    }

    public bool End(string? what, DateTime? when = null)
    {
        var doing = what is null
            ? Tasks.LastOrDefault(d => d.End is null)
            : Tasks.Where(d => d.End is null)
                .FirstOrDefault(d => d.What == what);

        if (doing is null)
        {
            Console.WriteLine($"You have not started doing {what ?? "anything"} yet.");
            return false;
        }

        var end = when ?? DateTime.Now;
        if (end < doing.Start)
        {
            Console.WriteLine($"Cannot end {doing.What} at {end:HH:mm} because it started at {doing.Start:HH:mm}.");
            return false;
        }

        doing.End = end;
        Console.WriteLine($"You did {doing.What} from {doing.Start:HH:mm} to {doing.End:HH:mm}");
        return true;
    }

    public void EndAll(DateTime when)
    {
        foreach (var task in Tasks.Where(t => t.End is null && t.Start <= when))
        {
            task.End = when;
        }
    }
}
