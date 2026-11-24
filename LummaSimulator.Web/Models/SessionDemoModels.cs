using System.Text.Json.Serialization;

namespace LummaSimulator.Web.Models;

public sealed record DesktopConnection(string RequestId,string Key,DateTimeOffset ExpiresAt);
public sealed record DesktopConnectionStatus(string Status,DateTimeOffset ExpiresAt);
public sealed record ParticipantEvent(string Id,string Name);
public sealed record EventParticipant(string Id,int Round,string Alias,string ConsentStatus,bool CanOpen);
public sealed record ParticipantRoster(string EventId,long SelectionVersion,IReadOnlyList<EventParticipant> Participants);
public sealed record VisitorConsent(string? RequestId,string Status,string? EventName,int? Round,string? Alias,DateTimeOffset? ExpiresAt);
public sealed record SessionDemoOpenLink(string ViewId,string Token,DateTimeOffset ExpiresAt);
public sealed record SyntheticSessionView(string EventId,string EventName,int Round,string Alias,DateTimeOffset ExpiresAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SelectParticipantEvent(string EventId,long Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartParticipantDemo(string ParticipantId,string WpfRunId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpenParticipantDemo(string ParticipantId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideSessionDemo(string RequestId,bool Allow,bool Confirmed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RedeemSessionDemo(string Token);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelParticipantDemo(string WpfRunId);
