"""Quick in-process smoke test of the portal (no other services required)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from fastapi.testclient import TestClient

import portal.app as papp  # noqa: E402

client = TestClient(papp.app)
client.__enter__()  # trigger startup (seeding, sweeper thread)

# --- login page renders
r = client.get("/login")
assert r.status_code == 200 and "Northwind" in r.text, r.status_code

# --- alex: password only
r = client.post("/login", data={"email": "alex@northwind.demo", "password": "Tr0ub4dor&3"})
d = r.json()
print("alex login:", r.status_code, d)
assert d["stage"] == "done"
assert "nw_session" in client.cookies
r = client.get("/mail")
assert "Inbox" in r.text or "inbox" in r.text.lower()
print("alex mailbox OK, has", r.text.count("mail-item"), "mail items")

# --- relay-suspect: httpx/TestClient sends no Sec-Fetch headers => flagged
r = client.post("/login", data={"email": "alex@northwind.demo", "password": "wrong"})
assert r.status_code == 400

# --- riley: password + TOTP
r = client.post("/login", data={"email": "riley@northwind.demo", "password": "Northwind2024!"})
d = r.json()
print("riley stage:", d)
assert d["stage"] == "totp"
r = client.get("/api/demo/totp", params={"email": "riley@northwind.demo"})
code = r.json()["code"]
print("current code:", code)
r = client.post("/login/totp", data={"email": "riley@northwind.demo", "code": code})
print("riley totp:", r.status_code, r.json())
assert r.status_code == 200

# --- sam: password ok -> passkey stage, no creds yet
r = client.post("/login", data={"email": "sam@northwind.demo", "password": "Yubi!Demo2024"})
d = r.json()
print("sam stage:", r.status_code, d)
assert d.get("stage") == "passkey_setup"

# --- webauthn options for sam (no creds -> 400)
r = client.get("/api/webauthn/authenticate/options", params={"email": "sam@northwind.demo"})
print("sam options (expect 400):", r.status_code, r.json())

# --- security events recorded
evs = papp.store.recent_events(20)
print("\nsecurity events:")
for e in evs:
    print(" ", e["kind"], "-", e["details"].get("title", ""))

print("\nPORTAL SMOKE TEST PASSED")
