# DevCoreBlog - AI Agent Behavior Rules

This file defines the strict constraints, architecture rules, and coding standards for the DevCoreBlog project. ALL AI agents MUST adhere to these rules implicitly in every interaction for this workspace.

## 1. TECH STACK (Strict Constraints)
- **Framework:** ASP.NET Core MVC (net10.0)
- **ORM:** Entity Framework Core 10
- **Database:** PostgreSQL
- **Frontend:** Razor Views (.cshtml) + Tailwind CSS.
- **Authentication:** Cookie Authentication (`Microsoft.AspNetCore.Authentication.Cookies`).

## 2. ARCHITECTURE & BOUNDARIES (Do NOT Violate)
- **Architecture:** N-Tier folder structure within a single project (`/Core`, `/Repositories`, `/Services`, `/Controllers`).
- **NO SPAs / NO APIs:** Do NOT suggest or write React, Vue, Angular, or Web APIs. This is a purely Server-Side rendered MVC application.
- **NO ASP.NET Core Identity:** Stick to the existing simple cookie authentication implementation. Do not add complex Identity, Role, or Claim setups.
- **NO DTOs / NO AutoMapper:** Services must return Domain Entities directly to controllers. Do not overcomplicate the data transfer.
- **NO Hallucinations:** Do not assume the existence of NuGet packages, external libraries, or complex features that are not explicitly present in the codebase. Stick strictly to the existing stack.

## 3. CODING STANDARDS & BEHAVIOR
- **Conciseness (No Unnecessarily Long Code):** Write clean, DRY (Don't Repeat Yourself), and direct code. Avoid over-engineering. **Do not write unnecessarily long, bloated, or overly clever code.** If a simple LINQ query or basic `if` block works, use it. Keep methods short and focused.
- **Junior-Friendly Comments:** All C# classes, interfaces, and complex logic MUST have English comments (`//`) explaining *what* the code does and *why* it is written that way. Think of it as a tutorial for a junior developer.
- **Tech Minimal Design:** For CSS/UI, use a "Tech Minimal" approach. Use sharp edges, high contrast, and clean layouts. **BANNED UI ELEMENTS:** No excessive shadows (`shadow-2xl`), no glassmorphism (`backdrop-blur`), no neon gradients, and no overly rounded corners (`rounded-3xl`).

## 4. EXECUTION WORKFLOW
- **Explain What You Did:** After writing or modifying code, provide a brief, easy-to-understand summary in the chat explaining what was changed and the reasoning behind it.
- **Safety First:** If a user request contradicts these rules, warn the user before proceeding.
