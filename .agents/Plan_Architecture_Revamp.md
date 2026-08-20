# DevCoreBlog Mimari ve Performans Revizyon Planı

**AI Ajanı İçin Kritik Kurallar (MANDATORY RULES):**
1. **Halüsinasyon Önlemi:** Bir dosyayı değiştirmeden önce mutlaka içeriğini oku. Olmayan değişkenleri veya kütüphaneleri varsayma.
2. **Kademeli İlerleme:** Her seferinde sadece BİR Atomik Adım (Atomic Step) tamamla.
3. **Onay İşareti:** Bir adımı başarıyla bitirdiğinde yanındaki `[ ]` işaretini `[x]` olarak güncelle ve dosyayı kaydet.
4. **Test Odaklılık:** Her adımdan sonra projeyi derle (`dotnet build`). Eğer derleme hatası varsa bir sonraki adıma geçme, hatayı çöz.
5. **Mevcut Yapıyı Koruma:** Geliştirme yaparken projenin temel MVC yapısını ve çalışır durumdaki iş mantığını (business logic) silme, sadece üzerine ekle veya güvenlice taşı.

---

## Faz 1: Veri Yapısı Optimizasyonu (Post Entity)
*Amaç: İçerik yönetimini profesyonelleştirmek ve ilerideki fazlara zemin hazırlamak.*

- [x] **Adım 1.1:** `Core/Entities/Post.cs` dosyasına yeni özellikleri ekle. (`ThumbnailUrl`, `Excerpt`, `IsPublished`).
- [x] **Adım 1.2:** `PostRepository.cs` ve `PostService.cs` içerisindeki mevcut sorguları yeni alanlara göre (örneğin sadece `IsPublished == true` olanları getirecek şekilde) güncelle.
- [x] **Adım 1.3:** Terminalde `dotnet ef migrations add UpdatePostDataStructure` komutunu çalıştırarak veritabanı yansımasını oluştur.
- [x] **Adım 1.4:** `dotnet ef database update` ile veritabanını güncelle.
- [x] **TEST:** Projeyi çalıştır (`dotnet run`) ve Admin panelinden yeni alanların veritabanına hatasız kaydedildiğini doğrula.

---

## ## Faz 2: CDN Entegrasyonu (Cloudinary)
*Amaç: Görsel yüklerini sunucudan alıp CDN'e aktarmak.*

