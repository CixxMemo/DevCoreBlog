# DevCoreBlog

ASP.NET Core MVC (.NET 10), PostgreSQL ve Razor/Tailwind kullanan blog projesi. Mevcut kapsam; yazı/kategori yönetimi, Türkçe Tiptap JSON yazım formu, Cloudinary görselleri, yönetici oturumu, webhook ve portföy beslemesidir. Markdown/Toast UI akışı geçiş süresince korunur; public JSON gösterimi henüz tamamlanmadı.

## Agent ile çalışma

**Önce [AGENTS.md](AGENTS.md) dosyasını oku.** Güncel mimari, SOLID, güvenlik, kod kalitesi ve doğrulama kuralları buradadır. .agents/AGENTS.md yalnızca köke yönlendirir; eski planlar aktif değildir.

Kişisel geliştirme planı, ilerleme/karar kayıtları ve kabul kanıtları yerelde
tutulur; GitHub checkout'unda bulunmaları beklenmez. Yerelde mevcutsa agent
görevle ilgili kayıtları ve teknik sözleşmeleri okur; yoksa AGENTS.md, gerçek kaynaklar ve kullanıcının
açık göreviyle çalışır. Kullanıcının kod/terminal komutu yazması gerekmez.

## Teknoloji ve proje haritası

| Alan | Mevcut teknoloji |
|---|---|
| Web | C#, ASP.NET Core MVC, net10.0, nullable açık |
| Veri | EF Core 10 + Npgsql + PostgreSQL |
| Arayüz | Razor, Tailwind CSS 4.3.3, küçük vanilla JavaScript modülleri |
| Oturum | ASP.NET Core Cookie Authentication |
| İçerik | Tiptap 3.31.4 ve sürümlü JSON; geçişte Markdig/Toast UI; Prism |
| Medya/config | CloudinaryDotNet, environment, yerelde DotNetEnv |

```text
AGENTS.md                         Tek güncel ana talimat kaynağı
.agents/AGENTS.md                 Kök talimatlara yönlendirme
DevCoreBlog.csproj                Web uygulaması
Controllers/ Views/ Middlewares/  HTTP ve sunum
DevCoreBlog.Core/                 Domain ve sözleşmeler
DevCoreBlog.Data/                 EF ve repository implementasyonları
DevCoreBlog.Services/             Use case'ler ve servisler
Migrations/                      Bugün Web'de bulunan migration geçmişi
wwwroot/                         Statik varlıklar
docs/                            Yerel teknik sözleşmeler ve işletim belgeleri; Git dışında
```

Dört proje korunur: Data ve Services, Core sözleşmelerine bağımlıdır; Services Data'ya, Core vendor SDK'larına referans vermez. .NET paket sürümleri `.csproj`, frontend sürümleri `package.json`/`package-lock.json` dosyalarından doğrulanır.

## Frontend derlemesi — Tailwind 4 / PostCSS

Build ortamında .NET SDK yanında Node.js 22 veya üzeri ve npm gerekir. Sabit paketler `package.json`/`package-lock.json` içindedir. İlk kurulum:

```sh
npm ci --ignore-scripts --no-fund --no-audit
```

Tek frontend derleme komutu:

```sh
npm run build
```

`dotnet build DevCoreBlog.csproj` ve `dotnet publish DevCoreBlog.csproj` aynı derlemeyi otomatik çalıştırır; eksik veya lockfile'dan eski npm kurulumu önce `npm ci` ile hazırlanır. `dotnet run --no-build` öncesinde build yapılmalıdır. Razor/JS sınıfı değişince yeniden build gerekir; tarayıcıda Tailwind derleyicisi yoktur.

