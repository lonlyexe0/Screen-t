#!/usr/bin/env python3
"""
Low-Latency Screen Extension Server (Python Prototype)
Auto-detects and streams the Secondary/Virtual Display.
"""

import sys
import time
import socket
import struct
import ctypes
from ctypes import wintypes
import numpy as np
import cv2

MAGIC = 0x5343524E  # 'SCRN'
CHUNK_SIZE = 1400

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32

SRCCOPY = 0x00CC0020
DIB_RGB_COLORS = 0
BI_RGB = 0

class RECT(ctypes.Structure):
    _fields_ = [
        ('left', wintypes.LONG),
        ('top', wintypes.LONG),
        ('right', wintypes.LONG),
        ('bottom', wintypes.LONG)
    ]

class MONITORINFOEX(ctypes.Structure):
    _fields_ = [
        ('cbSize', wintypes.DWORD),
        ('rcMonitor', RECT),
        ('rcWork', RECT),
        ('dwFlags', wintypes.DWORD),
        ('szDevice', wintypes.WCHAR * 32)
    ]

def get_monitors():
    monitors = []
    def monitor_enum_proc(hMonitor, hdcMonitor, lprcMonitor, dwData):
        info = MONITORINFOEX()
        info.cbSize = ctypes.sizeof(MONITORINFOEX)
        if user32.GetMonitorInfoW(hMonitor, ctypes.byref(info)):
            r = info.rcMonitor
            is_primary = bool(info.dwFlags & 1)
            monitors.append({
                'name': info.szDevice,
                'x': r.left,
                'y': r.top,
                'width': r.right - r.left,
                'height': r.bottom - r.top,
                'primary': is_primary
            })
        return True

    MONITOR_ENUM_PROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HMONITOR, wintypes.HDC, ctypes.POINTER(RECT), wintypes.LPARAM)
    user32.EnumDisplayMonitors(0, 0, MONITOR_ENUM_PROC(monitor_enum_proc), 0)
    return monitors

class FastScreenCapture:
    def __init__(self, x=0, y=0, width=1920, height=1080):
        self.x = x
        self.y = y
        self.width = width
        self.height = height

        self.h_screen_dc = user32.GetDC(0)
        self.h_memory_dc = gdi32.CreateCompatibleDC(self.h_screen_dc)
        self.h_bitmap = gdi32.CreateCompatibleBitmap(self.h_screen_dc, self.width, self.height)
        gdi32.SelectObject(self.h_memory_dc, self.h_bitmap)

        class BITMAPINFOHEADER(ctypes.Structure):
            _fields_ = [
                ('biSize', wintypes.DWORD),
                ('biWidth', wintypes.LONG),
                ('biHeight', wintypes.LONG),
                ('biPlanes', wintypes.WORD),
                ('biBitCount', wintypes.WORD),
                ('biCompression', wintypes.DWORD),
                ('biSizeImage', wintypes.DWORD),
                ('biXPelsPerMeter', wintypes.LONG),
                ('biYPelsPerMeter', wintypes.LONG),
                ('biClrUsed', wintypes.DWORD),
                ('biClrImportant', wintypes.DWORD)
            ]

        class BITMAPINFO(ctypes.Structure):
            _fields_ = [
                ('bmiHeader', BITMAPINFOHEADER),
                ('bmiColors', wintypes.DWORD * 3)
            ]

        self.bmi = BITMAPINFO()
        self.bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        self.bmi.bmiHeader.biWidth = self.width
        self.bmi.bmiHeader.biHeight = -self.height
        self.bmi.bmiHeader.biPlanes = 1
        self.bmi.bmiHeader.biBitCount = 32
        self.bmi.bmiHeader.biCompression = BI_RGB

        self.buffer = np.zeros((self.height, self.width, 4), dtype=np.uint8)

    def capture(self):
        gdi32.BitBlt(
            self.h_memory_dc, 0, 0, self.width, self.height,
            self.h_screen_dc, self.x, self.y, SRCCOPY
        )
        gdi32.GetDIBits(
            self.h_memory_dc, self.h_bitmap, 0, self.height,
            self.buffer.ctypes.data_as(ctypes.c_void_p),
            ctypes.byref(self.bmi), DIB_RGB_COLORS
        )
        return self.buffer[:, :, :3]

    def close(self):
        gdi32.DeleteObject(self.h_bitmap)
        gdi32.DeleteDC(self.h_memory_dc)
        user32.ReleaseDC(0, self.h_screen_dc)


