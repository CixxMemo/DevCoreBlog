# UI, UX ve erişilebilirlik

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F34 — Yönetim panelini telefonda kullanılabilir yap

**Neden:** 390 px ekranda sabit sidebar ana alanı yaklaşık 166 px'e düşürüyor.

**Ön koşul:** F33 tamamlanmış.

**Dokunulacak alanlar:** Views/Shared/_AdminLayout.cshtml; admin list/form views; ilgili yerel CSS/JS.

**Agent'ın uygulayacağı sıra:**

1. Küçük ekranlarda sidebar'ı kapalı drawer yap; masaüstünde mevcut gezinmeyi koru. Tekil aç/kapat düğmesi, Escape, odak geri dönüşü ve arka alanın etkileşimden çıkarılmasını uygula.
2. Formları küçük ekranda tek kolona indir; başlık, kaydet ve validation görünür kalsın. Tablolara kontrollü yatay kaydırma veya içerik kaybettirmeyen satır düzeni uygula.
3. 360/390/768 px ve masaüstünde menü, uzun başlık, çok kategori, validation ve %200 yakınlaştırmayı incele. Dokunma alanlarını yeterli, odak göstergelerini görünür tut.

**Kabul ve kanıt:** Sayfa gövdesinde yatay taşma yok; menü klavyeyle açılır/kapanır; editör ve kaydetme kullanılabilir; ekran görüntüsü ve konsol kontrolü kayıtlı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Marka, renk paleti veya yeni UI kütüphanesi değişmez.

**Geri dönüş:** Yalnızca responsive düzen değişikliklerini geri al.

**İzlenebilirlik:** Rapor §8 mobil/admin.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F35 — Frontend varlıklarını tekrarlanabilir derlemeye geçir

**Neden:** Tailwind Play CDN üretim için uygun değil; latest ve dağınık dış script'ler sonucu değişken kılıyor.

**Ön koşul:** F34 tamamlanmış; D04 teknik build aracı kararı bu fazda doğrulanır.

**Dokunulacak alanlar:** Layout'lar; tailwind.config.js; wwwroot/css ve js; gerekirse doğrulanmış package.json/lock; build talimatları.

**Agent'ın uygulayacağı sıra:**

1. Mevcut CDN Tailwind sürümünü, kullanılan sınıfları ve gerçekten yüklü editör/Prism bağımlılıklarını envanterle. Bu fazı majör sürüm yükseltmesine dönüştürme.
2. Mevcut Tailwind'i derlemek için gerekli resmi CLI bağımlılığını sürümünü doğrulayarak açıkça ekle; bu build aracı mevcut yığının parçasıdır, yeni UI framework'ü değildir. Lockfile ve tek belgelenmiş build komutu oluştur. Somut paket/sürüm/gerekçeyi D04 kaydına yaz; mevcut yığın için gereken build aracı doğrulaması rutin teknik iştir. Yığın değişimi önerme.
3. Razor ve JS class tarama kapsamını ayarla; dinamik sınıfları sonlu açık eşlemeyle üret. Üretilmiş CSS'in clean checkout'ta nasıl hazırlanacağını belirt.
4. Mevcut editör/Prism script'lerini doğrulanmış sabit sürümlere bağla; uygun lisanslarla yerelde sun veya gerekli CDN için integrity/crossorigin politikasını doğrula. Eksik toolbar yükleme sırasını düzelt.
5. Development/Publish akışında CSS/JS'nin mevcut olduğunu doğrula; cache busting kullan. Paket varmış gibi import yazma.

**Kabul ve kanıt:** Temiz bağımlılık kurulumu ve asset build tekrarlanabilir; hiçbir sayfa Play CDN istemez; kod bloğu/editör çalışır; önemli ekranlar görsel olarak korunur. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Tailwind majör upgrade, React/Vue, tüm dependency'leri en son sürüme topluca yükseltme yok.

**Geri dönüş:** Eski kilitli varlık sürümüne dön; CDN latest kullanımına dönme.

**İzlenebilirlik:** G10; Rapor §7 ve §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F36 — Ortak görsel düzeni ve okunabilirliği toparla

**Neden:** Tekrarlanan kartlar, küçük metinler ve üst üste CSS override'ları tutarsızlık oluşturuyor.

**Ön koşul:** F35 tamamlanmış.

**Dokunulacak alanlar:** Public Razor views/partials; wwwroot/css/site.css; Tailwind yapılandırması.

**Agent'ın uygulayacağı sıra:**

1. Tech Minimal dilinde font ölçüsü, satır aralığı, spacing, border ve renk token'larını ortaklaştır. Mevcut marka rengini koru; neon/glass/aşırı radius/gölge ekleme.
2. Yazı kartını ve gerekli ortak durum bileşenlerini Razor partial'a ayır. Placeholder, kategori ve tarih gösterimini birleştir; yeni component framework'ü kullanma.
3. Gerçekte artık kullanılmadığı arama ve tarayıcıyla kanıtlanan carousel/script/CSS'i kaldır. !important katmanlarını kontrollü azalt; tek seferde site CSS'ini yeniden yazma.
4. Metin kontrastı, link/focus görünürlüğü, görsel aspect ratio ve boş/hata durumlarını aynı örnek içeriklerle doğrula.

