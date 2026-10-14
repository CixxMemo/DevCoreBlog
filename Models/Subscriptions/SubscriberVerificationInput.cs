using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DevCoreBlog.Models.Subscriptions;

/// <summary>Bounded form proof; never serialized or echoed back into validation output.</summary>
public sealed class SubscriberVerificationInput
{
    [Required, StringLength(43, MinimumLength = 43)]
    [JsonIgnore]
    public string Token { get; set; } = string.Empty;
    public override string ToString() => nameof(SubscriberVerificationInput);
}

public enum SubscriberVerificationPage { Ready, Confirmed, Invalid, Unavailable }
