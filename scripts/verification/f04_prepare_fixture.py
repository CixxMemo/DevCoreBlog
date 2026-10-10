#!/usr/bin/env python3
"""Add a protected access-change adapter only to the owned disposable HTTP source copy."""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, required=True)
source = parser.parse_args().source.resolve()
assert source.name == 'source' and source.parent.name.startswith('devcoreblog-f17.')
assert source.parent.parent in (Path('/tmp'), Path('/private/tmp')) and not (source / '.git').exists()
target = source / 'Controllers/F04FixtureAccessController.cs'
assert not target.exists(), 'Never replace an existing controller'
target.write_text('''using DevCoreBlog.Core.Entities;
using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// Owned test-copy-only adapter: the real application exposes no classification endpoint yet.
[Authorize]
public sealed class F04FixtureAccessController(IPostService posts) : Controller
{
    [HttpPost("fixture-access/hide-control")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hide(CancellationToken token)
    {
        var stored = await posts.GetPostByIdAsync(4010);
        if (stored is null) return NotFound();
        var input = new Post
        {
            Id = stored.Id, Title = stored.Title, Content = stored.Content,
            Summary = stored.Summary, Excerpt = stored.Excerpt, CategoryId = stored.CategoryId,
            IsActive = stored.IsActive, IsPublished = stored.IsPublished, PublishDate = stored.PublishDate,
            ThumbnailUrl = stored.ThumbnailUrl, ThumbnailPublicId = stored.ThumbnailPublicId,
            ThumbnailWidth = stored.ThumbnailWidth, ThumbnailHeight = stored.ThumbnailHeight,
            ThumbnailAlt = stored.ThumbnailAlt, ContentKind = stored.ContentKind,
            AccessScope = PostAccessScope.Subscribers
        };
        var result = await posts.UpdatePostAsync(input, token, stored.EditVersion);
        return result.IsValid ? Ok() : StatusCode(result.IsConflict ? 409 : 400);
    }
}
''')
