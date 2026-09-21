# DevCoreBlog — AI agent ile atomik geliştirme planı

**Tarih:** 21 Eylül 2026  
**Durum:** F00 kural/dokümantasyon yenilemesi tamamlandı; uygulama kodu fazları başlamadı. Sıradaki faz F01.  
**Temel:** [19 Eylül inceleme raporu](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/PROJE_INCELEME_RAPORU_2026-09-19.md) ve repository kökündeki güncel AGENTS.md (v2).  
**Kapsam:** 10 çalışma grubu, F00–F57 arasında 58 faz. Eski koşullu tek proje yolu yürürlükten kaldırıldı. Bunlar 58 gün veya 58 ayrı proje anlamına gelmez. Bir faz tek bir doğrulanabilir sonuçtur.

## Kullanıcı için nasıl kullanılacak?

Kod yazman, terminal komutu çalıştırman veya dosya taşıman gerekmiyor. Agent seçilen fazın araştırma, kodlama ve doğrulamasını yapacak; sana sonucu ve sıradaki fazı söyleyecek. Senden yalnızca ürün tercihi, eksik gerçek bilgi veya henüz yetkilendirilmemiş canlı işlem kararı istenir.

1. Önce [güncel karar kaydını](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md) incele. Kurallar bu kullanıcı talebiyle yenilendi; F00 tamamlandı, sıradaki uygulama hazırlığı F01.
2. [hazır agent mesajlarından](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/AGENT_PROMPTLARI.md) başlangıç metnini kullan.
3. Bir faz bitince sonucu kontrol et. Devam etmek istediğinde aynı dosyadaki “sıradaki tek faz” mesajını gönder.
4. [ilerleme çizelgesi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) tek durum kaynağıdır. Agent geçmiş sohbeti hatırlamasa bile buradan devam edebilir.

Bu planı oluşturma talebi uygulama kodunu değiştirme veya bütün fazları çalıştırma talebi olarak yorumlanmaz.

## Kurallara uyum sözleşmesi

| Konu | Uygulanacak sınır |
|---|---|
| Yığın | ASP.NET Core MVC net10, EF Core 10, PostgreSQL, Razor ve Tailwind. |
| Mimari | Mevcut dört projeli modüler monolit; Services/Data → Core, Web composition root DI bağlantıları. F24/F26 bağımlılık borcunu giderir. |
| SPA/API | Site MVC kalır; mevcut webhook ve portföy feed sözleşmeleri korunur. Yeni SPA/genel API projesi kendiliğinden eklenmez. |
| Kimlik | Basit cookie authentication; Identity, yeni rol sistemi, kullanıcı veritabanı ve karmaşık claims mimarisi yok. |
| Veri aktarımı | Küçük input DTO/typed ViewModel güvenlik ve okunabilirlik için izinli; açık alan eşleme zorunlu. Gereksiz DTO/mapper ağı ve AutoMapper yok. |
| Kod | Kısa, anlaşılır, DRY. Gerekli domain interface'leri; CQRS/MediatR/soyutlama fabrikaları yok. |
| Açıklamalar | C# sınıf/interface, değiştirilmiş yöntem ve karmaşık mantıkta kısa English what/why yorumları; gerekli Razor bloklarında English açıklama. |
| Tasarım | Tech Minimal: keskin kenarlar, yüksek kontrast, temiz düzen. Glass, neon, aşırı radius/gölge yok. |
| Dil | Güncel kök kurala göre UI English; kullanıcı makaleleri kendi dilinde kalır. Kullanıcıya raporlama Türkçe. |
| Bağımlılıklar | Önce varlığını/sürümünü keşfet. Olmayan NuGet/JS araçlarını kullanma; yeni gereksinimi mevcut yığınla gerekçelendir. F35 için açık D04 kapısı vardır. |
| Secret | Gerçek .env, parola, cookie veya token'ı sohbete/loga/dokümana yazma; Cloudinary secret kaynağı environment olarak kalır. |
| İş akışı | Bir çalıştırmada bir atomik faz; build başarısızsa ilerleme; kanıtsız [x] yok. |

