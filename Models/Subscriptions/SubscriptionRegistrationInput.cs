using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DevCoreBlog.Models.Subscriptions;

/// <summary>Only the email field binds from the public form; lifecycle and identity are server-owned.</summary>
public sealed class SubscriptionRegistrationInput
{
    [Required(ErrorMessage = "E-posta adresinizi girin.")]
    [StringLength(254, ErrorMessage = "E-posta adresi en fazla 254 karakter olabilir.")]
    [JsonIgnore]
    public string Email { get; set; } = string.Empty;
    public override string ToString() => nameof(SubscriptionRegistrationInput);
}

public sealed class SubscriptionRegistrationPage
{
    public bool Available { get; init; }
    public bool Accepted { get; init; }
    public bool Failed { get; init; }
    public SubscriptionRegistrationInput Input { get; init; } = new();
}
