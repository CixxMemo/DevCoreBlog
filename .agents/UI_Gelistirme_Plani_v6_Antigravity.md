
# DevCoreBlog MVP UI/UX Geliştirme Planı v6 (Tech Minimal & Merlin'in Kazanı İlhamlı)

**Hedef:** DevCoreBlog'un arayüzünü, Merlin'in Kazanı sitesinin ana iskeletini (Sol menü, üst Hero alanı, kart gridi ve sağ trend sütunu) örnek alarak yeniden kurgulamak. Bu kurgu, projenin `.agents` dosyasında zorunlu kılınan **"Tech Minimal"** (keskin kenarlar, yüksek kontrast, gölgesiz, flat tasarım) kurallarına %100 sadık kalarak, Tailwind CSS ile hızlı bir MVP olarak inşa edilecektir.

---

## 🏗️ [x] Faz 1: Altyapı Hazırlığı ve Tailwind Entegrasyonu (Tamamlandı)
*Özel CSS yazılmayacak. Mevcut teknoloji yığınına (ASP.NET Core MVC) sadık kalınacak, SPA/API kullanılmayacaktır.*

**Görevler:**
1.  [x] **Tailwind CDN:** `Views/Shared/_Layout.cshtml` dosyasının `<head>` kısmına Tailwind CSS CDN ekle.
2.  [x] **Mevcut Stiller:** `wwwroot/css/site.css` içindeki eski layout stillerini devre dışı bırak.
3.  [x] **Renk Paleti (Tech Minimal):** Tailwind config içine, Merlin'in Kazanı hissini verecek ancak gradient veya neon içermeyen yüksek kontrastlı düz renkler (flat colors) tanımla.
    *   *Örnek:* `colors: { primary: '#4f46e5', background: '#f9fafb', surface: '#ffffff', border: '#1f2937' }`

---

## 🧭 [x] Faz 2: Layout İskeleti ve Sol Menü (Sidebar) (Tamamlandı)
*Hedef: Sayfayı Merlin'in Kazanı gibi yapılandırmak; solda sabit kategori menüsü, ortada geniş içerik alanı.*

**Dosya:** `Views/Shared/_Layout.cshtml`

**Görevler:**
1.  [x] **Ana Kapsayıcı:** `<body>` etiketini Flexbox ile böl: `<body class="flex bg-background min-h-screen text-gray-900 font-sans">`
2.  [x] **Sol Menü (Tech Minimal Sidebar):**
    *   `w-64` (250px), `fixed`, `h-screen`, `bg-surface` classlarını kullan.
    *   *Kural İhlalini Önleme:* Gölge (`shadow`) YOK. Sağ tarafa keskin bir kenarlık çek: `border-r-2 border-border`.
    *   **Logo:** En üste keskin fontlu bir DevCoreBlog logosu/metni.
    *   **Menü Linkleri:** Ana Sayfa, Haberler, Keşfet, Vibe Coding, AI, Felsefe.
    *   *Link Stilleri:* Merlin'in Kazanı'ndaki gibi liste görünümü, ancak yuvarlak hatlar olmadan: `block py-3 px-4 hover:bg-border hover:text-white rounded-none border-b border-gray-200 transition-none font-bold`.
3.  [x] **Sağ İçerik Alanı:**
    *   `<main class="flex-1 ml-64 p-6 lg:p-8 flex gap-8">` (Bu alan kendi içinde Orta ve Sağ sütun olarak bölünecek).

---

## 🖼️ [x] Faz 3: Orta Sütun - Hero ve Kart Gridi (Main Content) (Tamamlandı)
*Hedef: Sitenin kalbi. Üstte Merlin'in Kazanı 'Bundle/Oyun' alanı tarzı büyük öne çıkanlar, altta blog postları.*

**Dosya:** `Views/Home/Index.cshtml`

