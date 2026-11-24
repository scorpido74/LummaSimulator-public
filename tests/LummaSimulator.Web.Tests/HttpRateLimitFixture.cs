using System.Net;
using System.Net.Http.Headers;

namespace LummaSimulator.Web.Tests;

// Only for expected presenter-management throttling by the isolated HTTP fixture.
// No exception/status retries other than one 429; callers retain every assertion.
internal static class HttpRateLimitFixture
{
    internal static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send,
        CancellationToken ct=default,Func<TimeSpan,CancellationToken,Task>? wait=null,TimeProvider? clock=null)
    {
        ct.ThrowIfCancellationRequested();
        var response=await send();
        if(response.StatusCode!=HttpStatusCode.TooManyRequests) return VisibleResponse(response);
        TimeSpan delay;
        try {
            var retry=response.Headers.RetryAfter;
            // Existing production fixed windows are one minute. Middleware need not emit Retry-After.
            delay=retry?.Delta ?? (retry?.Date is { } date ? date-(clock ?? TimeProvider.System).GetUtcNow() : TimeSpan.FromSeconds(60));
            if(delay<TimeSpan.Zero) delay=TimeSpan.Zero;
            // Do not clip a server-required wait and then retry too early: fail visibly instead.
            if(delay>TimeSpan.FromSeconds(120)) throw new HttpRequestException("Fixture Retry-After exceeds the 120-second retry budget.",null,HttpStatusCode.TooManyRequests);
        } finally { response.Dispose(); }
        Console.WriteLine($"Integration fixture: presenter-management returned HTTP 429; waiting {delay.TotalSeconds:F1}s, then retrying once.");
        await (wait ?? ((duration,token)=>Task.Delay(duration,token)))(delay,ct);
        ct.ThrowIfCancellationRequested();
        var repeated=await send();
        if(repeated.StatusCode==HttpStatusCode.TooManyRequests) {
            repeated.Dispose();
            throw new HttpRequestException("Presenter-management fixture still returned HTTP 429 after one retry.",null,HttpStatusCode.TooManyRequests);
        }
        return VisibleResponse(repeated);
    }
    private static HttpResponseMessage VisibleResponse(HttpResponseMessage response)
    {
        // Negative authorization assertions must not accidentally accept a server failure.
        if((int)response.StatusCode>=500) {
            var code=response.StatusCode;response.Dispose();
            throw new HttpRequestException("Unexpected HTTP server error in presenter-management fixture: "+(int)code,null,code);
        }
        return response;
    }

    internal static Task<HttpResponseMessage> GetAsync(HttpClient client,string path) => SendAsync(()=>client.GetAsync(path));
    internal static async Task<string> GetStringAsync(HttpClient client,string path)
    {
        using var response=await GetAsync(client,path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    internal static async Task RunChecksAsync()
    {
        var now=DateTimeOffset.FromUnixTimeSeconds(1800000000);var clock=new RetryClock(now);
        foreach(var scenario in new[]{"delta","date","missing","past"}) {
            var calls=0;var waits=new List<TimeSpan>();
            var throttled=new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if(scenario=="delta") throttled.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            if(scenario=="date") throttled.Headers.RetryAfter=new RetryConditionHeaderValue(now.AddSeconds(5));
            if(scenario=="past") throttled.Headers.RetryAfter=new RetryConditionHeaderValue(now.AddSeconds(-5));
            using var result=await SendAsync(()=>Task.FromResult(++calls==1?throttled:new HttpResponseMessage(HttpStatusCode.OK)),
                wait:(duration,_)=>{waits.Add(duration);return Task.CompletedTask;},clock:clock);
            var expected=scenario switch {"delta"=>3,"date"=>5,"past"=>0,_=>60};
            TestProgram.Check(calls==2 && result.StatusCode==HttpStatusCode.OK && waits.SequenceEqual(new[]{TimeSpan.FromSeconds(expected)}),"Fixture honors bounded Retry-After/fixed-window fallback: "+scenario);
        }
        foreach(var code in new[]{HttpStatusCode.BadRequest,HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden}) {
            var calls=0;var waits=0;
            using var result=await SendAsync(()=>{calls++;return Task.FromResult(new HttpResponseMessage(code));},wait:(_,_)=>{waits++;return Task.CompletedTask;});
            TestProgram.Check(calls==1 && waits==0 && result.StatusCode==code,"Fixture does not conceal or retry unexpected HTTP status: "+(int)code);
        }
        foreach(var code in new[]{HttpStatusCode.InternalServerError,HttpStatusCode.ServiceUnavailable}) {
            var calls=0;var waits=0;var failed=false;
            try {using var ignored=await SendAsync(()=>{calls++;return Task.FromResult(new HttpResponseMessage(code));},wait:(_,_)=>{waits++;return Task.CompletedTask;});}
            catch(HttpRequestException e){failed=e.StatusCode==code;}
            TestProgram.Check(failed && calls==1 && waits==0,"Unexpected server error fails visibly without retry: "+(int)code);
        }
        var repeatedCalls=0;var repeatedWaits=0;
        var visible=false;
        try {using var ignored=await SendAsync(()=>{repeatedCalls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));},
            wait:(_,_)=>{repeatedWaits++;return Task.CompletedTask;});}
        catch(HttpRequestException e){visible=e.StatusCode==HttpStatusCode.TooManyRequests;}
        TestProgram.Check(repeatedCalls==2 && repeatedWaits==1 && visible,"Persistent HTTP 429 always fails after exactly one retry, even for negative authorization assertions");
        var oversizedCalls=0;var oversizedWaits=0;var oversizedVisible=false;
        try {
            using var ignored=await SendAsync(()=>{
                oversizedCalls++;var result=new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                result.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(121));return Task.FromResult(result);
            },wait:(_,_)=>{oversizedWaits++;return Task.CompletedTask;});
        } catch(HttpRequestException e) {oversizedVisible=e.StatusCode==HttpStatusCode.TooManyRequests;}
        TestProgram.Check(oversizedVisible && oversizedCalls==1 && oversizedWaits==0,"Excessive Retry-After fails rather than ignoring the server delay or retrying without a bound");
        var networkCalls=0;var networkVisible=false;
        try {using var ignored=await SendAsync(()=>{networkCalls++;throw new HttpRequestException("Synthetic connection failure");});}
        catch(HttpRequestException){networkVisible=true;}
        TestProgram.Check(networkVisible && networkCalls==1,"Connection failures are not retried by the rate-limit fixture");
    }
    private sealed class RetryClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()=>now;
    }
}
