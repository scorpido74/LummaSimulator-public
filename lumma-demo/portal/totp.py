"""Minimal RFC 6238 TOTP (used for the 'authenticator app' demo user).

Deliberately stdlib-only. This is the *phishable* second factor that the
AiTM demo defeats — contrast with WebAuthn/passkeys, which it cannot defeat.
"""
import base64
import hashlib
import hmac
import secrets
import time


def new_secret() -> str:
    return base64.b32encode(secrets.token_bytes(20)).decode()


def hotp(secret_b32: str, counter: int, digits: int = 6) -> str:
    key = base64.b32decode(secret_b32)
    msg = counter.to_bytes(8, "big")
    digest = hmac.new(key, msg, hashlib.sha1).digest()
    offset = digest[-1] & 0xF
    code = int.from_bytes(digest[offset : offset + 4], "big") & 0x7FFFFFFF
    return str(code % 10**digits).zfill(digits)


def current_code(secret_b32: str, at: float | None = None) -> str:
    t = at if at is not None else time.time()
    return hotp(secret_b32, int(t // 30))


def verify(secret_b32: str, code: str, window: int = 1) -> bool:
    t = time.time()
    base_counter = int(t // 30)
    for drift in range(-window, window + 1):
        if hmac.compare_digest(hotp(secret_b32, base_counter + drift), code.strip()):
            return True
    return False
