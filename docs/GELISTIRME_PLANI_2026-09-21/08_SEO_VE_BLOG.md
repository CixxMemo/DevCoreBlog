# SEO, RSS ve site kimliği

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F43 — Kalıcı ve kanonik URL sözleşmesini uygula

**Neden:** Aynı yazı /post ve /yazi altında 200 dönebiliyor; içerik birden çok adrese bölünüyor.

**Ön koşul:** F42 tamamlanmış.

**Dokunulacak alanlar:** HomeController route'ları; link üreten views/services; SiteUrl configuration.

**Agent'ın uygulayacağı sıra:**

1. Önce mevcut route ve dış linkleri envanterle; English UI ile /post/{slug} yolunu yazılar için kanonik kabul et. Çakışan gerçek kullanım varsa taşımadan önce somut alternatif sun.
2. Eski /yazi/{slug} yolundan doğru yeni adrese 301 yönlendir; query/slug encode kurallarını koru. Kategori rotası için de mevcut İngilizce yolu esas al; yeni slug üretme.
3. Mutlak URL kökünü yalnızca doğrulanmış SITE_URL yapılandırmasından üret; gelişigüzel Host/X-Forwarded-Host değerlerinden alma. Development localhost ve production HTTPS kurallarını ayır.
4. Nav, kart, ilgili yazı, feed korunuyorsa feed ve admin View bağlantılarını aynı sözleşmeye geçir.

**Kabul ve kanıt:** Eski URL tek sıçramada 301 ve yeni URL 200; bulunmayan slug 404; redirect loop yok; host spoof testinde üretilen URL sabit; eski slug'lar korunmuş. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Toplu slug değiştirme, domain satın alma veya gerçek DNS değişimi yok.

**Geri dönüş:** Eski URL'leri açık tut; yanlış 301 yayımlanmışsa kalıcı cache etkisini ayrıca değerlendir.

**İzlenebilirlik:** F2; Rapor §9 SEO.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F44 — Sayfa meta verilerini ve makale şemasını ekle

**Neden:** Canonical, description, sosyal paylaşım verisi ve yapılandırılmış makale bilgisi eksik.

**Ön koşul:** F43 tamamlanmış.

**Dokunulacak alanlar:** Layout/head partial; article views; Post entity/service; gerekirse UpdatedDate migration.

**Agent'ın uygulayacağı sıra:**

1. Ana sayfa/kategori/yazı için benzersiz title ve kısa description üret; içerik yazısında varsa özeti kullan, ham Markdown/HTML'yi meta'ya basma.
2. Canonical, Open Graph ve sosyal görsel alanlarını F43 doğrulanmış URL kaynağından üret. Geçerli görsel yoksa güvenli mevcut site görselini kullan veya alanı kaldır.
3. BlogPosting JSON-LD'yi güvenli JSON serializer ile üret; HTML içinde script kapatma payload'ını test et. Yazar/yayıncı gibi gerçek bilgisi olmayan alanları uydurma.
4. Güncellenme zamanı yoksa nullable UpdatedDate alanını küçük migration ile ekle; yalnızca anlamlı içerik değişikliklerinde güncelle, sayaç artışında değiştirme. Eski kayıtlar için sahte güncelleme tarihi doldurma.
5. Arama, admin ve önizleme sayfalarının indeksleme davranışını belirle; noindex'i erişim kontrolü gibi kullanma.

**Kabul ve kanıt:** Her sayfada tek canonical; metinler encode; malicious title script çalıştırmaz; JSON parse edilir; gerçek tarih/URL/görsel kullanılır; sayaç SEO tarihini değiştirmez. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Uydurma yazar, sahte rating ve otomatik içerik üretimi yok.

**Geri dönüş:** Yeni nullable alanı veri kaybettiren down migration ile silmek yerine uyumlu kod geri dönüşü kullan.

**İzlenebilirlik:** G2; Rapor §9.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F45 — Sitemap ve robots çıktısını düzelt

**Neden:** XML encoding bildirimi yanıtla uyuşmuyor; lastmod ve görünür içerik listesi güvenilir değil.

**Ön koşul:** F44 tamamlanmış.

**Dokunulacak alanlar:** SeoController; sitemap/robots actions; PostService.

**Agent'ın uygulayacağı sıra:**

