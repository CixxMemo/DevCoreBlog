# İlerleme çizelgesi

- **Son kayıt:** 23 Eylül 2026 — F12 yazı/kategori kuralları servis, MVC ve webhook için ortaklaştırıldı.
- **Tamamlanan plan fazı:** 13/58 (F00–F12).
- **Tamamlanan uygulama kodu fazı:** 11.
- **Aktif faz:** Yok.
- **Sıradaki faz:** F13 — kullanıcı onayı bekleniyor.
- **Uygulama kodu:** F02–F09 güvenlik fazları tamamlandı. F10 migration zincirini doğruladı. F11 kapaksız kayıt ve görünür hataları düzeltti. F12 başlık/içerik/özet/kategori/HTTPS URL sınırlarını servis katmanında zorunlu kıldı; form ve webhook aynı kuralları kullanıyor.

## Kullanım

- [x] yalnızca fazın tüm kabul kanıtları varsa kullanılır.
- İZLENİYOR/DOĞRULANAMADI/ENGELLİ durumları tamamlandı değildir; [ ] kalır.
- UYGULANAMAZ seçime bağlıdır; gerekçe ve karar kaynağıyla işaretlenir, [x] yapılmaz ve o dalın geçerli faz sayısından çıkarılır.
- ERTELENDİ kullanıcı kararıyla kaydedilir; sessizce tamamlandıya çevrilmez.
- Her tamamlanan fazın kayıt bağlantısı olur. Ortam/kanıt eksikse sonraki bağımlı fazı başlatma.
- Tek seferde bir satır uygulanır. Mevcut dört projeli yapı ve entegrasyonlar korunur; eski koşullu yol kaldırıldı.

## Ana fazlar

