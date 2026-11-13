"""Shared utilities for the LummaSimulator local demo platform."""
from __future__ import annotations

import base64
import json
import os
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import httpx

ROOT = Path(__file__).resolve().parent.parent
STATE_DIR = Path(os.environ.get("LUMMA_STATE", ROOT / "state"))


def b64u(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode().rstrip("=")


def b64u_dec(data: str) -> bytes:
    if isinstance(data, bytes):
        return data
    return base64.urlsafe_b64decode(data + "=" * (-len(data) % 4))


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def ts_short(iso: str) -> str:
    try:
        dt = datetime.strptime(iso.replace("Z", ""), "%Y-%m-%dT%H:%M:%S.%f")
    except ValueError:
        try:
            dt = datetime.strptime(iso.replace("Z", ""), "%Y-%m-%dT%H:%M:%S")
        except ValueError:
            return iso
    return dt.strftime("%H:%M:%S")


def uid(n: int = 12) -> str:
    return uuid.uuid4().hex[:n]


def state_path(*parts: str) -> Path:
    p = STATE_DIR.joinpath(*parts)
    p.parent.mkdir(parents=True, exist_ok=True)
    return p


def post_json(url: str, payload: Any, timeout: float = 3.0) -> dict | None:
    """Fire-and-forget JSON POST. Never raises (demo glue code)."""
    try:
        r = httpx.post(url, json=payload, timeout=timeout)
        if r.status_code < 300:
            return r.json() if r.content else {}
    except Exception as e:  # noqa: BLE001
        print(f"[util] post_json {url} failed: {e}")
    return None


def get_json(url: str, timeout: float = 3.0) -> dict | None:
    try:
        r = httpx.get(url, timeout=timeout)
        if r.status_code == 200:
            return r.json()
    except Exception as e:  # noqa: BLE001
        print(f"[util] get_json {url} failed: {e}")
    return None


class JsonStore:
    """Tiny thread-safe JSON-file backed store for demo services."""

    def __init__(self, path: Path, default: Any = None):
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()
        self._data = default if default is not None else {}
        self._load()

    def _load(self) -> None:
        if self.path.exists():
            try:
                self._data = json.loads(self.path.read_text())
            except Exception:  # noqa: BLE001
                self._data = {}

    def update(self, fn) -> Any:
        """Run fn(data) under lock, persist, return its result."""
        with self._lock:
            result = fn(self._data)
            self.path.write_text(json.dumps(self._data, indent=2, default=str))
            return result

    @property
    def data(self):
        with self._lock:
            return self._data
