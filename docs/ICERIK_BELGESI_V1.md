# İçerik belgesi v1 doğrulama sözleşmesi

Sunucu sınırı `ContentDocumentValidator.Validate(string | ReadOnlyMemory<byte>)`.
F05 bu bileşeni ve salt sentetik kabul aracını ekler; F06 bağımsız okuma türevini
ekler. HTTP, DI kullanımı, kayıt, HTML üretimi ve editör geçişi daha sonraki fazlardır. Mevcut Markdown
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
Limit→413 eşlemesini yapar; bu faz response üretmez.

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
anlamını taşır; gelecekte renderer kendi sabit güvenli rel değerini üretmelidir.

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
genişletemez. Renderer ileride bu kimlikten dar embed üretir; JSON iframe taşımaz.

Literal `<script>` gibi metin **text verisi** olarak korunabilir; bu HTML üretimi
değildir. Gelecek renderer bu değeri encode etmek zorundadır. `html` node veya
event/style attribute'ları reddedilir. Doğrulanmış model `Html.Raw` güveni vermez.

## Okuma türevi (F06)

`DocumentTextProducer.Produce(ValidatedContentDocument)` aynı doğrulanmış snapshot'tan
immutable `DocumentReading` üretir: PlainText, WordCount, ReadingMinutes ve
belge sırasındaki immutable Headings. JSON yeniden parse edilmez; HTML üretilmez.
Bu bileşen henüz kayıt/arama/UI'ya bağlanmaz.

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
değildir. F07 renderer bu aynı sıradaki kimlikleri kullanmalıdır.

Türev metin ve başlık hâlâ plain data'dır; `<script>` gibi literal text
korunabilir. HTML/Razor/e-posta tüketicisi kendi doğrulanmış encoding sınırını
uygulamalıdır. F06 sonuçları `Html.Raw` güveni vermez.

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
