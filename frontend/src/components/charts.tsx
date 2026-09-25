"use client";

import { useId, useRef, useState } from "react";

// Panelin grafikleri: yumuşak eğriler, gradyan dolgular, tek uyumlu palet, üstüne gelince ayrıntı.
// Değerler her zaman tablo ya da sayı olarak da ekranda; grafik tamamlayıcıdır, tek kaynak değildir.

export type Tone = "signal" | "good" | "warn" | "bad" | "info" | "mute";
const toneVar: Record<Tone, string> = {
  signal: "var(--chart)", good: "var(--chart-good)", warn: "var(--chart-warn)", bad: "var(--chart-bad)", info: "var(--chart-info)", mute: "var(--chart-mute)",
};
const toneTint: Record<Tone, string> = {
  signal: "var(--signal-tint)", good: "var(--good-tint)", warn: "var(--warn-tint)", bad: "var(--bad-tint)", info: "var(--info-tint)", mute: "var(--mute-tint)",
};
const fmt = (v: number) => new Intl.NumberFormat("tr-TR").format(v);

type TipRow = { label: string; value: string; color?: string };
type Tip = { x: number; y: number; title: string; rows: TipRow[] };

function useTip() {
  const [tip, setTip] = useState<Tip | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  // İpucu görünüm alanına göre sabitlenir; taşması gizlenen kartların içinde kırpılmaz.
  const show = (e: { clientX: number; clientY: number }, title: string, rows: TipRow[]) => setTip({ x: e.clientX, y: e.clientY, title, rows });
  return { tip, ref, show, hide: () => setTip(null) };
}

function TipBox({ tip }: { tip: Tip | null }) {
  if (!tip) return null;
  const flip = typeof window !== "undefined" && tip.x > window.innerWidth - 240;
  return <div className={`chart-tip${flip ? " flip" : ""}`} style={{ left: tip.x, top: tip.y }} role="status">
    <div className="chart-tip-title">{tip.title}</div>
    {tip.rows.map((r, i) => <div key={i} className="chart-tip-row">
      {r.color && <span className="chart-tip-key" style={{ background: r.color }} />}
      <strong>{r.value}</strong><span>{r.label}</span>
    </div>)}
  </div>;
}

// Tek yönlü (monoton) kübik eğri: noktaların arasında sahte tepe ya da çukur üretmez.
function smoothPath(pts: [number, number][]) {
  const n = pts.length;
  if (n === 0) return "";
  if (n === 1) return `M${pts[0][0]},${pts[0][1]}`;
  const dx: number[] = [], dy: number[] = [], m: number[] = [];
  for (let i = 0; i < n - 1; i++) { dx[i] = pts[i + 1][0] - pts[i][0]; dy[i] = pts[i + 1][1] - pts[i][1]; m[i] = dx[i] ? dy[i] / dx[i] : 0; }
  const t: number[] = [m[0]];
  for (let i = 1; i < n - 1; i++) t[i] = m[i - 1] * m[i] <= 0 ? 0 : (m[i - 1] + m[i]) / 2;
  t[n - 1] = m[n - 2];
  for (let i = 0; i < n - 1; i++) {
    if (m[i] === 0) { t[i] = 0; t[i + 1] = 0; continue; }
    const a = t[i] / m[i], b = t[i + 1] / m[i], s = a * a + b * b;
    if (s > 9) { const k = 3 / Math.sqrt(s); t[i] = k * a * m[i]; t[i + 1] = k * b * m[i]; }
  }
  let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
  for (let i = 0; i < n - 1; i++) {
    const h = dx[i] / 3;
    d += ` C${(pts[i][0] + h).toFixed(1)},${(pts[i][1] + t[i] * h).toFixed(1)} ${(pts[i + 1][0] - h).toFixed(1)},${(pts[i + 1][1] - t[i + 1] * h).toFixed(1)} ${pts[i + 1][0].toFixed(1)},${pts[i + 1][1].toFixed(1)}`;
  }
  return d;
}

// Eksen için düzgün üst sınır: 1, 2, 5 katlarına yuvarlar.
function niceMax(v: number) {
  if (v <= 4) return Math.max(1, Math.ceil(v));
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const k of [1, 2, 2.5, 5, 10]) if (k * p >= v) return k * p;
  return 10 * p;
}

