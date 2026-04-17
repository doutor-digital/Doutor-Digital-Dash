import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  Cell,
} from "recharts";
import type { StageCount } from "@/types";

const COLORS = [
  "#3b63f5",
  "#5e85ff",
  "#8b5cf6",
  "#22d3ee",
  "#14b8a6",
  "#eab308",
  "#f97316",
  "#f43f5e",
  "#94a3b8",
];

export function StageBarChart({ data }: { data: StageCount[] }) {
  return (
    <div className="h-72 w-full">
      <ResponsiveContainer>
        <BarChart data={data} margin={{ top: 10, right: 10, left: 0, bottom: 40 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(148,163,184,.1)" />
          <XAxis
            dataKey="stage"
            tick={{ fill: "#94a3b8", fontSize: 11 }}
            interval={0}
            angle={-20}
            textAnchor="end"
          />
          <YAxis tick={{ fill: "#94a3b8", fontSize: 11 }} />
          <Tooltip
            contentStyle={{
              background: "rgba(11,16,32,.95)",
              border: "1px solid rgba(148,163,184,.2)",
              borderRadius: 10,
            }}
          />
          <Bar dataKey="count" radius={[6, 6, 0, 0]}>
            {data.map((_, i) => (
              <Cell key={i} fill={COLORS[i % COLORS.length]} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