**Kabul ve kanıt:** Ana sayfa/kategori/arama/detay aynı görsel dilde; küçük metinler okunur; kullanılmayan kod için kullanım araması kayıtlı; mobil/masaüstü görsel regresyon yok. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni marka veya mevcut içerikleri AI ile yeniden yazma yok.

**Geri dönüş:** Faz diff'ini geri al; F35 build ve güvenlik davranışını koru.

**İzlenebilirlik:** Rapor §7 ve §8; SRP/DRY.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F37 — Okuyucu gezinmesi ve aramayı erişilebilir yap

**Neden:** Mobil drawer odağı yönetmiyor; arama ve kategori erişimi her ekranda tutarlı değil.

**Ön koşul:** F36 tamamlanmış.

**Dokunulacak alanlar:** Views/Shared/_Layout.cshtml; nav/category ViewComponent; site.js.

**Agent'ın uygulayacağı sıra:**

1. Global aramayı mevcut GET MVC rotasına bağla; gönderilen terimi kutuda güvenli encode ederek koru. Yalnızca aktif gerçek kategorileri göster.
2. Public drawer'da aria-expanded/controls, Escape, ilk odak, focus trap, kapanınca geri odak ve arka içeriği inert yapma davranışlarını tamamla; desktop'a geçince kilitleri temizle.
3. Skip to content bağlantısı, semantik nav/main, görünür klavye odağı ve mevcut sayfa işaretini ekle. JavaScript yokken temel gezinme ve arama erişilebilir kalsın.

**Kabul ve kanıt:** Sadece klavyeyle menü/arama/detay erişilir; resize sonrası sayfa kilitlenmez; boş kategori bağlantısı yok; ekran okuyucu isimleri anlaşılır. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni arama servisi, SPA gezinmesi ve görsel tasarım değişimi yok.

**Geri dönüş:** Navigasyon düzeni geri alınabilir; mevcut çalışan rotalar korunur.

**İzlenebilirlik:** Rapor §8 mobil/erişilebilirlik.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F38 — Ana sayfada içerik keşfini gerçek veriye bağla

**Neden:** Vitrin sayfaya göre değişiyor; Trending yerel dokuz kayıttan seçiliyor; statik kategori kutuları yanıltıcı.

**Ön koşul:** F37 tamamlanmış.

**Dokunulacak alanlar:** HomeController; PostService/repository; Views/Home/Index.cshtml ve ilgili partial'lar.

**Agent'ın uygulayacağı sıra:**

1. Vitrinin yalnızca ilk sayfada yer almasını sağla; mevcut featured alanı yoksa yeni karmaşık editoryal model kurmadan en yeni uygun yazıyı kullan. Liste tekrarını bilinçli yönet.
2. En çok okunanları bütün görünür yazılardan sınırlı DB sorgusuyla seç. Zaman pencereli veri tutulmuyorsa etiketi Most Read yap; güncel trend iddiasında bulunma.
3. Statik/tekrarlanan kategori kutularını gerçek aktif kategoriler ve anlamlı boş durumla değiştir. Tek H1 ve belirgin içerik sırası kullan.
4. Sayfalama ve filtre sonucunda kaç yazı bulunduğunu açıkla; sayfa numaralarında sabit ikinci sıralama anahtarı kullan.

**Kabul ve kanıt:** İkinci sayfada tekrar hero yok; Most Read doğru global sonuç; gelecekteki/taslak/pasif içerik hiçbir alanda görünmez; boş DB ekranı düzgün. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni featured entity, kişiselleştirme veya sahte sosyal kanıt yok.

**Geri dönüş:** Sunum ve sorgu farkını geri al; ortak görünürlük filtresini kaldırma.

**İzlenebilirlik:** G5; Rapor §8 keşif.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F39 — Yazı okuma deneyimini tamamla

**Neden:** İlgili yazılar hesaplanıp gösterilmiyor; uzun teknik içerikte yön bulma ve kod okuma eksik.

**Ön koşul:** F38 tamamlanmış.

**Dokunulacak alanlar:** Yazı detay Razor view; renderer başlık çıktısı; site.css/site.js; PostService.

**Agent'ın uygulayacağı sıra:**

1. İlgili görünür yazıları sınırlı sayıda göster; kategori, yayın/güncelleme tarihi ve okuma süresini tutarlı sırayla sun. Okuma süresi görünür metinden hesaplanıp en az bir dakika olsun.
2. Uzun içerikte güvenli başlık kimliklerinden TOC üret; tekrarlanan başlıklarda unique ID sağla. Renderer'ın XSS kurallarını gevşetme.
3. Metin alanını yaklaşık 65–75 karakter genişliğinde tut; kod bloklarında yatay kaydırma ve erişilebilir Copy düğmesi ekle. Görsellerin taşmasını önle ve gerçek açıklaması olmayan alt metni uydurma.
4. Kısa/uzun makale, tablo, kod, YouTube ve eksik görselle mobil/masaüstü kontrol et; reklam/reklam benzeri sahte kutuları ürün içeriği gibi sunma.

