# Faz kanıtı şablonu

Bu şablonu tamamlanan/engellenen faz için docs/uygulama-kayitlari/Fxx-YYYY-MM-DD.md konumuna kopyala. Bu şablon bir test sonucu değildir.

## Kimlik

- Faz:
- Tarih:
- Uygulanan AGENTS.md sürümü ve kararlar:
- Başlangıç git durumu / korunmuş kullanıcı değişiklikleri:
- Ön koşul kayıtları:

## Ne ve neden değişti?

Kullanıcının anlayacağı kısa açıklama.

## Değişen dosyalar

Yalnızca bu fazın dosyaları ve değişiklik nedeni. Kullanıcının önceden değişmiş dosyasıysa katkıyı ayır. Secret/ham cookie içeriği yazma.

## Doğrulama

| Kontrol | Komut veya etkileşim | Ortam | Beklenen | Gerçek sonuç | Kanıt |
|---|---|---|---|---|---|
| Derleme | | | | | |
| Faz davranışı | | | | | |
| İlgili regresyon | | | | | |
| UI varsa tarayıcı | | | | | |
| Migration varsa boş/önceki DB | | | | | |

Uygun olmayan satırı UYGULANAMAZ ve gerekçesiyle belirt. Çalıştırılmayanı GEÇTİ yazma.
Tarayıcı ekran görüntüsü yolu, test logu veya script çıktısı bağlantısı ver; parola/token içeren çıktı saklama.

## Kurallara uyum

- Mevcut yığın ve seçilmiş proje/API sınırı:
- Input DTO/ViewModel gerekiyorsa kapsam ve açık eşleme; gereksiz mapper/Identity/framework eklenmediğinin kontrolü:
- English UI/comments ve Tech Minimal etkisi:
- Faz dışı değişiklik kontrolü:

## Veri ve geri dönüş

- Migration/kalıcı veri etkisi:
- Geri dönüş için gereken:
- Geri alınırsa yeniden açılabilecek güvenlik riski:
- Gerçek ortamda yapılmayanlar:

## Sonuç

- TAMAMLANDI / DOĞRULANAMADI / ENGELLİ / UYGULANAMAZ:
- Kalan somut engel:
- DURUM.md güncellendi mi?
- Sıradaki tek faz:

