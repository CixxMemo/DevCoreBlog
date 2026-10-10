# CSP sözleşmesi ve kaynak envanteri — F52

F52 ile **Content-Security-Policy engelleme modunda bütün ortamlarda** etkindir.
F51 Report-Only hazırlığı tarihsel kayıttadır.
Encoding ve güvenli Markdown sınırları ayrı zorunlu korumalardır.

Tiptap'in kayıtsız yönetici denemesi `/AdminPost/EditorTrial` aynı enforcing
politikanın `style-src-attr 'none'` sınırını kullanır. `injectCSS:false`, yerel
bundle/CSS ve minimal extension set'i ile yeni izin/nonce eklenmez. Create/Edit
Toast UI istisnası korunur. [Tiptap build sözleşmesi](TIPTAP_BUILD_SOZLESMESI.md).

JSON kayıt geçişinde gerçek legacy Edit409 yanıtı yeniden ölçüldü. Başarısız
Create/Edit yanıtında dar style-src-attr none politikası korunur; ortak formun
sunucu tarafından üretilen `data-rich-editor-enabled` alanı yalnız200'de true
olur. `post-editor.js` diğer yanıtlarda Toast UI'yi başlatmaz, encode edilmiş
textarea'yı görünür tutar. Kullanıcı metni ve mevcut kurtarma kopyası korunur;
genel unsafe-inline izni veya yeni CSP istisnası eklenmez. Ayrı gerçek Chrome
`document_persistence_browser_probe.cjs` masaüstü/mobil409, klavye/focus,
input/kurtarma korunması ve sıfır normal CSP/runtime hatasını sınar.

## Etkinleşme ve yanıt sınırı

`SecurityHeadersMiddleware` koşulsuz kullanılır; kapatma/report-only configuration
anahtarı yoktur. Eski `Security:Csp:ReportOnlyEnabled` setting’i kaldırıldı.
HTML 200/404/500 yanıtında OnStarting ile enforcing policy yazılır; Response.Clear
başlıkları kaybettirmez. JS/CSS/JSON/XML/RSS’ye HTML politikası yazılmaz.
Uygulama hattındaki yanıtlar ayrıca `X-Content-Type-Options: nosniff`,
`Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY` taşır.
HTML’de frame-ancestors none da uygulanır. Framework startup Host filtresinin
uygulamaya ulaşmadan verdiği boş 400 bu başlık hattının dışındadır; host reddi korunur.

HSTS F50’deki yerleşik UseHsts üzerinden tek yerde, Development dışında HTTPS’te
30 gün olarak yönetilir; Nginx’e ikinci HSTS/CSP kopyası konmaz. Gerçek Production
500/TLS fixture’ında HSTS ve CSP birlikte doğrulandı. Public output cache ve HEAD
aynı statik policy’yi taşır. Nonce gerekmez; header/body eşleşmesi değişmez.

Public report endpoint’i, üçüncü taraf raporlama ve veri toplama eklenmedi.
Gerçek sunucuya dağıtım yapılmadı. Geri dönüş report-only’a düşerse engelleme
kaybı açıkça kaydedilmelidir; encoding/renderer düzeltmeleri geri alınmaz.

## Gerçek kaynak envanteri

| Sınır | Mevcut kullanım | Uygulanan izin |
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
F51/F52 dependency güvenlik sertifikası veya sanitizer sürüm yükseltmesi değildir.

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
sözleşmesi korunur. Şema/canlı veri değişmez. F52’de aynı politika enforce edilerek 27 normal ziyaret
ve 36 browser kontrolü geçti; normal CSP ihlali sıfırdı. Kasıtlı inline script,
event handler, eval, dış script ve base injection engellendi. Eval kontrolü
automation evaluate yerine browser fixture’ının yerelden yüklediği script içinde
çalışır; automation kanalının CSP bypass’ı koruma başarısı sayılmaz.
F52 kanıtı header/cache/error ve Production
regresyonunu açıklar.

Kaynaklar: [OWASP CSP](https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html),
[MDN style-src-attr ve CSSOM farkı](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/style-src-attr).

Başlık kaynakları: [MDN Referrer-Policy](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Referrer-Policy), [ASP.NET Core 10.0.10 HSTS middleware](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Middleware/HttpsPolicy/src/HstsMiddleware.cs).

## JSON yazım formu (F09)

AdminWriting Create/Edit yanıtları200/400/413/409 durumlarında static enforcing
`style-src-attr 'none'` politikasını korur. Yerel Tiptap ESM injectCSS:false ile
başlar; hizalama sabit CSS class'ı kullanır. Server-invalid JSON editöre verilmez;
Razor-encoded textarea korunur. Hata yokken validation summary üretilmez:
MVC'nin otomatik gizli `<li style="display:none">` çıktısı ilk browser kabulünde
CSP ihlali oluşturdu; koşullu summary ile giderildi. Middleware politika izinleri
ve eski Toast UI200 Create/Edit istisnası değişmedi. Klavye, mobil, bütün temel
biçimler ve400/409 editörleri gerçek Chrome'da aynı sınırlarda sınanır.

JSON yazım formundaki tablolar exact Tiptap Table API'sinin static renderHTML
uyarlamasıyla gösterilir; varsayılan dinamik genişlikli TableView devreye girmez.
View:null/resizable:false, static hücre hizalama sınıfları ve yerel CSS kullanılır.
Tablo ekleme/silme, paste, seçim ve kayıt akışı için yeni CSP izni eklenmez;
AdminWriting200/400/413/409 yanıtlarının style-src-attr:none sınırı korunur.
