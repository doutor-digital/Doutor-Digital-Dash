C=$(docker ps -qf name=ddapi_api|head -1)
K=$(docker exec "$C" printenv Admin__ApiKey)
MARCO=$(date -u +%Y-%m-%dT%H:%M:%S)
curl -s -o /tmp/r.json -w "http=%{http_code} tempo=%{time_total}s\n" -X POST \
  "https://api-vps.doutordigitalconsultoria.com/internal/spine/reconciliacao/preencher?unitId=18&de=2026-08-01&ate=2026-08-31&aplicar=true" \
  -H "X-Admin-Key: $K" --max-time 900
head -c 200 /tmp/r.json; echo
echo "== ultimas linhas de log (sem filtro), nivel warn+:"
docker logs --since "$MARCO" "$C" 2>&1 | grep -avE "^ *(SELECT|INSERT|UPDATE|FROM|WHERE|LEFT|ORDER|LIMIT|VALUES|Executed|info:)" | tail -40 | cut -c1-240
