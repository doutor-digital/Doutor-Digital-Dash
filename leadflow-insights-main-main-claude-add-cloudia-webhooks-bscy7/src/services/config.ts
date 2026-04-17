import { api } from "@/lib/api";

export interface SetApiKeyRequest {
  apiKey: string;
  expiresAt?: string;
}

export interface CloudiaApiKeyStatus {
  configured: boolean;
  expiresAt?: string | null;
}

export const configService = {
  async setCloudiaKey(payload: SetApiKeyRequest): Promise<unknown> {
    const { data } = await api.post<unknown>("/api/config/cloudia-api-key", payload);
    return data;
  },

  async status(): Promise<CloudiaApiKeyStatus> {
    const { data } = await api.get<CloudiaApiKeyStatus>("/api/config/cloudia-api-key/status");
    return data;
  },

  async remove(): Promise<unknown> {
    const { data } = await api.delete<unknown>("/api/config/cloudia-api-key");
    return data;
  },
};
