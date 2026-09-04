C=$(docker ps -qf name=ddapi_api|head -1)
K=$(docker exec "$C" printenv Admin__ApiKey)
curl -s -o /tmp/r.json -w "http=%{http_code} tempo=%{time_total}s\n" -X POST \
  "https://api-vps.doutordigitalconsultoria.com/internal/spine/reconciliacao/preencher?unitId=18&de=2026-08-01&ate=2026-08-31&aplicar=true" \
  -H "X-Admin-Key: $K" --max-time 900
echo "== log dos ultimos 90s, tirando o ruido de SQL:"
docker logs --since 90s "$C" 2>&1 \
  | grep -avE "Executed DbCommand|^ *(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|LEFT|INNER|ORDER|LIMIT|VALUES|RETURNING)" \
  | tail -45 | cut -c1-240