| Bitti | Faz | Sonuç | Durum | Kanıt |
|---|---|---|---|---|
| [x] | F00 | Güncel agent kurallarını ve planı yerleştir | TAMAMLANDI — BELGE | [F00 kanıtı](../uygulama-kayitlari/F00-2026-09-21.md) |
| [x] | F01 | Tekrarlanabilir başlangıç ve güvenli doğrulama ortamı | TAMAMLANDI — DOĞRULAMA | [F01 kanıtı](../uygulama-kayitlari/F01-2026-09-21.md) |
| [x] | F02 | Eksik yönetici bilgileriyle girişi kapat | TAMAMLANDI — KOD | [F02 kanıtı](../uygulama-kayitlari/F02-2026-09-21.md) |
| [x] | F03 | Yönetici bildirimini düz metin olarak göster | TAMAMLANDI — KOD | [F03 kanıtı](../uygulama-kayitlari/F03-2026-09-21.md) |
| [x] | F04 | Güvenli Markdown ve kontrollü video üretimi | TAMAMLANDI — KOD | [F04 kanıtı](../uygulama-kayitlari/F04-2026-09-22.md) |
| [x] | F05 | Cookie POST işlemlerinde tutarlı antiforgery | TAMAMLANDI — KOD | [F05 kanıtı](../uygulama-kayitlari/F05-2026-09-22.md) |
| [x] | F06 | Giriş denemelerini sınırlandır | TAMAMLANDI — KOD | [F06 kanıtı](../uygulama-kayitlari/F06-2026-09-22.md) |
| [x] | F07 | Parola doğrulamasını hash'e geçir | TAMAMLANDI — KOD | [F07 kanıtı](../uygulama-kayitlari/F07-2026-09-22.md) |
| [x] | F08 | Cookie ve oturum yaşam döngüsünü belirle | TAMAMLANDI — KOD | [F08 kanıtı](../uygulama-kayitlari/F08-2026-09-22.md) |
| [x] | F09 | Bütün görsel yüklemelerine aynı güvenlik politikası | TAMAMLANDI — KOD | [F09 kanıtı](../uygulama-kayitlari/F09-2026-09-22.md) |
| [x] | F10 | Migration keşfini ve sürüm uyumunu düzelt | TAMAMLANDI — KOD | [F10 kanıtı](../uygulama-kayitlari/F10-2026-09-22.md) |
| [x] | F11 | Dosyasız yazı kaydını ve alan hatalarını düzelt | TAMAMLANDI — KOD | [F11 kanıtı](../uygulama-kayitlari/F11-2026-09-23.md) |
| [x] | F12 | Yazı ve kategori iş kurallarını ortak doğrula | TAMAMLANDI — KOD | [F12 kanıtı](../uygulama-kayitlari/F12-2026-09-23.md) |
| [ ] | F13 | Formların frontend doğrulama bağımlılığını onar | BAŞLAMADI | — |
| [ ] | F14 | Kategori düzenlemesinde korunan alanları sakla | BAŞLAMADI | — |
| [ ] | F15 | Kategori silme kuralını veritabanında güvenceye al | BAŞLAMADI | — |
| [ ] | F16 | Yayın zamanını açık saat dilimiyle işle | BAŞLAMADI | — |
| [ ] | F17 | Tek yayın görünürlüğü kuralını uygula | BAŞLAMADI | — |
| [ ] | F18 | Yayın kuralına uyan cache politikası | BAŞLAMADI | — |
| [ ] | F19 | Düzenlemede kalıcı slug'ı koru | BAŞLAMADI | — |
| [ ] | F20 | Slug benzersizliği ve çakışma göçü | BAŞLAMADI | — |
| [ ] | F21 | Okunma sayacını atomik artır | BAŞLAMADI | — |
| [ ] | F22 | Eşzamanlı editlerde veri kaybını önle | BAŞLAMADI | — |
| [ ] | F23 | Hata sayfaları ve HTTP durumlarını düzelt | BAŞLAMADI | — |
| [ ] | F24 | Repository bağımlılıklarını sözleşmeye taşı | BAŞLAMADI | — |
| [ ] | F25 | Controller ve Razor'dan DbContext'i çıkar | BAŞLAMADI | — |
| [ ] | F26 | Renderer ve Cloudinary bağlantısını DI sınırına al | BAŞLAMADI | — |
| [ ] | F27 | Webhook kabul sözleşmesini sadeleştir | BAŞLAMADI | — |
| [ ] | F28 | Webhook tekrarlarını tek kayda indir | BAŞLAMADI | — |
| [ ] | F29 | Portföy beslemesini sınırlı DB sorgusuyla üret | BAŞLAMADI | — |
| [ ] | F30 | Yazı editörünün ortak kodunu tek yerde topla | BAŞLAMADI | — |
| [ ] | F31 | Başarısız kayıtta yazının kaybolmasını önle | BAŞLAMADI | — |
| [ ] | F32 | Taslak, zamanlama ve yayınlama eylemlerini anlaşılır yap | BAŞLAMADI | — |
| [ ] | F33 | Sunucu çıktısıyla tutarlı güvenli önizleme ekle | BAŞLAMADI | — |
| [ ] | F34 | Yönetim panelini telefonda kullanılabilir yap | BAŞLAMADI | — |
| [ ] | F35 | Frontend varlıklarını tekrarlanabilir derlemeye geçir | BAŞLAMADI | — |
| [ ] | F36 | Ortak görsel düzeni ve okunabilirliği toparla | BAŞLAMADI | — |
| [ ] | F37 | Okuyucu gezinmesi ve aramayı erişilebilir yap | BAŞLAMADI | — |
| [ ] | F38 | Ana sayfada içerik keşfini gerçek veriye bağla | BAŞLAMADI | — |
| [ ] | F39 | Yazı okuma deneyimini tamamla | BAŞLAMADI | — |
| [ ] | F40 | Okuyucu aramasını ve sayfalamayı sınırlandır | BAŞLAMADI | — |
| [ ] | F41 | Yönetici yazı listesini sunucuda sayfala | BAŞLAMADI | — |
| [ ] | F42 | Arayüz dili ve tarih biçimini tutarlı yap | BAŞLAMADI | — |
| [ ] | F43 | Kalıcı ve kanonik URL sözleşmesini uygula | BAŞLAMADI | — |
| [ ] | F44 | Sayfa meta verilerini ve makale şemasını ekle | BAŞLAMADI | — |
| [ ] | F45 | Sitemap ve robots çıktısını düzelt | BAŞLAMADI | — |
| [ ] | F46 | Standart RSS ile takip edilebilirlik sağla | BAŞLAMADI | — |
| [ ] | F47 | Gerçek site kimliğini ve temel bilgi sayfalarını tamamla | BAŞLAMADI | — |
| [ ] | F48 | Ölçülmüş sorgu ve sayfa performansını iyileştir | BAŞLAMADI | — |
| [ ] | F49 | Medya kayıtlarını bakım yapılabilir hale getir | BAŞLAMADI | — |
| [ ] | F50 | Dağıtım sınırlarını ve reverse proxy güvenini tanımla | BAŞLAMADI | — |
| [ ] | F51 | CSP uyumluluğunu raporlama modunda hazırla | BAŞLAMADI | — |
| [ ] | F52 | Doğrulanmış CSP ve güvenlik başlıklarını uygula | BAŞLAMADI | — |
| [ ] | F53 | Gerçek hata ve servis durumlarını görünür yap | BAŞLAMADI | — |
| [ ] | F54 | Git deposundaki üretilmiş dosyaları temizle | BAŞLAMADI | — |
| [ ] | F55 | Otomatik kalite kapısını kur | BAŞLAMADI | — |
| [ ] | F56 | Yedek geri yükleme ve yayınlama prosedürünü kanıtla | BAŞLAMADI | — |
| [ ] | F57 | Uçtan uca kabul ve yeni SOLID değerlendirmesi | BAŞLAMADI | — |

## Son agent teslimi

- Tamamlanan faz: F12 yazı ve kategori iş kurallarını ortak doğrulama.
- Değişen davranış: Başlık/içerik/ad boşluğu, metin uzunlukları, aktif kategori ve mutlak HTTPS kapak kuralı artık servis katmanında zorunlu. MVC dar input modelleri kullanıyor; webhook aynı servis sonucunu alan bazlı 400 cevabına çeviriyor. Summary/Excerpt boşlukları boş string'e normalleştiriliyor.
- Çalıştırılan kontroller ve sonuç: Build 0 uyarı/0 hatayla geçti. İzole PostgreSQL'de doğrudan servis bypass denemeleri reddedildi; aynı bozuk özet MVC ve webhook'ta reddedildi; over-posting DB'de etkisiz kaldı ve F02–F11 regresyonları geçti. Masaüstü ve 390 px tarayıcı kontrolünde sınırlar, aktif kategori listesi ve taşma doğrulandı.
- Doğrulanamayan/engel: Gerçek production verisi incelenmedi. Sentetik fixture'da bir uzun özet ve pasif kategori bağlantısı raporlandı, değiştirilmedi. Mevcut jQuery konsol hatası F13, sabit admin sidebar darlığı F34 kapsamındadır.
- Sıradaki tek faz: F13.
- Kullanıcıdan gereken: F13'ü başlatmak için açık onay.

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [Hazır mesajlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/AGENT_PROMPTLARI.md)
