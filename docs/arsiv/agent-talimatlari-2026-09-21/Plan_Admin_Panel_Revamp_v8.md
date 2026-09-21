> HISTORICAL ARCHIVE — NOT ACTIVE INSTRUCTIONS. Superseded by repository-root AGENTS.md on 2026-09-21.

# DevCoreBlog - Hardened Admin Panel & Integration Hub Plan (v8)

**Target:** Transform the DevCoreBlog Admin Panel into a secure, developer-first command center. This overhaul introduces an advanced Markdown editor (drag-and-drop/paste Cloudinary uploads, responsive video embed renderer, autosave), real-time ViewCount analytics, inline post status toggling, a hardened n8n/Make.com webhook receiver, and a dedicated, lightweight, read-only endpoint for the React portfolio showcase (latest 3 published posts), strictly adhering to `.agents/AGENTS.md`.

---

## 🔒 Strict Architecture & Security Guardrails (.agents Compliant)

### 1. Architectural Rules
* **Server-Side ASP.NET Core MVC (net10.0):** Pure server-side application with Razor views and minimal vanilla JavaScript.
* **No Separate API Project / No SPAs:** Endpoints live within the existing MVC project structure.
* **No Heavy DTOs or AutoMapper:** Controllers interact directly with Domain Entities (`Post`, `Category`) or simple projections.
* **Cookie Authentication Only:** Retain `Microsoft.AspNetCore.Authentication.Cookies` for admin authentication.
* **Junior-Friendly English Comments:** Every new or updated C# class, method, and Razor markup MUST include clear English comments (`//` or `<!-- -->`) explaining *what* it does and *why*.

### 2. Security & Hardening Standards
* **Rate Limiting:** Built-in ASP.NET Core `RateLimiter` middleware protecting webhook and public endpoints against DDoS and brute-force attacks.
* **Timing-Attack Proof Secret Validation:** Webhook secret validation uses `CryptographicOperations.FixedTimeEquals` for constant-time byte comparisons.
* **Environment-Based Secret Management:** Webhook API secret is loaded strictly from environment variables (`.env` / `appsettings.json`), never hardcoded.
* **Fail-Safe Default for Automations:** Posts created via webhook default to `IsPublished = false` (Draft) unless explicitly flagged.
* **Strict CORS for Portfolio:** The portfolio endpoint is restricted exclusively to the portfolio site's configured origin.
* **Mass Assignment Protection:** Controller action strictly whitelists writable fields (`Title`, `Content`, `Summary`, `CategoryId`, `CoverImageUrl`).

### 3. Tech Minimal UI Standards
* Sharp corners ONLY (`rounded-none` or minimal `rounded-sm`).
* Flat, high-contrast borders (`border-2 border-black` / `border-neutral-900`).
* **STRICTLY BANNED:** No soft shadows (`shadow-md`, `shadow-2xl`), no glassmorphism (`backdrop-blur`), no gradients (`bg-gradient-*`), no rounded pills (`rounded-full`, `rounded-2xl`, `rounded-3xl`).

---

## 🧭 Phase 1: Admin Shell & Global Feedback System (`Views/Shared/_AdminLayout.cshtml`)

### 1.1 Tech-Minimal Brutalist Shell
* Overhaul `Views/Shared/_AdminLayout.cshtml` to match the brutalist Tech-Minimal design language:
  - Sidebar: Fixed left navigation `w-64 border-r-2 border-black bg-white`.
  - Links: Flat rectangular hover states (`hover:bg-black hover:text-white px-4 py-2.5 font-bold border-b border-neutral-200 transition-none`).
  - Active Link State: `bg-black text-white`.
* Sidebar Menu Items:
  1. `Dashboard` (`/Admin/Dashboard`)
  2. `Posts` (`/AdminPost`)
  3. `Categories` (`/AdminCategory`)
  4. `Automations & API` (`/Admin/Automations`)
  5. `View Public Site` (`/` in new tab)
  6. `Sign Out` (`/Account/Logout`)

### 1.2 Non-Blocking Vanilla JS Toast Notifications
* Add a global notification container in `_AdminLayout.cshtml`:
  - Renders server-side `TempData["Success"]` and `TempData["Error"]`.
  - Exposes `window.showToast(message, type)` for client-side asynchronous feedback (autosave, inline status toggle, image uploads).
  - Styling: Sharp rectangular banner (`border-2 border-black bg-white px-4 py-3 font-mono text-sm shadow-none`).

---

