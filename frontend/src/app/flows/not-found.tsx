import { Message } from "@/components/ui";
export default function FlowNotFound() {
  return <Message title="Akış bulunamadı" description="Bu akış mevcut projede bulunmuyor ya da bağlantı geçerli değil." href="/flows" action="Akışlara dön" />;
}
