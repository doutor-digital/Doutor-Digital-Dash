import { create } from "zustand";
import { persist } from "zustand/middleware";
import { setAuthToken } from "@/lib/api";

interface User {
  name: string;
  email: string;
  role?: string;
}
interface AuthStore {
  user: User | null;
  token: string | null;
  login: (u: User, token: string) => void;
  logout: () => void;
}

export const useAuth = create<AuthStore>()(
  persist(
    (set) => ({
      user: null,
      token: null,
      login: (user, token) => {
        setAuthToken(token);
        set({ user, token });
      },
      logout: () => {
        setAuthToken(null);
        set({ user: null, token: null });
      },
    }),
    { name: "doutor.digital.auth" }
  )
);
