# DevCoreBlog - UI/UX İyileştirme ve "Tech Minimal" Uygulama Planı

Bu belge, DevCoreBlog projesinin UI/UX güncellemelerini AI ajanlarının (Cursor vb.) sıfır hata ve maksimum kontrolle yapması için hazırlanmış teknik bir refactoring planıdır. `AGENTS.md` kurallarına sıkı sıkıya bağlı kalınarak, gereksiz CSS yazmaktan kaçınılmış ve Tailwind Utility Class'ları ile mevcut CSS değişkenleri harmanlanmıştır.

> [!IMPORTANT]
> Tüm değişiklikler `AGENTS.md` içindeki "Tech Minimal" kurallarına (keskin hatlar, gölge/cam efekti yok) uygun olarak tasarlanmıştır.

## [x] Faz 1: Hiyerarşi, Okunabilirlik ve Düzen (Düşük Risk, Yüksek Etki)

Bu fazda global okunabilirliği artırmak için Tailwind sınıfları ve mevcut CSS kuralları optimize edilecektir.

### [x] Faz 1.1: Grid Boşluklarının Optimizasyonu
- **Dosya:** `Views/Home/Index.cshtml`
- **İşlem:** Grid yapısındaki `gap-4` sınıfı, tasarıma daha fazla nefes aldırmak için `gap-6` veya `gap-8` olarak değiştirilecektir.
  - *Değişecek Satır:* `<div class="grid grid-cols-1 md:grid-cols-2 gap-4">` -> `<div class="grid grid-cols-1 md:grid-cols-2 gap-8">`

### [x] Faz 1.2: Tipografik Okunabilirliğin Artırılması
- **Dosya:** `Views/Home/Index.cshtml`
- **İşlem:** Kart içi özet metninin (`post-card-summary`) okunabilirliğini artırmak için `.cshtml` içindeki `<p class="post-card-summary">` elementine `leading-relaxed` (veya `leading-loose`) Tailwind sınıfı eklenecektir.

### [x] Faz 1.3: Vurgu Renklerinin (Tags) Güncellenmesi
- **Dosya:** `wwwroot/css/terminal-theme.css`
- **İşlem:** `.post-card-tag` sınıfı güncellenerek, metin rengi daha belirgin bir terminal sarısı/mavisi ile değiştirilecektir (örneğin mevcut `color: var(--text-muted)` yerine `color: #569CD6` veya benzer bir pastel vurgu).

## [x] Faz 2: Kart Etkileşimi ve "Tech Minimal" Adaptasyonu (Orta Risk)

Kartların üzerine gelindiğinde (hover) oluşan etkileşimler, "gölge yok" kuralına tam uyumlu hale getirilecektir.

### [x] Faz 2.1: Mevcut "Ağır" Hover Efektlerinin Temizlenmesi
- **Dosya:** `wwwroot/css/terminal-theme.css`
- **İşlem:** `.post-card:hover` sınıfı altındaki `box-shadow: 4px 4px 0 0 var(--accent);` ve `transform: translateY(-3px) translateX(-2px);` efektleri tamamen **silinecektir**. "Tech Minimal" kuralı gereği gölge istenmemektedir.

### [x] Faz 2.2: Minimal Hover Efekti Ekleme
- **Dosya:** `wwwroot/css/terminal-theme.css`
- **İşlem:** Silinen gölge yerine, hover durumunda kartın arka planını çok hafif aydınlatacak bir kural eklenecektir: `background-color: rgba(255, 255, 255, 0.03);`. Border renginin değişmesi kuralı (`border-color: var(--accent);`) korunacaktır.

### [x] Faz 2.3: Yapısal Doğrulama
- **Dosya:** `Views/Home/Index.cshtml`
- **İşlem:** Kart yapısı halihazırda `<a class="... post-card">` ile sarmalandığı için iç içe `<a>` etiketi hatası yoktur. İçerideki "cat README.md" bölümünün `<span>` olarak kalması korunacaktır (Değişiklik gerekmez, doğrulama adımıdır).

## [x] Faz 3: Navigasyon ve Ekosistem Detayları (Basit Ekleme)

Terminal konseptini güçlendirecek son rötuşlar yapılacaktır.

### [x] Faz 3.1: Portfolyo Yönlendirmesinin Eklenmesi
- **Dosya:** `Views/Shared/_Layout.cshtml`
- **İşlem:** Sol sidebar'daki "Links" kartının (Card 4) içine, mevcut GitHub linkinin altına Portfolyo yönlendirmesi eklenecektir.
  - *Eklenen Link:* `<a href="#" class="sidebar-link"><span class="sidebar-link-icon">›</span> ./portfolio.sh</a>`

### [x] Faz 3.2: Mikro Animasyon Kontrolü
- **Dosya:** `Views/Shared/_Layout.cshtml` (veya ilgili CSS)
- **İşlem:** Üst kısımdaki (Topbar) `$` sembolünün yanına veya arama kutusuna focus olunduğunda daha belirgin bir etkileşim eklenebilir. (Mevcut `topbar-cursor` kontrol edilecek).

---

## Doğrulama Planı (Verification Plan)
- Değişiklikler sonrası projeyi derleyip (`dotnet build`) çalıştırarak (`dotnet run`) anasayfadaki boşlukların, okunabilirliğin ve hover efektlerinin (gölgesiz) test edilmesi.
- Tarayıcı Developer Tools (F12) ile iç içe `<a>` etiketi oluşmadığından emin olunması.
