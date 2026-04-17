import { ReactElement, Suspense, lazy } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "@/hooks/useAuth";
import { useClinic } from "@/hooks/useClinic";
import { Loader2 } from "lucide-react";

// ─── Pages (lazy) ─────────────────────────────────────────────────────────────

const DashboardLayout  = lazy(() => import("@/components/layout/DashboardLayout"));
const LoginPage        = lazy(() => import("@/pages/LoginPage"));
const DashboardPage    = lazy(() => import("@/pages/DashboardPage"));
const UnitSelectPage   = lazy(() => import("@/pages/UnitSelectPage"));
const LeadsPage        = lazy(() => import("@/pages/LeadsPage"));
const LeadDetailPage   = lazy(() => import("@/pages/LeadDetailPage"));
const FunnelPage       = lazy(() => import("@/pages/FunnelPage"));
const SourcesPage      = lazy(() => import("@/pages/SourcesPage"));
const EvolutionPage    = lazy(() => import("@/pages/EvolutionPage"));
const LiveMetricsPage  = lazy(() => import("@/pages/LiveMetricsPage"));
const AnalyticsPage    = lazy(() => import("@/pages/AnalyticsPage"));
const AlertsPage       = lazy(() => import("@/pages/AlertsPage"));
const AttendantsPage   = lazy(() => import("@/pages/AttendantsPage"));
const UnitsPage        = lazy(() => import("@/pages/UnitsPage"));
const ReportsPage      = lazy(() => import("@/pages/ReportsPage"));
const SettingsPage     = lazy(() => import("@/pages/SettingsPage"));
const NotFoundPage     = lazy(() => import("@/pages/NotFoundPage"));

// ─── Fullscreen loader ────────────────────────────────────────────────────────

function RouteLoader() {
  return (
    <div className="fixed inset-0 z-50 flex flex-col items-center justify-center gap-3 bg-[rgba(6,6,14,0.97)]">
      {/* Logo / identidade */}
      <div className="mb-1 flex items-center gap-2 select-none">
        <div className="h-8 w-8 rounded-xl bg-gradient-to-br from-brand-400 to-violet-600 grid place-items-center shadow-lg shadow-violet-500/20">
          <span className="text-[13px] font-black text-white">D</span>
        </div>
        <span className="text-[15px] font-bold tracking-tight text-slate-200">
          Doutor Digital
        </span>
      </div>

      {/* Spinner */}
      <Loader2 className="h-5 w-5 animate-spin text-brand-400" />

      {/* Texto */}
      <p className="text-[11px] text-slate-600 tracking-widest uppercase">
        Carregando…
      </p>
    </div>
  );
}

// ─── Guards ───────────────────────────────────────────────────────────────────

function RequireAuth({ children }: { children: ReactElement }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  return children;
}

function RequireClinic({ children }: { children: ReactElement }) {
  const { unitId, tenantId } = useClinic();
  if (!unitId && !tenantId) return <Navigate to="/select-unit" replace />;
  return children;
}

// ─── App ──────────────────────────────────────────────────────────────────────

export default function App() {
  return (
    <Suspense fallback={<RouteLoader />}>
      <Routes>

        {/* Pública */}
        <Route path="/login" element={<LoginPage />} />

        {/* Seleção de unidade — requer login, mas não clínica */}
        <Route
          path="/select-unit"
          element={
            <RequireAuth>
              <UnitSelectPage />
            </RequireAuth>
          }
        />

        {/* Área protegida — requer login + clínica */}
        <Route
          element={
            <RequireAuth>
              <RequireClinic>
                <DashboardLayout />
              </RequireClinic>
            </RequireAuth>
          }
        >
          <Route index path="/"            element={<DashboardPage />}   />
          <Route path="/leads"             element={<LeadsPage />}        />
          <Route path="/leads/:id"         element={<LeadDetailPage />}   />
          <Route path="/funnel"            element={<FunnelPage />}       />
          <Route path="/sources"           element={<SourcesPage />}      />
          <Route path="/evolution"         element={<EvolutionPage />}    />
          <Route path="/live"              element={<LiveMetricsPage />}  />
          <Route path="/analytics"         element={<AnalyticsPage />}    />
          <Route path="/alerts"            element={<AlertsPage />}       />
          <Route path="/attendants"        element={<AttendantsPage />}   />
          <Route path="/units"             element={<UnitsPage />}        />
          <Route path="/reports"           element={<ReportsPage />}      />
          <Route path="/settings"          element={<SettingsPage />}     />
          <Route path="*"                  element={<NotFoundPage />}     />
        </Route>

      </Routes>
    </Suspense>
  );
}