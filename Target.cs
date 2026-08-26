using Taste;
using Taste.Savoring;

namespace Today;

/// <summary>
///     The day a command works on, together with how to keep a change to it.
/// </summary>
/// <remarks>
///     Which jar a day belongs in cannot be read off the day itself: one out of
///     <see cref="History" /> is a <see cref="Day" /> like any other, and
///     <c>Savor&lt;TTaste&gt;</c> resolves the jar from the taste's static type. Savoring a past
///     day directly would therefore write it over the day in progress — silently, since that is
///     a perfectly good <c>Day</c> going into the file that holds them. So where a day came
///     from travels with the day, and <see cref="Keep" /> is the only thing that writes.
///     Nothing is persisted unless it is called, which is what makes a command that only reads
///     leave the files alone.
/// </remarks>
public sealed class Target
{
    private readonly bool fromHistory;

    private Target(Day day, bool fromHistory)
    {
        Day = day;
        this.fromHistory = fromHistory;
    }

    /// <summary>The day in progress, as served from its own jar.</summary>
    public static Target Current(Day day) => new(day, fromHistory: false);

    /// <summary>A day already filed away, reached through <see cref="History" />.</summary>
    public static Target Past(Day day) => new(day, fromHistory: true);

    public Day Day { get; }

    /// <summary>
    ///     Whether this is the day in progress. What separates the two is that only today has a
    ///     "now" — a command that would otherwise default a time to it has to ask for one instead.
    /// </summary>
    public bool IsToday => !fromHistory;

    /// <summary>
    ///     Writes the day back to wherever it came from.
    /// </summary>
    public void Keep()
    {
        // History holds the reference, so savoring the History is what persists a change to a
        // day sitting inside it. There is no separate jar per day to write.
        if (fromHistory)
        {
            Cook.Serve<History>().Savor();
        }
        else
        {
            Day.Savor();
        }
    }
}
