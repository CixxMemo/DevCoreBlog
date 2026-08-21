# DevCoreBlog - Klasik MVP UI/UX Geliştirme ve Uygulama Planı

Bu belge, sitenin mevcut "vibe coding/terminal" tasarımından vazgeçilip, daha klasik, temiz ve "Tech Minimal" MVP tasarımına geçiş için oluşturulmuş **Doğrudan Yapay Zeka Komut Listesidir (Checklist)**.

> **YAPAY ZEKA İÇİN KESİN ÇALIŞMA KURALI:** Bu plan kusursuz bir "Execution (Uygulama)" planıdır. Her adımın başındaki `[ ]` işareti atomik bir görevi temsil eder. Yapay zeka (Antigravity) **her adımda SADECE BİR iş yapacak, projeyi derleyip/test edecek, doğruluğundan emin olduktan sonra işareti `[x]` yapıp bir sonraki adıma geçecektir.** Adımları birleştirmek veya atlamak kesinlikle YASAKTIR.

---

## 🛑 KRİTİK TASARIM KURALLARI (MANDATORY RULES)
1. **AI Slop Yasak:** Karmaşık, aşırı süslü veya gereksiz CSS/JS animasyonları KULLANILMAYACAK.
2. **Tasarım Dili (Tech Minimal):** Keskin hatlar (border-radius: 0), minimalist görünüm. Vurgu renkleri (Accent Colors) olarak Sarı ve Turuncu tonları kullanılacak. Neon veya cam efekti (glassmorphism) yasak.
3. **Teknoloji Sabiti:** Projede var olan **Tailwind CSS** kullanılacak. Ekstra bir UI kütüphanesi (Bootstrap vb.) projeye dahil edilmeyecek. JS işlemleri (Carousel vb.) Vanilla JS ile yazılacak.
4. **Tema:** Tasarım hem Karanlık (Dark) hem Aydınlık (Light) temayı destekleyecek. CSS/JS'te `prefers-color-scheme` kullanılarak kullanıcının sistem varsayılanı ilk tercih edilecek.

---

## 🛠️ Faz 1: Temizlik ve Tema Altyapısı
*Amaç: Eski terminal tasarımını temizleyip, sistem temasına duyarlı klasik iskeleti (Tailwind destekli) kurmak.*

- [x] **Adım 1.1:** `wwwroot/css/terminal-theme.css`, `wwwroot/js/command-palette.js` ve `wwwroot/js/terminal.js` dosyalarını sil.
- [x] **Adım 1.2:** `Views/Shared/_Layout.cshtml` içinden silinen bu dosyalara yapılan `<link>` ve `<script>` referanslarını kaldır.
- [x] **Adım 1.3:** `wwwroot/css/site.css` içindeki eski terminal özel kodlarını (grid pattern, monospace zorlamaları vb.) tamamen temizle.
- [x] **Adım 1.4:** `_Layout.cshtml` içindeki "Command Palette Modal" ve "Bento Grid" HTML yapılarını sil, yerini standart bir `<div>` wrapper'a bırak.
- [x] **Adım 1.5:** Tailwind konfigürasyonunu (script içindeki) ve `site.css`'i güncelleyerek `:root` (Aydınlık) ve `@media (prefers-color-scheme: dark)` (Karanlık) için Tech Minimal renk paletini (arka plan, metin, turuncu/sarı vurgu) tanımla.

---

## 🏗️ Faz 2: Topbar, Sidebar ve Footer Geliştirmesi
*Amaç: Sitenin ana iskeletini keskin hatlarla oluşturmak.*

- [x] **Adım 2.1:** `_Layout.cshtml` içinde Topbar oluştur. Soluna Logo/Banner yerleştir.
- [x] **Adım 2.2:** Topbar'ın ortasına Arama (Search) inputu yerleştir. (Köşeli tasarım, focus olunca turuncu kenarlık).
- [x] **Adım 2.3:** Topbar'ın sağına Login butonu ve Tema Değiştirici (Güneş/Ay) butonu ekle.
- [x] **Adım 2.4:** Tema değiştirici butonu için Vanilla JS kodunu yaz; seçimi LocalStorage'a kaydet ve sayfa yenilendiğinde hatırlanmasını sağla.
- [x] **Adım 2.5:** Ana sayfada görünecek Sol Menüyü (Sidebar) oluştur. Kategorileri veritabanından köşeli butonlar/linkler olarak listele.
- [x] **Adım 2.6:** Sayfanın altına Footer alanı oluştur. Portfolio (Web Sitesi), GitHub, LinkedIn ve Twitter(X) ikonlu linklerini ekle.

---

## 🎠 Faz 3: Ana Sayfa (Index) - Carousel ve Post Listesi
*Amaç: Kullanıcıyı karşılayan ana ekranı dinamik, okunabilir ve "Tech Minimal" yapmak.*

- [x] **Adım 3.1:** `Views/Home/Index.cshtml` sayfasındaki eski "tail -f" vb. terminal HTML kodlarını tamamen sil.
- [x] **Adım 3.2:** `Index.cshtml` en üstüne Tailwind ile bir Carousel (Slider) çerçevesi ekle (İlk 3 post için).
- [x] **Adım 3.3:** Carousel için Vanilla JS kodu yaz. Otomatik kayma, Sol/Sağ ok tuşları (`<` `>`) ve alt kısımda sayfalama noktaları (dot navigation) çalışsın.
- [x] **Adım 3.4:** Carousel'in altına, geri kalan postları dikey olarak alt alta (stacked) listeleyecek ana çerçeveyi kodla.
- [x] **Adım 3.5:** Post listesindeki kartların tasarımını tamamla (Görsel, Başlık, Özet, Kategori). Kartların köşelerini keskin (rounded-none) yap ve hover durumunda sarı/turuncu border çıkar. Tüm kartı tıklanabilir yap.

---

## 📄 Faz 4: Dinamik Görünüm Yönetimi (Post Detay Sayfası)
*Amaç: Yazı okuma ekranında kullanıcının dikkatini dağıtacak unsurları gizlemek.*

- [x] **Adım 4.1:** `Controllers/PostController.cs` veya ilgili controller'da, Detail sayfasına giderken `ViewData["HideSidebar"] = true;` ve `ViewData["HideSearch"] = true;` değişkenlerini ayarla.
- [x] **Adım 4.2:** `_Layout.cshtml` içinde Razor `if` bloğu ekleyerek, `ViewData["HideSidebar"]` true ise Sol Kategori Menüsünü render etme. Detay sayfasını tam genişliğe (full width) yay.
- [x] **Adım 4.3:** `_Layout.cshtml` içinde Razor `if` bloğu ekleyerek, `ViewData["HideSearch"]` true ise Topbar'daki Arama Çubuğunu render etme.

---

## 🔎 Faz 5: Son Kontroller ve Test
*Amaç: Tasarımın kusursuz, köşeli ve hatasız olmasını sağlamak.*

- [x] **Adım 5.1:** Tüm HTML/CSS kodlarını tara. `rounded` veya `border-radius` kalıntılarını bulup yok et (köşeli tasarıma zorla).
- [x] **Adım 5.2:** Sistemi hem Aydınlık hem Karanlık modda çalıştır, menülerin, yazıların ve butonların kontrast oranlarını manuel test et (gözle görülür bir sorun kalmasın).
- [x] **Adım 5.3:** Arama, Kategori tıklama ve Yazı detayına gitme linklerinin bozulmadığından emin olmak için navigasyon testleri yap.
