"""EDR sensor service — WithSecure Elements style detection (simulated).

Ingests endpoint telemetry, runs detection rules, raises alerts to the Merlon
SOC console, and enforces containment (host isolation) when instructed.
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

import rules
from common import config
from common.util import ROOT, JsonStore, now_iso, post_json, state_path, uid

app = FastAPI(title="EDR sensor (WithSecure-style simulation)")
templates = Jinja2Templates(directory=_HERE / "templates")
app.mount("/theme", StaticFiles(directory=ROOT / "common" / "static"), name="theme")

store = JsonStore(
    state_path("edr", "state.json"),
    default={
        "events": [],
        "alerts": [],
        "contained": {},  # host -> ts
        "terminated": {},
    },
)


@app.get("/healthz")
def healthz():
    return {"ok": True, "service": "edr"}


@app.post("/api/telemetry")
async def telemetry(request: Request):
    ev = await request.json()
    ev.setdefault("ts", now_iso())
    ev.setdefault("host", "WS-DEMO-01")

    triggered = rules.evaluate(ev)
    alert_payloads = []

    def _do(data):
        data["events"].append(ev)
        data["events"] = data["events"][-500:]
        for r in triggered:
            alert = {
                "id": uid(10),
                "ts": now_iso(),
                "source": "withsecure-elements",
                "detection": r.name,
                "severity": r.severity,
                "host": ev.get("host", "WS-DEMO-01"),
                "mitre": r.mitre,
                "narrative": r.narrative,
                "advice": r.advice,
                "trigger_event": {
                    "category": ev.get("category"),
                    "action": ev.get("action"),
                    "details": ev.get("details"),
                },
            }
            data["alerts"].append(alert)
            alert_payloads.append(alert)

    store.update(_do)

    for alert in alert_payloads:
        post_json(f"{config.SOC_URL}/api/alerts", alert, timeout=2.0)

    contained = store.data["contained"].get(ev.get("host", "WS-DEMO-01"))
    return {"ok": True, "detections": [r.name for r in triggered], "contained": bool(contained)}


@app.get("/api/events")
def api_events():
    return {"ok": True, "events": list(reversed(store.data["events"][-100:]))}


@app.get("/api/alerts")
def api_alerts():
    return {"ok": True, "alerts": list(reversed(store.data["alerts"][-100:]))}


@app.get("/api/contained/{host}")
def api_contained(host: str):
    return {"ok": True, "contained": bool(store.data["contained"].get(host))}


@app.post("/api/contain")
async def contain(request: Request):
    body = await request.json()
    host = body.get("host", "WS-DEMO-01")

    def _do(data):
        data["contained"][host] = now_iso()

    store.update(_do)
    return {"ok": True, "host": host, "contained": True}


@app.post("/api/release")
async def release(request: Request):
    body = await request.json()
    host = body.get("host", "WS-DEMO-01")

    def _do(data):
        data["contained"].pop(host, None)

    store.update(_do)
    return {"ok": True, "host": host, "contained": False}


@app.post("/api/terminate")
async def terminate(request: Request):
    """Simulate the SOC terminating the malicious process on the host."""
    body = await request.json()
    host = body.get("host", "WS-DEMO-01")

    def _do(data):
        data["terminated"][host] = now_iso()

    store.update(_do)
    return {"ok": True, "host": host}


@app.get("/api/terminated/{host}")
def api_terminated(host: str):
    return {"ok": True, "terminated": bool(store.data["terminated"].get(host))}


@app.get("/", response_class=HTMLResponse)
def status_page(request: Request):
    data = store.data
    return templates.TemplateResponse(
        request,
        "status.html",
        {
            "request": request,
            "alerts": list(reversed(data["alerts"][-50:])),
            "events": list(reversed(data["events"][-50:])),
            "contained": data["contained"],
        },
    )


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=config.EDR_PORT, log_level="warning")
