"""End-to-end scenario tests for the LummaSimulator demo platform.

Requires all services running (`./demo.sh start`). These tests walk the
complete demo story:

  1. alex (password only)  -> phished via AiTM   -> session stolen
  2. riley (password+TOTP)  -> phished, OTP relayed -> session stolen
  3. sam  (password+YubiKey) -> phishing relay BLOCKED (origin binding)
  4. Lumma agent deployed via ClickFix -> WithSecure detections ->
     Merlon SOC isolate -> exfiltration blocked

Uses the VirtualYubiKey software authenticator (same crypto path as a real
YubiKey: ES256, origin-bound client data, signature over authData+clientDataHash).
"""
from __future__ import annotations

import time

import httpx
import pytest

from tests.virtual_yubikey import PORTAL, PORTAL_ORIGIN, VirtualYubiKey

PHISH = "http://127.0.0.1:8001"
C2 = "http://127.0.0.1:8002"
SOC = "http://127.0.0.1:8003"
EDR = "http://127.0.0.1:8004"
ENDPOINT = "http://127.0.0.1:8005"

PHISH_ORIGIN = "http://phish.localhost:8001"


def wait_for(fn, timeout=15, interval=0.5, desc="condition"):
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        last = fn()
        if last:
            return last
        time.sleep(interval)
    raise AssertionError(f"timeout waiting for {desc}; last={last}")


@pytest.fixture(scope="session", autouse=True)
def services_up():
    for url in (PORTAL, PHISH, C2, SOC, EDR, ENDPOINT):
        r = httpx.get(f"{url}/healthz", timeout=5)
        assert r.status_code == 200, f"{url} not healthy: {r.status_code}"
    yield


# ---------------------------------------------------------------- scenario 1
def test_alex_password_only_phished_and_session_stolen():
    """AiTM: victim with password-only account loses the session cookie."""
    # Victim 'opens the phishing mail' and submits credentials to the phish site
    r = httpx.post(
        f"{PHISH}/login",
        data={"email": "alex@northwind.demo", "password": "Tr0ub4dor&3"},
        headers={"user-agent": "Mozilla/5.0 (Victim Browser)"},
        follow_redirects=False,
    )
    assert r.status_code == 200, r.text
    assert r.json()["next"] == "/done"

    # The C2 should now hold a stolen session
    def c2_has_session():
        state = httpx.get(f"{C2}/api/state", timeout=5).json()
        return next(
            (s for s in state["sessions"] if s["user"] == "alex@northwind.demo"), None
        )

    stolen = wait_for(c2_has_session, desc="alex stolen session in C2")
    assert stolen["cookie"], "session cookie missing in C2 loot"

    # Attacker opens the mailbox with the stolen cookie
    r = httpx.get(f"{C2}/session/{stolen['id']}/mail", timeout=10)
    assert r.status_code == 200, r.text
    assert "Northwind" in r.text and "Inbox" in r.text, "stolen mailbox did not render"
    assert "ATTACKER VIEW" in r.text


# ---------------------------------------------------------------- scenario 2
def test_riley_totp_relayed_session_stolen():
    """AiTM: TOTP does NOT save you — the proxy relays the code in real time."""
    r = httpx.post(
        f"{PHISH}/login",
        data={"email": "riley@northwind.demo", "password": "Northwind2024!"},
        headers={"user-agent": "Mozilla/5.0 (Victim Browser)"},
    )
    assert r.status_code == 200
    assert r.json()["next"] == "/totp"

    # Victim reads the code from the authenticator app (demo helper)
    code = httpx.get(
        f"{PORTAL}/api/demo/totp", params={"email": "riley@northwind.demo"}, timeout=5
    ).json()["code"]
    r = httpx.post(
        f"{PHISH}/totp",
        data={"email": "riley@northwind.demo", "code": code},
        headers={"user-agent": "Mozilla/5.0 (Victim Browser)"},
    )
    assert r.status_code == 200 and r.json().get("ok")

    def c2_has_session():
        state = httpx.get(f"{C2}/api/state", timeout=5).json()
        return next(
            (s for s in state["sessions"] if s["user"] == "riley@northwind.demo"), None
        )

    stolen = wait_for(c2_has_session, desc="riley stolen session in C2")
    assert "TOTP" in stolen["via"]


