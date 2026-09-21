# DevCoreBlog

ASP.NET Core MVC (.NET 10), PostgreSQL ve Razor/Tailwind kullanan blog projesi. Mevcut kapsam; yazı/kategori yönetimi, Markdown içerik, Cloudinary görselleri, yönetici oturumu, webhook ve portföy beslemesidir.

## Agent ile çalışma

**Önce [AGENTS.md](AGENTS.md) dosyasını oku.** Güncel mimari, SOLID, güvenlik, kod kalitesi ve doğrulama kuralları buradadır. .agents/AGENTS.md yalnızca köke yönlendirir; eski planlar aktif değildir.

- [Geliştirme planı](docs/GELISTIRME_PLANI_2026-09-21/README.md)
- [İlerleme](docs/GELISTIRME_PLANI_2026-09-21/DURUM.md)
- [Kararlar](docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)
- [Hazır agent mesajları](docs/GELISTIRME_PLANI_2026-09-21/AGENT_PROMPTLARI.md)
- [19 Eylül inceleme raporu](docs/PROJE_INCELEME_RAPORU_2026-09-19.md)

Kullanıcının kod/terminal komutu yazması gerekmez; agent seçilen tek fazı uygular ve doğrular. F00 kural/dokümantasyon yenilemesi tamamlandı; sıradaki faz F01. Uygulama kodundaki güvenlik ve veri bütünlüğü bulguları henüz düzeltilmiş sayılmaz.

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
docs/                            Aktif plan, karar, kanıt ve tarihsel arşiv
```

Dört proje korunur. Core'un Markdig bağımlılığı ve Services'ın Data bağımlılığı F24/F26'da giderilecek teknik borçtur. Gerçek paket sürümleri .csproj dosyalarından doğrulanır.

## Agent için yerel doğrulama başlangıcı

Önce kurulu .NET SDK'yı ve mevcut değişiklikleri incele. Root'tan derleme komutu:

```sh
dotnet build DevCoreBlog.csproj
```

Konfigürasyon adları [.env.example](.env.example) dosyasındadır. Gerçek .env ve secret'lar commit/rapora yazılmaz. Testte ayrı PostgreSQL ve sentetik admin/medya verisi kullanılır; production kaynağına bağlanılmaz.

Migration assembly keşfinde bilinen uyumsuzluk vardır (F10). Eski talimatlardaki yanlış proje yollarıyla database update çalıştırma; önce assembly/tool sürümü ve hedef test DB doğrulanmalıdır. Test şeması hazır olduğunda yerel çalıştırma:

```sh
dotnet run --project DevCoreBlog.csproj
```

Bu komutların belgelenmesi çalıştırıldıkları anlamına gelmez. Build/test/tarayıcı kanıtları ilgili faz kaydında tutulur. CSRF, cache, XSS veya SOLID açısından kusursuzluk iddia edilmez; inceleme raporu ve ilerleme çizelgesi güncel durumu ayırır.