## 📊 Phase 2: Actionable Command Dashboard (`/Admin/Dashboard.cshtml` & `AdminController.cs`)

### 2.1 Metric & Analytics Tiles
* Update `AdminController.Dashboard()` to supply real-time blog stats via `IPostService` and `ICategoryService`:
  - **Total Posts** (Total count in system)
  - **Published Articles** (`IsPublished == true`)
  - **Drafts in Progress** (`IsPublished == false`)
  - **Total Views** (Cumulative `ViewCount` across all articles)
  - **Active Categories** (Total category count)
* Render tiles using 2-column/4-column brutalist boxes (`border-2 border-black p-5 bg-white`).

### 2.2 Top 5 Most Read Articles Widget
* Display a ranked table showing the top 5 articles sorted by `ViewCount DESC`:
  - Columns: Rank (`#01`, `#02`...), Title, Category Badge, View Counter, Quick Action (`Edit` / `View Live`).

### 2.3 Scratchpad (Fast Draft & Idea Capture)
* Add a minimalist quick-note textarea on the dashboard:
  - Automatically persists thoughts to browser `localStorage` on keystroke.
  - "Create Draft from Note" button redirects to `/AdminPost/Create` with pre-filled title and body.

### 2.4 System & Integration Health Status
* Compact status block verifying:
  - Database: `Connected (PostgreSQL)`
  - Media CDN: `Cloudinary (Active)`
  - Environment: `Development` / `Production`
  - Webhook Pipeline: `Ready (Rate Limited)`

---

## 📝 Phase 3: High-Productivity Post Management (`/AdminPost/Index.cshtml` & `AdminPostController.cs`)

### 3.1 Live Per-Row ViewCount Tracking
* Add a dedicated `Views` column displaying real-time views:
  - Badge style: `<span class="font-mono font-bold text-xs bg-neutral-100 border border-black px-2 py-1">👁️ @post.ViewCount</span>`.

### 3.2 Instant Inline Status Toggle (`IsPublished`)
* Enable instant status switching without opening the edit screen:
  - Clicking the status badge (`Published` / `Draft`) sends a lightweight `POST /AdminPost/TogglePublish/{id}` request.
  - Server toggles `post.IsPublished` and returns `{ success: true, isPublished: boolean }`.
  - UI updates badge color dynamically and triggers a toast notification without page reload.

### 3.3 Client-Side Filter & Search Toolbar
* Top table filter controls:
  - **Instant Search:** Filters rows immediately as the user types (matching title or category).
  - **Category Dropdown:** Filter by category.
  - **Status Selector:** Toggle between `All`, `Published`, or `Drafts`.

### 3.4 Direct "View Live" Quick Link
* Each row includes an external link button to view the post directly on the public site (`/post/@post.Slug`), disabled or styled with a draft indicator if unpublished.

---

## ✍️ Phase 4: Modern Markdown Editor & Rich Media Pipeline (`/AdminPost/Create.cshtml`, `Edit.cshtml`)

### 4.1 Drag-and-Drop & Clipboard Image/GIF Uploads
* Equip the editor textarea with an asynchronous upload dropzone:
  - **Drag-and-Drop:** Dragging any image/GIF (`.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`) over the editor initiates upload.
  - **Clipboard Paste (`Ctrl + V`):** Pasting image data triggers upload.
* **Upload Flow:**
  1. Vanilla JS sends the file via `fetch('POST /AdminPost/UploadEditorImage')` using `FormData`.
  2. `AdminPostController.UploadEditorImage` uses `IImageService` to upload to Cloudinary.
  3. Returns JSON `{ success: true, url: "https://res.cloudinary.com/..." }`.
  4. JavaScript inserts `![Image Description](uploaded_url)` at the current cursor position.
  5. Displays non-blocking toast: `Image uploaded and inserted.`

### 4.2 Rich Media & Responsive Video Embed Renderer (`MarkdownHelper.cs`)
* **Toolbar Embed Action:**
  - Add a `[ 🎥 Embed Video ]` button in the editor toolbar.
  - Prompts for a YouTube/Loom URL and inserts: `[video](https://www.youtube.com/watch?v=VIDEO_ID)`.
* **Markdown Pipeline Pipeline (`DevCoreBlog.Core/Shared/Helpers/MarkdownHelper.cs`):**
  - Configure `MarkdownHelper.ToHtml()` to recognize YouTube URLs (`youtube.com/watch?v=...`, `youtu.be/...`) and auto-render a responsive, high-contrast, sharp `<iframe>` container (`aspect-video w-full border-2 border-black`).