def send_frame(sock, target_addr, frame_id, jpeg_bytes):
    total_len = len(jpeg_bytes)
    total_chunks = (total_len + CHUNK_SIZE - 1) // CHUNK_SIZE

    for chunk_idx in range(total_chunks):
        offset = chunk_idx * CHUNK_SIZE
        chunk_data = jpeg_bytes[offset : offset + CHUNK_SIZE]
        payload_len = len(chunk_data)

        header = struct.pack("!IIHHI", MAGIC, frame_id, chunk_idx, total_chunks, payload_len)
        sock.sendto(header + chunk_data, target_addr)


def main():
    target_ip = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
    target_port = int(sys.argv[2]) if len(sys.argv) > 2 else 9999
    target_addr = (target_ip, target_port)

    monitors = get_monitors()
    print("=================================================")
    print(" Screen Extension Server (Python)                ")
    print("=================================================")
    print("[INFO] Algilanan Ekranlar:")
    target_mon = None
    for i, m in enumerate(monitors):
        kind = "Ana Ekran" if m['primary'] else "IKINCI / SANAL EKRAN"
        print(f"  [{i}] {m['name']} ({m['width']}x{m['height']} at {m['x']},{m['y']}) -> {kind}")
        if not m['primary'] and target_mon is None:
            target_mon = m

    if target_mon is None and monitors:
        target_mon = monitors[0]

    print(f"\n[INFO] Hedef Ekran: {target_mon['name']} ({target_mon['width']}x{target_mon['height']})")
    print(f"[INFO] Hedef Linux: {target_ip}:{target_port} (60 FPS)")
    print("=================================================")

    cap = FastScreenCapture(x=target_mon['x'], y=target_mon['y'], width=target_mon['width'], height=target_mon['height'])
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    frame_id = 0
    encode_params = [int(cv2.IMWRITE_JPEG_QUALITY), 75]
    frame_interval = 1.0 / 60

    try:
        while True:
            img_bgr = cap.capture()

            # Canli Fare Imlecini Ciz
            pt = wintypes.POINT()
            if user32.GetCursorPos(ctypes.byref(pt)):
                cx = pt.x - target_mon['x']
                cy = pt.y - target_mon['y']
                if 0 <= cx < target_mon['width'] and 0 <= cy < target_mon['height']:
                    pts = np.array([
                        [cx, cy],
                        [cx, cy + 20],
                        [cx + 5, cy + 16],
                        [cx + 9, cy + 25],
                        [cx + 13, cy + 23],
                        [cx + 9, cy + 15],
                        [cx + 15, cy + 15]
                    ], np.int32)
                    cv2.fillPoly(img_bgr, [pts], (255, 255, 255))
                    cv2.polylines(img_bgr, [pts], True, (0, 0, 0), 2)

            frame_id += 1

            success, enc = cv2.imencode('.jpg', img_bgr, encode_params)
            if success:
                send_frame(sock, target_addr, frame_id, enc.tobytes())

            elapsed = time.perf_counter() - t0
            rem = frame_interval - elapsed
            if rem > 0:
                time.sleep(rem)
    except KeyboardInterrupt:
        print("\n[INFO] Durduruldu.")
    finally:
        cap.close()
        sock.close()


if __name__ == "__main__":
    main()
