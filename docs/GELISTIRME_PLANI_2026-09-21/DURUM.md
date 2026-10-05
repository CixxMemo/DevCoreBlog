# İlerleme çizelgesi

- **Son kayıt:** 5 Ekim 2026 — E01 ve F57 yeniden kabulü tamamlandı; güncel GitHub Actions kabulü 25/25 geçti.
- **Tamamlanan plan fazı:** 58/58 (F00–F57).
- **Tamamlanan uygulama kodu fazı:** 52.
- **Aktif faz:** Yok.
- **Sıradaki faz:** Yok; ana plan tamamlandı. Yeni iş veya canlı kurulum için kullanıcıdan ayrı onay beklenir.
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
| [x] | F39 | Yazı okuma deneyimini tamamla | TAMAMLANDI — KOD | [F39 kanıtı](../uygulama-kayitlari/F39-2026-10-02.md) |
| [x] | F40 | Okuyucu aramasını ve sayfalamayı sınırlandır | TAMAMLANDI — KOD | [F40 kanıtı](../uygulama-kayitlari/F40-2026-10-02.md) |
| [x] | F41 | Yönetici yazı listesini sunucuda sayfala | TAMAMLANDI — KOD | [F41 kanıtı](../uygulama-kayitlari/F41-2026-10-03.md) |
| [x] | F42 | Arayüz dili ve tarih biçimini tutarlı yap | TAMAMLANDI — KOD | [F42 kanıtı](../uygulama-kayitlari/F42-2026-10-03.md) |
| [x] | F43 | Kalıcı ve kanonik URL sözleşmesini uygula | TAMAMLANDI — KOD | [F43 kanıtı](../uygulama-kayitlari/F43-2026-10-03.md) |
| [x] | F44 | Sayfa meta verilerini ve makale şemasını ekle | TAMAMLANDI — KOD | [F44 kanıtı](../uygulama-kayitlari/F44-2026-10-03.md) |
| [x] | F45 | Sitemap ve robots çıktısını düzelt | TAMAMLANDI — KOD | [F45 kanıtı](../uygulama-kayitlari/F45-2026-10-03.md) |
| [x] | F46 | Standart RSS ile takip edilebilirlik sağla | TAMAMLANDI — KOD | [F46 kanıtı](../uygulama-kayitlari/F46-2026-10-03.md) |
| [x] | F47 | Gerçek site kimliğini ve temel bilgi sayfalarını tamamla | TAMAMLANDI — KOD | [F47 kanıtı](../uygulama-kayitlari/F47-2026-10-03.md) |
| [x] | F48 | Ölçülmüş sorgu ve sayfa performansını iyileştir | TAMAMLANDI — KOD | [F48 kanıtı](../uygulama-kayitlari/F48-2026-10-04.md) |
| [x] | F49 | Medya kayıtlarını bakım yapılabilir hale getir | TAMAMLANDI — KOD | [F49 kanıtı](../uygulama-kayitlari/F49-2026-10-04.md) |
| [x] | F50 | Dağıtım sınırlarını ve reverse proxy güvenini tanımla | TAMAMLANDI — KOD | [F50 kanıtı](../uygulama-kayitlari/F50-2026-10-04.md) |
| [x] | F51 | CSP uyumluluğunu raporlama modunda hazırla | TAMAMLANDI — KOD | [F51 kanıtı](../uygulama-kayitlari/F51-2026-10-04.md) |
| [x] | F52 | Doğrulanmış CSP ve güvenlik başlıklarını uygula | TAMAMLANDI — KOD | [F52 kanıtı](../uygulama-kayitlari/F52-2026-10-04.md) |
| [x] | F53 | Gerçek hata ve servis durumlarını görünür yap | TAMAMLANDI — KOD | [F53 kanıtı](../uygulama-kayitlari/F53-2026-10-05.md) |
| [x] | F54 | Git deposundaki üretilmiş dosyaları temizle | TAMAMLANDI — DEPO BAKIMI | [F54 kanıtı](../uygulama-kayitlari/F54-2026-10-05.md) |
| [x] | F55 | Otomatik kalite kapısını kur | TAMAMLANDI — DOĞRULAMA/OTOMASYON | [F55 kaydı](../uygulama-kayitlari/F55-2026-10-05.md) |
| [x] | F56 | Yedek geri yükleme ve yayınlama prosedürünü kanıtla | TAMAMLANDI — DOĞRULAMA/İŞLETİM BELGESİ | [F56 kanıtı](../uygulama-kayitlari/F56-2026-10-05.md) |
| [x] | F57 | Uçtan uca kabul ve yeni SOLID değerlendirmesi | TAMAMLANDI — KABUL/DEĞERLENDİRME; güncel uzak CI 25/25 | [İlk kayıt](../uygulama-kayitlari/F57-2026-10-05.md), [yeniden kabul](../uygulama-kayitlari/F57-CI-2026-10-05.md), [E01](../uygulama-kayitlari/E01-2026-10-05.md) |

## Onaylı ek işler

Ana planın 58 fazlık sayacı değişmez; ek bakım işleri ayrı izlenir.

| Bitti | İş | Durum | Kanıt |
|---|---|---|---|
| [x] | [E01 — Vendored editör güvenlik bakımını tamamla](E01_EDITOR_GUVENLIK.md) | TAMAMLANDI — BUILD/RUNTIME VENDOR BAKIMI | [E01 kaydı](../uygulama-kayitlari/E01-2026-10-05.md) |

## Son agent teslimi

- Bu turdaki iş: Kullanıcı onaylı commit/push ve F57 yeniden kabulü. E01 değişiklikleri korundu; önceki uzak CI’da görülen webhook ilk/tekrar tarih hassasiyeti farkı F28 persistence sınırında düzeltildi. [Yeniden kabul](../uygulama-kayitlari/F57-CI-2026-10-05.md), [son kabul raporu](../SON_KABUL_RAPORU_2026-10-05.md).
- Doğrulama: yeni root build0 hata/0 uyarı, deterministik kırmızı→yeşil regresyon, temiz yerel kapı25/25 ve [GitHub Actions25/25](https://github.com/CixxMemo/DevCoreBlog/actions/runs/37292561215); kaynak commit’i `2a7a5f1f916cc88e86bec4f9f94e05399d14da45`. E01 Chrome journey25/25, CSP36/36, güvenlik9/9 ve integrity5/5 kanıtları ayrı korunur.
- Kalan sınır: arşivli Editor bakım sorumluluğu ve D28 dar braces risk kabulü sürer; canlı VPS/Nginx/Cloudinary/yedek/keyring ve gerçek CWV doğrulanmış değildir. F57 tamamlanması canlıya hazır veya dağıtılmış sonucu değildir. SOLID82/100 kaynak inceleme sonucudur; otomatik sertifika veya yeni puan değildir.
- Aktif/sıradaki faz: Yok. Ana plan58/58, uygulama kodu fazı52; ek E01 ayrı tamamlandı. F28 düzeltmesi yeni numaralı faz eklemez.
- Commit/push kullanıcı onayıyla yapıldı; canlı kurulum/restore/deploy veya sonraki iş başlatılmadı. Yeni iş için kullanıcı onayı beklenir.

[Ana plan](README.md) · [Hazır mesajlar](AGENT_PROMPTLARI.md)
