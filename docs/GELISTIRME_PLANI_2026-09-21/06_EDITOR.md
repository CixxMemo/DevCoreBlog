# Editör ve içerik üretimi

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F30 — Yazı editörünün ortak kodunu tek yerde topla

**Neden:** Create/Edit ekranlarının yüzlerce satırlık benzer script'i güvenlik ve davranış düzeltmelerinin farklılaşmasına yol açıyor.

**Ön koşul:** F29 tamamlanmış.

**Dokunulacak alanlar:** Views/AdminPost/Create.cshtml ve Edit.cshtml; ortak Razor partial; wwwroot/js/post-editor.js (yeni).

**Agent'ın uygulayacağı sıra:**

1. Önce iki ekranın farklarını çıkar; ortak alanları partial'a, editör başlatma/yükleme/bildirim davranışını tek yerel JS modülüne taşı.
2. Create ve Edit'e özgü kimlik, değer ve URL'leri Razor ile güvenli encode edilen data attribute'larından aktar. Kullanıcı metnini JS kaynak koduna veya innerHTML'e yerleştirme.
3. İşlevsel davranışı koru; alan isimlerini, antiforgery token'ını ve normal form gönderimini değiştirme. F03/F04/F05/F09 düzeltmelerini ortak kodda sürdür.

**Kabul ve kanıt:** Her iki ekran açılır; kapak seçmeden ve seçerek kayıt, görsel yükleme ve doğrulama hatası çalışır; tekrarlanan editör kodu kalkar; konsolda yeni hata yok. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Bu faz yeni editör paketi, yeni tasarım veya otomatik kaydetme eklemez.

**Geri dönüş:** Yalnızca bu fazın taşıma diff'ini geri al; önceki güvenlik düzeltmelerini koru.

**İzlenebilirlik:** Rapor §7, §8; SRP/DRY.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F31 — Başarısız kayıtta yazının kaybolmasını önle

**Neden:** Yerel taslak submit anında siliniyor; sunucu reddettiğinde içerik kaybolabiliyor. Edit için de kurtarma gerekiyor.

**Ön koşul:** F30 tamamlanmış.

**Dokunulacak alanlar:** Ortak editör JS; AdminPostController; Create/Edit views.

**Agent'ın uygulayacağı sıra:**

1. Yerel kurtarma kaydını yeni yazı/düzenlenen yazı kimliğine göre ayır; kısa debounce ile başlık/içerik/kategori ve kayıt zamanını sakla. Dosya veya parola saklama.
2. Submit anında kaydı silme. Yalnızca sunucunun başarılı kaydı takip eden redirect'i ile doğrulanmış ilgili taslağı temizle; validation/HTTP/network hatasında koru.
3. Mevcut sunucu içeriğiyle farklı yerel kayıt varsa Restore/Discard seçimini sun; sessizce üstüne yazma. Çok sekmede daha yeni kaydı sessizce ezme.
4. localStorage kapalı/doluysa editör ve normal kayıt çalışsın; kurtarmanın bu tarayıcıya ait olduğunu English kısa metinle açıkla.

**Kabul ve kanıt:** Create/Edit alanları sayfa yenileme ve başarısız submit sonrası kurtarılır; başarılı kayıtta yalnızca ilgili kayıt temizlenir; eski taslak sessizce server içeriğini ezmez. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni autosave API, veritabanı revision sistemi ve cihazlar arası senkron yok.

**Geri dönüş:** Kurtarma davranışını kapatmak gerekirse mevcut yerel metinleri silme; dışa kopyalamaya izin ver.

**İzlenebilirlik:** Rapor §8 editör; F8.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F32 — Taslak, zamanlama ve yayınlama eylemlerini anlaşılır yap

**Neden:** IsPublished ve tarih alanlarının ilişkisi editör için belirsiz; yanlışlıkla erken yayın oluşabiliyor.

**Ön koşul:** F31 tamamlanmış.

**Dokunulacak alanlar:** AdminPost Create/Edit partial; AdminPostController; PostService.

**Agent'ın uygulayacağı sıra:**

1. Save Draft, Schedule ve Publish eylemlerini aynı mevcut MVC formunun açık submit değerleriyle ayır. Güvenli form input sözleşmesini ve mevcut servis iş kurallarını kullan; gerekirse küçük typed input modeli kullan.
2. Save Draft yayın bayrağını kapatır; Schedule açıkça gelecekte bir an ister; Publish şimdi yayınlamayı açıkça ifade eder. Düzenlenmiş bir yayınlı yazının normal Save davranışını etiketinde açıkla.
3. F16 zaman dilimi ve F17 görünürlük kuralını kullan; geçersiz zaman veya kategori seçimini sunucuda reddet. Kullanıcının içerik girişini koru.
4. Liste/forma Draft, Scheduled, Published, Inactive durumlarını aynı kuralla yansıt; planlanmış yazıya henüz herkese açıkmış gibi View bağlantısı verme.

**Kabul ve kanıt:** Üç eylemin DB durumu ve okuyucu görünürlüğü doğru; ileri tarihli yazı erken açılmaz; invalid submit içerik kaybettirmez; English etiketler anlaşılır. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni workflow motoru, onaylayan kullanıcı veya rol yok.

**Geri dönüş:** Form görünümünü geri alırken kayıtlı yayın tarihlerini topluca değiştirme.

**İzlenebilirlik:** G5; Rapor §8 ve §9.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F33 — Sunucu çıktısıyla tutarlı güvenli önizleme ekle

**Neden:** Editör önizlemesi gerçek Markdown render'ından farklı olabilir; taslağı herkese açmadan sonucu görmek gerekiyor.

**Ön koşul:** F32 tamamlanmış.

**Dokunulacak alanlar:** Mevcut AdminPostController'a MVC HTML action; preview Razor view; ortak editör formu.

**Agent'ın uygulayacağı sıra:**

1. Yetkili, antiforgery korumalı POST ile yalnızca HTML Razor önizleme üret; bu bir yeni Web API/JSON endpoint'i değildir. Aynı güvenli renderer ve validation sınırlarını kullan.
2. Önizlemeyi kaydetmeden aç; DB yazısı, yayın durumu değişimi ve ViewCount artışı yapma. Cache-Control no-store ve indekslenmeme kuralı uygula.
3. Kaydedilmemiş içerik, uzun kod, tablo ve görseli gerçek detay tipografisiyle göster. Tek kullanımlık form target/yeni pencere yaklaşımında kullanıcının alanlarını kaybetme.

**Kabul ve kanıt:** Taslak önizleme yalnızca oturum sahibi tarafından görülür; XSS fixture'ları çalışmaz; DB/sayaç değişmez; gerçek detay ile biçim aynı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Public share-preview linki ve yeni istemci framework'ü yok.

**Geri dönüş:** Yeni önizleme girişini kaldır; yazıları ve yerel kurtarma kayıtlarını koru.

**İzlenebilirlik:** G2, G4; Rapor §8.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

