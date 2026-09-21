# DevCoreBlog — Teknik inceleme ve geliştirme raporu

Tarih: 19 Eylül 2026. İncelenen sürüm: çalışma dizininin mevcut durumu; commit edilmemiş değişiklikler ve yeni webhook/besleme dosyaları dahil.

Güncellik notu — 21 Eylül 2026: Agent kuralları [kök AGENTS.md](../AGENTS.md) altında yenilendi. Bu rapordaki eski kural çelişkileri tarihsel bulgulardır; [önceki kurallar](arsiv/agent-talimatlari-2026-09-21/AGENTS_LEGACY.md) arşivdedir. Uygulama güvenliği ve 56/100 SOLID puanı kod değişmeden yeniden değerlendirilmiş değildir.

## 1. Kısa raporu

DevCoreBlog, geliştirilmeye değer bir ASP.NET Core MVC blog temeline sahip. Yazı/kategori yönetimi, Markdown editörü, görsel yükleme, arama, sayfalama, koyu tema, sitemap ve otomasyon entegrasyonu mevcut. Yeniden yazılması gerekmiyor. Buna karşılık güvenli yayınlama, veri bütünlüğü, mobil yönetim, erişilebilirlik ve işletim güvencesi tamamlanmadan üretime hazır kabul edilmemeli.

**SOLID değerlendirmem: 56/100.** Bu, aşağıdaki beş ilkeye eşit ağırlık veren uzman değerlendirmesidir; otomatik araç çıktısı veya matematiksel bir kalite garantisi değildir.

En önemli bulgular:

1. Yönetici kullanıcı adı tanımlıyken parola ortam değişkeni eksikse, parola gönderilmeden oturum açılabiliyor. Bu belirli yanlış yapılandırmaya bağlı bir kimlik doğrulama açığıdır; mevcut gerçek ortamın bu durumda olduğunu iddia etmiyorum.
2. Markdown içeriğinde ve yönetici bildirimlerinde iki ayrı XSS yolu var. Her ikisi de zararsız test işaretleriyle tarayıcıda doğrulandı.
3. Geleceğe planlanan yazılar henüz zamanı gelmeden ziyaretçilere açılıyor.
4. Bazı cookie ile çalışan POST işlemleri CSRF tokenı doğrulamıyor.
5. Kapak dosyası seçmeden yazı kaydetmek, dosya yanlışlıkla zorunlu kabul edildiğinden başarısız oluyor; hata formda görünmüyor.
6. Mobil yönetim panelinde sabit yan menü, 390 px ekranda ana alanı 166 px'e indiriyor.
7. Cache temizleme, slug yönetimi, migration keşfi ve eşzamanlı güncelleme davranışları düzeltilmeli.

**Öneri: 10 iş paketinde olgun bir tek yazarlı blog.** İlk dört paket güvenlik ve doğruluk; sonraki paketler mimari, editör, okuma deneyimi, SEO, performans ve işletim. Tahmini 25–40 net geliştirici günü; içerik üretimi, kapsam genişlemesi ve harici servis/altyapı beklemeleri hariç. Bu bir iş kırılımı tahminidir, teslim tarihi taahhüdü değildir.

## 2. İnceleme kapsamı ve kanıt düzeyi

İncelenen yapı: 4 proje dosyası; 8 controller; 22 Razor görünümü; Core, Data ve Services katmanları; middleware; tüm uygulama CSS/JS dosyaları; 5 migration ve model snapshot; başlangıç/configuration; README ve depo kuralları. Uygulama/migration kaynakları yaklaşık 8.152 satır, 63 dosya. Üçüncü taraf kütüphane kaynakları uygulama kodu olarak değerlendirilmedi.

Uygulama kaynakları değiştirilmedi. Kaydedilmemiş kullanıcı değişiklikleri korundu. Derleme ve davranış kontrolleri geçici bir kopyada yapıldı. Gerçek .env ve cookies.txt içerikleri test ortamına taşınmadı; gerçek PostgreSQL ve Cloudinary verileri kullanılmadı. Ayrı PostgreSQL örneği ve sahte içerik oluşturuldu. Test şeması model yapısına göre hazırlandı; migration zincirinin uygulanması bu testin parçası değildi.

Doğrulamalar:

| Kontrol | Sonuç |
|---|---|
| .NET 10 derlemesi | Başarılı: 0 hata, 0 uyarı |
| NuGet doğrudan/geçişli paket taraması | Resmi NuGet kaynağına erişen taramada bilinen açık bildirilmedi |
| Normal taslak yazı, anonim detay isteği | 404; bu temel kontrol çalışıyor |
| Gelecek tarihli yazı | Ana sayfa, detay, arama, sitemap ve dış beslemede erken göründü |
| Kullanıcı adı var, ADMIN_PASSWORD yok, parola alanı gönderilmemiş | 302 yönetim yönlendirmesi ve auth cookie üretildi |
| Token olmadan giriş/çıkış/yayın değişikliği | İstekler kabul edildi; yayın değişikliği 200 döndü |
| Markdown XSS | İçerik içindeki olay işleyicisi tarayıcıda test işaretini değiştirdi |
| Yönetim bildirimi XSS | Webhook ile gelen başlık, yayın düğmesine basılınca bildirim üzerinden çalıştı |
| Kapak dosyası olmadan Create | Kaydetmedi; log: “The thumbnailFile field is required.” |
| Yayından kaldırma sonrası önbellek | Önceden ısıtılan detay sayfası anonim isteğe hâlâ 200 döndü |
| Sayfa numarası 0 | PostgreSQL negatif OFFSET hatası ve 302 hata sayfası yönlendirmesi |
| Mobil yönetim, 390 px genişlik | Yan menü 224 px, ana alan 166 px |
| Editör tarayıcı konsolu | İki adet “jQuery is not defined” hatası |
| Public tarayıcı konsolu | Tailwind Play CDN üretim uyarısı; Prism Toolbar bağımlılık uyarısı |
| EF migration keşfi | Mevcut yapılandırmayla “No migrations were found.”; tools/runtime patch sürümü uyarısı |

