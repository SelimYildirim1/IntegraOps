// Süre metinleri ve son zaman ölçeri için yardımcılar. Sunucu saatine göre hesaplanır.

export function spanText(seconds: number) {
  const s = Math.max(0, Math.round(seconds));
  if (s < 60) return `${s} sn`;
  const m = Math.floor(s / 60);
  if (m < 60) return s % 60 && m < 10 ? `${m} dk ${s % 60} sn` : `${m} dk`;
  const h = Math.floor(m / 60);
  if (h < 24) return m % 60 ? `${h} sa ${m % 60} dk` : `${h} sa`;
  const d = Math.floor(h / 24);
  return h % 24 ? `${d} gün ${h % 24} sa` : `${d} gün`;
}

export type Deadline = {
  ratio: number;       // 0–1 arasında dolan pay; geçmişse 1
  late: boolean;       // son zaman geçti mi
  text: string;        // "12 dk kaldı" ya da "3 dk geçti"
  allowance: string;   // tanınan toplam süre
};

export function deadline(startedAt: string | null, deadlineAt: string | null, now = Date.now()): Deadline | null {
  if (!startedAt || !deadlineAt) return null;
  const start = Date.parse(startedAt), end = Date.parse(deadlineAt);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) return null;
  const total = (end - start) / 1000;
  const remaining = (end - now) / 1000;
  const late = remaining < 0;
  return {
    ratio: late ? 1 : Math.min(1, Math.max(0, (now - start) / (end - start))),
    late,
    text: late ? `${spanText(-remaining)} geçti` : `${spanText(remaining)} kaldı`,
    allowance: spanText(total),
  };
}

export function elapsedBetween(from: string | null, to: string | null) {
  if (!from || !to) return null;
  const a = Date.parse(from), b = Date.parse(to);
  if (!Number.isFinite(a) || !Number.isFinite(b)) return null;
  return (b - a) / 1000;
}

// İş olaylarının zaman farkını gösterir; ağdaki teslim sırasını ölçmez.
export function stepTiming(seconds: number, allowance: number): { tone: string; text: string } {
  if (seconds < 0) {
    const difference = -seconds < 1 ? "1 saniyeden az" : spanText(-seconds);
    return {
      tone: "late",
      text: `Zaman sırası tutarsız: bu adımın olay zamanı önceki adımdan ${difference} önce. Kaynak olay zamanlarını kontrol edin.`,
    };
  }
  return {
    tone: seconds > allowance ? "late" : "ok",
    text: `${spanText(seconds)} sonra gerçekleşti, ${spanText(allowance)} tanınmıştı`,
  };
}
