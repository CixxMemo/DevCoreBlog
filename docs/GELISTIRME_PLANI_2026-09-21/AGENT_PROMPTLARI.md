# AI agent'a verilecek hazır mesajlar

Aşağıdaki mesajlar kullanıcıdan kod yazmasını istemez. Kullanıcı yalnızca yapılacak fazı seçer; inceleme, düzenleme ve test işini agent yapar. Dosya yolları repository köküne göredir.

## İlk uygulama — yalnızca F01

```text
DevCoreBlog kökündeki AGENTS.md ve
docs/GELISTIRME_PLANI_2026-09-21/README.md, KARARLAR.md, DURUM.md dosyalarını oku.
F00 kuralların yenilenmesiyle tamamlandı; eski A/B tercihini yeniden sorma.
Bu çalıştırmada yalnızca F01'i uygula: güncel kaynakları ve mevcut değişiklikleri
koruyarak başlangıç build/izole test ortamı/başlangıç kanıtını hazırla.
Uygulama davranışını düzeltmeye veya F02'ye geçme. Gerçek secret'ları kullanma.
Sonucu ve sıradaki tek fazı Türkçe bildir; kanıt olmadan [x] işaretleme.
```

## Bir sonraki çalıştırma — sıradaki tek faz

```text
docs/GELISTIRME_PLANI_2026-09-21/README.md, KARARLAR.md, DURUM.md ve
AGENTS.md dosyalarını oku. Kararları ve önceki fazın gerçek test kanıtını
kontrol ederek sıradaki geçerli TEK atomik fazı uygula.
Değiştireceğin dosyaları önce oku; benim ve diğer agent'ların mevcut
değişikliklerini koru. Kartta yazan neden, kapsam, yöntem ve kabul ölçütlerini izle.
Kodlamayı, build'i ve gerekli davranış/tarayıcı/DB kontrollerini sen yap.
Mevcut yetki içindeki rutin teknik tercihler için benden tekrar onay isteme.
Kanıt yoksa fazı tamamlandı işaretleme. Başarılıysa faz kaydını ve DURUM.md'yi güncelle.
Bu faz bitince dur; sonraki fazı kendiliğinden başlatma.
Sonuçta neyi neden değiştirdiğini, test sonucunu ve sıradaki fazı sade Türkçe anlat.
```

## Belirli fazı seçme — örnek F11

```text
DevCoreBlog geliştirme planında yalnızca F11'i uygula.
AGENTS.md, docs/GELISTIRME_PLANI_2026-09-21/README.md,
KARARLAR.md, DURUM.md ve F11 kartını oku.
Ön koşullar tamamlanmamışsa onları gizlice atlama veya birden çok faza yayılma;
eksik ön koşulu bildir. Tamamsa F11'i kodla, kabul kontrollerini çalıştır,
kanıtını kaydet ve dur. Başka fazı başlatma.
```

## Yarıda kalan fazı sürdürme

```text
DevCoreBlog planındaki aktif/yarım kalmış faza devam et.
Önce DURUM.md, son faz kaydı, git diff ve kökteki AGENTS.md dosyasını oku.
Yapılmış işi yeniden yazma veya resetleme. Geçen testleri yeni bir değişiklik
gerektirmedikçe gereksiz tekrarlama; kalan kabul kontrollerini tamamla.
Aynı fazın dışına çıkma. Tamamlandığında kanıtı kaydet ve dur.
```

## Yalnızca sonuç kontrolü

```text
Son tamamlandığı bildirilen fazı plan kartı ve gerçek diff/test çıktısıyla denetle.
Yeni özellik ekleme. Kök AGENTS.md uyumunu, kabul ölçütlerini ve kullanıcı
değişikliklerinin korunmasını kontrol et. Kanıt yetersizse bunu açıkça yaz
ve DURUM.md'deki tamamlandı iddiasını gerekçeyle düzelt.
```

## Kullanıcının görmesi gereken kısa faz sonucu

```text
Faz: Fxx — ...
Değişen: ...
Neden: ...
Doğrulama: ... geçti / ... doğrulanamadı.
Kalan: ... (yoksa “Yok”)
Sıradaki tek faz: Fyy — ...
```

Fazın ayrıntılı teknik kanıtı dosyada kalır; kullanıcı her seferinde yüzlerce satır log okumak zorunda değildir.

