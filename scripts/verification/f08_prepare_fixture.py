#!/usr/bin/env python3
"""Inject a protected document adapter exclusively into the runner's owned disposable source."""
import argparse
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--source', type=Path, required=True)
source = p.parse_args().source.resolve()
assert source.name == 'source' and source.parent.name.startswith('devcoreblog-f17.')
assert source.parent.parent in (Path('/tmp'), Path('/private/tmp')) and not (source / '.git').exists()
target = source / 'Controllers/F08FixtureDocumentController.cs'
assert not target.exists()
target.write_text('''using DevCoreBlog.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// Test-only HTTP translation adapter. F09 will implement the actual editor form boundary.
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class F08FixtureDocumentController(PostDocumentService documents) : Controller
{
    [HttpPost("fixture-document/{id:int}")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(3211264)]
    [RequestFormLimits(ValueLengthLimit = 3211264, ValueCountLimit = 8)]
    public async Task<IActionResult> Save(int id, long expectedEditVersion, string documentJson, CancellationToken token)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(documentJson)) return BadRequest();
        var result = await documents.SaveAsync(id, expectedEditVersion, documentJson, token);
        return result.Status switch
        {
            PostDocumentSaveStatus.Saved => Json(new { result.EditVersion }),
            PostDocumentSaveStatus.NotFound => NotFound(),
            PostDocumentSaveStatus.Conflict => StatusCode(409),
            PostDocumentSaveStatus.InvalidDocument when result.Error?.Kind == DocumentFailureKind.Limit => StatusCode(413),
            _ => BadRequest()
        };
    }
}
''')
