#!/usr/bin/env bash
# LummaSimulator demo platform — local launcher
#
#   ./demo.sh start    start all services (venv auto-created)
#   ./demo.sh stop     stop everything
#   ./demo.sh restart  stop + start
#   ./demo.sh status    health overview
#   ./demo.sh urls      print all demo URLs
#   ./demo.sh hosts     (optional, sudo) pin *.localhost to 127.0.0.1 in /etc/hosts
#   ./demo.sh reset     stop + wipe all state (fresh demo data)
#   ./demo.sh test      reset + start + run the end-to-end scenario tests
#
# All services bind 127.0.0.1 only. Browser URLs use *.localhost names
# (secure contexts in Chrome/Firefox — required for WebAuthn/YubiKey).

set -u
cd "$(dirname "$0")"
ROOT="$(pwd)"
VENV="$ROOT/.venv"
STATE="$ROOT/state"
PY="$VENV/bin/python"
LOGS="$STATE/logs"
PIDS="$STATE/pids"

DEMO_HOSTS="portal.localhost phish.localhost c2.localhost soc.localhost edr.localhost endpoint.localhost"

ensure_venv() {
  if [ ! -x "$PY" ]; then
    echo "[demo] creating virtualenv…"
    python3 -m venv "$VENV"
    "$PY" -m pip install -q -r requirements.txt
  fi
}

wait_healthy() { # $1=url $2=timeout_sec
  local i=0
  while [ $i -lt $(( ${2:-20} * 4 )) ]; do
    if curl -sf -m 1 "$1" >/dev/null 2>&1; then return 0; fi
    sleep 0.25; i=$((i+1))
  done
  return 1
}

do_start() {
  ensure_venv
  mkdir -p "$LOGS" "$PIDS"

  start_service() { # name module port
    local name="$1" module="$2" port="$3"
    if curl -sf -m 1 "http://127.0.0.1:$port/healthz" >/dev/null 2>&1; then
      echo "[demo] $name already running on :$port"
      return
    fi
    nohup "$PY" -m uvicorn "$module:app" --host 127.0.0.1 --port "$port" --log-level warning \
      >> "$LOGS/$name.log" 2>&1 &
    echo $! > "$PIDS/$name.pid"
    echo "[demo] $name starting on 127.0.0.1:$port (pid $(cat "$PIDS/$name.pid"))"
  }

  start_service portal portal.app 8000
  start_service phish  phish.app  8001
  start_service c2     c2.app     8002
  start_service soc    soc.app    8003
  start_service edr    edr.app    8004
  start_service endpoint endpoint_sim.app 8005

  echo
  echo "[demo] waiting for services…"
  local ok=1
  for pair in "portal:8000" "phish:8001" "c2:8002" "soc:8003" "edr:8004" "endpoint:8005"; do
    local name="${pair%%:*}" port="${pair##*:}"
    if wait_healthy "http://127.0.0.1:$port/healthz" 20; then
      echo "  ✓ $name (:$port)"
    else
      echo "  ✗ $name (:$port) FAILED — see $LOGS/$name.log"
      ok=0
    fi
  done
  echo
  if [ $ok -eq 1 ]; then
    do_urls
    echo "[demo] tip: open the portal inbox, click the phishing mail, and follow the README runbook."
  else
    echo "[demo] some services failed to start; check the logs in $LOGS/"
    exit 1
  fi
}

do_stop() {
  if [ -d "$PIDS" ]; then
    for f in "$PIDS"/*.pid; do
      [ -e "$f" ] || continue
      local name="$(basename "$f" .pid)"
      local pid="$(cat "$f")"
      if kill "$pid" 2>/dev/null; then
        echo "[demo] stopped $name (pid $pid)"
      fi
      rm -f "$f"
    done
  fi
  # belt and braces: kill anything still listening on our ports
  for port in 8000 8001 8002 8003 8004 8005; do
    local pid="$(ss -tlnpH "sport = :$port" 2>/dev/null | grep -oP 'pid=\K[0-9]+' | head -1)"
    if [ -n "${pid:-}" ] && kill "$pid" 2>/dev/null; then
      echo "[demo] killed stray process on :$port (pid $pid)"
    fi
  done
  echo "[demo] stopped."
}

do_status() {
  local bad=0
  for pair in "portal:8000" "phish:8001" "c2:8002" "soc:8003" "edr:8004" "endpoint:8005"; do
    local name="${pair%%:*}" port="${pair##*:}"
    if curl -sf -m 1 "http://127.0.0.1:$port/healthz" >/dev/null 2>&1; then
      echo "  ✓ $name (:$port) up"
    else
      echo "  ✗ $name (:$port) down"; bad=1
    fi
  done
  exit $bad
}

do_urls() {
  cat <<'EOF'

  ───────────────────────────── DEMO URLS ─────────────────────────────
   Victim portal (mail + YubiKey)  http://portal.localhost:8000
   Phishing site (AiTM)            http://phish.localhost:8001
   Attacker C2 console             http://c2.localhost:8002
   Merlon SOC dashboard            http://soc.localhost:8003
   WithSecure EDR view             http://edr.localhost:8004
   Endpoint simulator              http://endpoint.localhost:8005
  ─────────────────────────────────────────────────────────────────────
   Use Chrome or Firefox (needed for WebAuthn / YubiKey on http://*.localhost)
   Demo accounts:
     alex@northwind.demo  / Tr0ub4dor&3    (password only)
     riley@northwind.demo / Northwind2024! (+ TOTP, code shown on screen)
     sam@northwind.demo   / Yubi!Demo2024  (+ YubiKey — register first)

EOF
}

do_hosts() {
  echo "[demo] adding *.localhost entries to /etc/hosts (needs sudo)…"
  for h in $DEMO_HOSTS; do
    if ! grep -qE "^[^#]*\s$h\b" /etc/hosts; then
      echo "127.0.0.1 $h" | sudo tee -a /etc/hosts >/dev/null
      echo "  added $h"
    else
      echo "  $h already present"
    fi
  done
  echo "[demo] this guarantees all browsers resolve the demo hosts to 127.0.0.1."
}

do_reset() {
  do_stop
  rm -rf "$STATE"
  echo "[demo] all state wiped (sessions, loot, alerts, demo data)."
  echo "[demo] run './demo.sh start' for a fresh environment."
}

case "${1:-help}" in
  start)   do_start ;;
  stop)    do_stop ;;
  restart) do_stop; sleep 0.5; do_start ;;
  status)  do_status ;;
  urls)    do_urls ;;
  hosts)   do_hosts ;;
  reset)   do_reset ;;
  test)
    ensure_venv
    # deterministic runs: fresh state, then services, then tests
    do_stop >/dev/null 2>&1
    rm -rf "$STATE"
    do_start >/dev/null 2>&1 || { echo '[demo] failed to start services for tests'; exit 1; }
    "$PY" -m pytest tests/ -v
    ;;
  *)
    grep '^#   \./demo.sh' "$0" | sed 's/^#   //'
    ;;
esac
