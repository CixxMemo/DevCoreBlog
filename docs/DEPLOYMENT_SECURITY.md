# DevCoreBlog dağıtım güvenliği

Bu belge F08 kapsamında yönetici cookie'si ve ASP.NET Core Data Protection anahtarları için dağıtım sözleşmesini tanımlar. Hosting ve reverse proxy ayrıntıları F50'de gerçek platforma göre tamamlanacaktır.

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

Dosya sistemi konumu açıkça seçildiğinde ASP.NET Core anahtarları kendiliğinden dosya düzeyinde şifrelemez. Bu nedenle F08 sözleşmesi şifreli kalıcı volume ve uygulama hesabına özel izinleri birlikte zorunlu kılar. Platforma özgü Key Vault, KMS veya sertifika ile anahtar şifreleme kararı gerçek hosting topolojisi bilindiğinde F50'de verilecektir.

Yerel Development ortamında `DATA_PROTECTION_KEYS_PATH` boş bırakılabilir. Framework'ün geliştirici hesabına ait varsayılan anahtar deposu kullanılır. İzole testler ise repository veya kullanıcı anahtarlarına dokunmayan geçici, `700` izinli bir dizin kullanır.

Kaynaklar: [ASP.NET Core Data Protection yapılandırması](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [Data Protection anahtar depoları](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0), [ASP.NET Core SameSite cookie davranışı](https://learn.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-10.0).
