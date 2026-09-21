# Performans, işletim ve kabul

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F48 — Ölçülmüş sorgu ve sayfa performansını iyileştir

**Neden:** Tam liste okumaları ve izlenen entity'ler veri büyüdükçe maliyet yaratıyor.

**Ön koşul:** F47 tamamlanmış.

**Dokunulacak alanlar:** Repository/service read paths; gerekli index migration; görsel view attributes.

**Agent'ın uygulayacağı sıra:**

1. F40/F41 sonrası temsilî test verisinde ana sayfa/detay/arama/admin sorgu sayısı, süre ve dönen kayıt miktarını ölç; ilk ölçüm kaydet.
2. Yalnızca okuma yollarında AsNoTracking, DB tarafında filtre/sınırlama ve gerekli navigation yüklemesi kullan. Güncelleme yollarını körlemesine no-tracking yapma.
3. EXPLAIN çıktısına göre gerçekten kullanılan sorgulara gereken küçük index'i ekle; her alana index veya ölçmeden full-text extension ekleme.
4. Görsellerin width/height ve uygun below-fold lazy loading davranışını düzelt; hero/LCP görselini geciktirme. Aynı koşullarda önce/sonra ölç.

**Kabul ve kanıt:** Aynı fixture/veri/ortamda iyileşme kanıtlı; N+1/full-table materialization yok; CRUD/concurrency kontrolleri geçer; görsel kayma azalır. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Sentetik tek ölçümü gerçek kullanıcı CWV sonucu diye sunma; yeni cache servisi/CDN/arama motoru yok.

**Geri dönüş:** Index geri dönüşünde veri korunur; kod performans diff'i geri alınabilir.

**İzlenebilirlik:** Rapor §10; ISP/SRP.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F49 — Medya kayıtlarını bakım yapılabilir hale getir

**Neden:** Yalnızca URL tutmak kapak değiştirme ve kullanılmayan görselleri güvenle ayırt etmeyi zorlaştırıyor.

**Ön koşul:** F48 tamamlanmış.

**Dokunulacak alanlar:** ImageService; Post kapak bilgileri; gerekirse nullable media metadata migration; admin form.

**Agent'ın uygulayacağı sıra:**

1. Mevcut Cloudinary sonucundan doğrulanmış PublicId ve boyut gibi gereken en az metadata'yı sakla; hemen ayrı kapsamlı MediaAsset sistemi kurma. Eski URL'lerden kesin olmayan kimlik tahmin etme.
2. Kapak için isteğe bağlı gerçek açıklama alanı ekle; dekoratif görsellerin alt metnini boş bırakmayı destekle. Kullanıcı metnini encode et.
3. Başarısız upload/DB save veya kapak değiştirme durumlarının sahiplik ve geri dönüş davranışını belgeleyip test et.
4. Kullanılmayan medya için yalnızca dry-run envanteri çıkar; Markdown içi ve diğer yazılardaki referansları kontrol et. Gerçek uzak dosyayı otomatik silme; silme ayrı, somut listeyle onay gerektiren bakım işlemidir.

**Kabul ve kanıt:** Yeni kapak metadata'sı doğru; eski kapaklar çalışır; upload başarısızlığında mevcut görsel korunur; dry-run hiçbir uzak kaynağı değiştirmez. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Medya kütüphanesi, toplu uzaktan silme ve üçüncü taraf hesabına taşınma yok.

**Geri dönüş:** Nullable kolonları bırakabilen uyumlu kod geri dönüşü; hiçbir Cloudinary dosyasını rollback bahanesiyle silme.

**İzlenebilirlik:** G7; Rapor §9 ve §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F50 — Dağıtım sınırlarını ve reverse proxy güvenini tanımla

**Neden:** HTTPS, cookie, URL ve rate limit gerçek proxy düzenine göre doğru çalışmalı.

**Ön koşul:** F49 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; configuration validation; .env.example; deployment documentation.

**Agent'ın uygulayacağı sıra:**

1. Gerçek barındırma topolojisini mevcut deploy/config kaynağından keşfet; bilinmiyorsa kullanıcıdan yalnızca barındırma tercihini iste, platform uydurma.
2. Forwarded headers için yalnızca bilinen proxy/network'lere güven; istemcinin gönderdiği X-Forwarded-For/Host/Proto değerlerine sınırsız güvenme. Middleware sırasını auth/rate limit/HTTPS ile birlikte doğrula.
3. AllowedHosts, production SITE_URL, HTTPS/HSTS ve cookie davranışını actual platform gereksinimiyle ayarla; development testlerini kıran production zorlamasını ayır.
4. F06 limitinin çok instance'da toplam sınır garantisi vermediğini belgeye yaz; gerçek dağıtım çok instance ise platformdaki mevcut rate limit olanağını doğrula, hayali Redis ekleme.

