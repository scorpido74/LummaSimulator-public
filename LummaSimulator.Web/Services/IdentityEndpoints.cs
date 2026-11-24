namespace LummaSimulator.Web.Services;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/api/identity").RequireRateLimiting("demo");
        // Same visitor cookie and server-side proof as the portal; presenter cookies confer no access.
        group.MapGet("/{resource}",async Task<IResult> (string resource,HttpContext http,DemoStore store,SecurityAuditService audit,
            DateTimeOffset? from,DateTimeOffset? until,string? eventType,string? demoRunId,int? offset,int? limit) => {
            if(resource is not ("sessions" or "audit")) return Results.NotFound();
            if((from.HasValue && until.HasValue && from.Value>until.Value) ||
                (eventType is not null && (!Enum.TryParse<IdentityEvent>(eventType,out var kind) || !Enum.IsDefined(kind) || kind.ToString()!=eventType)) ||
                (demoRunId is not null && !Guid.TryParseExact(demoRunId,"D",out _))) return Results.BadRequest();
            await store.Gate.WaitAsync(http.RequestAborted);
            try {
                var owner=await audit.OwnerAsync(VisitorCookie.Read(http),http.RequestAborted);
                if(owner is null) return Results.Unauthorized();
                var pageOffset=Math.Clamp(offset ?? 0,0,100000);var pageLimit=Math.Clamp(limit ?? 50,1,100);
                var start=from?.ToUnixTimeMilliseconds();var end=until?.ToUnixTimeMilliseconds();
                if(resource=="sessions") return Results.Ok(new {Offset=pageOffset,Limit=pageLimit,Items=await audit.SessionsAsync(owner,http.RequestAborted,pageOffset,pageLimit,start,end,demoRunId)});
                return Results.Ok(new {Offset=pageOffset,Limit=pageLimit,Items=await audit.AuditAsync(owner,start,end,eventType,demoRunId,pageOffset,pageLimit,http.RequestAborted)});
            } finally { store.Gate.Release(); }
        });
    }
}