### 4.3 Live Google Search Snippet (SERP) Preview
* Right-hand settings column renders a live simulation of a Google search result card:
  - Real-time updates for Title, Slug Preview (`devcoreblog.com/post/your-slug`), and Meta Description (Summary).

### 4.4 LocalStorage Auto-Save & Recovery
* Persists `title`, `summary`, `content`, and `categoryId` to `localStorage` every 3 seconds while typing.
* On opening `/AdminPost/Create`, if an unsaved draft exists, shows a recovery banner:
  - `Unsaved draft found. [Restore] [Discard]`.
* Clears local storage automatically upon successful server submission.

### 4.5 Live Word Count & Reading Time Estimation
* Real-time metrics bar below editor:
  - Word count: e.g., `1,420 words`.
  - Estimated reading time: e.g., `~6 min read` (200 words/min).

---

## 🏷️ Phase 5: Category Management Enhancements (`/AdminCategory/*`)

### 5.1 Real-Time English Slug Preview
* Dynamic preview while typing category name:
  - Shows `Slug: /category/artificial-intelligence`.
  - Validates character cleanliness instantly.

### 5.2 Category Post Count & Direct Filter Link
* Table displays exact number of linked posts.
* Clicking the count badge redirects to `/AdminPost?categoryId={id}`.

---

## 🛡️ Phase 6: Hardened Integration Endpoints (Webhook + Portfolio API)

### 6.1 Program.cs Security Middleware Setup
* **Rate Limiter Configuration:**
  - `WebhookLimiter`: Max 5 requests per minute per IP for webhook writes.
  - `PortfolioLimiter`: Max 30 requests per minute for the public portfolio read endpoint.
* **CORS Policy for Portfolio:**
  - Configured specifically for the user's portfolio origin (e.g. `https://mehmetcan.dev` or `localhost:3000` in dev).

### 6.2 Hardened n8n / Make.com Webhook Endpoint (`Controllers/WebhookController.cs`)
* **Endpoint:** `POST /api/webhooks/posts`
* **Security Controls:**
  1. Rate limited via `[EnableRateLimiting("WebhookLimiter")]`.
  2. Requires `X-DevCore-Secret` header.
  3. Validates secret against `Configuration["DevCoreBlog:WebhookApiKey"]` using `CryptographicOperations.FixedTimeEquals`.
  4. Returns generic `401 Unauthorized` on any auth failure.
  5. **Fail-Safe Mode:** Defaults `post.IsPublished = false` (Draft) unless explicitly verified.
  6. Validates required fields (`Title`, `Content`, `CategoryId`) directly on domain entity.
  7. Returns JSON `{ success: true, postId = post.Id, slug = post.Slug }`.

### 6.3 Lightweight Public Portfolio Showcase Endpoint (`Controllers/PublicFeedController.cs`)
* **Endpoint:** `GET /api/public/posts/latest`
* **Purpose:** Allows the React portfolio site to show the latest 3 published articles with direct links back to DevCoreBlog.
* **Security Controls:**
  1. Restricted with `[EnableCors("PortfolioPolicy")]`.
  2. Rate limited via `[EnableRateLimiting("PortfolioLimiter")]`.
  3. Read-only, caching-friendly execution.
* **Response Payload (Direct Projection, No Bloated DTOs):**
  ```json
  [
    {
      "id": 12,
      "title": "Vibe Coding: The Future of Automation",
      "slug": "vibe-coding-the-future-of-automation",
      "summary": "How AI-assisted architectures accelerate freelance delivery.",
      "coverImageUrl": "https://res.cloudinary.com/...",
      "publishDate": "2026-08-20T00:00:00Z",
      "url": "https://devcoreblog.com/post/vibe-coding-the-future-of-automation",
      "categoryName": "Vibe Coding"
    }
  ]
  ```

---

## ⚡ Phase 7: Automations & API Documentation Hub (`Views/Admin/Automations.cshtml`)

* Add a dedicated admin view at `/Admin/Automations`:
  - **Webhook Connection Card:** Displays endpoint URL (`POST /api/webhooks/posts`) and header requirement (`X-DevCore-Secret: **********`).
  - **n8n / Make.com Payload Template:** Ready-to-copy JSON structure for automation nodes.
  - **cURL Test Snippet:** Pre-formatted terminal test command.
  - **Portfolio API Card:** Displays endpoint URL (`GET /api/public/posts/latest`) and current allowed CORS origin.

---

## 🛠️ Step-by-Step Execution Order for Antigravity

