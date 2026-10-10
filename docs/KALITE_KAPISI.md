# F55 / F57 — Kalite kapısı

Kişisel faz kayıtları ve ekran görüntüsü/test kanıtları yerelde Git dışında
tutulur. Temiz GitHub checkout'unda bu kayıtlar gerekmez; bu belge, test
script'leri ve Actions sonuçları kamuya açık doğrulama kaynaklarıdır.

## Çalıştırma

Repository kökünde tek komut:

```sh
python3 scripts/verification/run_quality_gate.py --report-dir /tmp/devcore-quality-report
```

Rapor dizini boş olmalıdır; eski kanıt yeniden kullanılmaz. Gerekenler: global.json
ile tam .NET SDK10.0.302, Node22+, npm, Python3, rsync, PostgreSQL16 araçları
(initdb/pg_ctl/createdb/psql/pg_isready), ripgrep, openssl. Yerelde PostgreSQL bin
PATH içinde olmalıdır; root olarak initdb çalışmaz. Test portları55459/15196, erişim55474/15204 ve
proxy/diagnostics15186–15188/15195 boş olmalıdır. Aktif PostgreSQL portu üzerinde
runner erken durur. PostgreSQL yalnız yeni /tmp fixture cluster’ında başlatılır;
mevcut DB connection/config kullanılmaz.

Kapı kaynakları boş geçici dizine kopyalar; .env, cookie, bin/obj/npm/generated
kopyalamaz. Uygulama credential/config ve eski DEVCORE fixture flag’leri process
environment’ından alınmaz. npm ci → npm audit → public NuGet restore → asset dahil
build → dört proje NuGet transitif audit → yerel dotnet-ef10.0.10 restore → test
tool build/checks → gerçek PostgreSQL migration/görünürlük/sayaç eşzamanlılığı →
HTTP/CSRF/CSP → Production TLS/proxy/session → bounded DB/hata/log privacy sırası
uygulanır. Yeni NuGet.Config yalnız geçici kaynakta public nuget.org kullanır.

Sürümlü belge doğrulamasıyla `DocumentValidation` restore/build/check aşamaları
da eklenir: host/DB/HTTP kullanmadan şema, Unicode, URL, resource limitleri ve
kısmi belge reddi kontrol edilir. [Belge v1 sözleşmesi](ICERIK_BELGESI_V1.md).
F06 aynı araç/check içinde görünür metin, başlık hedefleri ve okuma türevi
kabulünü ekler; kalite aşaması sayısı değişmez.
F07 aynı araçta güvenli C# web renderer fixture'larını; `postgres-http` grubunda
`document-web.json` ile kayıtsız yönetici JSON önizlemesinin authorization/CSRF,
Schema400/Limit413 ve bütün DB satırlarının değişmezliğini ekler. Gerçek Chrome
`document_web_browser_probe.cjs` ayrı masaüstü/mobil/klavye/CSP kabulüdür;
medya stub'ları gerçek YouTube/Cloudinary kanıtı değildir. Aşama sayısı değişmez.
Bu eklemeyle kapı normal29, `--final-acceptance` ile31 aşamadır; tarihsel
24/25/28 sonuçları önceki kaynaklara aittir.

E01 ile uygulama build’inden sonra `editor_build_probe.mjs` eklenir: değiştirilmiş
upstream/sanitizer hash’inin reddi, deterministik yeniden build, eski runtime
sanitizer’ın yokluğu ve dağıtım receipt’i. E02 ile `publish-boundary` eklenir:
yalnız owned kaynakta sentetik `.env*` ve özel belge JSON'ları oluşturulur,
SDK item keşfi ve gerçek publish çıktısı denetlenir; gerçek `.env` okunmaz.
Uygulama DLL'leri, appsettings ve lisanslı frontend varlıkları korunmalıdır.
Tiptap yerel build temeliyle `tiptap-distribution` aşaması da eklenir: gerçek
bundle/CSS hash, deterministik tekrar build, kullanılan paket LICENSE metinleri
ve eşleşen Tiptap aile sürümleri. Güncel varsayılan kapı26,
`--final-acceptance` kapısı28 aşamadır.

`content-access` ayrı owned PostgreSQL/HTTP fixture'ında tür/erişim metadata'sını
sınar: boş ve önceki şemadan migration, eski alanların korunması, enum/bülten
invariants, public sorgu matrisi ve atomik sayaç reddi. GET/HEAD404, meta/JSON-LD/
crawler/portföy sızıntısı, admin overposting reddi ve gerçekten ısıtılmış cache'in
erişim değişikliğiyle eviction'ı `public-access.json` içindedir. Mutation adapter'ı
yalnız geçici kaynakta oluşur; uygulamaya yeni HTTP endpoint'i eklenmez.
Private veri bulunan owned DB'de down migration'ın reddi de sınanır.

