using System.Text.Json;

namespace LummaSimulator.Web.Models;

internal enum PrizeEventStatus { Draft, Open, Closed, Purging }
internal enum PrizeGroupStatus { Open, Closed, Frozen }
internal enum PrizeParticipantState { Active, Cancelled }
internal enum PrizeClaimState { Pending, Redeemed, Forfeited, Expired }
internal enum PrizeStartCodeState { Available, Consumed, Revoked }
internal enum PrizeDrawStatus { Committed }
internal enum PrizeDrawKind { Initial, Replacement, Instant }
internal enum PrizeLedgerKind { Reserve, Release, Issue, Adjust }

public sealed record PrizeConfiguration(
    bool PrizeEnabled = false, string Visibility = "Private", string Mode = "Group",
    int GroupSize = 15, int WinnersPerGroup = 1, string AssignmentMode = "Automatic",
    string NextGroupMode = "Manual", string DrawMethod = "Manual",
    string RevealMode = "Together", int AnimationSeconds = 10, bool StartCodeRequired = false,
    string PrizeSelection = "Fixed", string PrizeName = "Cybertruck-demoprijs", long? Stock = null,
    int ChanceNumerator = 1, int ChanceDenominator = 10, int? MaxInstantWins = null,
    int ReactionSeconds = 120, int RetentionDays = 30, int PostCloseClaimSeconds = 86400,
    string ClaimFieldSchemaJson = "{}")
{
    internal void Validate()
    {
        if (Visibility is not ("Public" or "Private") || Mode is not ("Group" or "Instant")
            || GroupSize is < 1 or > 200 || WinnersPerGroup < 1 || WinnersPerGroup > GroupSize
            || AssignmentMode is not ("Automatic" or "Code") || NextGroupMode is not ("Manual" or "Automatic")
            || DrawMethod != "Manual" || RevealMode is not ("Together" or "Sequential")
            || AnimationSeconds is not (5 or 10 or 15) || (Visibility == "Public" && !StartCodeRequired)
            || PrizeSelection is not ("Fixed" or "Random") || string.IsNullOrWhiteSpace(PrizeName)
            || PrizeName.Length > 100 || Stock < 0 || ChanceDenominator < 1
            || ChanceNumerator < 0 || ChanceNumerator > ChanceDenominator || MaxInstantWins < 0
            || ReactionSeconds < 1 || RetentionDays is < 1 or > 90 || PostCloseClaimSeconds < 0)
            throw new ArgumentException("Invalid prize configuration.");
        using var fields = JsonDocument.Parse(ClaimFieldSchemaJson);
        if (fields.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Claim field schema must be an object.");
    }
}

public sealed record PrizeProfile(string Id, string Name, PrizeConfiguration Configuration, long Version);
public sealed record PrizeRoundWinner(string Alias,string PrizeName);
public sealed record PrizeRoundSnapshot(string EventId,string EventName,string GroupId,int Number,int Capacity,int Occupied,
    string Status,string? Alias,bool IsWinner,string? PrizeName,int WinnerCount,IReadOnlyList<PrizeRoundWinner> Winners,int? NextCapacity)
{public string DrawResultKey {get;init;}="RoundRowUnknown";public string PrizeMessageKey {get;init;}="VisitorPrizeUnknown";public bool DrawPerformed {get;init;}}
public sealed record PrizeRoundBoard(string EventId,string EventName,int DefaultCapacity,int? NextCapacity,IReadOnlyList<PrizeRoundSnapshot> Rounds);
internal sealed record PrizeDraft(string Id, string Name, string ProfileId, string Snapshot, long Version);

internal static class PrizeProfiles
{
    internal const string Cybertruck = "builtin-cybertruck";
    internal static readonly PrizeProfile[] BuiltIns =
    [
        new(Cybertruck, "Cybertruck", new(PrizeEnabled:false), 1),
        new("builtin-instant", "Direct winnen", new(PrizeEnabled:false,Mode: "Instant", MaxInstantWins: 10, PrizeName: "Demoprijs"), 1),
        new("builtin-draw", "Evenement met loting", new(PrizeEnabled:false,PrizeName: "Demoprijs"), 1),
        new("builtin-custom", "Aangepast", new(PrizeEnabled:false), 1)
    ];
}

internal sealed class PrizeConflictException(string message) : Exception(message);

internal static class PrizeTransitions
{
    internal static bool Event(PrizeEventStatus from, PrizeEventStatus to) => (from, to) is
        (PrizeEventStatus.Draft, PrizeEventStatus.Open) or (PrizeEventStatus.Open, PrizeEventStatus.Closed)
        or (PrizeEventStatus.Closed, PrizeEventStatus.Open) or (PrizeEventStatus.Closed, PrizeEventStatus.Purging)
        or (PrizeEventStatus.Open, PrizeEventStatus.Purging);
    internal static bool Group(PrizeGroupStatus from, PrizeGroupStatus to) => (from, to) is
        (PrizeGroupStatus.Open, PrizeGroupStatus.Closed) or (PrizeGroupStatus.Closed, PrizeGroupStatus.Open)
        or (PrizeGroupStatus.Closed, PrizeGroupStatus.Frozen);
    internal static bool Claim(PrizeClaimState from, PrizeClaimState to) =>
        from == PrizeClaimState.Pending && to is PrizeClaimState.Redeemed or PrizeClaimState.Forfeited or PrizeClaimState.Expired;
}
