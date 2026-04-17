import { api } from "@/lib/api";

export interface SetApiKeyRequest {
  apiKey: string;
  expiresAt?: string;
  expiresInDays?: number;
}

export interface CloudiaApiKeyStatus {
  configured: boolean;
  expiresAt?: string | null;
}

interface CloudiaStatusRaw {
  configured?: boolean;
  is_configured?: boolean;
  expiresAt?: string | null;
  expires_at?: string | null;
}

export const configService = {
  async setCloudiaKey(payload: SetApiKeyRequest): Promise<unknown> {
    const { data } = await api.post<unknown>("/api/config/cloudia-api-key", payload);
    return data;
  },

  async status(): Promise<CloudiaApiKeyStatus> {
    const { data } = await api.get<CloudiaStatusRaw>("/api/config/cloudia-api-key/status");
    return {
      configured: Boolean(data?.configured ?? data?.is_configured ?? false),
      expiresAt: data?.expiresAt ?? data?.expires_at ?? null,
    };
  },

  async remove(): Promise<unknown> {
    const { data } = await api.delete<unknown>("/api/config/cloudia-api-key");
    return data;
  },

  async setAdminKey(key: string): Promise<unknown> {
    const { data } = await api.post<unknown>("/api/config/admin-key", { key });
    return data;
  },
};
