# Migration, formlar ve veri kuralları

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F10 — Migration keşfini ve sürüm uyumunu düzelt

**Neden:** DbContext ile migration assembly ayrıldığı için migration keşfi bozuk.

**Ön koşul:** F09 tamamlanmış; mevcut dört projeli yapı korunuyor.

**Dokunulacak alanlar:** Program.cs; ilgili csproj dosyaları; Migrations/; README.md; mevcut tool manifest varsa o dosya.

**Agent'ın uygulayacağı sıra:**

1. F00 sonrası gerçek proje haritasını esas al. Mevcut migration dosyalarını ve uygulanmış migration kimliklerini envanterle.
2. Mevcut dört projede en küçük çözüm olarak Web'deki migration assembly'sini açık yapılandır. Geçmiş migration kimliklerini değiştirme.
3. EF runtime/design/tools patch sürümlerini doğrulanmış uyumlu sürüme hizala; gereksiz major upgrade yapma.
4. Migration listesi, boş DB kurulum SQL'i ve eski şema test kopyasının yükseltmesini doğrula; README komutlarını gerçek csproj yollarıyla güncelle. Önceki IsPublished/IsActive dönüşümünü veri raporuyla ele al, içerikleri otomatik yayına alma.

**Kabul ve kanıt:** Migration listesi gerçek 5 geçmiş migration'ı görür; boş test DB ve örnek eski DB yükselir; beklenmedik drop/data loss yok; build geçer. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Production database update ve eski migration'ları silip yeniden yaratma yok.

**Geri dönüş:** Test DB yeniden kurulabilir; gerçek veri göçünde forward-fix/backup planı, otomatik destructive Down yok.

**İzlenebilirlik:** F6.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F11 — Dosyasız yazı kaydını ve alan hatalarını düzelt

**Neden:** Opsiyonel kapak yanlışlıkla zorunlu; hata kullanıcıya görünmüyor.

**Ön koşul:** F10 tamamlanmış.

**Dokunulacak alanlar:** AdminPostController.Create/Edit; Views/AdminPost/Create/Edit.

**Agent'ın uygulayacağı sıra:**

1. Opsiyonel thumbnailFile parametresini nullable yap; önceki kapak URL'sini Edit'te koru.
2. İlgili alan için validation göstergesi ve genel hata özeti ekle; başarılı olmayan kaydı başarı gibi yönlendirme.
3. Yeni dosya yokken Create/Edit'in DB kaydını test et; yükleme hatasında mevcut içeriği ve kullanıcı girişlerini koru.

**Kabul ve kanıt:** Kapaksız Create başarılı; dosyasız Edit önceki kapağı korur; bozuk upload açık hata; validation başarısızlığında form değerleri kalır. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Medya sağlayıcısı değişimi ve genel editör tasarımı yok.

**Geri dönüş:** Yalnızca binding/gösterim diff'i; DB şeması değişmez.

**İzlenebilirlik:** F1.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F12 — Yazı ve kategori iş kurallarını ortak doğrula

**Neden:** HTML form kısıtları servis ve webhook üzerinden atlanabiliyor.

**Ön koşul:** F11 tamamlanmış.

**Dokunulacak alanlar:** Post/Category entities; PostService/CategoryService; ilgili MVC binding; korunuyorsa webhook.

**Agent'ın uygulayacağı sıra:**

1. Başlık/ad boşluğu, azami metin uzunlukları, içerik büyüklüğü, geçerli aktif kategori ve URL protokolü için açık sınırları kaydet; mevcut veriyle çakışmayı önce raporla.
2. Domain/service sınırında ortak iş kuralı validation kullan; gerekli form input modelinde izinli alanları ve yüzey validation sınırlarını tanımla. Alanları açıkça eşle; AutoMapper veya yeni validation framework kurma.
3. Controller'da izinli alanları bind et; CreatedDate/ViewCount/Slug gibi sistem alanlarını istemciden kabul etme. Webhook'u aynı kurallardan geçir; error'ları doğru alanlara/400 cevabına eşleştir.
4. Summary/Excerpt isteğe bağlıysa boş string normalizasyonunu açık uygula; iş kurallarıyla annotation davranışı çelişmesin.

