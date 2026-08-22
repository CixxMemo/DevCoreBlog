# DevCoreBlog - UI Refinements & Localization Plan (v7)

**Target:** Refine the front-end layout, address responsive & visual shortcomings, and localize the entire UI into English, while maintaining 100% compliance with `.agents/AGENTS.md` rules.

---

## 🔒 Strict Architecture & Style Guardrails (Must Follow)
1. **Tech Minimal Styling:** 
   - Sharp corners ONLY (`rounded-none` or `rounded-sm` where strictly necessary).
   - Flat, high-contrast borders (`border-2 border-black` or `border-neutral-900`).
   - **BANNED:** No soft shadows (`shadow-md`, `shadow-2xl`), no glassmorphism (`backdrop-blur`), no gradients (`bg-gradient-*`), no rounded pills (`rounded-full`, `rounded-2xl`, `rounded-3xl`).
2. **Coding Standards:**
   - **Junior-Friendly English Comments:** Every modified Razor block and C# helper method MUST have concise English comments explaining *what* it does and *why*.
   - **Conciseness & DRY:** Write straightforward, direct code. Avoid over-engineering, extra packages, or SPA/API abstractions.
3. **Scope:** Front-end Razor views (`_Layout.cshtml`, `Index.cshtml`), `SlugGenerator.cs`, and UI formatting only. Do not break existing database entities or cookie auth.

---

## 📌 Phase 1: Complete English Localization & Date Formatting

### 1.1 Layout Navigation & Global UI Strings (`Views/Shared/_Layout.cshtml`)
* Replace all Turkish navigation and UI labels with concise English terminology:
  - `Ana Sayfa` ➔ `Home`
  - `Haberler` ➔ `News`
  - `Keşfet` ➔ `Discover`
  - `Kategoriler` ➔ `Categories`
  - `Giriş Yap` ➔ `Sign In` / `Login`
  - `Tema Değiştir` ➔ `Toggle Theme`
  - Footer & copyright strings standardized to English.

### 1.2 Home View Strings & Meta Data (`Views/Home/Index.cshtml`)
* Translate all sectional headers, badges, and counters:
  - `TRENDLER` ➔ `TRENDING`
  - `GÜNÜN FELSEFE ALINTISI` ➔ `PHILOSOPHY SPOTLIGHT` or `QUOTE OF THE DAY`
  - `HAFTANIN AI ARACI` ➔ `FEATURED AI TOOL`
  - `SON YAZILAR` ➔ `LATEST ARTICLES`
  - `[ AKIŞ ]` ➔ `[ FEED ]`
  - `okunma` / `views` ➔ `views` (e.g., `7 views`)
  - `YENİ` ➔ `NEW`, `TREND` ➔ `HOT`

### 1.3 Standardized Date Formatting (Across All Views)
* Replace any localized or numerical dates (`08.08.2026` / `21.08.2026`) with standard international English blog formatting:
  - Razor implementation: `@post.CreatedAt.ToString("MMM dd, yyyy")` (e.g., `Aug 21, 2026` or `Aug 08, 2026`).

### 1.4 English-Optimized Slug Helper (`DevCoreBlog.Core/Shared/Helpers/SlugGenerator.cs`)
* Ensure `SlugGenerator.cs` handles English titles cleanly:
  - Converts accents/diacritics to clean ASCII lowercase (`a-z`, `0-9`, `-`).
  - Removes trailing/leading hyphens and avoids double hyphens.
  - Retains existing method signatures so no controllers or services break.

---

## 🎨 Phase 2: Tech-Minimal Geometric Image Fallback (No Raw Text)

### 2.1 Problem
Articles without a cover image currently render a plain gray box with raw text `[ DEVCORE POST ]`, which looks unfinished.

