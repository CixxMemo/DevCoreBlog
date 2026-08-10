Sen kıdemli bir ASP.NET Core MVC sistem mimarısın. DevCoreBlog projesinde kod tekrarını önlemek ve güvenlik standartlarını profesyonel seviyeye çıkarmak için aşağıdaki refactoring adımlarını uygulamanı istiyorum.

GÜVENLİK VE KURALLAR (Kritik):

İşleme başlamadan önce .agents/AGENTS.md dosyasını baştan sona oku. Bu görevlerin oradaki kurallarla çelişmediğini teyit et. Çelişen bir durum varsa işlemi durdur ve bana açıkla.

Projeye zarar vermemek için, kodlarda hiçbir değişiklik yapmadan önce terminalde git checkout -b feature/shared-and-env-setup komutunu çalıştırarak yeni bir branch aç.

Dosyaları doğrudan değiştirme. Önce hangi dosyada ne değişiklik yapacağını (diff formatında) bana göster ve devam etmek için onayımı bekle.

Görev 1: Shared Mimarisinin Kurulması

Proje ana dizininde Shared adında yeni bir klasör oluştur.

Mevcut Helpers klasörünü (MarkdownHelper.cs ve SlugGenerator.cs dosyalarıyla birlikte) bu Shared klasörünün içine taşı. Yeni dizin Shared/Helpers olmalı.

Taşıdığın bu dosyaların namespace tanımlarını namespace DevCoreBlog.Shared.Helpers olarak güncelle.

Projedeki tüm Controller ve Service dosyalarını tara. Eski using DevCoreBlog.Helpers; tanımlarını bularak using DevCoreBlog.Shared.Helpers; olarak değiştir.

Görev 2: .env Entegrasyonu ve Güvenlik

Terminal üzerinden projeye DotNetEnv paketini kuracak komutu çalıştır.

Proje kök dizininde .env adında bir dosya oluştur. appsettings.json içindeki veritabanı bağlantı cümleni (Connection String) alarak .env dosyasına DB_CONNECTION_STRING=senin_baglanti_cumlen formatında ekle.

.gitignore dosyasını aç ve en altına yeni bir satır olarak .env yaz.

Program.cs dosyasını aç. var builder = WebApplication.CreateBuilder(args); satırını bul ve hemen altına çevre değişkenlerini sisteme yüklemek için DotNetEnv.Env.Load(); kodunu ekle.

Yine Program.cs içinde, AddDbContext yapılandırmasını bul ve veritabanı bağlantı dizesini şu şekilde .env dosyasından almasını sağla: var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");