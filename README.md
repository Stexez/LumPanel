# LumPanel — LUM Craft sunucu yönetim paneli

VDS'te çalışan bütün programları (LumSMP, LumCraft, Velocity, web sitesi, Discord botu, agent, Caddy) **tek pencereden** ve **arka planda** yöneten Windows uygulaması. CMD pencereleri açılmaz; her programın çıktısı panelin içinde görünür.

Tek dosya: `LumPanel.exe` (.NET Framework 4.8; Windows Server / Windows 10+ üzerinde kurulum gerektirmez).

## Kurulum (VDS)

1. `LumPanel.exe` dosyasını bir klasöre koy (ör. `C:\LumCraft\Panel\`).
2. Çift tıkla. İlk açılışta site ve Caddy hazır gelir.
3. **Ayarlar → Otomatik bul** ile Minecraft sunucularını, Velocity'yi, botu ve agent'ı ekle; kontrol et, **Kaydet**.
4. Şu an açık olan CMD pencerelerini kapat (ya da panelde turuncu "panel dışında açık" görünenleri **Durdur**). Sonra **Hepsini sırayla başlat**.
5. Site eskiden Windows zamanlanmış göreviyle açılıyorsa: **Ayarlar → Sitenin eski zamanlanmış görevini kapat**.
6. **Ayarlar → Windows açılınca LumPanel'i başlat** işaretle (VDS yeniden başlayınca her şey kendiliğinden açılır).

Ayarlar `lumpanel.json`, olaylar `lumpanel-olaylar.log`, yedekler `yedekler\` klasöründe (exe'nin yanında) tutulur.

## Sayfalar

| Sayfa | Ne işe yarar |
|---|---|
| Genel Bakış | LumSMP büyük kart: yeşil/kırmızı ışık, AÇ/KAPAT, oyuncu, CPU/RAM grafiği; diğer servislerin kartları; sistem özeti |
| Konsollar | Her programın canlı çıktısı (uyarı sarı, hata kırmızı), komut gönderme, ↑/↓ komut geçmişi, arama, hızlı komut butonları, logu kaydet/kopyala |
| Oyuncular | Çevrimiçi oyuncular; özel mesaj, at, yasakla, OP, whitelist, oyun modu; herkese duyuru / ekrana başlık |
| Yapay Zeka | Konsol hatalarını yakalar, ne olduğunu ve nasıl düzeltileceğini anlatır; sohbet |
| Zamanlayıcı | Her gün yeniden başlatma (oyunculara 10/5/1 dk, 10 sn uyarı), her gün/aralıkla komut, her gün yedek |
| Yedekler | Dünya yedeği (sunucu açıkken güvenli), eski yedekleri otomatik silme |
| Olay Kaydı | Başlatma, durdurma, çökme, zamanlanmış görevler |
| Ayarlar | Servis ekle/düzenle/sırala, otomatik bul, Discord bildirimi, Windows ile başlat |

Çöken servis otomatik yeniden başlatılır (10 dakikada 5'ten fazla çökerse durur ve uyarır). Discord webhook girilirse çökmeler Discord'a bildirilir. Pencere kapatılınca panel sağ alttaki simgeye küçülür; servisler çalışmaya devam eder.

## Yapay zeka — ek ücret yok

Varsayılan bağlantı **Claude Code**: panel, VDS'te kurulu Claude Code'u kullanır; kullanım **kendi Claude planından** düşer, API ücreti yoktur.

1. VDS'te PowerShell: `irm https://claude.ai/install.ps1 | iex`
2. Bir kez `claude` yazıp kendi hesabınla giriş yap.

En küçük model (Haiku) kullanılır, düşük efor, kısa yanıt; günlük istek sınırı Ayarlar'da (varsayılan 40; otomatik açıklamalar bunun en fazla yarısını kullanır). Claude Code araçları kapalı çalışır (dosya okuyamaz, komut çalıştıramaz); konsol satırları yalnızca okunur. Konsoldan gönderilen satırlar Anthropic'e iletilir.

İstersen Ayarlar'dan **API anahtarı** bağlantısına geçebilirsin (kullanım başına ücretli; anahtar Windows şifrelemesiyle saklanır).

## Otomatik güncelleme

Panel 6 saatte bir (ve açıldıktan 1 dk sonra) GitHub'daki son sürüme bakar (Ayarlar → Güncelleme, varsayılan depo `Stexez/LumPanel`). Yeni sürüm varsa sol menüde yeşil "güncelle" butonu çıkar; "otomatik kur" açıksa sunucularda oyuncu yokken kendisi kurar. Kurulum: servisler düzgünce kapatılır, exe değiştirilir (eskisi `LumPanel.exe.old` olarak kalır), panel yeniden açılır ve servisleri başlatır. `lumpanel.json`, loglar ve yedekler korunur.

**Yeni sürüm yayınlamak:**
1. `src/Program.cs` içindeki `AssemblyVersion` değerini artır (ör. `1.1.0.0` → `1.2.0.0`) ve `build.ps1` çalıştır.
2. GitHub'da depoda **Releases → Draft a new release**: etiket `v1.2.0`, açıklamaya değişiklikleri yaz.
3. `dist\LumPanel.exe` ve `dist\LumPanel.exe.sha256` dosyalarını sürüme ekle → **Publish release**.

Depo herkese açık olmalı (gizli depoda Ayarlar'a erişim anahtarı gerekir).

## Derleme

`build.ps1` (bilgisayarda .NET SDK ve .NET Framework 4.8 referans paketleri olmalı) → `dist\LumPanel.exe`.

Test: `test\fake_server.py` sahte Paper/Velocity konsoludur. `LumPanel.exe --config test\lumpanel-test.json --shot <klasör> --start lumsmp` her sayfanın ekran görüntüsünü alır; `--detect <dosya>` otomatik bul sonucunu yazar.
