#!/usr/bin/env python3
"""
İkinci Monitör - Linux İstemcisi (HTTP MJPEG Zero-Lag Stream)
Windows'un yerel yayınına bağlanır ve doğrudan tam ekran gösterir.
"""

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

            a = bytes_buf.find(b'\xff\xd8') # JPEG SOI (Start)
            b = bytes_buf.find(b'\xff\xd9') # JPEG EOI (End)

            if a != -1 and b != -1:
                jpg = bytes_buf[a:b+2]
                bytes_buf = bytes_buf[b+2:]

                # Zero-Lag Politikası: Eğer tamponda daha yeni bir kare biriktiyse eskiyi at!
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
