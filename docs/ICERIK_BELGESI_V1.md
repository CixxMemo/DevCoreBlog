# İçerik belgesi v1 doğrulama sözleşmesi

Sunucu sınırı `ContentDocumentValidator.Validate(string | ReadOnlyMemory<byte>)`.
F05 bu bileşeni ve salt sentetik kabul aracını ekler; F06 bağımsız okuma türevini,
F07 güvenli web HTML ve kayıtsız yönetici doğrulama yüzeyini ekler.
F08 atomik kalıcı belge kaydını ekler; tam editör geçişi sonraki fazdadır. Mevcut Markdown
akışı bu belgeyi tüketmez. Yeni runtime paketi yoktur.

Zarf tam olarak `{"version":1,"document":{"type":"doc","content":[...]}}`.
İstemcinin Tiptap `getJSON()` sonucu `document` içinde taşınır. Sürüm formatı
içerikten tahmin edilmez. Alan adları/türleri büyük-küçük harfe duyarlıdır.
Bilinmeyen zarf/node/mark/attribute alanı, yinelenmiş decoded JSON anahtarı,
yorum, trailing comma, bozuk JSON veya UTF reddedilir; alanlar sessiz silinmez.

Başarı yalnız iç constructor'ı olan `ValidatedContentDocument` döndürür; içindeki
Core ağacı string, enum, typed attribute ve `ImmutableArray` değerlerinden oluşur.
Core modelini elle kurmak doğrulanmış belge yetkisi vermez. Her çağrı ayrı sayaç
tutar. Başarısızlıkta belge yoktur; hata yalnız `Schema`/`Limit`, sabit kod ve
şema yolu taşır. Payload/hata değeri loglanmaz. İlk ihlal döner; tüm hataları
toplamak için kaynak kullanımı büyütülmez. İleride HTTP katmanı Schema→400,
Limit→413 eşlemesini yapar; F07 belge önizlemesi bu eşlemeyi uygular.

## Node ve mark şeması

Node alanları `type`, izinli `attrs`, türe göre `content` veya `text`/`marks`.
Eksik attrs boş alan kümesidir; `attrs:null` geçersizdir.

| Tür | İçerik ve attribute |
|---|---|
| doc | Yalnız kök; en az bir block |
| paragraph | text/hardBreak, boş olabilir; textAlign=null/left/center/right |
| heading | text/hardBreak, boş olabilir; zorunlu level=2/3/4, aynı textAlign |
| text | Boş olmayan text; content yok; marks isteğe bağlı array |
| hardBreak | Atomik; content/text/marks yok |
| blockquote | En az bir block |
| bulletList / orderedList | En az bir listItem; orderedList start=1..10.000 (varsayılan1), type=null/"1" |
| listItem | İlk çocuk paragraph, devamında block |
| codeBlock | Yalnız marksız text, boş olabilir; language=null veya aşağıdaki sabit değerler |
| image | Atomik; zorunlu HTTPS src; alt/title=null veya sınırlı metin; width/height yalnız eksik/null |
| youtube | Atomik; zorunlu dar HTTPS src; width=640, height=480, start=0 (eksikse aynı varsayılanlar) |
| table | 1..20 tableRow, dikdörtgen |
| tableRow | 1..10 tableCell/tableHeader |
| tableCell / tableHeader | En az bir block; colspan/rowspan yalnız1, colwidth yalnıznull; align=null/left/center/right |

Block: paragraph, heading, blockquote, bulletList, orderedList, codeBlock, image,
youtube, table. Hücre altındaki bütün derinliklerde table yasaktır. Birleştirme,
boyutlandırma, serbest font/renk/CSS/HTML/iframe yoktur. Bilinmeyen boş attribute
bile reddedilir. Bilinen default alanların typed varsayılanlara dönüşmesi alan
silerek geçersiz belgeyi kabul etmek değildir.

Code language: plaintext, csharp, javascript, typescript, json, python, bash,
sql, html, css, markdown. Sınıf adı/HTML attribute olarak serbest dil kabul edilmez.

Marks yalnız text'te: bold, italic, underline, code, link. Aynı tür tekrarlanamaz.
İlk dört türün attribute'ları boş olmalıdır. Link: zorunlu href; title=null veya
en çok300 rune; target=null/_self/_blank; rel=null/"noopener noreferrer"/
"noopener noreferrer nofollow"; class yalnıznull. Typed model target ve nofollow
anlamını taşır; F07 renderer kendi sabit güvenli rel değerini üretir.

