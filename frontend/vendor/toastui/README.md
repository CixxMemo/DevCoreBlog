# Toast UI Editor 3.2.2 — E01 yerel güvenlik dağıtımı

5 Ekim 2026: hazır CDN bundle’ı DOMPurify2.3.3 içeriyordu; runtime güvenlik
bulgusu nedeniyle artık doğrudan kopyalanmaz. Aynı Editor3.2.2 ve CSS korunur.
`npm run build`, resmî DOMPurify3.4.16 CJS modülünü bütün olarak webpack368
modülünün yerine koyup Terser ile yerel bundle üretir. Bu NHN’nin yeni resmî
release’i değildir; DevCoreBlog tarafından bakımı yapılan dağıtımdır.

`upstream/toastui-editor-all.js` sabit, değiştirilmemiş resmî **build input**’udur.
Acorn AST ile tek module table/368 factory/module parametresi kontrol edilir;
hem upstream hem yeni sanitizer SHA256 eşleşmeden build durur. Minified kodda
regex yaması veya özel sanitizer implementasyonu yoktur. Eski module bütünüyle
çıkarılır; diğer Editor/ProseMirror/ToastMark kodu korunur.

Build yalnız CSS, lisanslar, bu README, yamalı JS ve `build.json` receipt’i
`wwwroot/generated/toastui` içine koyar. Upstream kaynak frontend altında Web
default item glob’undan dışlanır ve publish paketine girmez. Browser ağdan paket
indirmez; mevcut script URL’si ve `asp-append-version` cache busting korunur.

- [Resmî upstream JS](https://uicdn.toast.com/editor/3.2.2/toastui-editor-all.js): SHA256 `0d8c202706e3ea8d91c28307c445b55b746cf985d0994d70951b257ba207c8d9`.
- [Resmî CSS](https://uicdn.toast.com/editor/3.2.2/toastui-editor.min.css): değişmedi, SHA256 `c70e24c68fefc205e8e504edc07fd6a5efd3044a623b4be7e3ac16cc8a736ed9`.
- [Editor lisansı](https://github.com/nhn/tui.editor/blob/editor%403.2.2/LICENSE): MIT; ProseMirror MIT/Microsoft bildirimleri korunur.
- [DOMPurify3.4.16 release](https://github.com/cure53/DOMPurify/releases/tag/3.4.16): MPL2.0 OR Apache2.0; npm integrity package-lock içinde. CJS SHA256 `1144c3ba99465d58ff93ab2419ebd99b7d7e12525b1ca4f45308771acf34cfec`; lisans dosyası doğrudan kilitli paketten sunulur.
- İlk E01 yamalı çıktı SHA256 `3887290898f676e4a398e0d120e6bfbfc2a6d0bf629bafde04568a6089191ce4`. `build.json` her build’de gerçek çıktı hash’ini içerir; `editor_build_probe.mjs` deterministik yeniden derlemeyi kıyaslar.

DOMPurify, Acorn ve Terser exact dev dependency’dir; DOMPurify kodu runtime JS’ye
girdiğinden build-time risk sayılmaz. Yeni advisory npm audit/kalite kapısında
bloklanır; F55 braces istisnası buna uygulanmaz. Kaynak/hash/sürüm değişiminde
resmî advisory ve browser XSS/CSP/upload/recovery/preview kontrolleri tekrar gerekir.
NHN repository’si arşivlidir; Editor’in tamamı için sürdürülen upstream garantisi
yoktur. Editör değişimi ayrı ürün kararıdır; E01 yalnız doğrulanmış sanitizer
bakımını yapar. Sunucudaki güvenli Markdown sınırı değişmez.