Yönetici rotası fixture'larda açıkça `/fixture-admin/login` olarak atanır;
HTTP yardımcıları `DEVCORE_TEST_ADMIN_LOGIN_PATH` değerini kullanır. Gerçek
`ADMIN_LOGIN_PATH` veya dotenv okunmaz. `postgres-http` içinde
`f01_admin_boundary_probe.py`, yapılandırılmış GET/HEAD/POST, eski adreslerin
kapalı olması, anonim challenge için404/no-store ve Location bulunmaması,
CSRF, logout ve geçersiz/eksik rota config'inin fail-closed davranışını ölçer.
Ek vakalar için fixture login bütçesi20'dir; rate-limit kabulü ayrı süreçte
ölçülür. Keyring owned geçici dizindedir; kaynak kopyasına `.local`, `.kilo`,
`.codex` ve `.auth` girmez. Test dosyalarındaki eski Fxx adları tarihsel kimliklerdir.

`f01_admin_browser_probe.cjs` ayrı gerçek Chrome kontrolüdür: public okuyucu
linki, masaüstü/mobil giriş, klavyeyle giriş/çıkış ve CSP. Mevcut
`f57_journey.cjs` aynı sentetik rotayla editör kabulünü sürdürür. Playwright modülü,
Chrome executable, loopback base URL ve boş rapor dizini dışarıdan belirtilir;
bu browser kontrolleri kalite kapısının otomatik aşamaları değildir.

Audit dışında build/restore NuGetAudit=false kullanır: bu güvenlik sertifikası
değildir; dört ayrı gerçek audit başarısızsa kapı başarısızdır. Kullanıcının aşağıda kabul ettiği tek build-time istisnası dışındaki vulnerability veya
advisory erişim/parse hatası başarıya çevrilmez; başarısız audit diğer kontrollerin
çalışmasına engel olmaz ve final exit1 olur. Kurulum/build/fixture hataları da
exit1’dir. Timeout kendi process grubunu durdurur; test kaynakları temizlenir.

Rapor summary.json gerçek stage exit/latency ve başarılı/başarısız sonucunu taşır.
Fixture raw output/application log/cookie/hash export edilmez; yalnız boolean
kontroller ve safe raporlar saklanır. Test medya adapter’ı canlı Cloudinary’yi
kanıtlamaz. Browser/video/gerçek Nginx/hosting kabulü bu otomasyonun kapsamı
olarak iddia edilmez; mevcut faz kanıtları ayrıdır. SDK seçimi/EF sürümü sabittir,
paket veya ürün major yükseltmesi yapılmamıştır.

## F57 genişletilmiş kabul

```sh
python3 scripts/verification/run_quality_gate.py --final-acceptance --report-dir /tmp/devcore-final-quality
```

Bu seçenek güncel26 aşamaya iki bağımsız disposable cluster kontrolü ekler: auth/CSRF,
güvenli Markdown, atomik sayaç, kalıcı webhook/restart, cache/zamanlı yayın ve
kategori silme yarışı; ardından eski edit/eşzamanlı güncelleme. Ek portlar
PostgreSQL55461/55462 ve uygulama15199/15201/15200’dür; boş olmalıdır.
F57 probe kaynak namespace’i ve gerçek DB data_directory sahipliğini doğrular;
gerçek ortam credential/config taşımaz. Ham auth/provider logları export edilmez.
Yedi regresyon grubu ve sekiz F22 sonucu eksik/başarısızsa kapı exit1 olur.

5 Ekim 2026 temiz yerel kaynakta **24/24** geçti. Gerçek Chrome yolculuğu,
CSP/mobil/klavye ve native %200 zoom ayrı tarayıcı kontrolleridir; bu komut onları
çalıştırmış sayılmaz. F57 raporu kanıtları ayırır.
İlk F57’de npm/NuGet audit’i yerel vendored editörü taramadığı için kapının yeşil
olması P1 DOMPurify bulgusunu kapatmıyordu; E01 bu dağıtım sınırını değiştirir.

E01 sonrası kaydedilen sonuç **25/25** idi. Yamalı sanitizer exact npm dependency ve
hash kontrollü runtime dağıtımıdır; yeni DOMPurify advisory’si artık npm audit’in
kapsamındadır. Diğer arşivli vendor kodunun tümü audit edilmiş sayılmaz.
E01 kaydı ayrı browser güvenlik/paste ve
publish kanıtını taşır. F57 yeniden kabulü
yerel ve uzak25/25 sonucunu kaydetti; yedi basamaklı PublishDate regresyonu
webhook ilk/tekrar yanıtının PostgreSQL hassasiyetinde aynı kaldığını doğrular.

## GitHub Actions