Beş mark birlikte geçerlidir. Tiptap'in standart Code mark'ı diğer mark'ları
dışladığı için tam istemci şemasında `Code.extend({ excludes: '' })` gerekir.
HardBreak bu sürümde mark taşımaz; istemci `keepMarks:false` ile uyarlanmalıdır.
Bu uyarlamalar F05'te yapılmadı; tam istemci fazında server ile round-trip testi
zorunludur. F03'teki kayıtsız deneme tam şema uygulaması değildir.

## Kaynak sınırları

| Ölçüm | En çok |
|---|---:|
| Zarf ve whitespace dahil girdi UTF-8 byte | 1.048.576 |
| Node derinliği; kök doc=1, text dahil | 32 |
| Kök/text/atomik dahil bütün node | 10.000 |
| Bütün text node'larındaki mark toplamı | 30.000 |
| Tek text node mark | 5 |
| Bütün text değerlerinin Unicode scalar (rune) toplamı | 200.000 |
| Tek text değeri | 20.000 rune |
| URL | 2.048 rune |
| Alt veya image/link title | 300 rune |
| Tek tablo | 20 satır × 10 sütun |
| Bütün tablolardaki cell/header toplamı | 1.000 |
| image / youtube toplamı | 50 / 10 |
| Ordered list start | 10.000 |

Grapheme birleşimi normalize edilmez: emoji scalar1, birleşen aksan ayrıca1;
UTF-16 surrogate çiftleri bölünmez. Alt/title/URL görünür text toplamına girmez.
Text'te LF/CR/TAB dışında control yasaktır; label'da bütün control yasaktır.
JSON sözdizimi için ayrıca72 container derinliği parse sırasında engellenir:
32 node'un content array/object katmanlarına zarf/attribute payı vardır.
Bu koruma geçerli32 node belgesini reddetmez. UTF-8 byte limiti tree allocation
öncesinde; node/mark/metin/media/tablo limitleri dolaşım sırasında uygulanır.

## URL ve medya sınırı

Link yalnız credential'sız mutlak HTTPS, tek `/` ile başlayan yerel referans
veya boş olmayan `#fragment` olabilir. Görsel yalnız credential'sız mutlak HTTPS.
Whitespace/control, literal backslash/HTML quote/bracket, bozuk yüzde escape,
encoded control/backslash/percent ve yerel URL'de encoded slash/colon reddedilir.
HTTP, protocol-relative, data, javascript, mailto ve çıplak göreli yol yoktur.
Sunucu URL fetch yapmaz. Bu kontrol medya byte'ı, dosya formatı, sağlayıcı
sahipliği veya upload başarısı kanıtı değildir; sonraki upload fazı mevcut
Cloudinary/imza/decode politikasını ayrıca uygular.

YouTube kimliği tam11 ASCII harf/rakam/`-`/`_` olmalıdır. İzinli ham biçimler:
`https://youtu.be/ID`, `https://[www.]youtube.com/watch?v=ID`,
`https://[www.]youtube.com/embed/ID`, `https://www.youtube-nocookie.com/embed/ID`.
Ek query/fragment/port/path/escape yoktur. URI normalization izinli grameri
genişletemez. F07 renderer bu kimlikten yalnız nocookie embed üretir; JSON iframe taşımaz.

Literal `<script>` gibi metin **text verisi** olarak korunabilir; bu HTML üretimi
değildir. Web renderer bu değeri encode eder. `html` node veya
event/style attribute'ları reddedilir. Doğrulanmış model `Html.Raw` güveni vermez.

## Okuma türevi (F06)

`DocumentTextProducer.Produce(ValidatedContentDocument)` aynı doğrulanmış snapshot'tan
immutable `DocumentReading` üretir: PlainText, WordCount, ReadingMinutes ve
belge sırasındaki immutable Headings. JSON yeniden parse edilmez; HTML üretilmez.
F07 renderer/önizleme aynı okuma sonucunu kullanır; kayıt ve arama henüz bağlanmaz.

