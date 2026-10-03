# F43 rota envanteri

Başlangıç HEAD: `8b7c3b6` (F42 tamamlandı). [Başlangıç ölçümü](baseline.json).
Program named route sırası: post-en, post, category-en, category, default MVC.
Repo bağlantıları: kart/çok okunan/kategori menüleri Türkçe; admin LIVE ve feed
İngilizce; sitemap Türkçe ve Request.Host origin kullanıyordu. Repository içinde
bu tercihle çakışan dış istemci sözleşmesi bulunmadı; canlı dış trafik ölçülmedi.

| Yol | Önce | F43 |
|---|---|---|
| `/post/{slug}` görünür | 200 | 200 kanonik |
| `/yazi/{slug}` görünür | 200 | tek yerel 301 → `/post/{slug}` |
| `/category/{slug}` aktif | 200 | 200 kanonik |
| `/kategori/{slug}` aktif | 200 | tek yerel 301 → `/category/{slug}` |
| `/Home/Detail?slug=…` | default action alternatifi | tek yerel 301, query korunur |
| `/Home/Category?slug=…` | default action alternatifi | tek yerel 301, query korunur |
| kanonik yolda sondaki `/` | aynı action alternatifi | tek yerel 301 |
| bulunmayan/gizli yazı veya kategori | yayın/input sınırı | 404, Location yok |
| `/ara` | mevcut arama | değişmedi |

Query sırası/tekrarlı alanlar encode biçimiyle korunur. Slug yeniden üretilmez.
Özel slug `ğüş %?#` tek path segmentinde encode edilip doğru kaydı açar.
Mutlak origin `SITE_URL`; relative Location yerel path'tir. Kanonik kategori
paging Tag Helper'ı aynı English route'u seçer. [49 HTTP/DB kontrolü](urls.log)
ve [gerçek browser](browser.json) bu sözleşmeyi doğruladı.
