> TARİHSEL ARŞİV — AKTİF PLAN DEĞİLDİR. Yeni kök AGENTS.md ile 21 Eylül 2026 tarihinde yürürlükten kaldırıldı.

# Koşullu tek proje yolu

[Ana plan](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/README.md) · [Kararlar](/Users/mehmetcankocakurt/Documents/development/DevCoreBlog/docs/GELISTIRME_PLANI_2026-09-21/KARARLAR.md)

Bu dört kart yalnızca kullanıcı **D01=B** seçerse etkinleşir. Sıra: **F09 → K01 → K02 → K03 → K04 → F10**. Önce Services, sonra Data, sonra Core taşınır; böylece kalan class library'ler Web projesine bağımlı hale getirilmez ve circular reference önlenir. Yalnızca planın varlığı endpoint kaldırmaya yetki vermez.

## K01 — Dış API entegrasyonlarını kontrollü sonlandır

**Neden:** Yalnızca D01=B seçilirse mevcut Web API yasağına geri dönmek gerekir; otomasyon ve portföy özellikleri azalacaktır.

**Ön koşul:** D01=B, F09 tamamlanmış; entegrasyonların sonlanması açıkça seçilmiş.

**Dokunulacak alanlar:** WebhookController; PublicFeedController; Program.cs; Automations view; entegrasyon belgeleri.

**Agent'ın uygulayacağı sıra:**

1. Kaldırılacak iki dış sözleşmeyi ve kullanan gerçek istemcileri envanterle; kullanıcının B seçiminin bu ürün kaybını kapsadığını kaydet. Harici otomasyonu kendin düzenleme/mesaj gönderme.
2. Mevcut MVC login/CRUD/upload action'larını koru. Dış endpoint'leri kontrollü olarak kaldır; CORS, secret/rate-limit/policy ve UI bağlantılarını yalnızca artık kullanım yoksa temizle.
3. Webhook DTO'su dahil yalnızca sonlanan entegrasyon kodunu kaldır. Yazıları veya geçmiş kaynağını silme; önceki değişiklikleri taşıyan güvenli snapshot kalsın.

**Kabul ve kanıt:** Kaldırılmış URL'ler veri üretmez/döndürmez; MVC admin ve public blog çalışır; broken Automations menüsü yok; kaynak yazılar korunur. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Kullanıcının onayı olmadan bu kart etkin değildir; gerçek dış sistem hesabını düzenlemek yok.

**Geri dönüş:** Entegrasyon ancak ayrı açık izin ve güvenli sürümle geri açılır; DB kayıtları korunur.

**İzlenebilirlik:** D01=B; .agents/AGENTS.md API yasağı.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## K02 — Services katmanını Web projesindeki klasöre taşı

**Neden:** Tek projeye dönüşü derlenebilir küçük taşımalara bölmek.

**Ön koşul:** K01 tamamlanmış.

**Dokunulacak alanlar:** DevCoreBlog.Services/** kaynakları; /Services; root csproj; solution; DI references.

**Agent'ın uygulayacağı sıra:**

1. Services kaynaklarını /Services klasörüne taşı; Core/Data referansları bu aşamada mevcut kalsın. Çakışma yoksa namespace'leri koruyarak davranışsal diff'i küçült.
2. Services projesinin gereken mevcut paket/framework referanslarını root'ta doğrula; duplicate derleme ve içerik glob'larını düzelt.
3. Root'tan Services ProjectReference'ını kaldır; solution girişini yalnızca taşıma tamamlanınca çıkar. Build ve auth/CRUD/upload contract smoke çalıştır.

**Kabul ve kanıt:** Services class library bağımlılığı kalmaz; Core/Data hâlâ derlenir; business behavior aynı. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Service refactor, yeni paket sürümü ve veri migration yok.

**Geri dönüş:** Bu taşımanın dosya/proje referansları birlikte geri alınır; diğer değişiklikler korunur.

**İzlenebilirlik:** D01=B; SRP mantıksal katmanlarda korunur.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## K03 — Data ve repository katmanını aynı projeye taşı

**Neden:** Services taşımasından sonra Data'nın Web'e geçmesi circular reference üretmeden yapılabilir.

**Ön koşul:** K02 tamamlanmış.

**Dokunulacak alanlar:** DevCoreBlog.Data kaynakları; /Data ve /Repositories; csproj; DbContext registration; solution.

**Agent'ın uygulayacağı sıra:**

1. DbContext'i /Data, repository'leri /Repositories içine taşı; entity/interface Core referansı bu aşamada kalsın. Root migrations geçmişini taşımayla yeniden üretme.
2. Data paket referanslarını ve root compile exclusions'ını doğrula; artık kullanılmayan Data ProjectReference/solution kaydını kaldır.
3. Derle, test CRUD'u çalıştır ve migration keşfi durumunu kaydet; F10 tarihçe ve boş/mevcut DB doğrulamasını ayrıca tamamlayacak.

**Kabul ve kanıt:** Data class library bağımlılığı yok; DbContext tek; migration geçmişi kaybolmamış; baseline'a yeni derleme hatası eklenmemiş. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Index/kolon ekleme, migration tarihçesini sıfırlama yok.

**Geri dönüş:** Dosya/proje referanslarını birlikte geri al; DB şemasına dokunulmaz.

**İzlenebilirlik:** D01=B; F10 öncesi fiziksel düzen.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

---

## K04 — Core taşımasını tamamla ve tek projeyi doğrula

**Neden:** AGENTS.md'nin /Core /Repositories /Services /Controllers yapısını tamamlamak.

**Ön koşul:** K03 tamamlanmış.

**Dokunulacak alanlar:** DevCoreBlog.Core kaynakları; /Core; root csproj; solution; README.

**Agent'ın uygulayacağı sıra:**

1. Entity/interface/helper kaynaklarını /Core içine taşı; gereken mevcut paket referanslarını root'ta doğrula. F26 Markdown bağımlılığını mantıksal Core'dan daha sonra çıkaracak.
2. Son Core ProjectReference/solution kaydını ve gereksiz glob exclusions'ını kaldır; artık kaynak içermeyen eski proje dosyalarını kontrollü temizle. Kullanıcının izlenmeyen dosyasını topluca silme.
3. Seçilen nihai tek proje yerleşimini KARARLAR'a ve dosya yolu eşlemesine yaz; bütün sonraki kartlar yeni yolu kullansın.
4. Clean test kopyasında root build ve temel auth/CRUD/read testlerini çalıştır; birden çok DbContext/entity derlenmediğini kontrol et.

**Kabul ve kanıt:** Tek uygulama csproj'si; katmanlar doğru klasörlerde; public/admin işlevleri korunmuş; F10'a hazır. Kod değiştiyse derleme sıfır hatayla tamamlanmalı; ilgili eski kontroller korunmalı.

**Bu fazın dışında:** Namespace kozmetiği için toplu rename ve SOLID puanını otomatik artırma yok.

**Geri dönüş:** Bu taşıma diff'i birlikte geri alınır; D01=B tamamlandı iddiası geri çekilir.

**İzlenebilirlik:** D01=B; .agents/AGENTS.md tek proje.

**Durma noktası:** Kanıtı uygulama kaydına ve DURUM.md'ye işle; sonraki fazı başlatma.

