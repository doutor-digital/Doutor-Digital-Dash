import { api } from "@/lib/api";

export interface LoginRequest {
  email: string;
  password: string;
}

export interface Unit {
  id: number;
  clinicId: number;
  name: string;
  isDefault: boolean;
}

export interface LoginResponse {
  userName: string;
  email: string;
  role: string;
  tokenType: string;
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  selectedUnit: Unit;
  availableUnits: Unit[];
}

export const authService = {
  async login(payload: LoginRequest): Promise<LoginResponse> {
    const { data } = await api.post<LoginResponse>(
      "/api/auth/login",
      payload
    );

    return data;
  },
};