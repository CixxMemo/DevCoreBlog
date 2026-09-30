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
            "https://images.example.test/f30.png", providerFormat, 1, 1));
    }
}
''')
