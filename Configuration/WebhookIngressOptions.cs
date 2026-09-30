namespace DevCoreBlog.Configuration;

/// <summary>Holds the environment-only webhook credential and publication permission.</summary>
public sealed class WebhookIngressOptions
{
    public const long MaximumRequestBodyBytes = 2_097_152;

    public WebhookIngressOptions(string? secret, bool allowPublish)
    {
        Secret = secret;
        AllowPublish = allowPublish;
    }

    public string? Secret { get; }

    public bool AllowPublish { get; }
}
