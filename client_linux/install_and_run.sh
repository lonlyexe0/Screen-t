#!/usr/bin/env bash
# ==============================================================================
# İkinci Monitör (Screen Extension) - GNOME / Arch / Garuda Otomatik Kurulum
# ==============================================================================

printf "\n====================================================\n"
printf "   İkinci Monitör İstemcisi - Otomatik Kurulum     \n"
printf "====================================================\n"

# 1. Gerekli Paketlerin Kurulumu (Arch / Garuda Pacman)
printf "\n[1/4] Gerekli paketler kontrol ediliyor (OpenCV, NumPy)...\n"
sudo pacman -S --needed --noconfirm python python-numpy python-opencv

# 2. Güvenlik Duvarı
printf "\n[2/4] Güvenlik duvarı kontrol ediliyor...\n"
if command -v ufw >/dev/null 2>&1; then
    sudo ufw allow 8080/tcp >/dev/null 2>&1 || true
fi

# 3. Uygulama Dizinini ve Dosyasını Oluştur
APP_DIR="$HOME/.local/share/second_monitor"
mkdir -p "$APP_DIR"
cat << 'EOF' > "$APP_DIR/client.py"
#!/usr/bin/env python3
import sys
import time
import urllib.request
import numpy as np
import cv2

def main():
    stream_url = sys.argv[1] if len(sys.argv) > 1 else "http://192.168.1.10:8080"
    window_name = "İkinci Monitör"

    cv2.namedWindow(window_name, cv2.WND_PROP_FULLSCREEN)
    cv2.setWindowProperty(window_name, cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_FULLSCREEN)

    print("=================================================")
    print(f"[*] Windows Yayınına Bağlanılıyor: {stream_url}")
    print("[*] Doğrudan Tam Ekran Açıldı.")
    print("[*] Kapatmak için: 'q' veya ESC tuşuna basın.")
    print("=================================================")

    while True:
        try:
            req = urllib.request.Request(stream_url, headers={'User-Agent': 'SecondMonitorClient'})
            stream = urllib.request.urlopen(req, timeout=5)
            print("[OK] Yayın başarıyla yakalandı! Görüntü aktarılıyor...")
            break
        except Exception as e:
            print(f"[*] Windows yayını bekleniyor ({stream_url})... ({e})")
            time.sleep(2)

    bytes_buf = b""
    try:
        while True:
            chunk = stream.read(8192)
            if not chunk:
                break
            bytes_buf += chunk

            a = bytes_buf.find(b'\xff\xd8')
            b = bytes_buf.find(b'\xff\xd9')

            if a != -1 and b != -1:
                jpg = bytes_buf[a:b+2]
                bytes_buf = bytes_buf[b+2:]

                if b'\xff\xd9' in bytes_buf:
                    continue

                img = cv2.imdecode(np.frombuffer(jpg, dtype=np.uint8), cv2.IMREAD_COLOR)
                if img is not None:
                    cv2.imshow(window_name, img)

            key = cv2.waitKey(1) & 0xFF
            if key in (27, ord('q')):
                break
    except KeyboardInterrupt:
        pass
    finally:
        cv2.destroyAllWindows()

if __name__ == "__main__":
    main()
EOF

chmod +x "$APP_DIR/client.py"

# 4. GNOME Uygulamalar Menüsüne Kısayol Ekle (.desktop)
printf "\n[3/4] GNOME Uygulama menüsüne kısayol ekleniyor...\n"
mkdir -p "$HOME/.local/share/applications"
cat << EOF > "$HOME/.local/share/applications/second-monitor.desktop"
[Desktop Entry]
Name=İkinci Monitör
Comment=Windows Yerel Ekran Alıcısı
Exec=python3 $APP_DIR/client.py http://192.168.1.10:8080
Icon=video-display
Terminal=false
Type=Application
Categories=Utility;Network;
EOF

update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
printf "✓ GNOME menüsüne 'İkinci Monitör' eklendi.\n"

printf "\n====================================================\n"
printf " KURULUM TAMAMLANDI!\n"
printf " Hedef: http://192.168.1.10:8080\n"
printf "====================================================\n"
printf "\n[4/4] İstemci doğrudan tam ekran açılıyor...\n"
printf "Kapatmak için: 'q' veya ESC tuşuna basın.\n\n"

python3 "$APP_DIR/client.py" "http://192.168.1.10:8080"
