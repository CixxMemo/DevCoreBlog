# Salt okunur veri geçiş envanteri

`tools/DevCoreBlog.TransitionInventoryTool` mevcut PostgreSQL veritabanından yalnız
toplu geçiş bilgisi üretir. MVC host'unu başlatmaz; migration, seed, medya çağrısı
veya veri değişikliği yapmaz. Uygulama katmanına eklenmez; Data proje referansı
mevcut Npgsql paketini kullanır. Yeni NuGet bağımlılığı yoktur.

## Çalıştırma

Repository kökünde:

```sh
dotnet build tools/DevCoreBlog.TransitionInventoryTool/DevCoreBlog.TransitionInventoryTool.csproj
dotnet tools/DevCoreBlog.TransitionInventoryTool/bin/Debug/net10.0/DevCoreBlog.TransitionInventoryTool.dll
```

Bağlantı yalnız `TRANSITION_INVENTORY_CONNECTION` environment değişkeninden okunur.
Araç `.env` okumaz ve komut satırı argümanı kabul etmez. Yetkili operatör bağlantıyı
güvenli süreç ortamıyla sağlar; credential komuta, geçmişe veya rapora yazılmaz.
Rapor stdout'ta JSON'dur. `verified` ve exit code 0 tamamlanmış snapshot;
`unverified` ve exit code 1 eksik doğrulamadır. Sonraki bölüm başarısız olursa
önceki doğrulanmış bölümler korunur; eksik alanlar sıfır kayıt anlamına gelmez.

## Sınırlar

- Yeni bağlantıda default read-only ve RepeatableRead transaction içinde ayrıca
  `SET TRANSACTION READ ONLY` uygulanır; iki read-only ayarı sunucudan doğrulanır.
  Bu, hesabın yetkilerini değiştirmez. Tercih edilen operatör hesabı yalnız gerekli
  SELECT yetkilerine sahiptir; rapor superuser/create-database yetkisini görünür kılar.
- Connect timeout 5 saniye, command/statement timeout 10 saniye, lock timeout
  1 saniye, cancellation timeout 1 saniye ve toplam cancellation deadline
  30 saniyedir. Rapor başına bölüm en çok 65.536 karakterdir. İçerik analizi
  öncesinde en çok 50.000 yazı ve 67.108.864 UTF-8 içerik byte sınırı uygulanır.
  İlk toplam sorgusu da statement deadline'a tabidir.
- Sabit SQL sorguları kullanılır. Yalnız bilinen iki kapak metadata ifadesi,
  sunucuda bulunan izinli kolonlara göre seçilir; dış SQL input'u yoktur.
- Rapor başlık, slug değeri, gövde, medya URL/PublicId, receipt anahtarı, bağlantı
  adresi, kullanıcı adı veya parola içermez. Hedef fingerprint'i bağlantının
  host/port/veritabanı/kullanıcı bileşenlerinin SHA-256 özetidir; parola dahil değildir.
  Fingerprint anonimleştirme garantisi sayılmaz; envanter raporu özel yerel alanda saklanır.
- PostgreSQL sürümü, collation/ctype, encoding, UTC snapshot zamanı, şema ve slug
  unique index durumu, sayımlar, dört literal Türkçe ILIKE gözlemi ve migration
  düzeyi raporlanır. ILIKE gözlemleri tam Türkçe arama doğrulaması değildir.

Read-only davranışın kapsamı [PostgreSQL transaction belgesinde](https://www.postgresql.org/docs/current/sql-set-transaction.html),
bağlantı ve timeout ayarları [Npgsql bağlantı belgesinde](https://www.npgsql.org/doc/connection-string-parameters.html)
açıklanır. Süre sınırları sınırsız taramayı başarılı boş sonuç gibi göstermez.

## Sonucun anlamı

`Content` mevcut uygulamanın Markdown metin sözleşmesidir. İlk karakteri JSON'a
benzeyen içerik ve Markdown/HTML görsel örüntüsü yalnız aday sayımıdır; tam belge
parser'ı veya tüm olası medya referanslarının eksiksiz envanteri değildir.
Draft/Scheduled/Published/Inactive mevcut kategori ve yayın durumuna göre ayrılır.
Kapak URL doluluğu sağlayıcı varlığının erişilebilir olduğunu kanıtlamaz.
PublicId veya boyut kolonu bulunmuyorsa ilgili sonuç `null` olur.

Gerçek/sentetik içerik sınıflaması sahibinden ayrıca alınır. Sıfır webhook receipt
aktif tüketici yokluğu kanıtı değildir; webhook ve portföy kullanımı operatör/kullanıcı
envanteriyle doğrulanır. Sağlayıcı export'u ayrı kaynaktır: export yoksa uzak varlık
eşleşmesi doğrulanmamıştır. Bu rapor silme, dönüşüm, canlı restore/deploy veya
uzak medya temizleme yetkisi vermez. Başka hedefe geçildiğinde ve veri dönüşümünden
önce envanter yeniden alınır.

## Sentetik doğrulama

Tool derlendikten sonra, mevcut PostgreSQL araçlarıyla yalnız yeni disposable
cluster üzerinde:

```sh
python3 scripts/verification/transition_inventory_probe.py --report-dir /tmp/devcoreblog-inventory-report
```

Rapor dizini önceden bulunmamalıdır. Probe gerçek bağlantı veya `.env` yüklemez;
cluster'ı sonunda kapatıp temizler. Boş DB/bağlantı hatası ayrımı, şema/metadata,
slug çakışması, yayın/medya sayımları, UTC, read-only yazma reddi, satır/içerik
koruma, kapasite/timeout, secret sızıntısı ve çalıştırılan SQL'de write bulunmaması
kontrol edilir. Yazma reddi denemesi yalnız probe'un sahip olduğu sentetik DB'dedir.
