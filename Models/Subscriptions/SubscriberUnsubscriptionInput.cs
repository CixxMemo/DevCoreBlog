using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DevCoreBlog.Models.Subscriptions;

/// <summary>Only the bounded encrypted proof may be supplied; never echo it in a response.</summary>
public sealed class SubscriberUnsubscriptionInput
{
    [Required, StringLength(512, MinimumLength = 100)]
    [JsonIgnore]
    public string Token { get; set; } = string.Empty;
    public override string ToString() => nameof(SubscriberUnsubscriptionInput);
}

public enum SubscriberUnsubscriptionPage { Ready, Unsubscribed, Invalid, Unavailable }