Bu çalışma bir kaynak kodu ve yerel davranış incelemesidir. Üretim sunucusu, TLS/reverse proxy, gerçek yedekler, gerçek veri miktarı, trafik altında yük testi ve internetten saldırı testi yapılmadı. Paket taramasının temiz olması uygulama mantığının güvenli olduğunu göstermez. Lighthouse/Core Web Vitals skorları ölçülmedi.

## 3. Mevcut temel ve güçlü taraflar

- MVC/Razor ile sunucu tarafı render, blog için uygun bir başlangıç. JavaScript zorunluluğunu sınırlamak ve arama motorlarına içerik sunmak mümkün.
- Controller → Service → Repository ayrımı yazı/kategori işlemlerinin çoğunda uygulanmış. Controller’lar servis arayüzlerini kullanıyor.
- EF Core LINQ sorguları kullanılıyor; incelenen kodda birleştirilmiş ham SQL üzerinden doğrulanmış bir SQL injection yolu bulmadım.
- Yönetim controller’larında Authorize mevcut; temel yazı/kategori CRUD formları antiforgery doğruluyor.
- UpdatePostAsync mevcut kaydı yükleyip izin verilen alanları atıyor; CreatedDate ve ViewCount gibi alanlar formdan doğrudan güncellenmiyor.
- Webhook’ta secret kontrolü, sabit zamanlı karşılaştırma, kategori varlık kontrolü ve rate limit bulunuyor. Varsayılan yayın durumu taslak.
- Cloudinary entegrasyonu IImageService arkasında; taşınabilirlik için bir başlangıç var.
- Public listelemede sayfalama, Markdown kod renklendirme, koyu tema ve mobil ziyaretçi menüsü mevcut.

Bu altyapıyı geliştirmek için SPA dönüşümü, mikroservis veya kapsamlı bir framework değişimi gerekmiyor.

## 4. Güvenlik bulguları ve düzeltmeleri

Öncelik tanımları: P0 yayına çıkmadan kapatılmalı; P1 ilk düzeltme grubunda ele alınmalı; P2 sağlamlaştırma ve sürdürülebilirlik işi. Bunlar CVSS puanı değildir.

### G1 — Eksik parola yapılandırmasında giriş açılıyor — P0, doğrulandı

Kanıt: AccountController.cs:69–101. ADMIN_USERNAME tanımlı, ADMIN_PASSWORD eksik olduğunda gönderilmeyen password parametresi ile beklenen parola null oluyor. Eşitlik kontrolü geçiyor. ModelState kontrol edilmiyor. Giriş formundaki required özelliği doğrudan HTTP isteğini engellemiyor.

Düzeltme: Başlangıçta kullanıcı adı ve parola doğrulama yapılandırmasını kontrol edip eksik/boşsa uygulamayı güvenli biçimde durdurmak. Login içinde de boş girişleri reddetmek. Başlangıç doğrulamasına güvenip istek doğrulamasını kaldırmamak. Cookie auth korunabilir; ASP.NET Core Identity zorunlu değil.

Kabul testi: Eksik/boş kullanıcı adı veya parola ayarı uygulamayı başlatmamalı; boş parola isteği hiçbir koşulda auth cookie üretmemeli.

### G2 — Saklanan Markdown üzerinden XSS — P0, doğrulandı

Kanıt: MarkdownHelper.cs:41–69; Views/Home/Detail.cshtml:50. Markdig HTML çıktısı sanitize edilmeden Html.Raw ile yazılıyor. Webhook da içerik alabildiğinden “yalnızca admin yazar, güvenilir” varsayımı yeterli değil. Testte zararsız bir img/onerror işaretçisi çalıştı.

Önkoşul: Saldırganın içerik yazabilecek admin/otomasyon yetkisini veya o içerik akışını kontrol etmesi gerekir. Anonim ziyaretçinin doğrudan yazı ekleyebildiği saptanmadı. Sonuç, içeriği açan ziyaretçi veya yönetici tarayıcısında aynı origin altında kod çalışmasıdır.

Düzeltme: Markdown dönüşümünden sonra izin listeli HTML temizleme. Script, on* olay öznitelikleri ve tehlikeli URL protokollerini kaldırma. Video için yalnızca onaylanan YouTube adresleri/öznitelikleri. Alternatif olarak ham HTML'yi devre dışı bırakıp gerekli embed çıktısını kontrollü üretmek; yalnızca ham HTML kapatmanın bütün gelişmiş eklenti/öznitelik yollarını kapattığı varsayılmamalı. CSP ek savunmadır, temizleme yerine geçmez. [OWASP XSS rehberi](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)

Kabul testi: Yazı, webhook ve önizleme akışlarında aynı içerik güvenlik politikası; olay işleyicileri çalışmamalı, normal Markdown ve izin verilen videolar bozulmamalı.

### G3 — Yönetici bildiriminde başlık üzerinden XSS — P0, doğrulandı

Kanıt: AdminPostController.cs:93–98; Views/AdminPost/Index.cshtml:240–276; Views/Shared/_AdminLayout.cshtml:336–344. Başlık JSON mesajına ekleniyor, mesaj template string içinde innerHTML'e yerleştiriliyor. JSON escaping bu aşamada HTML temizleme sağlamaz.

Önkoşul: Kötü niyetli bir başlığın kayıt edilmesi ve yöneticinin ilgili yayın düğmesine basması. Webhook ile oluşturulan test başlığı bu akışı doğruladı. Yazı taslak olsa bile yönetici işlemi riski tetikleyebilir.

Düzeltme: Bildirim iskeletini DOM ile oluşturmak; mesajı textContent üzerinden atamak. HTML mesajlarına ihtiyaç yok. Razor'ın normal metin encoding davranışını korumak.

### G4 — Cookie kullanan POST işlemlerinde CSRF doğrulama boşlukları — P1, doğrulandı

Kanıt: AccountController.cs:69,119; AdminPostController.cs:80,266,295. Login, Logout, TogglePublish, UploadEditorImage ve UploadImage token doğrulamıyor. Yönetim giriş/çıkış ve toggle için tokensız istek kabulü doğrulandı; dosya uçları koddan tespit edildi.

