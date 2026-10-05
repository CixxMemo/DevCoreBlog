# Webhook gönderimi ve güvenli tekrar sözleşmesi

F27–F28 · 30 Eylül 2026. Endpoint: `POST /api/webhooks/posts`.
Mevcut JSON alan adları ve başarılı `200` yanıtı korunur.

## Kimlik, giriş ve yayın

`Content-Type: application/json` ve özel yapılandırmadan gelen `X-DevCore-Secret`
gereklidir. Secret doğrulanmadan JSON ayrıştırılmaz veya veritabanına erişilmez.
Gövde en çok 2 MiB; hız sınırı varsayılan 5 istek/dakika, kuyruk kapalıdır.
Alan/kategori kuralları ortak içerik servisinde uygulanır.
Varsayılan taslaktır; `isPublished=true` ancak sunucunun
`ALLOW_WEBHOOK_PUBLISH=true` ayarıyla yayın yetkisi verir.

## İstemci geçişi

`Idempotency-Key` geriye uyum için isteğe bağlıdır. **Anahtarsız isteklerin tekrar
koruması yoktur**; her başarılı çağrı yeni yazı oluşturur. Mevcut otomasyonları
geçirmek için dış sistemde her yeni yazıya bir UUID veya benzer benzersiz değer
ata, bu değeri ve payload'ı kalıcı tut ve bütün yeniden denemelerde aynen gönder.
Her denemede yeni anahtar üretme. Bu faz dış otomasyon sistemini değiştirmez.

Anahtar tek header değeridir; 1–128 ASCII harf/rakam, nokta, alt çizgi veya tire
kabul edilir. Boş, çoklu, boşluklu veya aşırı uzun değer `400` üretir. Anahtarlar
büyük/küçük harfe duyarlıdır ve bu endpoint'in bütün istemcileri arasında ortaktır.
Anahtar secret değildir; kişisel bilgi/credential kullanma.

## Aynı anahtarın sonucu

- İlk geçerli gönderim: yazı ve işlem kaydı tek PostgreSQL transaction'ında
  oluşturulur. Veritabanındaki benzersiz anahtar eşzamanlı istekleri de korur.
- Aynı anahtar ve aynı tanımlı payload: ilk başarılı `200` yanıtının alanları
  döner; yeni yazı, yeni sayaç veya yeni işlem kaydı oluşmaz.
  İlk yanıt da transaction içinde DB'den tekrar okunan işlem kaydından üretilir;
  `publishDate` PostgreSQL'in sakladığı mikro saniye hassasiyetini taşır.
- Aynı anahtar ve farklı tanımlı payload: `409`, mevcut yazı değişmez.
- Yazı daha sonra düzenlense/yayın ayarı değişse/silinse de ilk başarılı sonuç
  döner. Bu yanıt güncel yazı durumunu ifade etmez; silinmiş yazıyı yeniden yaratmaz.

Özet SHA-256 ile typed payload'ın sabit alan sırasındaki JSON'undan hesaplanır.
JSON alan sırası ve biçimlendirme boşlukları etkisizdir; bilinmeyen alanlar yok
sayılır. Title, Content, Summary, Excerpt, CoverImageUrl, CategoryId, IsPublished
ve PublishDate değerleri karşılaştırılır. String içindeki boşluklar anlamlıdır;
null ile boş string farklıdır. Atlanan alan ile o alanın tipteki varsayılanı
aynıdır. Tarih typed DateTime JSON temsiliyle karşılaştırılır; yeniden denemede
tarih metnini ve diğer tanımlı değerleri değiştirmeme kuralı kullanılır. Varsayılan
sunucu tarihi ve sunucunun yayın izni özete dahil edilmez. Ham içerik ve secret
tekrar koruma tablosunda saklanmaz; yalnız özet, anahtar, oluşturma zamanı, nullable
post FK'sı ve ilk yanıtın küçük alanları tutulur.

## Saklama, hata ve yeniden deneme

Başarılı anahtarlar **süresiz** saklanır; TTL/otomatik silme yoktur. Post silinirse
FK null olur, ilk post ID'si ve yanıt kaydı korunur. Böylece çok geç gelen tekrar
ve restart da ikinci yazı üretmez. Kayıt birikimi yalnız başarılı, yetkili,
sınırlanmış gönderimlerden gelir. İleride saklama süresi değişimi ayrı veri ve
istemci geçiş kararı gerektirir; tablo boşaltılmaz.

Kimlik, JSON, validation veya transaction hatası yeni anahtarı tüketmez. `400`,
`401`, `413`, `415` için girdiyi/yetkiyi düzelt; `409` için aynı anahtarı farklı
içerikle yeniden kullanma. `429`, geçici `5xx` veya yanıt kaybında artan beklemeyle
aynı anahtar ve aynı payload'ı tekrar gönder. Sunucu commit etmiş ama yanıt
kaybolmuşsa sonraki istek kayıtlı sonucu döndürür. DB hatası commit öncesindeyse
iki kayıt da geri alınır ve aynı anahtarla yeniden denenebilir.

## Dağıtım ve geri dönüş

Web kodundan önce `WebhookReceipts` migration'ı uygulanmalıdır. Bu faz yalnız
boş ve önceki şemalı sentetik PostgreSQL'de migration çalıştırdı; canlı ortam
ayrı operasyonel adımdır. Down migration işlem geçmişini silmemek için reddedilir.
Sorunda webhook'u güvenli biçimde kapat, kayıtları koruyan forward-fix veya
onaylı yedek geri yükleme kullan. Tekrar koruma tablosunu boşaltma.

Dayanak: [EF Core transaction belgeleri](https://learn.microsoft.com/en-us/ef/core/saving/transactions),
[PostgreSQL 16 benzersizlik kontrolü](https://www.postgresql.org/docs/16/index-unique-checks.html).
