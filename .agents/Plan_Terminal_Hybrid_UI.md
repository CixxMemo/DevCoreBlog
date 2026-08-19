# Plan: Terminal-Hybrid UI/UX Revamp

## 🤖 Agent Instructions & Context
* **Role:** You are a senior frontend developer and ASP.NET Core MVC expert.
* **Goal:** Transform the current "AI Slop" dashboard-like blog UI into a "Terminal-Hybrid" design. 
* **Concept:** The site should feel like a CLI (vibe coding environment) for navigation and listing, but provide a clean, highly readable, modern typography experience for reading the actual blog content.
* **Tech Stack:** ASP.NET Core MVC (C#), Razor Pages (`.cshtml`), Vanilla JavaScript (ES6+), CSS3.
* **Rules:**
  * DO NOT use heavy frontend frameworks (React/Vue) for this. Keep it native to Razor and Vanilla JS.
  * Work atomically. Complete one step before moving to the next.
  * Update this `plan.md` by checking off `[x]` as tasks are completed.
  * Preserve the existing backend architecture and Entity Framework models.

---

## 🛠️ Phase 1: Structure & Layout Conversion (Grid to Linear)
*Bypass the current grid layout to mimic a terminal's stdout stream.*

- [x] **Step 1.1:** Open `Views/Home/Index.cshtml` and `Views/Home/Category.cshtml`.
- [x] **Step 1.2:** Remove grid-based CSS classes (e.g., `col-md-6`, `grid`, flex wrapping). 
- [x] **Step 1.3:** Restructure the post loop to render as a linear list (vertical stack). 
- [x] **Step 1.4:** Redesign the post item card:
  - Remove heavy borders and box-shadows.
  - Prefix post titles with a terminal prompt style (e.g., `<span class="prompt">cixxmemo@devcore:~$</span> cat `).
  - Format the date and category as terminal output metadata (e.g., `[2026-08-12] [AI]`).

## 🎨 Phase 2: Hybrid Typography & Theming
*Create the contrast between CLI vibe and reading UX.*

- [x] **Step 2.1:** Open `Views/Shared/_Layout.cshtml` and inject Google Fonts (or similar) for:
  - Monospace: `JetBrains Mono` or `Fira Code` (for terminal elements).
  - Sans-Serif: `Inter` or `Roboto` (for blog body text).
- [x] **Step 2.2:** Open `wwwroot/css/site.css` (or `_Layout.cshtml.css`).
- [x] **Step 2.3:** Apply global terminal theme variables:
  - Background: Deep dark (e.g., `#0d1117` or `#09090b`).
  - Text (Primary): Off-white/slate (`#e2e8f0`).
  - Accent (Prompt/Success): Terminal green (`#10b981`) or neon purple (`#a855f7`).
- [x] **Step 2.4:** Scope the typography:
  - Apply the Monospace font to `.sidebar`, `.cli-input`, `.post-meta`, and `code` blocks.
  - Apply the Sans-Serif font to `.post-content`, `.blog-body` with `line-height: 1.7` and `font-size: 1.125rem` for maximum readability.

## 💻 Phase 3: Interactive JS CLI Parser (The Vibe)
*Make the terminal visually functional, not just decorative.*

- [x] **Step 3.1:** Add a fixed or sticky terminal input area in `Views/Shared/_Layout.cshtml` (e.g., bottom of the sidebar or fixed to the bottom of the screen).
  - Include a prompt `$` and a text `<input id="cli-input" type="text" autofocus />`.
- [x] **Step 3.2:** Create `wwwroot/js/terminal.js` and link it in the Layout.
- [x] **Step 3.3:** Implement the CLI logic in `terminal.js`:
  - Listen for the `Enter` key on `#cli-input`.
  - Parse basic commands:
    - `help`: Print available commands (ls, cd, clear, about).
    - `clear`: Clear the DOM output area.
    - `ls`: Fetch and display categories (can be static array mapping or AJAX call to a lightweight JSON endpoint).
    - `cd <slug>`: Redirect the browser using `window.location.href = '/Home/Category/' + slug`.
- [x] **Step 3.4:** Add command history navigation (Up/Down arrow keys) for true terminal UX.

## ⚙️ Phase 4: Integration & Polish
*Ensure the backend serves the frontend flawlessly.*

- [x] **Step 4.1:** Check `Controllers/HomeController.cs`. If necessary, add a small endpoint `[HttpGet("api/categories")]` to return category names purely for the JS `ls` command to consume.
- [x] **Step 4.2:** Refine Mobile UX:
  - Ensure the CLI input is accessible on mobile keyboards.
  - Make sure the linear list doesn't cause horizontal scrolling.
- [x] **Step 4.3:** Test the "Read Mode": Click on a post, go to `Views/Home/Detail.cshtml`, and ensure the markdown content renders beautifully in the Sans-serif reading font, while keeping the surrounding wrapper in the terminal vibe.
