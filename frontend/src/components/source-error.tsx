import { explainError } from "@/lib/errors";

/**
 * A source system's error, explained for the operator first and quoted verbatim second.
 * `compact` fits a table cell: the plain title, with the raw text on hover.
 */
export default function SourceError({ error, stepName, compact = false }: { error: string | null | undefined; stepName?: string | null; compact?: boolean }) {
  if (!error) return null;
  const x = explainError(error);
  if (compact) {
    return x
      ? <span className="badge bad" title={error}>{x.title}</span>
      : <span className="error-content" style={{ display: "inline-block", padding: "4px 8px" }}>{error}</span>;
  }
  if (!x) return <div><p className="help" style={{ marginBottom: 6 }}>Kaynak sistemin bildirdiği hata</p><pre className="error-content">{error}</pre></div>;
  return <div className="callout bad source-error">
    <strong>{stepName ? `${stepName} adımında: ` : ""}{x.title}</strong>
    <p style={{ marginTop: 4 }}>{x.detail}</p>
    <p className="help" style={{ marginTop: 4 }}>{x.who}</p>
    <details style={{ marginTop: 6 }}>
      <summary className="help">Teknik ayrıntı (kaynak sistemin ham metni)</summary>
      <pre className="error-content" style={{ marginTop: 6 }}>{error}</pre>
    </details>
  </div>;
}
