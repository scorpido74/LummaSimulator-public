# LummaSimulator-public
LummaSimulator is een interactief demoplatform van Infinigate voor securitybewustwording. Demonstreert phishing, ClickFix en sessiediefstal in een gecontroleerde omgeving en laat zien hoe YubiKeys en passkeys beschermen. Met een centraal dashboard, anonieme demoaccounts en automatische opschoning.

## Local demo environment (`lumma-demo/`)

A self-contained local demo environment for the Lumma awareness story lives in
[`lumma-demo/`](lumma-demo/) — with Merlon Security (SOC), WithSecure (EDR) and
Yubico (real WebAuthn/YubiKey) in their showcase roles.

```bash
cd lumma-demo
./demo.sh start   # then open http://portal.localhost:8000
./demo.sh test    # automated end-to-end scenarios (virtual YubiKey)
```

See [`lumma-demo/README.md`](lumma-demo/README.md) for the full runbook,
architecture and safety notes. Everything is simulated and binds to 127.0.0.1.