- Tailwind CSS **4.3.3**, resmî `@tailwindcss/postcss` **4.3.3** ve PostCSS **8.5.29** exact sürümlerle kilitlidir. `scripts/frontend/build.mjs`, `scripts/frontend/tailwind-bundle.mjs` üzerinden resmî PostCSS API'sini çağırır. CLI/watch bağımlılığı kullanılmaz. [Resmî PostCSS entegrasyonu](https://tailwindcss.com/docs/installation/using-postcss).
- `frontend/tailwind.public.css` ve `frontend/tailwind.admin.css` iki giriş dosyasıdır; ortak `frontend/tailwind.css`, `source(none)` ile otomatik taramayı kapatır. Yalnız `Views/**/*.cshtml` ve `wwwroot/js/**/*.js` taranır. Sınıf adlarını tam literal yaz; `bg-${color}-600` gibi birleştirme kullanma.
- Girişlerin `@config` ile yüklediği `frontend/tailwind.public.cjs` ve `frontend/tailwind.admin.cjs` iki layout temasını korur; `frontend/tailwind.shared.cjs` ortak fontları tanımlar. Public koyu tema class ile çalışır. `frontend/tailwind.compat.css` mevcut görünümü koruyan sınırlı uyumluluk kurallarını taşır.
- Çıktılar `wwwroot/generated/tailwind-public.css` ve `wwwroot/generated/tailwind-admin.css` dosyalarıdır. Tailwind MIT lisansı `wwwroot/generated/TAILWIND-LICENSE` olarak yayımlanır. Prism 1.30.0 ve Tiptap 3.31.4 aynı kilitli yerel build'e dahildir; Tiptap bundle/chunk, lisans ve build receipt'i `wwwroot/generated/tiptap/` altındadır.
- Mevcut Toast UI 3.2.2 dağıtımı [kaynak/hash/lisans kaydıyla](frontend/vendor/toastui/README.md) yerelde tutulur; build sırasında kilitli DOMPurify modülüyle hazırlanır. Prism toolbar, Show Language'den önce yüklenir; diller yerel components dizininden otomatik açılır.
- Çıktı `wwwroot/generated/` içine yazılır; bu klasör ve `node_modules/` Git'e eklenmez. Temiz checkout'ta build üretir ve publish'e dahil eder. Uygulama çalışma ortamında Node/npm gerekmez. Yerel CSS/JS URL'leri `asp-append-version` ile cache busting kullanır.
- Temiz scriptsiz kurulum, aynı platformda aynı hash ile ikinci derleme, temiz publish ve değişen kaynak/chunk için incremental publish kontrolü: `sh scripts/verification/run_f35_assets.sh`. Yalnız geçici kaynak kopyasında çalışır; gerçek `.env` kopyalanmaz. Yeni publish dizini doğrulanır; eski dağıtım dizinindeki artık dosyaların temizliği ayrıca yönetilir.

Tailwind 4'ün resmî tarayıcı hedefi **Safari 16.4+, Chrome 111+, Firefox 128+** seviyesidir. [Geçiş rehberi ve tarayıcı sınırları](https://tailwindcss.com/docs/upgrade-guide). 11 Ekim 2026 kabulünde gerçek Chrome 154.0.8037.98, patched Gecko Firefox 153.0 ve native macOS Safari 26.0.1 kullanıldı. Masaüstü ve dar viewport ölçümleri fiziksel mobil cihaz veya bütün tarihsel tarayıcı sürümlerinde test yapıldığı anlamına gelmez. Uygulama server-side MVC/Razor olarak kalır.

## Agent için yerel doğrulama başlangıcı

Önce kurulu .NET SDK'yı ve mevcut değişiklikleri incele. Root'tan derleme komutu:

```sh
dotnet build DevCoreBlog.csproj
```

Konfigürasyon adları [.env.example](.env.example) dosyasındadır. Gerçek .env ve secret'lar commit/rapora yazılmaz. Testte ayrı PostgreSQL ve sentetik admin/medya verisi kullanılır; production kaynağına bağlanılmaz.

Migration geçmişi Web projesindedir ve `ApplicationDbContext` bu assembly'yi açıkça kullanır. Mevcut migration'ları listelemek ve idempotent kurulum SQL'i üretmek için gerçek proje yolları şunlardır:

```sh
dotnet ef migrations list --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj --context ApplicationDbContext
dotnet ef migrations script --idempotent --project DevCoreBlog.csproj --startup-project DevCoreBlog.csproj --context ApplicationDbContext --output migration.sql
```

Yeni migration da aynı `--project` ve `--startup-project` değerleriyle oluşturulur. `database update` yalnızca doğrulanmış hedef bağlantıda, yedek/geri dönüş planıyla çalıştırılır; production veritabanı geliştirme doğrulaması için kullanılmaz. Yerel uygulama şeması hazır olduğunda çalıştırma:

```sh
dotnet run --project DevCoreBlog.csproj
```

Bu komutların belgelenmesi çalıştırıldıkları anlamına gelmez. Yeni çalışma için gerçek build/test kanıtı gerekir; CSRF, cache, XSS veya SOLID açısından kusursuzluk iddia edilmez. Yerel kabul kayıtları ve GitHub Actions sonuçları kendi sınırlarıyla yorumlanır.

