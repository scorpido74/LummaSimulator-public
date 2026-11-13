"""Virtual YubiKey — a software ES256 WebAuthn authenticator for testing.

Implements the client side of the WebAuthn ceremony exactly like a browser
with a YubiKey would: builds clientDataJSON (with a chosen ORIGIN — this is
what the AiTM relay cannot forge), signs with a P-256 key, and submits the
same JSON a browser submits.

With `origin=` you choose which site the "browser" is on. This is how the
tests prove that assertions from the phishing origin are rejected while
assertions from the real portal origin succeed.
"""
from __future__ import annotations

import base64
import hashlib
import json
import os

import httpx
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec

RP_ID = "portal.localhost"
PORTAL = "http://127.0.0.1:8000"
PORTAL_ORIGIN = "http://portal.localhost:8000"


def b64u(b: bytes) -> str:
    return base64.urlsafe_b64encode(b).decode().rstrip("=")


def b64u_dec(s: str) -> bytes:
    return base64.urlsafe_b64decode(s + "=" * (-len(s) % 4))


class VirtualYubiKey:
    def __init__(self):
        self.priv = ec.generate_private_key(ec.SECP256R1())
        nums = self.priv.public_key().public_numbers()
        self.cose_key = {
            1: 2, 3: -7, -1: 1,
            -2: nums.x.to_bytes(32, "big"),
            -3: nums.y.to_bytes(32, "big"),
        }
        self.cred_id = os.urandom(32)
        self.sign_count = 0
        self.rp_id_hash = hashlib.sha256(RP_ID.encode()).digest()

    # ------------------------------------------------------------------
    def _auth_data(self, include_cred: bool) -> bytes:
        self.sign_count += 1
        flags = 0x45 if include_cred else 0x05  # UP|UV|AT : UP|UV
        ad = self.rp_id_hash + bytes([flags]) + self.sign_count.to_bytes(4, "big")
        if include_cred:
            from fido2 import cbor

            ad += b"\x00" * 16  # aaguid
            ad += len(self.cred_id).to_bytes(2, "big") + self.cred_id
            ad += cbor.encode(self.cose_key)
        return ad

    def _client_data(self, typ: str, challenge: str, origin: str) -> bytes:
        return json.dumps(
            {"type": typ, "challenge": challenge, "origin": origin}
        ).encode()

    def _sign(self, data: bytes) -> bytes:
        return self.priv.sign(data, ec.ECDSA(hashes.SHA256()))

    # ------------------------------------------------------------------
    def register_response(self, options: dict, origin: str = PORTAL_ORIGIN) -> dict:
        challenge = options["challenge"]
        client_data = self._client_data("webauthn.create", challenge, origin)
        auth_data = self._auth_data(include_cred=True)
        from fido2 import cbor

        att_obj = cbor.encode({"fmt": "none", "attStmt": {}, "authData": auth_data})
        return {
            "id": b64u(self.cred_id),
            "rawId": b64u(self.cred_id),
            "type": "public-key",
            "response": {
                "clientDataJSON": b64u(client_data),
                "attestationObject": b64u(att_obj),
            },
        }

    def assertion_response(self, options: dict, origin: str = PORTAL_ORIGIN) -> dict:
        challenge = options["challenge"]
        client_data = self._client_data("webauthn.get", challenge, origin)
        auth_data = self._auth_data(include_cred=False)
        signature = self._sign(auth_data + hashlib.sha256(client_data).digest())
        return {
            "id": b64u(self.cred_id),
            "rawId": b64u(self.cred_id),
            "type": "public-key",
            "response": {
                "clientDataJSON": b64u(client_data),
                "authenticatorData": b64u(auth_data),
                "signature": b64u(signature),
            },
        }
