# Sorumluluk ayrımı ve SOLID

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F23 — Hata sayfaları ve HTTP durumlarını düzelt

**Neden:** Exception 302/200'e dönüşüyor; error layout'u DB arızasında yine hata veriyor.

**Ön koşul:** F22 tamamlanmış.

**Dokunulacak alanlar:** ExceptionHandlingMiddleware; Program.cs; HomeController.Error; Views/Shared/Error; bağımsız error layout/404 view (yeni).

**Agent'ın uygulayacağı sıra:**

1. Tek ana exception yaklaşımı seç; merkezi log ve gerçek 500 durumunu koru, redirect loop'u kaldır.
2. Error/404 sayfalarını DB sorgulamayan hafif layout ile render et; kullanıcıya yalnızca trace id ve anlaşılır English mesaj göster.
3. AGENTS.md kapsamında korunan mevcut JSON uçlarında HTML redirect yerine doğru JSON hata ve status üret; stack trace/secret sızdırma. Request cancellation'ı beklenmedik sunucu hatası gibi raporlama.

**Kabul ve kanıt:** Test DB kapalıyken 500 sayfası bir kere açılır; unknown URL 404; geçersiz JSON işlemi doğru durum; içerik/secret sızıntısı yok. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Genel logging framework veya yeni hata API'si yok.

**Geri dönüş:** DB'den bağımsız statik 500 fallback korunur.

**İzlenebilirlik:** F7.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F24 — Repository bağımlılıklarını sözleşmeye taşı

**Neden:** Servisler concrete repository'lere bağlı; değişim ve izole doğrulama zor.

**Ön koşul:** F23 tamamlanmış.

**Dokunulacak alanlar:** Core/Interfaces yeni IPostRepository/ICategoryRepository; repository implementasyonları; servis constructor'ları; Program.cs.

**Agent'ın uygulayacağı sıra:**

1. Servislerin gerçekten kullandığı repository işlemlerini çıkar; yalnızca gerekli iki domain sözleşmesini ekle.
2. Mevcut implementasyonları bu sözleşmelere bağla; DI ve servis constructor'larını güncelle. Services içindeki Data türü kullanımlarını kaldır, artık gerekmeyen Services→Data ProjectReference'ını çıkar; Web composition root concrete kayıtları bağlasın.
3. Public okumalar/admin yazmaları için interface ayrımı yalnızca tüketici ihtiyacını küçültüyorsa yap; generic katmanları gereksiz çoğaltma. Mevcut domain entity dönüşlerini koru.

**Kabul ve kanıt:** Servisler concrete Data repository türü referansı kullanmaz; build ve önceki CRUD/görünürlük kontrolleri geçer; davranış aynı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** CQRS/MediatR/UnitOfWork framework veya geniş DTO/AutoMapper dönüşümü yok.

**Geri dönüş:** Aynı fazda taşınan constructor ve DI diff'leri birlikte geri alınır.

**İzlenebilirlik:** SOLID D/O/I.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F25 — Controller ve Razor'dan DbContext'i çıkar

**Neden:** Dashboard ve public layout veri katmanına doğrudan bağlı.

**Ön koşul:** F24 tamamlanmış.

**Dokunulacak alanlar:** AdminController; _Layout; servis/repository sözleşmeleri; navigasyon ViewComponent ve view (yeni).

**Agent'ın uygulayacağı sıra:**

1. Kategori navigasyonunu async ViewComponent üzerinden servisle besle; view içinde senkron Any/ToList sorgusu kalmasın.
2. Dashboard metriklerini servis/repository toplamalarına taşı; tüm yazıları Count için yükleme.
3. Servisler gerekli entity/skaler veya dar tipli sonuç döndürsün; dashboard/navigation için yararlıysa küçük typed ViewModel kullan. Ayrı taşıma projesi ve mapper ağı kurma. Bir DbContext üzerinde Task.WhenAll ile paralel sorgu başlatma.

**Kabul ve kanıt:** Views ve Controller'larda DbContext injection/EF sorgusu yok; menü yalnızca aktif kategori; dashboard sayıları doğru; SQL sorgu sayısı kayıtlı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Dashboard görünümü ve domain modeli yeniden tasarlanmaz.

**Geri dönüş:** Bileşen/DI/callsite tek değişiklik olarak geri alınır; önceki güvenlik kontrolleri korunur.

**İzlenebilirlik:** SOLID S/D; rapor §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F26 — Renderer ve Cloudinary bağlantısını DI sınırına al

**Neden:** Core dış render kütüphanesine bağlı; Cloudinary istemcisi servis içinde doğrudan inşa ediliyor.

**Ön koşul:** F25 tamamlanmış.

**Dokunulacak alanlar:** Core/Shared/Helpers/MarkdownHelper; servis/render sözleşmesi; ImageService; Program.cs; ilgili csproj referansları.

**Agent'ın uygulayacağı sıra:**

1. F04 güvenli renderer davranışını değiştirmeden altyapı/servis konumuna taşı; Razor çağrılarını güncelle. Core'da gereksiz Markdig paket referansını kaldır.
2. Cloudinary Account/client'ı composition root'ta doğrulanmış environment değerleriyle kur; ImageService'e DI ile ver.
3. Mevcut IImageService'i koru; IFormFile→Stream dönüşümünü ancak testlenebilirlik için gerçek ihtiyaç varsa basit sınır olarak yap. Aynı anda sağlayıcı değiştirme.

**Kabul ve kanıt:** Core dış renderer paketine bağlı değil; bütün Markdown güvenlik fixture'ları aynı sonucu verir; upload client testte değiştirilebilir; secret loglanmaz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni storage provider, genel plugin mimarisi ve güvenlik davranışı değişimi yok.

**Geri dönüş:** İsim/konum geçişi tek diff; güvenli renderer implementasyonu kaybolmaz.

**İzlenebilirlik:** SOLID S/D; rapor §6–7.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

