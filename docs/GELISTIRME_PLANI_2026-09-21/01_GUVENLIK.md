# Giriş, XSS, CSRF ve yükleme güvenliği

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F02 — Eksik yönetici bilgileriyle girişi kapat

**Neden:** Kullanıcı adı tanımlı, parola eksik olduğunda parolasız oturum açılabiliyor.

**Ön koşul:** F01 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; Controllers/AccountController.cs; .env.example; F01 doğrulama betikleri.

**Agent'ın uygulayacağı sıra:**

1. Mevcut environment okuma sırasını incele. Kullanıcı adı/parola eksik veya whitespace ise güvenli, secret içermeyen yapılandırma hatasıyla başlangıcı durdur.
2. Login parametrelerini nullable kabul edip boş/eksik girdiyi doğrulamadan önce reddet. ModelState ve beklenen yapılandırmayı kontrol et; istemcideki required özelliğine güvenme.
3. Yanlış girişte genel English hata mesajını koru; hiçbir başarısız durumda cookie üretme. Normal doğru giriş/çıkış davranışını koru.

**Kabul ve kanıt:** Eksik kullanıcı/parola yapılandırmaları başlamaz; parola alanı olmayan veya boş istek auth cookie alamaz; doğru test hesabı giriş yapar. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Parola hash geçişi, tasarım değişikliği ve yeni kimlik sistemi yok.

**Geri dönüş:** Güvenlik açığını tekrar açan sürüm yayına alınmaz; yerel faz diff'ini geri almak gerekirse uygulamayı dış erişime kapalı tut.

**İzlenebilirlik:** G1.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F03 — Yönetici bildirimini düz metin olarak göster

**Neden:** Post başlığı innerHTML üzerinden yönetici tarayıcısında kod çalıştırıyor.

**Ön koşul:** F02 tamamlanmış.

**Dokunulacak alanlar:** Views/Shared/_AdminLayout.cshtml; Views/AdminPost/Index.cshtml; mevcut bildirim çağrıları.

**Agent'ın uygulayacağı sıra:**

1. showToast içindeki HTML şablonundan kullanıcı/kayıt kaynaklı message interpolasyonunu kaldır.
2. İskeleti createElement ile kur; mesajı textContent ile ata; kapatma işlemini addEventListener ile bağla. Sabit rozet ve sınıflar kullanıcı verisinden türetilmesin.
3. Sunucu JSON encoding'ini koru; bütün bildirim çağrılarının string metin kullandığını doğrula.

**Kabul ve kanıt:** Zararsız img/onerror başlığı harfiyen görünür ve çalışmaz; başarı/hata/kapatma davranışı sürer. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Global frontend refactor veya yeni bildirim kütüphanesi yok.

**Geri dönüş:** Yalnızca bildirim diff'i geri alınabilir; HTML mesaj yolunu güvenli olmayan biçimde tekrar açma.

**İzlenebilirlik:** G3.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F04 — Güvenli Markdown ve kontrollü video üretimi

**Neden:** Ham HTML ve gelişmiş Markdown öznitelikleri saklanan XSS yolu oluşturuyor.

**Ön koşul:** F03 tamamlanmış.

**Dokunulacak alanlar:** DevCoreBlog.Core/Shared/Helpers/MarkdownHelper.cs; Views/Home/Detail.cshtml; güvenlik fixture'ları.

**Agent'ın uygulayacağı sıra:**

1. Kurulu Markdig sürümünün gerçek API'sini oku. Raw HTML'yi kapat; UseAdvancedExtensions yerine gerçekten kullanılan güvenli uzantıları açıkça seç. GenericAttributes gibi serbest öznitelik yollarını açma.
2. Bağlantı ve resim URL'lerini parse edilmiş Markdown düğümleri/renderer sınırında doğrula; javascript/data/vbscript ve obfuscation örneklerini reddet. Normal https bağlantılar, güvenli göreli iç linkler, tablo/liste/kod çalışsın.
3. Video etiketini HTML regex sanitizasyonuyla işleme. Yalnızca onaylanan YouTube host'undan doğrulanan video kimliği için kontrollü Markdig düğümü/renderer kullan; sabit iframe şablonu ve kodlanmış değerler üret.
4. Ham HTML'yi metin/etkisiz çıktı olarak ele al; depodaki içerikleri topluca yeniden yazma. Güvenli alt küme yeterli değilse regex tabanlı sanitizer icat etme; kanıtlanan ihtiyaç için bağımlılık kararı kaydet.

