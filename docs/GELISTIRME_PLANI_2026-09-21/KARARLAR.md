# Güncel mimari ve çalışma kararları

**Güncelleme:** 23 Eylül 2026 — F12 içerik sınırları.

Kullanıcı eski kuralların yenilenmesini, SOLID/temiz kod/güvenlik sınırlarının güçlendirilmesini, eski planların kaldırılabilmesini ve yeni geliştirme planının buna uyarlanmasını açıkça istedi. Aşağıdaki teknik seçimler bu yetki kapsamında mevcut ürün yapısını koruyarak yapıldı. Kullanıcının ayrıca eski A/B seçeneklerinden birini seçtiği iddia edilmiyor; o karar ağacı yeni kurallarla kaldırıldı.

Tek ana kaynak [repository kökündeki AGENTS.md](../../AGENTS.md). [.agents/AGENTS.md](../../.agents/AGENTS.md) yalnızca buraya yönlendirir. Arşiv talimatları yürürlükte değildir.

## D01 — Mevcut dört projeli modüler monolit korunur

Web, Core, Data ve Services mevcut mimaridir. Eski tek proje zorunluluğu kaldırıldı; birleştirme kullanıcıya görünür bir kazanım sağlamadan taşıma riski yaratıyordu. SOLID proje sayısıyla değil sorumluluk ve bağımlılık yönüyle değerlendirilir.

Hedef: Data → Core, Services → Core; Web composition root implementasyonları bağlar. Mevcut Services→Data bağımlılığı F24, Core→Markdig bağımlılığı F26 ile giderilecek. Bu belge değişikliği ilgili referansları henüz değiştirmedi.

**Durum:** KARAR VERİLDİ. Eski tek proje geçiş kartları aktif plandan çıkarıldı.

## D02 — MVC ile mevcut entegrasyonlar desteklenir

Blog MVC/Razor olarak kalır. Mevcut webhook ve portföy feed'i korunacak ve F27–F29 ile güçlendirilecek. Artık geçici API istisnası beklenmez; bunlar desteklenen mimari sınırın parçasıdır.

Yeni SPA/genel API projesi/kimlik sistemi kendiliğinden eklenmez. Yeni dış HTTP yüzeyi yalnızca açık görev ihtiyacıyla, küçük doğrulanmış sözleşmeyle ele alınır. Webhook cookie'den ayrı doğrulanır; body/input sınırları, rate limit ve kalıcı idempotency hedeflenir.

**Durum:** KARAR VERİLDİ. Önceki API yasağına bağlı koşullu geliştirme kaldırıldı.

## D03 — Talimat hiyerarşisi ve eski planlar

Sistem/çalışma ortamı talimatları ve kullanıcının açık isteği önceliklidir. Repository içinde kök AGENTS.md ana kaynaktır; aktif plan buna uyar. .agents içinde ikinci kural kopyası tutulmaz.

Yedi eski geliştirme planı ve önceki ana kural metni [tarihsel arşive](../arsiv/agent-talimatlari-2026-09-21/README.md) taşındı. Kopyalar metni korur ancak aktif talimat değildir. Eski tek proje, constructor'da doğrudan environment okuma, terminal/neon tasarım, DTO yasağı veya CDN zorunluluğu yeni işe yanlışlıkla uygulanmaz.

Korunan tercihler: Tech Minimal, English UI ve anlamlı English kod yorumları; kullanıcı içeriklerinin kendi dili ve kullanıcıya Türkçe raporlama. Tek atomik faz, uygun build/test ve kanıtlı ilerleme güçlendirildi.

**Durum:** UYGULANDI — F00.

## D04 — Bağımlılık ve Tailwind build aracı

Mevcut Tailwind sürümüne uygun en küçük resmi build aracı F35'te doğrulanacak. Paket/API varlığı, uyum, lisans ve bakım maliyeti araştırılmadan bağımlılık eklenmez. Gerekli araç version/lock ile sabitlenir; Play CDN ve latest üretim hedefi değildir.

Küçük gerekçeli test/build bağımlılığı yasak değil. Olmayan aracı var saymak, gereksiz framework veya bütün yığını değiştirmek yasak. Rutin teknik işte yeniden kullanıcı izni istenmez; gerçek kapsam/ücretli servis/ürün değişimi ayrıca somutlaştırılır.

**Durum:** POLİTİKA BELİRLENDİ; somut sürüm F35'te doğrulanacak.

## D05 — RSS ve HTML önizleme MVC çıktılarıdır

F33 yetkili HTML önizleme, F46 standart XML RSS üretir. Ayrı API mimarisi veya SPA gerekmez. Auth/no-store/yayın görünürlüğü/encoding kuralları çıktı formatından bağımsız uygulanır.