/* Eğilim grafiği: alan eğrisi (ana seri) ve sütunlar (ikincil seri) aynı eksende; imleç günü bulur. */
export function TrendChart({ points, primary, secondary, height = 180, format = fmt }: {
  points: { label: string; a: number; b: number }[];
  primary: { name: string; tone: Tone }; secondary: { name: string; tone: Tone };
  height?: number; format?: (v: number) => string;
}) {
  const { tip, ref, show, hide } = useTip();
  const [active, setActive] = useState<number | null>(null);
  const id = useId();
  const w = 760, left = 36, right = 14, top = 16, bottom = 26;
  const plotW = w - left - right, plotH = height - top - bottom;
  const n = points.length;
  const max = niceMax(Math.max(1, ...points.map(p => Math.max(p.a, p.b))));
  const slot = plotW / Math.max(1, n);
  const x = (i: number) => left + slot * (i + 0.5);
  const y = (v: number) => top + (1 - v / max) * plotH;
  const bar = Math.min(18, Math.max(4, slot * 0.42));
  const linePts = points.map((p, i) => [x(i), y(p.a)] as [number, number]);
  const line = smoothPath(linePts);
  const area = n > 1 ? `${line} L${x(n - 1).toFixed(1)},${top + plotH} L${x(0).toFixed(1)},${top + plotH} Z` : "";
  const ca = toneVar[primary.tone], cb = toneVar[secondary.tone];
  const ticks = max <= 4 ? Array.from({ length: max + 1 }, (_, i) => i) : [0, max / 2, max];
  const every = n <= 16 ? 1 : Math.ceil(n / 12);
  const onMove = (e: React.PointerEvent<SVGSVGElement>) => {
    const r = e.currentTarget.getBoundingClientRect();
    const px = ((e.clientX - r.left) / r.width) * w;
    const i = Math.max(0, Math.min(n - 1, Math.floor((px - left) / slot)));
    setActive(i);
    show(e, points[i].label, [{ label: primary.name, value: format(points[i].a), color: ca }, { label: secondary.name, value: format(points[i].b), color: cb }]);
  };
  return <div className="chart-wrap" ref={ref}>
    <svg className="chart trend" viewBox={`0 0 ${w} ${height}`} role="img" aria-label={`${primary.name} ve ${secondary.name}, ${n} gün`}
      onPointerMove={onMove} onPointerLeave={() => { setActive(null); hide(); }}>
      <defs>
        <linearGradient id={`${id}a`} x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor={ca} stopOpacity="0.28" /><stop offset="1" stopColor={ca} stopOpacity="0" /></linearGradient>
        <linearGradient id={`${id}b`} x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor={cb} stopOpacity="0.95" /><stop offset="1" stopColor={cb} stopOpacity="0.55" /></linearGradient>
      </defs>
      {ticks.map((t, i) => <g key={i}>
        <line x1={left} x2={w - right} y1={y(t)} y2={y(t)} className="grid" />
        <text x={left - 8} y={y(t) + 4} className="axis" textAnchor="end">{format(Math.round(t))}</text>
      </g>)}
      {points.map((p, i) => {
        const h = p.b > 0 ? Math.max(3, (p.b / max) * plotH) : 0;
        const bx = x(i) - bar / 2, by = top + plotH - h, r = Math.min(4, bar / 2, h);
        return h > 0 ? <path key={i} d={`M${bx},${top + plotH} V${by + r} a${r},${r} 0 0 1 ${r},-${r} H${bx + bar - r} a${r},${r} 0 0 1 ${r},${r} V${top + plotH} Z`}
          fill={`url(#${id}b)`} style={{ opacity: active !== null && active !== i ? 0.45 : 1, transition: "opacity .15s" }} /> : null;
      })}
      {area && <path d={area} fill={`url(#${id}a)`} />}
      {n > 1 && <path d={line} fill="none" stroke={ca} strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />}
      {active !== null && <line x1={x(active)} x2={x(active)} y1={top - 4} y2={top + plotH} className="crosshair" />}
      {points.map((p, i) => (i === n - 1 || active === i) && <g key={`d${i}`}>
        <circle cx={x(i)} cy={y(p.a)} r="8" fill={ca} opacity="0.18" />
        <circle cx={x(i)} cy={y(p.a)} r="4" fill={ca} stroke="var(--surface)" strokeWidth="2" />
      </g>)}
      {points.map((p, i) => i % every === 0 && <text key={`l${i}`} x={x(i)} y={height - 8} className="axis" textAnchor="middle">{p.label}</text>)}
    </svg>
    <TipBox tip={tip} />
  </div>;
}