**Görevler:**
1.  [x] **Üst Bar (Arama):** Ana alanın en üstünde köşeleri keskin (`rounded-none`), kalın kenarlıklı (`border-2 border-border`) arama çubuğu.
2.  [x] **Hero Alanı (Öne Çıkan Yazılar):**
    *   Arama çubuğunun hemen altına CSS Grid ile büyük, asimetrik bir alan kur (Örn: Sol %60, Sağ altlı üstlü %40).
    *   *Kural İhlalini Önleme:* Görsellerde blur veya yuvarlak köşe YOK (`rounded-none`). Görsel üstü yazılarda gradient YOK; düz, yarı saydam, keskin kutular kullan (Örn: `bg-black bg-opacity-80`).
3.  [x] **Makale Kartları (Grid Layout):**
    *   "Son Yazılar" başlığı (keskin ve büyük fontlu) altında grid: `<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">`
    *   **Tech Minimal Kart Stili:**
        *   Zemin: `bg-surface border-2 border-border rounded-none`.
        *   Hover: Gölge yerine pozisyon değişimi (`hover:-translate-y-1 hover:translate-x-1 border-black transition-transform`).
        *   Kategori Etiketi: Merlin'in Kazanı'ndaki renkli etiketlerin keskin, flat hali: `bg-primary text-white text-xs px-2 py-1 font-bold uppercase`.
        *   Görsel: `aspect-video object-cover border-b-2 border-border`.
        *   Açıklama: Sade başlık ve yorum/okunma sayısı gibi meta veriler.

---

## 📈 [x] Faz 4: Sağ Sütun (Trendler ve Ekstralar) (Tamamlandı)
*Hedef: Merlin'in Kazanı sağ sütunundaki "Şu an konuşulanlar", "Ne Oynayabiliriz" tarzı etkileşim alanlarını bloga uyarlamak.*

**Dosya:** `Views/Home/Index.cshtml` (veya Partial View)

**Görevler:**
1.  [x] **Layout Bölünmesi:** Faz 2'deki `<main>` içinde, sağ tarafta sabit veya kayan `w-80` genişliğinde bir sütun (`<aside>`) oluştur. (Main content `flex-1`, sağ sütun `w-80` olacak şekilde).
2.  [x] **Trend Olanlar (Trending List):**
    *   Numaralandırılmış (1, 2, 3...) keskin hatlı liste.
    *   Merlin'in Kazanı'ndaki "Sıcak", "Çok Sıcak" metinlerini "Trend", "Yeni" gibi flat tasarımlı metin etiketlerine (badge) çevir.
3.  [x] **Ekstra Alanlar:** Blogun doğasına uygun olarak sağ sütuna "Günün Felsefe Alıntısı" veya "Haftanın AI Aracı" gibi küçük, köşeli ve border-2 ile çevrili minimalist kutular ekle.

---

## 🛠️ Antigravity AI Kodlama ve Davranış Kuralları (STRICT RULES)
**Bu kuralların ihlali KESİNLİKLE yasaktır:**
1. **Tech Minimal Sınırları:** Kodlarda `rounded-lg`, `rounded-full`, `shadow-md`, `backdrop-blur`, `bg-gradient` gibi sınıflar **YASAKTIR**. Yalnızca `rounded-none`, kalın kenarlıklar (`border-2 border-black`), ve flat/düz renkler kullanılacaktır.
2. **Kısa ve Odaklı Kod (Conciseness):** Kodları gereksiz yere uzatma. DRY (Don't Repeat Yourself) prensibine uy. Basit bir `foreach` veya LINQ sorgusu yetiyorsa over-engineering yapma.
3. **Junior-Friendly Yorumlar:** Yazdığın her C# veya Razor mantığının (özellikle grid yapıları ve Model binding kısımlarının) hemen üstüne, bir junior geliştiricinin anlayacağı basitlikte İngilizce yorum satırları (`<!-- -->` veya `//`) ekle. Neyi ve neden yaptığını açıkla.
4. **Mimarinin Korunması:** ASP.NET Core MVC yapısı dışına çıkma. SPA (React vb.) veya API önerme. Sadece `_Layout.cshtml` ve `Index.cshtml` view dosyalarını Tailwind ile modernize et.
5. **Geri Bildirim:** Kodu yazdıktan sonra sohbette neleri değiştirdiğini ve bu değişikliklerin ardındaki mantığı kısa ve anlaşılır bir şekilde açıkla.
