import os
import sys
import time
import io
import urllib.request
import zipfile

URL = "https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_mono_export_templates.tpz"
TARGET_DIR = r"C:\Users\Administrator\AppData\Roaming\Godot\export_templates\4.7.stable.mono"

class BufferedRemoteSeekable:
    def __init__(self, url, length, buffer_size=8 * 1024 * 1024):
        self.url = url
        self.length = length
        self.buffer_size = buffer_size
        self.pos = 0
        self.buf = b""
        self.buf_start = 0

    def seek(self, offset, whence=io.SEEK_SET):
        if whence == io.SEEK_SET:
            self.pos = offset
        elif whence == io.SEEK_CUR:
            self.pos += offset
        elif whence == io.SEEK_END:
            self.pos = self.length + offset
        return self.pos

    def tell(self):
        return self.pos

    def seekable(self):
        return True

    def close(self):
        pass

    def read(self, size=-1):
        if size == 0:
            return b""
        if size < 0:
            size = self.length - self.pos

        # Check if the requested range is inside current buffer
        buf_end = self.buf_start + len(self.buf)
        if self.buf_start <= self.pos < buf_end:
            offset_in_buf = self.pos - self.buf_start
            avail = len(self.buf) - offset_in_buf
            if avail >= size:
                res = self.buf[offset_in_buf : offset_in_buf + size]
                self.pos += len(res)
                return res

        # Not fully inside buffer, fetch a new buffer window
        fetch_size = max(size, self.buffer_size)
        end = min(self.pos + fetch_size - 1, self.length - 1)
        if self.pos > end:
            return b""

        req = urllib.request.Request(self.url, headers={
            "User-Agent": "Mozilla/5.0",
            "Range": f"bytes={self.pos}-{end}"
        })
        with urllib.request.urlopen(req) as resp:
            data = resp.read()
            self.buf = data
            self.buf_start = self.pos
            res = data[:size]
            self.pos += len(res)
            return res

def main():
    print("=== 开始解析官方 Godot 4.7.stable.mono 导出模板 ===")
    os.makedirs(TARGET_DIR, exist_ok=True)

    opener = urllib.request.build_opener(urllib.request.HTTPRedirectHandler)
    req = urllib.request.Request(URL, headers={"User-Agent": "Mozilla/5.0"})
    with opener.open(req) as res:
        final_url = res.geturl()
        total_len = int(res.headers.get("Content-Length", 0))

    print(f"解析到资源地址，文件总长: {total_len / 1024 / 1024:.1f} MB")

    remote = BufferedRemoteSeekable(final_url, total_len)
    zf = zipfile.ZipFile(remote)

    # 提取目标：android_debug.apk, android_release.apk, android_source.zip, version.txt
    targets = [
        "templates/version.txt",
        "templates/android_debug.apk",
        "templates/android_release.apk",
        "templates/android_source.zip"
    ]

    for t in targets:
        try:
            info = zf.getinfo(t)
        except KeyError:
            print(f"警告: 未找到 {t}")
            continue

        out_name = os.path.basename(t)
        out_path = os.path.join(TARGET_DIR, out_name)
        print(f"正在下载并提取: {out_name} ({info.file_size / 1024 / 1024:.2f} MB)...")
        start_t = time.time()

        with zf.open(info) as src, open(out_path, "wb") as dst:
            downloaded = 0
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                dst.write(chunk)
                downloaded += len(chunk)
                mb = downloaded / 1024 / 1024
                percent = downloaded * 100 / info.file_size
                elapsed = time.time() - start_t
                speed = mb / max(0.1, elapsed)
                print(f"\r  进度: {percent:5.1f}% ({mb:6.1f} MB) - 速度: {speed:5.2f} MB/s", end="", flush=True)
            print()

        print(f"✔ 已保存至: {out_path}")

    print("\n✅ Android 官方 Mono 导出模板安装完成！")

if __name__ == "__main__":
    main()
