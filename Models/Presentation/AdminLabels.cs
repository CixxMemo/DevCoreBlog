using DevCoreBlog.Core.Publishing;

namespace DevCoreBlog.Models.Presentation;

/// <summary>Translates display values without changing stored enums or operational state codes.</summary>
public static class AdminLabels
{
    public static string Publication(PostPublicationState state) => state switch
    {
        PostPublicationState.Draft => "Taslak",
        PostPublicationState.Scheduled => "Planlandı",
        PostPublicationState.Published => "Yayımlandı",
        _ => "Etkin değil"
    };

    public static string Database(string state) => state switch
    {
        "Verified" => "Doğrulandı",
        "Timed out" => "Zaman aşımı",
        _ => "Kullanılamıyor"
    };

    public static string Environment(string name) => name switch
    {
        "Development" => "Geliştirme",
        "Staging" => "Deneme",
        "Production" => "Canlı",
        _ => "Özel ortam"
    };
}