F55’te GitHub remote için .github/workflows/quality.yml hazırlandı.
Pull request/push/manual tetik, contents read-only, checkout credential
persist false, resmi actions commit SHA sabitlemesi, ubuntu24.04 ve PostgreSQL16
araçları kullanılır. Gerçek env secret istemez. Artifacts7 gün saklanır.
F57 ile workflow aynı `--final-acceptance` komutunu kullanır.
5 Ekim2026 kullanıcı onaylı push commit’i `2a7a5f1` için
[run37292561215](https://github.com/CixxMemo/DevCoreBlog/actions/runs/37292561215)
Linux’ta25/25 geçti. Kişisel kabul kaydı yerelde job/artifact metadata'sını korur;
Actions artifact'indeki kapı raporu gerçek run ID/commit'i taşır.
`summary.json.remote_workflow_executed` GitHub Actions origin’ini bildirir;
yerelde false kalır. Bu provenance uygulama environment’ını fixture’a aktarmak
veya kapının bağımsız başarı kontrolü yerine kullanmak değildir.
Branch protection/required check veya remote repository settings değiştirilmedi.

## Bağımlılık bakımının güncel durumu

9 Ekim2026 yeniden taramasında kilitli `postcss-selector-parser6.1.4` için
[GHSA-rj75-hqrm-r3gf](https://github.com/advisories/GHSA-rj75-hqrm-r3gf)
bulgusu da görüldü; `postcss-nested6.2.0` ve Tailwind zincirine taşınıyor.
Resmî kayıt <7.1.6 aralığını etkilenen,7.1.6'yı yamalı gösteriyor. Bu repository'de
paket trusted frontend kaynaklarını build sırasında işliyor; publish'te Node
runtime değildir. Bu kapsam gözlemi önceki dar istisnayı genişletmez:
`npm_audit_policy.py` yeni advisory'yi reddeder ve genel kapı **FAIL** kalır.
Bu, 9 Ekim rota fixture düzeltmesinin tarihsel sonucudur. 10 Ekim Tiptap build
entegrasyonunda yalnız Tailwind subtree'sine exact selector-parser7.1.6 override
uygulandı; eski CSS/vendor çıktılarının byte eşitliği doğrulandı. Yeni graph'ta
bu advisory artık bulunmuyor; audit politikası veya istisna genişletilmedi.
Temiz kalite kapısı ve browser sonucu ayrı gerçek kanıttır. Eski yeşil sonuçlar
güncel audit kabulü değildir. [Tiptap build sözleşmesi](TIPTAP_BUILD_SOZLESMESI.md).

5 Ekim2026 npm audit, Tailwind3.4.17 build zincirindeki braces3.0.3 için
[GHSA-vfj7-8cjw-p6xm / CVE-2026-93687](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
bildirdi: deeply nested brace pattern stack-exhaustion. Resmi kayıt <=3.0.3’ü
etkilenen sayıyor, patched version yok. npm’in5 high dependency kaydı aynı kök
advisory’nin braces/chokidar/fast-glob/micromatch/tailwindcss zinciridir;5 bağımsız
runtime açığı olarak yorumlanmaz. npm build araçları publish Node runtime’ı değildir;
bu durum bulguyu kapatmaz. Resmi npm registry latest3.0.3 ve tüm yayımlanan sürümler
etkilenen aralıkta olduğundan güvenli override yoktur; package/lockfile değişmedi.

Kullanıcı 5 Ekim2026’da bu spesifik build-time risk için açık istisna onayı verdi.
`npm_audit_policy.py` yalnız GHSA-vfj7-8cjw-p6xm ve mevcut beş dev-only sürümün
recursive via graph’ını kabul eder: braces3.0.3, chokidar3.6.0, fast-glob3.3.3,
micromatch4.0.8, tailwindcss3.4.17. Her node exact node_modules yolunda, lock.dev=true
olmalıdır; manifest dependencies/optionalDependencies boş kalır. Runtime input
build glob’una taşınmaz; frontend glob’ları repository tarafından belirlenir.
Build sırasında güvenilmeyen glob/config/desen çalıştırılması risk kapsamı
dışıdır; böyle bir kullanımda istisna kaldırılmalıdır.

Yeni advisory, package/version/node, prod kapsamı, malformed yanıt/count, network
error/timeout veya 5 Kasım2026 review tarihinin geçmesi kapıyı bloklar. Tarih
kendiliğinden uzatılmaz; dependency bakımında upstream yama yeniden araştırılır.
Ham npm audit JSON ve gerçek exit1 korunur. Summary kabul edilen riskin ID/sürüm/
kapsamını listeler; durum PASS_WITH_ACCEPTED_BUILD_RISK’tir, vulnerability-free
denmez. Açık yamalanmış değildir, kullanıcı tarafından dar kapsamda kabul edilmiştir.

İstisna regresyon komutu:

```sh
python3 scripts/verification/f55_audit_policy_probe.py --audit /tmp/devcore-quality-report/npm-audit.json --report /tmp/devcore-audit-policy.json
```

Arşivli Toast UI’nin kalan kodu için bakım sorumluluğu sürer; E01’in kilitli
DOMPurify modülü artık npm audit kapsamındadır. Audit bütün vendor kodunu veya
gerçek browser/Cloudinary davranışını güvenlik sertifikasıyla doğrulamaz.

Kaynaklar: [.NET SDK global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json),
[NuGet package audit CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list),
[GitHub Actions sözdizimi](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
