# Tiptap yerel build ve deneme sözleşmesi

Tiptap ücretsiz çekirdeği mevcut Razor/Tailwind/vanilla JS uygulamasında yerel
ES module olarak hazırlanır. Exact npm sürümleri `package.json` ve gerçek
transitive graph `package-lock.json` kaynağındadır. Tiptap core/pm/starter-kit
3.31.4, küçük bundler esbuild0.28.2; lisansları MIT'dir. React, ücretli extension,
CDN, Tiptap Cloud veya Markdown dönüşüm bağımlılığı eklenmez.

## Build ve lisans

`npm ci --ignore-scripts --no-fund --no-audit` ardından `npm run build` mevcut
Tailwind/Prism/Toast UI çıktısını ve yeni `wwwroot/generated/tiptap/` çıktısını
üretir. .NET build aynı girişleri kullanır. esbuild'in platform binary'si kilitli
optional npm paketinden gelir; Node22+ altında install script'i çalıştırmak gerekmez.
Disposable kaynak kopyaları `node_modules` taşımaz: genel `bin` dışlaması native
esbuild binary'sini kırpabilir; kilitli temiz npm kurulumu yapılır.

Kaynak JS/CSS `frontend/tiptap/`, bundler `scripts/frontend/tiptap-bundle.mjs`
altındadır. `trial.js` yerel dependency chunk'ını dinamik yükler; yükleme hatasını
Türkçe durum metniyle gösterir. ESM ES2022, minified, source map kapalı;
hash içeren chunk isimleri giriş ve cache bağlantısını korur.
`build.json` çıktı byte/SHA-256, kullanılan paket sürüm/lisans ve input listesini
taşır. Paketlerin gerçek LICENSE metinleri, @tiptap/pm third-party metni ve
esbuild legal comment çıktısı yayımlanan lisans dosyalarıyla korunur.
Yeni lisans veya uyumsuz Tiptap sürümü build'de reddedilir. Mevcut E01
upstream/sanitizer hash kontrolleri değişmez.

## Yetkili, kayıtsız deneme

`GET /AdminPost/EditorTrial`, mevcut yönetici authorization sınırındadır;
no-store/noindex yanıt verir. Yazı sorgusu, input binding, kayıt POST'u, upload,
local/session storage veya gerçek içerik JSON'u yoktur. Deneme paragraf, metin,
kalın biçimlendirme ve geri al/yinele ile sınırlıdır. Türkçe status, textbox
label/description ve görünür keyboard focus vardır. Yenileme deneme metnini siler;
ekran bu davranışı açıkça söyler. Mevcut Create/Edit Toast UI formu korunur.

Tiptap `injectCSS:false` ile çalışır; CSS yerel dosyadadır. Minimal denemede
dinamik konum stilleri gereken Dropcursor dahil diğer araçlar devre dışıdır;
tam editör ve sunucu JSON doğrulaması sonraki uygulama kapsamıdır. Bu sayfa
genel `style-src-attr 'none'` politikasını kullanır; CSP middleware'i, nonce ve
Create/Edit'in dar legacy istisnası değişmez. Tiptap DOM'u güvenilir web/e-posta
yayın çıktısı sayılmaz; sunucu renderer veya kayıt altyapısı tamamlanmış değildir.

## Kabul komutları

```sh
npm run build
node scripts/verification/editor_build_probe.mjs
node scripts/verification/tiptap_build_probe.mjs
dotnet build DevCoreBlog.csproj
```

Tiptap probe deterministik yeniden build, asset hash/byte, lisans metni ve aile
sürümünü doğrular; kalite kapısında ayrı `tiptap-distribution` aşamasıdır.
`tiptap_browser_probe.cjs` ayrı gerçek Chrome kabulüdür: yalnız explicit
loopback/sentetik fixture'da çalışır; anonymous denial, no-store, sıkı CSP,
Türkçe yazma, bold/undo/redo, desktop/mobile/focus/taşma, yenilemede kayıt yokluğu,
dependency chunk hatası ve mevcut Toast UI korunmasını ölçer. Playwright modülü,
Chrome executable, `DEVCORE_TIPTAP_BASE_URL`, `DEVCORE_TIPTAP_REPORT_DIR` operatör
tarafından belirtilir. Kalite kapısı bu browser kabulünü kendiliğinden çalıştırmaz.

Tailwind zincirindeki selector parser için yalnız Tailwind subtree'sine
`postcss-selector-parser7.1.6` override uygulanır. Resmi
[GHSA-rj75-hqrm-r3gf](https://github.com/advisories/GHSA-rj75-hqrm-r3gf)
bu sürümü yamalı gösterir. Eski build çıktısıyla byte eşitliği ayrıca kontrol
edilir; Tailwind major yükseltilmez. Npm audit ham exit1, önceden onaylı braces
bulgusu nedeniyle sürebilir; [mevcut dar kalite politikası](KALITE_KAPISI.md)
korunur ve vulnerability-free iddiası yapılmaz.

Resmi kaynaklar: [vanilla kurulum](https://tiptap.dev/docs/editor/getting-started/install/vanilla-javascript),
[Editor API / injectCSS](https://tiptap.dev/docs/editor/api/editor),
[StarterKit yapılandırması](https://tiptap.dev/docs/editor/extensions/functionality/starterkit),
[esbuild API](https://esbuild.github.io/api/).
