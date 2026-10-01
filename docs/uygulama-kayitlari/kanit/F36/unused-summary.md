# Kullanılmayan kaynak kontrolü

Başlangıçta Views/JS/CSS kullanım araması: unused-before.log. Ana sayfa, kategori ve arama DOM'unda carousel ve legacy stil hedefleri sıfırdı.

- site.js yalnız carousel başlangıcını içeriyordu; hero-carousel-track, carousel-prev/next ve cyber-carousel hedefleri Razor'da yoktu. İki layout çağrısı kaldırıldı.
- _Layout.cshtml.css Bootstrap dönemi navbar-brand/btn-primary/nav-pills/footer stilleriydi; layout'lar DevCoreBlog.styles paketini yüklemiyordu. View hedefleri yoktu.
- table-admin, form-control textarea, post-content/blog-body alias'ları ve animate-fade-up kullanım hedefleri bulunmadı. Aktif markdown-content sınırı korundu.
- Son arama unused-after.log boş: kaldırılan hedefler için kaynak eşleşmesi yok (rg exit 1 normal eşleşme yok sonucu).
- site.css !important sayısı 20 → 1. Kalan radius kuralı yalnız mevcut admin davranışına sınırlandı; public kök 16px, admin mobil kök 14px olarak tarayıcıda ölçüldü.
- Prism CSS sonrası uygulama CSS'i yükleniyor; token renklendirme ve toolbar sırası regresyon kontrolünde korunuyor.