**Öncelik:** Sistem/çalışma ortamı talimatları ve kullanıcının açık isteği → repository kökündeki AGENTS.md → uyumlu güncel plan. `.agents/AGENTS.md` yalnızca yönlendirir; arşiv planları yürürlükte değildir. F00 bu geçişi tamamladı. SOLID ihlali ve spagetti kod yeni değişikliklerde yasaktır; puan hedefi bu yasağı gevşetmez.

## Çalışma grupları

| Grup | Fazlar | Kullanıcı açısından sonuç |
|---|---|---|
| [Hazırlık ve kural kararı](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/00_HAZIRLIK.md) | F00–F01 | Agent nereden başlayacağını ve hangi kuralları izleyeceğini bilir. |
| [Giriş, XSS, CSRF ve yükleme güvenliği](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/01_GUVENLIK.md) | F02–F09 | En doğrudan güvenlik açıkları kapanır. |
| [Migration, formlar ve veri kuralları](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/02_VERI_VE_CRUD.md) | F10–F15 | Yazı ve kategori işlemleri doğru veriyle çalışır. |
| [Yayın, cache ve eşzamanlılık](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/03_YAYIN_BUTUNLUGU.md) | F16–F22 | Taslaklar gizli, zamanlama doğru, kayıtlar ve URL'ler kararlı olur. |
| [Sorumluluk ayrımı ve SOLID](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/04_MIMARI.md) | F23–F26 | Davranışı koruyarak bağımlılıklar sadeleşir. |
| [Mevcut entegrasyonlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/05_ENTEGRASYONLAR.md) | F27–F29 | Korunmasına karar verilen mevcut otomasyon ve besleme güvenilir olur. |
| [Editör ve içerik üretimi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/06_EDITOR.md) | F30–F33 | Yazı oluşturma, kurtarma ve önizleme anlaşılır olur. |
| [UI, UX ve erişilebilirlik](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/07_UI_UX.md) | F34–F42 | Mobilde ve masaüstünde okuma/yönetim rahatlar. |
| [SEO, RSS ve site kimliği](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/08_SEO_VE_BLOG.md) | F43–F47 | Site kalıcı adreslerle bulunabilir ve takip edilebilir olur. |
| [Performans, işletim ve kabul](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/09_KALITE_VE_ISLETIM.md) | F48–F57 | Sonuç ölçülür, tekrar kurulabilir ve geri yüklenebilir olur. |

**Varsayılan uygulama sırası:** F00 → F01 → … → F57. Numaralı sırayı atlayıp UI çalışmasına başlanmaz. F00 tamamlandı; uygulama F01 ile başlar. F27–F29 mevcut entegrasyonları iyileştiren normal fazlardır. RSS ertelenirse F46 açık gerekçeyle ertelenir; tamamlandı sayılmaz.

**Geçiş eşikleri:**

- F09 sonrası: temel giriş/XSS/CSRF/yükleme güvenliği.
- F22 sonrası: güvenilir CRUD, yayın zamanı, kalıcı adres, cache ve eşzamanlı veri.
- F33 sonrası: sürdürülebilir temel mimari ve güvenilir içerik üretim akışı.
- F47 sonrası: okuyucu deneyimi, SEO ve temel blog kimliği.
- F57 sonrası: yalnızca kabul kanıtları uygunsa kullanıma/yayına hazırlık sonucu.

Bunlar ara kilometre taşlarıdır; örneğin F09'un bitmesi “site artık tamamen güvenli” demek değildir.

## Agent her fazı nasıl yürütecek?

