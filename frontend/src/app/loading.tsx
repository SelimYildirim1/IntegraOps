export default function Loading() {
  return <section className="surface message loading" role="status" aria-live="polite">
    <span className="message-icon"><span className="spinner" /></span>
    <h2>Yükleniyor</h2><p>Veriler getiriliyor.</p>
  </section>;
}
