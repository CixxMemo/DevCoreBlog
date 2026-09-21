# Yayın, cache ve eşzamanlılık

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F16 — Yayın zamanını açık saat dilimiyle işle

**Neden:** Sunucu local timezone değişince yayın saati kayıyor.

**Ön koşul:** F15 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; .env.example; AdminPostController; PostService; yayın tarihi formu.

**Agent'ın uygulayacağı sıra:**

1. SITE_TIME_ZONE ile açık site saat dilimi kullan; plan varsayılanı Europe/Istanbul, gerçek dağıtım değeri F01 ortam kaydında doğrulanmalı.
2. Datetime-local girdiyi bu dilimden UTC'ye dönüştür; DB ve kıyaslarda UTC kullan; editte ters dönüşüm uygula. Var olan UTC kayıtları topluca kaydırma.
3. Yerleşik TimeProvider'ı DI üzerinden kullan; geçersiz/çift anlamlı saatlerde anlaşılır hata üret.

**Kabul ve kanıt:** Sunucu TZ değişse de aynı kullanıcı girdisi aynı UTC anına gider; Create→Edit roundtrip korunur; mevcut UTC içerik değişmez. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Arayüz dilini değiştirme; geçmiş tarihlere toplu tahmini dönüşüm yok.

**Geri dönüş:** Kod/config diff'i; geçmiş veri dönüşümü olmadığından geri alma veri kaydırmaz.

**İzlenebilirlik:** F8, G5.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F17 — Tek yayın görünürlüğü kuralını uygula

**Neden:** Gelecek ve pasif içerikler public yüzeylere sızıyor.

**Ön koşul:** F16 tamamlanmış.

**Dokunulacak alanlar:** PostRepository; CategoryRepository; PostService; Home/Seo; PublicFeedController.

**Agent'ın uygulayacağı sıra:**

1. Görünürlük ifadesini tek yerde tanımla: IsActive && IsPublished && PublishDate<=now && Category.IsActive. Public yolların tamamında aynı SQL'e çevrilebilir kuralı kullan.
2. Ana sayfa, kategori, arama, detay, ilgili yazılar, sitemap ve korunmuş beslemeyi bağla; admin sorguları taslakları görebilmeli.
3. Doğruluk sağlanana kadar ilgili public çıktı/veri cache'lerini bu fazda geçici devre dışı bırak; F18 güvenli cache'i kuracak. Kategori pasifliği kaynaklı eski kayıt görünürlüğünü raporla; otomatik yeniden aktifleştirme yapma.

**Kabul ve kanıt:** Yayın/taslak/gelecek/pasif yazı/pasif kategori matrisi bütün public yüzeylerde doğru; admin kayıtları yönetebilir; sınır anı TimeProvider ile test edilir. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** SEO veya yeni yayın durumu DB enum'u yok.

**Geri dönüş:** Filtreyi gevşeterek uyumluluk sağlama; cache kapalı güvenli sürüm kullanılabilir.

**İzlenebilirlik:** G5, F8.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F18 — Yayın kuralına uyan cache politikası

**Neden:** Yayından kaldırma ve planlı yayın cache nedeniyle tutarsız.

**Ön koşul:** F17 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; PostService; CategoryService; HomeController; korunmuş feed cache headers.

**Agent'ın uygulayacağı sıra:**

1. UseOutputCache'i authentication/authorization sonrasına al. Detay action'ında sayaç yan etkisi bulunduğu için HTML output cache'i kapalı tut.
2. Küçük blog için önce tek süreç politikası seç; mutable EF entity grafiğini kontrolsüz paylaşma. İki cache katmanını gerekçesiz üst üste kurma. Bunu gereksiz karmaşık bir scheduler'a dönüştürme; güvenli invalidation kanıtlanamıyorsa ilgili sorguyu cache'leme.
3. Liste/kategori cache'lerini etiketle; post/category değişikliğinde ilgili etiketleri düşür. Kilitsiz static CancellationTokenSource mekanizmasını kaldır veya tüm erişimini doğru senkronize et.
4. TTL'yi bir sonraki planlı yayın anıyla sınırla; yarış halinde eski veri tekrar cache'e yazılmasını test et. Page/query key'lerini sınırla. Public feed'in istemci cache süresini geri çekme beklentisine göre düzenle; tarayıcıya verilmiş içeriğin geri alınamayacağını belgele.

**Kabul ve kanıt:** Isınmış cache sonrası unpublish yeni istekte gizlenir; planlı tarih geldiğinde görünür; kategori değişikliği yansır; paralel güncellemelerde exception/stale reinsert yok. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Redis veya çok düğümlü yayın mimarisi yok; ölçüm olmadan hız kazanımı iddia etme.

**Geri dönüş:** Güvenli fallback ilgili cache'leri kapalı tutmaktır.

**İzlenebilirlik:** G8; rapor §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F19 — Düzenlemede kalıcı slug'ı koru

**Neden:** Başlık ve durum değişikliği eski yazı/kategori adreslerini kırıyor.

**Ön koşul:** F18 tamamlanmış.

**Dokunulacak alanlar:** PostService.UpdatePostAsync; CategoryService.UpdateCategoryAsync; slug önizleme metinleri.

