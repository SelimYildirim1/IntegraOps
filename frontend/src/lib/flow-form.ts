export type CreateFlowInput = {
  code: string;
  name: string;
  steps: { code: string; name: string; maxDelaySeconds: number | null; instructions?: string; link?: string }[];
};

export type FlowFormState = {
  message: string;
  errors: Record<string, string>;
  createdCode?: string;
};

export const initialFlowState: FlowFormState = { message: "", errors: {} };

export function validateFlow(value: unknown):
  | { ok: true; data: CreateFlowInput }
  | { ok: false; errors: Record<string, string> } {
  const errors: Record<string, string> = {};
  const record = (input: unknown): Record<string, unknown> =>
    input !== null && typeof input === "object" && !Array.isArray(input)
      ? input as Record<string, unknown> : {};
  const text = (input: unknown) => typeof input === "string" ? input.trim() : "";
  const readCode = (input: unknown, field: string) => {
    const code = text(input);
    if (!/^[a-z][a-z0-9-]{0,63}$/.test(code)) {
      errors[field] = "Küçük İngilizce harfle başlayın; harf, rakam ve tire kullanın. En fazla 64 karakter.";
    }
    return code;
  };
  const readName = (input: unknown, field: string) => {
    const name = text(input);
    if (!name || name.length > 160) errors[field] = "1–160 karakterlik bir ad yazın.";
    return name;
  };
  const raw = record(value);
  const code = readCode(raw.code, "flow-code");
  const name = readName(raw.name, "flow-name");
  if (!Array.isArray(raw.steps) || raw.steps.length < 2 || raw.steps.length > 30) {
    errors.steps = "Akış 2–30 sıralı adımdan oluşmalı.";
    return { ok: false, errors };
  }
  const codes = new Set<string>();
  const steps = raw.steps.map((value, index) => {
    const step = record(value);
    const code = readCode(step.code, `step-${index}-code`);
    const name = readName(step.name, `step-${index}-name`);
    if (codes.has(code)) errors[`step-${index}-code`] = "Adım kodları birbirinden farklı olmalı.";
    codes.add(code);

    let maxDelaySeconds: number | null = null;
    if (index > 0) {
      const amount = text(step.delayValue);
      const multiplier = step.delayUnit === "seconds" ? 1
        : step.delayUnit === "minutes" ? 60 : step.delayUnit === "hours" ? 3600 : step.delayUnit === "days" ? 86400 : 0;
      const seconds = Number(amount) * multiplier;
      if (!/^\d+$/.test(amount) || !multiplier || !Number.isSafeInteger(seconds)
        || seconds < 1 || seconds > 2592000) {
        errors[`step-${index}-delay`] = "Süreyi tam sayı olarak girin. İzin verilen aralık: 1 saniye–30 gün.";
      } else maxDelaySeconds = seconds;
    }
    const instructions = text(step.instructions);
    if (instructions.length > 2000) errors[`step-${index}-instructions`] = "Talimat en fazla 2000 karakter olabilir.";
    const link = text(step.link);
    if (link && (link.length > 1024 || !/^https?:\/\/[^\s{}]+(\{(transactionId|flow|step)\}[^\s{}]*)*$/.test(link)))
      errors[`step-${index}-link`] = "https:// ile başlayan tam adres yazın; yalnızca {transactionId}, {flow} ve {step} yer tutucuları kullanılabilir.";
    return { code, name, maxDelaySeconds, instructions, link };
  });
  return Object.keys(errors).length ? { ok: false, errors } : { ok: true, data: { code, name, steps } };
}
