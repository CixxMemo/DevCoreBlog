using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Commits the post and receipt together; PostgreSQL arbitrates concurrent keys.</summary>
public sealed class WebhookPostRepository(ApplicationDbContext context) : IWebhookPostRepository
{
    public Task<WebhookReceipt?> FindAsync(string key, CancellationToken cancellationToken) =>
        context.WebhookReceipts.AsNoTracking().SingleOrDefaultAsync(
            receipt => receipt.Key == key, cancellationToken);

    public async Task<WebhookInsertResult?> TryCreateAsync(
        Post post, string key, string payloadHash, CancellationToken cancellationToken)
    {
        var receipt = new WebhookReceipt
        {
            Key = key,
            PayloadHash = payloadHash,
            CreatedAt = post.CreatedDate,
            Title = post.Title,
            Slug = post.Slug,
            IsPublished = post.IsPublished,
            PublishDate = post.PublishDate,
            Post = post
        };
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            context.Posts.Add(post);
            await context.SaveChangesAsync(cancellationToken);
            receipt.CreatedPostId = post.Id;
            context.WebhookReceipts.Add(receipt);
            await context.SaveChangesAsync(cancellationToken);
            // Use persisted precision for both the first response and later replays.
            await context.Entry(receipt).ReloadAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(receipt, true);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_WebhookReceipts" or "IX_Posts_Slug"
            } postgresException)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.Entry(receipt).State = EntityState.Detached;
            context.Entry(post).State = EntityState.Detached;
            post.Id = 0;
            if (postgresException.ConstraintName == "IX_Posts_Slug")
            {
                return null;
            }

            var winner = await FindAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("Committed webhook receipt was not found.");
            return new(winner, false);
        }
    }
}
