namespace LummaSimulator.App.Models;

// Presentation content only. Execution phases and ATT&CK mappings remain in the existing model.
public sealed record ScenarioOverview(string Title, string Description, string AttackerGoal,
    IReadOnlyList<string> Roles, IReadOnlyList<string> LearningPoints)
{
    public static ScenarioOverview EventEdition { get; } = new(
        "Van phishing naar sessierisico",
        "Een begeleide demonstratie: veilig aanmelden met een passkey, een misleidende herstelmelding herkennen en onderscheid maken tussen simulatie en echte detectie.",
        "De aanvaller probeert een bezoeker met een geloofwaardige melding tot een onveilige handeling te verleiden. Deze demonstratie gebruikt uitsluitend fictieve gegevens.",
        ["Hacker — bedient deze WPF-simulator en vertelt het aanvalsscenario.",
         "Slachtoffer — registreert een passkey in de afzonderlijke portal, meldt zich aan en oefent met een veilige ClickFix-keuze.",
         "SOC — onderzoekt uitsluitend werkelijk waargenomen detecties en respons in de bestaande beveiligingsconsole."],
        ["Controleer de website vóór het aanmelden. Een passkey is aan de juiste website gebonden.",
         "Voer geen instructies van een onverwachte herstelmelding uit. Stop en verifieer via een vertrouwd kanaal.",
         "Passkeys beschermen authenticatie, maar voorkomen niet ieder risico op een al aangemeld endpoint.",
         "De zes fases hieronder zijn een simulatie. Lokale LIVE-activiteit is geen bewijs van MDR-detectie.",
         "Portal en WPF werken zelfstandig. Een bezoekerkeuze start geen WPF- of MDR-activiteit."]);
}
