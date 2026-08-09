# DevCoreBlog - Geliştirme ve Genişletme Planı (Faz 1-3)

## 1. TECH STACK (Sabit — Sadece Belirtilenler Kullanılacak)

| Katman | Teknoloji | Notlar |
|---|---|---|
| Framework | ASP.NET Core MVC | net10.0 |
| ORM | Entity Framework Core | 10.0.10 |
| Veritabanı | PostgreSQL | localhost / DevCoreBlogDb |
| Kimlik Doğrulama | Cookie Authentication | Framework içi (`Microsoft.AspNetCore.Authentication.Cookies`) |
| View Engine | Razor (.cshtml) | Server-side |
| CSS | Tailwind CSS | Tech Minimal Tasarım |
| Mimari | N-Tier Klasör Mimarisi | Core, Repositories, Services katmanları |

## 2. SINIRLAR & MASTER RULES (AGENTS.md Kuralları Geçerlidir)

- ❌ **ASP.NET Core Identity, Rol/Claim YOK.**
- ❌ **DTO ve AutoMapper YOK.**
- ❌ **Web API, JWT, SPA, React YOK.**
- ❌ **Yapay Zeka Tasarımı (Glassmorphism, Abartılı Gölgeler) YOK.** Tech Minimal esastır.
- ✅ **Junior-Friendly English Code Comments** zorunludur.
- ✅ **Gereksiz uzun kod yazmak (Over-engineering)** yasaktır.
- ✅ **Fazları tamamladıktan sonra fazlar arası bağlantıları kontrol et**
- ✅ **Her fazı tamamladıktan sonra [ ] içlerini x ile doldur**

---
## Önceki Planın Fazları (Tamamlandı)
- [x] Eski Faz 1: Klasör Bazlı Katmanlı Mimari (SOLID) Dönüşümü
- [x] Eski Faz 2: Tech Minimal Tasarım (Tailwind CSS Entegrasyonu)
- [x] Eski Faz 3: Geliştirici Odaklı Yeni Özellikler (Hızlı Kazanımlar)

---

## 🎨 Faz 1: Okuyucu Deneyimi (Frontend & UI) Geliştirmeleri

Bu fazda ziyaretçilerin blogda daha fazla vakit geçirmesi ve daha iyi bir okuma deneyimi yaşaması sağlanacaktır.

- [x] **Faz 1.1: Aydınlık / Karanlık Mod (Dark Mode Toggle)**
  - [x] Navbar'a karanlık mod / aydınlık mod geçişi yapacak şık bir buton ekle.
  - [x] Tailwind'in `dark:` sınıflarını kullanarak tüm temanın karanlık modda kusursuz görünmesini sağla.
  - [x] Kullanıcının tercihini tarayıcı çerezine (cookie) veya `localStorage`'a kaydederek sayfa yenilendiğinde hatırlanmasını (Vanilla JS ile) sağla.

- [x] **Faz 1.2: Benzer Yazılar (Related Posts)**
  - [x] `PostService` içerisine `GetRelatedPostsAsync(int currentPostId, int categoryId)` metodu ekle. Aynı kategorideki diğer 3 yazıyı getirsin.
  - [x] Yazı detay sayfasının (`Detail.cshtml`) en altına "Benzer Yazılar" başlığıyla bu 3 yazıyı listele.

- [x] **Faz 1.3: Sayfalama (Pagination)**
  - [x] `HomeController.Index` ve `Category` aksiyonlarına sayfalama mantığı ekle (Örn: `page` parametresi alarak her sayfada 9 yazı göster).
  - [x] Sayfanın en altına Tailwind ile şık bir "Önceki Sayfa | Sonraki Sayfa" buton yapısı ekle.

---

## ⚙️ Faz 2: Admin ve İçerik Yönetimi Geliştirmeleri

Bu fazda admin panelinin yetenekleri artırılarak gerçek bir CMS deneyimi sunulacaktır.

- [x] **Faz 2.1: Görsel Yükleme Sistemi (Image Upload)**
  - [x] `AdminPostController` içerisine upload aksiyonu ekle. Yüklenen görselleri `wwwroot/uploads` klasörüne kaydet.
  - [x] Admin panelinde, yazı formuna basit bir "Görsel Yükle" butonu ekle. Yükleme sonrası admin'e Markdown kodunu kopyalama imkanı sun (Vanilla JS ile).

- [x] **Faz 2.2: İleri Tarihli Yayınlama (Scheduled Posts)**
  - [x] `Post` modeline `PublishDate` (DateTime) propertysi ekle (Migration gerektirir).
  - [x] `PostService`'deki tüm "aktif yazıları getir" sorgularına şu şartı ekle: `IsActive == true && PublishDate <= DateTime.UtcNow`.
  - [x] Admin panelindeki formlara "Yayınlanma Tarihi" input'u ekle. İleri tarih seçilen yazılar o an gelene kadar ziyaretçilere gizli kalsın.

---

## 🚀 Faz 3: Performans ve SEO Geliştirmeleri

Projenin arama motorlarındaki görünürlüğü ve hızı maksimize edilecektir.

- [x] **Faz 3.1: Dinamik Sitemap (.xml)**
  - [x] `/sitemap.xml` rotasına hizmet verecek bir aksiyon ekle.
  - [x] Veritabanındaki tüm aktif yazıları ve kategorileri standart XML formatında render ederek SEO uyumluluğunu sağla.

- [x] **Faz 3.2: Output Caching (Performans Artışı)**
  - [x] ASP.NET Core `Output Caching` middleware'ini projeye dahil et.
  - [x] Ziyaretçi sayfalarını (Anasayfa, Kategori, Yazı Detay) kısa süreliğine önbelleğe alarak veritabanı yükünü sıfıra indir.
  - [x] *Test Case:* Sayfa yüklendiğinde veritabanına giden sorguları terminalden izle ve önbellekten dönerken DB'nin yorulmadığını doğrula.

---
**Son Durum Kontrolü:** Tüm fazlar bittiğinde kodda yapay zeka jargonu içeren hiçbir metin kalmamış olmalı; `AGENTS.md` kurallarına sıkı sıkıya uyulmalıdır.
