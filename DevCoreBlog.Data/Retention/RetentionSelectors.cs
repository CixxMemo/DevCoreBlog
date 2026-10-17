namespace DevCoreBlog.Data.Retention;

/// <summary>Fixed SQL predicates shared by bounded inspection and mutation; no request text is interpolated.</summary>
internal static class RetentionSelectors
{
    internal const string Verification = "\"Digest\" IS NOT NULL AND greatest(\"ExpiresUtc\",\"ConsumedUtc\") < @now - interval '604800 seconds'";
    internal const string Session = "least(\"ExpiresUtc\",\"RevokedUtc\") < @now - interval '604800 seconds'";
    internal const string Payload = """
        "PayloadRedactedUtc" IS NULL AND "State" IN (3,4,6,7) AND "LeaseId" IS NULL AND "TerminalUtc" IS NOT NULL
        AND greatest("ExpiresUtc","TerminalUtc") < @now - interval '86400 seconds'
        AND "AttemptCount"=(SELECT count(*) FROM "TransactionalEmailAttempts" a WHERE a."JobId"=t."Id")
        AND NOT EXISTS(SELECT 1 FROM "TransactionalEmailAttempts" a WHERE a."JobId"=t."Id" AND (a."CompletedUtc" IS NULL OR a."CompletedUtc">@now OR a."Outcome"=3))
        """;
    internal const string Provider = "\"ReceivedUtc\" < @now - interval '7776000 seconds'";
    internal const string OldPending = "\"Status\"=0 AND greatest(\"CreatedUtc\",\"UpdatedUtc\") < @now - interval '604800 seconds'";
    internal const string Orphan = OldPending + """
         AND "Version"=1 AND "Suppression"=0 AND "VerifiedUtc" IS NULL AND "UnsubscribedUtc" IS NULL
        AND NOT EXISTS(SELECT 1 FROM "ProviderAddressSuppressions" p WHERE p."AddressKey"=t."AddressKey")
        AND NOT EXISTS(SELECT 1 FROM "TransactionalEmailJobs" j WHERE j."SubscriberId"=t."Id")
        AND NOT EXISTS(SELECT 1 FROM "SubscriberVerificationTokens" v WHERE v."SubscriberId"=t."Id")
        AND NOT EXISTS(SELECT 1 FROM "ReaderLoginTokens" l WHERE l."SubscriberId"=t."Id")
        AND NOT EXISTS(SELECT 1 FROM "ReaderSessions" s WHERE s."SubscriberId"=t."Id")
        AND NOT EXISTS(SELECT 1 FROM "NewsletterRecipients" r WHERE r."SubscriberId"=t."Id")
        """;
}
