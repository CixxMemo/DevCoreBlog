# F37 başlangıç bulguları

Temiz başlangıç HEAD 27625bf (F36 tamamlandı).

- _Layout sidebar translate-x-full ile görsel olarak kapalı; kapalı bağlantılarda inert/aria-hidden yok.
- Açma düğmesinde aria-expanded/controls yok; openSidebar yalnız transform, backdrop ve body class değiştiriyor.
- Escape kapatıyor ama odak geri dönmüyor; ilk odak/focus trap/resize temizliği yok.
- Arama ve Ctrl+K yalnız Home/Index içinde; label/submit düğmesi yok.
- Skip link, main id/tabindex ve nav aria-current yok.

Bu bulgular HEAD kaynakları ve F36 tarayıcı kaydıyla doğrulandı; F37 browser.json son davranış gözlemlerini içerir.
