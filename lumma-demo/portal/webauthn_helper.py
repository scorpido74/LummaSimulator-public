"""WebAuthn (passkey / YubiKey) support for the portal.

Uses Yubico's own `python-fido2` library as the FIDO2 relying party.
The origin check is STRICT (scheme + host + port equality) — stricter than the
library default, which ignores ports. This is the property that defeats
adversary-in-the-middle phishing proxies: an assertion signed by the YubiKey
on the phishing origin does not verify here.
"""
from __future__ import annotations

import fido2.features

fido2.features.webauthn_json_mapping.enabled = True

from fido2.server import Fido2Server  # noqa: E402
from fido2.webauthn import (  # noqa: E402
    AttestedCredentialData,
    PublicKeyCredentialDescriptor,
    PublicKeyCredentialUserEntity,
)

from common.util import b64u

# Only ES256 + RS256 — what browsers/YubiKeys produce and what we can verify.
ALLOWED_ALGS = [{"type": "public-key", "alg": -7}, {"type": "public-key", "alg": -257}]


class WebAuthnManager:
    def __init__(self, rp_id: str, rp_name: str, expected_origin: str):
        self.rp_id = rp_id
        self.expected_origin = expected_origin
        self.server = Fido2Server(
            {"id": rp_id, "name": rp_name},
            attestation="none",
            verify_origin=lambda origin: origin == expected_origin,
        )

    # ---- registration ----
    def registration_options(
        self,
        user_handle: bytes,
        email: str,
        display_name: str,
        existing_public_keys: list[bytes],
    ) -> tuple[dict, str]:
        creds = [AttestedCredentialData(blob) for blob in existing_public_keys]
        user = PublicKeyCredentialUserEntity(
            id=user_handle, name=email, display_name=display_name
        )
        options, state = self.server.register_begin(user, credentials=creds or None)
        pk = options.public_key
        challenge = b64u(pk.challenge)
        payload = {
            "rp": {"id": self.rp_id, "name": pk.rp.name},
            "user": {
                "id": b64u(pk.user.id),
                "name": pk.user.name,
                "displayName": pk.user.display_name,
            },
            "challenge": challenge,
            "pubKeyCredParams": ALLOWED_ALGS,
            "timeout": 60000,
            "attestation": "none",
            "excludeCredentials": [
                {"type": "public-key", "id": b64u(c.credential_id)} for c in creds
            ],
            "authenticatorSelection": {
                "residentKey": "preferred",
                "userVerification": "preferred",
            },
        }
        return payload, challenge

    def complete_registration(self, state, response: dict):
        """Returns AuthenticatorData; raises ValueError on any check failure."""
        return self.server.register_complete(state, response)

    # ---- authentication ----
    def authentication_options(
        self, existing_public_keys: list[bytes]
    ) -> tuple[dict, str]:
        creds = [AttestedCredentialData(blob) for blob in existing_public_keys]
        options, state = self.server.authenticate_begin(
            credentials=creds or None, user_verification="preferred"
        )
        pk = options.public_key
        challenge = b64u(pk.challenge)
        payload = {
            "challenge": challenge,
            "timeout": 60000,
            "rpId": self.rp_id,
            "allowCredentials": [
                {"type": "public-key", "id": b64u(c.credential_id)} for c in creds
            ],
            "userVerification": "preferred",
        }
        return payload, challenge

    def complete_authentication(self, state, credentials: list, response: dict):
        """Returns the matched AttestedCredentialData; raises ValueError."""
        return self.server.authenticate_complete(state, credentials, response)

    @staticmethod
    def descriptors_for(public_keys: list[bytes]) -> list:
        return [
            PublicKeyCredentialDescriptor(id=AttestedCredentialData(blob).credential_id)
            for blob in public_keys
        ]
