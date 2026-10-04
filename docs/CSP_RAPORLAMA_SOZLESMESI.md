# F51 CSP raporlama sözleşmesi ve kaynak envanteri

F51 yalnız **Content-Security-Policy-Report-Only** hazırlığıdır. Bu mod ihlali
bildirir, saldırıyı engellemez; F03/F04 encoding ve güvenli Markdown sınırını
ikame etmez. F52 onayı olmadan enforcement başlığı veya canlı politika eklenmez.

## Etkinleşme ve yanıt sınırı

`appsettings.Development.json` içindeki `Security:Csp:ReportOnlyEnabled=true`
yerel geliştirmede etkindir. Başlangıçtan önce process environment’a
`Security__Csp__ReportOnlyEnabled=false` verilerek kapatılabilir; bu setting
dotenv’ten yüklenmez. Development dışında açık report-only hazırlığı başlangıçta
reddedilir. Production varsayılanı kapalıdır; F50 servis düzeni korunur.

Middleware yalnız HTML yanıtta, OnStarting sırasında başlık üretir. 404/500
HTML hata akışları da bu sınırdadır; JSON/XML/RSS/statik JS/CSS’ye HTML politikası
konmaz. Public liste output cache’iyle politika sabittir. Nonce veya kullanıcı
metninden hash üretilmez; farklı cache body/header nonce eşleşmesi sorunu yoktur.

Public rapor endpoint’i, report-uri/report-to, analytics veya üçüncü taraf
raporlama servisi yoktur. Tarayıcıdaki securitypolicyviolation ve konsol gözlemi
geçici testte tutulur; gerçek içerik/token/cookie/ham rapor depolanmaz. Rapor
hedefi olmadığına ilişkin tarayıcı uyarısı açıklanmıştır, koruma iddiası değildir.

## Gerçek kaynak envanteri

| Sınır | Mevcut kullanım | Aday izin |
|---|---|---|
| Script | F35 yerel Toast UI 3.2.2, Prism 1.30.0 ve yerel autoload grammars; jQuery 3.7.1/Validation 1.21.0/Unobtrusive 4.0.0; küçük site/admin modülleri | `'self'`; script öznitelikleri `'none'`; unsafe-inline/unsafe-eval/wildcard yok |
| Style element/link | F35 Tailwind 3.4.17, Prism, Toast UI, site/admin/error CSS; mevcut Google Fonts stylesheet | `'self' https://fonts.googleapis.com`; inline style element yok |
| Style attribute | Pinned Toast UI/ProseMirror editörün dinamik boyut/konum stilleri | Yalnız başarılı AdminPost Create/Edit HTML’de `'unsafe-inline'`; diğer ekranlarda `'none'` |
| Font | Inter ve JetBrains Mono için mevcut Google Fonts dosyaları | `'self' https://fonts.gstatic.com` |
| Image | Cloudinary teslimi, mevcut herhangi bir doğrulanmış HTTPS legacy kapak/Markdown URL sözleşmesi; yerel görseller; editör CSS data ikonları | `'self' https: data:` |
| Connect | Editör upload endpoint’i ve aynı origin etkileşimleri | `'self'`; Cloudinary’ye tarayıcıdan upload/connect izni yok |
| Frame | Server renderer’ın dar YouTube URL/ID sözleşmesinden ürettiği nocookie embed | `https://www.youtube-nocookie.com`; wildcard yok |
| Object/base/form/ancestor | Object veya base yok; yerel MVC POST formları; sayfa dışarıda frame edilmez | object/base/ancestor `'none'`, form `'self'` |

HTTPS image izni geniş bir **image** origin sözleşmesidir; dar script izni ile
aynı şey değildir. Core cover validation ve Markdown renderer hâlen keyfî HTTPS
image URL’lerini desteklediği için yalnız res.cloudinary.com’a sessizce daraltmak
mevcut içeriği kırardı. Bu faz image sözleşmesini değiştirmez. Data izni editör
CSS ikonları için korunur; kullanıcı Markdown data URL’leri güvenli renderer’da
reddedilmeye devam eder. Harici içerik yüklemenin mevcut gizlilik etkileri F47
taslağında kalır. YouTube iframe’in kendi iç asset/bağlantıları child belgenin
politikasıdır; parent sayfaya ek Google/video CDN script izinleri eklenmedi.

Vendor bundle’daki new Function yalnız globalThis bulunmayan eski runtime
fallback’idir; gerçek Chrome akışında eval ihlali olmadı. unsafe-eval eklenmedi.
F35’te kaydedilmiş eski vendored DOMPurify bakım borcu kapanmış sayılmaz;
F51 dependency güvenlik sertifikası veya sanitizer sürüm yükseltmesi değildir.

## Inline kodun taşınması

- Public tema head’te senkron yerel `public-theme.js` ile ilk stili uygular,
  DOM hazır olunca düğmeye addEventListener bağlar; global toggleTheme kaldırıldı.
- Toast sabit yerel dosyada textContent ile gösterilir. TempData Razor-encoded
  data-success/error/message öznitelikleridir; raw JSON/JS içinde yazılmaz.
- Kategori adı data-confirm-category’den alınır; quote/HTML içeren ad executable
  onsubmit’e dönüşmez. Native confirm iptal/kabul ve CSRF POST korunur.
- Dashboard scratchpad, kategori slug yardımcısı, otomasyon pano işlemi ve
  validation ayarı küçük ayrı yerel modüllerdir; dependency yükleme sırası korunur.
- Admin shell ve hata CSS’i ayrı dosyadır. Görünüm/Tech Minimal değiştirilmez.
- Güvenli JSON-LD script[type=application/ld+json] inert veri olarak kalır;
  default System.Text.Json encoder sınırı ve F44 semantiği korunur.

## Ölçüm ve kabul

Önceki kaynak kopyasına tarayıcı fixture’ında sıkı Report-Only aday başlığı
verildi; inline script/style ihlalleri ve Create’te 31 style-src-attr ihlali
ölçüldü. Son durumda normal okuyucu/login/admin/Create/Edit/upload/preview/code/
embed akışlarında açıklanmamış CSP ihlali yoktur. Editör stil istisnası bu ölçüme
dayanır; genel style-src veya script izni genişletilmedi.

Kabul sentetik verili gerçek PostgreSQL ve Chrome’dadır. Image upload gerçek
uygulama HTTP/CSRF/policy hattından fixture storage’a gider; images.example.test
HTTPS cevabı tarayıcı fixture’ında sentetik PNG’dir, gerçek Cloudinary hesabı
kanıtı değildir. Kötü amaçlı örnekteki src=x görseli editor preview’da 404 alır;
event handler/script çalışmaz. YouTube nocookie frame belgesi gerçek HTTP 200
ile yüklendi; video playback veya bir kullanıcı hesabı testi yapılmadı.

Ayrı sentetik inline probe, Report-Only’nin scripti çalıştırıp ihlal bildirdiğini
kanıtlar. Normal akışların sıfır ihlal sonucuna bu kasıtlı probe dahil edilmez.
Mobil/masaüstü, klavye drawer Escape/odak geri dönüşü, pano işlemi ve açık Restore
sözleşmesi korunur. Şema/canlı veri değişmez. F52’de aday **enforce** edilerek
bütün akışlar yeniden doğrulanmalıdır; Report-Only başarısı enforce kabulü değildir.

Kaynaklar: [OWASP CSP](https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html),
[MDN style-src-attr ve CSSOM farkı](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/style-src-attr).
