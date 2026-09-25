"use client";
import Icon from "@/components/icons";
export default function ErrorPage({ reset }: { reset: () => void }) {
  return <section className="surface message" role="alert">
    <span className="message-icon"><Icon name="alert" size={22} /></span>
    <h1>Görünüm yüklenemedi</h1>
    <p>Beklenmeyen bir sorun oluştu. Sayfayı yeniden yüklemeyi deneyin; sorun sürerse Sistem sayfasından servis durumuna bakın.</p>
    <button className="button" onClick={reset}><Icon name="refresh" />Tekrar dene</button>
  </section>;
}