Text, biçimli text, inline kod, link etiketi, kod bloğu ve hücre metni korunur.
JSON anahtarları, mark/attribute değerleri, URL/alt/title/video kimliği dışarıda
kalır. Bloklar LF ile ayrılır; boş bloklar gereksiz satır eklemez. Inline text
run'ları aralarına boşluk eklemeden birleşir. HardBreak LF üretir; yazılmış
whitespace, code LF/CR/TAB ve Unicode dizileri trim/normalize edilmez.
Son bloğun ardından yapay LF eklenmez; yazılmış son hardBreak/LF korunur.

Kelime, Unicode harf/rakam veya underscore ile başlayan kesintisiz gruptur.
Birleşen Unicode mark'ları mevcut kelimeyi sürdürür, kendi başlarına kelime
başlatmaz. Diğer karakterler grubu bitirir. Emoji sayılmaz; Türkçe, supplementary
harf ve birleşen aksan bölünmez. Apostrof/tire ayrı kelime sınırıdır. Bu açık,
sınırlı mühendislik sayımıdır; dilbilimsel tokenizer veya kişiye özgü hız değildir.
Tahmin200 kelime/dakika; pozitif sayıda yukarı yuvarlanır, kelimesiz belge0 dakika.
Legacy Markdown'ın boş içerikte en az1 dakika kuralı yeni JSON'a taşınmaz.

Her h2–h4, boş veya tekrarlı olsa da belge sırasıyla `document-section-N` kimliği
alır. N1'den başlar, invariant decimal biçimdedir. Title bütün inline text ve
hardBreak'ten gelir; h2–h4 seviyesi korunur. Kimlik başlık metninden veya serbest
attribute'tan gelmez; aynı belge ve farklı culture aynı sonucu verir. Başlık
ekleme/sıralama kimlikleri değiştirebilir; bu kimlikler kalıcı dış URL taahhüdü
değildir. F07 renderer bu aynı sıradaki kimlikleri kullanır.

Türev metin ve başlık hâlâ plain data'dır; `<script>` gibi literal text
korunabilir. HTML/Razor/e-posta tüketicisi kendi doğrulanmış encoding sınırını
uygulamalıdır. F06 sonuçları `Html.Raw` güveni vermez.

## Güvenli web gösterimi (F07)

`DocumentWebRenderer.Render(ValidatedContentDocument)` JSON/HTML parse etmez;
yalnız doğrulanmış immutable Core ağacını tüketir. `DocumentTextProducer` ile
aynı snapshot'ın `DocumentReading` sonucunu ve HTML'ini tek
`RenderedContentDocument` içinde döndürür. Başlık id/level sırası doğrudan bu
okuma sonucundan alınır. Çıktı tipinin public constructor/setter'ı yoktur.
PlainText veya elle kurulmuş Core ağacı güvenli HTML kabiliyeti değildir.

Metin, href, src, alt, title ve başlık kimliği yerleşik `HtmlEncoder.Default`
ile kodlanır. Açılan bütün mark'lar ters sırayla kapanır; link için sabit
`_self`/`_blank` ve her zaman `noopener noreferrer`, gerekiyorsa `nofollow`
üretilir. Kullanıcı rel/class/style/event verisi çıktı şablonuna aktarılmaz.
Türkçe, emoji ve literal HTML, tarayıcıda metin olarak korunur.

| Şema | Web çıktısı |
|---|---|
| paragraph / heading / hardBreak | p / h2–h4 ve ortak document-section-N / br |
| bold / italic / underline / code / link | strong / em / u / code / a |
| blockquote / list / listItem | blockquote / ul veya kayıtlı start ile ol / li |
| codeBlock | whitespace koruyan pre/code; sabit language sınıfı; odaklanabilir kaydırma |
| image / GIF | Kaynak HTTPS URL'si korunur; encode alt/title, boş alt dekoratif; lazy/async |
| youtube | Yalnız typed VideoId'den www.youtube-nocookie.com/embed/ID; sabit title, lazy, no-referrer, fullscreen |
| table / row / cell / header | Odaklanabilir adlandırılmış kaydırma bölgesi; table/tbody/tr/td/th |

Hizalama yalnız sabit sol/orta/sağ sınıflarıdır. Hücre ve paragraf hizalaması
ayrı düğüm değerleridir; iç paragraf kendi hizalamasını uygular. Tablo header
düğümü th üretir; şema satır/sütun başlık kapsamı taşımadığından scope uydurulmaz.
Görselin gerçek boyutu/sahipliği veya GIF frame'leri burada doğrulanmaz;
kaynak değiştirilmez, sunucu URL fetch yapmaz. Kod okunabilir düz gösterimdir;
bu faz ayrıca syntax highlighting veya kopyalama etkileşimi eklemez.

