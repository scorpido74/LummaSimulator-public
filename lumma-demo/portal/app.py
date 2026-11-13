"""Northwind Secure Mail — the demo 'victim' application.

Login options:
  * password only (alex)
  * password + TOTP authenticator app (riley) — phishable by an AiTM proxy
  * password + YubiKey/passkey (sam)  — phishing-resistant (WebAuthn origin binding)

Security-relevant events are streamed to the Merlon SOC dashboard.
WebAuthn is implemented with Yubico's python-fido2 library.
"""
from __future__ import annotations

import sys
import threading
import time
from pathlib import Path

_HERE = Path(__file__).resolve().parent
for _p in (_HERE, _HERE.parent):
    if str(_p) not in sys.path:
        sys.path.insert(0, str(_p))

import httpx
import uvicorn
from fastapi import FastAPI, Form, Request, WebSocket
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

import store
import totp as totp_mod
from common import config
from common.util import ROOT, b64u, b64u_dec, now_iso, post_json, uid
from webauthn_helper import WebAuthnManager

BASE = Path(__file__).resolve().parent

app = FastAPI(title="Northwind Secure Mail")
app.mount("/static", StaticFiles(directory=BASE / "static"), name="static")
app.mount("/theme", StaticFiles(directory=ROOT / "common" / "static"), name="theme")
templates = Jinja2Templates(directory=BASE / "templates")

SESSION_COOKIE = "nw_session"
wa = WebAuthnManager(config.RP_ID, config.RP_NAME, config.PORTAL_ORIGIN)


@app.on_event("startup")
def _startup() -> None:
    store.init()
    t = threading.Thread(target=_sweep_pending, daemon=True)
    t.start()


def _sweep_pending() -> None:
    """Unfulfilled WebAuthn challenges => likely AiTM relay phishing attempt."""
    while True:
        time.sleep(10)
        for row in store.stale_pending(45):
            store.mark_fired(row["challenge"])
            event(
                "webauthn_relay_suspected",
                row["email"],
                {
                    "title": "WebAuthn challenge requested but never completed",
                    "detail": (
                        "An authentication challenge was issued but no valid assertion "
                        "followed — consistent with a phishing proxy that cannot relay "
                        "the ceremony (origin binding)."
                    ),
                    "purpose": row["purpose"],
                    "severity": "high",
                },
            )


# ---------------------------------------------------------------- helpers
def event(kind: str, email: str | None, details: dict | None = None) -> None:
    store.add_event(kind, email, details)
    post_json(
        config.SOC_WEBHOOK,
        {
            "source": "portal",
            "kind": kind,
            "email": email,
            "details": details or {},
            "ts": now_iso(),
        },
        timeout=1.5,
    )


def current_user(request: Request) -> dict | None:
    token = request.cookies.get(SESSION_COOKIE)
    if not token:
        return None
    sess = store.get_session(token)
    if not sess:
        return None
    user = store.get_user(sess["email"])

    # Session-takeover signal: same session used from a different client.
    ua = request.headers.get("user-agent")
    if sess.get("ua") and ua and ua != sess["ua"]:
        details = {
            "title": "Session takeover suspected",
            "detail": (
                "An existing session is being used from a different client "
                "(user-agent changed mid-session) — typical of a stolen session cookie."
            ),
            "severity": "critical",
            "old_ua": (sess.get("ua") or "")[:80],
            "new_ua": ua[:80],
        }
        event("session_takeover_suspected", sess["email"], details)
        # Re-anchor so we alert once per UA change.
        import sqlite3

        with store.db() as conn:
            conn.execute(
                "UPDATE sessions SET ua=? WHERE token=?", (ua, token)
            )

    store.touch_session(token)
    return user


def looks_like_relay(request: Request) -> bool:
    """Browsers always send Sec-Fetch-* headers on form posts. Relays don't."""
    return request.headers.get("sec-fetch-site") is None


