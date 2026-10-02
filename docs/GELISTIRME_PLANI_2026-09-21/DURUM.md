# İlerleme çizelgesi

- **Son kayıt:** 2 Ekim 2026 — F38 ilk sayfa vitrini, global Most Read ve boş DB kabulü doğrulandı.
- **Tamamlanan plan fazı:** 39/58 (F00–F38).
- **Tamamlanan uygulama kodu fazı:** 37.
- **Aktif faz:** Yok.
- **Sıradaki faz:** F39 — kullanıcı onayı bekleniyor.
- **Uygulama kodu:** F02–F09 güvenlik fazları tamamlandı. F10 migration zincirini doğruladı. F11–F13 form/içerik güvenliğini tamamladı. F14 kategori update'ini güvenli alanlara daralttı. F15 kategori silmesini DB constraint'i ve yarış yönetimiyle veri kaybına karşı korudu. F16 yayın zamanını açık saat dilimiyle UTC'ye bağladı. F17 public görünürlüğü tek kurala bağladı. F18 tek süreçli liste cache'ini yayın sınırı ve mutation invalidation'ıyla doğruladı. F19 düzenlemede kayıtlı slug'ı korudu. F20 benzersiz slug indekslerini ve geçmiş çakışma göçünü ekledi. F21 yalnızca uygun public GET'leri atomik sayıyor. F22 eski edit sekmelerinin içerik ezmesini sürüm karşılaştırmasıyla önlüyor. F23 hata akışında gerçek 404/500 ve JSON durumlarını koruyor. F24 servislerin somut Data repository bağımlılığını Core sözleşmelerine taşıdı. F25 dashboard sorgularını servis/repository sınırına ve kategori menülerini asenkron bileşene taşıdı. F26 güvenli Markdown renderer'ını Services'e taşıyıp Cloudinary istemcisini composition root'tan enjekte etti. F27 webhook'u secret-önce, sınırlı JSON kabulüne ve açık yayın yetkisine bağladı. F28 anahtarlı gönderimleri kalıcı işlem kaydı ve tek transaction ile tekrar korumasına bağladı. F29 portföyü dar DB projection, kararlı sıralama ve yapılandırılmış site URL'sine bağladı. F30 Create/Edit ortak formunu ve editör davranışlarını tek partial/modülde topladı. F31 Create/Edit kurtarmayı yazı kimliği, açık Restore/Discard, sekme koordinasyonu ve doğrulanmış başarılı kayıt bilgisine bağladı. F32 açık kayıt eylemlerini, normal Save korumasını ve doğru yayın durumunu uyguladı. F33 aynı güvenli render sınırından, DB yazısı/sayaç/yayın yan etkisi olmayan özel HTML önizleme üretiyor. F34 yönetim panelini mobil drawer, kontrollü tablo kaydırma ve dar ekranda kullanılabilir form/editörle düzenledi; gerçek %200 Chrome yakınlaştırmasında yatay taşma olmadığını doğruladı. F35 Tailwind CLI/lockfile ile yerel CSS ve sabit lisanslı editör/Prism varlıklarını build/publish akışına bağladı.

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
| [x] | F13 | Formların frontend doğrulama bağımlılığını onar | TAMAMLANDI — KOD | [F13 kanıtı](../uygulama-kayitlari/F13-2026-09-23.md) |
| [x] | F14 | Kategori düzenlemesinde korunan alanları sakla | TAMAMLANDI — KOD | [F14 kanıtı](../uygulama-kayitlari/F14-2026-09-23.md) |
| [x] | F15 | Kategori silme kuralını veritabanında güvenceye al | TAMAMLANDI — KOD | [F15 kanıtı](../uygulama-kayitlari/F15-2026-09-23.md) |
| [x] | F16 | Yayın zamanını açık saat dilimiyle işle | TAMAMLANDI — KOD | [F16 kanıtı](../uygulama-kayitlari/F16-2026-09-27.md) |
| [x] | F17 | Tek yayın görünürlüğü kuralını uygula | TAMAMLANDI — KOD | [F17 kanıtı](../uygulama-kayitlari/F17-2026-09-27.md) |
| [x] | F18 | Yayın kuralına uyan cache politikası | TAMAMLANDI — KOD | [F18 kanıtı](../uygulama-kayitlari/F18-2026-09-27.md) |
| [x] | F19 | Düzenlemede kalıcı slug'ı koru | TAMAMLANDI — KOD | [F19 kanıtı](../uygulama-kayitlari/F19-2026-09-27.md) |
| [x] | F20 | Slug benzersizliği ve çakışma göçü | TAMAMLANDI — KOD | [F20 kanıtı](../uygulama-kayitlari/F20-2026-09-27.md) |
| [x] | F21 | Okunma sayacını atomik artır | TAMAMLANDI — KOD | [F21 kanıtı](../uygulama-kayitlari/F21-2026-09-27.md) |
| [x] | F22 | Eşzamanlı editlerde veri kaybını önle | TAMAMLANDI — KOD | [F22 kanıtı](../uygulama-kayitlari/F22-2026-09-28.md) |
| [x] | F23 | Hata sayfaları ve HTTP durumlarını düzelt | TAMAMLANDI — KOD | [F23 kanıtı](../uygulama-kayitlari/F23-2026-09-28.md) |
| [x] | F24 | Repository bağımlılıklarını sözleşmeye taşı | TAMAMLANDI — KOD | [F24 kanıtı](../uygulama-kayitlari/F24-2026-09-28.md) |
| [x] | F25 | Controller ve Razor'dan DbContext'i çıkar | TAMAMLANDI — KOD | [F25 kanıtı](../uygulama-kayitlari/F25-2026-09-29.md) |
| [x] | F26 | Renderer ve Cloudinary bağlantısını DI sınırına al | TAMAMLANDI — KOD | [F26 kanıtı](../uygulama-kayitlari/F26-2026-09-29.md) |
| [x] | F27 | Webhook kabul sözleşmesini sadeleştir | TAMAMLANDI — KOD | [F27 kanıtı](../uygulama-kayitlari/F27-2026-09-30.md) |
| [x] | F28 | Webhook tekrarlarını tek kayda indir | TAMAMLANDI — KOD | [F28 kanıtı](../uygulama-kayitlari/F28-2026-09-30.md) |
| [x] | F29 | Portföy beslemesini sınırlı DB sorgusuyla üret | TAMAMLANDI — KOD | [F29 kanıtı](../uygulama-kayitlari/F29-2026-09-30.md) |
| [x] | F30 | Yazı editörünün ortak kodunu tek yerde topla | TAMAMLANDI | [Kayıt](../uygulama-kayitlari/F30-2026-09-30.md) |
| [x] | F31 | Başarısız kayıtta yazının kaybolmasını önle | TAMAMLANDI — KOD | [Kayıt](../uygulama-kayitlari/F31-2026-09-30.md) |
| [x] | F32 | Taslak, zamanlama ve yayınlama eylemlerini anlaşılır yap | TAMAMLANDI — KOD | [Kayıt](../uygulama-kayitlari/F32-2026-09-30.md) |
| [x] | F33 | Sunucu çıktısıyla tutarlı güvenli önizleme ekle | TAMAMLANDI — KOD | [Kayıt](../uygulama-kayitlari/F33-2026-10-01.md) |
| [x] | F34 | Yönetim panelini telefonda kullanılabilir yap | TAMAMLANDI — KOD | [Kayıt](../uygulama-kayitlari/F34-2026-10-01.md) |
| [x] | F35 | Frontend varlıklarını tekrarlanabilir derlemeye geçir | TAMAMLANDI — KOD | [F35 kanıtı](../uygulama-kayitlari/F35-2026-10-01.md) |
| [x] | F36 | Ortak görsel düzeni ve okunabilirliği toparla | TAMAMLANDI — KOD | [F36 kanıtı](../uygulama-kayitlari/F36-2026-10-01.md) |
| [x] | F37 | Okuyucu gezinmesi ve aramayı erişilebilir yap | TAMAMLANDI — KOD | [F37 kanıtı](../uygulama-kayitlari/F37-2026-10-01.md) |
| [x] | F38 | Ana sayfada içerik keşfini gerçek veriye bağla | TAMAMLANDI — KOD | [F38 kanıtı](../uygulama-kayitlari/F38-2026-10-02.md) |
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