/* Alan çizgisi: tek seri, yumuşak eğri; imleç en yakın noktaya kilitlenir. */
export function Sparkline({ points, tone = "signal", height = 64, unit = "", format = fmt, label }: {
  points: { label: string; value: number; hint?: string }[]; tone?: Tone; height?: number; unit?: string; format?: (v: number) => string; label: string;
}) {
  const { tip, ref, show, hide } = useTip();
  const [active, setActive] = useState<number | null>(null);
  const id = useId();
  const w = 320, padX = 8, padT = 10, padB = 6;
  const max = Math.max(1, ...points.map(p => p.value));
  const n = points.length;
  const x = (i: number) => n <= 1 ? w / 2 : padX + (i / (n - 1)) * (w - 2 * padX);
  const y = (v: number) => padT + (1 - v / max) * (height - padT - padB);
  const line = smoothPath(points.map((p, i) => [x(i), y(p.value)]));
  const area = n > 1 ? `${line} L${x(n - 1).toFixed(1)},${height - padB} L${x(0).toFixed(1)},${height - padB} Z` : "";
  const color = toneVar[tone];
  const last = n - 1;
  const onMove = (e: React.PointerEvent<SVGSVGElement>) => {
    const r = e.currentTarget.getBoundingClientRect();
    const px = ((e.clientX - r.left) / r.width) * w;
    let best = 0; for (let i = 1; i < n; i++) if (Math.abs(x(i) - px) < Math.abs(x(best) - px)) best = i;
    setActive(best);
    const p = points[best];
    show(e, p.label, [{ label: p.hint ?? label, value: `${format(p.value)}${unit}`, color }]);
  };
  return <div className="chart-wrap" ref={ref}>
    <svg className="chart spark" viewBox={`0 0 ${w} ${height}`} role="img" aria-label={`${label}, ${n} dönem`}
      onPointerMove={onMove} onPointerLeave={() => { setActive(null); hide(); }}>
      <defs><linearGradient id={id} x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor={color} stopOpacity="0.3" /><stop offset="1" stopColor={color} stopOpacity="0" /></linearGradient></defs>
      {area && <path d={area} fill={`url(#${id})`} />}
      {n > 1 && <path d={line} fill="none" stroke={color} strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />}
      {active !== null && <line x1={x(active)} x2={x(active)} y1={padT - 4} y2={height - padB} className="crosshair" />}
      {n > 0 && <circle cx={x(last)} cy={y(points[last].value)} r="4" fill={color} stroke="var(--surface)" strokeWidth="2" />}
      {active !== null && active !== last && <circle cx={x(active)} cy={y(points[active].value)} r="4" fill={color} stroke="var(--surface)" strokeWidth="2" />}
    </svg>
    <TipBox tip={tip} />
  </div>;
}

/* Sütunlar: bir ya da iki seri; her sütun kendi ayrıntısını taşır, iki seride açıklama şeridi görünür. */
export function Columns({ points, series, height = 140, unit = "", format = fmt, axis = true, labelEvery }: {
  points: { label: string; values: number[]; hint?: string }[]; series: { name: string; tone: Tone }[];
  height?: number; unit?: string; format?: (v: number) => string; axis?: boolean; labelEvery?: number;
}) {
  const { tip, ref, show, hide } = useTip();
  const [active, setActive] = useState<number | null>(null);
  const id = useId();
  const w = 720, left = axis ? 44 : 6, right = 8, top = 12, bottom = axis ? 22 : 6;
  const plotW = w - left - right, plotH = height - top - bottom;
  const max = niceMax(Math.max(1, ...points.flatMap(p => p.values)));
  const n = Math.max(1, points.length), k = Math.max(1, series.length);
  const slot = plotW / n;
  const bar = Math.min(22, Math.max(3, (slot - 6) / k - 2));
  const group = bar * k + 2 * (k - 1);
  const ticks = axis ? (max <= 4 ? Array.from({ length: max + 1 }, (_, i) => i) : [0, max / 2, max]) : [];
  const every = labelEvery ?? (points.length <= 16 ? 1 : Math.ceil(points.length / 12));
  return <div className="chart-wrap" ref={ref}>
    {series.length > 1 && <div className="chart-legend">{series.map(s => <span key={s.name}><i style={{ background: toneVar[s.tone] }} />{s.name}</span>)}</div>}
    <svg className="chart" viewBox={`0 0 ${w} ${height}`} role="img" aria-label={`${series.map(s => s.name).join(", ")}, ${points.length} dönem`}
      onPointerLeave={() => { setActive(null); hide(); }}>
      <defs>{series.map((s, j) => <linearGradient key={j} id={`${id}${j}`} x1="0" x2="0" y1="0" y2="1">
        <stop offset="0" stopColor={toneVar[s.tone]} stopOpacity="0.95" /><stop offset="1" stopColor={toneVar[s.tone]} stopOpacity="0.55" /></linearGradient>)}</defs>
      {ticks.map((t, i) => { const yy = top + plotH - (t / max) * plotH; return <g key={i}>
        <line x1={left} x2={w - right} y1={yy} y2={yy} className="grid" />
        <text x={left - 6} y={yy + 4} className="axis" textAnchor="end">{format(Math.round(t))}</text>
      </g>; })}
      {!axis && <line x1={left} x2={w - right} y1={top + plotH} y2={top + plotH} className="grid" />}
      {points.map((p, i) => {
        const gx = left + i * slot + (slot - group) / 2;
        const dim = active !== null && active !== i;
        return <g key={i} style={{ opacity: dim ? 0.45 : 1, transition: "opacity .15s" }}
          onPointerMove={e => { setActive(i); const rows: TipRow[] = series.map((s, j) => ({ label: s.name, value: `${format(p.values[j] ?? 0)}${unit}`, color: toneVar[s.tone] })); if (p.hint) rows.push({ label: p.hint, value: "" }); show(e, p.label, rows); }}>
          <rect x={left + i * slot} y={top - 4} width={slot} height={plotH + bottom + 4} fill="transparent" />
          {series.map((s, j) => {
            const v = p.values[j] ?? 0;
            const h = v > 0 ? Math.max(3, (v / max) * plotH) : 0;
            const xx = gx + j * (bar + 2), yy = top + plotH - h, r = Math.min(4, h / 2, bar / 2);
            const d = h > 0 ? `M${xx},${top + plotH} V${yy + r} a${r},${r} 0 0 1 ${r},-${r} H${xx + bar - r} a${r},${r} 0 0 1 ${r},${r} V${top + plotH} Z` : "";
            return d ? <path key={j} d={d} fill={`url(#${id}${j})`} /> : null;
          })}
          {axis && i % every === 0 && <text x={gx + group / 2} y={height - 6} className="axis" textAnchor="middle">{p.label}</text>}
        </g>;
      })}
    </svg>
    <TipBox tip={tip} />
  </div>;
}

