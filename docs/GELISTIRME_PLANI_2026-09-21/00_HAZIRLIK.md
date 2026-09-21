# Hazırlık ve kural kararı

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [İlerleme](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/DURUM.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu bölüm bir çalıştırma emri değildir. Her seferinde yalnızca **bir faz kartı** uygulanır. Kartın maddeleri aynı sonucun içindeki uygulama sırasıdır; ayrı özellik paketleri değildir. Değiştirmeden önce gerçek dosyaları oku. Dosya yolları mevcut dört projeli yapıyı tarif eder; kökteki AGENTS.md bağlayıcıdır.

## F00 — Güncel agent kurallarını ve planı yerleştir

**Neden:** Eski tek proje/API/DTO yasakları gerçek mimari ve güvenli sınır modellemesiyle çelişiyordu. Kullanıcı kuralların yenilenmesini açıkça istedi.

**Ön koşul:** Kullanıcının 21 Eylül 2026 tarihli kural yenileme ve eski planları temizleme talebi. Yeniden A/B tercihi istenmez.

**Dokunulacak alanlar:** Kök AGENTS.md; .agents/AGENTS.md yönlendirmesi; eski plan arşivi; bu planın karar/durum/kart/prompt belgeleri; README.

**Agent'ın uygulayacağı sıra:**

1. Gerçek dört projeli MVC yığınını ve mevcut entegrasyonları kaynak koddan doğrula; yeni ana kurallarda mevcut yapıyı koru.
2. SOLID, kısa/DRY kod, güvenlik, veri bütünlüğü, UI/UX, dependency ve doğrulama kurallarını kök AGENTS.md içinde tek kaynak olarak tanımla. Küçük güvenli input DTO/ViewModel'lerini gerekçeli izinli yap.
3. Eski talimat/planları aktif .agents ağacından çıkar; tarihsel arşiv olarak açıkça işaretle. .agents/AGENTS.md yalnızca köke yönlendirsin.
4. Tek projeye dönüş/entegrasyon silme dalını aktif plandan çıkar. F27'yi tipli webhook input validation'ına, ilgili form/liste fazlarını yeni veri sözleşmesi kuralına uyarla.
5. Dosya bağlantıları, faz bağımlılıkları, tek talimat kaynağı ve uygulama kaynaklarının değişmediğini doğrula; kanıtı kaydet.

**Kabul ve kanıt:** Kök talimat dosyası mevcut; eski aktif plan yok; karar bekleyen A/B dalı yok; F00–F57 sırası tutarlı; uygulama dosyaları değişmemiş. Yalnızca belge değiştiği için build yerine dokümantasyon/tutarlılık doğrulaması uygulanır.

**Bu fazın dışında:** Uygulama kodu, DB, gerçek secret, endpoint veya production dağıtımı değiştirilmez. Kuralların yenilenmesi güvenlik düzeltmelerinin uygulandığı anlamına gelmez.

**Geri dönüş:** Orijinal kurallar ve planlar tarihsel arşivde korunur; yalnızca bu belge değişikliğini geri almak mümkündür.

**İzlenebilirlik:** Güncel kullanıcı talebi; kök AGENTS.md; KARARLAR D01–D06; önceki raporun mimari kural çelişkisi.

**Durma noktası:** F00 kanıtını ve DURUM'u kaydet; F01'i bu dokümantasyon çalışmasında başlatma.

---

## F01 — Tekrarlanabilir başlangıç ve güvenli doğrulama ortamı

**Neden:** İlk incelemedeki test sonuçlarını güncel kod için yeniden doğrulamak; başka agent’ın mevcut değişikliklerini korumak.

**Ön koşul:** F00 tamamlanmış; seçilen kural metni ve başlangıç sırası kayıtlı.

**Dokunulacak alanlar:** docs/uygulama-kayitlari/; gerekirse scripts/verification/ (yeni); uygulama kaynakları salt okunur.

**Agent'ın uygulayacağı sıra:**

1. Git durumunu, değişmiş ve izlenmeyen dosyaları kaydet; secret değerlerini kaydetme. Kullanıcının değişikliklerini reset/stash/silme. Mevcut çalışma ağacını içeren kontrollü inceleme kopyası hazırla.
2. Kurulu SDK, EF tool, paketler ve gerçek komutları keşfet. dotnet build DevCoreBlog.csproj çalıştır; sonucu ve uyarıları kaydet.
3. Yerel, adı açıkça test olan ayrı PostgreSQL ve sahte admin/medya yapılandırması kur. Gerçek .env/DB/Cloudinary hesabına yazma. Migration keşfi bozuksa yalnızca fixture şemasını testte kur ve bunun migration testi olmadığını belirt.
4. Standart kütüphanelerle sınırlı HTTP kontrol betiği ve güvenli test veri seti hazırla: yayınlı/taslak/gelecek/pasif, aynı başlık, Türkçe karakter, uzun metin. Mevcut açıkların beklenen başlangıç başarısızlıklarını ayrı kaydet.
5. Kontrolleri agent çalıştırır. Kullanıcıdan terminal açması, SQL yazması veya kod yapıştırması istenmez. Test sunucularını yalnızca ihtiyaç süresince çalıştır.

**Kabul ve kanıt:** Derleme kanıtı, veri izolasyonu, tekrar çalıştırılabilir kontrol komutları ve başlangıç bulguları kayıtlı; prod kaynakları etkilenmemiş. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Yeni test framework'ü/paket varsayma; şema düzeltme ve üretim dağıtımı yok.

**Geri dönüş:** Yalnızca oluşturduğun test süreçlerini durdur; test verisi dışındaki veriyi temizleme.

**İzlenebilirlik:** Rapor §2; bütün fazların kanıt altyapısı.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