def _ctx(request: Request, **kw) -> dict:
    user = current_user(request)
    kw.setdefault("user", user)
    kw.setdefault("demo_mode", config.DEMO_MODE)
    kw.setdefault("rp", {"id": config.RP_ID, "origin": config.PORTAL_ORIGIN})
    return kw


# ---------------------------------------------------------------- routes
@app.get("/", response_class=HTMLResponse)
def index(request: Request):
    if current_user(request):
        return RedirectResponse("/mail", status_code=302)
    return RedirectResponse("/login", status_code=302)


@app.get("/healthz")
def healthz():
    return {"ok": True, "service": "portal"}


@app.get("/login", response_class=HTMLResponse)
def login_page(request: Request):
    if current_user(request):
        return RedirectResponse("/mail", status_code=302)
    return templates.TemplateResponse(request, "login.html", _ctx(request, error=None))


@app.post("/login")
def login(request: Request, email: str = Form(...), password: str = Form(...)):
    user = store.get_user(email)
    relay = looks_like_relay(request)
    if not user or not store.verify_password(password, user["password_hash"]):
        event(
            "login_failed",
            email,
            {"title": "Failed login", "detail": "Invalid credentials", "severity": "low"},
        )
        return JSONResponse({"ok": False, "error": "Invalid email or password."}, 400)

    if user["requires_passkey"]:
        if not store.credentials_for(email):
            # One-time enrollment login: no passkey registered yet, so we allow
            # password login ONCE to register a key (like out-of-band enrollment).
            token = store.create_session(email, request.headers.get("user-agent"))
            event(
                "enrollment_login",
                email,
                {
                    "title": "Enrollment login (no passkey yet)",
                    "detail": (
                        "Password-only login allowed ONCE because no passkey is registered "
                        "yet. The user must register a YubiKey/passkey immediately."
                    ),
                    "severity": "medium",
                    "relay_suspect": relay,
                },
            )
            resp = JSONResponse({"ok": True, "stage": "passkey_enrollment"})
            resp.set_cookie(SESSION_COOKIE, token, httponly=True, samesite="lax", path="/")
            return resp

        event(
            "mfa_enforced_passkey",
            email,
            {
                "title": "Password accepted, passkey required",
                "detail": (
                    "Password-only login was refused because this account requires a "
                    "YubiKey/passkey second factor. Even a stolen password is useless."
                ),
                "severity": "medium",
                "relay_suspect": relay,
            },
        )
        return {"ok": True, "stage": "passkey"}

    if user["totp_secret"]:
        return {"ok": True, "stage": "totp"}

    token = store.create_session(email, request.headers.get("user-agent"))
    event(
        "login_success",
        email,
        {
            "title": "Login (password only)",
            "detail": (
                "Session created with password only — no phishing-resistant factor present."
                + (" Request pattern suggests a relay/proxy." if relay else "")
            ),
            "severity": "medium" if relay else "low",
            "method": "password",
            "relay_suspect": relay,
        },
    )
    resp = JSONResponse({"ok": True, "stage": "done"})
    resp.set_cookie(
        SESSION_COOKIE, token, httponly=True, samesite="lax", path="/"
    )
    return resp


@app.post("/login/totp")
def login_totp(request: Request, email: str = Form(...), code: str = Form(...)):
    user = store.get_user(email)
    if not user or not user.get("totp_secret"):
        return JSONResponse({"ok": False, "error": "TOTP not enabled for this account."}, 400)
    if not totp_mod.verify(user["totp_secret"], code):
        event(
            "login_failed",
            email,
            {"title": "Failed TOTP verification", "detail": "Invalid one-time code.", "severity": "low"},
        )
        return JSONResponse({"ok": False, "error": "Invalid code."}, 400)
    token = store.create_session(email, request.headers.get("user-agent"))
    relay = looks_like_relay(request)
    event(
        "login_success",
        email,
        {
            "title": "Login (password + TOTP)",
            "detail": (
                "Session created. TOTP codes are relayed in real time by modern phishing "
                "proxies — the one-time code did not stop the attacker."
                + (" Request pattern suggests a relay/proxy." if relay else "")
            ),
            "severity": "high" if relay else "medium",
            "method": "totp",
            "relay_suspect": relay,
        },
    )
    resp = JSONResponse({"ok": True, "stage": "done"})
    resp.set_cookie(SESSION_COOKIE, token, httponly=True, samesite="lax", path="/")
    return resp