/* Halka: parça-bütün. Dilimler arasında yüzey boşluğu, ortada toplam; üstüne gelince dilim öne çıkar. */
export function Donut({ segments, size = 72, thickness = 9, center, title = "Dağılım" }: {
  segments: { label: string; value: number; tone: Tone }[]; size?: number; thickness?: number; center?: React.ReactNode; title?: string;
}) {
  const { tip, ref, show, hide } = useTip();
  const [active, setActive] = useState<number | null>(null);
  const total = segments.reduce((a, s) => a + s.value, 0);
  const r = (size - thickness) / 2, c = 2 * Math.PI * r, gap = segments.filter(s => s.value > 0).length > 1 ? 3 : 0;
  let start = 0;
  return <div className="chart-wrap donut-wrap" ref={ref} style={{ width: size, height: size }}>
    <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img" aria-label={`${title}: ${segments.map(s => `${s.label} ${fmt(s.value)}`).join(", ")}`}
      onPointerLeave={() => { setActive(null); hide(); }}>
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--line-2)" strokeWidth={thickness} />
      {total > 0 && segments.map((s, i) => {
        if (s.value <= 0) return null;
        const len = (s.value / total) * c;
        const el = <circle key={i} cx={size / 2} cy={size / 2} r={r} fill="none" stroke={toneVar[s.tone]} strokeWidth={active === i ? thickness + 2 : thickness}
          strokeDasharray={`${Math.max(0.5, len - gap)} ${c}`} strokeDashoffset={-(start + gap / 2)} transform={`rotate(-90 ${size / 2} ${size / 2})`}
          style={{ opacity: active !== null && active !== i ? 0.35 : 1, transition: "opacity .15s, stroke-width .15s" }}
          onPointerMove={e => { setActive(i); show(e, s.label, [{ label: `%${Math.round((s.value / total) * 100)}`, value: fmt(s.value), color: toneVar[s.tone] }]); }} />;
        start += len;
        return el;
      })}
    </svg>
    {center && <div className="donut-center">{center}</div>}
    <TipBox tip={tip} />
  </div>;
}

/* Halka ölçer: bir oranın sınıra göre durumu; iz aynı rengin açık tonu. */
export function Ring({ ratio, tone = "signal", size = 44, thickness = 5, label }: {
  ratio: number; tone?: Tone; size?: number; thickness?: number; label: string;
}) {
  const clamped = Math.max(0, Math.min(1, ratio));
  const r = (size - thickness) / 2, c = 2 * Math.PI * r;
  return <svg viewBox={`0 0 ${size} ${size}`} width={size} height={size} role="img" aria-label={`${label}: %${Math.round(clamped * 100)}`} className="ring">
    <title>{`${label}: %${Math.round(clamped * 100)}`}</title>
    <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={toneTint[tone]} strokeWidth={thickness} />
    <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={toneVar[tone]} strokeWidth={thickness} strokeLinecap="round"
      strokeDasharray={`${clamped * c} ${c}`} transform={`rotate(-90 ${size / 2} ${size / 2})`} />
  </svg>;
}