### 2.2 Solution: Brutalist Tech Pattern Fallback
* In `Views/Home/Index.cshtml` (and any post card partial), replace the plain gray box with a clean, vector/SVG-based geometric pattern:
  - Use an inline SVG with a subtle tech grid, circuit dot matrix, or diagonal hatch pattern in monochrome (`bg-neutral-100 dark:bg-neutral-900 border-b-2 border-black`).
  - Overlay a sharp, minimalist badge in the center: e.g., `<span class="font-mono text-xs font-bold uppercase tracking-widest border border-black bg-white px-2 py-1">DEVCORE // POST</span>`.
  - Maintain the exact aspect ratio (`aspect-video` or matching container height) so card dimensions remain uniform.

---

## 🔍 Phase 3: Search Bar Proportion & Keyboard Shortcut Indicator

### 3.1 Problem
The current top search input is excessively tall and dominates the view without visual cues.

### 3.2 Solution: Streamlined Input with `Ctrl + K` Cue
* **Dimensions:** Reduce height to a balanced, compact size (`py-2.5 px-4 text-sm font-mono`).
* **Styling:** Sharp borders (`rounded-none border-2 border-black focus:outline-none focus:bg-neutral-50`).
* **Visual Elements:**
  - Left: Minimalist terminal-style search prompt symbol `>` or clean search SVG icon.
  - Right: Sharp, non-intrusive shortcut indicator badge:
    ```html
    <!-- Keyboard shortcut hint -->
    <kbd class="hidden sm:inline-block font-mono text-[10px] font-bold border border-black bg-neutral-100 px-1.5 py-0.5 uppercase">
      Ctrl + K
    </kbd>
    ```
  - Placeholder: `Search articles, concepts, or tags...`

---

## 📱 Phase 4: Responsive Layout & Mobile Navigation Drawer

### 4.1 Problem
The sidebar is currently `fixed w-64`, which obscures content or overflows on mobile/tablet viewports.

### 4.2 Solution: Responsive Drawer with Vanilla JS Toggle
1. **Mobile Top Header (`md:hidden`):**
   - Sticky top bar containing:
     - Logo (`DEVCORE [BLOG]`).
     - Hamburger toggle button (`border-2 border-black p-1.5`).
2. **Sidebar Viewport Adaptability (`Views/Shared/_Layout.cshtml`):**
   - Mobile: Off-canvas drawer (`fixed inset-y-0 left-0 z-50 transform -translate-x-full transition-transform duration-200 ease-in-out md:translate-x-0`).
   - Desktop: Static/fixed left rail (`md:w-64 md:fixed md:h-screen`).
   - Backdrop overlay on mobile when sidebar is open (`bg-black/50 md:hidden`).
3. **Main Content Container Alignment:**
   - Use `w-full md:ml-64 p-4 sm:p-6 lg:p-8 min-w-0` to guarantee that content flows without horizontal overflow on smaller screens.
4. **Vanilla JS Toggle Script:**
   - Lightweight, inline script at the bottom of `_Layout.cshtml` (no external libraries):
     - Toggles `-translate-x-full` on the sidebar.
     - Toggles backdrop visibility on button click.
     - Adds `Escape` key and click-outside listeners to close the drawer.

---

## 🛠️ Step-by-Step Execution Order for Antigravity

1. **Step 1:** Update `DevCoreBlog.Core/Shared/Helpers/SlugGenerator.cs` (Verify clean English slugging).
2. **Step 2:** Overhaul `Views/Shared/_Layout.cshtml` (English strings, mobile drawer navigation, responsive wrappers).
3. **Step 3:** Overhaul `Views/Home/Index.cshtml` (English strings, date formats, refined search bar with `Ctrl + K`, geometric SVG fallbacks for empty images).
4. **Step 4:** Review and verify responsive behavior across mobile (`< 768px`) and desktop screens.

---

## 📋 Verification & Safety Checklist
- [x] Are all UI labels strictly in English?
- [x] Are all article dates formatted as `MMM dd, yyyy` (e.g. `Aug 21, 2026`)?
- [x] Are all corners strictly sharp (`rounded-none`) with no soft shadows or gradients?
- [x] Does every modified code block have clear English comments (`//` or `<!-- -->`)?
- [x] Does the mobile sidebar toggle smoothly without breaking desktop layout?
- [x] Did we avoid introducing any new NuGet packages, SPAs, or Web APIs?
