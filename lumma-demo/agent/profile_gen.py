"""Fake browser profile generator — the 'victim data' the stealer will take.

Generates a realistic-looking (but 100% fake) browser profile tree inside the
demo state directory: Chromium/Edge/Firefox profiles with Login Data, Cookies,
Web Data, wallet extensions, Discord/Telegram tokens, etc.

SAFETY: everything lands under state/endpoint/profile. The agent refuses to
operate outside that directory. No real browser data is ever touched.
"""
from __future__ import annotations

import json
import random
import string
from pathlib import Path

from common.util import state_path

PROFILE_ROOT = state_path("endpoint", "profile")

FAKE_PASSWORDS = [
    "S3cure!P@ss2024",
    "Tr0ub4dor&3",
    "correct-horse-battery",
    "Summer2023!",
    "Qwerty#12345",
    "MyDog'sName!2019",
]

FAKE_SITES = [
    ("https://mail.northwind.demo", "alex@northwind.demo"),
    ("https://portal.office.com", "alex.devries@northwind-corp.example"),
    ("https://github.com", "adevries-nl"),
    ("https://aws.amazon.com", "alex@northwind.demo"),
    ("https://linkedin.com", "alex.de.vries"),
    ("https://paypal.com", "a.devries@quicknet.example"),
    ("https://booking.com", "adevries"),
    ("https://dhl.com", "a.devries@northwind.demo"),
]


def _rand(n: int = 32) -> str:
    return "".join(random.choices(string.ascii_letters + string.digits, k=n))


def _cookie(host: str) -> dict:
    return {
        "host": host,
        "name": random.choice(["SESSIONID", "SID", "AUTH", "_ga", "token", "JSESSIONID"]),
        "value": _rand(40),
        "httponly": random.choice([True, False]),
        "secure": True,
    }


def generate(force: bool = False) -> Path:
    if PROFILE_ROOT.exists() and any(PROFILE_ROOT.iterdir()) and not force:
        return PROFILE_ROOT

    # --- Chromium (main profile) -----------------------------------------
    chromium = PROFILE_ROOT / "Chromium" / "Default"
    chromium.mkdir(parents=True, exist_ok=True)

    login_data = {
        "_note": "SIMULATED Chromium 'Login Data' (really SQLite in reality; JSON here)",
        "logins": [
            {"origin_url": url, "username": user, "password": random.choice(FAKE_PASSWORDS)}
            for url, user in FAKE_SITES
        ],
    }
    (chromium / "Login Data").write_text(json.dumps(login_data, indent=2))

    cookies = {
        "_note": "SIMULATED Cookies store",
        "cookies": [
            _cookie(random.choice(["portal.northwind.demo", "mail.northwind.demo",
                                    "intranet.northwind.demo", "hr.northwind.demo"]))
            for _ in range(24)
        ],
    }
    (chromium / "Cookies").write_text(json.dumps(cookies, indent=2))

    web_data = {
        "_note": "SIMULATED 'Web Data' — autofill + cards",
        "autofill": [
            {"name": "A. de Vries", "street": "Keizersgracht 121", "city": "Amsterdam", "zip": "1015 CW"},
            {"name": "Alex de Vries", "phone": "+31 6 2143 8890"},
        ],
        "cards": [
            {"label": "Visa", "number": "4539 8832 1104 7761", "exp": "09/27", "cvv": "314"},
            {"label": "Mastercard", "number": "5412 8810 3392 4470", "exp": "02/26", "cvv": "881"},
        ],
    }
    (chromium / "Web Data").write_text(json.dumps(web_data, indent=2))

    (chromium / "Local State").write_text(
        json.dumps(
            {
                "_note": "SIMULATED 'Local State' — holds the DPAPP-key (encrypted OSK) in reality",
                "os_crypt": {"encrypted_key": "DPAPI" + _rand(60)},
            },
            indent=2,
        )
    )

    # --- Edge profile ------------------------------------------------------
    edge = PROFILE_ROOT / "Microsoft Edge" / "Profile 1"
    edge.mkdir(parents=True, exist_ok=True)
    (edge / "Login Data").write_text(
        json.dumps(
            {
                "_note": "SIMULATED Edge credentials",
                "logins": [
                    {"origin_url": "https://teams.microsoft.com", "username": "alex@northwind.demo",
                     "password": random.choice(FAKE_PASSWORDS)},
                    {"origin_url": "https://outlook.office365.com", "username": "alex@northwind.demo",
                     "password": random.choice(FAKE_PASSWORDS)},
                ],
            },
            indent=2,
        )
    )
    (edge / "Cookies").write_text(json.dumps({"cookies": [_cookie("login.microsoftonline.com") for _ in range(6)]}, indent=2))

    # --- Firefox ------------------------------------------------------------
    ff = PROFILE_ROOT / "Firefox" / "Profiles" / "x9f2k1ds.default-release"
    ff.mkdir(parents=True, exist_ok=True)
    (ff / "logins.json").write_text(
        json.dumps(
            {
                "_note": "SIMULATED Firefox logins",
                "logins": [
                    {"hostname": "https://intern.northwind.demo", "username": "adevries", "password": random.choice(FAKE_PASSWORDS)},
                ],
            },
            indent=2,
        )
    )
    (ff / "cookies.sqlite").write_text(json.dumps({"cookies": [_cookie("intern.northwind.demo") for _ in range(8)]}, indent=2))

    # --- Crypto wallets -----------------------------------------------------
    for wallet in ("MetaMask", "Exodus", "Coinbase Wallet"):
        ext = chromium / "Extensions" / wallet
        ext.mkdir(parents=True, exist_ok=True)
        (ext / "vault.json").write_text(
            json.dumps({"seed": " ".join(_rand(6) for _ in range(12)), "balance": "2.41 ETH"}, indent=2)
        )

    # --- Chat tokens ---------------------------------------------------------
    discord = PROFILE_ROOT / "discord" / "Local Storage"
    discord.mkdir(parents=True, exist_ok=True)
    (discord / "token.txt").write_text("MTE" + _rand(66))
    telegram = PROFILE_ROOT / "Telegram Desktop" / "tdata"
    telegram.mkdir(parents=True, exist_ok=True)
    (telegram / "key_datas").write_bytes(_rand(64).encode())

    # --- Screenshot stub + system info ---------------------------------------
    ss = PROFILE_ROOT / "screenshots"
    ss.mkdir(parents=True, exist_ok=True)
    (ss / "screen_01.png").write_bytes(b"\x89PNG\r\n\x1a\nFAKE-SCREENSHOT-DEMO-IMAGE")
    (PROFILE_ROOT / "system.txt").write_text(
        f"Host: WS-DEMO-01\nUser: adenijs (demo)\nOS: Windows 11 Pro 23H2 (simulated)\nIP: 10.13.37.{random.randint(2,254)}\n"
    )

    return PROFILE_ROOT


def summary() -> dict:
    files = [p for p in PROFILE_ROOT.rglob("*") if p.is_file()]
    return {
        "root": str(PROFILE_ROOT),
        "files": len(files),
        "bytes": sum(f.stat().st_size for f in files),
    }


if __name__ == "__main__":
    root = generate(force="--force" in __import__("sys").argv)
    print("profile generated at", root)
    print(json.dumps(summary(), indent=2))