Gerçek test mümkünse yapılır. Agent önce kodu ve mevcut ortam/hesap/araçları
inceler, gerekli izole ortamın kurulabilirliğini araştırır. Gerçek entegrasyonun
yerine mock sonucu sunulmaz. Ortam kurulamazsa somut engel ve gerçeğe en yakın
kontrol kaydedilir; teorik inceleme çalıştırılmış test sayılmaz. Ayrıntılı kural
[AGENTS.md bölüm 9](AGENTS.md#9-çalışma-ağacı-ve-doğrulama) içindedir.

## Git, yerel dosyalar ve yayın paketi

`bin/`, `obj/`, `node_modules/`, `wwwroot/generated/` ve yerel cookie/auth
kayıtları takip edilmez. Derleme çıktıları build ile yeniden üretilir; migration,
lockfile ve lisanslı frontend kaynakları korunur. `git rm --cached` yerel dosyayı
silmez. Kişisel plan/kayıt/kanıt/arşiv ve iç inceleme raporları da Git dışında
kalır. `docs/` klasörünün tamamı, teknik sözleşmeler dahil, yalnız yerelde
korunur; AGENTS.md ve test/CI kaynakları takip edilir. Bu README'deki `docs/`
bağlantıları yerel belgelere aittir; temiz GitHub checkout'unda bulunmayabilir.

Gerçek `.env` ignore edilir; yalnız güvenli placeholder içeren `.env.example`
takipte kalır. Yerel `.env` için `chmod 600 .env` dosya sahibine okuma/yazma verir,
diğer normal kullanıcıların erişimini kapatır. Root/yönetici ve aynı hesapta
çalışan süreçler için mutlak koruma değildir. Production `.env` okumaz;
[dağıtım sözleşmesindeki](docs/DEPLOYMENT_SECURITY.md) servis environment'ını kullanır.

Git ignore kuralları publish'i yönetmez. Proje ayrıca `.env*`, `docs/**`, AGENTS.md
ve `.agents/**` dosyalarını SDK item keşfinden dışlar; `publish-boundary` kontrolü
sentetik dosyalarla gerçek publish çıktısını sınar. `docs/` belgeleri GitHub'a
ve uygulama paketine dahil edilmez.

Gerçek `.env`, cookie ve Data Protection anahtarlarını eklemeyin.
[Oturum ve geçmiş riski](docs/DEPLOYMENT_SECURITY.md#f54--git-takibi-ve-geçmişteki-oturum-kayıtları)
Git takibinden çıkarmanın geçmişi temizlemediğini ve gerektiğinde oturum iptalini
açıklar.

## Otomatik kalite kapısı (F55)

`python3 scripts/verification/run_quality_gate.py --report-dir /tmp/devcore-quality-report`
temiz build, gerçek advisory taraması ve sentetik PostgreSQL/HTTP kontrollerini
tek akışta çalıştırır. Rapor dizini boş olmalıdır. Gereksinimler ve ayrıntılı kapsam
[yerel kalite kapısı belgesindedir](docs/KALITE_KAPISI.md).

Npm audit istisnasızdır: geçerli ve tutarlı v2 JSON, sıfır bulgu ve gerçek exit0
gerekir. Her severity'deki eski/yeni bulgu, erişim/parse/count hatası veya boş
yanıt+exit1 kapıyı durdurur. Önceki braces risk kabulü kaldırıldı; mevcut lock'ta
braces zinciri yoktur. Sıfır bilinen bulgu mutlak güvenlik garantisi değildir.
`npm-audit-policy` aşamasının sentetik olumsuz kontrolleri gerçek registry,
PostgreSQL, Cloudinary veya tarayıcı entegrasyonu yerine sayılmaz.

Normal kapı 31, `--final-acceptance` kapısı 33 aşamadır. 11 Ekim 2026 Tailwind
geçişi kabulünde gerçek macOS 15.7.4 ARM64 ve Ubuntu 24.04.4 Linux ARM64 üzerinde
final kapı **33/33**, npm audit exit0/sıfır bulgu ve temiz/incremental asset
build/publish kontrolleri geçti. Bunlar yerel kabul sonuçlarıdır; Linux x64 veya
canlı sunucu kabulü değildir. Bu README güncellemesinde testler yeniden çalıştırılmadı.

GitHub Actions'ın [.github/workflows/quality.yml](.github/workflows/quality.yml)
dosyası aynı final kapıyı push/PR ve manuel dispatch için tanımlar; yerel başarı
uzak workflow'un çalıştığını göstermez. Tam kabul için `--final-acceptance`
seçeneğini kullanın; kırmızı sonuç başarı sayılmaz.

## Yedek ve yayınlama (F56)

[Prosedür ve izole restore komutu](docs/YEDEK_VE_YAYIN_PROSEDURU.md) DB/medya/anahtar kapsamını, staging sırasını ve önceki binary uyumluluğunu açıklar. Test aracı canlı DB kabul etmez; canlı deploy/restore için ayrı açık yetki gerekir.