**Kabul ve kanıt:** Proxy fixture'larında sahte header client kimliği/URL'yi değiştiremez; HTTPS redirect loop yok; cookie ve limit doğru kaynağı kullanır; secret loglanmaz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** DNS/canlı hosting değişimi ve kullanıcının hesabında servis satın alma yok.

**Geri dönüş:** Bilinen güvenli config sürümüne dön; bütün proxy'lere güvenme ile sorunu kapatma.

**İzlenebilirlik:** G6, G10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F51 — CSP uyumluluğunu raporlama modunda hazırla

**Neden:** Inline script/handler ve dış asset'ler doğrudan sıkı CSP uygulanınca editörü bozabilir.

**Ön koşul:** F50 tamamlanmış.

**Dokunulacak alanlar:** Layout/view inline scripts; site/editor JS; response header setup.

**Agent'ın uygulayacağı sıra:**

1. F35 asset envanterine göre script/style/font/image/frame/connect kaynaklarını listele; yalnızca gerçekten kullanılan Cloudinary/YouTube gibi origin'leri dahil et.
2. Inline event handler'ları addEventListener'a taşı; gerekli inline yapılandırmayı encode edilmiş data attribute veya nonce kullanan küçük blokla sınırla. eval gereksinimini doğrulamadan unsafe-eval ekleme.
3. Content-Security-Policy-Report-Only başlığını lokal/test ortamında uygula; yeni public rapor API'si kurmadan tarayıcı ihlallerini kaydet. Bu modun saldırıyı engellemediğini açıkça belirt.
4. Okuyucu/admin/login/editor/upload/preview/code/embed akışlarında ihlalleri gider; XSS önleme F03/F04 yerine yalnızca CSP'ye yaslanma.

**Kabul ve kanıt:** Gerekli akışlarda açıklanmamış CSP ihlali yok; mevcut içerik/embed çalışıyor; script izinleri geniş wildcard içermiyor. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** CSP'yi yalnızca header var diye tamamlandı sayma; üçüncü taraf raporlama servisi yok.

**Geri dönüş:** Report-Only başlığını geri almak mümkündür; daha önceki XSS düzeltmelerine dokunma.

**İzlenebilirlik:** G2, G3, G10. CSP için OWASP kaynağı: https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F52 — Doğrulanmış CSP ve güvenlik başlıklarını uygula

**Neden:** Raporlama politikası tek başına koruma sağlamaz.

**Ön koşul:** F51 tamamlanmış.

**Dokunulacak alanlar:** Program.cs/header middleware; security smoke checks.

**Agent'ın uygulayacağı sıra:**

1. F51'in kanıtlanmış politikasını test ortamında enforce et; nonce gerekiyorsa her response'ta değişsin ve HTML/output cache ile tutarlı olsun. Cache'ten farklı nonce'lı body/header eşleşmesi üretme.
2. X-Content-Type-Options, Referrer-Policy ve frame-ancestors gibi gereken başlıkları açıkça ayarla; production HSTS F50 ile tek yerde yönetilsin.
3. Yetkili editör dahil bütün kritik akışları ve XSS fixture'larını yeniden çalıştır. İhlali çözemiyorsan geniş unsafe izinler eklemek yerine o akışın ihtiyacını raporla.

**Kabul ve kanıt:** CSP engelleme modunda; bütün temel akışlar geçer; cache/nonce uyumlu; güvenlik header'ları HTML/hata yanıtlarında tutarlı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Kanıt olmadan canlıya policy itme veya CSP ile sanitization'ı ikame etme yok.

**Geri dönüş:** Gerekirse önceki doğrulanmış CSP sürümüne dön; report-only'a düşmek koruma kaybı olarak açıkça kaydedilir.

**İzlenebilirlik:** G10; savunma katmanı.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F53 — Gerçek hata ve servis durumlarını görünür yap

**Neden:** Cloudinary Active gibi statik etiketler arızayı gizliyor; operasyonel teşhis zayıf.

**Ön koşul:** F52 tamamlanmış.

**Dokunulacak alanlar:** AdminController/dashboard status bölümü; logging configuration; Program.cs.

**Agent'ın uygulayacağı sıra:**