1. AGENTS.md, bu README, KARARLAR, DURUM ve seçilen kartı oku. Eski planlar bağlayıcı değildir; gerektiğinde yalnızca tarihsel bağlam için arşivden oku.
2. Git durumunu ve dokunacağın dosyaların içeriğini oku. Başka agent'ın/developer'ın mevcut dirty veya untracked dosyalarını koru. HEAD tek başına kullanıcının güncel çalışması değildir.
3. Fazın hedefini tek paragrafta Türkçe açıkla. Öncülleri ve gerçek dosya/paket varlığını doğrula.
4. Yalnızca kartın sonucuna gereken küçük değişikliği yap. Rutin teknik tercihler için kullanıcıyı tekrar tekrar durdurma.
5. Her derlenebilir atomik kod değişikliğinden sonra doğru proje komutuyla build al. Fazın kabul kontrollerini çalıştır; başarısızsa bu fazı düzelt. Sadece dokümantasyon değişmişse link/kurallar/sıra doğrulaması yap; yapılmamış build'i geçmiş gibi yazma.
6. UI etkileniyorsa gerçek tarayıcıda mobil/masaüstü, ilgili etkileşim ve konsol kontrolünü yap. Kurulu browser doğrulama skill'i kullanılıyorsa önce talimatını oku.
7. Migration varsa hem boş test DB hem önceki şemalı test DB üzerinde yükseltmeyi doğrula. Gerçek veriyle toplu düzeltme veya prod migration'ı otomatik çalıştırma.
8. Diff'i incele; secret, gereksiz dosya ve kapsam dışı refactor bırakma. Test framework'ü sırf sayı için ekleme; davranışsal güvenlik/veri testleri değerlidir, CSS değişimine yansımalı unit test gerekmez.
9. [kayıt şablonuyla](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KAYIT_SABLONU.md) kanıtı yaz. DURUM'da yalnızca başarıyla doğrulanan fazı [x] yap.
10. Kullanıcıya ne değişti, neden, nasıl doğrulandı ve sıradaki tek fazı söyle; **dur**. Kendiliğinden sonraki faza geçme.

Bir kart gerçek kod incelendiğinde tek değişiklik için fazla büyürse önce aynı ID altında bağımlı alt kartlara böl; o çalıştırmada yalnızca ilk alt kartı uygula. Ana faz tüm alt kartlar geçmeden tamamlanmaz. Bu, kapsam büyütme izni değildir.

## Ortak tamamlanma ölçütü

Bir faz ancak hedeflenen davranış çalışıyor, ilgili eski davranışlar bozulmamış, build/kabul kanıtı kayıtlı, kurallara uyum sağlanmış ve geri dönüş yolu açıkken tamamlanır. Ekran görüntüsü DB doğruluğunun; mock testi Cloudinary'nin; hazır CI dosyası çalışmış CI'nın; lokal smoke canlı trafik testinin yerine geçmez.

Test ortamı yoksa erişilemeyen kısmı “doğrulanamadı” yaz. Eksik kanıtı kullanıcıya sade anlat; [x] koyma. İnceleme tarihindeki test sonucu güncel implementasyon için otomatik başarı değildir.

**Güvenlik fallback'leri:** Güvenilir invalidation yoksa ilgili cache kapalı kalabilir; medya doğrulaması eksikse yeni upload kapatılabilir; yapılandırma eksikse yönetici girişi açılmaz. Açığı geri getiren rollback kullanma.

## Rapor bulgularının faz karşılıkları

| İnceleme bulgusu | Fazlar |
|---|---|
| G1 eksik parola | F02, F07 |
| G2 Markdown XSS | F04, F33, F44, F51–F52 |
| G3 toast XSS | F03, F30 |
| G4 CSRF | F05, F33 |
| G5 gelecekteki/pasif içerik | F16–F18, F29, F32, F38–F39, F45–F46 |
| G6 giriş denemesi/parola | F06–F08, F50 |
| G7 upload | F09, F49 |
| G8 cache tutarsızlığı | F17–F18, F45 |
| G9 webhook/kaynak tüketimi | F27–F29; F40–F41 |
| G10 deployment/depo | F35, F43, F50–F56 |
| Rapor F1 opsiyonel kapak | Plan F11 |
| Rapor F2 slug | Plan F19–F20, F43 |
| Rapor F3 sayaç | Plan F21–F22 |
| Rapor F4 kategori edit | Plan F14 |
| Rapor F5 kategori delete | Plan F15 |
| Rapor F6 migration | Plan F10 |
| Rapor F7 hata işleme | Plan F23 |
| Rapor F8 validation/zaman | Plan F11–F13, F16–F17, F32, F42 |
| Spagetti/tekrar | F24–F26, F30, F35–F36 |
| UI/UX | F11, F30–F42 |
| SEO/blog kapsamı | F43–F47 |
| Performans/operasyon | F18, F29, F40–F41, F48–F56 |

Rapordaki F1 gibi bulgu kodları ile plandaki F01 gibi faz kodları farklıdır.

## SOLID hedefi ve doğrulama