**Durum:** KAPSAMDA. Kullanıcı RSS'i açıkça ertelerse gerekçesiyle kaydedilir; tamamlandı sayılmaz.

## D06 — Küçük tipli sözleşmeler izinli, kontrolsüz aktarım yasak

Eski mutlak DTO yasağı kaldırıldı. Form input DTO'su veya typed ViewModel izinli alanları ve validation'ı görünür kıldığı sürece uygundur. Domain entity'sini dış girdiye topluca açmak veya küçük modeli sırf yasak uğruna uzun JsonElement ayrıştırmasıyla değiştirmek doğru değildir.

- F12: Ortak domain/service iş kuralı, gerekirse küçük form input modeli.
- F25: Dashboard/navigation için küçük typed model; DbContext view/controller'a sızmaz.
- F27: Mevcut WebhookPostPayload korunur/daraltılır; validation ve açık alan eşleme yapılır. DTO silme talimatı kaldırıldı.
- F29/F41: Public output veya sayfalı ekran için küçük sınırlı model/projection; entity navigation grafiği dışarı açılmaz.
- F18: Gerekiyorsa immutable cache snapshot; paylaşılan mutable tracked entity grafiği yasak.

Her entity için DTO ailesi, ayrı transfer projesi, gereksiz mapper zinciri ve AutoMapper eklenmez. DTO varlığı tek başına güvenlik sağlamaz; server authorization ve ortak iş kuralları zorunludur.

**Durum:** KARAR VERİLDİ; ilgili fazlar güncellendi.

## D07 — Yazı ve kategori giriş sınırları

F12 ile bütün yazma yollarında aşağıdaki sunucu tarafı iş kuralları geçerlidir:

| Alan | Kural |
|---|---|
| Yazı başlığı | Boş/yalnızca boşluk olamaz; en çok 200 karakter |
| Yazı içeriği | Boş/yalnızca boşluk olamaz; en çok 200.000 karakter |
| Özet | İsteğe bağlı; en çok 500 karakter; boşluk değeri boş string'e çevrilir |
| Kısa alıntı | İsteğe bağlı; en çok 1.000 karakter; boşluk değeri boş string'e çevrilir |
| Kapak URL'si | İsteğe bağlı; en çok 2.048 karakter; doluysa host içeren mutlak HTTPS URL |
| Yazı kategorisi | Var olan ve aktif kategori |
| Kategori adı | Boş/yalnızca boşluk olamaz; en çok 100 karakter |

Bu sınırlar Core'daki ortak kurallardan servis, MVC ve webhook'a uygulanır. Form annotation'ları yalnızca erken kullanıcı geri bildirimi sağlar; servis doğrulamasının yerini almaz. Slug, CreatedDate, ViewCount ve kapak URL'si form binding'inden alınmaz. Mevcut kayıtlar otomatik kesilmez veya dönüştürülmez.

İzole F01 veri envanterinde 500 karakteri aşan bir sentetik özet ve pasif kategoriye bağlı bir sentetik yazı bulundu. Kayıtlar korunur; yeniden yazılmak istenirse güncel kuralları sağlamaları gerekir. Gerçek production verisi F12 sırasında okunmadı veya değiştirilmedi.

**Durum:** UYGULANDI — F12.

## Gerektiğinde alınacak gerçek ürün/ortam bilgileri

Bunlar şimdi topluca sorulmaz. Mevcut kaynaktan doğrulanamıyorsa ilgili fazda sorulur; önceki tercih tekrar sorulmaz.

| Faz | Gerekebilecek bilgi | Agent'ın önce hazırlayacağı çalışma |
|---|---|---|
| F16 | Gerçek site saat dilimi; plan varsayımı Europe/Istanbul | UTC dönüşümü/roundtrip kanıtı. |
| F20 | Gerçek URL çakışmasında korunacak yazı | Dry-run liste ve alternatif. |
| F27 | Otomasyonun doğrudan yayın tercihi | Taslak varsayılanı/açık yayın yetkisi. |
| F47 | Gerçek yazar/biyografi/iletişim | Uydurulmamış içerikle sayfa taslağı. |
| F50 | Kaynakta yoksa gerçek hosting/proxy düzeni | Config ve header güven sınırı. |
| F56 | Sonradan istenecek canlı dağıtım yetkisi | Staging kanıtı ve geri dönüş planı. |

F00 yalnızca dokümantasyon geçişini tamamlar. Güvenlik bulguları ve mevcut 56/100 SOLID değerlendirmesi kod fazları uygulanmadan değişmiş sayılmaz. Sıradaki faz F01'dir.
