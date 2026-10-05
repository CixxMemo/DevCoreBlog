# F56 — Yedek, geri yükleme ve yayınlama prosedürü

Bu belge F50’nin onaylı tek Linux sunucu + Nginx + tek loopback Kestrel hedefini
esas alır. Sağlayıcı, gerçek PostgreSQL sunucusu, domain/TLS, şifreli depolama ve
backup servisi henüz seçilip doğrulanmadı. Aşağıdaki canlı adımlar uygulanmadı;
yalnız sentetik yerel PostgreSQL restore denemesi kanıtlandı.

## Yedek kapsamı

| Bileşen | Kapsam ve sınır |
|---|---|
| PostgreSQL | Posts/Categories bütün alanlar, UTC yayın/güncelleme tarihleri, slug/aktiflik/yayın izinleri, viewcount/EditVersion, WebhookReceipts kalıcı cevapları, sequences, indexes/constraints ve __EFMigrationsHistory. DB verisi hassastır; dump repo/rapor/wwwroot’a konmaz. |
| Medya referansı | ThumbnailUrl/PublicId/width/height/alt DB ile gelir; Markdown içindeki linkler Content içinde kalır. URL/PublicId bulunması dosyanın Cloudinary’de mevcut olduğunu kanıtlamaz. |
| Medya dosyası | Cloudinary nesnelerinin/orijinallerinin ayrı sağlayıcı yedeği veya doğrulanmış export’u gerekir. DB dump görüntü byte’larını içermez. Başka HTTPS/YouTube/legacy kaynaklar üzerindeki kontrol varsayılmaz; eksik medya ayrıca kaydedilir. Canlı Cloudinary’ye erişilmedi, export özelliği/ücreti varsayılmadı. |
| Release | Önceki ve yeni publish paketi, release SHA/digest, SDK/runtime/paket/lockfile, migration listesi ve apply SQL’i. src/vendor lisansları korunur. |
| Secret dışı config | Doğrulanmış SITE_URL/SITE_TIME_ZONE, DEPLOYMENT_PROFILE, listener/host/proxy ve servis ayarlarının sürümü. Nginx/systemd şablonu canlı config kanıtı değildir. Raporlara credential veya özel bağlantı string’i girmez. |
| Secret/anahtar | EnvironmentFile, DB/Cloudinary/webhook/admin credential ve Data Protection key ring ayrı şifreli, erişimi dar kasada yedeklenir. Değerleri repo/log/manifest’e yazılmaz. Key ring normal rollback’te silinmez; DB dump bunun yedeği değildir. |
| PostgreSQL global nesneleri | Test dump --no-owner/--no-acl kullanır; cluster roles/grants/tablespaces ayrıca host’a göre hazırlanır. Test superuser/trust auth production örneği değildir. Rollerin parola hash’leri rapora konmaz. |

Yedek seti için UTC alınma zamanı, release kimliği, son migration, DB/server/client
sürümü, dosya boyutu/checksum ve geri yükleme denemesi sonucu kaydedilir. Checksum
bütünlük karşılaştırmasıdır; saldırganın dump ile checksum’u birlikte değiştirmesine
karşı kimlik doğrulama sağlamaz. Yalnız güvenilir kaynaktan alınan dump restore
edilir: PostgreSQL restore kaynak SQL’ini çalıştırır. Dosya/dizin izinleri en az
600/700 ve ayrı şifreli/offsite kopya gerekir; gerçek şifreleme/ACL/retention
kurulumu bu fazda doğrulanmadı. Yedekler repo/servisin public dosya alanından uzakta
tutulur. Her release öncesi yedek zorunludur; periyodik sıklık/retention/RPO/RTO
host ve işletim ihtiyacıyla ayrıca belirlenir. Küçük fixture süresi production RTO
veya felaket kurtarma SLA’sı değildir.

## Kanıtlı test komutu ve güvenlik sınırı

```sh
DEVCORE_F49_PROBE=1 DEVCORE_F30_PROBE=1 DEVCORE_F56_PROBE=1 \
DEVCORE_F56_REPORT=/tmp/devcore-restore-report.json \
DEVCORE_F17_PG_PORT=55460 DEVCORE_F17_APP_PORT=15198 \
sh scripts/verification/run_f17_visibility.sh
```

Prerequisites F55 kalite belgesine ek pg_dump/pg_restore’dur; bu testte bütün
PostgreSQL araçları16.14’tür. Test kaynağı ayrı /tmp cluster’a kopyalanır; gerçek
.env/cookie alınmaz. Source snapshot ve backup/restore sonrası tam row JSON,
sequences ve migration listesi bellekte karşılaştırılır; ham içerik/dump report’a
aktarılmaz. Sentetik UTC/Unicode/kapak bilgisi ve kalıcı webhook cevabı dahil edilir.

`scripts/operations/test_database_archive.py` bir **test aracı**dır: yalnız
/tmp veya /private/tmp altındaki devcoreblog-f17.* dizinindeki gerçek data_directory
eşleşen cluster’a izin verir. Loopback port/DB adı açık, PGPASSFILE=/dev/null’dır.
Backup custom archive; restore yeni f56_* veritabanına checksum/list doğrulaması
sonrası --single-transaction/--exit-on-error ile yapılır. --clean/--create/drop
kullanılmaz. Kaynağa, mevcut hedefe, production adına, dış archive yoluna ve bozuk
arşive restore reddedilir. Production bağlantısı kabul etmesi için bu guard’lar
kaldırılmaz; canlı kurtarma ayrı doğrulanmış hedef/yetki planıyla yapılır.

