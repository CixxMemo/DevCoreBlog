# Toast UI Editor 3.2.2 dağıtımı

Mevcut CDN `latest` yanıtında 3.2.2 doğrulandı. npm dağıtımı mevcut all-in-one dosyasını içermediği için aynı resmi sabit CDN dosyaları değiştirilmeden depoda tutulur; build sırasında yerel `wwwroot/generated/toastui` içine kopyalanır. Ağ erişimi runtime için gerekmez.

- [JS kaynağı](https://uicdn.toast.com/editor/3.2.2/toastui-editor-all.min.js)
- [CSS kaynağı](https://uicdn.toast.com/editor/3.2.2/toastui-editor.min.css)
- [Sürüm lisansı](https://github.com/nhn/tui.editor/blob/editor%403.2.2/LICENSE)
- Bundle içindeki DOMPurify 2.3.3 lisansı ayrıca korunur; Microsoft bildirimi JS içinde bulunur. ProseMirror MIT bildirimi eklenmiştir.
- Editor sürümü yükseltilmedi; içerik güvenliğinin otoritesi sunucudaki güvenli Markdown renderer olmaya devam eder. Bu kayıt dependency güvenlik denetimi değildir.

SHA-256 (indirilen dosyaların bütünlüğü):

- `toastui-editor-all.min.js`: `f50e1b7c0fc4e5d9a1ccd0d8be78cb3a950ccb3bf676fbf1627810c76aeaedd8`
- `toastui-editor.min.css`: `c70e24c68fefc205e8e504edc07fd6a5efd3044a623b4be7e3ac16cc8a736ed9`
