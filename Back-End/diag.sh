K=$(docker exec $(docker ps -qf name=ddapi_api|head -1) printenv Admin__ApiKey)
curl -s "https://api-vps.doutordigitalconsultoria.com/internal/spine/tratamentos/diagnostico?unitId=$1&de=2026-08-01&ate=2026-08-31" \
  -H "X-Admin-Key: $K" --max-time 600