1. **Step 1:** Configure `RateLimiter` and `PortfolioPolicy` CORS in `Program.cs`.
2. **Step 2:** Update `MarkdownHelper.cs` in `DevCoreBlog.Core` with responsive video/iframe embedding.
3. **Step 3:** Implement secure `WebhookController.cs` (n8n/Make) and `PublicFeedController.cs` (Portfolio Showcase).
4. **Step 4:** Add `UploadEditorImage` (Cloudinary) and `TogglePublish` endpoints to `AdminPostController.cs`.
5. **Step 5:** Redesign `Views/Shared/_AdminLayout.cshtml` (Tech-minimal sidebar, toast system, English UI).
6. **Step 6:** Upgrade `Views/Admin/Dashboard.cshtml` (Stats cards, top 5 read posts, scratchpad, health check).
7. **Step 7:** Revamp `Views/AdminPost/Index.cshtml` (ViewCount column, inline toggle, live search & category filters).
8. **Step 8:** Overhaul `Views/AdminPost/Create.cshtml` & `Edit.cshtml` (Markdown editor + drag & drop image/GIF upload, video embed, Google preview, autosave).
9. **Step 9:** Create `Views/Admin/Automations.cshtml` (n8n/Make & Portfolio documentation hub).
10. **Step 10:** Polish `Views/AdminCategory/*` (Live slug preview, post count linking).

---

## 📋 Verification & Safety Checklist
- [x] Are all Admin views strictly styled in **Tech Minimal** (`rounded-none`, `border-2 border-black`, no soft shadows, no gradients)?
- [x] Does the editor upload images/GIFs directly to Cloudinary on drag-and-drop and clipboard paste?
- [x] Are YouTube/video links transformed into responsive iframes?
- [x] Does every post row in `/AdminPost/Index` display its real **ViewCount**?
- [x] Does the inline publish toggle work without refreshing the page?
- [x] Is the webhook endpoint protected with `FixedTimeEquals`, Rate Limiting, and fail-safe drafting?
- [x] Does `/api/public/posts/latest` return exactly the latest 3 published articles with strict CORS?
- [x] Are all C# classes and Razor templates documented with **Junior-Friendly English Comments**?
- [x] Is the architecture strictly server-side ASP.NET Core MVC (no AutoMapper, no heavy DTO layers)?

# DevCoreBlog - Admin Panel Revamp (v8) Test Suite

This test suite provides step-by-step verification protocols to validate all visual, functional, performance, and security requirements outlined in `Plan_Admin_Panel_Revamp_v8.md`.

---

## 🧭 Phase 1: Admin Shell & Global Feedback System [x]

### Test 1.1: Sidebar Navigation & Active States

* **Action:** Navigate between `/Admin/Dashboard`, `/AdminPost`, `/AdminCategory`, and `/Admin/Automations`.


* **Expected Result:**
* The active navigation item renders in high-contrast inverted colors (`bg-black text-white`).


* Inactive items retain flat white backgrounds with sharp black hover transitions.


* All borders remain strict 2px solid black (`border-2 border-black`) with no rounded corners (`rounded-none`).





### Test 1.2: Vanilla JS Toast Notification Stack

* **Action 1 (Manual Trigger):** Open the browser developer console (`F12`) and execute:
```javascript
window.showToast("Test notification dispatched successfully!", "success");

```


* **Expected Result:** A sharp rectangular toast appears at the top-right corner with a solid black border (`border-2 border-black bg-white`), remains visible for 4 seconds, and gracefully exits the DOM without layout shift.


* **Action 2 (Stacking Test):** Dispatch 3 notifications in rapid succession from the console.
* **Expected Result:** Notifications stack vertically in chronological order without visual overlap or broken spacing.

---

## 📊 Phase 2: Actionable Command Dashboard [x]

### Test 2.1: Real-Time Metric Tiles Verification

* **Action:** Check the displayed counts for **Total Posts**, **Published Articles**, **Drafts in Progress**, **Total Views**, and **Active Categories** on `/Admin/Dashboard`.


* **Expected Result:** All metrics match the exact database records in PostgreSQL via `IPostService` and `ICategoryService`.



### Test 2.2: Top 5 Read Articles Ranking

* **Action:** In the database, manually update the `ViewCount` of a specific article to the highest number and refresh `/Admin/Dashboard`.


* **Expected Result:**
* The article appears at position `#01` in the Top 5 table.


* Title, Category badge, view count, and quick-action links (`Edit` / `View Live`) render correctly.





