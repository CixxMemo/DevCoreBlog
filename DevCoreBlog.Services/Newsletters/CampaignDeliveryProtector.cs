using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using DevCoreBlog.Services.Subscriptions;
using Microsoft.AspNetCore.DataProtection;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Operational attestation covers HTTPS one-click proxy privacy and DKIM header coverage; opt-in never manufactures that evidence.</summary>
public sealed class CampaignDeliveryReadiness(bool deliveryVerified, IEmailSubmissionContext submission, OneClickUnsubscriptionLinks links)
{
    public bool IsReady => deliveryVerified && submission.IsConfigured && links.HasHttpsOrigin;
}

/// <summary>Freezes wire content and exit proofs once; encrypted credential binding prevents a changed sender/key from replaying old work.</summary>
public sealed class CampaignDeliveryProtector(IDataProtectionProvider protection, IEmailSubmissionContext submission,
    IEmailSenderProfile profile, SubscriberExitLinks humanExits, OneClickUnsubscriptionLinks oneClick)
{
    private const int MaximumPlaintextBytes = 2_097_152;
    // This JSON is authenticated encrypted storage, never HTML/JS output. Avoid HTML escaping multiplying a valid rendered body beyond the envelope bound.
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8, AllowDuplicateProperties = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public bool MatchesSender(CampaignMessage message) => profile.Identity is { } sender
        && sender.Email == message.SenderEmail && sender.Name == message.SenderName && sender.ReplyTo == message.ReplyTo
        && profile.Contract == message.ProviderContract;

    public PreparedCampaignDispatch PrepareDispatch(CampaignMessage message, DateTime now)
    {
        TransactionalEmailRules.RequireUtc(now);now=ReaderIdentityRules.Utc(now);
        if (!submission.IsConfigured || submission.Binding is not {Length:64} binding || !MatchesSender(message) || !oneClick.HasHttpsOrigin)
            throw new InvalidOperationException("Campaign delivery configuration does not match its snapshot.");
        var expires=now+CampaignDeliveryRules.MaximumLifetime;
        var encoded=Protect(ContextProtector(message.Id),new DispatchPayload(1,message.Id,message.SnapshotHash,binding,oneClick.Origin,now,expires));
        return new(message.Id,message.SnapshotHash,encoded.Cipher,encoded.Hash,now,expires);
    }

    internal DispatchPayload? ReadDispatch(CampaignDispatch dispatch)
    {
        var context=Read<DispatchPayload>(ContextProtector(dispatch.CampaignId),dispatch.ProtectedContext,dispatch.ContextHash);
        return context is not null && context.Version==1 && context.CampaignId==dispatch.CampaignId
            && context.SnapshotHash==dispatch.SnapshotHash && context.CreatedUtc==dispatch.CreatedUtc && context.ExpiresUtc==dispatch.ExpiresUtc
            && context.Binding is {Length:64} && context.Origin==oneClick.Origin ? context : null;
    }

    internal PreparedCampaignDelivery Prepare(CampaignPreparation page, CampaignSelectedAddress address, DispatchPayload context, DateTime now)
    {
        now=ReaderIdentityRules.Utc(now);
        var id=Guid.NewGuid();
        var proof=new SubscriberExitProof(address.SubscriberId,address.PeriodId,SubscriberVerificationRules.AddressBinding(address.AddressKey));
        var human=humanExits.Create(proof);var header=oneClick.Create(proof);
        var html=page.Message.Html+"<p><a href=\""+HtmlEncoder.Default.Encode(human)+"\">Bülten aboneliğinden çık</a></p>";
        var text=page.Message.Text+"\n\nBülten aboneliğinden çık:\n"+human;
        _=new EmailSendRequest(address.EmailAddress,page.Message.Subject,html,text,Key(id),new DateTimeOffset(now),EmailPurpose.Newsletter,header);
        var payload=new DeliveryPayload(1,id,page.Message.Id,address.SubscriberId,address.SubscriberVersion,address.PeriodId,
            page.Message.SnapshotHash,address.EmailAddress,address.AddressKey,page.Message.Subject,html,text,header,context.Binding,context.ExpiresUtc);
        var encoded=Protect(JobProtector(id,page.Message.Id,address.SubscriberId),payload);
        return new(id,page.Message.Id,address.SubscriberId,address.SubscriberVersion,address.PeriodId,page.Dispatch.Version+1,encoded.Cipher,encoded.Hash,now,context.ExpiresUtc);
    }

    internal DeliveryPayload? ReadJob(CampaignDeliveryLease lease)
    {
        var value=Read<DeliveryPayload>(JobProtector(lease.Id,lease.CampaignId,lease.SubscriberId),lease.ProtectedPayload,lease.PayloadHash);
        if (value is null || value.Version!=1 || value.Id!=lease.Id || value.CampaignId!=lease.CampaignId || value.SubscriberId!=lease.SubscriberId
            || value.SubscriberVersion!=lease.SubscriberVersion || value.PeriodId!=lease.PeriodId || value.ExpiresUtc!=lease.ExpiresUtc
            || value.SnapshotHash!=lease.SnapshotHash || value.Binding is not {Length:64}
            || !SubscriberAddress.TryCreate(value.Recipient,out var address) || address?.Key!=value.AddressKey) return null;
        try { _=new EmailSendRequest(value.Recipient,value.Subject,value.Html,value.Text,Key(lease.Id),DateTimeOffset.UnixEpoch,EmailPurpose.Newsletter,value.HeaderExit); }
        catch(ArgumentException) { return null; }
        return value;
    }
    internal static string Key(Guid id)=>"nl/"+id.ToString("N");
    private IDataProtector ContextProtector(Guid id)=>protection.CreateProtector("AetherNotes.CampaignDispatch.v1",id.ToString("N"));
    private IDataProtector JobProtector(Guid id,Guid campaign,Guid subscriber)=>protection.CreateProtector("AetherNotes.CampaignDelivery.v1",id.ToString("N"),campaign.ToString("N"),subscriber.ToString("N"));
    private static (string Cipher,string Hash) Protect<T>(IDataProtector protector,T value)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,JsonOptions);
        if(bytes.Length>MaximumPlaintextBytes)throw new ArgumentException("Campaign envelope exceeds its bound.");
        return (Convert.ToBase64String(protector.Protect(bytes)),Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static T? Read<T>(IDataProtector protector,string cipher,string hash)
    {
        try
        {
            if(cipher.Length>CampaignDeliveryRules.MaximumProtectedBytes)return default;
            var bytes=protector.Unprotect(Convert.FromBase64String(cipher));
            if(bytes.Length>MaximumPlaintextBytes || Convert.ToHexString(SHA256.HashData(bytes))!=hash)return default;
            return JsonSerializer.Deserialize<T>(bytes,JsonOptions);
        }
        catch(Exception e) when(e is CryptographicException or FormatException or JsonException or ArgumentException){return default;}
    }
    internal sealed record DispatchPayload(int Version,Guid CampaignId,string SnapshotHash,string Binding,string Origin,DateTime CreatedUtc,DateTime ExpiresUtc)
    {public override string ToString()=>nameof(DispatchPayload);}
    internal sealed record DeliveryPayload(int Version,Guid Id,Guid CampaignId,Guid SubscriberId,long SubscriberVersion,Guid PeriodId,
        string SnapshotHash,string Recipient,string AddressKey,string Subject,string Html,string Text,string HeaderExit,string Binding,DateTime ExpiresUtc)
    {public override string ToString()=>nameof(DeliveryPayload);}
}
