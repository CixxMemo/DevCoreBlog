// Race and failure checks against the production store module, with deterministic storage/locks.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
const source = await readFile(new URL('../../frontend/tiptap/recovery-store.js', import.meta.url), 'utf8');
const { createDocumentRecovery, clearDocumentRecovery, decodeRecovery, recoveryKey, recoveryBytes } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const checks = {};
function check(name, value) { checks[name] = !!value; assert.ok(value, name); }
const data = new Map();
const storage = { getItem: key => data.get(key) ?? null, setItem: (key,value) => data.set(key,value), removeItem: key => data.delete(key) };
let queue = Promise.resolve();
const locks = { request: (_key, _options, action) => { const result = queue.then(action); queue = result.catch(()=>{}); return result; } };
const initial = () => ({ DocumentJson: JSON.stringify({version:1,document:{type:'doc',content:[{type:'paragraph'}]}}), Title:'Türkçe 👋', CategoryId:'1001', Summary:'', Excerpt:'', ThumbnailAlt:'', PublishDate:'2026-10-12T12:00', IsActive:true, EditVersion:'0' });
function session(id=0, fields=initial(), overrides={}) {
    const states=[];
    const instance=createDocumentRecovery({ id, storage, locks, readFields:()=>({...fields}), notify:s=>states.push(s), revision:randomUUID, now:()=>new Date().toISOString(), ...overrides });
    return { instance, fields, states };
}
const key=recoveryKey(0), a=session(), b=session();
a.fields.Title='Birinci sekme'; const revision=await a.instance.save(); const raw=data.get(key);
check('first_writer_persists_identity_revision_and_fields',decodeRecovery(raw,0)?.revision===revision);
b.fields.Title='İkinci sekme'; check('second_writer_cannot_overwrite_unseen_revision',await b.instance.save()===null && data.get(key)===raw && b.states.at(-1).kind==='conflict');
check('rejected_server_admission_keeps_raw_and_current_text',await b.instance.restore(async()=>false)===false && data.get(key)===raw && b.fields.Title==='İkinci sekme');
check('admitted_restore_keeps_local_copy',await b.instance.restore(async fields=>{Object.assign(b.fields,fields);return true;})===true && data.get(key)===raw);
b.fields.Title='Daha yeni'; const newer=await b.instance.save();
check('old_commit_cannot_clear_new_revision',newer!==revision && await clearDocumentRecovery({id:0,revision,storage,locks})===false && data.has(key));
check('matching_commit_clears_only_its_revision',await clearDocumentRecovery({id:0,revision:newer,storage,locks})===true && !data.has(key));
check('invalid_receipt_cannot_clear',await clearDocumentRecovery({id:0,revision:'forged',storage,locks})===false);
const c=session(23); await c.instance.save(); check('edit_and_create_keys_are_distinct',data.has(recoveryKey(23)) && !data.has(key));
check('foreign_identity_cannot_decode',decodeRecovery(data.get(recoveryKey(23)),24)===null);
for(const [name,unknown] of Object.entries({legacy:'{"version":1,"fields":{"Content":"Markdown"}}',future:JSON.stringify({...JSON.parse(raw),version:2}),foreign:JSON.stringify({...JSON.parse(raw),postId:24}),malformed:'{',oversized:'x'.repeat(recoveryBytes+1),extra:JSON.stringify({...JSON.parse(raw),unexpected:true})})) {
 data.set(key,unknown);const s=session(); check(name+'_retained_without_write',await s.instance.save()===null && data.get(key)===unknown && s.states.at(-1).kind==='unknown');
 check(name+'_only_explicit_discard_removes',await s.instance.discard()===true && !data.has(key));
}
const limited=session(); limited.fields.Title='x'.repeat(201); check('metadata_limit_keeps_prior_copy',await limited.instance.save()===null && !data.has(key));
limited.fields=initial();
const unavailable=session(0,initial(),{storage:{...storage,setItem:()=>{throw new Error('Quota');}}});
check('quota_failure_is_reported_without_success',await unavailable.instance.save()===null && unavailable.states.at(-1).kind==='unavailable');
const denied=session(0,initial(),{storage:{...storage,getItem:()=>{throw new Error('Blocked');}}});
check('denied_storage_fails_closed',await denied.instance.save()===null && denied.states.at(-1).kind==='unavailable');
const uncoordinated=session(0,initial(),{locks:undefined});
check('missing_locks_never_writes',await uncoordinated.instance.save()===null && !data.has(key));
check('invalid_identity_never_writes',await session(-1).instance.save()===null);
data.set(key,raw); const raced=session(); data.set(key,JSON.stringify({...JSON.parse(raw),revision:randomUUID()}));
check('discard_compares_unseen_raw_before_removing',await raced.instance.discard()===false && data.has(key));
data.clear();
const postDraft=session(),newsletterDraft=session(0,initial(),{scope:'newsletter'});
postDraft.fields.Title='Makale taslağı';newsletterDraft.fields.Title='Bülten taslağı';
const postRevision=await postDraft.instance.save(),newsletterRevision=await newsletterDraft.instance.save();
check('newsletter_create_and_article_create_are_isolated',postDraft.instance.key!==newsletterDraft.instance.key && data.size===2);
check('newsletter_commit_cannot_clear_article_create',await clearDocumentRecovery({id:0,scope:'newsletter',revision:newsletterRevision,storage,locks}) && data.has(recoveryKey(0)) && !data.has(recoveryKey(0,'newsletter')));
check('article_commit_cannot_clear_other_newsletter_copy',await clearDocumentRecovery({id:0,revision:postRevision,storage,locks}) && data.size===0);
check('unknown_recovery_scope_is_fail_closed',recoveryKey(0,'other')===null);
console.log(JSON.stringify({count:Object.keys(checks).length,checks},null,2));
