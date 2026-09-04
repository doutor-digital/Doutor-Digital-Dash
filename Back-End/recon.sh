#!/bin/bash
# Cruzamento franquia x Kommo em todas as unidades. So LEITURA: esta rota nao
# escreve nada na Kommo, apenas grava o vinculo no nosso banco e relata.
K=$(docker exec $(docker ps -qf name=ddapi_api|head -1) printenv Admin__ApiKey)
curl -s -X POST "https://api-vps.doutordigitalconsultoria.com/internal/spine/reconciliacao/todas?de=${1}&ate=${2}&pausaMs=1200" \
     -H "X-Admin-Key: $K" --max-time 1800
