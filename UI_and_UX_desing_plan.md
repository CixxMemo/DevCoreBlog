# DevCoreBlog - Hybrid CLI & Bento UI Implementation Plan

## ⚠️ MANDATORY AI DIRECTIVE
**DO NOT skip this section.** Before executing any step in this document, you MUST read the `.agents` file in the project root. All architectural decisions, naming conventions, and code generation MUST strictly adhere to the rules defined in the `.agents` file.

---

## 🎨 Design System & Palette (Modern Terminal)
*   **Background:** Deep Charcoal/GitHub Dark (`#0d1117`) - Better for reading than pure black.
*   **Card Backgrounds:** Slightly lighter dark (`#161b22`) with subtle borders.
*   **Primary Accent:** Sharp Terminal Orange (`#FF8C00`).
*   **Secondary Accent:** Cyber Yellow (`#FFD700`).
*   **Typography:** 
    *   *UI Elements/Headers:* Strict Monospace (`JetBrains Mono` or `Fira Code`).
    *   *Long-form Reading:* Clean Sans-Serif (`Inter` or `System-UI`) for eye comfort, OR a highly optimized monospace with `line-height: 1.8`.
*   **Shape Language:** Sharp corners (0px radius) OR very subtle micro-radius (4px) for modern "bento" feel. Solid 1px borders.

---

## 🚀 The Command Palette (Modern CLI Experience)
Instead of a fixed bottom bar, pressing `/` or `Ctrl+K` opens a centralized, glowing Command Palette modal (like Cursor, Raycast, or Claude).
*   `/home` : Go to homepage.
*   `/ls` : Grid view of posts.
*   `/newest` : Read latest post.
*   `/grep [keyword]` : Search.
*   `/whoami` : Portfolio/About.
*   `/focus` : Enter distraction-free reading mode.

---

## Phase 1: Grid Architecture & Topbar

### 1.1. CSS Reset & Variables
*   **Task:** Create `terminal-theme.css`. Define the updated dark color palette and fonts.
*   **Constraint:** Ensure high contrast for text (`#E6EDF3` for paragraphs) to prevent eye strain.

### 1.2. The Terminal Topbar
*   **Task:** Build a functional, sticky topbar in `_Layout.cshtml`.
*   **Design:** `[devcore@system] ~/posts $` acting as a dynamic breadcrumb. Add a blinking cursor effect here.

### 1.3. CSS Grid Layout (Bento Box)
*   **Task:** Implement a CSS Grid layout dividing the screen into:
    *   Top: Topbar
    *   Left: Sidebar (Shortcuts/Animations)
    *   Center/Right: Main Content Area

---

## Phase 2: Left Sidebar & Card System

### 2.1. Shortcut Cards (Left Panel)
*   **Task:** Build a left sidebar containing actionable "Cards".
*   **Content:** Quick links to categories, GitHub links, or trigger buttons for UI animations (e.g., Matrix rain background toggle, Focus mode toggle).
*   **Design:** Each card has a `1px solid #30363d` border. On hover, the border glows `#FF8C00` and text turns `#FFD700`.

### 2.2. Blog Post Cards (Main Area)
*   **Task:** Refactor `Views/Home/Index.cshtml` to display posts as a grid of cards.
*   **Design:** 
    *   Header: Monospace date and category `[2026-08-09] [C#]`.
    *   Body: Post title in Orange, brief description.
    *   Interaction: Entire card is clickable. Minimalist hover transition.

---

## Phase 3: The Command Palette (JavaScript)

### 3.1. Command Modal Injection
*   **Task:** Add a hidden modal in `_Layout.cshtml` with a large input field and a results list below it.
*   **Design:** Glassmorphism dark background, heavy orange border.

### 3.2. Event Listener & Routing
*   **Task:** Create `wwwroot/js/command-palette.js`.
*   **Logic:** 
    *   Press `/` to open modal and focus input.
    *   Type commands. Filter available commands visually in the dropdown.
    *   Press `Enter` to execute. Backend routing handles dynamic commands like `/newest`.

---

## Phase 4: Reading Optimization (`/cat`)

### 4.1. Reading View Typography
*   **Task:** Refactor `Views/Home/Detail.cshtml`.
*   **Constraint:** Max-width 750px for the text container. Font size 17px, line height 1.8. 
*   **Design:** The background remains dark, but the text is soft. Code blocks get a distinct Terminal aesthetic (darker background, orange border). Remove sidebars dynamically when reading for maximum focus.

---
**END OF PLAN**
