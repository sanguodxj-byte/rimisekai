"""Compose an overview and ZIP from actual Godot portrait captures, without redrawing UI."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "_ui_mobile"
PREFIX = "portrait_complete"
SCREENS = [
    ("tab0", "据点地图 · 角色标识"),
    ("entry0", "库存 · 现有图标"),
    ("entry5", "技能星盘 · 全景"),
    ("skills_focus", "技能星盘 · 聚焦"),
    ("combat", "战斗 · 四人错层头像"),
    ("boss2", "Boss · 多格透视敌阵"),
]
FONT = "C:/Windows/Fonts/simsun.ttc"
thumb_w, thumb_h = 360, 780
margin, gap, top, cell_h = 32, 32, 164, 850
sheet = Image.new("RGB", (1208, 1900), (18, 18, 18))
draw = ImageDraw.Draw(sheet)
heading = ImageFont.truetype(FONT, 36)
body = ImageFont.truetype(FONT, 23)
draw.text((margin, 28), "竖屏界面 · 验收截图", font=heading, fill=(235, 235, 235))
draw.text((margin, 84), "1080 × 2340  /  Godot 实际渲染  /  测试状态  /  非真机实拍", font=body, fill=(175, 175, 175))
for i, (tag, label) in enumerate(SCREENS):
    source = OUT / f"{PREFIX}_{tag}.png"
    with Image.open(source) as image:
        assert image.size == (1080, 2340), (source, image.size)
        thumbnail = image.convert("RGB").resize((thumb_w, thumb_h), Image.Resampling.LANCZOS)
    x = margin + (i % 3) * (thumb_w + gap)
    y = top + (i // 3) * cell_h
    draw.text((x, y - 34), label, font=body, fill=(225, 225, 225))
    sheet.paste(thumbnail, (x, y))
    draw.rectangle((x - 1, y - 1, x + thumb_w, y + thumb_h), outline=(75, 75, 75), width=1)

draw.text((margin, 1830), "战斗人物和敌人图像槽保持空白，已预留图片接口。", font=body, fill=(175, 175, 175))
sheet_path = OUT / "portrait_overview.png"
sheet.save(sheet_path)
sources = sorted(OUT.glob(f"{PREFIX}_*.png"))
with ZipFile(OUT / "portrait_screenshots.zip", "w", ZIP_DEFLATED) as archive:
    for path in sources:
        with Image.open(path) as image:
            assert image.size == (1080, 2340), (path, image.size)
        archive.write(path, path.name)
    archive.write(sheet_path, sheet_path.name)
print(f"{len(sources)} actual 1080x2340 captures")
print(sheet_path)
print(OUT / "portrait_screenshots.zip")
