"""Central configuration for the LummaSimulator demo platform.

All services run locally with well-known ports. Browser-facing URLs use
*.localhost hostnames (browsers treat these as secure contexts, which is
required for WebAuthn). Service-to-service traffic uses 127.0.0.1 by
default; override the *_URL variables (e.g. Docker service names) via env.
"""
import os

# --- Ports ---------------------------------------------------------------
PORTAL_PORT = int(os.environ.get("PORTAL_PORT", 8000))
PHISH_PORT = int(os.environ.get("PHISH_PORT", 8001))
C2_PORT = int(os.environ.get("C2_PORT", 8002))
SOC_PORT = int(os.environ.get("SOC_PORT", 8003))
EDR_PORT = int(os.environ.get("EDR_PORT", 8004))
ENDPOINT_PORT = int(os.environ.get("ENDPOINT_PORT", 8005))

# --- Browser-facing origins ----------------------------------------------
PORTAL_ORIGIN = f"http://portal.localhost:{PORTAL_PORT}"
PHISH_ORIGIN = f"http://phish.localhost:{PHISH_PORT}"
C2_ORIGIN = f"http://c2.localhost:{C2_PORT}"
SOC_ORIGIN = f"http://soc.localhost:{SOC_PORT}"
ENDPOINT_ORIGIN = f"http://endpoint.localhost:{ENDPOINT_PORT}"

# --- Internal service URLs (override for Docker: http://portal:8000 etc.) ---
PORTAL_URL = os.environ.get("PORTAL_URL", f"http://127.0.0.1:{PORTAL_PORT}")
PHISH_URL = os.environ.get("PHISH_URL", f"http://127.0.0.1:{PHISH_PORT}")
C2_URL = os.environ.get("C2_URL", f"http://127.0.0.1:{C2_PORT}")
SOC_URL = os.environ.get("SOC_URL", f"http://127.0.0.1:{SOC_PORT}")
EDR_URL = os.environ.get("EDR_URL", f"http://127.0.0.1:{EDR_PORT}")
ENDPOINT_URL = os.environ.get("ENDPOINT_URL", f"http://127.0.0.1:{ENDPOINT_PORT}")

# --- WebAuthn / Yubico ----------------------------------------------------
RP_ID = "portal.localhost"
RP_NAME = "Northwind Secure Mail"
DEMO_MODE = os.environ.get("LUMMA_DEMO", "1") == "1"
SOC_WEBHOOK = f"{SOC_URL}/api/events"
EDR_TELEMETRY = f"{EDR_URL}/api/telemetry"

# Fake company used across the demo
COMPANY = "Northwind Logistics"
