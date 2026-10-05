# F55 — Kalite kapısı

## Çalıştırma

Repository kökünde tek komut:

```sh
python3 scripts/verification/run_quality_gate.py --report-dir /tmp/devcore-quality-report
```

Rapor dizini boş olmalıdır; eski kanıt yeniden kullanılmaz. Gerekenler: global.json
ile tam .NET SDK10.0.302, Node22+, npm, Python3, rsync, PostgreSQL16 araçları
(initdb/pg_ctl/createdb/psql/pg_isready), ripgrep, openssl. Yerelde PostgreSQL bin
PATH içinde olmalıdır; root olarak initdb çalışmaz. Test portları55459/15196 ve
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

## GitHub Actions

Remote GitHub olduğu ve mevcut CI bulunmadığı için .github/workflows/quality.yml
hazırlandı. Pull request/push/manual tetik, contents read-only, checkout credential
persist false, resmi actions commit SHA sabitlemesi, ubuntu24.04 ve PostgreSQL16
araçları kullanılır. Gerçek env secret istemez. Artifacts7 gün saklanır.
Workflow remote’da çalıştırılmadı; yerel macOS kabulü Linux CI başarısı değildir.
Branch protection/required check veya remote repository settings değiştirilmedi.

## Açık kalan bağımlılık bulgusu

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
python3 scripts/verification/f55_audit_policy_probe.py --audit docs/kanitlar/F55-2026-10-05/npm-audit.json --report /tmp/devcore-audit-policy.json
```

F35 Toast UI/vendor borcu bağımsızdır; yerel vendor dosyaları npm/NuGet advisory
taramalarının kapsamı değildir. Remote workflow hâlâ yalnız hazırlanmış config’dir.

Kaynaklar: [.NET SDK global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json),
[NuGet package audit CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list),
[GitHub Actions sözdizimi](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
