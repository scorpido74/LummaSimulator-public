"""SQLite-backed store + demo seed data for the Northwind Secure Mail portal."""
from __future__ import annotations

import base64
import hashlib
import hmac
import os
import secrets
import sqlite3
import threading
import time
from pathlib import Path

from common.util import ROOT, now_iso

DB_PATH = Path(os.environ.get("LUMMA_STATE", ROOT / "state")) / "portal.db"
_lock = threading.Lock()

SCHEMA = """
CREATE TABLE IF NOT EXISTS users(
  email TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  password_hash TEXT NOT NULL,
  totp_secret TEXT,
  requires_passkey INTEGER NOT NULL DEFAULT 0,
  user_handle BLOB NOT NULL
);
CREATE TABLE IF NOT EXISTS webauthn_credentials(
  cred_id BLOB PRIMARY KEY,
  email TEXT NOT NULL,
  public_key BLOB NOT NULL,
  label TEXT NOT NULL,
  sign_count INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS sessions(
  token TEXT PRIMARY KEY,
  email TEXT NOT NULL,
  created_at TEXT NOT NULL,
  last_seen TEXT NOT NULL,
  ua TEXT
);
CREATE TABLE IF NOT EXISTS security_events(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts TEXT NOT NULL,
  kind TEXT NOT NULL,
  email TEXT,
  details TEXT
);
CREATE TABLE IF NOT EXISTS pending_challenges(
  challenge TEXT PRIMARY KEY,
  purpose TEXT NOT NULL,
  email TEXT NOT NULL,
  created_at TEXT NOT NULL,
  consumed INTEGER NOT NULL DEFAULT 0,
  fired INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS mail(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  email TEXT NOT NULL,
  sender TEXT NOT NULL,
  subject TEXT NOT NULL,
  body_html TEXT NOT NULL,
  ts TEXT NOT NULL,
  unread INTEGER NOT NULL DEFAULT 1
);
"""

SEED_USERS = [
    {
        "email": "alex@northwind.demo",
        "display_name": "Alex de Vries",
        "password": "Tr0ub4dor&3",
        "totp": False,
        "requires_passkey": 0,
    },
    {
        "email": "riley@northwind.demo",
        "display_name": "Riley Jansen",
        "password": "Northwind2024!",
        "totp": True,
        "requires_passkey": 0,
    },
    {
        "email": "sam@northwind.demo",
        "display_name": "Sam Pietersen",
        "password": "Yubi!Demo2024",
        "totp": False,
        "requires_passkey": 1,
    },
]


def db() -> sqlite3.Connection:
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH, timeout=10)
    conn.row_factory = sqlite3.Row
    return conn


def init() -> None:
    with _lock, db() as conn:
        conn.executescript(SCHEMA)
        if conn.execute("SELECT COUNT(*) c FROM users").fetchone()["c"] == 0:
            seed(conn)


# --- password hashing -----------------------------------------------------
def hash_password(password: str) -> str:
    salt = os.urandom(16)
    iters = 120_000
    dk = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, iters)
    return f"pbkdf2${iters}${base64.b64encode(salt).decode()}${base64.b64encode(dk).decode()}"


def verify_password(password: str, stored: str) -> bool:
    try:
        _, iters, salt_b64, hash_b64 = stored.split("$")
        dk = hashlib.pbkdf2_hmac(
            "sha256", password.encode(), base64.b64decode(salt_b64), int(iters)
        )
        return hmac.compare_digest(dk, base64.b64decode(hash_b64))
    except Exception:  # noqa: BLE001
        return False


# --- users ----------------------------------------------------------------
def get_user(email: str) -> dict | None:
    with db() as conn:
        row = conn.execute("SELECT * FROM users WHERE email=?", (email,)).fetchone()
        return dict(row) if row else None


def list_users() -> list[dict]:
    with db() as conn:
        return [dict(r) for r in conn.execute("SELECT * FROM users").fetchall()]


# --- sessions --------------------------------------------------------------
def create_session(email: str, ua: str | None) -> str:
    token = secrets.token_urlsafe(32)
    with _lock, db() as conn:
        conn.execute(
            "INSERT INTO sessions(token,email,created_at,last_seen,ua) VALUES(?,?,?,?,?)",
            (token, email, now_iso(), now_iso(), ua),
        )
    return token


