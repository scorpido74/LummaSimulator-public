"""Merlon Security — SOC / detection & response console (demo).

The central dashboard of the demo: ingests
  * detection alerts from the (simulated) WithSecure Elements EDR sensor
  * security events from the Northwind portal (login failures, AiTM blocks,
    session-takeover signals)
and lets the operator respond: isolate the host, terminate the process.

Branding note: 'Merlon Security' is used here as the demo's security-partner
role. This is a mock console, not a real Merlon product.
"""
from __future__ import annotations

import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
for _p in (_HERE, _HERE.parent):
    if str(_p) not in sys.path:
        sys.path.insert(0, str(_p))

import uvicorn
from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from common import config
from common.util import ROOT, JsonStore, now_iso, post_json, state_path, uid

templates = Jinja2Templates(directory=_HERE / "templates")
app = FastAPI(title="Merlon SOC (demo)")
app.mount("/theme", StaticFiles(directory=ROOT / "common" / "static"), name="theme")

store = JsonStore(
    state_path("soc", "soc.json"),
    default={"alerts": [], "events": [], "actions": []},
)


@app.get("/healthz")
def healthz():
    return {"ok": True, "service": "soc"}


# ------------------------- intake -------------------------
@app.post("/api/alerts")
async def alert(request: Request):
    a = await request.json()
    a.setdefault("id", uid(10))
    a["kind"] = "edr_alert"

    def _do(data):
        data["alerts"].append(a)
        data["alerts"] = data["alerts"][-500:]

    store.update(_do)
    return {"ok": True, "id": a["id"]}


@app.post("/api/events")
async def portal_event(request: Request):
    e = await request.json()
    e.setdefault("id", uid(10))

    def _do(data):
        data["events"].append(e)
        data["events"] = data["events"][-500:]

    store.update(_do)
    return {"ok": True, "id": e["id"]}


# ------------------------- response actions -------------------------
@app.post("/api/respond/contain")
async def respond_contain(request: Request):
    body = await request.json()
    host = body.get("host", "WS-DEMO-01")
    r = post_json(f"{config.EDR_URL}/api/contain", {"host": host}, timeout=4.0)
    ok = bool(r and r.get("ok"))

    def _do(data):
        data["actions"].append(
            {"ts": now_iso(),
             "action": "contain_host", "host": host, "ok": ok, "by": "soc-operator"}
        )

    store.update(_do)
    return JSONResponse({"ok": ok} if ok else {"ok": False, "error": "EDR unreachable"}, 200 if ok else 502)


@app.post("/api/respond/terminate")
async def respond_terminate(request: Request):
    body = await request.json()
    host = body.get("host", "WS-DEMO-01")
    r = post_json(f"{config.EDR_URL}/api/terminate", {"host": host}, timeout=4.0)
    ok = bool(r and r.get("ok"))

    def _do(data):
        data["actions"].append(
            {"ts": now_iso(),
             "action": "terminate_process", "host": host, "ok": ok, "by": "soc-operator"}
        )

    store.update(_do)
    return JSONResponse({"ok": ok} if ok else {"ok": False, "error": "EDR unreachable"}, 200 if ok else 502)


@app.post("/api/respond/release")
async def respond_release(request: Request):
    body = await request.json()
    host = body.get("host", "WS-DEMO-01")
    r = post_json(f"{config.EDR_URL}/api/release", {"host": host}, timeout=4.0)
    ok = bool(r and r.get("ok"))

    def _do(data):
        data["actions"].append(
            {"ts": now_iso(),
             "action": "release_host", "host": host, "ok": ok, "by": "soc-operator"}
        )

    store.update(_do)
    return JSONResponse({"ok": ok} if ok else {"ok": False, "error": "EDR unreachable"}, 200 if ok else 502)


# ------------------------- dashboard -------------------------
SEVERITY_ORDER = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}


@app.get("/", response_class=HTMLResponse)
def dashboard(request: Request):
    data = store.data
    return templates.TemplateResponse(request, "soc.html", {"request": request, "soc": data})


@app.get("/api/state")
def api_state():
    data = store.data
    alerts = data["alerts"]
    events = data["events"]

    critical = [a for a in alerts if a.get("severity") == "critical"]
    mfa_wins = [
        e
        for e in events
        if e.get("kind") in ("webauthn_origin_rejected", "webauthn_relay_suspected")
    ]
    return {
        "ok": True,
        "counts": {
            "alerts": len(alerts),
            "critical": len(critical),
            "events": len(events),
            "mfa_wins": len(mfa_wins),
        },
        "alerts": list(reversed(alerts[-50:])),
        "events": list(reversed(events[-50:])),
        "actions": list(reversed(data["actions"][-30:])),
    }


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=config.SOC_PORT, log_level="warning")
