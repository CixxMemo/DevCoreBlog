# E01 — Vendored editör güvenlik bakımını tamamla

F57’de saptanan P1 bulgusu için ayrı atomik ek iştir. Kullanıcı 5 Ekim 2026’da
onayladı. F00–F57 ana plan faz sayısını veya ürün kapsamını değiştirmez.

**Ön koşul:** [F57 bulgusu](../uygulama-kayitlari/F57-2026-10-05.md).

**Kapsam:** Mevcut Toast UI3.2.2 işlevini koruyan resmî DOMPurify yaması; sabit
upstream kaynak/hash, bütün sanitizer modülünün AST kontrollü değiştirilmesi,
kilitli build araçları, lisans/dağıtım ve gerçek browser regresyonu.

**Kabul:** Eski runtime sanitizer çıkarılmış; resmî advisory/yama doğrulanmış;
hash değişimi build’i durduruyor; temiz build/audit ve publish sınırı kanıtlı;
gerçek editörde XSS/CSP/paste/upload/recovery/preview/yayın akışları geçiyor.
Braces istisnası genişletilmez; server Markdown veya CSP koruması gevşetilmez.

**Dışında:** Editör ürününü değiştirmek, uygulama mimarisini yeniden yazmak,
commit/push/remote dispatch veya canlı dağıtım. NHN arşivli olduğu için genel
upstream bakım garantisi verilmez; gelecek advisory’ler ayrıca değerlendirilir.

**Uygulama:** [E01 kanıtı](../uygulama-kayitlari/E01-2026-10-05.md). Sonuç yalnız
doğrulanmış kayıtla DURUM’a işlenir; F57 uzak CI koşulu ayrı kalır.

**Durma noktası:** Bu işi bitirip dur. Sonraki tek iş güncel uzak CI kanıtıyla F57
yeniden kabulüdür; kendiliğinden commit/push/deploy yapılmaz.

**Sonraki ayrı onay tamamlandı:** Kullanıcı commit/push ve F57 yeniden kabulünü
ayrıca onayladı. [F57 yeniden kabulü](../uygulama-kayitlari/F57-CI-2026-10-05.md)
güncel uzak25/25 kanıtını kaydetti; ana plan58/58. Canlı deploy yapılmadı.