**Kabul ve kanıt:** script, onerror, SVG, tehlikeli URL, generic attribute ve sahte youtube host örnekleri çalışmaz; normal Markdown, C# kodu ve izinli video render edilir. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni sanitizer paketinin kurulu olduğunu varsayma; ham HTML desteğini korumak için güvenliği gevşetme; veri göçü yok.

**Geri dönüş:** Yeni renderer sorunluysa yalnızca güvenli düz metin gösterimine dön; önceki XSS yoluna dönme.

**İzlenebilirlik:** G2; Markdig resmi kaynakları ve OWASP XSS.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F05 — Cookie POST işlemlerinde tutarlı antiforgery

**Neden:** Login/logout/toggle/upload işlemlerinde token doğrulaması eksik.

**Ön koşul:** F04 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; AccountController; AdminPostController; ilgili Razor formları ve fetch çağrıları.

**Agent'ın uygulayacağı sıra:**

1. Cookie kullanan MVC action kapsamına tutarlı AutoValidateAntiforgeryToken veya eşdeğer action koruması ekle.
2. Form tokenını render et; mevcut toggle ve upload fetch isteklerine gönder. Tokenı URL'ye veya loga yazma.
3. Yalnızca AGENTS.md kapsamında korunan mevcut secret-auth webhook için açık ve dar istisna uygula; o endpoint hiçbir şekilde admin cookie'sine güvenmesin. Hatalı token cevabını UI anlaşılır göstersin.

**Kabul ve kanıt:** Giriş dahil bütün cookie POST uçları eksik/yanlış tokenla 400; geçerli tokenla normal akış; anonim yetkisiz yazma reddi; webhook secret kontrolü korunmuş. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Genel IgnoreAntiforgeryToken kullanma; yeni endpoint/DTO oluşturma.

**Geri dönüş:** Token üreten ve doğrulayan taraflar aynı faz diff'i olarak değerlendirilir; korumayı kalıcı kapatarak uyumsuzluk çözülmez.

**İzlenebilirlik:** G4.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F06 — Giriş denemelerini sınırlandır

**Neden:** Mevcut rate limiter login'e uygulanmıyor.

**Ön koşul:** F05 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; Controllers/AccountController.cs; login hata görünümü.

**Agent'ın uygulayacağı sıra:**

1. Yerleşik RateLimiter ile yalnızca login POST'a açık bir deneme sınırı ve kısa süreli bekleme politikası bağla.
2. Güvenilmeyen forwarded header'ı IP kabul etme; F50 dağıtım ayarına kadar doğrudan bağlantı IP'si kullan. Tek admin hesabını kolayca kalıcı kilitleyen mekanizma kurma.
3. 429 davranışı ve uygun bekleme bilgisini English olarak göster; parola/kullanıcı girdisini loglamadan deneme olaylarını izle.

**Kabul ve kanıt:** Testte eşik aşımı 429; pencere sonrası giriş açılır; public sayfalar etkilenmez; tek değişkenli istemci verisi sınırsız partition üretmez. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** CAPTCHA, hesap/rol sistemi ve Redis gibi yeni altyapı yok.

**Geri dönüş:** Eşik yapılandırmasını düzelt; bütün limiter'ı sessizce kaldırma.

**İzlenebilirlik:** G6.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F07 — Parola doğrulamasını hash'e geçir

**Neden:** Düz metin ADMIN_PASSWORD kalıcı kimlik bilgisi olarak tutuluyor.

**Ön koşul:** F06 tamamlanmış.

**Dokunulacak alanlar:** AccountController; mevcut servis katmanında küçük credential doğrulayıcı (yeni); Program.cs; .env.example; yerel hash üretim yardımcısı (yeni).

**Agent'ın uygulayacağı sıra:**

