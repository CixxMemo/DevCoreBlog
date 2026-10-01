from http.server import BaseHTTPRequestHandler, HTTPServer
from html.parser import HTMLParser
from urllib.request import urlopen
from urllib.error import HTTPError
class StripScripts(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=False); self.out=[]; self.script=False
    def handle_starttag(self,tag,attrs):
        if tag=='script': self.script=True
        elif not self.script: self.out.append(self.get_starttag_text())
    def handle_endtag(self,tag):
        if tag=='script': self.script=False
        elif not self.script: self.out.append('</'+tag+'>')
    def handle_data(self,data):
        if not self.script: self.out.append(data)
    def handle_entityref(self,name):
        if not self.script: self.out.append('&'+name+';')
    def handle_charref(self,name):
        if not self.script: self.out.append('&#'+name+';')
    def handle_decl(self,decl): self.out.append('<!'+decl+'>')
class Proxy(BaseHTTPRequestHandler):
    def do_GET(self):
        try: response=urlopen('http://127.0.0.1:15169'+self.path)
        except HTTPError as err: response=err
        data=response.read(); content_type=response.headers.get('Content-Type','application/octet-stream')
        if 'text/html' in content_type:
            parser=StripScripts(); parser.feed(data.decode()); data=''.join(parser.out).encode()
        self.send_response(response.status); self.send_header('Content-Type',content_type)
        self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    def log_message(self,*args): pass
HTTPServer(('127.0.0.1',15170),Proxy).serve_forever()