### Test 2.3: Scratchpad (Autosave & Fast Draft Creation)

* **Action:** Type a note into the Scratchpad box on the dashboard (e.g., `Architecture insights on distributed n8n workers`), then refresh the page (`F5`).


* **Expected Result:**
* The text persists across page reloads via `localStorage`.


* Clicking **"Create Draft from Note"** redirects to `/AdminPost/Create` with the title and content fields pre-populated.





### Test 2.4: System Health Status Widget

* **Action:** Inspect the System Status card on the dashboard.


* **Expected Result:** Displays `Connected (PostgreSQL)`, `Active (Cloudinary)`, `Development / Production`, and `Ready (Rate Limited)` in clean tech-minimal badges.



---

## 📝 Phase 3: High-Productivity Post Management [x]

### Test 3.1: Per-Row ViewCount Tracking

* **Action:** Open `/AdminPost` (`/AdminPost/Index.cshtml`).


* **Expected Result:** Each table row includes a dedicated `Views` column displaying a sharp badge: `👁️ [Count]` matching the actual `ViewCount` column in the database.



### Test 3.2: Instant Inline Status Toggle (`IsPublished`)

* **Action:** Click the status badge of a draft post directly inside the table row. Monitor the Network tab (`F12 -> Network`).


* **Expected Result:**
* A non-blocking `POST /AdminPost/TogglePublish/{id}` request is sent.


* The badge switches to green `Published` without a full page reload.


* A toast notification confirms: `Post status updated.`




### Test 3.3: Client-Side Instant Filtering & Search

* **Action 1 (Search):** Type partial keywords into the table search bar (e.g., `vibe` or `automation`).
* **Expected Result:** Table rows filter instantly in real-time, displaying only matching titles or categories.
* **Action 2 (Category & Status Dropdown):** Select a specific category and toggle between `All`, `Published`, and `Drafts`.


* **Expected Result:** Table updates immediately to show the exact subset of records.

### Test 3.4: "View Live" Quick Link

* **Action:** Click the "View Live" action button on a published post row.


* **Expected Result:** The post opens in a new browser tab (`target="_blank"`) via its public route `/post/@post.Slug`. For draft articles, the button remains disabled or indicates draft preview state.



---

## ✍️ Phase 4: Modern Markdown Editor & Rich Media Pipeline [ ]

### Test 4.1: Drag-and-Drop & Clipboard Paste Image/GIF Uploads

* **Action 1 (Drag & Drop):** Drag an image or GIF file (`.png`, `.jpg`, `.gif`, `.webp`) from the desktop and drop it directly onto the editor textarea.


* **Action 2 (Clipboard Paste):** Copy an image to the clipboard (`Ctrl + C`) and press `Ctrl + V` inside the editor textarea.


* **Expected Result:**
* An uploading placeholder `![Uploading image...]()` appears at the cursor position.


* The file uploads asynchronously to Cloudinary via `POST /AdminPost/UploadEditorImage`.


* The placeholder automatically updates to `![Image](https://res.cloudinary.com/...)` upon completion.


* A success toast confirms the upload.





### Test 4.2: YouTube/Video Markdown to Responsive `<iframe>` Conversion

* **Action:** Insert a YouTube link in the editor:
```markdown
[video](https://www.youtube.com/watch?v=dQw4w9WgXcQ)

```


Save the post and navigate to its public detail page (`/post/@slug`).
* **Expected Result:** `MarkdownHelper.cs` parses the link and outputs a responsive YouTube iframe wrapped in a tech-minimal container (`aspect-video w-full border-2 border-black rounded-none`).



### Test 4.3: Real-Time Google SERP Snippet Preview

* **Action:** In `/AdminPost/Create`, enter a Title (e.g., `Building Resilient Freelance Automations`) and Summary (e.g., `A practical architectural breakdown for modern workflows.`).


* **Expected Result:** The right-hand Google search simulation box dynamically updates the blue title, green/dark URL slug (`[devcoreblog.com/post/building-resilient-freelance-automations](https://devcoreblog.com/post/building-resilient-freelance-automations)`), and grey meta description in real time.



### Test 4.4: LocalStorage Draft Auto-Recovery

* **Action:** Fill in a title, summary, and long markdown body on `/AdminPost/Create`. Close the browser tab without clicking Save. Reopen `/AdminPost/Create` in a new tab.


* **Expected Result:**
* A banner appears: `Unsaved draft found. [Restore] [Discard]`.


* Clicking **Restore** repopulates all form fields completely.


