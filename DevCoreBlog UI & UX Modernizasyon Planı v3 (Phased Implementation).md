# DevCoreBlog UI & UX Modernizasyon Planı v3 (Phased Implementation)

## Hedef (Goal)

DevCoreBlog kullanıcı arayüzünü (Frontend) "Vibe Coding", "AI Agents" konseptlerine uygun, okuması keyifli, otantik bir teknoloji blogu haline getirmek. Tasarım, ilham alınan sitelerin (dev.to, merlininkazani) işlevselliğini barındırırken, kesinlikle jenerik bir "AI slop" olmayacak ve projenin `AGENTS.md` dosyasındaki **"Tech Minimal"** (cam efekti yok, aşırı ovalleştirme yok, neon gradient yok) kurallarına %100 uyacaktır.

Tüm geliştirmeler, kontrol edilebilir ve test edilebilir olması için **Fazlara (Phases)** bölünmüştür.

## Kullanıcı Onayı Gereken Konular (User Review Required)

> [!NOTE]
> **Carousel Yapısı:** Kayan vitrin (Cyber Carousel) için dışarıdan ağır bir JavaScript kütüphanesi (Slick, Swiper vb.) kurmak yerine, performans ve "Tech Minimal" kuralları gereği Tailwind CSS + Vanilla JS ile özel, hafif bir carousel yazılacaktır. Bu yaklaşım performans için uygun mudur?

---

## Önerilen Değişiklikler ve Fazlar (Proposed Changes & Phases)

### FAZ 1: Layout & Core Shell Redesign (Ana İskelet) [TAMAMLANDI]

Bu fazda sayfanın ana iskeleti olan `_Layout.cshtml` yeniden yapılandırılacak. Sağlı sollu dağınık yapı yerine, keskin ve düzenli bir Grid sistemi kurulacak.

#### [MODIFY] `Views/Shared/_Layout.cshtml` & `wwwroot/css/terminal-theme.css`
- **Persistent Left Sidebar:** Sayfanın sol tarafına sabit (fixed), 1px solid border ile ayrılmış keskin hatlı bir menü eklenecek. İçerisinde logomuz, navigasyon linkleri (Monospace ikonlar: `~/ Ana Sayfa`, `[#] Kategoriler`) yer alacak.
- **Central Command Line (Top Bar):** Üst kısımdaki klasik header yerine, sayfanın tam ortasında duran devasa bir komut satırı/arama çubuğu yerleştirilecek: `devcore:~$ [ Aramak için kelime veya komut girin... (Ctrl+K) ]`. Bu bar aynı zamanda sitede arama yapacak ve `/komut` şeklindeki kısayolları (zaten var olan Command Palette mantığıyla) dinleyecek.
- **Responsive Uyumluluk:** Mobil ekranlarda sol bar gizlenip hamburger menüye dönüşecek, üst komut satırı görünür kalacak.

---

### FAZ 2: Hero Section & "Cyber Carousel" [TAMAMLANDI]

Ana sayfanın üst kısmı (Vitrin), ilham görselindeki gibi kayan bir yapıya bürünecek ancak "Vibe Coding" tarzında olacak.

#### [MODIFY] `Views/Home/Index.cshtml` & `wwwroot/js/site.js`
- **Cyber Carousel Eklenmesi:** Ana sayfanın en üstünde, son eklenen 3-4 "Öne Çıkan" (Featured) yazının döndüğü bir alan.
- **Tasarım Kuralları:** `border-radius: 0` veya çok düşük olacak. Resimlerin üzerinde düz renk, yüksek kontrastlı yarı saydam (backdrop-blur OLMADAN) siyah metin kutuları yer alacak.
- **Vibe Detayı:** Kayan resimlerin zamanlaması, alt kısımda bir terminal yükleme çubuğu gibi (örn: `Loading [██████░░░░] %60`) gösterilecek. Yön tuşları tamamen köşeli `<` ve `>` sembollerinden oluşacak.

---

### FAZ 3: Feed & "System Status" (İçerik Akışı ve Trendler) [TAMAMLANDI]

Ana sayfadaki yazıların listelenme şekli ve sağ taraf (Trendler).

#### [MODIFY] `Views/Home/Index.cshtml`
- **Grid Değişimi:** Carousel'in altında, sol tarafı (sayfanın %70'i) makale akışına (feed), sağ tarafı (%30) ise yan panele ayıran yeni bir grid tasarımı.
- **Vibe Coding Akışı (dev.to tarzı):** Makaleler sade, keskin hatlı kartlar olarak listelenecek. Yazar, okuma süresi ve etiketler (Tags) "Tech Minimal" tarzda net görünecek.
- **System Status (Şu An Konuşulanlar):** Sağ sütunda, en çok okunan veya trend olan yazılar bir "Terminal Log" formatında listelenecek. Normal 1-2-3 listesi yerine `[INFO]`, `[TREND]`, `[HOT]` gibi prefix'lerle (ön eklerle) dizeceğiz.

---

### FAZ 4: Makale Detay Sayfası İyileştirmeleri (Readability) [TAMAMLANDI]

Kullanıcının sitede zaman geçirmesini keyifli kılan en önemli yer makale okuma sayfasıdır.

#### [MODIFY] `Views/Home/Post.cshtml`
- **Zen Okuma Deneyimi:** Metin alanı ortalanacak ve maksimum 700-800px genişlikle sınırlandırılarak göz yorması engellenecek.
- **Tipografi:** Başlıklarda JetBrains Mono, gövde metninde Inter fontlarının kontrastı (font ağırlıkları ve satır yükseklikleri) ayarlanacak.
- **Kod Blokları:** Prism.js ayarları, projenin tamamen keskin hatlı ve koyu tonlu "Tech Minimal" tasarım diline entegre edilecek.

---

## Doğrulama Planı (Verification Plan)

### Manuel Testler
1. **Kurallara Uygunluk Testi:** `AGENTS.md`'de yasaklanan `shadow-2xl`, `backdrop-blur`, `rounded-3xl` gibi Tailwind class'larının kullanılmadığı, tamamen keskin hatlı bir UI elde edildiği kod üzerinden doğrulanacak.
2. **Carousel Testi:** Yazılan Vanilla JS carousel'in zamanlamasının doğru çalıştığı, tıklamalarla yönlendirilebildiği ve mobil cihazlarda kaydırma (swipe - eğer eklenirse) veya tıklama ile çalıştığı test edilecek.
3. **Command Bar Testi:** Üstteki arama/komut çubuğunun hem arama (GET query) yaptığı hem de eski Command Palette işlevini taşıdığı kontrol edilecek.
4. **Layout Testi:** Sol barın (Sidebar) masaüstünde ekranı sıkıştırmadığı, içerik alanının okuması ferah kaldığı test edilecek.

### Otomatik Testler (Gerekirse)
- ASP.NET Core projesinin başarıyla derlendiği (`dotnet build`) kontrol edilecektir. MVC yapısına ve model kullanımına (`Post`, `Category`) dokunulmadan sadece View (HTML/CSS/JS) değişikliği yapılacağı için mevcut C# testleri bozulmayacaktır.
