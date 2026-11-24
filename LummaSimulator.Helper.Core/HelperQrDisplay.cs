using LummaSimulator.App.Services;
using LummaSimulator.Shared;

namespace LummaSimulator.Helper;

// Contains only a public invitation, never the capability used to obtain it.
public sealed class HelperQrDisplay
{
    public Uri? Link {get;private set;}
    public string? EventName {get;private set;}
    public ParticipantQr? Code {get;private set;}
    public bool LaptopOnly=>Link is {} link&&ParticipantLink.LaptopOnly(link);
    public void Clear(){Link=null;EventName=null;Code=null;}
    public bool Apply(string portalOrigin,ReceiverQrState selection,IReadOnlyList<RoundEvent> authorized)
    {
        Clear();
        if(selection.Status!="Ready")return false;
        if(selection.PortalOrigin!=portalOrigin||selection.EventId is not {} id
            ||!Guid.TryParseExact(id,"D",out _)||selection.VisitorPath!=ParticipantLink.EventPath(id))throw new InvalidDataException();
        var item=authorized.SingleOrDefault(e=>e.Id==id)??throw new InvalidDataException();
        var link=ParticipantLink.ForEvent(portalOrigin,id);var code=ParticipantQr.Encode(link.AbsoluteUri);
        Link=link;Code=code;EventName=item.Name;return true;
    }
}
