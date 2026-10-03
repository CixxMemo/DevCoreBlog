# F47 — Veri kullanımı envanteri ve değerlendirme taslağı

Bu belge kaynak koduna dayanan taslaktır; kamuya yayımlanmış politika veya hukuki uygunluk garantisi değildir. Hosting, log saklama süresi ve sağlayıcı sözleşmeleri kaynakta doğrulanmadığından kullanıcı değerlendirmesi gerekir. Yeni veri toplama eklenmedi.

## Doğrulanan mevcut davranış

| Kullanım | Gerçek kaynak / sınır |
|---|---|
| Yönetici oturumu | `Program.cs`, `Security/AdminSessionPolicy.cs`: gerekli authentication cookie; süre environment ile belirlenir, varsayılan 30 dakika. Antiforgery MVC form güvenliğidir. |
| Güvenlik işlemleri | `Program.cs`: login/webhook/portföy rate limit anahtarında doğrudan bağlantı IP'si; login limit aşımında IP uyarı loguna yazılır. `Middlewares/ExceptionHandlingMiddleware.cs`: hata korelasyon ID'si ve exception türü. Saklama süresi uygulamada tanımlı değil. |
| Tarayıcıda yerel tercih | Public layout tema tercihini localStorage içinde tutar. Yönetici editörü taslak kurtarma içeriğini aynı tarayıcının localStorage alanında tutar; başarılı sunucu kaydı doğrulanınca temizlenir. |
| Dış kaynak yüklemeleri | Public/admin layout Google Fonts kullanır. Yayımlanan görsellerin adresi uzak sunucuya istek yapabilir; yönetici medya yüklemesi mevcut Cloudinary sağlayıcısına gider. İlgili içerikte YouTube nocookie embed yüklenebilir. Bu istekler ilgili sağlayıcıya bağlantı bilgilerini iletir. |
| İletişim | Contact sayfası yalnız mailto ve kullanıcı tarafından verilmiş GitHub profil bağlantısı içerir. Sunucuya mesaj kaydeden form yoktur; e-posta uygulaması/sağlayıcısı üzerinden iletişim gerçekleşir. |
| Okunma sayacı | Yazı başına anonim detay GET istek sayısı; tekil kişi/ziyaretçi takibi olarak sunulmaz. |

Kaynakta newsletter aboneliği, ziyaretçi üyeliği, yorum formu veya analytics entegrasyonu bulunmadı. Hosting/proxy erişim logları bu tespitin dışında kalır.

## Kullanıcının değerlendirmesine sunulan İngilizce metin

> DevCoreBlog uses browser storage to remember your theme preference. The administrator's editor also stores recovery drafts locally in the administrator's browser. Administration uses authentication and request verification cookies.
>
> Direct connection IP addresses are used to limit requests to sign-in and integration endpoints. Rejected sign-in attempts may be logged with their IP address. Application errors include a request identifier for diagnosis.
>
> Pages load fonts from Google Fonts and may load images from external hosts. Posts containing video embeds may connect to YouTube's nocookie domain. Administrator image uploads use Cloudinary. These providers receive connection information when their resources are requested.
>
> The Contact page provides an email link and a GitHub profile link. It does not collect messages through a website form. Contacting the owner by email uses your email application and the relevant email providers.
>
> Article view counts measure page requests, not unique people.

## Yayımdan önce tamamlanacak gerçek bilgiler

Gerçek hosting/proxy ve log saklama/silme düzeni F50'de incelenecek. Bu taslağın kamuya yayımlanması, sorumlu kişi ve sağlayıcı/saklama süreçlerinin doğrulanmasıyla ayrıca değerlendirilmelidir. Eksik bilgiler yerine süre, hak, sözleşme veya yasal dayanak uydurulmadı. F47 About/Contact teslimi bu taslağın onaylanmış hukuki politika olduğu anlamına gelmez.
