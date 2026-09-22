// Tek serili, ince çubuklu SVG grafik. Değerler yalnızca uçlarda ve etkileşimde gösterilir; metin renk taşımaz.
export type BarPoint = { label: string; value: number; hint?: string };

export default function BarChart({ points, unit = "", height = 140, tone = "signal", format }: {
  points: BarPoint[]; unit?: string; height?: number; tone?: "signal" | "warn" | "info"; format?: (v: number) => string;
}) {
  const fmt = format ?? ((v: number) => new Intl.NumberFormat("tr-TR").format(v));
  const max = Math.max(1, ...points.map(p => p.value));
  const width = 720, left = 44, bottom = 22, top = 14;
  const plotW = width - left - 8, plotH = height - top - bottom;
  const slot = plotW / Math.max(1, points.length);
  const bar = Math.min(24, Math.max(4, slot - 4));
  const ticks = [0, max / 2, max].map(v => Math.round(v));
  const color = tone === "warn" ? "var(--warn)" : tone === "info" ? "var(--info)" : "var(--signal)";
  const peak = points.reduce((best, p, i) => (p.value > (points[best]?.value ?? -1) ? i : best), 0);
  return <svg className="chart" viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${points.length} dönemlik çubuk grafik`}>
    {ticks.map((t, i) => { const y = top + plotH - (t / max) * plotH; return <g key={i}>
      <line x1={left} x2={width - 8} y1={y} y2={y} className="grid" />
      <text x={left - 6} y={y + 4} className="axis" textAnchor="end">{fmt(t)}</text>
    </g>; })}
    {points.map((p, i) => {
      const h = Math.max(p.value > 0 ? 2 : 0, (p.value / max) * plotH);
      const x = left + i * slot + (slot - bar) / 2, y = top + plotH - h;
      const r = Math.min(4, h / 2);
      const path = h > 0 ? `M${x},${top + plotH} V${y + r} a${r},${r} 0 0 1 ${r},-${r} H${x + bar - r} a${r},${r} 0 0 1 ${r},${r} V${top + plotH} Z` : "";
      const showLabel = points.length <= 16 || i % Math.ceil(points.length / 12) === 0;
      return <g key={i} className="bar-group">
        {path && <path d={path} fill={color} />}
        <rect x={left + i * slot} y={top} width={slot} height={plotH + bottom} fill="transparent"><title>{`${p.label}: ${fmt(p.value)}${unit}${p.hint ? ` · ${p.hint}` : ""}`}</title></rect>
        {i === peak && p.value > 0 && <text x={x + bar / 2} y={y - 4} className="value" textAnchor="middle">{fmt(p.value)}{unit}</text>}
        {showLabel && <text x={x + bar / 2} y={height - 6} className="axis" textAnchor="middle">{p.label}</text>}
      </g>;
    })}
  </svg>;
}
