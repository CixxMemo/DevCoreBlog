# Portföy beslemesi sözleşmesi

F29 · 30 Eylül 2026. Mevcut endpoint `GET /api/public/posts/latest` ve JSON array
biçimi korunur; yeni API sürümü veya portföy uygulaması değişikliği yoktur.

## Kayıt ve alan sınırı

En çok üç yazı döner. F17'nin ortak görünürlük kuralı uygulanır: aktif yazı,
yayın izni, PublishDate ≤ UTC şimdi ve aktif kategori. Sıralama
`PublishDate DESC, Id ASC` ile kararlıdır. Filtre, sıralama, LIMIT 3 ve yalnız
gereken kolonların seçimi PostgreSQL'de çalışır. Tam Markdown içerik, entity
navigation grafiği, sayaç ve yönetim alanları okunmaz veya JSON'a eklenmez.

| JSON alanı | İçerik |
|---|---|
| id | Yazı ID'si |
| title | Başlık |
| slug | Kayıtlı slug |
| summary | Özet |
| excerpt | Alıntı |
| coverImageUrl | Kapak URL'si |
| publishDate | UTC yayın tarihi |
| url | Yapılandırılmış origin + mevcut post-en rotası |
| categoryName | Aktif kategori adı |

Tarih ve alan adları önceki HTTP sözleşmesiyle aynıdır. Kayıt yoksa `[]` döner.
`CancellationToken` controller → service → repository sorgusuna aktarılır.

## Site URL ayarı

Production'da `SITE_URL` zorunludur ve mutlak HTTPS origin olmalıdır:
`https://your-blog.example`. Credential, path (kök `/` dışında), query, fragment,
wildcard host ve kontrol karakteri kabul edilmez. Geçersiz/eksik production ayarı
başlatmayı durdurur; hata metni ayar değerini yazmaz. Development'da yalnız
loopback HTTP origin de kabul edilir; ayar yoksa `http://localhost:5000` kullanılır.
Gerçek domain bu belgede tahmin edilmez. `.env.example` güvenli placeholder verir.

URL, F43 ortak `PublicUrlBuilder` üzerinden `LinkGenerator` ve mevcut `post-en`
rotasından üretilir; istek Host, Scheme ve X-Forwarded-* değerleri mutlak link
kaynağı değildir. Aynı doğrulanmış origin mevcut OG URL ve sitemap loc alanlarına
da uygulanır. Yazının kanonik yolu `/post/{slug}` olur; kayıtlı slug değişmez.
Feed JSON alanları ve en çok üç kayıt sınırı korunur. Yeni meta/canonical etiketi
F44; sitemap XML encoding/lastmod ve robots düzeltmesi F45 kapsamındadır.

## CORS, erişim, hız ve cache

`PORTFOLIO_CORS_ORIGIN` virgülle ayrılmış tam HTTP/HTTPS origin listesidir. Path,
credential, query/fragment ve wildcard kabul edilmez; sondaki kök `/` normalize
edilir. Eksik development ayarı localhost:3000 ve localhost:5173'e izin verir.
Eksik/boş production ayarı ek browser origin izni vermez. Açık boş ayar bütün
ortamlarda ek origin iznini kapatır. UI aynı doğrulanmış listeyi gösterir.

Bu endpoint public'tir. CORS **kimlik veya erişim yetkisi değildir**; listede
olmayan origin'in tarayıcı JavaScript'i yanıtı okuyamaz, native HTTP istemcileri
public veriyi yine alabilir. İzinli origin için GET preflight desteklenir; POST
izni verilmez. Credential/cookie paylaşımı açılmaz.

Mevcut doğrudan bağlantı IP'si başına 30 istek/dakika, kuyruk 0 ve 429/Retry-After
korunur. Sahte forwarding header ile limiter kimliği değiştirilmez. Reverse proxy
kurulumu F50 kapsamındadır.

Feed `no-store` kalır, output cache politikası bağlanmaz. Böylece mutation,
kategori pasifleştirme ve zamanlı yayın sonrası eski görünürlük saklanmaz.
Bu F18'in doğruluk politikasına uygundur; yeni cache key/invalidasyon mekanizması yoktur.

## Gerçek ortam ve geri dönüş

Şema/migration değişikliği yoktur. Production'a alınırken gerçek `SITE_URL` ve
portföy origin'i sunucunun environment'ında ayarlanmalıdır. Bu faz gerçek
.env/hosting/portföy istemcisini değiştirmez ve deploy yapmaz.
Geri dönüşte alan/rota uyumu, yayın filtresi ve kayıt sınırı korunmalı;
Host kaynaklı URL veya bütün yazıları dışarı verme geri getirilmemelidir.

Dayanak: [EF Core dar projection ve sonuç sınırı](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying),
[ASP.NET Core 10 CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0).
