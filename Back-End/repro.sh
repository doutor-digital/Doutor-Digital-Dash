C=$(docker ps -qf name=ddapi_api|head -1)
K=$(docker exec "$C" printenv Admin__ApiKey)
# marca o ponto no log para so ler o que vier depois
MARCO=$(date -u +%Y-%m-%dT%H:%M:%S)
echo "== chamando Canaã (aplicar=true)"
curl -s -X POST "https://api-vps.doutordigitalconsultoria.com/internal/spine/reconciliacao/preencher?unitId=18&de=2026-08-01&ate=2026-08-31&aplicar=true" \
  -H "X-Admin-Key: $K" --max-time 900 | head -c 400
echo
echo "== log a partir de $MARCO"
docker logs --since "$MARCO" "$C" 2>&1 | grep -aE "Unhandled exception|Kommo PATCH|Reconciliacao: gravado|System\.[A-Za-z]+Exception" | head -12 | cut -c1-260
