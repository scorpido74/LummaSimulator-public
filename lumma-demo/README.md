# LummaSimulator — local demo environment

A self-contained, **fully simulated** security-awareness demo platform that shows
why **YubiKeys (phishing-resistant MFA)** matter, with your showcase partners in
their roles:

| Role | Partner | Implemented as |
|---|---|---|
| Detection & response (SOC) | **Merlon Security** | `soc/` — SOC dashboard: alert timeline, incident response (isolate host, kill process) |
| Endpoint protection | **WithSecure** | `edr/` — WithSecure™ Elements-style EDR sensor + detection rules for the Lumma stealer |
| Phishing-resistant MFA | **Yubico** | real **WebAuthn/FIDO2** on the victim portal, built with Yubico's own `python-fido2` library |
| The threat | **Lumma stealer** | `agent/lumma_agent.py` — a contained simulator of Lumma's behaviour (browser credential theft, exfil to C2, anti-forensics, persistence) |

Everything runs locally, binds to `127.0.0.1`, and only ever touches **fake demo
data**. See [Safety](#safety--scope).

> Based on the LummaSimulator concept by Infinigate (phishing, ClickFix, session
> theft, passkey protection, central dashboard, anonymous demo accounts,
> automatic cleanup) — rebuilt here as an open local demo environment for the
> Merlon × Yubico × WithSecure showcase.

---

## What the demo shows

Three colleagues at the fictional *Northwind Logistics* receive a phishing mail:

| User | Protection | What happens when phished |
|---|---|---|
| `alex@northwind.demo` | password only | AiTM proxy steals the **live session** → attacker reads his mailbox |
| `riley@northwind.demo` | password + TOTP app | OTP is relayed in real time → **session stolen anyway** (TOTP ≠ anti-phishing) |
| `sam@northwind.demo` | password + **YubiKey** | Browser refuses the ceremony on the phishing origin; relayed assertion is **rejected by origin binding** → attacker gets nothing. Password alone is useless. |

Then the stealer lands on the workstation via a **ClickFix** page (fake captcha →
"press Win+R and paste…"): the Lumma simulator harvests the (fake) browser
profile and exfiltrates it to the C2 — while **WithSecure-style detections** fire
and the **Merlon SOC** isolates the host, cutting off further exfiltration.

**The story:** *WithSecure detects, Merlon responds, Yubico prevents.*

---

## Architecture

```mermaid
flowchart TD
    subgraph victim[" Victim (browser) "]
        V[Employee\nusing Chrome]
    end

    subgraph apps[" Local services "]
        P[Portal\nNorthwind Secure Mail\nWebAuthn + YubiKey\n:8000]
        F[Phish site\nAiTM relay\n:8001]
        C[C2 console\nstolen loot\n:8002]
        S[Merlon SOC\ndetection & response\n:8003]
        E[WithSecure-style EDR\nsensor + rules\n:8004]
        W[Endpoint sim\nWS-DEMO-01\nfake browser profile\n:8005]
        A[Lumma simulator\ncontained agent]
    end

    V -->|1. clicks phishing mail| F
    V -->|2. password / TOTP / passkey relayed| F
    F <-->|relays to real portal| P
    F -->|3. stolen session / blocked passkey| C
    V -->|normal login with YubiKey| P
    subgraph infection
        F -->|ClickFix 'run this command'| C
        C -->|deploy payload| W
        A -->|runs on| W
        A -->|5. telemetry\n(process/file/net)| E
        A -->|6. exfiltration| C
    end
    E -->|7. detections| S
    P -->|security events\n(login, AiTM block, takeover)| S
    S -->|8. isolate / kill| E
    E -->|containment| W
```

All WebAuthn credentials are **origin-bound to `portal.localhost`** — the exact
property that defeats the phishing proxy. The portal additionally verifies the
assertion origin strictly (scheme + host + port).

## Quick start

Requirements: Python 3.11+ and a Chromium/Firefox browser (WebAuthn on
`http://*.localhost` needs a browser that treats these as secure contexts —
Chrome and Firefox do).

```bash
cd lumma-demo
./demo.sh start     # creates .venv, installs deps, starts all 6 services
./demo.sh urls      # print all URLs
```

Prefer Docker? `docker compose up --build` (same URLs, state in volumes,
`docker compose down -v` to reset).

Optional hardening for browsers: `./demo.sh hosts` pins the `*.localhost`
names to `127.0.0.1` in `/etc/hosts` (only needed if your browser/OS resolves
them oddly).

### URLs

| Service | URL |
|---|---|
| Victim portal (mail + YubiKey) | http://portal.localhost:8000 |
| Phishing site (AiTM) | http://phish.localhost:8001 |
| ClickFix page | http://phish.localhost:8001/clickfix |
| Attacker C2 console | http://c2.localhost:8002 |
| Merlon SOC dashboard | http://soc.localhost:8003 |
| WithSecure-style EDR view | http://edr.localhost:8004 |
| Endpoint simulator (workstation) | http://endpoint.localhost:8005 |

### Demo accounts (anonymous, auto-seeded)

| Email | Password | MFA |
|---|---|---|
| `alex@northwind.demo` | `Tr0ub4dor&3` | none |
| `riley@northwind.demo` | `Northwind2024!` | TOTP (code shown on screen in demo mode) |
| `sam@northwind.demo` | `Yubi!Demo2024` | YubiKey / passkey (first login = enrollment) |

`./demo.sh reset` wipes everything (sessions, loot, alerts, demo data) for a
fresh run.

---

## Runbook — the live demo (15 min)

**Setup before the audience arrives**

1. `./demo.sh reset && ./demo.sh start`
2. Open http://portal.localhost:8000 in Chrome, sign in as **sam**
   (first login = enrollment) → *Security settings* → **Register security key**
   → insert the **real YubiKey**, follow the browser prompt.
   (No YubiKey at hand? The automated tests use a *virtual* software YubiKey —
   `./demo.sh test` — but live nothing beats the real touch.)
3. Sign out. Open the SOC dashboard http://soc.localhost:8003 on a second
   screen/projector side. Have the C2 console http://c2.localhost:8002 ready.

**Scene 1 — the phish (Alex, password only)**

1. In the portal, sign in as **alex**, open the mail *"Action required: verify
   your mailbox"* — point out the lookalike sender and the urgent tone.
2. Click the button. Note the address bar: `phish.localhost:8001` — in reality
   a lookalike domain like `northwind-secure-verify.com`.
3. Enter alex's credentials. You land on "You are signed in".
4. Switch to the **C2 console**: a stolen session appeared. Click
   **Open victim mailbox** → the attacker reads Alex's mail in his own web UI.
5. SOC: events like *login (password only)* and *session takeover suspected*
   appear — detection after the fact.

**Scene 2 — TOTP doesn't save you (Riley)**

1. Sign out, sign in as **riley** (or reuse the phishing mail).
2. Phished again: the proxy now asks for the 6-digit code. The real code is
   shown by the demo helper on the portal login page (or any authenticator).
3. Enter it → C2 gets a stolen session again, *via TOTP relayed in real time*.
   Message: one-time codes are phishable; they're not the fix.

**Scene 3 — the YubiKey save (Sam)**

1. Open the phishing mail as **sam** (sign in at the portal first if needed —
   with the YubiKey, showing the ceremony on the real site).
2. On the phishing page, enter sam's password (it is "stolen" — that's fine).
3. The proxy tries to relay the WebAuthn challenge — the browser **refuses**
   (`NotAllowedError`): the key is bound to `portal.localhost`. The page shows
   "verification failed".
