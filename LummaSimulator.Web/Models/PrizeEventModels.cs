namespace LummaSimulator.Web.Models;

public sealed record PrizeManagedEvent(string Id,string Name,string? ProfileId,PrizeConfiguration Configuration,
    string Status,long Version,long CreatedAt,long? ClosedAt,long? PurgeAt,bool ConfigurationLocked);
public sealed record PrizeEventPage(IReadOnlyList<PrizeManagedEvent> Events,int Page,bool HasNext);
public enum PrizeEventAction { Start, Close, Reopen }
