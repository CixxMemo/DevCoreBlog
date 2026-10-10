# DevCoreBlog

ASP.NET Core MVC (.NET 10), PostgreSQL ve Razor/Tailwind kullanan blog projesi. Mevcut kapsam; yazı/kategori yönetimi, Markdown içerik, Cloudinary görselleri, yönetici oturumu, webhook ve portföy beslemesidir.

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
| Arayüz | Razor, Tailwind, gerekli JavaScript |
| Oturum | ASP.NET Core Cookie Authentication |
| İçerik | Markdig, Toast UI Editor, Prism |
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

Dört proje korunur: Data ve Services, Core sözleşmelerine bağımlıdır; Services Data'ya, Core vendor SDK'larına referans vermez. Gerçek paket sürümleri .csproj dosyalarından doğrulanır.

## Frontend derlemesi (F35)

Build ortamında .NET SDK yanında Node.js 22 veya üzeri ve npm gerekir. Sabit paketler `package.json`/`package-lock.json` içindedir. İlk kurulum:

```sh
npm ci --ignore-scripts --no-fund --no-audit
```

Tek frontend derleme komutu:

```sh
npm run build
```

`dotnet build DevCoreBlog.csproj` ve `dotnet publish DevCoreBlog.csproj` aynı derlemeyi otomatik çalıştırır; eksik veya lockfile'dan eski npm kurulumu önce `npm ci` ile hazırlanır. `dotnet run --no-build` öncesinde build yapılmalıdır. Razor/JS sınıfı değişince yeniden build gerekir; tarayıcıda Tailwind derleyicisi yoktur.

- Tarama: `Views/**/*.cshtml`, `wwwroot/js/**/*.js`; inline Razor script'leri de taranır. Sınıf adlarını tam literal olarak yaz; `bg-${color}-600` gibi birleştirme kullanma. Toast/drawer durumları mevcut sonlu literal adları kullanır.
- `frontend/tailwind.public.cjs` ve `tailwind.admin.cjs` mevcut iki layout temasını korur; ortak font/tarama ayarı `tailwind.shared.cjs` içindedir. Eski kullanılmayan browser `wwwroot/tailwind.config.js` kaldırıldı.
- Tailwind 3.4.17 resmi CLI ve Prism 1.30.0 npm lock'tan gelir. Mevcut Toast UI 3.2.2 dağıtımı [kaynak/hash/lisans kaydıyla](frontend/vendor/toastui/README.md) yerelde tutulur. Toolbar, Show Language'den önce yüklenir; Prism dilleri yerel components dizininden otomatik açılır.
- Çıktı `wwwroot/generated/` içine yazılır; bu klasör ve `node_modules/` Git'e eklenmez. Temiz checkout'ta build üretir ve publish'e dahil eder. Uygulama çalışma ortamında Node/npm gerekmez. Yerel CSS/JS URL'leri `asp-append-version` ile cache busting kullanır.
- Temiz kurulum, aynı hash ile ikinci derleme ve temiz publish kabul kontrolü: `sh scripts/verification/run_f35_assets.sh`. Yalnız geçici kaynak kopyasında çalışır; gerçek `.env` kopyalanmaz.

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
tek akışta çalıştırır. Gereksinimler, kapsam ve kullanıcı onaylı dar build-time risk istisnası
[kalite kapısı belgesindedir](docs/KALITE_KAPISI.md); kırmızı sonuç başarı sayılmaz.
GitHub Actions aynı kontrolleri push/PR'da çalıştırır; yerel başarı uzak CI sonucu
yerine geçmez. Tam kabul için `--final-acceptance` seçeneğini kullanın.

## Yedek ve yayınlama (F56)

[Prosedür ve izole restore komutu](docs/YEDEK_VE_YAYIN_PROSEDURU.md) DB/medya/anahtar kapsamını, staging sırasını ve önceki binary uyumluluğunu açıklar. Test aracı canlı DB kabul etmez; canlı deploy/restore için ayrı açık yetki gerekir.
