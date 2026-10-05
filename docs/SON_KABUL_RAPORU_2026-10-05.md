# DevCoreBlog — F57 son kabul değerlendirmesi

5 Ekim 2026 · Başlangıç commit’i `9cce9fed4faad06c1cc26d73c27e8fe80ae3390f`.

**F57 tamamlandı; ana plan58/58. Canlı kurulum henüz doğrulanmadı ve üretime
hazır sonucu verilmedi.** Onaylı [E01 editör bakımı](uygulama-kayitlari/E01-2026-10-05.md)
vendor sanitizer bulgusunu giderdi. [F57 yeniden kabulü](uygulama-kayitlari/F57-CI-2026-10-05.md)
uzak CI’nın yakaladığı webhook ilk/tekrar yanıt farkını dar repository düzeltmesiyle
kapattı; kaynak commit’i `2a7a5f1` için yerel ve [GitHub kapısı](https://github.com/CixxMemo/DevCoreBlog/actions/runs/37292561215)25/25 geçti.
Şema ve UI değişmedi. Aşağıdaki ilk değerlendirme kanıtları korunur;
E01 ve yeniden kabulün güncel kanıtları ayrı kayıtlardadır.

## Doğrulanmış kullanım

Gerçek Chrome’da tek bir sentetik yazı ile giriş → yeni kategori → editör görseli →
kapak sağlayıcı hatası → başarısız kayıttan açık Restore → kapaklı taslak → özel
önizleme → geleceğe zamanlama → Publish now → arama/canonical/BlogPosting/
sitemap/RSS/portföy → kalıcı adresi koruyan edit → Save Draft akışı geçti.
Başarısız kayıt yazı oluşturmadı; yerel kopya yalnız doğrulanmış başarılı kayıtta
temizlendi. Taslak ve zamanlanmış yazı erken public detayda görünmedi; geri çekilen
yazı detay, arama, sitemap ve RSS’ten çıktı. Medya test sağlayıcısı kullanıldı;
Cloudinary hesabına erişildiği iddia edilmiyor.

| Kontrol | Sonuç | Kanıt |
|---|---|---|
| Tek yazının uçtan uca yolculuğu | 25/25 | [Son yolculuk](kanitlar/F57-2026-10-05/journey-final/journey.json) |
| Normal/deliberate CSP, XSS, editör, tema, klavye ve mobil | 36/36; 27 normal ziyaret, beklenmeyen runtime/console hatası yok | [Chrome](kanitlar/F57-2026-10-05/csp-browser/browser.json) |
| Güvenlik/veri kabulü | 23 doğrudan kontrol + 7 grubun 49 alt kontrolü geçti; raporda 30 üst sonuç | [Temiz kaynak regresyonu](kanitlar/F57-2026-10-05/quality/final-regression.json) |
| Eski edit ve eşzamanlı güncelleme | 8/8; tek kazanan, diğer formda 409 ve metin korunması | [Edit](kanitlar/F57-2026-10-05/quality/final-edit-conflict.json) |
| Genişletilmiş kalite kapısı | 24/24 stage; gerçek npm riski kabul kapsamında, dört NuGet taraması bulgusuz | [Kapı](kanitlar/F57-2026-10-05/quality/summary.json) |
| Root/temiz kaynak build | 0 hata, 0 uyarı | [Root](kanitlar/F57-2026-10-05/root-build.log), [temiz build](kanitlar/F57-2026-10-05/quality/build.log) |
| Npm istisnasının negatif sınırları | 14/14 | [Politika](kanitlar/F57-2026-10-05/audit-policy.json) |
| Recovery motoru | 11 kontrol ve eski medya açıklaması kopyası kontrolü geçti | [Recovery](kanitlar/F57-2026-10-05/recovery.log) |
| Gerçek Chrome %200 zoom | Liste ve form: viewport/root scroll 960/960; editör yüklü; dört kayıt düğmesi 44 px; başlangıç %100’e dönüldü | [Ölçüm](kanitlar/F57-2026-10-05/zoom.json), [liste](kanitlar/F57-2026-10-05/zoom-list.png), [editör](kanitlar/F57-2026-10-05/zoom-editor.png) |
| Önceki fazların kayıt/yerel kanıt bağlantıları | F00–F56: 57 kayıt mevcut; belge içindeki kod örneği link sayılmadı | [Envanter](kanitlar/F57-2026-10-05/phase-evidence.json) |
| Uzak CI | İlk koşuda DOĞRULANAMADI; yeniden kabulde güncel GitHub/Linux kapısı25/25 | [İlk sorgu kapsamı](kanitlar/F57-2026-10-05/remote-ci.json), [güncel CI](kanitlar/F57-CI-2026-10-05/remote-ci.json) |

Gerçek PostgreSQL 16.14 ile atomic sayaç, sayaç sırasında edit, eşzamanlı edit,
kategori insert/delete yarışı, ısıtılmış cache invalidation, paralel okuma/yazma,
zamanlı yayının sınırda görünmesi ve webhook idempotency/restart yeniden sınandı.
Eksik/bozuk kimlik yapılandırması başlangıcı kapattı; boş/yanlış parola reddedildi.
11 cookie mutation uç noktası tokensız 400 verdi; preview da dahil. Gerçek TLS
proxy fixture’ında Secure/HttpOnly/Lax, expiry, restart ve session stamp değişimi
kalite kapısında geçti. Nginx kurulumu veya gerçek sağlayıcı testi değildir.

## SOLID: 56/100 → 82/100

Başlangıç raporunun beş eşit 20 puanlık rubriği uygulandı. Bu yorumlu kaynak
incelemesidir; otomatik kalite/güvenlik sertifikası değildir. Hedef banda ulaşıldı
diye puan verilmedi; her satırda iyileşme ve kesinti gerekçesi vardır.

| İlke | Önce | Şimdi | Kaynak ve kalan sınır |
|---|---:|---:|---|
| S — tek sorumluluk | 10 | 16 | [AdminController](../Controllers/AdminController.cs) dashboard servisinden okuyor; [navigation bileşeni](../Components/PublicCategoryNavigationViewComponent.cs) Razor’dan veri erişimini çıkarıyor. [Ortak form](../Views/AdminPost/_PostEditorForm.cshtml), post-editor/recovery modülleri ve [renderer](../DevCoreBlog.Services/Rendering/SafeMarkdownRenderer.cs) ayrılmış. Buna karşın [PostService](../DevCoreBlog.Services/PostService.cs) CRUD/yayın/slug/webhook/feed/RSS/sitemap koordinasyonunu birlikte taşıyor; yönetici controller’ında uzun upload/save akışı sürüyor. |
| O — değişime açıklık | 11 | 17 | [IPostRepository](../DevCoreBlog.Core/Interfaces/IPostRepository.cs), ICategoryRepository, [IImageStorage](../DevCoreBlog.Services/Images/IImageStorage.cs), ISafeMarkdownRenderer ve TimeProvider değişim noktalarıdır. Synthetic storage gerçek upload politikasını değiştirmeden yer değiştiriyor. Slug retry iki serviste benzer; PostService’e yeni kullanım yolu eklemek hâlâ merkezi sınıf değişikliği gerektiriyor. Güvenlik allowlist’inin kapalı olması tek başına O ihlali sayılmadı. |
| L — yerine geçebilme | 17 | 18 | [GenericRepository](../DevCoreBlog.Data/Repositories/GenericRepository.cs) null/not-found sözleşmesini koruyor; [PostRepository](../DevCoreBlog.Data/Repositories/PostRepository.cs) görünür sayaç için null, slug yarışı için false; [CategoryRepository](../DevCoreBlog.Data/Repositories/CategoryRepository.cs) yalnız ilgili FK yarışını false’a çeviriyor, diğer hataları yutmuyor. F57 gerçek HTTP/DB testleri bunları destekliyor. Bütün alternatif repository/provider implementasyonları ve her cancellation yolu için kapsamlı substitutability testi yok; tam puan verilmedi. |
| I — dar arayüz | 12 | 13 | [IRssPostReader](../DevCoreBlog.Services/Interfaces/IRssPostReader.cs), feed/webhook/sitemap/dashboard ve aktif kategori lookup sözleşmeleri dar. Ancak [IPostService](../DevCoreBlog.Services/Interfaces/IPostService.cs) ile ICategoryService hâlâ public okuma + admin yazmayı birleştiriyor; HomeController ve navigation tüketicileri ihtiyaç dışı yetenek görüyor. Kullanılmayan eski sınırsız post read metotları interface’te duruyor. En küçük iyileşme burada. |
| D — soyutlamaya bağımlılık | 6 | 18 | [Core projesi](../DevCoreBlog.Core/DevCoreBlog.Core.csproj) EF/Markdig/Cloudinary bağımlılığı taşımıyor; [Services projesi](../DevCoreBlog.Services/DevCoreBlog.Services.csproj) Data’ya referans vermiyor. Servisler Core repository sözleşmelerini, controller’lar servis sözleşmelerini kullanıyor; Cloudinary istemcisi [Program.cs](../Program.cs) composition root’unda. IFormFile ve concrete output-cache invalidator Services’in ASP.NET sınırına bağlanmasını sürdürüyor; yeniden kullanımda adaptasyon gerekir. |
| **Toplam** | **56** | **82** | S/O/L/I/D eşit ağırlık. Arayüz sayısı veya csproj sayısı için bonus verilmedi. |

Kaynak taraması controller/component/view içinde gerçek DbContext/EF sorgusu,
servis içinde `new Repository`, Core’da vendor import veya yeni NotImplemented
stub bulmadı. Eski uzun eğitim yorumları ve sınırsız legacy arayüzler ayrıca
bakım borcudur. Bunları F57 içinde geniş refactor ile gizlice değiştirmedik.

## Açık riskler ve yayın öncesi eksikler

**İlk F57 P1 bulgusu — E01 ile giderildi.** İlk Toast UI dağıtımı DOMPurify
**2.3.3** içeriyordu. Üreticinin
[GHSA-gx9m-whjm-85jf / CVE-2024-47875](https://github.com/cure53/DOMPurify/security/advisories/GHSA-gx9m-whjm-85jf)
kaydı `<2.5.0` aralığını etkilenen sayıyor; 2.5.0 o advisory’nin tarihsel ilk
yamasıdır, bugün tüm bulgular için güvenli sürüm önerisi değildir.
[Sürüm kanıtı](kanitlar/F57-2026-10-05/vendor-review.json).
Bu runtime editör varlığı npm lock audit kapsamında değil; `braces` için kabul
edilen D28 istisnası buna uygulanamaz. Sunucu güvenli renderer’ı ve enforcing
CSP ek koruma sağlıyor, fakat bağımlılığı yamalamıyor. Bu turda uygulamaya özgü
bu advisory exploit’i gösterilmedi; “ziyaretçi XSS’i doğrulandı” denmiyor.
Resmi [Toast UI repository’si](https://github.com/nhn/tui.editor) 2 Eylül 2026’da
arşivlenmiş görünüyor; sürdürülen güvenli dağıtım kararı ayrıca gerekli.

E01’de aynı Editor3.2.2’nin resmî kaynağındaki sanitizer modülü, hash/AST kontrolüyle
resmî DOMPurify3.4.16 modülüyle bütünüyle değiştirildi. Eski modül runtime/publish
çıktısında yoktur; yalnız dışlanan frontend upstream build input’unda kalır.
Üreticinin güncel rawtext-root önce/sonra regresyonu9/9 ve build integrity5/5,
aynı Chrome journey25/25/CSP36/36 ve temiz kapı25/25 geçti. Bu DevCoreBlog’un
bakımını yaptığı dağıtımdır; arşivli NHN Editor için genel upstream destek
garantisi değildir. [E01 kanıtı](uygulama-kayitlari/E01-2026-10-05.md),
[dağıtım kaynağı/lisans/hash](../frontend/vendor/toastui/README.md).

**Uzak CI kanıtı — tamamlandı.** İlk değerlendirmedeki PR’ye özel dar sorgu
push koşularını kapsamıyordu. Doğrudan Actions sorgusu önceki `e1b279f` koşusunun
webhook tekrar yanıtı hassasiyetinde başarısız olduğunu gösterdi; bu kanıt korundu.
Yedi basamaklı sabit UTC tarih girdisi hatayı yerelde yeniden gösterdi.
Transaction içinde receipt yeniden okunarak ilk/tekrar yanıtı DB'nin sakladığı
değerlere bağlandı; assertion’lar korunarak temiz yerel25/25 geçti.
Kullanıcı onayıyla commit/push edilen `2a7a5f1f916cc88e86bec4f9f94e05399d14da45`
için run37292561215 success; artifact checksum/run/commit eşleşmesi doğrulandı.
F02–F08 güvenlik/CSRF, yedi regresyon grubu ve F22 kabulü CI’da geçti.
[Kapı](kanitlar/F57-CI-2026-10-05/remote-quality/summary.json),
[güvenlik/veri](kanitlar/F57-CI-2026-10-05/remote-quality/final-regression.json),
[edit](kanitlar/F57-CI-2026-10-05/remote-quality/final-edit-conflict.json).
Branch protection/remote settings değiştirilmedi; deploy yapılmadı.

**Kabul edilmiş ayrı risk — braces.** GHSA-vfj7-8cjw-p6xm build-time istisnası
exact dev graph ve **5 Kasım 2026** inceleme sınırıyla açık. Ham audit exit1
korunuyor; vulnerability-free sonucu yok. Kullanıcı kabulü değiştirilmedi.

**Canlı işletim henüz kanıtlanmadı.** Sağlayıcı/VPS kapasitesi, domain/TLS,
gerçek Nginx/systemd, PostgreSQL erişim/rol ayarları, keyring ACL, Cloudinary byte
yedeği, yedek encryption/offsite/retention ve RPO/RTO hedefleri seçilip staging’de
sınanmalı. F56 test restore/yayın runbook’u bu gerçek kurulumun yerine geçmez.
F54 çalışma ağacından cookie’yi çıkardı; Git geçmişi temizlenmiş veya canlı
oturumlar iptal edilmiş sayılmaz. F47 veri kullanımı metni hâlâ değerlendirme taslağı.

## Performansın doğru yorumu

[F48 laboratuvar kanıtı](uygulama-kayitlari/F48-2026-10-04.md) 20.009 sentetik
yazıyla alınmıştı. O testin HTTP medyanları ana sayfa 10,12 ms, detay 2,87 ms,
arama 18,94 ms, admin 3,35 ms idi. Bunlar 4 Ekim’deki yerel ölçümler; bu turda
20.000 satır performans koşusu tekrarlanmadı. F57’de uygulama kaynak kodu aynı
kaldı. Bu sayılar canlı kapasite/SLA veya ziyaretçinin LCP/CLS/INP sonucu değildir.
Gerçek kullanıcı CWV verisi yok; uydurulmadı.

## Bilinçli kapsam dışı ürün işleri

Etiket/seri, sunucuda revision geçmişi, yorum/moderasyon, bülten, çok yazarlı
roller, ödeme, AI üretimi ve kişiselleştirme eklenmedi. Bunlar temel tek yöneticili
blogun kabulünü tamamlamak için zorunlu değildir ve ayrıca ürün kararı ister.
Yeni framework, Redis, queue veya sağlayıcı eklenmedi.

## Tamamlanan ek iş ve durma noktası

İlk öneri **“Vendored editör güvenlik bakımını tamamla”** adlı ayrı atomik işti:
resmi kaynak/advisory envanteri → mevcut Toast UI davranışını koruyan sürdürülebilir
yamalanmış dağıtımın seçimi → lisans/hash/build güncellemesi → gerçek browser
XSS/CSP/upload/recovery/preview kabulü. Minified dosyaya rastgele regex yaması veya
eski advisory’nin ilk patch’ine kör yükseltme uygulanmamalı. Editör ürünü değiştirmek
gerekirse ayrı kullanıcı tercihi gerekir. Kullanıcı E01’i onayladı ve aynı
Editor’i koruyan bu bakım **tamamlandı**; ürün değişmedi.

Güncel uzak CI kanıtıyla F57 yeniden kabulü **tamamlandı**; F00–F57’nin kapsam
kanıtları korunur. Ana plan58/58, ayrı E01 tamamlandı. Sıradaki numaralı faz yoktur.
Canlı kurulum veya yeni ürün işi ayrıca kullanıcı onayı ister ve başlatılmadı.