def get_session(token: str) -> dict | None:
    with db() as conn:
        row = conn.execute("SELECT * FROM sessions WHERE token=?", (token,)).fetchone()
        return dict(row) if row else None


def delete_session(token: str) -> None:
    with _lock, db() as conn:
        conn.execute("DELETE FROM sessions WHERE token=?", (token,))


def touch_session(token: str) -> None:
    with _lock, db() as conn:
        conn.execute("UPDATE sessions SET last_seen=? WHERE token=?", (now_iso(), token))


def sessions_for(email: str) -> list[dict]:
    with db() as conn:
        rows = conn.execute(
            "SELECT * FROM sessions WHERE email=? ORDER BY created_at DESC", (email,)
        ).fetchall()
        return [dict(r) for r in rows]


# --- webauthn ----------------------------------------------------------------
def add_credential(email: str, cred_id: bytes, public_key: bytes, label: str) -> None:
    with _lock, db() as conn:
        conn.execute(
            "INSERT OR REPLACE INTO webauthn_credentials(cred_id,email,public_key,label,sign_count,created_at)"
            " VALUES(?,?,?,?,0,?)",
            (cred_id, email, public_key, label, now_iso()),
        )


def credentials_for(email: str) -> list[dict]:
    with db() as conn:
        rows = conn.execute(
            "SELECT * FROM webauthn_credentials WHERE email=? ORDER BY created_at", (email,)
        ).fetchall()
        return [dict(r) for r in rows]


# --- security events -----------------------------------------------------------
def add_event(kind: str, email: str | None, details: dict | None = None) -> None:
    import json as _json

    with _lock, db() as conn:
        conn.execute(
            "INSERT INTO security_events(ts,kind,email,details) VALUES(?,?,?,?)",
            (now_iso(), kind, email, _json.dumps(details or {})),
        )


def recent_events(limit: int = 100) -> list[dict]:
    import json as _json

    with db() as conn:
        rows = conn.execute(
            "SELECT * FROM security_events ORDER BY id DESC LIMIT ?", (limit,)
        ).fetchall()
        out = []
        for r in rows:
            d = dict(r)
            d["details"] = _json.loads(d["details"] or "{}")
            out.append(d)
        return out


# --- pending challenges (relay detection) ----------------------------------------
def add_pending(challenge: str, purpose: str, email: str) -> None:
    with _lock, db() as conn:
        conn.execute(
            "INSERT OR REPLACE INTO pending_challenges(challenge,purpose,email,created_at,consumed,fired)"
            " VALUES(?,?,?,?,0,0)",
            (challenge, purpose, email, now_iso()),
        )


def consume_pending(challenge: str) -> None:
    with _lock, db() as conn:
        conn.execute(
            "UPDATE pending_challenges SET consumed=1 WHERE challenge=?", (challenge,)
        )


def stale_pending(older_than_seconds: float) -> list[dict]:
    cutoff = time.time() - older_than_seconds
    with _lock, db() as conn:
        rows = conn.execute(
            "SELECT * FROM pending_challenges WHERE consumed=0 AND fired=0"
        ).fetchall()
        out = []
        for r in rows:
            try:
                created = time.mktime(
                    time.strptime(r["created_at"], "%Y-%m-%dT%H:%M:%S.%fZ")
                )
            except ValueError:
                continue
            if created < cutoff:
                out.append(dict(r))
        return out


def mark_fired(challenge: str) -> None:
    with _lock, db() as conn:
        conn.execute("UPDATE pending_challenges SET fired=1 WHERE challenge=?", (challenge,))


# --- mail --------------------------------------------------------------------------
def mail_for(email: str) -> list[dict]:
    with db() as conn:
        rows = conn.execute(
            "SELECT * FROM mail WHERE email=? ORDER BY id DESC", (email,)
        ).fetchall()
        return [dict(r) for r in rows]


def mail_item(email: str, mail_id: int) -> dict | None:
    with db() as conn:
        row = conn.execute(
            "SELECT * FROM mail WHERE email=? AND id=?", (email, mail_id)
        ).fetchone()
        return dict(row) if row else None


