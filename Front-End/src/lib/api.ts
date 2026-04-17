import axios, { AxiosError } from "axios";
import type { ProblemDetails } from "@/types";
import { toast } from "sonner";

export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL || "http://localhost:5000";

export const api = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30_000,
  headers: { "Content-Type": "application/json" },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem("auth_token");
  if (token) config.headers.Authorization = `Bearer ${token}`;

  const adminKey = localStorage.getItem("admin_key");
  if (adminKey) config.headers["X-Admin-Key"] = adminKey;

  const cloudiaBearerToken = localStorage.getItem("cloudia_bearer_token");
  if (cloudiaBearerToken) {
    config.headers["X-Cloudia-Bearer"] = cloudiaBearerToken;
  }

  const cloudiaBaseUrl = localStorage.getItem("cloudia_base_url");
  if (cloudiaBaseUrl) {
    config.headers["X-Cloudia-Base-Url"] = cloudiaBaseUrl;
  }

  return config;
});

api.interceptors.response.use(
  (r) => r,
  (error: AxiosError<ProblemDetails>) => {
    const status = error.response?.status;
    const msg =
      error.response?.data?.detail ||
      error.response?.data?.title ||
      error.message;

    if (status === 401) {
      localStorage.removeItem("auth_token");
      if (!window.location.pathname.startsWith("/login")) {
        window.location.href = "/login";
      }
    } else if (status && status >= 500) {
      toast.error(`Erro ${status}: ${msg || "falha no servidor"}`);
    } else if (status === 404) {
      // silent - deixa o caller decidir
    } else if (msg && status !== 401) {
      toast.error(msg);
    }
    return Promise.reject(error);
  }
);

export function setAuthToken(token: string | null) {
  if (token) localStorage.setItem("auth_token", token);
  else localStorage.removeItem("auth_token");
}

export function setAdminKey(key: string | null) {
  if (key) localStorage.setItem("admin_key", key);
  else localStorage.removeItem("admin_key");
}


export function setCloudiaBearerToken(token: string | null) {
  if (token) localStorage.setItem("cloudia_bearer_token", token);
  else localStorage.removeItem("cloudia_bearer_token");
}

export function setCloudiaBaseUrl(url: string | null) {
  if (url) localStorage.setItem("cloudia_base_url", url);
  else localStorage.removeItem("cloudia_base_url");
}
