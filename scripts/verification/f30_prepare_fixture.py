#!/usr/bin/env python3
"""Replace storage only in the disposable source copy; retain the real upload policy."""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, required=True)
args = parser.parse_args()
source = args.source.resolve()
if not str(source).startswith('/private/tmp/devcoreblog-f17.') and not str(source).startswith('/tmp/devcoreblog-f17.'):
    raise SystemExit('Expected the disposable F17 source directory.')
if source.name != 'source' or (source / '.git').exists():
    raise SystemExit('Refusing to modify a repository checkout.')
program = source / 'Program.cs'
text = program.read_text()
registration = 'builder.Services.AddScoped<IImageStorage, CloudinaryImageStorage>();'
if text.count(registration) != 1:
    raise SystemExit('Expected the existing image registration exactly once.')
program.write_text(text.replace(registration, 'builder.Services.AddScoped<IImageStorage, F30FixtureStorage>();'))
(source / 'F30FixtureStorage.cs').write_text('''using DevCoreBlog.Services.Images;

// Test-copy-only storage result; does not contact or verify Cloudinary.
internal sealed class F30FixtureStorage : IImageStorage
{
    public Task<ImageStorageOutcome> UploadAsync(Stream stream, string providerFormat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ImageStorageOutcome.Success(
            "https://images.example.test/f30.png", providerFormat, 1, 1, "f30-fixture"));
    }
}
''')

# F49 uses distinct synthetic IDs and one deterministic provider failure, only in the disposable copy.
import os
if os.environ.get('DEVCORE_F49_PROBE') == '1':
    fixture = source / 'F30FixtureStorage.cs'
    text = fixture.read_text().replace('return Task.FromResult(ImageStorageOutcome.Success(',
        'if (providerFormat == "gif") return Task.FromResult(ImageStorageOutcome.Failure());\n        var id = "DevCoreBlog/f49-" + Guid.NewGuid().ToString("N");\n        return Task.FromResult(ImageStorageOutcome.Success(')
    text = text.replace('"https://images.example.test/f30.png", providerFormat, 1, 1, "f30-fixture"',
        '"https://images.example.test/" + id + ".png", providerFormat, 1, 1, id')
    text = text.replace('public Task<ImageStorageOutcome> UploadAsync', 'public async Task<ImageStorageOutcome> UploadAsync')
    text = text.replace('return Task.FromResult(ImageStorageOutcome.Failure());', 'return ImageStorageOutcome.Failure();')
    text = text.replace('return Task.FromResult(ImageStorageOutcome.Success(',
        'await File.AppendAllTextAsync(Path.Combine(AppContext.BaseDirectory, "f49-provider-journal.jsonl"),\n            System.Text.Json.JsonSerializer.Serialize(new { publicId = id, urls = new[] { "https://images.example.test/" + id + ".png" } }) + "\\n", cancellationToken);\n        return ImageStorageOutcome.Success(')
    text = text.replace('providerFormat, 1, 1, id));', 'providerFormat, 1, 1, id);')
    fixture.write_text(text)