* Submitting the post successfully flushes the saved draft from `localStorage`.





### Test 4.5: Dynamic Word Count & Read Time Calculation

* **Action:** Paste a 600-word article into the editor body.


* **Expected Result:** The live metrics counter below the editor calculates and displays: `600 words` and `~3 min read` (based on 200 words/minute).



---

## 🏷️ Phase 5: Category Management Enhancements [ ]

### Test 5.1: Live ASCII Slug Generator

* **Action:** On `/AdminCategory/Create`, type `AI & Modern Software Engineering` into the Category Name field.


* **Expected Result:** The live slug preview box dynamically renders: `Slug: /category/ai-modern-software-engineering` with no special characters or trailing hyphens.



### Test 5.2: Filtered Redirection via Post Count Badge

* **Action:** In `/AdminCategory/Index`, click on the post count badge next to a category (e.g., `3 Posts`).


* **Expected Result:** Navigates directly to `/AdminPost?categoryId={id}`, pre-filtering the post list to only show articles belonging to that category.



---

## 🛡️ Phase 6: Hardened Integration Endpoints (Security & API) [ ]

### Test 6.1: Authorized n8n/Make.com Webhook Delivery

* **Action:** Send a valid webhook request from the terminal with the correct secret header:


```bash
curl -X POST https://localhost:7001/api/webhooks/posts \
  -H "Content-Type: application/json" \
  -H "X-DevCore-Secret: YOUR_CONFIGURED_SECRET_KEY" \
  -d '{
    "title": "Automated Deployment Pipeline with n8n",
    "summary": "Exploring zero-maintenance webhook triggers.",
    "content": "## Automated Content\nCreated seamlessly via webhook.",
    "categoryId": 1
  }'

```


* **Expected Result:**
* Returns HTTP `200 OK` with payload `{"success":true,"postId":...,"slug":"automated-deployment-pipeline-with-n8n"}`.


* The article is saved in the database with `IsPublished = false` (Draft mode default).





### Test 6.2: Constant-Time Secret Validation & 401 Rejection

* **Action:** Send a request with an invalid secret or missing header:


```bash
curl -X POST https://localhost:7001/api/webhooks/posts \
  -H "Content-Type: application/json" \
  -H "X-DevCore-Secret: INVALID_KEY_ATTEMPT" \
  -d '{"title": "Unauthorized Payload"}'

```


* **Expected Result:**
* Returns generic HTTP `401 Unauthorized`.


* Validated via `CryptographicOperations.FixedTimeEquals` to eliminate timing attack vectors.


* No stack traces or internal configuration details leaked.





### Test 6.3: Webhook Rate Limiting (Anti-Spam / DDoS Prevention)

* **Action:** Send 6 consecutive webhook requests within 1 minute from the same IP.


* **Expected Result:** Requests 1 to 5 process successfully; the 6th request is rejected with HTTP `429 Too Many Requests`.



### Test 6.4: Portfolio Showcase Endpoint (`GET /api/public/posts/latest`)

* **Action:** Send a `GET` request from your browser or terminal:


```bash
curl -X GET https://localhost:7001/api/public/posts/latest

```


* **Expected Result:**
* Returns HTTP `200 OK` with a JSON array of **strictly the latest 3 published articles**.


* Response structure contains only safe, read-only fields:
```json
[
  {
    "id": 1,
    "title": "Article Title",
    "slug": "article-title",
    "summary": "Short summary...",
    "coverImageUrl": "https://res.cloudinary.com/...",
    "publishDate": "2026-08-20T00:00:00Z",
    "url": "https://devcoreblog.com/post/article-title",
    "categoryName": "AI"
  }
]

```





### Test 6.5: Portfolio CORS Policy Enforcement

* **Action:** Execute a `fetch` request to `/api/public/posts/latest` from an unauthorized origin (e.g., via browser console on `[http://unauthorized-domain.com](http://unauthorized-domain.com)`).


* **Expected Result:** The browser blocks the request due to CORS policy failure; only the configured portfolio domain is permitted.



---

## ⚡ Phase 7: Automations & API Hub [ ]

### Test 7.1: Documentation & Payload Template Verification

* **Action:** Open `/Admin/Automations` in the browser.


* **Expected Result:**
* Webhook connection details, header specs (`X-DevCore-Secret`), sample JSON payloads, and pre-formatted cURL commands render in copyable code blocks.


* The Portfolio API section clearly shows the endpoint (`GET /api/public/posts/latest`) and current allowed CORS origin.