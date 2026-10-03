# Güncel mimari ve çalışma kararları

**Güncelleme:** 3 Ekim 2026 — F41 sınırlı yönetici envanteri ve dönüş bağlamı.

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

F35'te mevcut Play CDN bundle'ı Tailwind 3.4.17 olarak doğrulandı; aynı sürümün resmi `tailwindcss` CLI paketi ve Prism 1.30.0 exact sürüm/lockfile ile sabitlendi (MIT). Tailwind 4'e geçilmedi. Mevcut Toast UI `latest` yanıtı 3.2.2; npm dağıtımı mevcut all-in-one dosyasını içermediğinden resmi 3.2.2 CDN dosyaları değiştirilmeden kaynak vendor dizininde, hash ve lisanslarla korunur. Runtime bütün bu varlıkları yerelden okur.

`npm run build`, mevcut public/admin temalarıyla ayrı CSS ve lisanslı vendor çıktısı üretir. Küçük Node standart kütüphane script'i yeterli olduğundan bundler/PostCSS framework'ü eklenmedi. `dotnet build/publish` npm kurulumunu gerektiğinde yeniler ve asset build'i çalıştırır. Node/npm yalnız build ortamında gereklidir. Razor/JS tam literal sınıfları taranır; üretilmiş çıktı Git dışında tutulur, temiz publish'e eklenir. [Resmi v3 CLI](https://v3.tailwindcss.com/docs/installation), [Prism Toolbar](https://prismjs.com/plugins/toolbar/), [Show Language](https://prismjs.com/plugins/show-language/), [Toast UI sürümü](https://github.com/nhn/tui.editor/releases/tag/editor%403.2.2), [F35 kanıtı](../uygulama-kayitlari/F35-2026-10-01.md).

Küçük gerekçeli test/build bağımlılığı yasak değil. Olmayan aracı var saymak, gereksiz framework veya bütün yığını değiştirmek yasak. Rutin teknik işte yeniden kullanıcı izni istenmez; gerçek kapsam/ücretli servis/ürün değişimi ayrıca somutlaştırılır.

**Durum:** UYGULANDI — F35; temiz kurulum, tekrar üretim, publish ve gerçek tarayıcı doğrulandı.

## D05 — RSS ve HTML önizleme MVC çıktılarıdır

F33 yetkili HTML önizleme, F46 standart XML RSS üretir. Ayrı API mimarisi veya SPA gerekmez. Auth/no-store/yayın görünürlüğü/encoding kuralları çıktı formatından bağımsız uygulanır.

**Durum:** F33 uygulandı. Native Preview in new tab düğmesi yalnız o POST için ayrı target ve URL kullanır; normal Save formu korunur. Detay/önizleme aynı güvenli içerik partial'ını kullanır. Önizleme upload yapmaz; kayıtlı kapağı gösterir. [F33 kanıtı](../uygulama-kayitlari/F33-2026-10-01.md). F46 kapsamda; kullanıcı RSS'i açıkça ertelerse gerekçesiyle kaydedilir, tamamlandı sayılmaz.

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

## D08 — Geçmiş slug çakışmasında tek URL sahibi

F20 göçünde aynı boş olmayan slug'ı paylaşan yazı veya kategorilerden en küçük ID mevcut URL'yi korur. Diğer kayıtlar mevcut bütün slug'lar saklı tutularak ilk boş numaralı adrese geçer. Boş/yalnızca boşluk slug'ı olan kayıtlar tür adı ve ID tabanlı boşta bir adrese geçer. Bu işlem kayıt silmez, birleştirmez veya geçmişte belirsiz olan URL için iki ayrı yönlendirme uydurmaz.

Gerçek veritabanında göç çalıştırılmadan önce [salt okunur envanter](../../scripts/verification/f20_slug_inventory.sql) incelenir. Önerilen ID sahibi gerçek ürün beklentisine uymuyorsa somut kayıt tablosuyla kullanıcı kararı alınır; production göçü otomatik çalıştırılmaz.

**Durum:** F20 kodu ve sentetik PostgreSQL göçüyle doğrulandı; gerçek veri envanteri/deploy ayrı operasyonel adımdır.

## D09 — Okunma sayacı istek sayısıdır

F21'de sayaç yalnızca anonim public yazı detayına gelen uygun GET isteğinde bir artar. HEAD ve oturum açmış yönetici ziyareti sayılmaz. Sayaç tekil ziyaretçi veya güvenilir insan/bot ayrımı değildir; kişi takibi ve yeni analitik API eklenmez. Güncelleme yalnızca `ViewCount` sütununu atomik değiştirir, detay çıktısı cache'lenmez.

**Durum:** F21 kodu ve sentetik PostgreSQL/HTTP eşzamanlılık kontrolüyle doğrulandı.

## D10 — İçerik edit sürümü sayaçtan ayrıdır

F22'de Post ve Category için uygulama tarafından artırılan tek `EditVersion` concurrency token'ı seçildi. Admin edit formu yüklenen sürümü taşır; EF Core kayıtta orijinal sürümü SQL koşulunda kıyaslar. Kaydetme çakışması 409 ile bildirilir, ikinci sekmenin girdisi ekranda kalır. Post `ViewCount` artışı bu sürümü değiştirmez; PostgreSQL `xmin` bu nedenle kullanılmadı. Tam revizyon geçmişi tutulmaz.

**Durum:** F22 kodu, boş/önceki şema migration'ı ve sentetik PostgreSQL/HTTP yarış kontrolüyle doğrulandı.

## D11 — Kalıcı webhook tekrar koruması ve istemci geçişi

F28 ile isteğe bağlı `Idempotency-Key` eklendi. Anahtarsız eski istemciler çalışır,
ancak tekrar koruması yalnız anahtar taşıyan gönderimlerde geçerlidir. Anahtar,
typed payload özeti ve oluşturulan post referansı tek transaction'da tutulur;
DB benzersizlik kuralı eşzamanlı isteklerde tek post sağlar. Aynı anahtar/aynı
girdi ilk yanıtı döndürür, farklı girdi 409 üretir. Başarılı kayıt süresiz saklanır;
post silinince nullable FK ayrılır ama ilk sonuç korunur. Başarısız işlem yeni
anahtarı tüketmez. Dış otomasyon değişikliği ve canlı migration yapılmadı.

[İstemci, saklama ve geri dönüş sözleşmesi](../WEBHOOK_SOZLESMESI.md).
**Durum:** F28 sentetik PostgreSQL/HTTP kabulüyle doğrulandı.

## D12 — Portföy için doğrulanmış site origin'i ve dar projection

F29 portföy feed'i mevcut JSON alanlarıyla en çok üç görünür yazı döndürür;
`PublishDate DESC, Id ASC`, LIMIT ve dar kolon seçimi DB'de uygulanır. Controller
küçük `IPublicFeedService` okuma sözleşmesini kullanır; tam Post/Category grafiği
ve Markdown içerik dışarı taşınmaz. `no-store` korunur.

Production `SITE_URL` için açık HTTPS origin gerektirir; eksik/geçersiz ayar
başlatmayı durdurur. Development loopback HTTP'ye izin verir, ayar yoksa
localhost:5000 varsayılanıdır. Feed URL'si mevcut post-en rotası ve bu origin'den
gelir. CORS'un gerçek doğrulanmış listesi UI'da da kullanılır; boş production
liste ek browser origin'i açmaz. CORS kimlik kontrolü değildir.

[Portföy/config/cache sözleşmesi](../PORTFOY_BESLEME_SOZLESMESI.md).
Canlı domain/hosting ve dış portföy istemcisi bu fazda değiştirilmedi.
**Durum:** F29 sentetik PostgreSQL/HTTP ve tarayıcı kanıtıyla doğrulandı.

## D13 — Açık yönetici kayıt eylemleri

F32 aynı MVC formunda Save Draft, Schedule ve Publish now kullanır. Schedule
gelecekte bir site zamanı ister; Publish now sunucunun UTC saatini alır. Edit'teki
normal Save kayıtlı yayın iznini ve tarihini korur. Eksik eylem Save Draft'tır;
eski IsPublished form değeri yetki/niyet kaynağı değildir. Bilinmeyen eylem
reddedilir. Yayın durumu aktif yazı/kategori, izin ve UTC tarihle hesaplanır.
F31 kurtarma biçimi korunur; kurtarılan eski yayın bayrağı yeniden yayınlama yapmaz.
Dashboard bayrak sayacı Publication enabled / Includes scheduled olarak gösterilir;
public görünürlüğü kanıtlamayan sıralama satırlarında LIVE bağlantısı yoktur.
**Durum:** F32 izole PostgreSQL/HTTP ve gerçek tarayıcı kanıtıyla doğrulandı.

## D14 — Sınırlı okuyucu sorguları ve collation sözleşmesi

F40 arama terimini100, kategori slug'ını200 karakterle; page'i1–1000 ve
pageSize'ı9/18/27 ile sınırlar. Varsayılan page 1/size 9; boş terim liste getirmez.
Geçersiz/model binding hatalı input400; geçerli ama bulunmayan sayfa veya
aktif olmayan/bulunmayan kategori404 olur. Boş ilk sayfa200 ve yönlendirme gösterir.
Sınırlar HTTP yanında service/repository tüketicilerine de uygulanır.

Count, yayın/kategori filtresi, kararlı sıralama ve Skip/Take PostgreSQL'de çalışır.
Kart projection'ı sekiz alanla sınırlıdır; içerik ve entity grafiği taşınmaz.
Arama sırası tam başlık, başlıkta eşleşme, PublishDate DESC, Id ASC olur.
Parametreli literal ILIKE, yüzde/alt çizgi/ters slash'ı escape eder. Harf katlama
veritabanının gerçek collation'ına bağlıdır; uygulama culture'ı tahmin edilmez.
Test PostgreSQL 16.14/C ortamında İSTANBUL eşleşirken istanbul eşleşmedi;
IĞDIR/ığdır ve ğüşiöç/ĞÜŞİÖÇ farkları [F40 kaydında](../uygulama-kayitlari/F40-2026-10-02.md).
Canlı ortamın collation'ı keşfedilmeden Türkçe eşleşme garantisi verilmez.
Bu faz collation/migration/extension veya yeni arama altyapısı eklemez.

Arama no-store'dur. pageSize ve diğer query varyantları mevcut F18 output cache'e
alınmaz; varsayılan ana sayfa/kategori cache'inin sınırlı anahtarları, mutation
invalidation'ı ve zamanlı yayın sınırı korunur. 1000'den fazla sayfa varsa UI
filtreyi daraltmayı ister. İçerik arama/Count maliyeti F48 ölçümüne açıktır.
**Durum:** F40 gerçek PostgreSQL/HTTP, SQL ve tarayıcı kanıtıyla doğrulandı.

## D15 — Yönetici envanteri ve güvenli dönüş bağlamı

F41 başlık/kategori adı aramasını, kategori ID ve yayın durumunu GET filtreleri
olarak sunucuda işler. Arama 100 karakter; page 1–1000; size 10/25/50, varsayılan25.
Count, tüm filtreler, CreatedDate DESC/Id ASC ve Skip/Take DB'de çalışır.
Liste Content/medya/summary/entity grafiği taşımaz; yalnız kullanılan kolonlar ve
ortak domain durum hesabının gereken skaler girdileri döner. HTTP yanında
service/repository de sınırları doğrular. Geçersiz input400; bilinmeyen kategori404.
Bulunmayan/stale sayfa aynı filtrelerle son geçerli sayfaya, boş sonuç ilk sayfaya döner.

F32 durum sözlüğü tek skaler expression'da tutulur; aynı expression SQL'e inline
edilir ve aynı delegate gösterimde değerlendirilir. Scheduled gelecekteki UTC
PublishDate'e bağlıdır; pasif post/kategori Inactive'tir. Sorgu ve etikette aynı
TimeProvider anı kullanılır. Public VisibleAt SQL sınırı değişmedi.

Liste edit ve native antiforgery POST toggle'a kanonik filtreli returnUrl verir.
Yalnız yerel /AdminPost veya /AdminPost/ yolu kabul edilir; URL2048 karakterle
sınırlıdır, control karakter/dış URL/başka local action işlemden önce400 olur.
Save/Cancel/Reload filtre bağlamını korur. Eski returnUrl'siz toggle JSON yanıtı
ve F31 doğrulanmış kayıt makbuzu korunur. Liste no-store; private cache eklenmez.
Delete confirmation kullanıcı başlığını executable inline event'e eklemez.
Yeni bağımlılık/şema/API/tablolu framework/toplu mutation yok.
**Durum:** [F41 gerçek PostgreSQL/HTTP/SQL ve tarayıcı kanıtı](../uygulama-kayitlari/F41-2026-10-03.md) ile doğrulandı.

## Gerektiğinde alınacak gerçek ürün/ortam bilgileri

Bunlar şimdi topluca sorulmaz. Mevcut kaynaktan doğrulanamıyorsa ilgili fazda sorulur; önceki tercih tekrar sorulmaz.

| Faz | Gerekebilecek bilgi | Agent'ın önce hazırlayacağı çalışma |
|---|---|---|
| F16 | Gerçek site saat dilimi; plan varsayımı Europe/Istanbul | UTC dönüşümü/roundtrip kanıtı. |
| F20 | Gerçek URL çakışmasında korunacak yazı | Dry-run liste ve alternatif. |
| F27 | Otomasyonun doğrudan yayın tercihi | `ALLOW_WEBHOOK_PUBLISH` varsayılan `false`; yalnız açık `true` ayarıyla yetkili webhook doğrudan yayımlar. Canlı ortamda açma kararı ayrıca verilir. |
| F47 | Gerçek yazar/biyografi/iletişim | Uydurulmamış içerikle sayfa taslağı. |
| F50 | Kaynakta yoksa gerçek hosting/proxy düzeni | Config ve header güven sınırı. |
| F56 | Sonradan istenecek canlı dağıtım yetkisi | Staging kanıtı ve geri dönüş planı. |

F00 yalnızca dokümantasyon geçişini tamamlar. Güvenlik bulguları ve mevcut 56/100 SOLID değerlendirmesi kod fazları uygulanmadan değişmiş sayılmaz. Sıradaki faz F01'dir.