1. Identity kullanmadan .NET'in yerleşik statik PBKDF2 API'si, kriptografik rastgele salt ve sabit zamanlı hash karşılaştırması kullan. Sürüm/algoritma/iterasyon/salt/hash formatını net tanımla; özel hash algoritması yazma.
2. Maliyet değerini uygulama anındaki resmi rehber ve yerel ölçümle belirle; parse edilen parametrelere alt/üst sınır koy. Mevcut olmayan NuGet paketi ekleme.
3. ADMIN_PASSWORD_HASH ile kapalı hata davranışı kur; malformed hash girişe izin vermesin. Geçişi test ortamında hazırla; gerçek parolayı rapora, shell argümanına veya Git'e yazma.
4. Gerçek credential değişimini güvenli yapılandırma işlemi olarak ayrı kaydet. Hash hazır olmadan gerçek ortama geçme; düz metne sessiz geri dönüş ekleme.

**Kabul ve kanıt:** Aynı parola farklı salt ile farklı hash; doğru/yanlış/boş parola sonuçları doğru; bozuk hash güvenli hata; örneklerde gerçek secret yok. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** ASP.NET Core Identity, üyelik, rol/claim genişlemesi yok.

**Geri dönüş:** Hash yapılandırmasıyla çalışan son güvenli sürüm korunur; otomatik plaintext fallback yok.

**İzlenebilirlik:** G6; .NET PBKDF2 ve OWASP parola saklama.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F08 — Cookie ve oturum yaşam döngüsünü belirle

**Neden:** Cookie güvenliği, süre ve credential değişiminde oturum iptali açık değil.

**Ön koşul:** F07 tamamlanmış.

**Dokunulacak alanlar:** Program.cs; AccountController; .env.example; deployment notları.

**Agent'ın uygulayacağı sıra:**

1. HTTPS üretimde Secure, HttpOnly ve uygun SameSite politikasını; açık oturum süresi ve yenileme sınırını belirle.
2. Basit cookie event/AuthenticationProperties mekanizmasıyla sunucu credential/session sürümü değişince eski bileti reddet. Ek rol/claim modeli veya kullanıcı tablosu kurma.
3. Data Protection için dağıtımda kalıcı/korumalı key yolu ve tek uygulama kimliği sözleşmesini belgele. Gerçek key dosyalarını bu fazda silme veya sıfırlama.

**Kabul ve kanıt:** Cookie bayrakları HTTPS fixture'da doğru; expiry/yenileme sınırı işler; test credential sürümü değişince önceki cookie reddedilir; restart aynı test anahtarıyla öngörülebilir. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Gerçek kullanıcı oturumlarını bu plan hazırlanırken geçersiz kılma; SSO/JWT yok.

**Geri dönüş:** Anahtar malzemesi korunur; yalnızca yapılandırma/code diff'i kontrollü geri alınır.

**İzlenebilirlik:** G6, G10.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## F09 — Bütün görsel yüklemelerine aynı güvenlik politikası

**Neden:** Kapak ve editör yüklemelerinin denetimleri tutarsız.

**Ön koşul:** F08 tamamlanmış.

**Dokunulacak alanlar:** IImageService/ImageService; AdminPost upload/create/edit; upload hata gösterimi.

**Agent'ın uygulayacağı sıra:**

1. Tek ortak doğrulama: dosya varlığı, uygulama boyut sınırı, izin verilen uzantı ve içerik imzası. SVG/HTML ve tanınmayan biçimleri reddet.
2. Uzantı/MIME tek başına yeterli sayılmaz; gerçek decode/format/piksel sınırını mevcut Cloudinary'nin doğrulanmış upload işleme seçenekleriyle uygula. Mevcut araçlarla güvenilir decode doğrulanamazsa başarı iddiası yerine somut bağımlılık ihtiyacını kaydet; el yapımı image parser yazma.
3. Kullanıcının dosya adını depolama yetkisi/yolu yapma. Tek common akış, token ve kota; başarısız upload DB kaydını bozmasın.
4. Gerçek sağlayıcı testi gerekirse yalnızca izinli test hesabında sentetik küçük dosya ve belirli test klasörü kullan; mock ile dış sağlayıcı testi geçmiş sayılmaz.

**Kabul ve kanıt:** Aşırı büyük, uzantısı sahte, SVG, bozuk dosya reddi; izinli örnekler doğru; kapak/editör aynı politika; CDN hatası açık ve secretsiz. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni medya sağlayıcısı ve gerçek Cloudinary varlıklarını silme yok.

**Geri dönüş:** Yeni yüklemeyi kapatmak güvenli fallback; eski görseller görünmeye devam eder.

**İzlenebilirlik:** G7.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

