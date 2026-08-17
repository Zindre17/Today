namespace Today;

public class Today
{
    public DateTime Date { get; set; } = DateTime.Now.Date;

    public List<Doing> Tasks { get; set; } = [];

    public bool Start(string what, DateTime? when)
    {
        if (Tasks.Any(IsAlreadyDoing))
        {
            Output.Error($"You are already doing {what}.");
            return false;
        }
        var doing = Insert(new Doing(what, when ?? DateTime.Now));
        Output.Success($"Started doing {doing.What} at {doing.Start:HH:mm}");
        return true;

        bool IsAlreadyDoing(Doing task)
            => task.What == what && task.End is null;
    }

    /// <summary>
    ///     Records something already finished, for the times you only remember to log it
    ///     afterwards. Unlike <see cref="Start" /> it does not mind a task of the same name
    ///     running: logging what you just did says nothing about what you are doing.
    /// </summary>
    public bool Did(string what, DateTime start, DateTime end)
    {
        if (end < start)
        {
            Output.Error($"Cannot have done {what} from {start:HH:mm} to {end:HH:mm}.");
            return false;
        }

        var doing = Insert(new Doing(what, start) { End = end });
        Output.Success($"You did {doing.What} from {doing.Start:HH:mm} to {doing.End:HH:mm}");
        return true;
    }

    /// <summary>
    ///     Deletes a task from the day. A name can repeat — <see cref="Did" /> allows it — so the
    ///     most recently started one goes, that being the one just logged by mistake.
    /// </summary>
    public bool Remove(string what)
    {
        var index = Tasks.FindLastIndex(t => t.What == what);

        if (index < 0)
        {
            Output.Error($"You have not done {what} today.");
            return false;
        }

        var doing = Tasks[index];
        Tasks.RemoveAt(index);

        Output.Success(doing.End is null
            ? $"Removed {doing.What}, which started at {doing.Start:HH:mm}"
            : $"Removed {doing.What}, {doing.Start:HH:mm} to {doing.End:HH:mm}");
        return true;
    }

    public bool End(string? what, DateTime? when = null)
    {
        var doing = what is null
            ? Tasks.LastOrDefault(d => d.End is null)
            : Tasks.Where(d => d.End is null)
                .FirstOrDefault(d => d.What == what);

        if (doing is null)
        {
            Output.Error($"You have not started doing {what ?? "anything"} yet.");
            return false;
        }

        var end = when ?? DateTime.Now;
        if (end < doing.Start)
        {
            Output.Error($"Cannot end {doing.What} at {end:HH:mm} because it started at {doing.Start:HH:mm}.");
            return false;
        }

        doing.End = end;
        Output.Success($"You did {doing.What} from {doing.Start:HH:mm} to {doing.End:HH:mm}");
        return true;
    }

    /// <summary>
    ///     Adds a task and keeps the day in start order, which is the order everything reads in.
    /// </summary>
    private Doing Insert(Doing doing)
    {
        Tasks.Add(doing);
        Tasks.Sort((a, b) => a.Start.CompareTo(b.Start));
        return doing;
    }

    public void EndAll(DateTime when)
    {
        foreach (var task in Tasks.Where(t => t.End is null && t.Start <= when))
        {
            task.End = when;
        }
    }
}
