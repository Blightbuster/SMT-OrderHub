namespace OrderHub.Client.Resources;

/// <summary>
/// Single source of truth for the UI cultures the app supports.
/// English is the only shipped culture today. To add a language:
///   1. Create Resources/SharedResource.&lt;lang&gt;.resx with translated strings.
///   2. Add the culture code to <see cref="All"/> below.
/// No other code changes are required — resource lookup falls back to
/// SharedResource.resx (English) for any culture without a satellite file.
/// </summary>
public static class SupportedCultures
{
    /// <summary>Culture codes with a shipped SharedResource.&lt;code&gt;.resx.</summary>
    public static readonly string[] All = ["en"];

    /// <summary>Default culture used when no user preference is known.</summary>
    public const string Default = "en";
}