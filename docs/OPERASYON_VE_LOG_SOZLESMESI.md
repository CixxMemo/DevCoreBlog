# F53 — Yönetici servis durumu ve güvenli teşhis

## Durumların anlamı

Yalnız yetkili `/Admin/Dashboard` sunucu tarafında bir kontrol yapar; public health
endpoint’i, arka plan polling’i ve dış izleme hesabı yoktur. Yanıt no-store’dur.

- **Database Verified:** gerçek PostgreSQL bağlantısında SELECT 1 ve mevcut bounded
  dashboard sorguları o istekte başarılı oldu. Sonraki erişimi garanti etmez.
- **Unavailable / Timed out:** metrikler okunamadı; panel gerçek **503** ile açılır,
  rakam tahmin etmez. Diğer HTML/JSON hata yolları kendi gerçek durumunu korur.
- DB için tek 2 saniyelik use-case deadline; scoped Npgsql bağlantısında ayrıca
  Timeout/CommandTimeout 2 saniye ve cancellation grace 500 ms bulunur. Bağlantı
  metric sorgularında yeniden kullanılır; DbContext scope sonunda kapatır. Gerçek
  takılı handshake ve tablo kilidi testleri yaklaşık 2,4/2,0 saniyede sonlandı.
  Bu kontroller bütün DNS/OS/hosting arızaları için mutlak gecikme garantisi değildir.
- **Media Configured — access not checked:** üç environment değeri mevcut; bu bilgi
  sağlayıcı erişimi veya doğru credential kanıtı değildir. Eksik değerlerde
  **Not configured**; SDK istemcisi kurulmaz, geçerli upload da güvenli 503 alır.
  Admin kimliği hâlâ zorunlu/fail-closed’dur. F26’nın eksik medya nedeniyle tüm
  uygulamayı durdurması, F53’te upload işleminin kapalı kalmasına daraltılmıştır.
- **Last upload / Last successful upload:** sadece bu süreçte storage dönüşü ve
  mevcut URL/format/kimlik/boyut sınırı doğrulanmış işlemin UTC gözlemidir. Upload
  başarısı DB save başarısı değildir. Sonraki başarısızlık eski başarı tarihini
  silmez; mevcut erişim “Active/Verified” diye sunulmaz. Restart geçmişi sıfırlar.
- **Webhook Configured / Not configured:** secret varlığı; eksik durumda gelen
  yazma isteği 401’dir. Gerçek istemci bağlantısı/otomasyon çalışması iddia edilmez.

Üretimde zararsız sağlayıcı erişim kontrolü doğrulanmadığı için yeni Cloudinary
Admin API/ping/hesap yetkisi eklenmedi. Mevcut dar upload sözleşmesi korunur.
Fixture upload sonucu gerçek Cloudinary hesabının çalıştığını kanıtlamaz.

## Log sınırı

Yerleşik JSON console tek provider’dır; UTC timestamp ve scope içerir. Middleware
server-assigned TraceIdentifier’i **X-Request-ID** ile döndürür ve aynı **TraceId**
scope’unu auth/upload/webhook/global hata olaylarına taşır. Client’ın gönderdiği
X-Request-ID alınmaz. Framework’ün uygulama öncesi boş Host400’ü bu hattın dışındadır.

Başarılı login/logout Information; reddedilen auth/upload/webhook ve bounded DB
kontrolü Warning; beklenmeyen sunucu hatası Error’dur. Gerçek client cancellation
server crash sayılmaz. Response başladıysa durum kodu yeniden yazılamaz: bağlantı
abort edilir, tamamlanmış başarılı response gibi sunulmaz; exception host’a yeniden
fırlatılıp stack/message sızdırılmaz. Header başlamadan hata gerçek500 kalır.

Uygulama event’leri sabit işlem adı/durum, gerekli exception **türü**, korelasyon
ID’si ve mevcut login güvenliği için proxy sonrasında çözülen IP ile sınırlıdır.
Payload, query string, post body, username/password, token/header/cookie, hash,
medya PublicId/URL ve connection string uygulama event alanı değildir. HTTP body
logging eklenmedi. EF/Npgsql provider logları, daha özel environment logging
seviyesi verilse bile post-configure filtresiyle kapalıdır; aksi halde hata SQL’i
ve provider exception mesajı kullanıcı input’u içerebilir. Destek için güvenli
uygulama özetlerini kullan; bu filtreyi günlük teşhis adına kaldırma.

## Linux işletim hedefi ve henüz doğrulanmayanlar

F50 hedefi tek Linux VPS, systemd, Nginx ve loopback Kestrel’dir. Gerçek sağlayıcı
ve sunucu kurulmadı. [Servis örneği](../deployment/systemd/devcoreblog.service.example)
stdout/stderr’i journal’a, SyslogIdentifier’i devcoreblog’a açıkça yönlendirir.
Uygulama repository/wwwroot altında log dosyası üretmez. Hedefte okuma:

```sh
sudo journalctl -u devcoreblog.service --since '1 hour ago' --no-pager
```

Journal kalıcı depolamada `/var/log/journal`, volatile depolamada `/run/log/journal`
altındadır; gerçek host’un Storage ayarı belirler. Bu yolların canlıda mevcut
olduğu doğrulanmadı. `/etc/systemd/journald.conf` ve drop-in’ler, erişim ACL/grupları,
rotation, yedek/forwarding ve efektif retention F56 staging öncesi okunup kayda
geçmelidir. İşletim hedefi en fazla 30 gün tutmak ve disk kotasını host kapasitesine
göre sınırlamaktır; bu öneri uygulanmış ayar veya hukuki saklama politikası değildir.
Paylaşılan journal retention ayarı diğer servisleri etkileyebileceğinden sistem
ayarları bu fazda değiştirilmedi. Journal okuma yetkisini yalnız gerekli operatöre
ver; genel web/download paylaşımı yapma. Export’u ayrıca redakte et.

Nginx/provider/system logları uygulamanın JSON filtresinden geçmez. Gerçek access/
error log yolları, formatı ve query/header içeriği canlı config’den ayrıca
belirlenmelidir; `/var/log/nginx` varsayılanını canlı kanıt gibi sunma. Secret’ları
URL/query’ye koyma; destek export’unda bağlantı/kimlik/kişisel bilgileri çıkar.
F47 veri kullanımı belgesi taslak kalır. Dış log forwarding/analytics eklenmedi.

## Doğrulama ve geri dönüş

F53 kaydı, gerçek DB/driver/HTTP testlerini,
özel canary log taramasını ve Chrome görüntülerini içerir. Ham log/cookie export’u
yoktur. Şema/veri etkisi yok; durum widget’ı kaldırılabilir ama sahte Active veya
başarısız DB’de sıfır metriklere dönülmez. Güvenli logging ve fail-closed upload
sınırı korunmalıdır.

Kaynaklar: [.NET logging/scopes](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0),
[Npgsql bağlantı parametreleri](https://www.npgsql.org/doc/connection-string-parameters.html),
[systemd service stdout/stderr](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml),
[journald storage/retention](https://github.com/systemd/systemd/blob/main/man/journald.conf.xml).
