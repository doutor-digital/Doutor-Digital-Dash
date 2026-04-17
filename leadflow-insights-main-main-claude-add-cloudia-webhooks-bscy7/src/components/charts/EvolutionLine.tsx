import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

export function EvolutionLine({
  data,
}: {
  data: Array<{ periodo: string; total: number }>;
}) {
  return (
    <div className="h-72 w-full">
      <ResponsiveContainer>
        <AreaChart data={data} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
          <defs>
            <linearGradient id="gradient-brand" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#3b63f5" stopOpacity={0.6} />
              <stop offset="95%" stopColor="#3b63f5" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(148,163,184,.1)" />
          <XAxis dataKey="periodo" tick={{ fill: "#94a3b8", fontSize: 11 }} />
          <YAxis tick={{ fill: "#94a3b8", fontSize: 11 }} />
          <Tooltip
            contentStyle={{
              background: "rgba(11,16,32,.95)",
              border: "1px solid rgba(148,163,184,.2)",
              borderRadius: 10,
            }}
          />
          <Area
            type="monotone"
            dataKey="total"
            stroke="#5e85ff"
            strokeWidth={2.5}
            fill="url(#gradient-brand)"
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
