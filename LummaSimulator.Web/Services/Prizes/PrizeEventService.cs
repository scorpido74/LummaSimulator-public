using System.Security.Claims;
using System.Text.Json;
using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Services.Prizes;

// Presenter-only orchestration. No visitor admission, draw, claim, hub or public prize endpoint.
public sealed class PrizeEventService(DemoStore store,PresenterService presenters,TimeProvider clock)
{
    private readonly PrizeStore _repository=new(store,new PrizeAuthorization(presenters),clock);

    internal static PrizeConfiguration Validate(PrizeConfiguration configuration)
    {
        try
        {
            configuration=configuration with { PrizeName=configuration.PrizeName.Trim() };
            configuration.Validate();
            if(configuration.ReactionSeconds>86400 || configuration.PostCloseClaimSeconds>90*86400
                || configuration.ClaimFieldSchemaJson.Length>4096 || configuration.PrizeName.Any(char.IsControl))
                throw new ArgumentException();
            using var fields=JsonDocument.Parse(configuration.ClaimFieldSchemaJson);
            var accepted=new HashSet<string>(["FirstName","LastName","Email","Company","ShippingAddress"],StringComparer.Ordinal);
            var canonical=new SortedDictionary<string,string>(StringComparer.Ordinal);var seen=new HashSet<string>();
            foreach(var field in fields.RootElement.EnumerateObject())
            {
                if(!accepted.Contains(field.Name) || !seen.Add(field.Name) || field.Value.ValueKind!=JsonValueKind.String)
                    throw new ArgumentException();
                var value=field.Value.GetString();
                if(value is not ("Hidden" or "Optional" or "Required"))throw new ArgumentException();
                if(value!="Hidden")canonical.Add(field.Name,value);
            }
            return configuration with { ClaimFieldSchemaJson=JsonSerializer.Serialize(canonical) };
        }
        catch(Exception e) when(e is ArgumentException or JsonException or NullReferenceException)
        { throw new DemoRequestException("PrizeInvalidSettings"); }
    }

    public Task<IReadOnlyList<PrizeProfile>> ProfilesAsync(ClaimsPrincipal actor,CancellationToken ct=default) =>
        _repository.ManagementProfilesAsync(actor,ct);
    public Task<PrizeProfile> DefaultProfileAsync(ClaimsPrincipal actor,CancellationToken ct=default) =>
        _repository.ManagementDefaultProfileAsync(actor,ct);
    public Task<PrizeEventPage> ListAsync(ClaimsPrincipal actor,int page=0,CancellationToken ct=default) =>
        _repository.ManagementListAsync(actor,page,ct);
    public Task<PrizeManagedEvent?> GetAsync(ClaimsPrincipal actor,string id,CancellationToken ct=default) =>
        _repository.ManagementGetAsync(actor,id,ct);

    public async Task<PrizeManagedEvent> CreateAsync(ClaimsPrincipal actor,string key,string name,string profileId,
        PrizeConfiguration configuration,bool start,CancellationToken ct=default)
    {
        var config=Validate(configuration);
        if(string.IsNullOrWhiteSpace(name) || name.Trim().Length>100 || name.Any(char.IsControl))
            throw new DemoRequestException("PrizeInvalidName");
        var created=await _repository.CreateDraftAsync(actor,key,name,profileId,ct,
            configuration:config,start:start);
        return await _repository.ManagementGetAsync(actor,created.Id,ct)??throw new InvalidOperationException();
    }
    public Task<PrizeManagedEvent> TransitionAsync(ClaimsPrincipal actor,string id,string expectedId,long version,string key,
        PrizeEventAction action,bool confirmed,CancellationToken ct=default) =>
        _repository.ManagementChangeAsync(actor,id,expectedId,version,key,action.ToString(),confirmed,null,null,ct);
    public Task<PrizeManagedEvent> RenameAsync(ClaimsPrincipal actor,string id,string expectedId,long version,string key,string name,CancellationToken ct=default) =>
        _repository.ManagementChangeAsync(actor,id,expectedId,version,key,"Rename",true,name,null,ct);
    public Task<PrizeManagedEvent> ConfigureAsync(ClaimsPrincipal actor,string id,string expectedId,long version,string key,
        PrizeConfiguration configuration,CancellationToken ct=default) =>
        _repository.ManagementChangeAsync(actor,id,expectedId,version,key,"Configure",true,null,Validate(configuration),ct);
    public Task<string> SaveProfileAsync(ClaimsPrincipal actor,string id,string expectedId,long version,string key,string name,CancellationToken ct=default) =>
        _repository.ManagementSaveProfileAsync(actor,id,expectedId,version,key,name,ct);
}