**Agent'ın uygulayacağı sıra:**

1. Mevcut kaydın slug'ını düzenleme sırasında koru; create sırasında üretim devam etsin.
2. Başlık, aktiflik ve yayın durumu değişikliğini URL değişikliğiyle bağlama. UI mevcut kalıcı adresi göstersin; her tuşta yeni adres vaat etmesin.
3. Gerekmedikçe slug değiştirme özelliği ekleme; ileride açık slug değişikliği istenirse yönlendirme geçmişi ayrı fazdır.

**Kabul ve kanıt:** Başlık/kategori adı/yayın durumu değişince eski URL çalışır; yeni kayıtta slug oluşur. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Var olan slug'ları topluca İngilizceleştirme veya yeni redirect tablosu yok.

**Geri dönüş:** Mevcut slug kayıtları değişmediği için kod diff'i geri alınabilir; otomatik yeniden üretim açığı yayımlanmaz.

**İzlenebilirlik:** F2.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F20 — Slug benzersizliği ve çakışma göçü

**Neden:** Aynı başlık aynı URL'yi üretiyor; eşzamanlı kayıtlar belirsiz.

**Ön koşul:** F19 tamamlanmış.

**Dokunulacak alanlar:** SlugGenerator; Post/Category repository-service; model konfigürasyonu; yeni migration.

**Agent'ın uygulayacağı sıra:**

1. Mevcut boş/çakışan slug'ları salt okunur raporla. Gerçek veride hangi kaydın mevcut URL'yi koruyacağı belirsizse sadece o kararı kullanıcıya somut tabloyla sun.
2. Yeni kayıt için deterministik suffix ve boş slug fallback kuralı; DB unique index son güvence. Eşzamanlı unique violation'ı sınırlı tekrar ile ele al.
3. Migration önce test verisi üzerinde çakışma çözümüyle doğrulansın. Aynı eski URL'yi paylaşan iki kayıt için iki ayrı 301 hedefi varmış gibi davranma; tek sahip kuralını belgele.

**Kabul ve kanıt:** Aynı başlıkta iki eşzamanlı kayıt farklı slug; boş/sembol/Türkçe başlık doğru; unique index mevcut; eski sahibi korunur. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Gerçek veri çakışmalarını tahminle silme/birleştirme yok.

**Geri dönüş:** Şema/veri değişikliği için backup ve forward-fix; unique index'i veri kaybıyla kaldırma.

**İzlenebilirlik:** F2.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F21 — Okunma sayacını atomik artır

**Neden:** Get-increment-full update kayıp artışa ve içerik ezilmesine yol açabilir.

**Ön koşul:** F20 tamamlanmış.

**Dokunulacak alanlar:** PostRepository; PostService.IncrementViewCountAsync; HomeController.Detail.

**Agent'ın uygulayacağı sıra:**

1. EF Core'un desteklenen atomik sütun güncellemesini kullan; yalnızca ViewCount değişsin.
2. Okunma tanımını dürüstçe yaz: tekil kişi değil uygun public detay isteği. Admin görüntülemelerini hariç tut; HEAD isteği sayaç artırmasın. Bot filtrelemesini kesin analitik diye sunma.
3. F18 uyarınca detay output cache'i kapalı kalır; yeni tracking API'si açma. İlgisiz tüm entity update çağrısını bu akıştan çıkar.

**Kabul ve kanıt:** N eşzamanlı uygun GET sayacı N artırır; içerik/yayın alanı değişmez; HEAD/admin saymaz; silinen kayıt 404. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Tekil ziyaretçi, gerçek zamanlı analitik platformu ve yeni API yok.

**Geri dönüş:** Sayaç artırımı geçici devre dışı bırakılabilir; full entity update'e geri dönme.

**İzlenebilirlik:** F3.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F22 — Eşzamanlı editlerde veri kaybını önle

**Neden:** İki açık düzenleme sekmesi son kaydedenin önceki değişikliği ezmesine izin veriyor.

**Ön koşul:** F21 tamamlanmış.

**Dokunulacak alanlar:** Post/Category model konfigürasyonu; gerekirse migration; Edit formları/controller/service.

**Agent'ın uygulayacağı sıra:**

1. Mevcut EF/Npgsql sürümünde basit concurrency token desteğini doğrula; tek bir strateji seç, ikisini birden ekleme.
2. Edit formu yüklenirken sürümü taşı; update sırasında DB orijinal sürümüyle kıyasla. İstemci sürümünü sunucu alanlarına sınırsız bind etme.
3. Çakışmada yeni DB içeriğini sessizce ezme; kullanıcının yazdığını koruyan English hata ve yeniden yükleme yolu sun. Sayaç artışlarının seçilen concurrency politikasıyla gereksiz edit çakışması üretip üretmediğini ölç.

**Kabul ve kanıt:** Aynı kaydı iki sekmede açıp farklı kaydetme ikinci işlemde açık çatışma üretir; ilk kayıt korunur; kullanıcı metni kaybolmaz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Tam revision history veya çok yazarlı roller yok.

**Geri dönüş:** Migration varsa forward-fix; değişiklikleri körlemesine last-write-wins'e dönüştürme.

**İzlenebilirlik:** F3.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

