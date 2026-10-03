"""Compose a review sheet from actual Godot portrait captures; never redraw game UI."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "_ui_mobile"
SCREENS = [
    ("title", "标题画面"),
    ("tab0", "据点地图"),
    ("tab3", "操作入口"),
    ("entry1", "交易页面"),
    ("entry4", "角色状态"),
    ("combat", "战斗画面 · 测试遭遇"),
]
FONT = "C:/Windows/Fonts/simsun.ttc"
thumb_w, thumb_h = 360, 780
margin, gap, top, cell_h = 32, 32, 154, 850
sheet = Image.new("RGB", (1208, 1886), (18, 18, 18))
draw = ImageDraw.Draw(sheet)
heading = ImageFont.truetype(FONT, 36)
body = ImageFont.truetype(FONT, 23)
draw.text((margin, 28), "手机竖屏适配 · 效果截图", font=heading, fill=(235, 235, 235))
draw.text((margin, 84), "1080 × 2340 原始画布  /  Godot 实际渲染  /  非真机截图", font=body, fill=(175, 175, 175))
for i, (tag, label) in enumerate(SCREENS):
    source = OUT / f"portrait_final_{tag}.png"
    with Image.open(source) as image:
        assert image.size == (1080, 2340), (source, image.size)
        thumbnail = image.convert("RGB").resize((thumb_w, thumb_h), Image.Resampling.LANCZOS)
    x = margin + (i % 3) * (thumb_w + gap)
    y = top + (i // 3) * cell_h
    draw.text((x, y - 34), label, font=body, fill=(225, 225, 225))
    sheet.paste(thumbnail, (x, y))
    draw.rectangle((x - 1, y - 1, x + thumb_w, y + thumb_h), outline=(75, 75, 75), width=1)

sheet_path = OUT / "portrait_overview.png"
sheet.save(sheet_path)
with ZipFile(OUT / "portrait_screenshots.zip", "w", ZIP_DEFLATED) as archive:
    for path in sorted(OUT.glob("portrait_final_*.png")):
        archive.write(path, path.name)
    archive.write(sheet_path, sheet_path.name)
print(sheet_path)
print(OUT / "portrait_screenshots.zip")
