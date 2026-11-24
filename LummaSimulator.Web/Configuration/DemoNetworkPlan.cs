using System.Net;

namespace LummaSimulator.Web.Configuration;

// A non-secret administrative plan, never a live listener/firewall/RP-ID override.
public sealed record DemoNetworkPlan(string PortalOrigin, string AppOrigin, string PortalAddress,
    string AppAddress, string DemoSubnet, string HelperSubnet, string Profile)
{
    public bool IsValid() => !Errors().Any();
    public IEnumerable<(string Field,string Message)> Errors()
    {
        if(!Origin(PortalOrigin,false)) yield return ("portalOrigin","Gebruik een HTTPS-origin op poort 443 of 7243, zonder pad, bijvoorbeeld https://portal.example.test.");
        if(!Origin(AppOrigin,true)) yield return ("appOrigin","Gebruik een HTTPS-origin op poort 443, zonder pad, bijvoorbeeld https://lumma-app.intern.example.");
        if(!Address(PortalAddress)) yield return ("portalAddress","Vul een lokaal IPv4- of IPv6-adres van de portal in, bijvoorbeeld 192.0.2.153.");
        if(!Address(AppAddress)) yield return ("appAddress","Vul het LAN-IP-adres van de Lumma-app-pc in, bijvoorbeeld 192.0.2.154.");
        if(!Subnet(DemoSubnet)) yield return ("demoSubnet","Vul een netwerkadres met CIDR-prefix in, bijvoorbeeld 192.0.2.0/24.");
        if(!Subnet(HelperSubnet)) yield return ("helperSubnet","Vul het netwerkadres met CIDR-prefix van de helpers in, bijvoorbeeld 192.0.2.0/24.");
        if(Profile is not ("Private" or "Domain")) yield return ("profile","Kies Privé of Domein.");
    }
    private static bool Origin(string value, bool receiver) => value.Length <= 255
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && value == uri.GetLeftPart(UriPartial.Authority) && !uri.IsLoopback
        && (receiver ? uri.Port == 443 : uri.Port is 443 or 7243);
    private static bool Address(string value) => IPAddress.TryParse(value, out var ip)
        && !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any);
    private static bool Subnet(string value)
    {
        var parts = value.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || IPAddress.IsLoopback(ip)
            || !int.TryParse(parts[1], out var prefix)) return false;
        var bytes = ip.GetAddressBytes();
        if (prefix < 1 || prefix > bytes.Length * 8) return false;
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
            if ((bytes[bit / 8] & (1 << (7 - bit % 8))) != 0) return false;
        return true;
    }
}
