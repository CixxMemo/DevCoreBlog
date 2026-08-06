# DevCoreBlog — MVP Geliştirme Planı (Detaylı, Adım Adım)

---

## 1. TECH STACK (Sabit — Değiştirilemez)

| Katman | Teknoloji | Versiyon / Kaynak |
|---|---|---|
| Framework | ASP.NET Core MVC | net10.0 (csproj'de tanımlı) |
| ORM | Entity Framework Core | 10.0.10 |
| Veritabanı | PostgreSQL | localhost / DevCoreBlogDb |
| EF Provider | Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| Kimlik Doğrulama | Cookie Authentication | Framework içi (`Microsoft.AspNetCore.Authentication.Cookies`) — **yeni paket YOK** |
| View Engine | Razor (.cshtml) | Server-side, tag helper'lı |
| CSS | Bootstrap 5 | Template'te mevcut (`wwwroot/lib/bootstrap`) |
| JS | jQuery + jQuery Validation | Sadece template'te gelen, form validasyonu için |
| Mimari | Düz MVC (Controller → DbContext → View) | Katmansız |

## 2. SINIRLAR (AI Guardrails — KESİNLİKLE UYULACAK)

Aşağıdakiler **YASAKTIR**. Plan dışına çıkma, ekleme yapma:

- ❌ ASP.NET Core Identity, rol/claim tabanlı yetki matrisi, kullanıcı tablosu **YOK**
- ❌ Repository Pattern, Unit of Work, Service/Manager katmanı, Interface soyutlaması **YOK**
- ❌ DTO, ViewModel klasörü, AutoMapper **YOK** (entity'ler doğrudan view'a gider)
- ❌ Areas **YOK** (Admin controller'ları düz `Controllers/` altında, `Admin` prefix'i ile)
- ❌ Web API endpoint'i, JWT, SPA, Blazor, React **YOK**
- ❌ Yeni NuGet paketi **YOK** (cookie auth framework içinde)
- ❌ Dosya/resim yükleme, zengin metin editörü (TinyMCE vb.) **YOK** — `Content` düz `<textarea>`
- ❌ Pagination, arama, etiket (tag) sistemi, yorum sistemi **YOK**
- ❌ Unit test projesi, Docker, CI/CD **YOK**
- ❌ `Post` / `Category` entity'lerine yeni alan **EKLENMEYECEK** → dolayısıyla **yeni migration YOK**
- ❌ SEO paketi, sitemap, RSS, e-posta gönderimi **YOK**
- ✅ Her madde bittiğinde `dotnet build` çalıştırılıp hatasız geçtiği doğrulanacak
- ✅ Bir faza başlamadan önceki fazın tamamı bitmiş olacak; fazlar arası atlama yapılmayacak
- ✅ **Her kod dosyası / kod parçası için İngilizce açıklayıcı yorum satırları eklenecektir.** Yorumlar dosyanın en üstünde ne yaptığını açıklayan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını anlatan satır içi yorumlar şeklinde olacaktır. Bu kural her fazda oluşturulan veya düzenlenen tüm dosyalar için geçerlidir.

## 3. MEVCUT DURUM (Koddan Doğrulandı)

- `Models/Post.cs`: Id, Title, Slug, Summary, Content, CreatedAt, IsPublished, CategoryId, Category (navigation)
- `Models/Category.cs`: Id, Name, Slug, Posts (navigation)
- `Data/ApplicationDbContext.cs`: `DbSet<Post> Posts`, `DbSet<Category> Categories`
- `Program.cs`: Npgsql kayıtlı, `AddControllersWithViews()` var, auth middleware **yok**, default route `{controller=Home}/{action=Index}/{id?}`
- `appsettings.json`: `DefaultConnection` mevcut, `AdminCredentials` bölümü **yok**
- `Controllers/HomeController.cs`: template hali (Index/Privacy/Error), DbContext inject **edilmemiş**
- `Views/Shared/_Layout.cshtml`: Bootstrap navbar (Home/Privacy linkleri), kategori menüsü **yok**
- Migration `InitialCreate` PostgreSQL'e uygulanmış — DB hazır

## 4. ALINAN KARARLAR

1. **Public URL'ler slug bazlı olacak** (`/yazi/ilk-blog-yazisi`, `/kategori/c-sharp-dersleri`) — çünkü `Slug` alanları entity'de zaten var. `Program.cs`'e 2 ek route yeterli.
2. **Admin URL'leri id bazlı olacak** (`/AdminPost/Edit/5`) — default route ile çalışır, ekstra route gerekmez.
3. **Slug, formdan elle girilmeyecek**; `Helpers/SlugGenerator.cs` ile Name/Title'dan otomatik üretilecek (TR karakter dönüşümü: ç→c, ğ→g, ı→i, ö→o, ş→s, ü→u).
4. **Entity direkt bind edileceği için** POST action'larda `ModelState.Remove("Slug")` ve Post için `ModelState.Remove("Category")` (navigation) yapılacak — aksi halde implicit `[Required]` validasyonu patlar.
5. **Admin şifresi** `appsettings.json` içinde düz metin — tek kullanıcılı MVP için kabul edilen sınır.
6. **Navbar kategori menüsü** `_Layout.cshtml`'e `@inject ApplicationDbContext` ile beslenecek (ViewComponent yok — over-engineering).
7. **Kategori silme koruması**: kategoriye bağlı yazı varsa silme engellenecek (aksi halde cascade delete yazıları uçurur), `TempData` ile hata mesajı gösterilecek.

---

## 5. FAZLAR

### FAZ 1 — Admin Layout + Dashboard (iskelet)

> **📝 YORUM KURALI:** Bu fazda oluşturulan veya düzenlenen tüm kod dosyalarına İngilizce açıklayıcı yorum satırları eklenecektir. Her dosyanın en üstünde dosyanın ne yaptığını anlatan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını açıklayan satır içi yorumlar yazılacaktır.

- [x] **1.1** `Views/Shared/_AdminLayout.cshtml` **oluştur** → Bootstrap'li admin layout: üst bar ("DevCoreBlog Admin") + sol menü (Dashboard, Kategoriler, Yazılar, Siteyi Görüntüle `/`, Çıkış). `_Layout.cshtml`'deki `<head>` (bootstrap + site.css) aynen taşınacak. Çıkış linki şimdilik `asp-controller="Account" asp-action="Logout"` işaret etsin (Faz 4'te çalışır hale gelecek).
- [x] **1.2** `Controllers/AdminController.cs` **oluştur** → Sadece `public IActionResult Dashboard() => View();` içersin.
- [x] **1.3** `Views/Admin/_ViewStart.cshtml` **oluştur** → İçerik: `@{ Layout = "_AdminLayout"; }`
- [x] **1.4** `Views/Admin/Dashboard.cshtml` **oluştur** → "Admin Paneli" başlığı + 2 linkli kart (Kategori Yönetimi → `/AdminCategory`, Yazı Yönetimi → `/AdminPost`).
- [x] **1.5** **Doğrula** → `dotnet build` + tarayıcıda `/Admin/Dashboard` açılıyor mu?

### FAZ 2 — Admin Kategori CRUD (İLK CRUD)

> **📝 YORUM KURALI:** Bu fazda oluşturulan veya düzenlenen tüm kod dosyalarına İngilizce açıklayıcı yorum satırları eklenecektir. Her dosyanın en üstünde dosyanın ne yaptığını anlatan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını açıklayan satır içi yorumlar yazılacaktır.

- [x] **2.1** `Controllers/AdminCategoryController.cs` **oluştur** → Constructor'da `ApplicationDbContext` inject et. `Index()` action: `_context.Categories.ToListAsync()` → view'a liste gönder.
- [x] **2.2** `Views/AdminCategory/_ViewStart.cshtml` **oluştur** → `@{ Layout = "_AdminLayout"; }`
- [x] **2.3** `Views/AdminCategory/Index.cshtml` **oluştur** → `@model List<Category>`; tablo (Id, Name, Slug, Yazı Sayısı `@item.Posts.Count` yerine `_context` karıştırma → controller'da `Include(c => c.Posts)` kullan) + "Yeni Kategori" butonu + satır başına Düzenle linki ve Sil formu (küçük POST formu, `onsubmit="return confirm(...)"`). `TempData["Error"]` varsa üstte alert göster (2.8'deki silme koruması için).
- [x] **2.4** `Helpers/SlugGenerator.cs` **oluştur** → `public static string Generate(string text)`: TR karakter dönüşümü + küçük harf + harf/rakam dışını `-` yap + çift tireleri tekle + baş/son tireyi kırp.
- [x] **2.5** `Controllers/AdminCategoryController.cs` **düzenle** → `Create()` GET (boş view döner) + `Create(Category category)` POST: `ModelState.Remove("Slug")` → `category.Slug = SlugGenerator.Generate(category.Name)` → `ModelState.IsValid` kontrol → `Add` + `SaveChangesAsync` → `RedirectToAction(nameof(Index))`.
- [x] **2.6** `Views/AdminCategory/Create.cshtml` **oluştur** → `@model Category`; sadece `Name` inputu (label + `asp-validation-for`) + Kaydet butonu + Index'e dön linki.
- [x] **2.7** `Controllers/AdminCategoryController.cs` **düzenle** → `Edit(int id)` GET: `FindAsync(id)`, null ise `NotFound()` → `Edit(int id, Category category)` POST: aynı slug mantığı + `Update` + `SaveChangesAsync`.
- [x] **2.8** `Views/AdminCategory/Edit.cshtml` **oluştur** → Create ile aynı form + gizli `Id` alanı (`<input type="hidden" asp-for="Id" />`).
- [x] **2.9** `Controllers/AdminCategoryController.cs` **düzenle** → `Delete(int id)` `[HttpPost]` action: kategoriyi `Include(c => c.Posts)` ile bul → **Posts.Count > 0 ise** `TempData["Error"] = "Bu kategoriye ait yazılar var, önce onları silin/taşıyın."` → Index'e redirect. Yoksa `Remove` + `SaveChangesAsync` → redirect.
- [x] **2.10** **Doğrula** → `dotnet build` + `/AdminCategory` altında ekle/düzenle/sil/listele turu.

### FAZ 3 — Admin Yazı (Post) CRUD

> **📝 YORUM KURALI:** Bu fazda oluşturulan veya düzenlenen tüm kod dosyalarına İngilizce açıklayıcı yorum satırları eklenecektir. Her dosyanın en üstünde dosyanın ne yaptığını anlatan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını açıklayan satır içi yorumlar yazılacaktır.

- [x] **3.1** `Controllers/AdminPostController.cs` **oluştur** → DbContext inject + `Index()`: `_context.Posts.Include(p => p.Category).OrderByDescending(p => p.CreatedAt).ToListAsync()`.
- [x] **3.2** `Views/AdminPost/_ViewStart.cshtml` **oluştur** → `@{ Layout = "_AdminLayout"; }`
- [x] **3.3** `Views/AdminPost/Index.cshtml` **oluştur** → `@model List<Post>`; tablo (Id, Title, Kategori `@item.Category.Name`, Yayında mı ✓/✗, CreatedAt) + "Yeni Yazı" butonu + Düzenle/Sil.
- [x] **3.4** `Controllers/AdminPostController.cs` **düzenle** → `Create()` GET: `ViewBag.CategoryId = new SelectList(_context.Categories, "Id", "Name")` → `Create(Post post)` POST: `ModelState.Remove("Slug")`, `ModelState.Remove("Category")` → slug üret → `post.CreatedAt = DateTime.UtcNow` → kaydet.
- [x] **3.5** `Views/AdminPost/Create.cshtml` **oluştur** → `@model Post`; Title input, Summary textarea (3 satır), Content textarea (15 satır), Category dropdown (`asp-items="ViewBag.CategoryId"`), IsPublished checkbox, Kaydet butonu.
- [x] **3.6** `Controllers/AdminPostController.cs` **düzenle** → `Edit(int id)` GET: bul + dropdown'u seçili değerle doldur (`new SelectList(..., post.CategoryId)`) → `Edit(int id, Post post)` POST: aynı ModelState/slug mantığı + `Update`.
- [x] **3.7** `Views/AdminPost/Edit.cshtml` **oluştur** → Create formunun aynısı + gizli `Id`.
- [x] **3.8** `Controllers/AdminPostController.cs` **düzenle** → `Delete(int id)` `[HttpPost]`: bul, `Remove`, kaydet, redirect (yazı silmede koruma gerekmez).
- [x] **3.9** **Doğrula** → `dotnet build` + `/AdminPost` CRUD turu; kategori dropdown'un dolu geldiğini kontrol et.

### FAZ 4 — Cookie Authentication (Tek Kullanıcı)

> **📝 YORUM KURALI:** Bu fazda oluşturulan veya düzenlenen tüm kod dosyalarına İngilizce açıklayıcı yorum satırları eklenecektir. Her dosyanın en üstünde dosyanın ne yaptığını anlatan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını açıklayan satır içi yorumlar yazılacaktır. Ve her faz bitiminde  [ ] parantezi [x] olarak doldurulacaktır.

- [x] **4.1** `appsettings.json` **düzenle** → Ekle: `"AdminCredentials": { "Username": "admin", "Password": "BURAYA_GUVENLI_BIR_SIFRE" }`
- [x] **4.2** `Program.cs` **düzenle** → `AddControllersWithViews()` sonrasına:
  `builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => { o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Login"; });`
  ve `app.UseRouting();` sonrasına **`app.UseAuthentication();`** (UseAuthorization'dan **önce** olmak zorunda).
- [x] **4.3** `Controllers/AccountController.cs` **oluştur** → `IConfiguration` inject.
  - `Login()` GET: kullanıcı zaten girişliyse `/Admin/Dashboard`'a yönlendir, değilse view.
  - `Login(string username, string password)` POST: config'deki değerlerle eşleşirse → `Claim(ClaimTypes.Name, username)` içeren `ClaimsIdentity` (cookie şeması ile) → `HttpContext.SignInAsync(...)` → `/Admin/Dashboard`'a redirect. Eşleşmezse `ViewBag.Error = "Kullanıcı adı veya şifre hatalı."`
  - `Logout()` `[HttpPost]`: `HttpContext.SignOutAsync()` → `/`'e redirect.
- [x] **4.4** `Views/Account/Login.cshtml` **oluştur** → Standart `_Layout` kullanır (admin layout DEĞİL); ortalanmış küçük kart: Kullanıcı Adı + Şifre inputu (name attribute'ları `username`/`password` olacak), Giriş butonu, `ViewBag.Error` alert'i.
- [x] **4.5** `Controllers/AdminController.cs` **düzenle** → Sınıfa `[Authorize]` ekle.
- [x] **4.6** `Controllers/AdminCategoryController.cs` **düzenle** → Sınıfa `[Authorize]` ekle.
- [x] **4.7** `Controllers/AdminPostController.cs` **düzenle** → Sınıfa `[Authorize]` ekle.
- [x] **4.8** `Views/Shared/_AdminLayout.cshtml` **düzenle** → Çıkış menü elemanını `<form asp-controller="Account" asp-action="Logout" method="post">` içinde butona çevir (GET ile logout olmaz).
- [x] **4.9** **Doğrula** → `dotnet build` + gizli sekmede `/AdminCategory` → Login'e yönlendiriyor mu? Yanlış şifre → hata mesajı. Doğru şifre → Dashboard. Çıkış → tekrar erişim engelleniyor mu?

### FAZ 5 — Public UI (Ziyaretçi Tarafı)

> **📝 YORUM KURALI:** Bu fazda oluşturulan veya düzenlenen tüm kod dosyalarına İngilizce açıklayıcı yorum satırları eklenecektir. Her dosyanın en üstünde dosyanın ne yaptığını anlatan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını açıklayan satır içi yorumlar yazılacaktır.Ve her faz bitiminde  [ ] parantezi [x] olarak doldurulacaktır.

- [x] **5.1** `Program.cs` **düzenle** → Default route'un **üstüne** 2 route ekle:
  `app.MapControllerRoute("post", "yazi/{slug}", new { controller = "Home", action = "Detail" });`
  `app.MapControllerRoute("category", "kategori/{slug}", new { controller = "Home", action = "Category" });`
  (Her ikisine de `.WithStaticAssets()` zinciri eklenmeyecek — sadece default route'ta kalacak.)
- [x] **5.2** `Controllers/HomeController.cs` **düzenle** → `ApplicationDbContext` inject et + `Index()` güncelle: `_context.Posts.Include(p => p.Category).Where(p => p.IsPublished).OrderByDescending(p => p.CreatedAt).ToListAsync()` → view'a gönder.
- [x] **5.3** `Views/Home/Index.cshtml` **düzenle** → `@model List<Post>`; her yazı için Bootstrap card: Title, Summary, `badge` ile kategori adı (link: `/kategori/@item.Category.Slug`), CreatedAt (`dd.MM.yyyy`), "Devamını Oku" butonu (`/yazi/@item.Slug`). Yazı yoksa "Henüz yazı yok." mesajı.
- [x] **5.4** `Controllers/HomeController.cs` **düzenle** → `Detail(string slug)` action ekle: `Include(p => p.Category)` + `FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished)` → null ise `NotFound()`.
- [x] **5.5** `Views/Home/Detail.cshtml` **oluştur** → `@model Post`; Title (h1), kategori badge linki + tarih satırı, `@Model.Content` içeriği. Ana sayfaya dön linki.
- [x] **5.6** `Controllers/HomeController.cs` **düzenle** → `Category(string slug)` action ekle: kategoriyi `FirstOrDefaultAsync(c => c.Slug == slug)` (null → `NotFound()`) → yazıları `Where(p => p.CategoryId == category.Id && p.IsPublished)` ile çek → `ViewBag.CategoryName = category.Name` → view'a yazı listesi gönder.
- [x] **5.7** `Views/Home/Category.cshtml` **oluştur** → `@model List<Post>`; üstte `"@ViewBag.CategoryName" kategorisindeki yazılar` başlığı + Index'teki kart yapısının aynısı. Boşsa "Bu kategoride yazı yok."
- [x] **5.8** `Views/Shared/_Layout.cshtml` **düzenle** → Başa `@inject DevCoreBlog.Data.ApplicationDbContext DbContext` → navbar'daki "Privacy" linkini kaldır → "Kategoriler" dropdown'ı ekle (`DbContext.Categories.ToList()` ile döngü, her biri `/kategori/@cat.Slug` linki) → footer'ı sadeleştir (`© 2026 DevCoreBlog`).
- [x] **5.9** **Temizlik** → `Views/Home/Privacy.cshtml` **sil** + `HomeController.Privacy()` action'ını **sil**.
- [x] **5.10** **Doğrula** → `dotnet build` + tur: `/` yazı listesi → bir yazıya tıkla (`/yazi/...`) → navbar'dan kategori seç (`/kategori/...`) → olmayan slug → 404.

### FAZ 6 — Son Rötuşlar

> **📝 YORUM KURALI:** Bu fazda oluşturulan veya düzenlenen tüm kod dosyalarına İngilizce açıklayıcı yorum satırları eklenecektir. Her dosyanın en üstünde dosyanın ne yaptığını anlatan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını açıklayan satır içi yorumlar yazılacaktır.Ve her faz bitiminde  [ ] parantezi [x] olarak doldurulacaktır.

- [x] **6.1** `wwwroot/css/site.css` **düzenle** → Sadece: kart hover efekti, `.card` alt boşlukları, admin tablo düzenlemesi, textarea fontu (monospace opsiyonel). Tema/renk paleti değişikliği YOK.
- [x] **6.2** `Views/Admin/Dashboard.cshtml` **düzenle** → Kartlara toplam kategori/yazı sayısı göster (controller'da `ViewBag` ile 2 sayı geç).
- [x] **6.3** **Final doğrulama** → `dotnet build` hatasız + tam kullanıcı turu: login → kategori ekle → yazı ekle → logout → `/`'de görünüyor mu → `/kategori/...` filtreliyor mu → `/yazi/...` açılıyor mu.

---

## 6. RİSKLER / DİKKAT EDİLECEKLER

- **Middleware sırası**: `UseAuthentication()` mutlaka `UseAuthorization()`'dan önce olacak — yanlış sırada `[Authorize]` sessizce çalışmaz.
- **Implicit `[Required]`**: `Slug` ve `Category` navigation'ı POST'ta validasyonu patlatır → `ModelState.Remove(...)` adımları atlanmayacak.
- **Cascade delete**: Kategori silinirken yazı koruması (madde 2.9) atlanırsa yazılar sessizce silinir.
- **DateTime**: `CreatedAt` her zaman `DateTime.UtcNow` (PostgreSQL `timestamp with time zone` uyumu) — `DateTime.Now` KULLANILMAYACAK.
- **Şifre**: `appsettings.json` içinde düz metin; repo public'e gidecekse dosya git'e commit edilmemeli (MVP kapsamı dışı, sadece not).

## 7. KAPSAM DIŞI (MVP sonrası backlog — bu planda YAPILMAYACAK)

Sayfalama, arama, etiket sistemi, yorumlar, resim yükleme, zengin metin editörü, çok kullanıcılı Identity, SEO/sitemap/RSS, caching, testler, deployment.
