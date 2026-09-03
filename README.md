# 🖥️ İkinci Ekran (Second Screen)

Windows üzerinde sanal veya fiziksel ikinci monitör oluşturarak, görüntüyü yerel ağ (LAN) üzerinden Linux (GNOME, Arch, Ubuntu vb.), tablet veya diğer cihazlara ultra düşük gecikme ile yansıtan açık kaynaklı ekran genişletme ve yayın aracı.

---

## ✨ Özellikler

- **⚡ Ultra Düşük Gecikme:** Windows DXGI Desktop Duplication API (`Vortice.Direct3D11` & `Vortice.DXGI`) ile doğrudan ekran kartı belleğinden yakalama.
- **🎛️ Modern WPF Arayüzü:** Canlı ekran seçimi, çözünürlük/FPS ayarları, anlık istemci sayısı ve yerel ağ bağlantı linkleri.
- **🔌 Sanal Ekran (Virtual Display) Desteği:** Tek tıkla sanal monitörü açma (`Enable-PnpDevice`) ve kapatma (`Disable-PnpDevice`).
- **📥 Sistem Tepsisi (System Tray) Entegrasyonu:** Pencere kapatıldığında arka planda kesintisiz çalışmaya devam eder, tepsiden yönetilebilir.
- **🐧 Çapraz Platform İstemci Desteği:** Linux istemcisi (`client_linux`) OpenCV tabanlı tam ekran pencere oluşturur ve masaüstü menüsüne entegre olur.
- **🚀 Kurulum ve Kısayol Kolaylığı:** Başlat Menüsü ve Masaüstü kısayollarını otomatik oluşturan kurulum betiği.

---

## 📁 Proje Yapısı

```text
├── IkinciEkran/             # Modern WPF Yönetim ve Yayın Uygulaması
│   ├── MainWindow.xaml      # Arayüz tasarımı
│   ├── Services/            # DXGI ekran yakalama ve HTTP yayın motoru
│   ├── Tray/                # Windows Bildirim Alanı (Tray) yönetimi
│   └── publish/             # Derlenmiş hazır çalıştırılabilir paket
├── client_linux/            # Linux istemci betikleri (OpenCV tabanlı)
│   ├── client.py            # Python tam ekran alıcı istemci
│   └── install_and_run.sh   # Arch/Ubuntu/GNOME tek tıkla kurulum
├── server_csharp/           # Konsol tabanlı alternatif C# yayın sunucusu
├── server_python/           # Alternatif Python yayın sunucusu
├── Kurulum_ve_Kisayol_Olustur.bat # Windows Başlat ve Masaüstü kısayol kurulumu
├── ac_ikinci_ekran.bat      # Sanal monitörü elle etkinleştirme
├── kapat_ikinci_ekran.bat   # Sanal monitörü elle kapatma
└── start_server.bat         # Konsol sunucusu başlatıcı
```

---

## 🚀 Hızlı Başlangıç

### 1. Windows (Sunucu)
1. Projeyi indirin veya klonlayın:
   ```bash
   git clone https://github.com/<kullanici-adiniz>/ikinci-ekran.git
   cd ikinci-ekran
   ```
2. **`Kurulum_ve_Kisayol_Olustur.bat`** dosyasını çalıştırın.
   - Bu işlem Windows Başlat Menüsü ve Masaüstünüze `İkinci Ekran` kısayolunu ekleyecektir.
3. Uygulamayı başlatın:
   - **"Yayını Başlat"** butonuna tıkladığınızda sanal ekran otomatik etkinleştirilir ve yayın başlar.
   - Ekranda görüntülenen yerel IP adresini (örn: `http://192.168.1.X:8080`) not edin.

### 2. Linux (İstemci)
İkinci ekran olarak kullanacağınız Linux makinesinde:
```bash
cd client_linux
chmod +x install_and_run.sh
./install_and_run.sh
```
Veya doğrudan Python ile:
```bash
python3 client.py http://<WINDOWS_IP_ADRESI>:8080
```
> Tam ekrandan çıkmak veya kapatmak için **`q`** veya **`ESC`** tuşuna basabilirsiniz.

---

## 🛠️ Geliştirme ve Derleme

- **Gereksinimler:**
  - Windows 10 / 11 (64-bit)
  - .NET 9.0 SDK
  - Virtual Display Driver (IddCx / USBMMIdd vb.)
- **Derleme:**
  ```powershell
  cd IkinciEkran
  dotnet build -c Release
  dotnet publish -c Release -o publish
  ```

---

## 📄 Lisans
Bu proje MIT lisansı altında sunulmaktadır.