4. C2 console: the passkey encounter is listed as **BLOCKED by origin binding**.
   The attacker has sam's password and *nothing else*.
5. SOC: *"Phishing-resistant MFA blocked an AiTM relay"*.
6. Bonus: try logging in as sam on the real portal with just the password —
   refused. The password is useless without the key.

**Scene 4 — Lumma lands (ClickFix → detection → response)**

1. Visit http://phish.localhost:8001/clickfix — the fake captcha telling the
   user to press <kbd>Win</kbd>+<kbd>R</kbd> and paste a PowerShell one-liner.
   Click "I have completed the steps" (simulates the victim running it).
2. Watch the **EDR view / SOC**: `HEUR:Suspicious.PowerShell.DownloadCradle` →
   `Trojan:W32/Lumma.Stealer!CredentialAccess` → `Gen:Heur.CryptoWalletStealer`
   → `Net:W32/Lumma.C2Exfil` light up in sequence.
3. C2: the exfiltrated browser profile appears (fake data).
4. In the **SOC**, click **Isolate WS-DEMO-01** — containment kicks in;
   try deploying again from the endpoint simulator: *refused*.
   (EDR view: http://edr.localhost:8004, workstation: http://endpoint.localhost:8005)

**Wrap-up**

- WithSecure (detection) + Merlon (response) catch the stealer **after** it runs.
- The YubiKey stopped the credential/session theft **before** it happened.
- Together: defense in depth. Passwords are compromised by design — assume it,
  and make stolen credentials worthless.

---

## Automated verification

```bash
./demo.sh test
```

Runs the complete story against a fresh environment:

1. Alex phished → session stolen → attacker mailbox renders
2. Riley TOTP relayed → session stolen
3. Sam: virtual YubiKey (software ES256 authenticator, same WebAuthn code path
   as a real key) registered → phishing-origin assertion **rejected** (origin
   mismatch) → SOC informed → C2 shows the block → the same key still works on
   the real portal
4. ClickFix → Lumma simulator → ≥4 WithSecure-style detections → exfil in C2
   → SOC containment blocks further deploys
5. SOC saw the full picture

The tests use `tests/virtual_yubikey.py` — a software P-256 authenticator that
builds client data with a chosen *origin* and signs exactly like a browser with
a YubiKey. That is how the AiTM block is proven cryptographically, not just in UI.

---

## Repo layout

```
lumma-demo/
├── demo.sh              # start/stop/reset/test launcher
├── docker-compose.yml   # optional container deployment
├── Dockerfile
├── common/              # shared config, helpers, theme
├── portal/              # victim app: login (password/TOTP/passkey), mailbox, security settings
├── phish/               # AiTM phishing replica + ClickFix page
├── c2/                  # attacker console: stolen sessions, exfil loot, stolen-mail view
├── edr/                 # WithSecure-style sensor: telemetry ingestion + detection rules
├── soc/                 # Merlon SOC dashboard: alerts, events, response actions
├── endpoint_sim/        # fake workstation WS-DEMO-01 (hosts the fake browser profile)
├── agent/               # Lumma simulator (contained) + fake profile generator
├── tests/               # e2e scenario tests + virtual YubiKey
└── tools/               # standalone WebAuthn proof, portal smoke test
```

## Safety & scope

- **The "malware" is a simulator.** `agent/lumma_agent.py` only reads the fake
  browser profile under `state/endpoint/profile` (it refuses any other path),
  only exfiltrates to the local C2, and only emits simulated telemetry.
- **All victim data is fake** — generated by `agent/profile_gen.py`.
- **No real product integration**: WithSecure™ Elements detections and the
  Merlon SOC are simulations of the partner roles so the demo runs offline.
  For a production-grade detection exercise, run the real WithSecure agent on a
  disposable VM and point it at the same simulated behaviours.
- WebAuthn is real (FIDO2/CTAP2 via the browser, verified server-side with
  Yubico's `python-fido2`), so a real YubiKey behaves exactly as in production.
- All services bind `127.0.0.1` only.

## Notes & gotchas

- Use **Chrome or Firefox**; Safari may not treat `http://*.localhost` as a
  secure context (WebAuthn would be unavailable). If hostname resolution is
  odd on your machine, run `./demo.sh hosts`.
- The portal flags logins without browser `Sec-Fetch` headers as relay-suspect
  (that's how httpx-based relays look) and session reuse with a different
  user-agent as session-takeover — these are the realistic SOC signals.
- Ports are configurable via env (`PORTAL_PORT`, etc.); internal URLs via
  `*_URL` (see `common/config.py`).
