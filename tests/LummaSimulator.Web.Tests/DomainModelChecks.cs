using LummaSimulator.Web.Models;

namespace LummaSimulator.Web.Tests;

internal static class DomainModelChecks
{
    public static void Run()
    {
        var at = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(1));
        var demoEvent = new DemoEvent(Guid.NewGuid(), "  Synthetic demonstration  ", at);
        TestProgram.Check(demoEvent.Name == "Synthetic demonstration" && demoEvent.CreatedAt.Offset == TimeSpan.Zero
            && demoEvent.CreatedAt == at, "Domain event normalizes its name and preserves the supplied instant as UTC");

        var standalone = new DemoSession(Guid.NewGuid(), at);
        var linked = new DemoSession(Guid.NewGuid(), at, demoEvent.Id);
        TestProgram.Check(standalone.DemoEventId is null && linked.DemoEventId == demoEvent.Id,
            "Domain sessions support both standalone use and an explicit optional event association");
        TestProgram.Check(linked.CreatedAt.Offset == TimeSpan.Zero && linked.CreatedAt == at,
            "Domain session preserves the supplied instant as UTC without consulting the clock");

        Reject(() => new DemoEvent(Guid.Empty, "Synthetic demonstration", at), "Empty domain event identity rejected");
        Reject(() => new DemoSession(Guid.Empty, at), "Empty domain session identity rejected");
        Reject(() => new DemoSession(Guid.NewGuid(), at, Guid.Empty), "Empty event reference cannot masquerade as an unlinked session");
        foreach (var name in new[] { null, "", "   ", "\nSynthetic demonstration", "Synthetic\tdemonstration", new string('x', 101) })
            Reject(() => new DemoEvent(Guid.NewGuid(), name!, at), "Invalid domain event name rejected without exposing its contents");

        TestProgram.Check(new DemoEvent(Guid.NewGuid(), new string('x', 100), at).Name.Length == 100,
            "Domain event accepts the existing event-name length boundary");
    }

    private static void Reject(Action action, string message)
    {
        var rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        TestProgram.Check(rejected, message);
    }
}
