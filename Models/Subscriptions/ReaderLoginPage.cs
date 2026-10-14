using System.ComponentModel.DataAnnotations;

namespace DevCoreBlog.Models.Subscriptions;

public sealed class ReaderLoginInput
{
    [Required(ErrorMessage = "E-posta adresinizi girin.")]
    [StringLength(254, ErrorMessage = "E-posta adresi en fazla 254 karakter olabilir.")]
    public string? Email { get; set; }
}
public sealed class ReaderLoginProof
{
    [Required, StringLength(43, MinimumLength = 43)]
    public string? Token { get; set; }
    [StringLength(512)]
    public string? ReturnUrl { get; set; }
}
public sealed class ReaderLoginPage
{
    public bool Available { get; init; }
    public bool Accepted { get; init; }
    public bool Failed { get; init; }
    public string? Error { get; init; }
}
public enum ReaderLoginConfirmation { Ready, Invalid, Unavailable }
