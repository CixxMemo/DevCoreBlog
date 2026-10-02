#!/usr/bin/env python3
"""Serve synthetic code and the current module with clipboard explicitly denied."""
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

class Fixture(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/article-reading.js':
            body = Path(__file__).resolve().parents[2].joinpath('wwwroot/js/article-reading.js').read_bytes()
            mime = 'text/javascript'
        elif self.path == '/':
            body = b'''<!doctype html><html lang="en"><meta charset="utf-8"><title>F39 clipboard denial fixture</title>
<article class="reading-article"><h1>Clipboard denied test</h1><div class="markdown-content">
<pre><code>Console.WriteLine("synthetic copy");</code></pre></div></article>
<script type="module" src="/article-reading.js"></script></html>'''
            mime = 'text/html'
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header('Content-Type', mime)
        self.send_header('Permissions-Policy', 'clipboard-write=()')
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass

HTTPServer(('127.0.0.1', 15173), Fixture).serve_forever()
