"""Detection rules — WithSecure Elements style (simulated).

Each rule inspects telemetry events (process / file / network) streamed from
the simulated endpoint and produces detections with realistic F-Secure /
WithSecure naming, MITRE ATT&CK mappings and response advice.

NOTE: this emulates what WithSecure Elements Endpoint Protection reports for
Lumma-family stealer behaviour so the demo runs fully offline. It is a
simulation of the detection pipeline, not the real product.
"""
from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class Rule:
    name: str  # WithSecure-style detection name
    mitre: str
    severity: str
    narrative: str
    advice: str
    category: str  # telemetry category this rule watches
    match: str  # value of details.match to trigger on


RULES: list[Rule] = [
    Rule(
        name="HEUR:Suspicious.PowerShell.DownloadCradle",
        mitre="T1059.001",
        severity="high",
        category="process",
        match="download_cradle",
        narrative=(
            "A hidden PowerShell process executed a download-and-execute one-liner "
            "typical of ClickFix social-engineering pages. This is how the Lumma "
            "infostealer was delivered to the workstation."
        ),
        advice="Block the process, reset the user's browser-stored credentials, hunt for the C2 domain.",
    ),
    Rule(
        name="Trojan:W32/Lumma.Stealer!CredentialAccess",
        mitre="T1555.003",
        severity="critical",
        category="file",
        match="browser_credentials_read",
        narrative=(
            "A process read Chromium browser credential stores ('Login Data', 'Local State', "
            "'Cookies') — the signature behaviour of the Lumma infostealer harvesting saved "
            "passwords and session cookies."
        ),
        advice="Isolate the host. Assume all browser-saved credentials and cookies compromised. Force password reset + session revocation.",
    ),
    Rule(
        name="Gen:Heur.CryptoWalletStealer",
        mitre="T1555.003",
        severity="high",
        category="file",
        match="wallet_files_read",
        narrative=(
            "Access to cryptocurrency wallet files and browser wallet extensions "
            "was observed — consistent with Lumma's wallet-grabbing module."
        ),
        advice="Isolate host, treat exposed wallets as compromised.",
    ),
    Rule(
        name="Trojan:W32/Lumma.SelfDelete",
        mitre="T1070.004",
        severity="medium",
        category="file",
        match="staging_cleanup",
        narrative=(
            "The suspicious process deleted its staging directory after exfiltration — "
            "an anti-forensic technique used by commodity stealers."
        ),
        advice="Capture memory/disk forensics before further cleanup.",
    ),
    Rule(
        name="Net:W32/Lumma.C2Exfil",
        mitre="T1041",
        severity="critical",
        category="network",
        match="c2_exfil",
        narrative=(
            "Bulk POST traffic to a Lumma-style gate endpoint (/gate.php) carrying "
            "compressed browser data — live exfiltration to attacker infrastructure."
        ),
        advice="Block the C2 domain at the firewall, isolate host, start incident response.",
    ),
    Rule(
        name="Trojan:Win32.Persistence.SchTasks",
        mitre="T1053.005",
        severity="high",
        category="process",
        match="persistence_created",
        narrative=(
            "The suspicious process created a scheduled task for persistence, "
            "ensuring the stealer runs again at logon."
        ),
        advice="Remove the scheduled task, isolate host, check for lateral movement.",
    ),
]


def evaluate(event: dict) -> list[Rule]:
    """Return rules triggered by a single telemetry event."""
    details = event.get("details") or {}
    hit = details.get("match")
    return [r for r in RULES if r.category == event.get("category") and r.match == hit]
