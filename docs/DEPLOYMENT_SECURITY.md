# DevCoreBlog dağıtım güvenliği

Bu belge F08 oturum/anahtar sınırlarını ve F50’de kullanıcı tarafından onaylanan hedefi tanımlar: tek Linux VPS/sunucu, Nginx ve yalnız IPv4 localhost’a bağlı tek Kestrel süreci. Sağlayıcı, alan adı, sertifika ve canlı sunucu henüz doğrulanmış değildir.

## Yönetici oturumu

- `ADMIN_SESSION_VERSION` gizli değildir. Varsayılanı `1` değeridir. Bu değeri değiştirmek bütün mevcut yönetici oturumlarını geçersiz kılar.
- `ADMIN_PASSWORD_HASH` veya `ADMIN_USERNAME` değiştiğinde de mevcut oturumlar otomatik olarak reddedilir. Cookie içine credential değeri değil, bunlardan türetilmiş tek yönlü bir oturum damgası yazılır.
- `ADMIN_SESSION_LIFETIME_SECONDS` varsayılan olarak `1800` saniyedir ve en fazla `28800` olabilir. Oturum kayarak yenilenmez; kullanıcı etkin olsa bile oluşturulduğu andan itibaren belirlenen sınırda sona erer.
- Yönetici cookie'si tarayıcı kapandığında silinen bir session cookie'dir. Üretimde `Secure`, her ortamda `HttpOnly`, `SameSite=Lax` ve `Path=/` kullanır.

Parola değişikliği dışında toplu oturum kapatma gerekiyorsa `ADMIN_SESSION_VERSION` değerini yeni, farklı ve kararlı bir değerle güncelleyin. Aynı deployment içindeki bütün örnekler aynı değeri kullanmalıdır.

## Data Protection anahtar halkası

Production ve diğer Development dışı ortamlarda `DATA_PROTECTION_KEYS_PATH` zorunludur. Değer şu koşulları sağlamalıdır:

1. Dizin deployment başlamadan önce mevcut olmalı ve mutlak bir yol kullanılmalıdır.
2. Dizin container veya süreç yaşamından bağımsız kalıcı depolamada olmalıdır. Birden fazla uygulama örneği aynı anahtar halkasına erişmelidir.
3. Depolama platform düzeyinde şifreli olmalı; dizine yalnızca DevCoreBlog uygulama hesabı erişmelidir. Unix sistemlerde dizin modu `700` olmalıdır; uygulama bunu Production başlangıcında doğrular.
4. Dizin repository, `wwwroot`, geçici dosya sistemi veya yayın paketinin içine yerleştirilmemelidir.
5. Anahtar dosyaları normal deployment, rollback veya oturum sürümü değişiminde silinmemeli ve sıfırlanmamalıdır. Yedekleme ve geri yükleme erişimi de aynı gizlilik seviyesini korumalıdır.

Uygulama kimliği kodda sabit olarak `DevCoreBlog` değeridir. Aynı cookie'leri okuyacak bütün örnekler aynı uygulama kimliğini, .NET Data Protection sürümünü ve anahtar halkasını kullanmalıdır. Farklı bir uygulama bu dizini paylaşmamalıdır.

Dosya sistemi konumu açıkça seçildiğinde ASP.NET Core anahtarları kendiliğinden dosya düzeyinde şifrelemez. Bu nedenle F08 sözleşmesi şifreli kalıcı volume ve uygulama hesabına özel izinleri birlikte zorunlu kılar. F50 yeni KMS/Key Vault servisi eklemez. Seçilecek sunucuda şifreli kalıcı depolama ve yedek şifrelemesi sağlanmalıdır; gerçek sağlayıcı seçilmediği için bunların varlığı doğrulanmadı.

Yerel Development ortamında `DATA_PROTECTION_KEYS_PATH` boş bırakılabilir. Framework'ün geliştirici hesabına ait varsayılan anahtar deposu kullanılır. İzole testler ise repository veya kullanıcı anahtarlarına dokunmayan geçici, `700` izinli bir dizin kullanır.