## Staging yayınlama sırası

1. **Build ve gate:** F55 kalite kapısını boş rapor dizininde çalıştır. Yeni
   dependency bulgusu/erişim/restore/build/test hatasında dur. Kabul edilen dar
   braces build-time riski D28 ve review tarihiyle görünür kalır. Release publish
   paketini oluştur, hash’le; .env/cookie/key/node_modules paket dışında olsun.
2. **Host ön koşulları:** Gerçek Nginx config/listener/TLS/domain, app hesabı,
   systemd service/EnvironmentFile ve kalıcı şifreli key ring’i doğrula. nginx -t,
   systemd-analyze verify, journal ACL/retention ve public host/forwarding kabulünü
   kaydet. Bunlar macOS fixture’da çalışmış sayılmaz. Staging DB ayrı olsun; public
   trafik veya webhook/otomasyon staging’e yazmasın.
3. **Backup:** Mutasyonları ve haricî webhook/upload otomasyonunu durdur; DB ile
   medya referans/export setini aynı bakım aralığına bağla. Onaylı hedefin DB ve
   medya/secret/keyring/release yedeklerini erişimi dar yerde al. Yeni boş izole
   DB’ye restore ve içerik/referans/migration kıyasını kanıtlamadan devam etme.
4. **Migration:** Mevcut __EFMigrationsHistory ile yeni release listesini karşılaştır.
   EF tool manifest ve Web migration assembly kullan; hedef connection’ı özel
   config ile doğrula, secret’ı CLI/log’a basma. İdempotent SQL’i önceden üretip
   incele; pending model change/kilit/timeout/transaction-suppressed işlemleri
   değerlendirmeden apply etme. Uygulama startup’ını otomatik migration motoru sayma.
5. **Smoke:** Trafik kapalıyken yeni release’i tek süreçte staging config ile başlat.
   Public görünür/gizli yazı/UTC zaman, SEO/RSS, auth/CSRF ve private dashboard gerçek
   DB Verified sonucunu kontrol et; Cloudinary için gerçek yetkili test ayrı kanıt
   ister. Cookie/key ring restart davranışı ve proxy başlıkları staging üzerinden
   doğrulansın. Redirect zinciri/yanlış origin/503/500/login/upload hatasında dur.
6. **Trafik açma:** Ancak migration, backup/restore, gerçek host ve smoke kanıtı
   tam olduğunda canlı release/symlink/service geçişini somutlaştırıp kullanıcıdan
   o canlı işlem için yetki al. F56 onayı canlı deploy yetkisi değildir. Tek
   uygulama sürecini koru; DB/medya yazılarını ve otomasyonu kontrollü yeniden aç.
   Yeni oturum/cache/limiter süreç sınırlarını dikkate al, güvenli logları gözle.

Bu release’te yeni migration veya C# uygulama değişimi yoktur. Test eski HEAD
uygulama kodunu ayrı binary olarak yeniden derledi; restored current schema’da
admin dashboard, kapak/Unicode alanları ve public HEAD görünürlük kontrolünden
geçirdi. Bu önceki kod snapshot’ı ile aynı şema için kanıttır; gelecekteki her eski
binary’nin yeni şemayla uyumlu olduğu anlamına gelmez. Yeni migration/release’te
uyumluluk tekrar kanıtlanır. Publish kabulü current test kopyasının sentetik medya
adapter’lı paketidir; canlıya taşınacak release değildir.

## Başarısızlık ve geri dönüş

| Durum | İşlem ve devam şartı |
|---|---|
| Backup/restore/checksum başarısız | Trafik kapalı kalır; eksik dump’ı geçerli yedek sayma. Kaynak DB/önceki release’e dokunma; güvenilir yeni yedekle tekrar kanıtla. |
| Transactional migration başarısız | Sonuç/şema/history incelenir; testte transaction rollback veri/kolonu tamamen geri aldı. Her migration’ın atomic olduğu varsayılmaz. |
| Transaction dışında/partial apply | Trafik/yazılar açılmaz. Exact schema/history envanteri çıkar; incelenmiş forward fix veya **ayrı yetkili** restore planı hazırla. Otomatik down migration yoktur. |
| Smoke/DB/media/host sağlığı başarısız | Trafik açılmaz. Secret göstermeyen TraceId/logla teşhis et. Current schema üzerinde test edilmiş önceki güvenli binary/config geri alınabilir; key ring korunur. Uyumsuz eski binary’ye dönülmez. |
| Trafik açıldıktan sonra veri yazıldı | Eski dump’a dönmek yeni yazıları kaybettirebilir; önce mevcut veri/yazı envanteri ve ek yedek, sonra kullanıcı onaylı kurtarma. DB/medya tarihlerini birlikte ele al. |

Başarı/başarısızlık 200 veya zoraki health etiketiyle gizlenmez. Public sağlık API’si
beklenmez; yetkili dashboard ölçümü, gerçek HTTP smoke ve migration/restore kıyası
kullanılır. Eski güvenlik sınırlarını geri getiren F50 öncesi binary/config otomatik
rollback hedefi değildir. Gerçek production restore, Cloudinary export/delete,
uzak dosya silme, systemd/Nginx değişimi ve canlı yayın bu fazda yapılmadı.

Kaynaklar: [PostgreSQL16 pg_dump](https://www.postgresql.org/docs/16/app-pgdump.html),
[pg_restore](https://www.postgresql.org/docs/16/app-pgrestore.html),
[EF migration apply](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