# ---------------------------------------------------------------- scenario 3
def test_sam_yubikey_registration_and_aitm_block():
    """The YubiKey scenario: register a (virtual) passkey, then watch the
    phishing relay fail on origin binding — and the password alone is useless."""

    # Step 1: one-time enrollment login (password) to register the key
    with httpx.Client(base_url=PORTAL, timeout=10) as c:
        r = c.post(
            "/login",
            data={"email": "sam@northwind.demo", "password": "Yubi!Demo2024"},
        )
        assert r.status_code == 200, r.text
        assert r.json()["stage"] == "passkey_enrollment"

        # Register the virtual YubiKey
        key = VirtualYubiKey()
        options = c.post("/api/webauthn/register/options").json()
        assert options["ok"], options
        reg_response = key.register_response(options, origin=PORTAL_ORIGIN)
        r = c.post(
            "/api/webauthn/register",
            json={"label": "Virtual YubiKey (test)", "response": reg_response},
        )
        assert r.status_code == 200, r.text
        assert r.json()["ok"]

    # Step 2: from now on, password-only login is refused
    r = httpx.post(
        f"{PORTAL}/login",
        data={"email": "sam@northwind.demo", "password": "Yubi!Demo2024"},
    )
    assert r.status_code == 200
    assert r.json()["stage"] == "passkey"

    # Step 3: victim falls for the phishing page
    r = httpx.post(
        f"{PHISH}/login",
        data={"email": "sam@northwind.demo", "password": "Yubi!Demo2024"},
        headers={"user-agent": "Mozilla/5.0 (Victim Browser)"},
    )
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["next"] == "/webauthn", body
    challenge = body["challenge"]

    # Step 4: the phish relay gets the REAL challenge from the portal — but the
    # victim's browser refuses to sign it on the phishing origin. We simulate
    # the *forged relay attempt*: an assertion created on the phishing origin.
    # (In a browser this fails even earlier — navigator.credentials.get throws
    # because rpId 'portal.localhost' is not registrable from phish.localhost.)
    key2 = VirtualYubiKey()  # same key material as key? No — use the SAME key:
    key2 = key  # the enrolled key
    fake_options = {"challenge": challenge}
    forged = key2.assertion_response(fake_options, origin=PHISH_ORIGIN)

    r = httpx.post(
        f"{PORTAL}/api/webauthn/authenticate",
        json={"email": "sam@northwind.demo", "response": forged},
    )
    assert r.status_code == 401, "portal must reject the phishing-origin assertion"
    assert r.json().get("error") == "origin_mismatch", r.json()

    # The portal logged the block to the SOC
    def soc_has_block():
        state = httpx.get(f"{SOC}/api/state", timeout=5).json()
        return any(
            e["kind"] == "webauthn_origin_rejected" for e in state["events"]
        )

    assert wait_for(soc_has_block, desc="SOC informed of origin-mismatch block")

    # The phish site itself reports the browser-level refusal
    r = httpx.post(
        f"{PHISH}/webauthn/failed",
        json={"email": "sam@northwind.demo", "error": "NotAllowedError (simulated browser refusal)"},
    )
    assert r.status_code == 200

    def c2_has_block():
        state = httpx.get(f"{C2}/api/state", timeout=5).json()
        return any(
            w["kind"] == "webauthn_blocked" for w in state["webauthn"]
        )

    assert wait_for(c2_has_block, desc="C2 shows the blocked passkey attempt")

    # And a REAL login with the same key works fine on the real origin
    options = httpx.get(
        f"{PORTAL}/api/webauthn/authenticate/options",
        params={"email": "sam@northwind.demo"},
        timeout=5,
    ).json()
    assert options["ok"]
    assertion = key.assertion_response(options, origin=PORTAL_ORIGIN)
    r = httpx.post(
        f"{PORTAL}/api/webauthn/authenticate",
        json={"email": "sam@northwind.demo", "response": assertion},
    )
    assert r.status_code == 200, r.text
    assert r.json()["stage"] == "done"
    assert "nw_session" in r.cookies


# ---------------------------------------------------------------- scenario 4
def test_lumma_stealer_detected_and_contained():
    """ClickFix -> Lumma simulator -> WithSecure detections -> SOC containment."""

    # 'Victim runs the command' on the ClickFix page
    r = httpx.post(f"{PHISH}/api/clickfix/deploy", json={"user": "alex@northwind.demo"}, timeout=15)
    assert r.status_code == 200, r.text
    assert r.json()["ok"]

    # WithSecure-style detections must fire — wait for each key detection
    def wait_detection(substr, timeout=30):
        def _check():
            alerts = httpx.get(f"{EDR}/api/alerts", timeout=5).json()["alerts"]
            return any(substr in a["detection"] for a in alerts) or None
        return wait_for(_check, timeout=timeout, desc=f"detection {substr}")

    assert wait_detection("PowerShell")
    assert wait_detection("Lumma")
    assert wait_detection("C2Exfil")
    assert wait_detection("Persistence")

    # Exfiltrated loot visible in the C2
    def exfil():
        state = httpx.get(f"{C2}/api/state", timeout=5).json()
        return state["exfil"] or None

    assert wait_for(exfil, timeout=30, desc="C2 received exfil")

    # SOC saw the alerts
    def soc_alerts():
        state = httpx.get(f"{SOC}/api/state", timeout=5).json()
        return state["alerts"] or None

    alerts = wait_for(soc_alerts, timeout=30, desc="SOC received alerts")
    assert any(a["source"] == "withsecure-elements" for a in alerts)

    # SOC isolates the host
    r = httpx.post(f"{SOC}/api/respond/contain", json={"host": "WS-DEMO-01"}, timeout=5)
    assert r.status_code == 200 and r.json()["ok"]

    # A new deploy is now refused on the contained host
    r = httpx.post(
        f"{ENDPOINT}/deploy",
        json={"origin": "clickfix", "user": "alex@northwind.demo"},
        timeout=15,
    )
    assert r.status_code == 423, r.text
    assert "contained" in r.json()["error"]

    # Release for future test runs
    r = httpx.post(f"{SOC}/api/respond/release", json={"host": "WS-DEMO-01"}, timeout=5)
    assert r.status_code == 200


# ---------------------------------------------------------------- scenario 5
def test_soc_saw_the_full_story():
    """SOC state should contain portal-side signals from all scenarios."""
    state = httpx.get(f"{SOC}/api/state", timeout=5).json()
    kinds = [e["kind"] for e in state["events"]]
    assert "login_success" in kinds  # alex/riley phished logins
    assert "webauthn_origin_rejected" in kinds  # sam's YubiKey save
    assert "mfa_enforced_passkey" in kinds  # password refused for sam