Kaynaklar: [ASP.NET Core Data Protection yapılandırması](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [Data Protection anahtar depoları](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0), [ASP.NET Core SameSite cookie davranışı](https://learn.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-10.0).

## F50 — Tek sunucu ve açık proxy güven sınırı

`DEPLOYMENT_PROFILE=nginx-loopback` Development dışındaki bütün ortamlarda zorunludur.
Kestrel tek `http://127.0.0.1:<port>` adresinde dinler; varsayılan port 5000’dir.
`0.0.0.0`, wildcard, localhost alias, HTTPS backend, birden fazla URL ve
`Kestrel:Endpoints` override başlangıçta reddedilir. Backend portu internete açılmaz.
Production `SITE_URL` port 443’te HTTPS DNS origin olmalıdır; localhost/IP origin
kabul edilmez. Bu doğrulama DNS sahipliğini veya sertifikayı doğrulamaz.

Forwarded Headers middleware HTTPS/HSTS, authentication ve rate limiting’den
önce çalışır. Yalnız `127.0.0.1` ve bunun IPv4-mapped karşılığı bilinen proxy’dir;
127/8 ağına veya bütün proxy’lere güvenilmez. En fazla bir hop, eşit sayıda
X-Forwarded-For/Proto değeri kabul edilir. X-Forwarded-Host/Prefix işlenmez.
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` ve eski IgnoreUnknownProxiesWithoutFor
uyumluluk bypass’ı reddedilir. Normal yerel Development forwarding’i kapalı tutar;
yerel proxy fixture için açık `nginx-loopback` profili kullanılabilir.

Host Filtering üretimde yalnız SITE_URL host’unu, Development’ta ayrıca
localhost/127.0.0.1/[::1] host’larını kabul eder. `AllowedHosts=*` gibi dış
configuration bu listeyi genişletemez. Bilinmeyen Host 400’dür. Mutlak public
adresler F43 URL builder’ından gelir. HTTP backend istekleri doğrulanmış host’a
443 portunda 308 ile gider; Nginx HTTPS başlığını doğru aktarınca döngü oluşmaz.
Mevcut HSTS 30 gün sınırı korunur. Yönetici ve antiforgery cookie’leri üretimde
Secure’dür; yönetici HttpOnly/SameSite=Lax, kaymayan süreli session cookie kalır.

Nginx `X-Forwarded-For $remote_addr` ve `X-Forwarded-Proto $scheme` değerlerini
**yeniden yazar**; `$proxy_add_x_forwarded_for` kullanmaz. İstemcinin Forwarded,
X-Forwarded-Host/Prefix değerleri upstream’e gönderilmez. Host `$host` olarak
aktarılır ve uygulamada yeniden doğrulanır. Cache ve error interception kapalıdır;
özel yönetim yanıtı cache’e alınmaz, 4xx/5xx başarıya çevrilmez. CDN, real_ip veya
ek proxy yoktur. Böyle bir ekleme ayrı güven/topoloji değerlendirmesi ister.
Loopback güveni aynı sunucudaki yerel süreçleri de kapsar; işletim sistemi hesabı
ve sunucu erişimi güven sınırının parçasıdır.

Login/webhook/portfolio limiter aynı çözümlenmiş RemoteIpAddress’i kullanır.
Bunlar **süreç başına** sınırlardır; yeniden başlatma sayaçları sıfırlar, birden
fazla süreç ortak toplam sınır sağlamaz. F18 cache invalidation da tek süreçlidir.
Bu düzen tek uygulama süreciyle çalıştırılır. Çok instance/CDN/container ağına
geçişte gerçek ingress, toplam limit ve cache tutarlılığı ayrıca kanıtlanmalıdır;
hayali Redis veya dağıtık limit garantisi eklenmedi.

## Yapılandırma ve dağıtım örnekleri

- [Nginx örneği](../deployment/nginx/devcoreblog.conf.example): gerçek alan adı,
  sertifika dosyaları ve backend portuyla birlikte uyarlanır. Bilinmeyen HTTP
  host’u 404, bilinmeyen TLS SNI handshake reddi alır. Şablon aktif desteklenen,
  SSL modüllü Nginx ister; handshake direktifi en az 1.19.4 gerektirir.
- [systemd örneği](../deployment/systemd/devcoreblog.service.example): tek özel
  uygulama hesabı, mevcut publish dizini ve sunucuda doğrulanmış dotnet yolu.
  Anahtar dizini önceden oluşturulur, uygulama hesabına ait 700 izinli kalıcı
  şifreli depolama kullanır; repository/wwwroot/yayın dizinine konmaz.
- [Servis environment örneği](../deployment/devcoreblog.env.example): gerçek
  değerler repository dışında root-owned 600 izinli EnvironmentFile’da tutulur.
  Production `.env` okumaz; Development `.env` yalnız eksik environment değerini
  doldurur. Hash, cookie, anahtar ve connection string loglanmaz veya kanıta yazılmaz.

Örnek dosyalar kurulum değildir. Gerçek sunucuda mevcut default_server ve global
real_ip/proxy/cache ayarları incelenmeli; alan adı, sertifika zinciri/yenilemesi,
80/443 erişimi, localhost listener, şifreli volume/anahtar izinleri ve servis
hesabı doğrulanmalıdır. Ardından `nginx -t`, `systemd-analyze verify` ve gerçek
Nginx üzerinden spoofed-header/HTTPS/login/upload/429 kabul kontrolleri yapılır.
F50 yerel ortamında Nginx/systemd bulunmadığı için bu host kontrolleri çalışmadı;
uygulama kabulü gerçek Production Kestrel, doğrulanan yerel TLS ve Nginx’in başlık
sözleşmesini taklit eden fixture ile yapıldı. F56 yalnız test restore ve yayınlama prosedürünü kanıtlar; canlı yayın/restore ayrıca açık yetki ister.

Geri dönüşte önceki güvenli binary ve onun uyumlu servis/proxy config’i birlikte
kullanılır; mevcut anahtar halkası korunur. F50 öncesi binary’ye dönmek wildcard
Host ve forwarding eksikliğini geri getirir; bütün proxy’lere güvenmek çözüm değildir.

Kaynaklar: [.NET 10 reverse proxy sınırı](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0),
[Nginx proxy_set_header](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_set_header),
[Nginx TLS/handshake](https://nginx.org/en/docs/http/ngx_http_ssl_module.html#ssl_reject_handshake).

## F53 işletim ve log sınırı

[Operasyon sözleşmesi](OPERASYON_VE_LOG_SOZLESMESI.md) güvenli JSON scope/event, yetkili servis durumu, journal hedefi ve gerçek sunucuda doğrulanacak retention/erişim yollarını tanımlar. Systemd örneğinde stdout/stderr journal’a açıkça yönlendirilir; bu örnek canlı log saklama/rotation kurulumu değildir. Eksik medya credential’ında admin teşhis açık, upload fail-closed kalır; kimlik zorunluluğu korunur.

## F54 — Git takibi ve geçmişteki oturum kayıtları

F54, doğrulanmış 245 root bin/obj çıktısını ve `cookies.txt` kaydını yalnız Git
index’inden çıkardı; yerel dosyalar korunur. Cookie jar localhost’a ait tek,
değeri bulunan session kaydı içeriyordu; kalıcı expiry alanı yoktu. Bu bilgi
oturumun bugün geçerli olduğunu veya üretim hesabına ait olduğunu kanıtlamaz.
Cookie değeri çözülmedi, sunucuya gönderilmedi, rapora/loga yazılmadı.

F08 güncel kimlik bilgileri ve `ADMIN_SESSION_VERSION` damgasını her istekte
kontrol eder; eksik/eski damga reddedilir. Tarihsel cookie’nin iç damgası ve
üretildiği ortam doğrulanmadığından canlı iptal yapıldığı iddia edilmez. Önceden
paylaşılan ortam hâlâ kullanılıyorsa o ortamın operatörü mevcut sürümü farklı
kararlı bir değerle değiştirip uygulamayı yeniden başlatmalı ve eski oturumun
reddini doğrulamalıdır. Bu bütün admin oturumlarını kapatır; gerçek `.env`,
production EnvironmentFile veya key ring F54’te değiştirilmedi. Canlı ortamda
iptal/rotasyon gerektiği değerlendirmesi ve uygulaması ayrı kaydedilir.

Index kaldırması eski commit/blob/clone kopyalarını temizlemez. Cookie geçmişte
kalır; history rewrite ve force-push yapılmadı. Secret geçmişini genel olarak
temizlenmiş saymayın. Gerçek secret tespitinde ilgili credential rotasyonu ayrıca
planlanır; dosyayı yeniden takip etmek geri dönüş yöntemi değildir.

## F56 yedek ve yayınlama sırası

[Yedek/restore/yayın prosedürü](YEDEK_VE_YAYIN_PROSEDURU.md) DB, medya referansı/byte ayrımı, migration/secret/keyring kapsamı, staging geçiş kapıları ve veri kaybettirmeyen geri dönüş sınırını tanımlar. Yerel test restore gerçek host kurulumunu veya Cloudinary yedeğini kanıtlamaz.