1. XML'i gerçekten gönderilen UTF-8 byte dizisiyle uyumlu üret; StringBuilder üzerinden UTF-16 bildirimi yazma. Doğru Content-Type ve XML escaping kullan.
2. F17 yayın filtresi, F43 kanonik URL ve F44 gerçek UpdatedDate/PublishDate bilgisini kullan; private/future/alias URL ekleme.
3. robots.txt içine doğru mutlak sitemap adresini koy; admin gizliliğini robots'a bağlama. URL sayısı mevcut protokol sınırına yaklaşmıyorsa gereksiz sitemap index mimarisi kurma.
4. Yayın/tarih/kategori değişikliklerinde F18 cache geçersizleştirmesine dahil et.

**Kabul ve kanıt:** XML parser hatasız; encoding/header uyumlu; future/draft/pasif yok; doğru lastmod/canonical; robots sitemap adresi gerçek config'ten. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Arama motoru hesaplarına kayıt veya site sahipliği doğrulaması bu fazda yok.

**Geri dönüş:** Geçerli önceki XML üretimine dön; yanlış görünürlük filtresine dönme.

**İzlenebilirlik:** G5, G8; Rapor §9 SEO.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F46 — Standart RSS ile takip edilebilirlik sağla

**Neden:** Blog yazılarının platformdan bağımsız takip edilmesi için standart yayın akışı gerekiyor.

**Ön koşul:** F45 tamamlanmış; D05 MVC XML sınırı bu fazda doğrulanır.

**Dokunulacak alanlar:** SeoController'a MVC XML content action; RSS discovery link; PostService.

**Agent'ın uygulayacağı sıra:**

1. D05'te XML yayın belgesinin klasik MVC çıktısı olduğu tanımlıdır; yeni JSON/Web API, API projesi veya controller mimarisi kurma. D05'teki MVC XML sınırını doğrula; RSS için yeni API mimarisi kurma.
2. Sınırlı sayıda son görünür yazıdan RSS 2.0 XML belgesi üret; sabit GUID, kanonik link, doğru tarih, güvenli metin özeti ve site başlığı kullan.
3. Head'de RSS discovery ve uygun yerde takip bağlantısı ekle; cache/tag politikasını sitemap ile uyumlu tut.

**Kabul ve kanıt:** XML okunur; entry kimlikleri edit sonrası değişmez; gizli yazı yok; yayın kaldırma güncellenir; encoding doğru. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Newsletter servisi, e-posta listesi veya dış RSS hesabı oluşturma yok.

**Geri dönüş:** RSS girişini kaldırmak gerekirse mevcut aboneler için kontrollü kapanış planla; makaleleri değiştirme.

**İzlenebilirlik:** Rapor §9 blog kapsamı.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F47 — Gerçek site kimliğini ve temel bilgi sayfalarını tamamla

**Neden:** Hakkında/iletişim/yazar bilgisi ve sahte placeholder'ların temizlenmesi blogun güvenilirliğini artırır.

**Ön koşul:** F46 tamamlanmış veya D05 ile gerekçeli ertelenmiş; gerçek kimlik bilgileri doğrulanmış.

**Dokunulacak alanlar:** MVC bilgi sayfaları; layout/footer; site configuration; varsa mevcut author alanları.

**Agent'ın uygulayacağı sıra:**

1. Mevcut gerçek site adı, yazar adı, açıklama ve bağlantıları çıkar; eksik biyografi/iletişim bilgilerini tek kısa kullanıcı sorusunda iste. İsim, başarı veya e-posta uydurma.
2. Onaylanan gerçek metinle sade About/Contact sayfaları ve footer bağlantıları oluştur. Backend e-posta gönderimi olmadan mevcut doğrulanmış iletişim kanalını kullan.
3. Kişisel veri toplanıyorsa fiilen toplanan verileri envanterle ve gerekli politika metnini kullanıcı değerlendirmesine sun; olmayan newsletter/analytics varmış gibi metin üretme.

**Kabul ve kanıt:** Tüm footer bağlantıları çalışır; demo domain/sahte sayaç/yanlış yazar kalmaz; kişisel bilgiler kullanıcı tarafından sağlanmış veya mevcut kaynaktan doğrulanmış. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Hukuki uygunluk garantisi, yeni kişisel veri toplama, e-posta gönderme ve sosyal hesap açma yok.

**Geri dönüş:** Yalnızca yeni bilgi sayfalarını/bağlantıları geri al; kullanıcının eski metnini koru.

**İzlenebilirlik:** Rapor §8 ve §9.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