# --- seed ----------------------------------------------------------------------------
def seed(conn: sqlite3.Connection) -> None:
    for u in SEED_USERS:
        totp_secret = (
            base64.b32encode(os.urandom(20)).decode() if u["totp"] else None
        )
        conn.execute(
            "INSERT INTO users(email,display_name,password_hash,totp_secret,requires_passkey,user_handle)"
            " VALUES(?,?,?,?,?,?)",
            (
                u["email"],
                u["display_name"],
                hash_password(u["password"]),
                totp_secret,
                u["requires_passkey"],
                os.urandom(16),
            ),
        )
    _seed_mail(conn)


def _phish_link(email: str) -> str:
    return f"http://phish.localhost:8001/login?target={email}"


def _seed_mail(conn: sqlite3.Connection) -> None:
    for u in SEED_USERS:
        email = u["email"]
        first = u["display_name"].split()[0]

        conn.execute(
            "INSERT INTO mail(email,sender,subject,body_html,ts) VALUES(?,?,?,?,?)",
            (
                email,
                "Northwind IT Support &lt;it-support@northwind-demo.com&gt;",
                "Action required: verify your mailbox within 24 hours",
                f"""
<div style="font-family:Segoe UI,Arial,sans-serif">
  <p>Dear colleague,</p>
  <p>Our records indicate your mailbox has not yet been verified under the new
  <b>Northwind Secure SSO policy</b>. Mailboxes that are not verified within
  <b>24 hours</b> will be <span style="color:#c0392b">temporarily suspended</span>.</p>
  <p style="text-align:center;margin:26px 0">
    <a href="{_phish_link(email)}"
       style="background:#1f6feb;color:#fff;padding:12px 26px;border-radius:6px;text-decoration:none;font-weight:700">
      Verify my mailbox now
    </a>
  </p>
  <p style="color:#666;font-size:12px">If the button does not work, copy this link into your browser:<br>
  <span style="font-family:monospace">{_phish_link(email)}</span></p>
  <hr>
  <p style="color:#999;font-size:11px">Northwind Logistics — IT Shared Services. This message was sent automatically.</p>
</div>
""",
                now_iso(),
            ),
        )

        conn.execute(
            "INSERT INTO mail(email,sender,subject,body_html,ts) VALUES(?,?,?,?,?)",
            (
                email,
                "Northwind Service Desk &lt;servicedesk@northwind.demo&gt;",
                "Welcome to Northwind Secure Mail",
                f"""
<div style="font-family:Segoe UI,Arial,sans-serif">
  <p>Hi {first},</p>
  <p>Your Secure Mail account is ready. Sign in at
  <b>portal.localhost:8000</b> with your corporate account.</p>
  <p>Depending on your team you either use:</p>
  <ul>
    <li>your password, or</li>
    <li>your password plus a six-digit code from your authenticator app, or</li>
    <li>your password plus your <b>YubiKey security key</b> (recommended).</li>
  </ul>
  <p>— The Northwind Service Desk</p>
</div>
""",
                now_iso(),
            ),
        )

        if u["requires_passkey"]:
            conn.execute(
                "INSERT INTO mail(email,sender,subject,body_html,ts) VALUES(?,?,?,?,?)",
                (
                    email,
                    "Northwind Security Office &lt;security@northwind.demo&gt;",
                    "Your YubiKey has arrived — activate it today",
                    """
<div style="font-family:Segoe UI,Arial,sans-serif">
  <p>Your new <b>YubiKey 5 Series</b> security key has been delivered to reception.</p>
  <p>Activate it from <b>Security settings → Register a passkey</b> inside Secure Mail.
  After activation, phishing sites can no longer use your password to access your
  account — even if your password is stolen.</p>
  <p>Questions? Contact the security office. Partner: <b>Yubico</b>.</p>
</div>
""",
                    now_iso(),
                ),
            )

        conn.execute(
            "INSERT INTO mail(email,sender,subject,body_html,ts) VALUES(?,?,?,?,?)",
            (
                email,
                "Facilities &lt;facilities@northwind.demo&gt;",
                "New coffee machine on floor 3",
                """
<div style="font-family:Segoe UI,Arial,sans-serif">
  <p>The old machine has been replaced. The new one takes the same pods as before.</p>
  <p>Small reminder: floor 3 kitchen will be closed Friday afternoon for maintenance.</p>
</div>
""",
                now_iso(),
            ),
        )