Düzeltme: MVC tarafında genel AutoValidateAntiforgeryToken politikası veya bütün ilgili action’larda tutarlı doğrulama. Fetch/FormData isteklerinde token gönderimi. Secret header ile çalışan mevcut webhook, cookie tabanlı form korumasından bilinçli ayrılmalı; bu uçta kimlik doğrulama ve tekrar istek koruması uygulanmalı. [Microsoft antiforgery rehberi](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

SameSite=Lax bazı klasik siteler arası POST saldırılarını azaltır; tokensız isteğin kabul edilmesi her tarayıcıda her kaynaktan saldırının başarılı olduğu anlamına gelmez. Aynı site kapsamındaki farklı origin'ler ve değişen cookie/dağıtım ayarları nedeniyle uygulama doğrulaması yine gereklidir.

### G5 — Planlanan içerik erken açığa çıkıyor — P1, doğrulandı

Kanıt: PostRepository.cs:47,63,85,98,109,129,156; PublicFeedController.cs:45–49. IsActive ve IsPublished kontrol ediliyor, PublishDate <= now kontrol edilmiyor. Sitemap de aynı eksik sorguyu kullanıyor.

Düzeltme: Tek bir yayın görünürlüğü kuralı: aktif kayıt + yayın izni + yayın zamanı gelmiş + kategori aktifliği politikası. Bu kural detay/liste/arama/ilişkili içerik/sitemap/besleme boyunca uygulanmalı. Zaman için UTC ve test edilebilir TimeProvider kullanılmalı. Yönetim ekranında Taslak, Planlandı, Yayında ve Arşiv ayrımı yapılmalı.

Tarih filtresi eklendiğinde cache'in de yayın anında yenilenmesi gerekir; mevcut 10 dakikalık veri cache'i aksi halde planlı yayını geciktirebilir.

### G6 — Giriş denemeleri ve parola saklama zayıflığı — P1, koddan tespit

Kanıt: AccountController.cs:72–77; Program.cs:86–114. Parola düz metin olarak ortam değişkeninden okunup karşılaştırılıyor. Login'e rate limit, kademeli bekleme veya deneme kısıtı uygulanmıyor. Mevcut rate limit yalnızca webhook ve public besleme üzerinde.

Düzeltme: Cookie auth ile devam ederek salt içeren güvenli parola hash doğrulaması kullanmak; ham parolayı kalıcı configuration'da tutmamak. Kullanıcı/IP bazlı kademeli sınırlama ve genel koruma; dağıtık kurulumda ortak sayaç. Hatalı giriş mesajları genel kalmalı. Eksik ayarlarda kapalı davranış ve hassas veri içermeyen güvenlik logları eklenmeli. Parola değişikliğinin mevcut oturumları da geçersiz kılacağı mekanizma tanımlanmalı. [OWASP parola saklama](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html), [kimlik doğrulama](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)

### G7 — Dosya yükleme doğrulaması tutarsız — P1/P2, koddan tespit

Kanıt: AdminPostController.cs:143–150,224–231,274–278,303–307; ImageService.cs:29–48. Editör yüklemelerinde yalnızca uzantı kontrolü var. Kapak yükleme akışı aynı uzantı kontrolünü de kullanmıyor. Uygulama seviyesinde içerik imzası, boyut, çözünürlük ve kota politikası yok.

Düzeltme: Bütün yüklemeleri aynı servis doğrulamasına bağlamak. İzin verilen biçim + gerçek içerik/decoding + boyut ve piksel sınırı; belirgin kullanıcı hatası. Dosyayı güvenli biçimde yeniden kodlama değerlendirilmeli. SVG, ayrı güvenlik politikası olmadan kabul edilmemeli. Upload yetkisi, antiforgery ve kota korunmalı. Cloudinary'nin yaptığı doğrulama, uygulama politikasının yerine geçirilmemeli. Burada doğrulanmış sunucu tarafı kod çalıştırma açığı saptanmadı. [OWASP dosya yükleme rehberi](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)

### G8 — Yayından kaldırma önbelleğe yansımıyor — P1, doğrulandı

Kanıt: HomeController.cs:60,86,146; PostService.cs:62–66,114–122. Yazı değişikliği yalnızca veri cache tokenını yeniliyor. 60 saniyelik çıktı cache'i temizlenmiyor. Testte yayından kaldırılan yazının önceden cache'lenmiş detayına anonim 200 yanıtı sürdü. Dış beslemenin 300 saniyelik public Cache-Control başlığı da ayrı bir tazelik sözleşmesi gerektiriyor.

Düzeltme: İlgili output cache kayıtlarını etiketleyip yayın/değişiklik/silme anında geçersiz kılmak. Kategori değişikliklerini de dahil etmek. Geri çekilen içeriğin erişim politikası açık olmalı. Program.cs:187–194 içinde UseOutputCache, authentication/authorization sonrasına alınmalı. Mevcut sıradan hareketle özel admin sayfasının sızdığı iddia edilmiyor; admin uçlarında OutputCache bulunmuyor. [Microsoft output cache rehberi](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0)

### G9 — Webhook tekrarları ve kaynak tüketimi — P2, koddan tespit

Secret kontrolü ve rate limit olumlu. Ancak idempotency anahtarı yok; aynı otomasyon isteğinin tekrar gelmesi aynı içerikten birden fazla yazı üretebilir. Alan bazlı uzunluk/iş kuralı sınırları eksik. IsPublished=true gelen otomasyon doğrudan yayına izin alıyor; “her zaman taslak” garantisi yok.

Düzeltme: İstek kimliği üzerinde unique kontrolü, zaman aşımı/tekrar politikası ve sınırlandırılmış alanlar. Editoryal amaç taslak üretmekse sunucu tarafında taslağa zorlamak. Otomatik yayın isteniyorsa ayrı ve açık bir yetki/politika. Gerekirse zaman damgalı HMAC; sadece HMAC'in replay'i tek başına çözmediği unutulmamalı. CORS, webhook veya besleme için kimlik doğrulama değildir.

### G10 — Dağıtım ve depo sağlamlaştırması — P2, kısmen ortama bağlı

- cookies.txt Git tarafından izleniyor ve cookie kaydı içeriyor. Değeri rapora alınmadı, geçerliliği denenmedi. İzlemeyi kaldırmak ve ignore etmek; gerçek oturum verisiyse oturumu geçersiz kılmak, geçmişteki erişimi değerlendirmek gerekir.
- .env Git tarafından izlenmiyor; bu olumlu. Geçmiş commitlerde hiç secret bulunmadığına ilişkin tam geçmiş taraması yapılmadı.
- appsettings.json AllowedHosts=*; sitemap ve public linkler Request.Host üzerinden üretiliyor. Sabit doğrulanmış SiteUrl ve kabul edilen host listesi kullanılmalı. Üretim ingress'inin ek korumaları incelenmediği için internette exploit edildiği iddia edilmiyor.
- Güvenilen proxy/forwarded headers yapılandırması görünmüyor. Proxy arkasında HTTPS ve gerçek istemci IP'si doğru anlaşılmalı; yalnızca güvenilen proxy'ler kabul edilmeli.
- Cookie SecurePolicy, oturum süresi ve yenileme politikası açıkça belirlenmeli. Framework'ün varsayılan HttpOnly/SameSite korumaları yokmuş gibi değerlendirilmemeli. Data Protection anahtarları üretimde kalıcı ve korumalı yönetilmeli.
- CSP ve ilgili response-header politikaları uygulama kodunda yok; reverse proxy tarafında bulunup bulunmadığı bilinmiyor. Inline script ve event handler azaltılarak nonce/hash tabanlı CSP uygulanabilir.
- Toast UI Editor latest URL'si kullanıyor; sürümü sabitlemek ve mümkünse uygulamayla paketlemek gerekir. Harici dosyalar için uygun olduğunda SRI. Bu bir bağımlılık kontrolü sorunudur; o editör sürümünde doğrulanmış CVE iddiası değildir.

## 5. İşlevsel doğruluk ve veri bütünlüğü

### F1 — Opsiyonel kapak dosyası zorunlu davranıyor

Create/Edit parametresi IFormFile thumbnailFile nullable değil. MVC eksik dosyayı validation hatasına dönüştürüyor. ModelOnly özeti property hatasını göstermiyor ve thumbnailFile için alan hata göstergesi yok. Create üzerinde doğrulandı; Edit aynı imzaya sahip.

Düzeltme: Opsiyonel IFormFile? kullanmak; ThumbnailUrl/Summary/Excerpt gibi alanların gerçekten zorunlu olup olmadığını açık tanımlamak; server-side validation ile UI beklentisini eşlemek. Editor yüklenemezse içeriğin düzenlenebilir textarea alternatifi olmalı.

### F2 — Slug çakışması ve değişen kalıcı adresler

Create aynı başlık için aynı slug üretir. Veritabanında unique slug indeksi yok. Detay sorgusu FirstOrDefault kullandığı için çakışma yazılardan birini erişilemez veya belirsiz hale getirir. UpdatePostAsync her güncellemede slug'ı yeniden üretir; başlık değişince eski URL kırılır. Kategori için aynı sorun var.

Düzeltme: Benzersiz slug indeksi + çakışmada deterministik ek üretimi; eşzamanlı insert çakışmasını DB hatasından doğru ele alma. Yayınlanmış slug'ı kararlı tutmak veya eski→yeni 301 yönlendirme kaydı. Boş/yalnızca sembol başlıkların boş slug üretmesini engellemek.

### F3 — Sayaç güncellemesi bütün yazıyı güncelliyor

IncrementViewCountAsync get → artır → Update(entity) → Save yapıyor. Eşzamanlı istekler artış kaybedebilir. GenericRepository.Update bütün entity'yi modified işaretlediğinden, eski değerlerle eşzamanlı bir içerik düzenlemesini ezme riski de var. Output cache hit'lerinde action çalışmadığı için sayaç her okuyucuyu saymaz.

Düzeltme: Sayaç için yalnızca ilgili sütunu atomik artırmak. Düzenlemelerde concurrency token ile çakışma uyarısı. Okunma ölçümünü sayfa render/cache stratejisinden ayırmak; admin ziyaretleri, botlar ve tekrarlı yüklemeler için ölçüm tanımını belirlemek. Mevcut sayaç gerçek tekil ziyaretçi metriği değildir.

### F4 — Kategori güncellemesi korunması gereken alanları sıfırlayabiliyor

AdminCategoryController sınırlı alanlarla yeni Category nesnesi bind ediyor; CategoryService bunu bütünüyle Update ediyor. IsActive varsayılan true ve CreatedDate varsayılan şimdi olduğundan mevcut pasiflik/tarih korunmuyor. Bind listesindeki Description modelde yok.

Düzeltme: Mevcut kategoriyi getirip yalnızca düzenlenebilir alanları atamak. Description ya modele kontrollü eklenmeli ya kullanılmayan binding kaldırılmalı. Aktiflik filtresi public menü ve kategori sorgularında da uygulanmalı.

### F5 — Kategori silme koruması yalnızca uygulamada

Servis “yazısı varsa silme” kontrolü yapıyor; DB yabancı anahtarı Cascade. Kontrol ile silme arasındaki eşzamanlı ekleme veya servis dışı silme, makalelerin kaybına yol açabilir.

Düzeltme: Ürün kuralı kategori altında yazı varsa silinmemesiyse DB'de Restrict/NoAction, uygun transaction ve anlaşılır hata. Yazılarda çöp kutusu/geri alma iş akışı ayrı tasarlanmalı; IsActive bulunması DeleteAsync'in hard delete yaptığı gerçeğini değiştirmiyor.

### F6 — Migration yerleşimi çalıştırma ayarıyla uyuşmuyor

DbContext Data projesinde, migration dosyaları Web projesinde. UseNpgsql açık MigrationsAssembly tanımlamıyor. Geçici kopyada EF keşfi “No migrations were found.” döndürdü. Test DB'sinde history tablosunun bulunmaması ayrıca beklendik bir uyarıydı; migration dosyalarının hangi assembly'de keşfedildiği ayrı sorundur.

Düzeltme: Migration dosyalarını/bağımlılıklarını tutarlı projeye taşımak veya migration assembly'sini açıkça Web assembly'si olarak ayarlamak. README'deki startup/project yollarını gerçek csproj konumlarına göre düzeltmek. EF runtime/design/tools patch sürümlerini uyumlu hale getirmek. Boş DB kurulumu ve mevcut DB yükseltmesini ayrı test etmek.

Geçmiş migration'larda IsPublished → IsActive yeniden adlandırması ve sonra yeni IsPublished=false eklenmesi var. Eski sürümden yükselen içerik için veri dönüştürme planı gerekli; geçmiş migration'ları gelişigüzel değiştirmek yerine yeni migration ile düzeltmek gerekir.

### F7 — Hata işleme doğru durum kodunu korumuyor

ExceptionHandlingMiddleware.cs:32 exception'ı /Home/Error'a redirect ediyor. Hata 500 yerine 302, ardından 200'e dönüşebilir; API istemcisi JSON beklerken HTML alabilir. Production UseExceptionHandler ile de iki ayrı hata mekanizması var. Error görünümü public layout üzerinden veritabanı sorguladığından DB arızasında hata sayfası da hata üretebilir.

Düzeltme: Tek sorumlu hata mekanizması; MVC için DB'den bağımsız 500 görünümü, mevcut JSON uçları için uygun hata gövdesi/durum kodu. 404 görünümü ayrı. Kullanıcıya ayrıntılı exception göstermeden izleme kimliği sunmak.

### F8 — Form doğrulaması, sıralama ve zaman dilimi

Modelde explicit uzunluk, URL, kategori ve içerik iş kuralı doğrulamaları zayıf. MVC'nin nullable olmayan alanlar için implicit required davranışı iş kurallarının yerine geçmez; webhook servis çağrıları MVC entity validation'dan geçmiyor. page parametresi sınırlandırılmadığı için 0 hataya yol açıyor. Eşit CreatedDate değerleri için ikincil sıralama yok. Tarih dönüşümü server local saat dilimine dayanıyor.

Düzeltme: Servis sınırında ortak doğrulama; page>=1 ve pageSize üst sınırı, kararlı ThenBy(Id) sıralaması. Kullanıcının seçtiği yayın zamanını açık site saat diliminden UTC'ye dönüştürmek. Testleri farklı sunucu saat dilimleriyle çalıştırmak.

## 6. SOLID puanı: 56/100

| İlke | Puan | Gerekçe | Puanı yükselten somut değişiklik |
|---|---:|---|---|
| S — Tek sorumluluk | 10/20 | PostService CRUD, slug, saat, cache, sayaç yönetiyor. Razor DB sorguluyor. Editör view'ları HTML + JS + upload + autosave içeriyor. | Ortak editör modülü; sayfa bileşenleri; yayın/görünürlük kuralı; dashboard ve navigasyon sorgularını ayırma. |
| O — Açık/kapalı | 11/20 | Servis ve görsel arayüzleri olumlu; somut repository, doğrudan Cloudinary inşası ve sabit yardımcılar değişimleri yayıyor. | Gereken değişim noktalarında repository/storage arayüzleri, DI ile yapılandırılmış client ve cache politikası. |
| L — Yerine geçebilme | 17/20 | GenericRepository alt sınıflarında açıkça kanıtlanmış bir sözleşme ihlali yok. Geniş polymorphism ve sözleşme testi de yok. | Repository null/bulunamadı/update/delete sözleşmelerini netleştirip entegrasyon testleriyle koruma. |
| I — Arayüz ayrımı | 12/20 | IPostService public okuma, admin yazma ve sayaç işlemlerini birlikte sunuyor. Her consumer gereğinden fazla yetenek görüyor. | Küçük okuma/yönetim sözleşmeleri; gereksiz tek-metot interface çoğaltmadan tüketici ihtiyaçlarına göre ayırma. |
| D — Bağımlılıkların ters çevrilmesi | 6/20 | Controller'ların çoğu arayüz kullanıyor; servisler concrete repository kullanıyor. AdminController ve _Layout doğrudan DbContext'e bağlı. | IPostRepository/ICategoryRepository; composition root dışında concrete erişimi azaltma; view içinde DbContext kullanmama. |
| **Toplam** | **56/100** | Katmanlar var, sınırlar tutarlı uygulanmıyor. | Uygulama sonrası yeniden incelemeyle **80–85 bandı** makul hedef. |

Interface eklemek tek başına SOLID değildir. Markdig'in Core'da bulunması tek başına bütün SOLID'i ihlal etmez; ancak Core'un dış kütüphanelerden bağımsız olduğu README iddiası yanlıştır ve render altyapısını domain'den ayırmak sınırları temizler. Async adı taşıyan UpdateAsync'in bellek içi değişiklik işaretlemesi tek başına LSP ihlali değildir; yanıltıcı API isimlendirmesi olarak sadeleştirilebilir.

Önerilen bağımlılık akışı: MVC action veya Razor ViewComponent → ilgili servis sözleşmesi → repository sözleşmesi → EF Core implementasyonu. Cloudinary ve HTML renderer altyapı ayrıntıları ayrı tutulmalı. Services'in ASP.NET IFormFile bağımlılığı gerektiğinde Stream/dosya bilgisi sınırına indirilebilir.

Depo kurallarındaki MVC, cookie auth ve entity döndürme yaklaşımı korunarak bu iyileştirmeler yapılabilir. AutoMapper, DTO ağı veya mikroservis zorunlu değil. Mevcut AGENTS.md tek proje/API yok diyor; gerçek kod 4 proje ve API controller'ları içeriyor. Gerçek kararı yansıtacak şekilde mimari kuralları güncellemek bakım için gerekli.

## 7. Spagetti ve modern olmayan kod değerlendirmesi

Projenin tamamı spagetti değil. C# iş akışları çoğunlukla takip edilebilir. Ancak aşağıdaki alanlarda değişikliklerin birbirine dolanma riski yüksek:

| Alan | Mevcut sorun | Önerilen düzenleme |
|---|---|---|
| Create 494 / Edit 409 satırlık view'lar | Editör, yükleme, video, metrik ve slug JS'i tekrarlanıyor. | Ortak form partial'ı + tek post-editor.js; sayfaya özgü başlangıç verileri. |
| _Layout 307 / _AdminLayout 378 satır | Sorgu, tema, sidebar, CSS/JS ve bildirim sorumlulukları karışık. | Navigasyon ViewComponent; ortak varlıklar; ayrı bildirim/tema modülü. |
| Kartlar | Ana sayfa/kategori/arama markup'ı tekrarlanıyor. | Tek yazı kartı partial'ı ve sınırlı varyantlar. |
| CSS yapılandırması | Inline Tailwind ayarları, ayrı kullanılmayan config ve global !important kuralları var. | Tek tasarım token kaynağı; derlenen CSS; bileşen kapsamlı kurallar. |
| site.js | Mevcut sayfada bulunmayan carousel elemanlarını arayan eski kod. | Kullanılmayan özellikleri kaldırma; gerekli JS'i ilgili sayfada yükleme. |
| Yükleme action'ları | İki farklı endpoint, benzer mantık, tutarsız validation/yanıt. | Mevcut istemcileri dikkate alarak ortak iç işlem ve açık uyumluluk kararı. |
| ViewBag | Dashboard, SEO, ilgili içerik ve sayfalama için runtime'a bağımlı sözleşme. | Küçük ve açık sayfa sözleşmeleri veya ViewComponent; servis entity sözleşmesini koruma. |
| Yorumlar ve README | Çok sayıda yorum kodu tekrar ediyor; bazıları davranışı yanlış anlatıyor. | “Ne yaptığı” yerine karmaşık kararda “neden”i açıklamak; yanlış güvenlik garantilerini düzeltmek. |

.NET 10, EF Core, async/await, nullable ve DI kullanımı modern. Razor MVC ve vanilla JS kullanmak eski teknoloji sorunu değil. Üretim için sorun olan örnekler: Tailwind Play CDN, sürümü latest bırakılmış editör, view içinde senkron DB sorgusu, eksik cancellation desteği, frontend bağımlılıklarının düzenli build/test sürecinin olmaması. Tailwind'in resmi belgesi Play CDN'i geliştirme amaçlı tanımlar. [Tailwind Play CDN](https://tailwindcss.com/docs/installation/play-cdn)

## 8. UI/UX nasıl geliştirilmeli?

### Okuyucu deneyimi

Mevcut teknik/minimal kimlik korunabilir. En büyük ihtiyaç daha fazla dekor değil; içeriğe ulaşma ve uzun yazı okuma kolaylığı.

1. **Ana sayfanın odağı:** Kısa bir site açıklaması/h1, bir seçili yazı ve okunaklı son yazılar akışı. İki yan sütun içerik alanını daraltıyor. Sağ sütundaki statik kutuları azaltmak; “öne çıkan” seçimini gerçek editoryal tercih yapmak. Her sayfada ilk üç kaydı tekrar hero olarak kullanmamak.
2. **Gerçek keşif:** Trending yalnızca mevcut sayfadaki 9 kayıttan hesaplanıyor; tüm site popülerliği değil. Bağımsız, zaman aralığı tanımlı sorgu. Filtre/etiket iddiaları gerçek veri modeliyle eşleşmeli; bugün arama placeholder'ındaki tags için bir etiket modeli yok.
3. **Yazı sayfası:** Yaklaşık 65–75 karakterlik satır genişliği, 17–19 px gövde hedefi, rahat satır aralığı. Mevcut paragraf boyutu masaüstünde iyi bir başlangıç. İçindekiler, başlık bağlantıları, kod kopyalama, kategori bağlantısı, önceki/sonraki yazı ve gerçekten gösterilen ilgili yazılar. HomeController ilgili yazıları getiriyor fakat Detail view kullanmıyor.
4. **Kimlik ve güven:** Yazar bilgisi, hakkında ve iletişim sayfaları; gerçek güncelleme tarihi. Okuyucuya gerekli politika metinleri yalnızca kullanılan veri toplama/abonelik süreçlerine göre hazırlanmalı.
5. **Arama:** Her public sayfadan erişilen alan, sorguyu sonuç ekranında değiştirebilme, sonuç sayısı, kategori filtresi ve sayfalama. Boş sonuçta somut alternatifler. Arama motoru ilk aşamada PostgreSQL ile yeterli olabilir.
6. **Tutarlı navigasyon:** Veritabanında bulunmayan kategori slug'larına sabit link vermemek. Aynı kategori hem sabit menüde hem dinamik listede yinelenmemeli. Kategori başlığındaki yazı sayısı mevcut sayfa kaydı değil toplam olmalı.
7. **Dil:** İngilizce etiketler, Türkçe tarih kültürü ve bazı Türkçe hata mesajları karışık. Önce birincil dil kararı; görünür metinler ve html lang buna uymalı. Çok dillilik gerekiyorsa ayrı kapsam.
8. **Görseller:** Kartlara responsive boyutlar, genişlik/yükseklik veya aspect ratio, ekran altındakilere lazy loading; ana görseli geciktirmeyen yükleme politikası. Cloudinary dönüşümleriyle uygun format/boyut. Mevcut URL'yi CDN'e taşımak tek başına optimizasyon değil.

### Mobil ve erişilebilirlik

- Admin sidebar mobilde drawer olmalı; yazı listesi dar ekranda kart/özet sunmalı; kaydetme erişilebilir bir konumda kalmalı. Doğrulanan 166 px ana alan mevcut paneli pratikte kullanışsız yapıyor.
- Public drawer'ın ekrandan transform ile taşınması klavye/ekran okuyucudan gizlenmesi anlamına gelmiyor. Kapalı içerik için inert/uygun gizleme; açıkken odak yönetimi, Escape, kapanınca odağın düğmeye dönmesi; aria-expanded ve aria-controls.
- Arama alanına erişilebilir label, ana içeriğe atlama bağlantısı, mantıklı başlık sırası ve belirgin focus-visible stilleri. focus:outline-none yerine görünür alternatif sağlanmalı.
- Metin ve eylem boyutları artırılmalı. 9–11 px meta etiketleri ve çok sayıda border/uppercase öğesi taramayı zorlaştırıyor.
- Koyu/açık temalarda kontrast, klavye ile tüm akışlar ve yüzde 200 zoom doğrulanmalı. Animasyonlarda reduced-motion tercihi gözetilmeli.

### Editör/yönetici deneyimi

- “Publish / Save Post” ve iki boolean yerine kullanıcı niyetine uygun Taslak kaydet / Planla / Yayımla eylemleri; teknik alan adlarını kullanıcı akışından çıkarma.
- Kaydetme başarılı olana kadar taslağı silmeme. Create.cshtml:272 localStorage kopyasını submit anında siliyor; ağ/validation hatasında kurtarma kopyası kaybolabilir.
- Edit sayfasına da değişiklik kaybı koruması; autosave'in “bu tarayıcıya kaydedildi” ile “sunucuya kaydedildi” ayrımı. Ortak cihazlarda localStorage taslaklarının gizliliği ve oturum sonrası temizleme tercihi.
- Gerçek yayın renderer'ıyla güvenli önizleme, isteğe bağlı sürüm geçmişi ve geri yükleme. Bunlar kapsamlı CMS yerine küçük, kontrollü özellikler olarak eklenebilir.
- Türkçe slug önizlemesini sunucuyla uyumlu hale getirme: mevcut JavaScript Türkçe harfleri siliyor, sunucu ASCII karşılığına dönüştürüyor.
- Upload progress, boyut/biçim hatası, yeniden deneme ve yükleme tamamlanmadan kaydetmenin yönetilmesi. Cloudinary bağlantısı yoksa bunun anlaşılır hata olarak sunulması.
- Dashboard'daki “Cloudinary Active” ve “Webhook Ready” statik metinler. Testte sahte Cloudinary ayarlarıyla bile aktif görünüyor. Gerçek sağlık kontrolü veya “yapılandırıldı / doğrulanmadı” ifadesi kullanılmalı.
- jQuery validation ya doğru sırayla yüklenmeli ya tutarlı başka form yaklaşımına geçilmeli. Şu an referanslanan validation eklentilerinin jQuery bağımlılığı yüklenmiyor.

## 9. Tam bir blog için ürün kapsamı

Tek yazarlı profesyonel blog için çekirdek kapsam: güvenilir taslak/planlama/yayınlama; kalıcı URL; arama ve kategoriler; kaliteli yazı okuma sayfası; yazar/hakkında/iletişim; SEO/paylaşım; medya yönetimi; yedekten geri dönüş; mobil ve erişilebilir yönetim.

Eksik SEO işleri: normal meta description, canonical, mutlak og:url, og:image/Twitter image, uygun sosyal kart, BlogPosting yapılandırılmış verisi, breadcrumb, düzenlenme tarihi, tutarlı sitemap. /post ve /yazi aynı içeriğe 200 veriyor; tek kanonik URL ve uygun 301 kararı gerekli. Sitemap lastmod şu anda CreatedDate; ayrıca StringBuilder üstünde XmlWriter deklarasyonu ile döndürülen UTF-8 içerik kodlaması tutarlı hale getirilmeli. Yönetim/giriş/arama sayfalarının indekslenme politikası ve robots.txt belirlenmeli; robots erişim kontrolü değildir. Favicon dosyası boş görünüyor.

RSS/Atom iyi bir sonraki adımdır. Etiketler ve yazı serileri teknik içerikte değerli olabilir. Yorum, bülten, üyelik ve çok yazarlılık bir blogun tamamlanması için zorunlu değil; moderasyon, kişisel veri ve işletim yükleriyle birlikte ayrıca değerlendirilmelidir. Mevcut tek-admin kuralı çok yazarlılık için ayrı mimari karar gerektirir.

## 10. Performans ve işletim

- Public besleme yalnızca 3 yazı döndürmek için tüm yazıları ve içeriklerini yüklüyor. Filtre/sıralama/Take ve gerekli alan seçimi veritabanında yapılmalı.
- Admin yazı listesi bütün kayıtları yüklüyor; filtreler tarayıcıda. Sunucu tarafında sayfalama/filtreleme. Kategori listelemede tüm Posts koleksiyonunu yüklemek yerine sayım sorgusu.
- Salt okunur EF sorgularında AsNoTracking; ihtiyaca uygun sütun seçimi; slug unique indeksi ve yayın/kategori listeleme indekslerini gerçek sorgu planına göre belirleme.
- Arama tüm içerikte lower/contains yapıyor ve sonuç sınırı yok. Önce sorgu uzunluğu ve sayfalama; büyüdüğünde uygun PostgreSQL full-text/trigram stratejisini ölçerek seçme.
- IMemoryCache key'leri kontrolsüz page değerleriyle çoğalabilir; sayfa aralığı ve cache boyut sınırı. Her query string için yeni output cache varyantının gerekip gerekmediğini belirleme.
- Static CancellationTokenSource kilitsiz cancel/dispose/reassign yapıyor. Eşzamanlı değişikliklerde yarış riski var. Ya senkronizasyonu doğru kurmak ya daha basit bir sürüm/etiketli cache modeli kullanmak; çok instance varsa süreç içi invalidation'ın diğer sunucuları etkilemediğini dikkate almak.
- Cloudinary görsel kimliği ve kullanım ilişkisi saklanmalı; değişen/silinen görsellerin kontrollü temizliği ve başarısız yazı kaydından kalan orphan dosyalar yönetilmeli.
- Test projesi ve uygulama CI pipeline'ı bulunmadı. Üçüncü taraf node_modules testleri uygulamanın test kapsamı sayılmadı.
- 245 bin/obj dosyası Git'te izleniyor. .gitignore bunları sonradan ekleyince mevcut takip sona ermez. İzlemeyi kontrollü kaldırmak, paket/build çıktısını kaynak depodan ayırmak gerekir.
- Sürüm kontrollü SDK/tool seçimi, build/test/package audit, migration dağıtım adımı, uygulama/DB sağlık kontrolleri, yapılandırılmış loglar ve geri alma planı eklenmeli.
- Yedek almak kadar geri yüklemeyi prova etmek de kabul koşulu olmalı. Gerçek üretim yedeği olmadığı sonucu çıkarılmıyor; depoda bu süreç belgelenmemiş.

## 11. On adımlı uygulama planı

| Adım | İş paketi | Bitti sayılması için koşul | Tahmini net gün |
|---:|---|---|---:|
| 1 | Güvenlik kapıları | Eksik credential başlangıçta reddedilir; iki XSS testi geçer; cookie POST token doğrular; login limitli | 3–4 |
| 2 | Veri modeli ve migration | Temiz DB kurulumu/yükseltme çalışır; slug unique; kategori silme DB'de korunur; tarih/URL kuralları belirli | 2–3 |
| 3 | Yayınlama doğruluğu | Bütün public yüzeylerde aynı yayın filtresi; UTC/saat dilimi; planlı yayın ve yayından kaldırma cache testleri geçer | 2–3 |
| 4 | CRUD ve editör hataları | Dosyasız kaydetme; görünür validation; doğru hata durum kodları; autosave veri kaybı koruması; jQuery hatası yok | 2–3 |
| 5 | Mimari sadeleştirme | View/controller DB erişimleri ayrılır; gerekli repository arayüzleri; ortak editör/kart bileşenleri; kritik davranışlar korunur | 3–5 |
| 6 | Mobil yönetim ve içerik süreci | 390 px ekranda yazı oluşturma/düzenleme kullanılabilir; yayın durumu açık; önizleme ve geri alma kararı uygulanmış | 3–5 |
| 7 | Okuyucu UI/UX ve erişilebilirlik | İyi tipografi, global arama, ilgili yazılar, doğru navigasyon; klavye/drawer/zoom kontrolleri geçer | 3–5 |
| 8 | SEO ve blog kimliği | Canonical/301, metadata/social cards, sitemap, yazar/hakkında/iletişim, RSS kararı tamam | 2–3 |
| 9 | Performans ve medya | Ölçülü sorgular, uygun indeksler, sayfalı yönetim, derlenmiş Tailwind, optimize görseller, sınırlandırılmış cache | 2–4 |
| 10 | Test, CI ve üretim işletimi | Kritik regresyon paketi, dependency audit, migration dağıtımı, health/log, yedek geri yükleme ve staging kabulü | 3–5 |
| | **Toplam** | Kapsam: mevcut MVC ile tek yazarlı blog | **25–40** |

Testler yalnızca son adımda yazılmamalı; her düzeltmenin gerekli regresyon kontrolü kendi adımıyla tamamlanmalı. Son adım bunları dağıtım/CI ve kabul sürecine bağlar. Güvenlik düzeltmeleri bitmeden yeni entegrasyon veya okuyucu hesabı gibi saldırı yüzeyini büyüten özellikler öne alınmamalı.

Kritik regresyon örnekleri: eksik admin parolası; içerik/bildirim XSS; tokensız yönetim POST; taslak/gelecek/pasif içerik görünürlüğü; dosyasız kaydetme; slug çakışması; eski URL; eşzamanlı sayaç/düzenleme; kategori silme yarışı; cache sonrası yayından kaldırma; geçersiz sayfalama; boş DB migration; klavyeyle ve mobilde yazı kaydetme.

Performans hedefleri mevcut skor diye sunulmamalı: örneğin gerçek kullanıcı ölçümünde LCP ≤2,5 saniye, INP ≤200 ms, CLS ≤0,1 hedeflenebilir. Mobil ve masaüstü ziyaretlerinin 75. yüzdeliğinde değerlendirmek gerekir. Trafik, cihaz ve veri hacmine göre baz ölçüm alınıp tekrar değerlendirilmelidir. [Google Web Vitals ölçütleri](https://web.dev/articles/vitals)

## 12. Önemli kaynak konumları

- [Giriş ve çıkış](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Controllers/AccountController.cs:69)
- [Markdown dönüşümü](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/DevCoreBlog.Core/Shared/Helpers/MarkdownHelper.cs:41)
- [Public HTML çıktısı](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Views/Home/Detail.cshtml:50)
- [Bildirim HTML'i](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Views/Shared/_AdminLayout.cshtml:336)
- [Yazı yönetimi ve dosya binding](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Controllers/AdminPostController.cs:129)
- [Public sorgular](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/DevCoreBlog.Data/Repositories/PostRepository.cs:41)
- [Cache ve yazı servisi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/DevCoreBlog.Services/PostService.cs:62)
- [Generic update](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/DevCoreBlog.Data/Repositories/GenericRepository.cs:105)
- [Kategori update/silme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/DevCoreBlog.Services/CategoryService.cs:106)
- [DbContext kullanan dashboard](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Controllers/AdminController.cs:32)
- [DbContext kullanan layout](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Views/Shared/_Layout.cshtml:205)
- [Middleware sırası](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Program.cs:187)
- [Hata yönlendirmesi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Middlewares/ExceptionHandlingMiddleware.cs:32)
- [Editör ve erken taslak silme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Views/AdminPost/Create.cshtml:269)
- [Sabit admin sidebar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Views/Shared/_AdminLayout.cshtml:160)
- [Ana sayfa veri seçimi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Views/Home/Index.cshtml:18)
- [Model ve indeksler](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/Migrations/ApplicationDbContextModelSnapshot.cs:83)
- [İnceleme tarihindeki mimari depo kuralları](arsiv/agent-talimatlari-2026-09-21/AGENTS_LEGACY.md)

Rapor mevcut kodu ve yukarıdaki doğrulama koşullarını esas alır. Önerilen paketler uygulandıktan sonra güvenlik, SOLID ve ürün kalitesi tekrar değerlendirilmelidir.
