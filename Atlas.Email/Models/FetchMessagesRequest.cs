namespace Atlas.Email.Models;

public class FetchMessagesRequest
{
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset? Until { get; set; }
    public Func<MailMessage, int, int, Task<bool>>? Filter { get; set; }
    public int MaxCount { get; set; } = 1000;
    public string? Folder { get; set; }

    /// <summary>
    /// When true (the default), every provider accumulates filtered messages into the list it
    /// returns from <see cref="IMailProvider.FetchMessagesAsync"/>. Callers that only care about
    /// the <see cref="Filter"/> callback -- e.g. a scheduled sync that processes each message as
    /// it streams by and never reads the return value -- should set this to false so the provider
    /// skips building that list. <see cref="Filter"/> still runs for every message exactly as
    /// before; only the accumulation into the returned collection is skipped, and the method
    /// returns an empty enumerable.
    /// </summary>
    public bool CollectMessages { get; init; } = true;
}