Başlangıç raporu **56/100**: S 10/20, O 11/20, L 17/20, I 12/20, D 6/20. Hedef bandı **80–85/100**; teknik inceleme yapılmadan verilmiş yeni puan değildir.

| İlke | Değişiklik | Başarı kanıtı |
|---|---|---|
| S — tek sorumluluk | F23–F26, F30, F36 | View DB sorgulamaz; controller iş kuralı/medya ayrıntısı taşımaz; editör tek modüldür. |
| O — değişime açıklık | F24, F26 | Gerekli repository/medya/renderer sözleşmeleriyle implementasyon değişimi sınırlı kalır. |
| L — yerine geçebilirlik | F24 ve F57 | Repository implementasyonlarının null/not-found, görünürlük ve hata sözleşmesi tutarlı kalır. Sırf puan için inheritance eklenmez. |
| I — dar arayüz | F24 | Her tüketici ihtiyaç duymadığı işlemlere bağımlı değildir; interface başına tek method zorlaması yok. |
| D — soyutlamaya bağımlılık | F24–F26 | Servis concrete repository'ye, Razor DbContext'e ve domain vendor Markdown SDK'sına bağlı değildir. |

Tek veya dört csproj olmak kendi başına SOLID puanı vermez. F57 güncel dosya kanıtlarıyla aynı rubriği tekrar uygular.

## Tam blog kapsamı ve sonraya bırakılanlar

Bu planda hedef: tek yöneticili güvenli blog; taslak/zamanlı yayın, görsel, kurtarma/önizleme, kategoriler, arama/sayfalama, ilişkili/en çok okunan içerik, mobil/erişilebilir UI, kalıcı URL, SEO/sitemap, karar verilirse RSS, gerçek site kimliği, bakım ve geri yükleme.

Etiketler, seriler, sunucuda revision geçmişi, yorum/moderasyon, bülten, çok yazarlı rol sistemi, ödeme, AI içerik üretimi ve kişiselleştirme ayrı ürün kararlarıdır. Temel blogu bitirmek için hepsi zorunlu değildir. Özellikle çok yazarlı üyelik mevcut basit auth sınırını değiştirir; bu plana gizlice eklenmez.

Kesin gün veya agent mesaj sayısı sözü verilmez. Test ortamının ve gerçek deployment kararlarının hazır olması süreyi etkiler; ilerleme kanıtlanmış fazlarla ölçülür.

## Teknik dayanaklar

Uygulama anında mevcut sürümle tekrar karşılaştırılacak birincil kaynaklar:

- Cookie form koruması: [Microsoft antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).
- Built-in hash altyapısı: [Microsoft PBKDF2](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0).
- Markdown pipeline ve extension davranışları: [Markdig resmi proje](https://github.com/xoofx/markdig).
- Auth sonrası output cache sırası: [Microsoft Output Caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0).
- Play CDN'nin development sınırı: [Tailwind resmi dokümanı](https://tailwindcss.com/docs/installation/play-cdn).
- CSP raporlama/uygulama ayrımı: [OWASP CSP](https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html).

## Plan dosyaları

- [Hazırlık ve kural kararı](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/00_HAZIRLIK.md)
- [Giriş, XSS, CSRF ve yükleme güvenliği](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/01_GUVENLIK.md)
- [Migration, formlar ve veri kuralları](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/02_VERI_VE_CRUD.md)
- [Yayın, cache ve eşzamanlılık](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/03_YAYIN_BUTUNLUGU.md)
- [Sorumluluk ayrımı ve SOLID](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/04_MIMARI.md)
- [Mevcut entegrasyonlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/05_ENTEGRASYONLAR.md)
- [Editör ve içerik üretimi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/06_EDITOR.md)
- [UI, UX ve erişilebilirlik](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/07_UI_UX.md)
- [SEO, RSS ve site kimliği](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/08_SEO_VE_BLOG.md)
- [Performans, işletim ve kabul](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/09_KALITE_VE_ISLETIM.md)
- [Kararlar ve kural çelişkileri](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)
- [Tek ilerleme çizelgesi](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md)
- [Kopyalanabilir agent mesajları](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/AGENT_PROMPTLARI.md)
- [Faz kanıtı şablonu](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KAYIT_SABLONU.md)
