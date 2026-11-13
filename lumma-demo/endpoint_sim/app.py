"""Endpoint simulator — fake corporate workstation WS-DEMO-01.

Hosts the fake browser profile, accepts (simulated) payload deploys from the
ClickFix page / C2 console, and shows the workstation view for the demo.
Respects EDR containment: when the SOC isolates the host, deploys are refused.
"""
from __future__ import annotations

import subprocess
import sys
import time
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

from agent import profile_gen
from common import config
from common.util import ROOT, JsonStore, get_json, now_iso, state_path, uid

templates = Jinja2Templates(directory=_HERE / "templates")
app = FastAPI(title="Endpoint simulator WS-DEMO-01")
app.mount("/theme", StaticFiles(directory=ROOT / "common" / "static"), name="theme")

store = JsonStore(
    state_path("endpoint", "endpoint.json"),
    default={"runs": [], "agent_running": False, "agent_pid": None},
)


def _edr(path: str) -> dict:
    return get_json(f"{config.EDR_URL}{path}") or {}


def _contained() -> bool:
    return bool(_edr("/api/contained/WS-DEMO-01").get("contained"))


@app.on_event("startup")
def _startup():
    profile_gen.generate()


@app.get("/healthz")
def healthz():
    return {"ok": True, "service": "endpoint-sim"}


@app.post("/deploy")
async def deploy(request: Request):
    body = await request.json()
    if _contained():
        return JSONResponse(
            {
                "ok": False,
                "error": "host is contained by the SOC — payload deployment refused",
            },
            status_code=423,
        )

    agent = _HERE.parent / "agent" / "lumma_agent.py"
    venv_python = _HERE.parent / ".venv" / "bin" / "python"
    cmd = [
        str(venv_python if venv_python.exists() else sys.executable),
        str(agent),
        "--campaign",
        body.get("origin", "manual"),
        "--user",
        body.get("user", "alex@northwind.demo"),
    ]
    proc = subprocess.Popen(cmd, cwd=str(_HERE.parent))

    def _do(data):
        data["agent_running"] = True
        data["agent_pid"] = proc.pid
        data["runs"].append(
            {"id": uid(8), "ts": now_iso(), "origin": body.get("origin", "manual"),
             "pid": proc.pid, "user": body.get("user", "")}
        )

    store.update(_do)
    return {"ok": True, "pid": proc.pid}


@app.get("/", response_class=HTMLResponse)
def workstation(request: Request):
    profile_gen.generate()
    data = store.data
    contained = _contained()
    return templates.TemplateResponse(
        request,
        "workstation.html",
        {
            "request": request,
            "profile": profile_gen.summary(),
            "runs": list(reversed(data["runs"][-10:])),
            "contained": contained,
        },
    )


@app.get("/api/status")
def status():
    return {
        "ok": True,
        "contained": _contained(),
        "runs": store.data["runs"][-10:],
        "profile": profile_gen.summary(),
    }


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=config.ENDPOINT_PORT, log_level="warning")