1. Hardcoded Active etiketlerini Configured/Not configured/Verified gibi gerçekten kanıtlanan duruma çevir; credential varlığı servis erişimi sayılmaz.
2. DB bağlantısını düşük maliyetli süre sınırlı check ile yetkili admin ekranında göster; Cloudinary için SDK'nin doğrulanmış zararsız check'i yoksa son başarılı işlem durumunu kullan.
3. İstek correlation ID, uygun log level ve kritik auth/upload/webhook hata kayıtlarını ekle; parola/token/cookie/ham post içeriğini loglama.
4. İptal edilmiş istekleri sunucu çökmesi gibi raporlama; log saklama/erişim ve gerçek barındırma log yolunu belgeye yaz.

**Kabul ve kanıt:** Bozuk test DB/medya config doğru durum verir; admin dışına hassas teşhis sızmaz; secret taraması temiz; timeout uygulamayı kilitlemez. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni public health Web API, dış izleme hesabı veya sürekli polling servisi yok.

**Geri dönüş:** Durum widget'ını kaldırmak mümkün; hatalı Active sabit metnine dönme.

**İzlenebilirlik:** G10; Rapor §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F54 — Git deposundaki üretilmiş dosyaları temizle

**Neden:** bin/obj ve cookies.txt izleniyor; tekrar üretilebilirlik ve oturum gizliliği zarar görüyor.

**Ön koşul:** F53 tamamlanmış.

**Dokunulacak alanlar:** .gitignore; git index; README/security operation notes.

**Agent'ın uygulayacağı sıra:**

1. Önce git ls-files ile gerçekten izlenen bin/obj/cookies dosyalarını listele; mevcut kullanıcı değişikliklerini sınıflandırmadan toplu git add/reset yapma.
2. Uygun ignore kurallarını ekle; yalnızca doğrulanmış üretilmiş dosyaları index'ten çıkar, kullanıcının yerel dosyasını silme. .env.example içinde yalnızca placeholder olsun.
3. Cookie dosyasının değerini çıktılamadan olası aktif oturumu F08 session version mekanizmasıyla geçersizleştirme ihtiyacını belirle; canlı oturum iptali gerçek ortamda ayrı kaydedilir.
4. Git geçmişinin bu işlemle temizlenmediğini açıkça belirt; geçmiş yeniden yazma ve remote force-push bu fazın parçası değildir.

**Kabul ve kanıt:** Temiz build sonrası git status üretilmiş dosya doldurmuyor; kaynaklar/yerel .env korunuyor; takipte cookie/secret yok; önceki değişikliklere dokunulmamış. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Çalışma ağacını temizlemek için reset --hard, clean -fd, gerçek dosya silme ve history rewrite yok.

**Geri dönüş:** Ignore/index farkını kontrollü geri al; secret dosyasını yeniden track etme.

**İzlenebilirlik:** G10; Rapor §10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F55 — Otomatik kalite kapısını kur

**Neden:** Test/CI eksikliği düzeltmelerin sonraki agent değişikliğinde geri bozulmasına açık kapı bırakıyor.

**Ön koşul:** F54 tamamlanmış.

**Dokunulacak alanlar:** Doğrulama scripts; SDK/tool configuration; mevcut CI platformu varsa workflow; README.

**Agent'ın uygulayacağı sıra:**

1. Kurulu ve desteklenen net10 SDK/EF paket uyumunu doğrula; gerekliyse global.json/tool manifest ile tekrarlanabilir sürümü sabitle. Patch yükseltmesini ayrı gerekçe ve resmi güvenlik notlarıyla yap; körlemesine major upgrade yapma.
2. Asset build, dotnet restore/build ve anlamlı HTTP/DB güvenlik-doğruluk kontrollerini tek belgeli komut zincirine bağla. CI DB'si test fixture'ı olsun; gerçek env secret istemesin.
3. Repository'nin gerçek CI sağlayıcısını keşfet; yoksa çalıştırılabilir yerel script ve seçilmiş sağlayıcı için incelenebilir config hazırla. Remote workflow tetiklenmediyse başarı iddiasında bulunma.
4. Dependency vulnerability kontrolünde gerçek advisory/source/version eşleşmesini kaydet; ağ olmadığı için kontrol yapılamadıysa bunu başarısız/eksik kanıt olarak yaz, temiz rapor gibi sunma.
5. Kaynakların root projeye glob ile yanlışlıkla dahil olmamasını ve mevcut dört projeli düzende komutların çalışmasını doğrula.