`_RenderedContentDocument.cshtml` tek yeni `Html.Raw` sınırıdır: yalnız bu çıktı
tipini alır ve yerel `document-content.css` ile gösterir. Sabit CSS, enforcing
CSP altında inline style/event/script gerektirmez. Mevcut public Markdown detay
ve eski önizleme henüz JSON'a çevrilmez; DB/şema/veri değişmez.

`/AdminDocument/Preview` ayrı MVC GET/POST yönetici doğrulama yüzeyidir.
GET boş form açar; POST antiforgery korumalı doğrulama ve renderer çağrısıdır.
Her iki sonuç no-store/noindex'tir; kayıt, upload, sayaç veya yayın eylemi yoktur.
Hatalı belge partial render edilmez; form metni Razor encoding ile korunur.
Schema400, belge kaynak sınırı413; eksik form400. Wire body sınırı3.211.264 byte
(3×1MiB +64KiB), form value sınırı aynı, value count8'dir. Bounded form-read
hatası ASP.NET antiforgery tarafından action'dan önce400'e çevrilebilir;
geçerli belge için1MiB UTF-8 kuralı server validator'da ayrıca uygulanır.
Anonim GET404/private challenge'dır; token'sız POST404 hata view'ının yeniden
yürütülmesi sırasında global antiforgery400 verebilir. Her durumda içerik
üretilmez, login adresine redirect yoktur. Gerçek4xx korunur.

F07'nin saf fixture'ları bütün node/mark'lar, attribute encoding, tehlikeli URL/
embed reddi, Unicode/code whitespace, heading snapshot, en büyük geçerli metin/
node/depth/tablo ve deterministik/parallel üretimi kapsar. Kalite kapısının
`postgres-http` grubundaki `document_web_preview_probe.py` authorization/CSRF/
limitler/overposting ve bütün DB satırlarının değişmezliğini sınar.
`document_web_browser_probe.cjs` ayrı gerçek Chrome desktop/mobile/klavye
kontrolüdür; medyanın deterministik stub yanıtı gerçek YouTube oynatma veya
Cloudinary/upload kabulü değildir. Browser kontrolleri kapı aşaması sayılmaz.

Encoding API kaynağı: [.NET HtmlEncoder](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodings.web.htmlencoder?view=net-10.0).
Iframe attribute kaynağı: [MDN iframe](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe).

## Doğrulama ve resmi kaynaklar

Salt sentetik araç:

```sh
dotnet run --project tools/DevCoreBlog.DocumentValidationTool --no-launch-profile
```

Her sayısal limitte başarı ve bir üstünde typed ret; pozitif node/mark/default
matrisi, Unicode, XSS/URL/duplicate/nesting negatifleri, kısmi belge ve çağrılar
arası durum izolasyonu kontrol edilir. Genel kalite kapısında restore/build/check
olarak da çalışır; DB/HTTP/gerçek sağlayıcı veya browser kabulü sayılmaz.
F06 fixture'ları ayrıca blok/inline sınırı, Unicode/emoji/kod/tablo/media,
kelime/dakika eşikleri, tekrarlı başlıklar, deterministik/no-mutation/parallel
üretim, F05'in en büyük geçerli girdileri ve legacy Markdown karşılaştırmasını
kontrol eder. Ortak örneklerin whitespace-normalized metin/dakika sonuçları eşit;
boş belgede yeni0/legacy1 farkı açık assertion ile korunur.