- Bu turdaki faz: F38 — ilk sayfayla sınırlı ve tekrar etmeyen vitrin; bütün görünür yazılardan DB'de seçilen beş Most Read; gerçek aktif Topics/boş durum ve sonuç/sayfa sayıları.
- Kontroller: Build 0 uyarı/0 hata; F38 çok kayıt 15, küçük/tek liste 4+4 ve fiziksel boş DB 6 kontrol geçti. F17/F12/F29 50, F35 30 asset ve F33 30 preview kontrolü geçti. Gerçek IAB masaüstü/mobil, açık/koyu tema, klavye Next/menü/Escape ve odak dönüşü doğrulandı; son ölçümlerde taşma/console hatası yok. [Kanıt](../uygulama-kayitlari/F38-2026-10-02.md).
- Sınırlama: Tüm cihazlar/gerçek ekran okuyucu/production yükü doğrulanmadı. Doğrudan test SQL değişimleri cache invalidation çağırmadığından kabul taze okuma ile yapıldı; F18 üretim cache sınırı korundu. Arama sınırları/sayfalama F40, tarih biçimi F42, ölçülmüş performans F48'de. Geçici ortam kapatıldı; bin/obj kaynak diff'ine eklenmedi.
- Aktif faz: Yok. Sıradaki tek faz: F39 — Yazı okuma deneyimini tamamla.
- Kullanıcıdan gereken: F39 için açık onay.

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [Hazır mesajlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/AGENT_PROMPTLARI.md)
