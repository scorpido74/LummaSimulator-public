using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Pages.Presenter.Prizes;

public sealed class PrizeEventInput
{
    public bool PrizeEnabled { get; set; }
    [Range(1,200)] public int GroupSize { get; set; }=15;
    [Range(1,200)] public int WinnersPerGroup { get; set; }=1;
    public string PrizeSelection { get; set; }="Fixed";
    [Required,StringLength(100)] public string PrizeName { get; set; }="Cybertruck-demoprijs";
    [Range(0,long.MaxValue)] public long? Stock { get; set; }
    public bool StartCodeRequired { get; set; }
    public string Visibility { get; set; }="Private";
    public string Mode { get; set; }="Group";
    public string AssignmentMode { get; set; }="Automatic";
    public string NextGroupMode { get; set; }="Manual";
    public string RevealMode { get; set; }="Together";
    public int AnimationSeconds { get; set; }=10;
    [Range(0,int.MaxValue)] public int ChanceNumerator { get; set; }=1;
    [Range(1,int.MaxValue)] public int ChanceDenominator { get; set; }=10;
    [Range(0,int.MaxValue)] public int? MaxInstantWins { get; set; }
    [Range(1,86400)] public int ReactionSeconds { get; set; }=120;
    [Range(1,90)] public int RetentionDays { get; set; }=30;
    [Range(0,129600)] public int PostCloseClaimMinutes { get; set; }=1440;
    public string FirstName { get; set; }="Hidden";
    public string LastName { get; set; }="Hidden";
    public string Email { get; set; }="Hidden";
    public string Company { get; set; }="Hidden";
    public string ShippingAddress { get; set; }="Hidden";

    internal PrizeConfiguration Configuration() => new(PrizeEnabled:PrizeEnabled,Visibility:Visibility,Mode:Mode,
        GroupSize:GroupSize,WinnersPerGroup:WinnersPerGroup,AssignmentMode:AssignmentMode,NextGroupMode:NextGroupMode,
        RevealMode:RevealMode,AnimationSeconds:AnimationSeconds,StartCodeRequired:StartCodeRequired,
        PrizeSelection:PrizeSelection,PrizeName:PrizeName,Stock:Stock,ChanceNumerator:ChanceNumerator,
        ChanceDenominator:ChanceDenominator,MaxInstantWins:MaxInstantWins,ReactionSeconds:ReactionSeconds,
        RetentionDays:RetentionDays,PostCloseClaimSeconds:checked(PostCloseClaimMinutes*60),
        ClaimFieldSchemaJson:JsonSerializer.Serialize(new{FirstName,LastName,Email,Company,ShippingAddress}));

    internal static PrizeEventInput From(PrizeConfiguration c)
    {
        using var fields=JsonDocument.Parse(c.ClaimFieldSchemaJson);
        string Field(string key)=>fields.RootElement.TryGetProperty(key,out var value)?value.GetString()??"Hidden":"Hidden";
        return new(){PrizeEnabled=c.PrizeEnabled,GroupSize=c.GroupSize,WinnersPerGroup=c.WinnersPerGroup,PrizeSelection=c.PrizeSelection,
            PrizeName=c.PrizeName,Stock=c.Stock,StartCodeRequired=c.StartCodeRequired,Visibility=c.Visibility,Mode=c.Mode,
            AssignmentMode=c.AssignmentMode,NextGroupMode=c.NextGroupMode,RevealMode=c.RevealMode,AnimationSeconds=c.AnimationSeconds,
            ChanceNumerator=c.ChanceNumerator,ChanceDenominator=c.ChanceDenominator,MaxInstantWins=c.MaxInstantWins,
            ReactionSeconds=c.ReactionSeconds,RetentionDays=c.RetentionDays,PostCloseClaimMinutes=c.PostCloseClaimSeconds/60,
            FirstName=Field("FirstName"),LastName=Field("LastName"),Email=Field("Email"),Company=Field("Company"),ShippingAddress=Field("ShippingAddress")};
    }
}
