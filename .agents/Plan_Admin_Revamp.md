# Plan: Complete Admin Panel Modernization & UI/UX Revamp

**CRITICAL DIRECTIVE FOR AI AGENT:**
1. Before executing any phase, you MUST read and strictly adhere to the rules defined in `.agents/AGENTS.md`. 100% compliance with agent guidelines is mandatory.
2. **STRICT TASK CHECKING RULE:** Execute tasks phase by phase (atomically). Immediately upon successfully completing a phase/sub-phase, you MUST update this file by changing its bracket from `[ ]` to `[x]`. Do not proceed to the next phase without checking off the completed one.
3. Do not write lengthy conversational explanations to the user; focus on writing clean, modular code directly.

---

### GOAL
Redesign the entire Admin Panel to feature a modern, light, modular, and "Notion-like" UI. The interface must be distraction-free, using soft gray backgrounds for navigation, pure white for content areas, and minimal/invisible borders. The Post Editor must become a seamless, full-page writing experience using Toast UI Editor.

---

### PHASES & ATOMIC STEPS

#### [x] Phase 1: Global Theme & Layout Restructuring
- [ ] **Phase 1.1:** ~~Update `Views/Shared/_AdminLayout.cshtml`. Remove all dark theme classes (`bg-dark`, `text-white`, etc.). Set the main body background to a very light gray (e.g., `#f7f7f5`).~~ ✅ **DONE**
- [ ] **Phase 1.2:** ~~Redesign the Sidebar. Make it a fixed left column with a transparent or soft-gray background. Update navigation links to have clean hover states (e.g., soft gray rounded backgrounds) and dark text/icons.~~ ✅ **DONE**
- [ ] **Phase 1.3:** ~~Create a Top Navbar (Header) inside the main content area for Breadcrumbs, Page Titles, and User Profile/Logout actions. Keep it clean with a subtle bottom border or soft shadow.~~ ✅ **DONE**
- [ ] **Phase 1.4:** ~~Update `_Layout.cshtml.css` (or admin custom CSS) to define global variables for the new light theme (primary text, secondary text, borders, backgrounds). Ensure fonts are modern and readable (e.g., Inter, Roboto, or system-ui).~~ ✅ **DONE**

#### [x] Phase 2: Dashboard (Command Center) Modularization
- [ ] **Phase 2.1:** ~~Update `Views/Admin/Dashboard.cshtml`. Create a responsive CSS Grid layout.~~ ✅ **DONE**
- [ ] **Phase 2.2:** ~~Build "Quick Stat Cards" at the top (e.g., Total Posts, Total Categories, Total Views). Use white backgrounds (`#ffffff`), soft rounded corners (`border-radius: 8px`), no heavy borders, and subtle shadows.~~ ✅ **DONE**
- [ ] **Phase 2.3:** ~~Add a "Recent Posts" list widget and a "Quick Actions" widget (buttons for "New Post", "New Category") below the stats.~~ ✅ **DONE**

#### [x] Phase 3: Data Grids & Listing Pages
- [ ] **Phase 3.1:** ~~Update `Views/AdminPost/Index.cshtml`. Wrap the table in a white, rounded card. Remove default browser table borders. Use clean row dividers (`border-bottom: 1px solid #eaeaea`) and ample cell padding. Add a primary "New Post" button at the top right.~~ ✅ **DONE**
- [ ] **Phase 3.2:** ~~Update `Views/AdminCategory/Index.cshtml` using the exact same modular table design language.~~ ✅ **DONE**
- [ ] **Phase 3.3:** ~~Standardize action buttons (Edit, Delete, View) across all tables using minimalist icon buttons or soft-colored badges.~~ ✅ **DONE**

#### [x] Phase 4: Distraction-Free Post Editor (Notion Style)
- [ ] **Phase 4.1:** ~~Restructure `Views/AdminPost/Create.cshtml` and `Views/AdminPost/Edit.cshtml` into a split layout: Left column (9/12) for writing, Right column (3/12) for "Post Settings" (Category, Date, Summary, Cover Image).~~ ✅ **DONE**
- [ ] **Phase 4.2:** ~~Integrate Toast UI Editor (Light Theme). Remove any previous dark mode CSS for the editor. Hide the default `<textarea>` and bind the TUI editor content to it on form submit.~~ ✅ **DONE**
- [ ] **Phase 4.3:** ~~Write CSS overrides to make the Title input and TUI Editor seamless. Remove borders, backgrounds, and outlines from the Title input (make it `font-size: 2.5rem; font-weight: bold;`). Remove the outer border of `.toastui-editor-defaultUI` so it blends into the white page.~~ ✅ **DONE**
- [ ] **Phase 4.4:** ~~Style the Right Column (Metadata) with a clean, light-gray background (`#f9f9f9`), subtle padding, and minimalist input fields. Place the "Publish/Save" button prominently at the top of this column.~~ ✅ **DONE**
- [ ] **Phase 4.5:** ~~Initialize the `addImageBlobHook` in the TUI Editor JS setup (leave the callback empty with a `console.log` for future backend integration).~~ ✅ **DONE**

#### [x] Phase 5: Category & Secondary Forms
- [ ] **Phase 5.1:** ~~Update `Views/AdminCategory/Create.cshtml` and `Views/AdminCategory/Edit.cshtml` to use clean, centered, white card layouts for their forms. Use the new minimalist input styles defined in Phase 1.~~ ✅ **DONE**

---

### OUTPUT EXPECTATIONS
* A completely cohesive, light-themed admin panel.
* High modularity: Cards, tables, and forms share the same CSS variables and design logic.
* A distraction-free, robust Post Editor with TUI Editor integrated seamlessly.
* Automatic checking of completed phases `[x]` in this file.

---

### NEXT ACTION
Read `.agents/AGENTS.md`, then begin executing **Phase 1.1**. Update the checklist items to `[x]` as you complete each sub-phase.