import { Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";

const COLORS = [
  "#3b63f5",
  "#8b5cf6",
  "#14b8a6",
  "#f97316",
  "#f43f5e",
  "#22d3ee",
  "#eab308",
  "#10b981",
  "#a855f7",
  "#94a3b8",
];

export function SourceDonut({
  data,
}: {
  data: Array<{ name: string; value: number }>;
}) {
  return (
    <div className="h-72 w-full">
      <ResponsiveContainer>
        <PieChart>
          <Pie
            data={data}
            dataKey="value"
            nameKey="name"
            innerRadius={60}
            outerRadius={95}
            paddingAngle={2}
            stroke="none"
          >
            {data.map((_, i) => (
              <Cell key={i} fill={COLORS[i % COLORS.length]} />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{
              background: "rgba(11,16,32,.95)",
              border: "1px solid rgba(148,163,184,.2)",
              borderRadius: 10,
            }}
          />
          <Legend
            verticalAlign="bottom"
            height={32}
            wrapperStyle={{ color: "#cbd5e1", fontSize: 11 }}
          />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
