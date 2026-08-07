# DevCoreBlog - Geliştirme ve Genişletme Planı (Faz 1-3)

## 1. TECH STACK (Sabit — Sadece Belirtilenler Kullanılacak)

| Katman | Teknoloji | Notlar |
|---|---|---|
| Framework | ASP.NET Core MVC | net10.0 (csproj'de tanımlı)[cite: 1] |
| ORM | Entity Framework Core | 10.0.10[cite: 1] |
| Veritabanı | PostgreSQL | localhost / DevCoreBlogDb[cite: 1] |
| Kimlik Doğrulama | Cookie Authentication | Framework içi (`Microsoft.AspNetCore.Authentication.Cookies`) — Değiştirilmeyecek[cite: 1] |
| View Engine | Razor (.cshtml) | Server-side, tag helper'lı[cite: 1] |
| CSS | Tailwind CSS | Bootstrap tamamen bırakılıp Tailwind'e geçilecek[cite: 1] |
| Mimari | N-Tier Klasör Mimarisi | Core, Repositories, Services katmanları tek proje içinde klasörlenecek |
| Ek Paketler | Markdig, Prism.js | Sadece Markdown ve Syntax Highlighting için eklenecek |

## 2. SINIRLAR & MASTER RULES (AI Guardrails — KESİNLİKLE UYULACAK)

Aşağıdakiler **YASAKTIR**. Plan dışına çıkma, ekleme yapma:

- ❌ **ASP.NET Core Identity, Rol/Claim YOK:** Mevcut basit Cookie Authentication yapısı korunacaktır[cite: 1].
- ❌ **DTO ve AutoMapper YOK:** Mimariyi aşırı karmaşıklaştırmamak için Servisler doğrudan Entity (Post, Category) döndürecektir[cite: 1].
- ❌ **Web API, JWT, SPA, React YOK:** Sistem tamamen Server-Side MVC (Razor) kalacaktır[cite: 1].
- ❌ **Yorum Sistemi ve Pagination YOK:** Dev loop'u uzatacak bu özelliklere girilmeyecektir[cite: 1].
- ❌ **AI Slop Tasarım YASAK:** CSS/UI tasarımlarında mor/mavi neon gradyanlar, `backdrop-blur` (glassmorphism), devasa gölgeler (`shadow-2xl`) ve abartılı yuvarlak köşeler (`rounded-3xl`) KESİNLİKLE kullanılmayacaktır. Tech Minimal (Keskin hatlar, monospaced kod fontları, yüksek kontrast) uygulanacaktır.
- ❌ **Ayrı Proje (.csproj) YOK:** Katmanlar `DevCoreBlog` projesi içinde `/Core`, `/Repositories`, `/Services` klasörlerinde fiziksel olarak ayrılacaktır.

Aşağıdakiler **ZORUNLUDUR**:

- ✅ **Söyle-Yap-Anlat Döngüsü (Junior-Friendly Summary):** 
  1. Kodlama bittikten sonra sohbet ekranında Junior seviyesindeki bir yazılımcının anlayacağı sadelikte **"Ne Yaptım ve Neden Yaptım?"** başlıklı kısa bir teknik açıklama sunacaktır.
- ✅ **Sadece Gerekli Migration:** Sadece `ViewCount` propertysi için EF Core Migration izni vardır. Bunun dışında mevcut `Post` ve `Category` yapısı bozulmayacaktır[cite: 1].
- ✅ **Sıralı İşlem:** Bir faza başlamadan önceki fazın tamamı bitmiş olacak; görevler sırayla yapılacak, atlama yapılmayacaktır[cite: 1].
- ✅ **Sürekli Build Kontrolü:** Her bir `.cs` dosyası değişikliğinde veya madde bitiminde terminalde `dotnet build` çalıştırılıp projenin hatasız derlendiği doğrulanacaktır[cite: 1].
- ✅ **Junior-Friendly English Code Comments:** Eklenecek veya değiştirilecek her C# sınıfına, interface'e, metoda ve karmaşık LINQ sorgularına açıklayıcı İngilizce yorum satırları (`//`) eklenecektir[cite: 1]. Yorumlar dosyanın en üstünde ne yaptığını açıklayan bir başlık bloğu, her önemli satır/blok için ise ne işe yaradığını anlatan satır içi yorumlar şeklinde olacaktır[cite: 1].
- ✅ **Planın kullanıcı tarafından rahatça takip edilebilemsi:** her faz ı bitirdiğinde her fazın başında olan [ ] parantezini [x] ile doldur.
---

## 🛠️ Faz 1: Klasör Bazlı Katmanlı Mimari (SOLID) Dönüşümü

Bu fazda, mevcut Controller içine sıkışmış veritabanı işlemleri soyutlanacak ve servis katmanına taşınacaktır. Yazılan her koda eğitici İngilizce yorum satırları eklenecektir.

- [x] **Faz 1.1: Core Katmanının Hazırlanması**
  - [x] `Models` klasörünün adını `Core/Entities` olarak değiştir[cite: 1].
  - [x] Tüm entity'lerin (Post, Category) miras alacağı `BaseEntity.cs` (Id, CreatedDate, IsActive) sınıfını oluştur ve uygula[cite: 1].
  - [x] `Core/Interfaces/IRepository.cs` generic arayüzünü oluştur (GetById, GetAll, Add, Update, Delete)[cite: 1].
  

- [x] **Faz 1.2: Repository (Data) Katmanının Kurulması**
  - [x] `ApplicationDbContext`'i kök dizindeki `/Data` klasöründe tutmaya devam et[cite: 1].
  - [x] `/Repositories/GenericRepository.cs` sınıfını oluştur ve `IRepository<T>`'yi implemente et[cite: 1].
  - [x] `/Repositories/PostRepository.cs` ve `/Repositories/CategoryRepository.cs` sınıflarını (gerekirse özel sorgular için) oluştur[cite: 1].
  - [x] *Process Requirement:* Generic Repository mantığını ve `DbSet<T>` kullanımını açıklayan yorumlar ekle ve sohbet ekranında eğitim özetini geç.

- [x] **Faz 1.3: Service Katmanının Kurulması**
  - [x] `/Services/Interfaces/IPostService.cs` ve `ICategoryService.cs` arayüzlerini oluştur[cite: 1].
  - [x] `/Services/PostService.cs` ve `CategoryService.cs` sınıflarını oluştur. İş kurallarını (örn. Slug kontrolü) buraya taşı[cite: 1].
  - [ ] *Process Requirement:* Service katmanının iş kuralı (business logic) tutma rolünü açıkla ve özet sun.

- [x] **Faz 1.4: Controller'ların Temizlenmesi ve DI (Dependency Injection)**
  - [x] `Program.cs` dosyasına Repository ve Service sınıflarını `AddScoped` ile ekle[cite: 1].
  - [x] `HomeController`, `AdminPostController`, `AdminCategoryController` içindeki `ApplicationDbContext` bağımlılıklarını kaldır; sadece `IPostService` ve `ICategoryService` kullan[cite: 1].
  - [x] *Process Requirement:* Controller constructor'larındaki Dependency Injection (DI) yapısını açıkla.
  - [x] *Test Case:* Uygulamayı çalıştır ve anasayfanın, kategori ve yazı detaylarının mevcut haliyle hatasız çalıştığını doğrula[cite: 1].

---

## 🎨 Faz 2: Tech Minimal Tasarım (Tailwind CSS Entegrasyonu)

Bu fazda klasik görünümler çöpe atılıp keskin, geliştirici odaklı UI inşa edilecektir.

- [x] **Faz 2.1: Tailwind Entegrasyonu**
  - [x] Projeye Tailwind CSS'i CDN veya NPM (hangisi hızlıysa) üzerinden dahil et[cite: 1].
  - [x] `tailwind.config.js` oluştur (veya ayarla) ve Master Rule'daki renk paletini (Dark/Light Tech Minimal) zorunlu kıl.

- [x] **Faz 2.2: _Layout ve Anasayfa Revizyonu**
  - [x] `_Layout.cshtml` dosyasını Tech Minimal tarzına uygun, üstte basit bir navbar, ortada içerik, altta footer olacak şekilde (gereksiz gölgesiz) baştan yaz[cite: 1].
  - [x] `Index.cshtml` içindeki blog kartlarını `rounded-sm`, border'lı, minimalist listelere veya kartlara dönüştür[cite: 1].

- [x] **Faz 2.3: Admin Paneli Görünümü**
  - [x] `_AdminLayout.cshtml` ve altındaki listeleme (Index) tablolarını Tailwind'in temiz tablo tasarımlarıyla güncelle[cite: 1]. (Satır araları border'lı, hover efektli, monospaced font destekli).

