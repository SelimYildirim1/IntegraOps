"use client";
export default function ErrorPage({ reset }: { reset: () => void }) {
  return <section className="surface message" role="alert"><h1>Görünüm yüklenemedi</h1>
    <p>Beklenmeyen bir sorun oluştu. Sayfayı yeniden yüklemeyi deneyin; sorun sürerse Sistem sayfasından servis durumuna bakın.</p>
    <button className="button" onClick={reset}>Tekrar dene</button>
  </section>;
}
