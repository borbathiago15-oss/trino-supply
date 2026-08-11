#!/bin/sh
# Vigia do piloto (OPS-001): verifica a saúde da API a cada minuto e o frescor do backup a cada
# hora. Transições (caiu/voltou) são notificadas via webhook (ALERT_WEBHOOK_URL — Slack/Discord/
# Google Chat aceitam {"text": "..."}). Sem webhook configurado, apenas loga — o operador
# acompanha com `docker compose logs -f alerts`.
set -u

API_URL="${API_HEALTH_URL:-http://api:8080/health/ready}"
WEBHOOK="${ALERT_WEBHOOK_URL:-}"
INTERVAL="${CHECK_INTERVAL_SECONDS:-60}"
THRESHOLD="${FAIL_THRESHOLD:-3}"

notify() {
  msg="$1"
  echo "[alerta] $(date -u +%FT%TZ) $msg"
  if [ -n "$WEBHOOK" ]; then
    curl -fsS -m 10 -X POST -H 'Content-Type: application/json' \
      -d "{\"text\":\"Trino Supply: $msg\"}" "$WEBHOOK" >/dev/null 2>&1 \
      || echo "[alerta] falha ao entregar no webhook"
  fi
}

state=up
fails=0
ticks=0

while true; do
  if curl -fsS -m 5 "$API_URL" >/dev/null 2>&1; then
    if [ "$state" = "down" ]; then
      state=up
      notify "API voltou ao ar (health OK)."
    fi
    fails=0
  else
    fails=$((fails + 1))
    echo "[vigia] health falhou ($fails/$THRESHOLD)"
    if [ "$fails" -ge "$THRESHOLD" ] && [ "$state" = "up" ]; then
      state=down
      notify "API FORA DO AR — health falhou $THRESHOLD vezes seguidas."
    fi
  fi

  # A cada hora: existe backup das últimas 26h? (janela diária + folga)
  ticks=$((ticks + 1))
  if [ $((ticks * INTERVAL)) -ge 3600 ]; then
    ticks=0
    if [ -d /backups ]; then
      fresh=$(find /backups -name '*.dump' -mmin -1560 2>/dev/null | head -1)
      [ -z "$fresh" ] && notify "ATENÇÃO: nenhum backup nas últimas 26 horas — verifique o serviço 'backup'."
    fi
  fi

  sleep "$INTERVAL"
done