---

## 🚀 Faz 3: Geliştirici Odaklı Yeni Özellikler (Hızlı Kazanımlar)

Bu faz, sistemi yormadan bloğun kalitesini artıracaktır. Eklenen C# metodlarına İngilizce eğitici yorumlar konulacaktır.

- [x] **Faz 3.1: Markdown & Syntax Highlighting**
  - [x] Blog yazıları artık Markdown olarak kaydedilecektir. Gösterim sırasında Markdown'ı HTML'e çevirmek için `Markdig` NuGet paketini projeye dahil et[cite: 1].
  - [x] Kod bloklarının (Örn: C#, Python) Tech Minimal tarzında renklendirilmesi için `Prism.js` (veya Highlight.js) kütüphanesini `_Layout.cshtml` içine göm[cite: 1].
  - [x] *Test Case:* Yeni bir yazıda Markdown kodu (` ```csharp ` gibi) ekle ve ekranda renkli render edildiğini gör[cite: 1].

- [x] **Faz 3.2: Okuma Süresi ve Görüntülenme Sayısı**
  - [x] `Post` modeline `ViewCount` (int) propertysi ekle[cite: 1].
  - [x] Detay sayfasına girildiğinde (Service üzerinden) `ViewCount`'u artır[cite: 1].
  - [x] Makale detay sayfasında (`Detail.cshtml`) kelime sayısına bağlı dinamik "Okuma Süresi" (Örn: "4 min read") bilgisini hesaplayıp UI'a yaz[cite: 1].
  - [x] *Test Case:* EntityFramework Migration oluştur (`Add-Migration AddViewCount`) ve veritabanını güncelle. Tıklanan postun okunma sayısının arttığını kontrol et[cite: 1].

- [x] **Faz 3.3: Basit Arama ve Dinamik SEO (OG Etiketleri)**
  - [x] `HomeController` içine `Search(string query)` metodu ekle ve Navbar'a basit bir arama formu koy[cite: 1]. Sadece Başlık (Title) veya İçerik'te (Content) geçen kelimeleri filtrelesin[cite: 1].
  - [x] `_Layout.cshtml` `head` tagleri arasına dinamik `<meta property="og:title">`, `<meta property="og:description">` etiketleri ekle ve bu verileri aktif sayfadan (`ViewBag` üzerinden) al[cite: 1].
  - [x] *Test Case:* Arama kutusuna mevcut bir makale başlığını yaz ve doğru sonucun geldiğini teyit et. Sayfa kaynağını görüntüle ve OG etiketlerinin dolduğunu doğrula[cite: 1].

---
**Son Durum Kontrolü:** Tüm fazlar bittiğinde kodda "Based on...", "As an AI..." gibi yapay zeka jargonu içeren hiçbir metin kalmamış olmalı; sadece temiz, junior dostu İngilizce kod yorumları bulunmalıdır.