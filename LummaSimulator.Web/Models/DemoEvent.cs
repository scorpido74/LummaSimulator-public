namespace LummaSimulator.Web.Models;

/// <summary>
/// In-memory identity of an overarching demonstration event.
/// Does not replace an ExperienceEvent or PrizeEvent, or grant access to either.
/// </summary>
public sealed record DemoEvent
{
    public Guid Id { get; }
    public string Name { get; }
    public DateTimeOffset CreatedAt { get; }

    public DemoEvent(Guid id, string name, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("An event identity is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Any(char.IsControl) || name.Trim().Length > 100)
            throw new ArgumentException("An event name must contain 1–100 characters without control characters.", nameof(name));

        Id = id;
        Name = name.Trim();
        CreatedAt = createdAt.ToUniversalTime();
    }
}
