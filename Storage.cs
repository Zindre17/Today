using Taste;

namespace Today;

/// <summary>
///     Where the persisted state lives, and how it got there.
/// </summary>
/// <remarks>
///     Tracked days are records the user cannot reconstruct, so they go in the data
///     directory; the theme is a preference they can retype, so it goes in the config
///     directory. That is the split the operating system already makes, and `Taste` keeps
///     a taste wherever its kitchen says.
/// </remarks>
public static class Storage
{
    private const string AppName = "today";

    /// <summary>
    ///     Settle where tastes are kept and must run before the first <see cref="Cook.Serve{T}" />:
    ///     the kitchen cannot be changed afterwards, and a taste served before the move would
    ///     be the empty one.
    /// </summary>
    public static void Arrange()
    {
        Cook.UseKitchen(new Kitchen
        {
            Pantry = DataDirectory,
            Pantries = new Dictionary<Type, string>
            {
                [typeof(Theme)] = ConfigDirectory,
            },
        });
    }

    /// <summary>
    ///     Days and history: records, not preferences. <c>TODAY_DATA_DIR</c> overrides it,
    ///     which is how a development run stays out of the real one.
    /// </summary>
    public static string DataDirectory =>
        Overridden("TODAY_DATA_DIR") ?? Beside(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    ///     The theme: a preference, and the only thing here the user would think to edit
    ///     by hand. <c>TODAY_CONFIG_DIR</c> overrides it.
    /// </summary>
    public static string ConfigDirectory =>
        Overridden("TODAY_CONFIG_DIR") ?? Beside(Environment.SpecialFolder.ApplicationData);

    private static string? Overridden(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } directory
            ? directory
            : null;

    /// <summary>
    ///     The OS location, with the app's own directory under it. An empty answer would
    ///     quietly put the user's day in the working directory, so it is refused instead.
    /// </summary>
    private static string Beside(Environment.SpecialFolder folder)
    {
        var root = Environment.GetFolderPath(folder);

        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                $"Could not find the {folder} directory. Set TODAY_DATA_DIR and "
                + "TODAY_CONFIG_DIR to say where today should keep its files.");
        }

        return Path.Combine(root, AppName);
    }
}
