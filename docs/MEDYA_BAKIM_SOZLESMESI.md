# Kapak bilgisi ve salt okunur medya bakımı

F49 kapsamı; [D23](GELISTIRME_PLANI_2026-09-21/KARARLAR.md#d23--kapak-kimliği-açıklama-ve-salt-okunur-bakım) uygulanır.

Yeni kapağın URL, PublicId ve gerçek width/height bilgileri yalnız doğrulanmış Cloudinary upload sonucundan alınır. PublicId en çok 255 karakter; boyutlar 1–4096; URL credential'sız HTTPS ve en çok 2048 karakterdir. Post formu yalnız isteğe bağlı `ThumbnailAlt` açıklamasını kabul eder (trim, en çok 300 karakter); kimlik/boyut/URL formdan alınmaz. Boş açıklama dekoratif görseldir. Detay ve sunucu önizlemesi Razor encoding kullanır; başlıktan açıklama türetilmez. Liste kartları bağlantı başlığını tekrar etmemek için dekoratif kalır. Açıklama değişimi gerçek içerik güncellemesidir; UpdatedDate sunucu UTC saatini kullanır. HTML'deki 1600×900 mevcut kırpma oranıdır; gerçek provider boyutlarıyla karıştırılmaz.

Eski URL'ler korunur; yeni dört nullable kolon eski kayıtlarda null kalır. URL'den PublicId tahmin edilmez. Bu faz ayrı medya kütüphanesi veya inline upload'lar için kalıcı varlık tablosu kurmaz. Inline upload HTTP JSON sözleşmesi aynı `success/url/message` alanlarını taşır.

## Sahiplik ve geri dönüş

| Olay | Kalıcı yazı | Uzak varlık / yeniden deneme |
|---|---|---|
| Validation veya erken edit çakışması | Değişmez; upload yapılmaz | Yeni varlık oluşmaz |
| Upload başarısız veya sonucu geçersiz | Eski kapak ve metadata korunur | Sağlayıcı isteği kabul edip yanıtı kaybolmuş olabilir; varlık oluşmadığını varsayma |
| Upload başarılı, DB save başarısız veya geç çakışma | DB'de eski kapak kalır; geç 409 formu kayıtlı eski kapağı gösterir | Yeni upload provider'da kalır; bakım incelemesine adaydır, otomatik silinmez |
| Upload ve DB save başarılı | Yeni URL/metadata aynı EF kayıt işleminde saklanır | Önceki kapak korunur; başka yazı/Markdown referansı olabilir |
| Dosya seçilmeden normal edit | Kayıtlı URL ve metadata korunur; açıklama düzenlenebilir | Provider işlemi yapılmaz |
| Yazı silme veya kod rollback | Post ilişkisinin durumu değişebilir | Cloudinary dosyası silinmez |

DB commit sonrası cache/yanıt arızası commit'i geri almaz. Başarı yanıtı alınmadıysa kayıt yeniden okunmalıdır. DB arızası gerçek 500, sürüm çakışması 409 kalır; hatalar başarı gibi sunulmaz. F31 kurtarma yalnız metinleri saklar ve doğrulanmış başarılı kayıtla temizlenir; dosyayı veya provider metadata'sını saklamaz. Hatalı kayıttan sonra yeni dosya tekrar seçilir; önceki başarılı upload'u otomatik benimseme/silme yoktur. Eski revision-1 yerel kopya açıklamasızsa mevcut server baseline açıklamasını koruyarak açık Restore seçeneğiyle açılır.

## Dry-run envanteri

`tools/DevCoreBlog.MediaInventoryTool` yerel operatör aracıdır; HTTP endpoint veya Cloudinary admin/deletion API'si değildir. Gerçek provider'dan operatörün doğruladığı bir export gerekir. Bu faz gerçek hesabın export'unu almadı; kabul kontrolleri sentetik export ile çalıştı. Export güncelliği, aynı hesap/uygulama sahipliği ve URL alias'larının doğruluğu operatör tarafından kontrol edilir; JSON'a secret eklenmez.

```json
[
  { "publicId": "DevCoreBlog/example", "urls": ["https://res.cloudinary.com/example/image/upload/v1/DevCoreBlog/example.png"] }
]
```

En çok 4 MiB giriş, 1000 farklı PublicId, varlık başına 1–10 credential'sız HTTPS alias kabul edilir. Dönüştürülmüş/eski URL'leri **export'taki doğrulanmış kimliğe açık alias olarak** ekle; URL'den kimlik çıkarmak yasaktır. Bağlantıyı repo dışında environment üzerinden sağla; tercihen yalnız `Posts` SELECT izni olan ayrı bakım hesabı kullan. Aracın kendisi de repeatable-read transaction içinde `SET TRANSACTION READ ONLY` uygular; migration/update/ensure-created çağırmaz.

```sh
dotnet build tools/DevCoreBlog.MediaInventoryTool --disable-build-servers
# MEDIA_INVENTORY_CONNECTION repo dışında güvenli şekilde ayarlanmış olmalı.
dotnet run --no-build --project tools/DevCoreBlog.MediaInventoryTool -- provider-inventory.json > media-dry-run.json
```

Bütün yazılar (taslak, future, pasif ve pasif kategori dahil) no-tracking dar projection ile ID sırasıyla stream edilir. Kapak PublicId/URL ve Content/Summary/Excerpt incelenir. URL eşleşmeleri HTML entity ve percent encoding sunumlarını çözer; gerçek kimlik üretmez. Kimlik metninin tek başına geçmesi yalnız olası referanstır. Her varlık için referans sayısı ve ilk 20 post ID örneği verilir; makale gövdesi/DB exception/bağlantı secret'ı çıktı olmaz.

- `Referenced`: Kayıtlı kapak kimliği veya açık URL alias'ı bulunmuştur; koru. Metin araması kasıtlı olarak temkinlidir, aynı prefix'in geçmesi de koruma sonucuna yol açabilir.
- `Review required`: Yalnız kimlik metni bulunmuştur; dönüşümlü URL veya düz metin olabilir, manuel incele.
- `No stored reference found`: Bu DB snapshot'ında verilen kimlik/alias'lar bulunamadı; **silme güvenliği veya silme yetkisi değildir**.

Kaydedilmemiş editör/yerel kurtarma kopyaları, harici siteler/uygulamalar, envanterde eksik alias'lar, legacy bilinmeyen kimlikler ve snapshot sonrası upload/save işlemleri kapsam dışıdır. Sağlayıcı yanıtı kaybolan upload ancak yeni güncel export'ta görülebilir. Bakım sırasında yeni kayıtların gelmesi yarış yaratır; herhangi bir silmeden önce somut liste, gerçek sahiplik, bütün referanslar, devam eden işler ve geri dönüş ayrıca değerlendirilip kullanıcıdan açık onay alınmalıdır. Araç silme komutu üretmez ve uzak varlıklara hiç bağlanmaz; eksik/hatalı giriş veya DB arızası exit 1/2 ile tamamlanmış rapor üretmeden biter.

Nullable kolonları bırakarak önceki kod sürümüne dönülebilir; eski kodla yapılan kapak değişimlerinden sonra metadata güncelliği ayrıca gözden geçirilir. Down migration açıklama/metadata'yı düşürür; varsayılan rollback değildir. Canlı migration ve uzak varlık silme bu fazda yapılmadı.
