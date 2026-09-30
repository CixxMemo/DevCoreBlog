#!/usr/bin/env python3
"""Verify explicit MVC publication intent against disposable PostgreSQL and anonymous HTTP."""
import argparse
from datetime import datetime, timedelta, timezone
import json
import os
import re
import subprocess
import uuid
from zoneinfo import ZoneInfo
from http_probe_support import (cookie_opener, submit_login, request,
    extract_antiforgery_token, extract_hidden_value)
from f11_optional_thumbnail_probe import post_fields

parser = argparse.ArgumentParser()
parser.add_argument('--base-url', default='http://127.0.0.1:15165')
parser.add_argument('--pg-port', default='55438')
args = parser.parse_args()
base = args.base_url.rstrip('/')
opener, _ = cookie_opener()
_, _, status, url, _ = submit_login(opener, base,
    os.environ.get('DEVCORE_TEST_ADMIN_USERNAME', 'f17-admin'), os.environ['DEVCORE_TEST_ADMIN_PASSWORD'])
assert status == 200 and '/Admin/Dashboard' in url
anon, _ = cookie_opener()
checks = {}
site = ZoneInfo('Europe/Istanbul')
future = (datetime.now(site) + timedelta(days=2)).replace(microsecond=0)
past = '2026-09-01T12:00:00'
prefix = 'F32_' + uuid.uuid4().hex[:8]

def database(title):
    sql = '''SELECT json_build_object('id', "Id", 'slug', "Slug", 'published', "IsPublished",
        'active', "IsActive", 'date', "PublishDate", 'version', "EditVersion")
        FROM "Posts" WHERE "Title" = :'marker';'''
    output = subprocess.check_output(['psql', '-X', '-h', '127.0.0.1', '-p', args.pg_port,
        '-U', os.environ['USER'], '-d', 'devcoreblog_f01_test', '-At', '-v', 'ON_ERROR_STOP=1',
        '-v', 'marker=' + title], input=sql.encode()).decode().strip()
    return json.loads(output) if output else None

def submit(title, action, *, post_id=None, **overrides):
    path = '/AdminPost/Create' if post_id is None else f'/AdminPost/Edit/{post_id}'
    _, _, page = request(opener, base + path)
    fields = post_fields(title, title + ' content retained', post_id=post_id)
    fields.update(__RequestVerificationToken=extract_antiforgery_token(page),
        SaveAction=action, PublishDate=future.strftime('%Y-%m-%dT%H:%M:%S'))
    if post_id:
        fields['EditVersion'] = extract_hidden_value(page, 'EditVersion')
    fields.update(overrides)
    s, u, body = request(opener, base + path, data=fields)
    return s, u, body

def row_state(record, expected, visible):
    _, _, index = request(opener, base + '/AdminPost')
    row = re.search(r'<tr\b[^>]*id="post-row-' + str(record['id']) + r'"[^>]*>.*?</tr>', index, re.S)
    assert row, 'Saved row missing'
    _, _, form = request(opener, base + f"/AdminPost/Edit/{record['id']}")
    return ('Saved state: ' + expected in form and 'data-status="' + expected.lower() + '"' in row.group()
        and expected in row.group() and ('live-link-btn' in row.group()) == visible)

def is_public(record):
    return request(anon, base + '/post/' + record['slug'])[0] == 200

def instant(record):
    return datetime.fromisoformat(record['date']).astimezone(timezone.utc)

name = prefix + '_Draft'
s, u, _ = submit(name, 'SaveDraft', IsPublished='true', PublishDate=past)
r = database(name)
checks['save_draft_overrides_spoofed_flag_and_is_hidden'] = s == 200 and u.endswith('/AdminPost') and not r['published'] and not is_public(r)
checks['draft_state_and_no_live_link'] = row_state(r, 'Draft', False)

name = prefix + '_Scheduled'
s, u, _ = submit(name, 'Schedule', IsPublished='false')
scheduled = database(name)
checks['schedule_utc_exact_and_not_early'] = scheduled['published'] and instant(scheduled) == future.astimezone(timezone.utc) and not is_public(scheduled)
checks['scheduled_state_and_no_live_link'] = row_state(scheduled, 'Scheduled', False)

