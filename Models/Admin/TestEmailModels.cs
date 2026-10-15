using System.ComponentModel.DataAnnotations;
using DevCoreBlog.Services.Email;

namespace DevCoreBlog.Models.Admin;

/// <summary>The HTTP form carries one document, subject and intent ID; recipient/sender/provider fields are deliberately absent.</summary>
public sealed class TestEmailInput
{
    public Guid RequestId { get; set; }
    [Required(ErrorMessage = "Test için içerik belgesi gereklidir.")]
    public string DocumentJson { get; set; } = "";
    [Required(ErrorMessage = "E-posta konusunu yazın.")]
    [StringLength(TestEmailMessageFactory.MaximumSubjectLength, ErrorMessage = "Konu en fazla 200 karakter olabilir.")]
    public string Subject { get; set; } = TestEmailMessageFactory.DefaultSubject;
}
public sealed record TestEmailComposeModel(TestEmailInput Input, bool Ready, string? Recipient);
public sealed record TestEmailResultModel(TestEmailResult Result);
