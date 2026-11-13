"""Standalone proof: full WebAuthn registration + authentication round-trip
against Yubico's python-fido2 Fido2Server, using a software ES256 key.

This validates:
  1. register_begin/register_complete with 'none' attestation (JSON dict input)
  2. authenticate_begin/authenticate_complete (JSON dict input)
  3. Strict origin verification (phish origin must be rejected)
"""
import hashlib
import json
import os

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec
from fido2 import cbor
import fido2.features
from fido2.server import Fido2Server

fido2.features.webauthn_json_mapping.enabled = True

RP_ID = "portal.localhost"
ORIGIN = "http://portal.localhost:8000"
PHISH_ORIGIN = "http://phish.localhost:8001"


def b64u(b: bytes) -> str:
    import base64

    return base64.urlsafe_b64encode(b).decode().rstrip("=")


# --- Build the virtual authenticator -----------------------------------
priv = ec.generate_private_key(ec.SECP256R1())
nums = priv.public_key().public_numbers()
cose_key = {
    1: 2,  # kty: EC2
    3: -7,  # alg: ES256
    -1: 1,  # crv: P-256
    -2: nums.x.to_bytes(32, "big"),
    -3: nums.y.to_bytes(32, "big"),
}
cred_id = os.urandom(32)
rp_id_hash = hashlib.sha256(RP_ID.encode()).digest()


def make_auth_data(include_cred: bool, sign_count: int, uv: bool = True) -> bytes:
    flags = 0x01 | (0x04 if uv else 0)  # UP | UV
    if include_cred:
        flags |= 0x40  # AT
    ad = rp_id_hash + bytes([flags]) + sign_count.to_bytes(4, "big")
    if include_cred:
        aaguid = b"\x00" * 16
        attested = aaguid + len(cred_id).to_bytes(2, "big") + cred_id
        attested += cbor.encode(cose_key)
        ad += attested
    return ad


# --- Server side ---------------------------------------------------------
srv = Fido2Server(
    {"id": RP_ID, "name": "Northwind"},
    attestation="none",
    verify_origin=lambda o: o == ORIGIN,  # strict demo-origin check
)

user = {"id": b64u(os.urandom(16)), "name": "sam@northwind.demo", "displayName": "Sam"}

opts, state = srv.register_begin(user)
pk = opts.public_key
challenge = pk.challenge
print("challenge:", b64u(challenge))

client_data = json.dumps(
    {"type": "webauthn.create", "challenge": b64u(challenge), "origin": ORIGIN}
).encode()
att_obj = cbor.encode(
    {
        "fmt": "none",
        "attStmt": {},
        "authData": make_auth_data(include_cred=True, sign_count=0),
    }
)
reg_response = {
    "id": b64u(cred_id),
    "rawId": b64u(cred_id),
    "type": "public-key",
    "response": {
        "clientDataJSON": b64u(client_data),
        "attestationObject": b64u(att_obj),
    },
}
auth_data = srv.register_complete(state, reg_response)
print(
    "REGISTERED:",
    type(auth_data).__name__,
    "cred_id ok:",
    auth_data.credential_data.credential_id == cred_id,
)
stored = auth_data.credential_data

# --- Authenticate, good origin -----------------------------------------
aopts, astate = srv.authenticate_begin([stored])
achallenge = aopts.public_key.challenge
cd = json.dumps(
    {"type": "webauthn.get", "challenge": b64u(achallenge), "origin": ORIGIN}
).encode()
ad = make_auth_data(include_cred=False, sign_count=1)
sig = priv.sign(ad + hashlib.sha256(cd).digest(), ec.ECDSA(hashes.SHA256()))
auth_response = {
    "id": b64u(cred_id),
    "rawId": b64u(cred_id),
    "type": "public-key",
    "response": {
        "clientDataJSON": b64u(cd),
        "authenticatorData": b64u(ad),
        "signature": b64u(sig),
    },
}
result = srv.authenticate_complete(astate, [stored], auth_response)
print("AUTH OK:", result.credential_id == cred_id)

# --- Authenticate with PHISH origin (AiTM relay) must fail ---------------
cd_phish = json.dumps(
    {"type": "webauthn.get", "challenge": b64u(achallenge), "origin": PHISH_ORIGIN}
).encode()
ad2 = make_auth_data(include_cred=False, sign_count=2)
sig2 = priv.sign(ad2 + hashlib.sha256(cd_phish).digest(), ec.ECDSA(hashes.SHA256()))
bad_response = {
    "id": b64u(cred_id),
    "rawId": b64u(cred_id),
    "type": "public-key",
    "response": {
        "clientDataJSON": b64u(cd_phish),
        "authenticatorData": b64u(ad2),
        "signature": b64u(sig2),
    },
}
try:
    srv.authenticate_complete(astate, [stored], bad_response)
    print("FAIL: phish origin was ACCEPTED (must be rejected!)")
except ValueError as e:
    print("PHISH ORIGIN REJECTED as expected:", e)