**Kabul ve kanıt:** Clean ortamda tekrar build; anlamlı kontroller geçer; olmayan paket/tool çağrısı yok; CI secret'ı gereksiz değil; gerçekten çalışmış kontrollerle yalnızca hazırlanmış config ayrılmış. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Her helper için uygulamayı taklit eden unit test, kapsam metriği uğruna test ve yeni test paketi varsayımı yok.

**Geri dönüş:** CI dosyası/script geri alınabilir; yerel çalışan build sözleşmesini koru.

**İzlenebilirlik:** Rapor §10; sürdürülebilirlik.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F56 — Yedek geri yükleme ve yayınlama prosedürünü kanıtla

**Neden:** Yedek alınması tek başına geri dönebilmeyi göstermiyor; migration ve medya birlikte düşünülmeli.

**Ön koşul:** F55 tamamlanmış.

**Dokunulacak alanlar:** docs deployment/runbook; güvenli backup/restore script taslakları; test ortamı.

**Agent'ın uygulayacağı sıra:**

1. DB, medya referansları, migration sürümü ve secret dışındaki gerekli config için yedek kapsamını gerçek topolojiye göre yaz; gerçek secret'ları dokümana kopyalama.
2. Yalnızca izole test DB'sinde yedek al/başka boş test DB'ye geri yükle; yazı/kategori/yayın zamanı/medya linki eşleşmesini doğrula.
3. Staging için build → backup → migration → smoke → trafik açma sırasını ve önceki binary ile schema uyumluluğunu yaz. Veri kaybettiren down migration'ı varsayılan rollback yapma.
4. Canlı yayımlama gerekiyorsa staging kanıtı, migration listesi ve geri dönüş planını somutlaştırıp ancak kullanıcı bu canlı işlemi yetkilendirdiğinde uygula. Bu planın hazırlanması canlı dağıtım talimatı değildir.

**Kabul ve kanıt:** Restore denemesi veriyle kanıtlı; başarısız migration/health durumunda ne yapılacağı belli; test ile canlı ayırt edilmiş; bilinmeyen hosting adımı uydurulmamış. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Canlı DB restore, deploy, uzak dosya silme veya dış hesaba erişim değişimi yok.

**Geri dönüş:** Test kaynakları kontrollü kaldırılır; canlıya dokunulmadığı için prod rollback iddiası yok.

**İzlenebilirlik:** Rapor §10 işletim.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F57 — Uçtan uca kabul ve yeni SOLID değerlendirmesi

**Neden:** Fazların tamamlanması yalnızca işaret sayısıyla değil bütün blog akışıyla doğrulanmalı.

**Ön koşul:** F56 tamamlanmış.

**Dokunulacak alanlar:** DURUM.md; uygulama kayıtları; yeni sonuç raporu; test fixture/scripts.

**Agent'ın uygulayacağı sıra:**

1. İzole ortamda login → kategori → taslak → görsel → başarısız kayıt/kurtarma → önizleme → zamanlama → yayın → arama/SEO/RSS → edit → unpublish akışını çalıştır; korunan webhook/feed varsa dahil et.
2. Eşzamanlı viewcount/edit, kategori silme, cache invalidation ve scheduled release kontrollerini tekrar çalıştır. F02–F08 XSS/auth/CSRF kontrollerini CI üzerinden doğrula.
3. Mobil/masaüstü/klavye/zoom ve konsol kontrollerini kaydet; gerçek kullanıcı CWV yoksa laboratuvar performansını ayrı adlandır.
4. Beş SOLID başlığını başlangıçtaki 56/100 rubriğiyle dosya kanıtları üzerinden tekrar puanla; hedef 80–85 bir tahmindir, tamamlandı diye otomatik puan verme.
5. P0/P1 açık, başarısız kabul veya zorunlu canlı karar varken 'üretime hazır' yazma. Tamamlananları, kalanları ve bilinçli ertelenen ürün özelliklerini sade Türkçe sonuç raporuna dök.

**Kabul ve kanıt:** Bütün geçerli fazlar kanıtlı; atlananlar seçimin gerekçesiyle işaretli; kritik riskler kapalı; eksikler görünür; kullanıcı temel blog işlerini kod yazmadan yapabiliyor. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Son kontrolde yeni ürün özelliği ekleme veya tüm kodu yeniden yazma yok; yeni bulguyu ayrı atomik faz önerisi yap.

**Geri dönüş:** Bu faz yalnızca değerlendirme yapar; başarısızlık ilgili faza geri açılır.

**İzlenebilirlik:** Raporun tümü; nihai kabul.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