- [x] **Adım 2.1:** Projeye Cloudinary paketini ekle (`dotnet add package CloudinaryDotNet`).
- [x] **Adım 2.2:** `Services/Interfaces/IImageService.cs` ve `Services/ImageService.cs` dosyalarını oluştur. Upload mantığını yaz.
- [x] **Adım 2.3:** `ImageService.cs` constructor'ı içerisinde Cloudinary bağlantısını kurarken `appsettings.json` KULLANMA. Bunun yerine değerleri `Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME")`, `CLOUDINARY_API_KEY` ve `CLOUDINARY_API_SECRET` kullanarak doğrudan ortam değişkenlerinden (.env) oku.
- [x] **Adım 2.4:** `Program.cs` içerisinde `IImageService` için Dependency Injection (DI) ayarını yap (`builder.Services.AddScoped<IImageService, ImageService>();`).
- [x] **Adım 2.5:** `AdminPostController.cs` dosyasını güncelle. Formdan gelen görseli (IFormFile) `ImageService` ile Cloudinary'e yükle ve dönen URL'i `Post.ThumbnailUrl` alanına ata.
- [x] **TEST:** Admin panelinden görsel içeren bir Post ekle. Veritabanında `ThumbnailUrl` kolonunda cloudinary linkini (http://res.cloudinary.com/...) gördüğünü onayla.

---

## Faz 3: N-Tier (Çok Katmanlı / Shared) Mimariye Geçiş
*Amaç: Monolitik klasör yapısını, bağımsız Class Library (Shared) projelerine bölmek. (DİKKAT: En riskli fazdır, adım adım ve derleyerek ilerle).*

- [ ] **Adım 3.1:** Solution kök dizininde yeni projeleri oluştur:
  - `dotnet new classlib -n DevCoreBlog.Core`
  - `dotnet new classlib -n DevCoreBlog.Data`
  - `dotnet new classlib -n DevCoreBlog.Services`
- [ ] **Adım 3.2:** Proje referanslarını bağla:
  - Data -> Core referansı ekle.
  - Services -> Core ve Data referanslarını ekle.
  - DevCoreBlog (Web) -> Tüm projelere referans ekle.
- [ ] **Adım 3.3:** `Core` klasöründeki her şeyi (`Entities`, `Interfaces`) `DevCoreBlog.Core` projesine fiziksel olarak taşı ve namespace'leri düzelt.
- [ ] **Adım 3.4:** `Data` (DbContext) ve `Repositories` klasörlerini `DevCoreBlog.Data` projesine taşı ve namespace'leri düzelt.
- [ ] **Adım 3.5:** `Services` klasörünü `DevCoreBlog.Services` projesine taşı ve namespace'leri düzelt.
- [ ] **Adım 3.6:** Ana `DevCoreBlog` projesindeki (Web) gereksiz klasörleri temizle. `Program.cs` içindeki DI ve using bildirimlerini yeni namespace'lere göre onar.
- [ ] **TEST:** Çözümü derle (`dotnet build`). Sıfır hata ile derlendiğinden emin ol. Projeyi çalıştır ve sayfaların kırılmadığını test et.

---

## Faz 4: DTO ve AutoMapper Kurulumu
*Amaç: Entity modellerini doğrudan UI katmanına taşımayı engellemek ve güvenliği artırmak.*

- [ ] **Adım 4.1:** Gerekli paketi kur (`dotnet add package AutoMapper.Extensions.Microsoft.DependencyInjection`).
- [ ] **Adım 4.2:** `DevCoreBlog.Core` içerisine `DTOs` klasörü aç. `PostDto`, `PostCreateDto`, `PostUpdateDto` sınıflarını oluştur.
- [ ] **Adım 4.3:** `DevCoreBlog.Services` içerisinde `MappingProfile.cs` oluştur ve haritalamaları (Entity <-> DTO) yapılandır.
- [ ] **Adım 4.4:** `PostService` ve Controller'ları güncelleyerek Entity yerine DTO dönmelerini sağla.
- [ ] **TEST:** Front-end (HomeController) ve Admin sayfalarında verilerin eksiksiz ve doğru listelendiğini doğrula.

---

## Faz 5: Global Exception Handling (Merkezi Hata Yönetimi)
*Amaç: Controller'lardaki try-catch bloklarını temizlemek ve hataları merkezi bir noktadan yönetmek.*

- [ ] **Adım 5.1:** Web projesinde `Middlewares` klasörü oluştur. İçine `ExceptionHandlingMiddleware.cs` ekle.
- [ ] **Adım 5.2:** Middleware içinde, hataları yakalayıp loglayacak ve uygun HTTP 500 formatında veya Error View ile dönecek mantığı yaz.
- [ ] **Adım 5.3:** `Program.cs` içerisinde `app.UseMiddleware<ExceptionHandlingMiddleware>();` tanımlamasını yap.
- [ ] **TEST:** Herhangi bir Controller'da kasten `throw new Exception("Test Hatası");` yaz. Uygulamanın çökmediğini ve özel hata sayfasının/mesajının döndüğünü gör. Ardından test kodunu sil.

---

## Faz 6: Caching (Önbellekleme)
*Amaç: Ana sayfadaki veritabanı yükünü düşürmek ve yanıt sürelerini hızlandırmak.*

- [ ] **Adım 6.1:** `Program.cs` içerisine `builder.Services.AddMemoryCache();` ekle.
- [ ] **Adım 6.2:** `PostService.cs` içerisine `IMemoryCache` inject et.
- [ ] **Adım 6.3:** Ana sayfaya gönderilen post listesini (örneğin `GetAllPublishedPostsAsync`) cache'e al. (Örn: 10 dakikalık Absolute Expiration ile).
- [ ] **Adım 6.4:** Admin panelinden yeni bir Post eklendiğinde, güncellendiğinde veya silindiğinde ilgili Cache anahtarını (Cache Key) temizleyen mekanizmayı ekle.
- [ ] **TEST:** Uygulamayı çalıştır. Ana sayfayı yenile. Loglardan veritabanına sadece ilk seferde sorgu atıldığını, sonraki yenilemelerde verinin bellekten (Cache) anında geldiğini doğrula.
