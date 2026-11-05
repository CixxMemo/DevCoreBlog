#!/usr/bin/env python3
"""Open the shelved email paths in a disposable copy so older stages can keep testing that dormant code.
The repository literal stays closed; a changed literal makes this fail instead of testing the wrong build."""
from pathlib import Path

CLOSED = 'public static bool Suspended => true;'
OPENED = 'public static bool Suspended => false;'


def switch_file(source):
    return Path(source) / 'DevCoreBlog.Core/Email/EmailPaths.cs'


def open_dormant_email_paths(source):
    path = switch_file(source)
    text = path.read_text()
    if text.count(CLOSED) != 1:
        raise RuntimeError('Email paths are not closed in the source copy')
    path.write_text(text.replace(CLOSED, OPENED))
