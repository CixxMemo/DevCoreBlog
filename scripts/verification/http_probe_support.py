"""Small HTTP helpers shared by the isolated security probes."""

from __future__ import annotations

import html
import http.cookiejar
import re
import secrets
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request


AUTHENTICATION_COOKIE_NAME = "DevCoreBlog.Admin"


def cookie_opener(*, allow_untrusted_https: bool = False):
    cookies = http.cookiejar.CookieJar()
    handlers = [urllib.request.HTTPCookieProcessor(cookies)]
    if allow_untrusted_https:
        context = ssl.create_default_context()
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        handlers.append(urllib.request.HTTPSHandler(context=context))
    opener = urllib.request.build_opener(*handlers)
    return opener, cookies


def request(
    opener,
    url: str,
    *,
    data: dict[str, str] | None = None,
    raw_data: bytes | None = None,
    headers: dict[str, str] | None = None,
):
    status, final_url, body, _ = request_with_headers(
        opener,
        url,
        data=data,
        raw_data=raw_data,
        headers=headers,
    )
    return status, final_url, body


def request_with_headers(
    opener,
    url: str,
    *,
    data: dict[str, str] | None = None,
    raw_data: bytes | None = None,
    headers: dict[str, str] | None = None,
):
    if data is not None and raw_data is not None:
        raise ValueError("Use form data or raw data, not both.")

    encoded = urllib.parse.urlencode(data).encode() if data is not None else raw_data
    method = "POST" if encoded is not None else "GET"
    req = urllib.request.Request(url, data=encoded, headers=headers or {}, method=method)
    try:
        with opener.open(req, timeout=5) as response:
            return (
                response.status,
                response.geturl(),
                response.read().decode("utf-8", "replace"),
                dict(response.headers.items()),
            )
    except urllib.error.HTTPError as error:
        return (
            error.code,
            error.geturl(),
            error.read().decode("utf-8", "replace"),
            dict(error.headers.items()),
        )


def multipart_payload(
    fields: dict[str, str],
    *,
    file_field: str,
    file_name: str,
    content_type: str,
    content: bytes,
) -> tuple[bytes, str]:
    """Build a small multipart body for upload probes without external packages."""
    boundary = f"----DevCoreBlogProbe{secrets.token_hex(12)}"
    chunks: list[bytes] = []
    for name, value in fields.items():
        chunks.extend(
            [
                f"--{boundary}\r\n".encode(),
                f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode(),
                value.encode(),
                b"\r\n",
            ]
        )

    chunks.extend(
        [
            f"--{boundary}\r\n".encode(),
            (
                f'Content-Disposition: form-data; name="{file_field}"; '
                f'filename="{file_name}"\r\n'
            ).encode(),
            f"Content-Type: {content_type}\r\n\r\n".encode(),
            content,
            b"\r\n",
            f"--{boundary}--\r\n".encode(),
        ]
    )
    return b"".join(chunks), f"multipart/form-data; boundary={boundary}"


def post_file(
    opener,
    url: str,
    token: str,
    *,
    file_field: str,
    file_name: str,
    content_type: str,
    content: bytes,
    fields: dict[str, str] | None = None,
):
    """Submit a multipart form with the antiforgery token in both supported locations."""
    form_fields = dict(fields or {})
    form_fields["__RequestVerificationToken"] = token
    body, multipart_content_type = multipart_payload(
        form_fields,
        file_field=file_field,
        file_name=file_name,
        content_type=content_type,
        content=content,
    )
    return request(
        opener,
        url,
        raw_data=body,
        headers={
            "Content-Type": multipart_content_type,
            "X-CSRF-TOKEN": token,
        },
    )


def has_authentication_cookie(cookies: http.cookiejar.CookieJar) -> bool:
    return any(cookie.name == AUTHENTICATION_COOKIE_NAME for cookie in cookies)


def extract_antiforgery_token(body: str) -> str | None:
    match = re.search(
        r'<input[^>]+name="__RequestVerificationToken"[^>]+value="([^"]+)"',
        body,
        re.IGNORECASE,
    )
    return html.unescape(match.group(1)) if match else None


def get_antiforgery_token(opener, url: str) -> tuple[int, str | None, str]:
    status, _, body = request(opener, url)
    return status, extract_antiforgery_token(body), body


def submit_login(
    opener,
    base_url: str,
    username: str,
    password: str | None,
    *,
    include_password: bool = True,
):
    get_status, token, _ = get_antiforgery_token(opener, f"{base_url}/Account/Login")
    form_data = {
        "username": username,
        "__RequestVerificationToken": token or "",
    }
    if include_password:
        form_data["password"] = password or ""

    status, final_url, body = request(
        opener,
        f"{base_url}/Account/Login",
        data=form_data,
    )
    return get_status, token, status, final_url, body


def wait_until_ready(opener, url: str, marker: str, timeout_seconds: int) -> None:
    deadline = time.monotonic() + timeout_seconds
    last_error = "server did not respond"
    while time.monotonic() < deadline:
        try:
            status, _, body = request(opener, url)
            if status == 200 and marker in body:
                return
            last_error = f"status={status}, marker={marker in body}"
        except (OSError, urllib.error.URLError) as error:
            last_error = str(error)
        time.sleep(0.25)
    raise RuntimeError(f"F01 server was not ready: {last_error}")
