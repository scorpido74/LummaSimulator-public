namespace LummaSimulator.Web.Models;

/// <summary>
/// In-memory demonstration session, optionally associated with a DemoEvent.
/// Its identity is not a visitor account, authentication session or access token.
/// Existing portal and WPF DemoRunIds remain separate correlation identifiers.
/// </summary>
public sealed record DemoSession
{
    public Guid Id { get; }
    public Guid? DemoEventId { get; }
    public DateTimeOffset CreatedAt { get; }

    public DemoSession(Guid id, DateTimeOffset createdAt, Guid? demoEventId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("A demonstration session identity is required.", nameof(id));
        if (demoEventId == Guid.Empty)
            throw new ArgumentException("Use null for a session without an event.", nameof(demoEventId));

        Id = id;
        DemoEventId = demoEventId;
        CreatedAt = createdAt.ToUniversalTime();
    }
}