name = prefix + '_Published'
start = datetime.now(timezone.utc)
s, u, _ = submit(name, 'Publish', IsPublished='false')
end = datetime.now(timezone.utc)
published = database(name)
checks['publish_uses_server_now_and_is_visible'] = published['published'] and start <= instant(published) <= end and is_public(published)
checks['published_state_and_live_link'] = row_state(published, 'Published', True)

for label, action, overrides, message in [
    ('PastSchedule', 'Schedule', {'PublishDate': past}, 'Choose a future publish date'),
    ('InactiveCategory', 'Publish', {'CategoryId': '1002'}, 'Select an active category'),
    ('MissingCategory', 'Publish', {'CategoryId': '999999'}, 'Select an active category'),
    ('BadDate', 'Publish', {'PublishDate': 'invalid'}, 'validation-summary-errors'),
    ('UnknownAction', 'Unknown', {}, 'validation-summary-errors'),
    ('InvalidEnum', '99', {}, 'The value &#x27;99&#x27; is invalid.'),
    ('SaveOnCreate', 'Save', {}, 'requires an existing post')
]:
    name = prefix + '_' + label
    s, u, body = submit(name, action, **overrides)
    checks[label + '_rejected_and_input_retained'] = (s == 200 and '/Create' in u and name in body
        and name + ' content retained' in body and message in body and database(name) is None
        and 'id="post-recovery-receipt"' not in body)

# Normal Edit Save keeps persisted publication settings, ignoring submitted flag/date.
old = published
name = prefix + '_SavedChanges'
s, u, _ = submit(name, 'Save', post_id=old['id'], IsPublished='false')
r = database(name)
checks['normal_save_keeps_published_flag_and_exact_date'] = r['published'] and instant(r) == instant(old) and is_public(r)
old = scheduled
name = prefix + '_SavedSchedule'
submit(name, 'Save', post_id=old['id'], IsPublished='false', PublishDate=past)
r = database(name)
checks['normal_save_keeps_schedule_and_exact_date'] = r['published'] and instant(r) == instant(old) and not is_public(r)

name = prefix + '_ReturnedToDraft'
submit(name, 'SaveDraft', post_id=published['id'], IsPublished='true')
r = database(name)
checks['edit_save_draft_unpublishes'] = not r['published'] and not is_public(r)
name = prefix + '_Rescheduled'
submit(name, 'Schedule', post_id=r['id'])
r = database(name)
checks['edit_schedule_sets_future_utc'] = r['published'] and instant(r) == future.astimezone(timezone.utc) and not is_public(r)
name = prefix + '_PublishScheduledNow'
start = datetime.now(timezone.utc)
submit(name, 'Publish', post_id=r['id'])
r = database(name)
checks['edit_publish_ignores_future_date_and_publishes_now'] = r['published'] and start <= instant(r) <= datetime.now(timezone.utc) and is_public(r)

name = prefix + '_Inactive'
submit(name, 'Publish', IsActive='false')
r = database(name)
checks['inactive_post_state_and_hidden_link'] = r['published'] and not is_public(r) and row_state(r, 'Inactive', False)
checks['inactive_category_state_and_hidden_link'] = row_state({'id': 2008}, 'Inactive', False)
_, _, form = request(opener, base + '/AdminPost/Create')
checks['three_native_actions_and_no_publication_checkbox'] = all('value="' + a + '"' in form for a in ['SaveDraft','Schedule','Publish']) and not re.search(r'<input[^>]*type="checkbox"[^>]*name="IsPublished"', form)
_, _, edit = request(opener, base + f"/AdminPost/Edit/{r['id']}")
checks['edit_save_explains_keep_publication'] = 'Save changes (keep publication)' in edit and 'Save changes keeps the saved publication flag and date.' in edit
print(json.dumps({'checks': checks, 'count': len(checks)}, indent=2))
assert all(checks.values()), 'F32 publication action verification failed'
