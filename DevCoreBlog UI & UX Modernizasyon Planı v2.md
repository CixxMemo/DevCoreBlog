# DevCoreBlog UI & UX Modernizasyon Planı v2

## Hedef (Goal)

DevCoreBlog projesinin genel kullanıcı arayüzünü (Frontend), "Vibe coding", "AI agents" ve "AI coding" odaklı bir içeriğe tam uyumlu, modern ve özgün bir hale getirmek. 

Bu tasarımda temel felsefe:
- **Özgün Vibe Coding Hissi:** Teknik, keskin ("Tech Minimal" kurallarına uygun), retro-fütüristik ama tamamen temiz bir görünüm. Kesinlikle "AI slop" (yapay zeka tarafından üretilmiş, birbirine benzeyen jenerik, aşırı süslü veya ruhsuz tasarım) hissi vermeyecek. Otantik bir geliştirici/hacker blogu ruhunu taşıyacak.
- **Yüksek Okunabilirlik:** *dev.to* ilhamıyla, içeriği merkeze alan, göz yormayan tipografi, net kod blokları ve uzun süre vakit geçirmesi keyifli bir yapı.
- **Dinamik Vitrin:** *merlininkazani.com* ilhamıyla, ancak tamamen projeye özgü yorumlanmış; en yeni veya öne çıkan haberlerin/blogların büyük görseller ve keskin hatlı grid (ızgara) bloklarıyla sunulduğu giriş alanı.

> *Not: Admin paneli (yazı yazma/düzenleme) değişiklikleri bu plandan çıkarılmış olup, ileride farklı bir yapılandırma ile ayrıca ele alınacaktır.*

## Kullanıcı Onayı Gereken Konular (User Review Required)

> [!NOTE]
> **Ana Sayfa Vitrini (Hero Grid):** Ana sayfada okuyucuyu karşılayan ilk alan, son 3-4 yazıyı büyük görsellerle, keskin hatlı kutular içerisinde gösterecek. Alt kısımda ise kronolojik, daha sade bir liste (feed) akacak. Bu yerleşim stratejisi sizin için uygun mu?

## Önerilen Değişiklikler (Proposed Changes)

### 1. Ana Sayfa: Vitrin ve Akış (Home Page)

#### [MODIFY] `Views/Home/Index.cshtml`
- **Hero Grid (Vitrin):** Sayfanın en üstüne "Öne Çıkanlar / Son Yazılar" bloğu eklenecek. Görsellerin üzerine yüksek kontrastlı metin kutularının bindiği (camsı efekt *olmadan*, düz arka plan renkleriyle) asimetrik veya 2-3 kolonlu bir grid yapısı.
- **Vibe Coding Akışı (Feed):** Vitrinin altında, makalelerin yazar, okuma süresi ve etiketlerle (Tech tags) birlikte sade kartlar halinde listelendiği, dev.to tarzı pürüzsüz kaydırma deneyimi sunan liste yapısı.

### 2. Genel Tasarım ve CSS (Frontend Styling)

#### [MODIFY] `Views/Shared/_Layout.cshtml`
- Mevcut "Terminal/Bento" yapısı korunacak ancak okuma alanını (content area) daha ferah bırakmak adına genişlik ve boşluk (padding/margin) ayarları optimize edilecek.
- Tipografi: Başlıklarda JetBrains Mono'nun "hacker/tech" hissiyatı daha vurgulu, gövde metinlerinde Inter'in okunabilirliği daha temiz kullanılacak.

#### [MODIFY] `wwwroot/css/site.css` ve `wwwroot/css/terminal-theme.css`
- **Tech Minimal Kuralları:** `border-radius: 0` veya çok ince (`2px`). Gölgeler (`box-shadow`) sadece 1px solid veya "hard shadow" şeklinde retro/teknik bir his verecek şekilde (neon yayılımı değil, düz renk bloğu şeklinde) uygulanacak.
- **Renk Paleti:** Mevcut GitHub Dark (Deep Charcoal) zemin rengi korunacak. Vurgular (Accent) için Terminal Yeşili veya Turuncu, tamamen düz (solid) renkler olarak butonlarda ve hover durumlarında kullanılacak.

### 3. Makale Okuma Sayfası (Post Detail)

#### [MODIFY] `Views/Home/Post.cshtml` (veya ilgili detay sayfası)
- Odak noktası tamamen metin ve kod. 
- Yazar bilgisi, yayın tarihi ve okuma süresi en üstte çok şık, terminal satırı gibi minimal bir barda verilecek.
- İçerikteki kod blokları (Prism.js) "Tech Minimal" konseptine %100 uyarlanacak (Koyu, keskin hatlı kutular).

## Doğrulama Planı (Verification Plan)

### Manuel Testler
1. **Görsel Otantiklik:** Yeni "Vitrin" yapısının ve makale kartlarının jenerik şablonlara benzemediği, projenin "vibe coding" ruhunu yansıttığı test edilecek.
2. **Okunabilirlik:** Uzun metinlerin ve kod bloklarının mobil ve masaüstünde göz yormadan okunabildiği test edilecek.
3. **Kural Kontrolü:** Uygulanan tüm CSS değişikliklerinin projenin `AGENTS.md` içerisindeki "Tech Minimal Design" kurallarına (cam efekti yok, yumuşak gölge yok, keskin kenarlar) tam uygunluğu doğrulanacak.
