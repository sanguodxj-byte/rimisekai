# -*- coding: utf-8 -*-
"""
tools/migrate_avatars_to_assets.py
一次性迁移：根目录全部 头像_*.png（117 张）按规范英文名迁入 assets/avatars/ 子目录体系，
并清理根目录 8 张 场景_*.png 测试迭代图（连同 .import 侧车）。
目录规划（与 portraits 镜像）：
  assets/avatars/identity/       31 身份 x 3 差分（93）
  assets/avatars/identity_moe/   7 身份 x 3 萌相（21）
  assets/avatars/special/        3 张初始角色（3）
"""

import os
import glob
import shutil

PROJ = r"D:\123\rimisekai"
DEST_ROOT = os.path.join(PROJ, "assets", "avatars")

IDENTITY_EN = {
    "女仆": "maid", "骑士": "knight", "商人": "merchant", "学者": "scholar",
    "神官": "priestess", "魔法师": "mage", "战士": "warrior", "护卫": "guard",
    "佣兵": "mercenary", "弓箭手": "archer", "猎人": "hunter", "刺客": "assassin",
    "盗贼": "thief", "吟游诗人": "bard", "舞娘": "dancer", "药师": "apothecary",
    "炼金术士": "alchemist", "铁匠": "blacksmith", "木匠": "carpenter",
    "厨师": "cook", "花匠": "gardener", "信使": "courier", "修女": "nun",
    "僧侣": "monk", "德鲁伊": "druid", "游侠": "ranger", "圣骑士": "paladin",
    "贵族": "noble", "管家": "butler", "学者助手": "scholar_assistant",
    "星术师": "astrologer",
}

SPECIAL_EN = {
    "头像_人类圣骑士.png": "avatar_human_paladin.png",
    "头像_鼠耳鼠尾圣女.png": "avatar_mouse_ear_saint.png",
    "头像_马耳马尾武装修女.png": "avatar_horse_war_nun.png",
}

SCENE_FILES = [
    "场景_16比9_中世纪卧室.png",
    "场景_16比9_地下城通道_DRPG.png",
    "场景_卧室_夜晚休息.png",
    "场景_正相光照测试_卧室.png",
    "场景_测试修复_卧室.png",
    "场景_测试修复_地下城.png",
    "场景_测试修复_正常地牢废墟.png",
    "场景_测试修复_正常白天村庄.png",
]


def build_mapping():
    mapping = {}
    for f in glob.glob(os.path.join(PROJ, "头像_*.png")):
        base = os.path.basename(f)
        if base in SPECIAL_EN:
            mapping[base] = (os.path.join("special", SPECIAL_EN[base]), "special")
            continue
        if "_萌相_差分" in base:
            zh_id = base[len("头像_"):base.index("_萌相_")]
            n = base[base.rindex("差分") + len("差分"):-len(".png")]
            en = IDENTITY_EN.get(zh_id)
            if en:
                mapping[base] = (os.path.join("identity_moe", f"{en}_moe_diff{n}.png"), "identity_moe")
                continue
        if "_差分" in base and base.startswith("头像_"):
            zh_id = base[len("头像_"):base.rindex("_差分")]
            n = base[base.rindex("差分") + len("差分"):-len(".png")]
            en = IDENTITY_EN.get(zh_id)
            if en:
                mapping[base] = (os.path.join("identity", f"{en}_diff{n}.png"), "identity")
                continue
        mapping[base] = (None, "UNMAPPED")
    return mapping


def main():
    mapping = build_mapping()
    files = sorted(glob.glob(os.path.join(PROJ, "头像_*.png")))

    unmapped = [os.path.basename(f) for f in files
                if os.path.basename(f) not in mapping or mapping[os.path.basename(f)][1] == "UNMAPPED"]
    if unmapped:
        print("!!! 未映射文件，中止：")
        for b in unmapped:
            print("   ", b)
        return

    dests = [v[0] for v in mapping.values()]
    dup = {d for d in dests if dests.count(d) > 1}
    if dup:
        print("!!! 目标命名冲突，中止：")
        for d in sorted(dup):
            print("   ", d)
        return

    counts = {}
    moved = 0
    for f in files:
        base = os.path.basename(f)
        rel, cat = mapping[base]
        dest_dir = os.path.join(DEST_ROOT, os.path.dirname(rel))
        os.makedirs(dest_dir, exist_ok=True)
        shutil.move(f, os.path.join(dest_dir, os.path.basename(rel)))
        imp_src = f + ".import"
        if os.path.exists(imp_src):
            shutil.move(imp_src, os.path.join(dest_dir, os.path.basename(rel)) + ".import")
        moved += 1
        counts[cat] = counts.get(cat, 0) + 1

    print(f"=== 头像迁移完成：{moved} 张已迁入 assets/avatars/ ===")
    for cat, n in sorted(counts.items()):
        print(f"  {cat:14s}: {n}")

    # 清理场景测试图（含 .import 侧车）
    removed = 0
    for name in SCENE_FILES:
        p = os.path.join(PROJ, name)
        if os.path.exists(p):
            os.remove(p)
            removed += 1
        imp = p + ".import"
        if os.path.exists(imp):
            os.remove(imp)
    print(f"=== 场景测试图清理完成：删除 {removed}/{len(SCENE_FILES)} 张（含 .import）===")

    print(f"根目录残留 头像_*.png：{len(glob.glob(os.path.join(PROJ, '头像_*.png')))}")
    print(f"根目录残留 场景_*.png：{len(glob.glob(os.path.join(PROJ, '场景_*.png')))}")


if __name__ == "__main__":
    main()