**Kabul ve kanıt:** Form ve korunmuş webhook aynı bozuk girdiyi reddeder; doğrudan servis çağrısı kuralı atlamaz; sistem alanlarına over-posting etkisiz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Mevcut kayıtları otomatik kırpan veri temizliği veya bütün entity modellerini kapsayan DTO/mapper ağı yok.

**Geri dönüş:** Mevcut veriler değiştirilmez; validation diff'i geri alınabilir, over-posting koruması korunur.

**İzlenebilirlik:** F8, G7, G9.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F13 — Formların frontend doğrulama bağımlılığını onar

**Neden:** jQuery yüklenmeden validation eklentileri çalışıyor.

**Ön koşul:** F12 tamamlanmış.

**Dokunulacak alanlar:** Views/Shared/_ValidationScriptsPartial.cshtml; _AdminLayout; form görünümleri.

**Agent'ın uygulayacağı sıra:**

1. wwwroot/lib içindeki gerçek jQuery ve validation varlıklarını keşfet; önce jQuery sonra eklentiler sırasını kur.
2. Bu varlıkları yalnızca gerekli form sayfalarında ve birer kez yükle; değişik sürümleri CDN'den karıştırma.
3. Hidden Markdown alanı ve server-side validation birlikte çalışsın. Editor yüklenemezse textarea görünür/düzenlenebilir olsun.

**Kabul ve kanıt:** Create/Edit/Category/Login akışında konsol ReferenceError yok; istemci ve sunucu hata metni tutarlı; JavaScript kapalıyken temel form kaydı mümkün. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni frontend framework veya form motoru yok.

**Geri dönüş:** Varlık sırasını tek diff olarak geri al; server validation her durumda sürer.

**İzlenebilirlik:** F1; rapor §8 editör.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F14 — Kategori düzenlemesinde korunan alanları sakla

**Neden:** Bağlanan yeni entity mevcut CreatedDate/IsActive değerlerini ezebiliyor.

**Ön koşul:** F13 tamamlanmış.

**Dokunulacak alanlar:** AdminCategoryController; CategoryService; kategori formları.

**Agent'ın uygulayacağı sıra:**

1. Update öncesi mevcut Category kaydını getir; bulunamadığında açık sonuç üret.
2. Yalnızca izin verilen düzenlenebilir alanları ata; CreatedDate ve istemcinin düzenlemediği aktiflik değerlerini koru.
3. Modelde olmayan Description binding'ini kaldır; sırf eski Bind listesi için yeni özellik ekleme.

**Kabul ve kanıt:** Ad değişikliği CreatedDate'i ve pasifliği bozmaz; olmayan Id 404/uygun hata; beklenmeyen alan ekleyerek güncelleme yapılamaz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Slug algoritması ve kategori yeniden tasarımı yok.

**Geri dönüş:** Sadece alan atama değişikliği; mevcut veri üzerine toplu düzeltme yapma.

**İzlenebilirlik:** F4.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F15 — Kategori silme kuralını veritabanında güvenceye al

**Neden:** Servis kontrolüne rağmen Cascade ilişkisi veri kaybı riski taşıyor.

**Ön koşul:** F14 tamamlanmış.

**Dokunulacak alanlar:** ApplicationDbContext; CategoryService; yeni migration; admin kategori hata mesajı.

**Agent'ın uygulayacağı sıra:**

1. Yazısı olan kategorinin silinememesi kuralını Restrict/NoAction yabancı anahtarıyla uygula.
2. Var olan kontrolleri kullanıcıya erken hata için koru; eşzamanlı DB constraint hatasını da yönet.
3. Yeni migration üret; eski dosyayı değiştirme. Bağlı yazıları otomatik silme/taşıma.

**Kabul ve kanıt:** Boş kategori silinir; dolu kategori ve eşzamanlı ekle/sil senaryosunda yazı kaybolmaz; migrasyon SQL'i yalnızca beklenen FK davranışını değiştirir. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Kategori birleştirme ve toplu taşıma yok.

**Geri dönüş:** Production'da Cascade'e dönerek çözme; test DB restore/forward-fix kullan.

**İzlenebilirlik:** F5.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

