/**
 * Configuration API — chave de API da Cloudia armazenada no backend.
 *
 * Todas as rotas aceitam header `X-Admin-Key` — já injetado pelo
 * interceptor em `@/lib/api`.
 *
 * OpenAPI:
 *  - POST   /api/config/cloudia-api-key        (body: SetApiKeyRequest)
 *  - GET    /api/config/cloudia-api-key/status
 *  - DELETE /api/config/cloudia-api-key
 *
 * LIMITAÇÃO: o schema `SetApiKeyRequest` está referenciado mas não
 * inlineado no OpenAPI. Assumimos o shape {apiKey, expiresAt?}. Se o
 * backend evoluir, ajustar em `src/api/types.ts#SetApiKeyRequest`.
 */

import { api } from "@/lib/api";
import { normalizeCloudiaStatus } from "@/adapters/normalize";
import type { CloudiaKeyStatusDto, SetApiKeyRequest } from "@/api/types";

export const configurationService = {
  async setCloudiaKey(payload: SetApiKeyRequest): Promise<void> {
    await api.post<unknown>("/api/config/cloudia-api-key", payload);
  },

  async getCloudiaStatus(): Promise<CloudiaKeyStatusDto> {
    const { data } = await api.get<unknown>(
      "/api/config/cloudia-api-key/status"
    );
    return normalizeCloudiaStatus(data);
  },

  async deleteCloudiaKey(): Promise<void> {
    await api.delete<unknown>("/api/config/cloudia-api-key");
  },
};
