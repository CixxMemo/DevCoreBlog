# Mevcut entegrasyonlar

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F27 — Webhook kabul sözleşmesini sadeleştir

**Neden:** Dış girdinin alan sınırları, validation ve doğrudan yayın yetkisi net değil; tipli güvenli sözleşme gerekiyor.

**Ön koşul:** F26 tamamlanmış; mevcut webhook/portföy sınırları kök AGENTS.md kapsamında korunuyor.

**Dokunulacak alanlar:** Controllers/WebhookController.cs; mevcut WebhookPostPayload; PostService; Views/Admin/Automations.cshtml.

**Agent'ın uygulayacağı sıra:**

1. Mevcut route/JSON sözleşmesini ve WebhookPostPayload alanlarını oku; yalnızca bu endpoint sözleşmesini güvenli hale getir.
2. Mevcut WebhookPostPayload'ı küçük typed input DTO olarak koru veya izinli alanlara daralt. Alan tipi/uzunluk/zorunluluk validation'ını uygula; sonra domain Post'a açık eşle. Yalnızca eski DTO yasağı yüzünden JsonElement ayrıştırması yazma. EF entity'sine serbest deserialization/over-posting yapma.
3. Secret kontrolünü veri yazmadan önce yap; genel auth hatası, alan boyutları ve kategori kurallarını uygula. Header secret yalnızca environment'dan gelsin.
4. Mevcut endpoint'e sınırlı body boyutu ve uygun yerleşik rate limit uygula; kimlik doğrulanmadan pahalı parse/DB/medya işi başlatma. Aşırı büyük istek 413, limit aşımı 429 dönsün.
5. Varsayılan taslak sürsün. Mevcut IsPublished=true davranışını sessizce kaldırma; ALLOW_WEBHOOK_PUBLISH gibi açık kapalı-varsayılan ayarla otomatik yayın politikasını belgeleyip kontrol et.

**Kabul ve kanıt:** Mevcut JSON alan adları uyumlu; bilinmeyen alan sistem alanını değiştiremez; secret yok/yanlışken kayıt yok; yayın yetkisi kapalıyken draft; bozuk tip/eksik/aşırı büyük alan doğru 4xx üretir; DTO sistem alanlarını taşımaz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni webhook/REST sürümü ve yeni auth sistemi yok.

**Geri dönüş:** Endpoint'i güvenli biçimde devre dışı bırak; güvensiz deserialize'ı geri getirme.

**İzlenebilirlik:** G9; kök AGENTS.md veri sözleşmeleri ve entegrasyon güvenliği; D06.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F28 — Webhook tekrarlarını tek kayda indir

**Neden:** Ağ tekrarı aynı yazıyı birden çok kez oluşturabiliyor.

**Ön koşul:** F27 tamamlanmış; mevcut webhook/portföy sınırları kök AGENTS.md kapsamında korunuyor.

**Dokunulacak alanlar:** WebhookController; küçük kalıcı webhook işlem entity'si (yeni, DTO değil); DbContext/repository; migration.

**Agent'ın uygulayacağı sıra:**

1. Korunmuş endpoint için idempotency anahtarı sözleşmesini belgeye ekle; istemci geçişini açıkça yönet.
2. Anahtar + payload özeti + oluşturulan post referansını aynı DB transaction/unique constraint içinde kaydet. Eşzamanlı iki istek de yalnızca tek yazı oluşturabilsin.
3. Aynı anahtar/aynı içerik önceki sonucu döndürsün; aynı anahtar/farklı içerik 409. Process içi dictionary kalıcı idempotency sayılmaz.
4. Saklama süresi ve hata sonrası yeniden deneme kuralını belirt; salt hata yüzünden anahtarı sonsuza dek tüketme.

**Kabul ve kanıt:** Seri ve eşzamanlı tekrar tek post; farklı payload 409; restart sonrası tekrar yeni post üretmez; secret yokken idempotency kaydı oluşmaz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Queue/Kafka/HMAC platformu ve otomasyon akışının dış sistemde düzenlenmesi yok.

**Geri dönüş:** Yeni kayıtlar korunur; test DB restore veya forward-fix; gerçek tekrar koruma tablosunu boşaltma.

**İzlenebilirlik:** G9.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F29 — Portföy beslemesini sınırlı DB sorgusuyla üret

**Neden:** Üç kayıt için bütün yazılar yükleniyor; dış URL host başlığından üretiliyor.

**Ön koşul:** F28 tamamlanmış; mevcut webhook/portföy sınırları kök AGENTS.md kapsamında korunuyor.

**Dokunulacak alanlar:** PublicFeedController; PostService/repository; Program.cs CORS; Automations view.

**Agent'ın uygulayacağı sıra:**

1. Mevcut URL/JSON alan sözleşmesini koru; bu fazda yeni endpoint ekleme.
2. F17 yayın filtresiyle OrderBy(PublishDate).ThenBy(Id).Take(3) işlemini DB'de uygula; HTTP çıktısını izinli alanlarla sınırlı projection veya küçük response modeliyle üret; entity navigation grafiğini serialize etme.
3. URL'yi doğrulanmış SiteUrl ve kanonik rota sözleşmesinden üret; CORS'u erişim yetkisi gibi anlatma. Cache politikasını F18 ile tutarlı tut.
4. Otomasyon sayfasındaki sahte sabit domain örneklerini gerçek configuration'dan veya açık placeholder'dan üret.

**Kabul ve kanıt:** En fazla 3 görünür kayıt; future/draft yok; sabit kaynaklı mutlak URL; küçük SQL/result; CORS/rate-limit/field uyumu. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Portföyün React kodunu değiştirme ve yeni API yok.

**Geri dönüş:** Mevcut endpoint sözleşmesini koruyan sürüme dön; tüm kayıtları public verme.

**İzlenebilirlik:** G5, G10; rapor §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

