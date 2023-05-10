namespace Today;

public class Today
{
    public DateTime Date { get; set; } = DateTime.Now.Date;

    public List<Doing> Tasks { get; set; } = new();

    public void Start(string what, DateTime? when)
    {
        if (Tasks.Any(IsAlreadyDoing))
        {
            Console.WriteLine($"You are already doing {what}.");
            return;
        }
        var doing = new Doing(what, when ?? DateTime.Now);
        Tasks.Add(doing);
        Tasks.Sort((a, b) => a.Start == b.Start ? 0 : (a.Start < b.Start ? -1 : 1));
        Console.WriteLine($"Started doing {doing.What} at {doing.Start:HH:mm}");

        bool IsAlreadyDoing(Doing task)
            => task.What == what && task.End is null;
    }

    public void End(string? what, DateTime? when = null)
    {
        var doing = what is null
            ? Tasks.Last(d => d.End is null)
            : Tasks.Where(d => d.End is null)
                .FirstOrDefault(d => d.What == what);

        if (doing is null)
        {
            Console.WriteLine($"You have not started doing {what ?? "anything"} yet.");
            return;
        }

        doing.End = when ??= DateTime.Now;
        Console.WriteLine($"You did {doing.What} from {doing.Start:HH:mm} to {doing.End:HH:mm}");
    }

    public void EndAll(DateTime when)
    {
        foreach (var task in Tasks.Where(t => t.End is null))
        {
            task.End = when;
        }
    }
}
