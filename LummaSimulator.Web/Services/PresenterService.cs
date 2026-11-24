using Microsoft.AspNetCore.Authentication;
namespace LummaSimulator.Web.Services;

public sealed class CleanupWorker(IServiceScopeFactory scopes, ILogger<CleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DemoService>().CleanupAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<PresenterService>().PurgeExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError(new EventId(500, "CleanupFailed"), "Temporary data cleanup failed; operator action required"); }
        }
    }
}

public static class VisitorCookie
{
    public const string Name = "__Host-Lumma.Visitor";
    public static string? Read(HttpContext context) => context.Request.Cookies[Name];
    private static CookieOptions Options => new() { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true };
    public static void Write(HttpContext context, string token) => context.Response.Cookies.Append(Name, token, Options);
    public static void Delete(HttpContext context) => context.Response.Cookies.Delete(Name, Options);
}