# ---------------- WebAuthn: passkey / YubiKey -----------------------------
@app.get("/api/webauthn/authenticate/options")
def webauthn_auth_options(request: Request, email: str):
    user = store.get_user(email)
    if not user:
        return JSONResponse({"ok": False, "error": "Unknown user."}, 404)
    creds = store.credentials_for(email)
    if not creds:
        return JSONResponse({"ok": False, "error": "No passkey registered."}, 400)
    payload, challenge = wa.authentication_options([c["public_key"] for c in creds])
    store.add_pending(challenge, "authenticate", email)
    return {"ok": True, **payload}


@app.post("/api/webauthn/authenticate")
async def webauthn_auth(request: Request):
    body = await request.json()
    email = body.get("email")
    response = body.get("response")
    user = store.get_user(email)
    if not user:
        return JSONResponse({"ok": False, "error": "Unknown user."}, 404)
    creds = store.credentials_for(email)
    if not creds:
        return JSONResponse({"ok": False, "error": "No passkey registered."}, 400)

    from fido2.webauthn import AttestedCredentialData

    challenge = _challenge_from_client_data(response)
    if not challenge:
        return JSONResponse({"ok": False, "error": "Malformed response."}, 400)

    state = {"challenge": challenge, "user_verification": None}
    try:
        matched = wa.complete_authentication(
            state,
            [AttestedCredentialData(c["public_key"]) for c in creds],
            response,
        )
    except ValueError as e:
        msg = str(e)
        origin_rejected = "origin" in msg.lower()
        event(
            "webauthn_origin_rejected" if origin_rejected else "webauthn_failed",
            email,
            {
                "title": (
                    "Phishing-resistant MFA blocked an AiTM relay"
                    if origin_rejected
                    else "Passkey verification failed"
                ),
                "detail": (
                    "A WebAuthn assertion was submitted with an origin that does not "
                    "match this service (assertion was created on the phishing site). "
                    "Origin binding — enforced by the YubiKey + WebAuthn — rejected it. "
                    f"({msg})"
                    if origin_rejected
                    else msg
                ),
                "severity": "high" if origin_rejected else "low",
            },
        )
        kind = "origin_mismatch" if origin_rejected else "verification_failed"
        return JSONResponse(
            {"ok": False, "error": kind, "detail": msg}, 401
        )

    store.consume_pending(challenge)
    token = store.create_session(email, request.headers.get("user-agent"))
    event(
        "login_success",
        email,
        {
            "title": "Login (password + YubiKey passkey)",
            "detail": "Session created with phishing-resistant MFA (WebAuthn, origin-bound).",
            "severity": "info",
            "method": "passkey",
        },
    )
    resp = JSONResponse({"ok": True, "stage": "done"})
    resp.set_cookie(SESSION_COOKIE, token, httponly=True, samesite="lax", path="/")
    return resp


def _challenge_from_client_data(response: dict) -> str | None:
    import base64
    import json

    try:
        cdj = b64u_dec(response["response"]["clientDataJSON"])
        challenge = json.loads(cdj)["challenge"]
        # normalize to the same encoding the state uses
        return base64.urlsafe_b64encode(
            base64.urlsafe_b64decode(challenge + "=" * (-len(challenge) % 4))
        ).decode().rstrip("=")
    except Exception:  # noqa: BLE001
        return None


# ---------------- mailbox ----------------
@app.get("/mail", response_class=HTMLResponse)
def mail_list(request: Request):
    user = current_user(request)
    if not user:
        return RedirectResponse("/login", status_code=302)
    mails = store.mail_for(user["email"])
    return templates.TemplateResponse(
        request, "mail.html", _ctx(request, mails=mails, active="inbox")
    )