**Kabul ve kanıt:** TOC hedefleri doğru; Copy başarısızsa anlaşılır bildirim; ilgili yazılar görünürlük filtresine uyuyor; içerik ekranı taşırmıyor. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yorumlar, paylaşım takip servisi veya dış widget yok.

**Geri dönüş:** Detay sunum diff'ini geri al; güvenli renderer'ı koru.

**İzlenebilirlik:** G2, G5; Rapor §8 okuyucu.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F40 — Okuyucu aramasını ve sayfalamayı sınırlandır

**Neden:** Sınırsız terim/sayfa ve Lower/Contains sorguları büyüdükçe maliyetli; sonuç bağlamı zayıf.

**Ön koşul:** F39 tamamlanmış.

**Dokunulacak alanlar:** HomeController arama/kategori actions; repository; search/category views.

**Agent'ın uygulayacağı sıra:**

1. Arama teriminin uzunluğunu ve page/pageSize üst sınırını sunucuda doğrula; pageSize sonlu seçenek olsun. Geçersiz ve bulunmayan sayfa için tutarlı davranış tanımla.
2. Filtreleme, sabit sıralama, Count ve Skip/Take işlemlerini DB'de yap; tüm kayıtları çekip bellekte arama yapma. Türkçe büyük/küçük harf davranışını gerçek PostgreSQL collation ile test edip beklentiyi açıkça belirt.
3. Aranan terim, sonuç sayısı, kategori filtresi, boş sonuç yönlendirmesi ve ileri/geri bağlantılarını göster; filtrelerin URL'de korunmasını sağla.

**Kabul ve kanıt:** Uzun terim/negatif/aşırı page güvenli; sayfalar kayıt tekrarlamaz; filtre korunur; SQL sınırlı veri getirir; Türkçe örnekler kayıtlı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Ölçüm olmadan Elasticsearch, PostgreSQL extension veya AI arama yok.

**Geri dönüş:** UI farkı geri alınabilir; input sınırlarını kaldırma.

**İzlenebilirlik:** G9/G10 kaynak tüketimi; Rapor §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F41 — Yönetici yazı listesini sunucuda sayfala

**Neden:** Bütün kayıtları istemciye gönderen liste büyüdükçe yavaşlıyor.

**Ön koşul:** F40 tamamlanmış.

**Dokunulacak alanlar:** AdminPostController; PostService/repository; Views/AdminPost/Index.cshtml.

**Agent'ın uygulayacağı sıra:**

1. Mevcut arama, kategori ve yayın durumunu GET parametreleriyle sunucu sorgusuna taşı; sınırlı listeyi ve toplam sayı/filtre bağlamını küçük typed sayfa modeliyle aktar; her entity için DTO ailesi veya AutoMapper ekleme.
2. F32 durum sözlüğünü kullan; Scheduled filtresi zamanı dikkate alsın. OrderBy yanında deterministik ikinci anahtar ve sınırlı pageSize ekle.
3. Edit veya toggle sonrası filtre/sayfa bağlamını güvenli local return URL ile koru; dış URL'ye açık redirect oluşturma.

**Kabul ve kanıt:** Çok kayıt fixture'ında yalnızca bir sayfa yüklenir; tüm filtreler birlikte doğru; toggle/edit dönüşü bağlam korunur; dış return URL reddedilir. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Toplu silme/yayınlama ve tablo framework'ü yok.

**Geri dönüş:** Eski sunumu geri alırken sorgu sınırlarını koru.

**İzlenebilirlik:** Rapor §8 yönetici ve §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F42 — Arayüz dili ve tarih biçimini tutarlı yap

**Neden:** English UI kararı varken sunucu kültürü Türkçe ay adları üretebiliyor.

**Ön koşul:** F41 tamamlanmış.

**Dokunulacak alanlar:** Razor views; ortak tarih biçimleme noktası; layout html lang; validation messages.

**Agent'ın uygulayacağı sıra:**

1. Arayüzdeki label, toast, validation, empty/error durumlarını English yap; Türkçe kullanıcı makalelerine dokunma.
2. Tarih gösteriminde açık kültür ve F16 saat dilimini kullan; MMM dd, yyyy kararını tüm ilgili ekranlara uygula. Form datetime-local değeri ile gösterim metnini karıştırma.
3. Sayfa iskeletinin html lang değerini gerçek UI diliyle eşleştir; kayıtlı içerik dilini biliyorsan makale düzeyinde belirt, verisi yokken dil uydurma.

**Kabul ve kanıt:** İşletim sistemi/tr-TR kültüründe UI ay adları tutarlı; saat dönüşümü değişmemiş; kalan Türkçe sistem mesajı envanteri boş veya belgeli. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni localization altyapısı, çok dilli URL ve içerik çevirisi yok.

**Geri dönüş:** Metin farkını geri al; veritabanı metinlerini değiştirme.

**İzlenebilirlik:** Kök AGENTS.md UI/dil kuralları; F8; Rapor §8.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

