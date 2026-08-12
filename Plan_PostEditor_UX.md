# Plan: Post Editor UX Enhancement

**CRITICAL DIRECTIVE FOR AI AGENT:**
1. Before executing any phase, you MUST read and strictly adhere to the rules defined in `.agents/AGENTS.md`. 100% compliance with agent guidelines is mandatory.
2. **STRICT TASK CHECKING RULE:** Execute tasks phase by phase (atomically). Immediately upon successfully completing a phase/sub-phase, you MUST update this file by changing its bracket from `[ ]` to `[x]`. Do not proceed to the next phase without checking off the completed one.
3. Do not write lengthy conversational explanations to the user; focus on writing clean, modular code directly.

---

### GOAL
Transform the `AdminPost/Create` and `AdminPost/Edit` pages into a professional, distraction-free writing environment (similar to Notion/Notopod). Shift focus entirely to Title and Content while moving metadata to a clean side panel, and replace the basic textarea with Toast UI Editor.

---

### PHASES & ATOMIC STEPS

#### [x] Phase 1: Layout & Grid Restructuring
- [x] **Phase 1.1:** Restructure `Views/AdminPost/Create.cshtml` into a 2-column split layout (Left: Main Writing Area 8/12 or 9/12; Right: Post Settings Panel 4/12 or 3/12).
- [x] **Phase 1.2:** Restructure `Views/AdminPost/Edit.cshtml` to mirror the exact same 2-column split layout.
- [x] **Phase 1.3:** Isolate Title & Content into the Left Column.
- [x] **Phase 1.4:** Move Category, Publish Date, Summary, and Cover Image Upload fields into the Right Column inside a clean "Post Settings" card.

#### [x] Phase 2: Modern Markdown Editor Integration (Toast UI)
- [x] **Phase 2.1:** Inject Toast UI Editor CDN scripts and CSS (including Dark Theme CSS) into `Views/Shared/_AdminLayout.cshtml`.
- [x] **Phase 2.2:** Hide the default `<textarea asp-for="Content">` in `Create.cshtml` and `Edit.cshtml` while keeping it in the DOM for form submission.
- [x] **Phase 2.3:** Initialize Toast UI Editor in `Create.cshtml` with support for both `markdown` and `wysiwyg` view modes.
- [x] **Phase 2.4:** Initialize Toast UI Editor in `Edit.cshtml` and pre-load existing Markdown content into the editor.
- [x] **Phase 2.5:** Add a JavaScript `onsubmit` handler on both forms to sync `editor.getMarkdown()` into the hidden `Content` textarea prior to submission.

#### [x] Phase 3: Styling & UI Refinement
- [x] **Phase 3.1:** Apply header-style CSS to the Title input (`border: none; outline: none; box-shadow: none; font-size: 2.5rem; font-weight: bold; background: transparent;`) in both views.
- [x] **Phase 3.2:** Style the Right Column "Post Settings" side panel with minimal borders, subtle padding, and compact form fields.
- [x] **Phase 3.3:** Move and fix the "Save / Publish" action buttons to the top-right header section of the editor layout.

#### [x] Phase 4: Image Upload Hook Setup (Prep)
- [x] **Phase 4.1:** Configure the `addImageBlobHook` callback inside the Toast UI Editor initialization script in `Create.cshtml`.
- [x] **Phase 4.2:** Configure the `addImageBlobHook` callback inside the Toast UI Editor initialization script in `Edit.cshtml`.

---

### OUTPUT EXPECTATIONS
* A clean, distraction-free, 2-column writing environment.
* Fully functional Toast UI Markdown Editor with dark mode support.
* Borderless, document-header style Title input.
* Automatic checking of completed phases `[x]` in this file as work progresses.

---

### NEXT ACTION
Read `.agents/AGENTS.md`, then begin executing **Phase 1.1**. Update the checklist items to `[x]` as you complete each sub-phase.