@app.get("/mail/{mail_id}", response_class=HTMLResponse)
def mail_detail(request: Request, mail_id: int):
    user = current_user(request)
    if not user:
        return RedirectResponse("/login", status_code=302)
    item = store.mail_item(user["email"], mail_id)
    if not item:
        return RedirectResponse("/mail", status_code=302)
    return templates.TemplateResponse(
        request, "mail.html", _ctx(request, mails=[], item=item, active="inbox")
    )


@app.post("/logout")
def logout(request: Request):
    token = request.cookies.get(SESSION_COOKIE)
    user = current_user(request)
    if token:
        store.delete_session(token)
    if user:
        event("logout", user["email"], {"title": "Logout", "detail": "Session ended.", "severity": "info"})
    resp = JSONResponse({"ok": True})
    resp.delete_cookie(SESSION_COOKIE, path="/")
    return resp


# ---------------- security settings ----------------
@app.get("/security", response_class=HTMLResponse)
def security_page(request: Request):
    user = current_user(request)
    if not user:
        return RedirectResponse("/login", status_code=302)
    creds = store.credentials_for(user["email"])
    sessions = store.sessions_for(user["email"])
    return templates.TemplateResponse(
        request,
        "security.html",
        _ctx(request, creds=creds, sessions=sessions, active="security"),
    )


@app.post("/api/webauthn/register/options")
async def webauthn_reg_options(request: Request):
    user = current_user(request)
    if not user:
        return JSONResponse({"ok": False, "error": "Not signed in."}, 401)
    creds = store.credentials_for(user["email"])
    payload, challenge = wa.registration_options(
        user["user_handle"],
        user["email"],
        user["display_name"],
        [c["public_key"] for c in creds],
    )
    store.add_pending(challenge, "register", user["email"])
    return {"ok": True, **payload}


@app.post("/api/webauthn/register")
async def webauthn_register(request: Request):
    user = current_user(request)
    if not user:
        return JSONResponse({"ok": False, "error": "Not signed in."}, 401)
    body = await request.json()
    response = body.get("response")
    label = (body.get("label") or "YubiKey / passkey")[:40]
    challenge = _challenge_from_client_data(response)
    state = {"challenge": challenge, "user_verification": None}
    try:
        auth_data = wa.complete_registration(state, response)
    except ValueError as e:
        return JSONResponse({"ok": False, "error": str(e)}, 400)
    cred_id = bytes(auth_data.credential_data.credential_id)
    store.add_credential(
        user["email"], cred_id, bytes(auth_data.credential_data), label
    )
    store.consume_pending(challenge)
    event(
        "passkey_registered",
        user["email"],
        {
            "title": "New passkey registered",
            "detail": f"A YubiKey/passkey '{label}' was registered for this account. "
            "The account is now protected by phishing-resistant MFA.",
            "severity": "info",
        },
    )
    return {"ok": True}


# ---------------- demo helpers ----------------
@app.get("/api/demo/totp")
def demo_totp(request: Request, email: str):
    if not config.DEMO_MODE:
        return JSONResponse({"ok": False, "error": "demo mode disabled"}, 403)
    user = store.get_user(email)
    if not user or not user.get("totp_secret"):
        return JSONResponse({"ok": False, "error": "no totp"}, 404)
    return {"ok": True, "code": totp_mod.current_code(user["totp_secret"])}


@app.get("/api/demo/accounts")
def demo_accounts(request: Request):
    users = store.list_users()
    return {
        "ok": True,
        "accounts": [
            {
                "email": u["email"],
                "display_name": u["display_name"],
                "mfa": "passkey" if u["requires_passkey"] else ("totp" if u["totp_secret"] else "password"),
            }
            for u in users
        ],
    }


@app.get("/api/sessions/{email}")
def api_sessions(email: str):
    return {"ok": True, "sessions": store.sessions_for(email)}


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=config.PORTAL_PORT, log_level="warning")