Karşılaştırılan resmi kaynaklar: [Tiptap JSON persistence](https://tiptap.dev/docs/editor/core-concepts/persistence),
[v3.31.4 Image](https://github.com/ueberdosis/tiptap/blob/v3.31.4/packages/extension-image/src/image.ts),
[v3.31.4 YouTube](https://github.com/ueberdosis/tiptap/blob/v3.31.4/packages/extension-youtube/src/youtube.ts),
[v3.31.4 TableCell](https://github.com/ueberdosis/tiptap/blob/v3.31.4/packages/extension-table/src/cell/table-cell.ts).
Yerel exact3.31.4 list/link/heading/code kaynakları da incelendi.
Sunucu şeması bu kaynaklardan dar bir uygulama sözleşmesidir; npm şeması
sunucu güvenlik sınırı yerine geçmez.

## Atomik kalıcı kayıt (F08)

`PostDocumentService.SaveAsync(postId, expectedEditVersion, documentJson, token)`
yalnız pozitif yazı kimliği ve1..long.MaxValue−1 beklenen düzenleme sürümü alır.
F05 validator aynı immutable belgeden F06 türevlerini üretir; hatalı input
persistence'a ulaşmaz. HTML, türev, sayaç, slug veya yayın alanı input değildir.
Doğrulanmış Tiptap v1 zarfı byte anlamı/whitespace korunarak metin olarak saklanır;
Core ağacının varsayılan serializer çıktısı wire format yerine kullanılmaz.

Core'daki `IPostDocumentRepository` yalnız dar snapshot ve koşullu belge yazımı
sunar. Data tek `UPDATE WHERE Id AND EditVersion` içinde DocumentVersion,
DocumentJson, DocumentPlainText, DocumentWordCount, DocumentReadingMinutes,
UTC UpdatedDate ve EditVersion+1 alanlarını açık eşler. PostgreSQL statement
transaction'ı bunların tamamını atomik yapar; ek çok adımlı transaction veya
tracked entity'nin bütün satırını güncelleme yoktur. Slug, Content, sayaç, medya,
CreatedDate, sınıflandırma ve site yayın durumu korunur. Başarılı commit mevcut
public liste cache'ini geçersizleştirir. DB/cache I/O hatası ve cancellation başarıya
çevrilmez; commit sonrasındaki cache/cancellation hatası kaydın geri alındığını
kanıtlamaz. HTTP entegrasyonu yeniden okuyarak sürümü değerlendirmelidir.

`ContentDocuments` additive migration'ı eski alan ve migration geçmişini tutar.
Beş belge alanı legacy satırda bütünüyle null, belge satırında bütünüyle doludur.
DB CHECK açık IS NULL/IS NOT NULL terimleriyle kısmi null takımı reddeder;
version1, JSON1..1.048.576 UTF-8 byte, plain text en çok1.048.576 byte,
word count0..200.000 ve minutes=(words+199)/200 şarttır. Metin sınırı doğrulanmış
200.000 rune ile ek blok/hardBreak LF ayırıcılarını kapsar; plain text200.000
rune'a kesilmez. CHECK tam JSON şeması veya metin eşitliği doğrulaması değildir.

`ReadAsync` NotFound/Legacy/Inconsistent/Ready ayrımı yapar. Kaydedilmiş zarf
aynı validator'dan tekrar geçer; metin/kelime/dakika yeniden türetilip kolonlarla
karşılaştırılır. Tutarsız kayıttan trusted document/HTML üretilmez. Boş veya
medyadan oluşan geçerli belgede0 kelime/0 dakika kabul edilir.

Eski Markdown Create/webhook belge alanlarını kabul etmez. JSON kayıtlarının
eski Edit yoluyla değiştirilmesi servis seviyesinde conflict olur; gerçek MVC
Edit409/no-store döner, kullanıcı input'unu formda tutar ve bu kontrol upload'dan
önce yapılır. Belge kaydı yayın metadata'sını değiştirmez; mevcut legacy toggle
da JSON satırlarında bu güvenli ret sınırına takılır. F09 form entegrasyonunda
yayın eylemleri JSON'a uygun atomik metadata/kayıt sınırıyla ele alınmalıdır.
Yeni genel HTTP endpoint'i eklenmez; F09 temel Create/Edit yolculuğunu
aynı belge sınırına bağlayacaktır. Bu faz mevcut public Markdown gösterimini,
arama sorgusunu, JSON kurtarmayı veya eski veriyi değiştirmez.

Down, tabloyu transaction boyunca kilitleyip herhangi bir belge alanı doluysa
reddeder; kolonlar ve veri kalır. Veri taşımayan disposable DB downgrade edilebilir.
Belge verisi varken önceki binary'ye dönüş otomatik uyumlu sayılmaz; forward repair
veya yeni kayıt yolunun kapatılması veri kaybetmeyen sınırdır. Gerçek yapılandırılmış
DB'ye migration uygulanmadı.

`ContentRulesTool --documents` yalnız runner'ın owned cluster'ında kayıt/okuma,
invalid input'ta sıfır I/O, stale ve paralel edit, sayaç yarışı, constraint rollback,
legacy yazıcı reddi, en büyük Unicode metin ve1MiB zarf sınırını doğrular.
`document_persistence_http_probe.py` gelecekteki HTTP eşlemesi için geçici kaynakta
oluşan auth/CSRF korumalı adapter'ı ve gerçek legacy Edit409'u ayırır. Adapter
üretim kaynak/publish yüzeyi değildir. Boş ve hemen önceki ContentAccess şemasından
yükseltme bütün eski kolonları korur; dolu veride gerçek EF down reddi sınanır.

Resmi atomiklik/concurrency kaynağı: [EF Core ExecuteUpdate](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete).
SQL null semantiği: [PostgreSQL CHECK constraints](https://www.postgresql.org/docs/current/ddl-constraints.html).

## Ortak JSON yazım formu (F09)

Yazı listesi ve dashboard'daki yeni yazı bağlantısı `AdminWriting/Create` formunu
açar; JSON satırlarının eski yönetici Edit bağlantısı `AdminWriting/Edit/{id}`
formuna yönlenir. İki ekran aynı Türkçe Razor partial ve yerel Tiptap modülünü
kullanır. Geçişte eski Markdown Create/Edit/Preview ve nottan taslak aktarma
adresleri korunur; webhook formatı değişmez. Bu geçici uyumluluk veri göçü değildir.

`PostWritingService`, izinli metadata ile v1 zarfını doğrular ve metin/okuma
türevlerini aynı snapshot'tan üretir. Core `IEditorPostRepository` sınırı Create'te
tek INSERT, Edit'te beklenen EditVersion ve document version1 koşuluyla tek UPDATE
kullanır. Başlık, özet, kategori, açıklama, etkinlik ve site yayın bilgileri bütün
belge alanlarıyla birlikte kaydedilir. Slug benzersizliği mevcut DB indeksiyle
korunur; edit slug/sayaç/CreatedDate/eski Content/erişim/tür/kapak kimliğini yazmaz.
Normal Save yayın iznini ve kayıtlı tam UTC anını korur; eksik eylem SaveDraft'tır.
SaveDraft/Schedule/Publish mevcut ortak yayın kuralını kullanır; e-posta üretmez.

Yetkili/no-store POST'ta antiforgery ve küçük typed form allowlist'i vardır.
1MiB UTF-8 belge sınırı validator'dadır; URL encoding için wire limiti
3×1MiB+65.536 byte, form value limiti aynı ve field count32'dir. Framework'ün
ön binding/antiforgery reddi400 olabilir. Action schema/metadata400, belge
limit413, edit409 verir. Başarısız view gönderilmiş zarf, metadata ve eski revision'ı
Razor encoding ile tutar; güncel revision sessizce benimsenmez. Başarılı commit
sonrası redirect formu yeniden okur ve tek kullanımlı Türkçe başarı mesajı gösterir.

Başlangıç zarfı yeniden doğrulanmış orijinal JSON'dur; Core ağacı wire JSON olarak
serialize edilmez. Client `getJSON()` version1 zarfıyla taşınır. Tiptap3.31.4
ücretsiz exact build: h2–h4, temel beş mark, liste, alıntı, kod, link, undo/redo;
Code excludes:'' ve HardBreak keepMarks:false. Hizalama sabit CSS class'ıyla
korunur; bu fazda yeni hizalama/medya/tablo araçları yoktur. Server-valid ama bu
istemcide desteklenmeyen node/attribute, tutarsız stored facts veya yükleme hatası
kaydı kapatır ve orijinal veriyi korur; bilinmeyen özellik sessiz düşürülmez.

Yeni form eski localStorage recovery/Markdown transfer değerini okumaz veya silmez;
JSON kurtarma F14'e aittir. Public JSON detay/arama geçişi F16, webhook F17,
kontrollü eski veri göçü F18'dir. Yeni form kabulü bu işleri tamamlamış sayılmaz.
Gerçek yapılandırılmış DB veya production migration bu kabulde kullanılmaz.

Resmî istemci kaynakları: [Tiptap Editor](https://tiptap.dev/docs/editor/api/editor),
[StarterKit](https://tiptap.dev/docs/editor/extensions/functionality/starterkit).
Exact API ve LICENSE dosyaları yerel kilitli paket/build receipt'inde doğrulanır.
