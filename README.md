# DevCoreBlog

**DevCoreBlog** is a high-performance, SEO-friendly, and modern blog infrastructure built with **ASP.NET Core MVC (.NET 10)**. Designed for developers and content creators, this project incorporates enterprise-grade software engineering practices—such as the Repository Pattern, Dependency Injection, and an N-Tier Architecture—providing a reliable and highly scalable foundation.

---

## 🏛 Architecture

The project is built upon a strict **N-Tier (Multi-Layered) Architecture**, adhering to S.O.L.I.D. principles to ensure high maintainability and a clear separation of concerns. The solution consists of 4 main layers:

1. **`DevCoreBlog.Core` (Shared Layer):** 
   The heart of the application. It contains Domain Entities (which map to database tables) and all service/repository contracts (Interfaces). This is the base layer upon which all other layers depend. It adheres strictly to the Dependency Inversion principle by having **zero dependencies** on external libraries.

2. **`DevCoreBlog.Data` (Data Access Layer):** 
   Responsible for database operations and data persistence. It houses the Entity Framework Core (PostgreSQL) `DbContext` configurations, database migrations, and the implementation of the **Repository Pattern**. It abstracts raw database operations away from the rest of the application.

3. **`DevCoreBlog.Services` (Business Logic Layer):** 
   The operational center where all business rules are enforced. It acts as a bridge between the Web UI and the Data layer, ensuring that Controllers never interact directly with the database. Operations such as Caching, CDN integration, auto-slug generation, and data validation reside entirely in this layer.

4. **`DevCoreBlog` (Web/UI Layer):** 
   The presentation layer containing Razor Views (`.cshtml`) and HTTP Controllers. It follows the "Thin Controller" pattern by simply receiving HTTP requests and delegating them directly to the `Services` layer. 

---

## ✨ Key Features

* 🚀 **Cloudinary CDN Integration:** 
  Images are hosted on Cloudinary's global Content Delivery Network rather than the local server. This drastically saves server bandwidth, disk space, and ensures lightning-fast image delivery to end-users worldwide.
  
* ⚡ **In-Memory Caching:** 
  The homepage and frequently accessed lists are cached using `.NET IMemoryCache`. This eliminates database fatigue and drops page response times down to milliseconds. A custom **Cache Invalidation** mechanism automatically clears specific caches whenever an admin creates, updates, or deletes a post, guaranteeing that the data is always fresh.

* 🛡️ **Advanced Security Measures:** 
  * **Anti Over-Posting:** Complete protection against over-posting vulnerabilities is achieved via strict `[Bind]` attributes applied at the Controller level. This ensures that unauthorized fields (like auto-generated slugs) cannot be manipulated by malicious requests.
  * **CSRF Protection:** All form submissions are protected via `[ValidateAntiForgeryToken]`.

* 🛠️ **Global Exception Handling:** 
  A comprehensive `ExceptionHandlingMiddleware` eliminates the need for messy `try-catch` blocks across controllers. All runtime errors are caught in a single centralized location, securely logged, and users are gracefully redirected to a standard, user-friendly HTTP 500 Error View.

* 📝 **Tech Minimal UI & Clean Code:** 
  The frontend is styled using Tailwind CSS with a strict "Tech Minimal" design philosophy (sharp edges, high contrast, clean layouts without excessive shadows or gradients). The C# codebase is heavily commented with junior-friendly explanations, serving as an educational resource as well as a production-ready application.

---

## 🛠 Tech Stack

- **Framework:** ASP.NET Core MVC (.NET 10.0)
- **ORM:** Entity Framework Core 10
- **Database:** PostgreSQL
- **Frontend:** Razor Views + Tailwind CSS
- **Authentication:** Cookie Authentication (`Microsoft.AspNetCore.Authentication.Cookies`)

---

## 🚀 Installation & Setup

Follow these steps to set up the development environment locally:

### 1. Configure Environment Variables
Copy the `.env.example` file located in the root directory and rename it to `.env`. Fill in your PostgreSQL database connection string and your Cloudinary API credentials:

```env
DB_CONNECTION_STRING=Host=localhost;Database=devcoreblog;Username=postgres;Password=your_password
CLOUDINARY_CLOUD_NAME=your_cloud_name
CLOUDINARY_API_KEY=your_api_key
CLOUDINARY_API_SECRET=your_api_secret
ADMIN_USERNAME=admin
ADMIN_PASSWORD=admin
```

### 2. Database Migrations
To apply the required tables to your PostgreSQL database, use the EF Core CLI to run the migrations. Since the data layer is separated, you must specify the startup project and the target project:

```bash
dotnet ef database update --project DevCoreBlog.Data --startup-project DevCoreBlog
```

### 3. Run the Application
Compile and run the project using the .NET CLI from the root directory:

```bash
dotnet run --project DevCoreBlog
```

Once the application starts, navigate to the URL provided in your terminal (typically `http://localhost:5159`) to view the blog. 

---
*DevCoreBlog — Clean code, modern architecture, uncompromised performance.*
