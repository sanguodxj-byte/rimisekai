# -*- coding: utf-8 -*-
"""
tools/migrate_portraits_to_assets.py
一次性迁移：根目录全部 立绘*.png（178 张）按规范英文名迁入 assets/portraits/ 子目录体系。
- 连同 Godot .import 侧车文件一起移动（保留 uid，避免编辑器重导入漂移）
- 迁移前校验命名冲突；迁移后校验根目录零残留
目录规划：
  assets/portraits/identity/       31 身份 x 3 差分（93）
  assets/portraits/identity_moe/   7 身份 x 3 萌相（21）
  assets/portraits/special/        3 张初始角色 + v2（4）
  assets/portraits/monster/        怪物立绘（3）
  assets/portraits/frame/          边框素材（1）
  assets/portraits/legacy/         旧编号角色系列与龙娘系列（~47）
  assets/portraits/legacy/test/    迭代测试文件（9）
"""

import os
import glob
import shutil

PROJ = r"D:\123\rimisekai"
DEST_ROOT = os.path.join(PROJ, "assets", "portraits")

IDENTITY_EN = {
    "女仆": "maid", "骑士": "knight", "商人": "merchant", "学者": "scholar",
    "神官": "priestess", "魔法师": "mage", "战士": "warrior", "护卫": "guard",
    "佣兵": "mercenary", "弓箭手": "archer", "猎人": "hunter", "刺客": "assassin",
    "盗贼": "thief", "吟游诗人": "bard", "舞娘": "dancer", "药师": "apothecary",
    "炼金术士": "alchemist", "铁匠": "blacksmith", "木匠": "carpenter",
    "厨师": "cook", "花匠": "gardener", "信使": "courier", "修女": "nun",
    "僧侣": "monk", "德鲁伊": "druid", "游侠": "ranger", "圣骑士": "paladin",
    "贵族": "noble", "管家": "butler", "学者助手": "scholar_assistant",
    "星术师": "astrologer", "龙骑士": "dragon_knight", "魔剑士": "magic_swordsman",
    "驯兽师": "beast_tamer",
}

MONSTER_EN = {
    "史莱姆": "slime", "哥布林": "goblin", "骷髅剑士": "skeleton_swordsman",
}

SPECIAL_EN = {
    "立绘_人类圣骑士.png": "portrait_human_paladin.png",
    "立绘_鼠耳鼠尾圣女.png": "portrait_mouse_ear_saint.png",
    "立绘_马耳马尾武装修女.png": "portrait_horse_war_nun.png",
    "立绘_马耳马尾武装修女_v2.png": "portrait_horse_war_nun_v2.png",
}

LEGACY_EN = {
    "立绘_01_精灵弓箭手.png": "portrait_elf_archer_01.png",
    "立绘_02_神圣修女.png": "portrait_holy_nun_02.png",
    "立绘_03_黑魔女.png": "portrait_dark_witch_03.png",
    "立绘_05_炼金术士.png": "portrait_alchemist_05.png",
    "立绘_06_星象占卜师.png": "portrait_star_diviner_06.png",
    "立绘_07_德鲁伊少女.png": "portrait_druid_girl_07.png",
    "立绘_09_神殿巫女.png": "portrait_shrine_maiden_09.png",
    "立绘_10_流浪诗人.png": "portrait_wandering_poet_10.png",
    "立绘_11_猫耳盗贼.png": "portrait_cat_eared_thief_11.png",
    "立绘_13_狼族狂战士.png": "portrait_wolf_berserker_13.png",
    "立绘_14_兔耳拳斗士.png": "portrait_rabbit_boxer_14.png",
    "立绘_15_羊角魔人.png": "portrait_horned_demon_15.png",
    "立绘_16_鸟翼天人.png": "portrait_winged_celestial_16.png",
    "立绘_17_犬耳女仆.png": "portrait_dog_eared_maid_17.png",
    "立绘_18_暗精灵刺客.png": "portrait_dark_elf_assassin_18.png",
    "立绘_19_牛角重锤士.png": "portrait_bull_hammer_19.png",
    "立绘_20_吸血鬼贵族.png": "portrait_vampire_noble_20.png",
    "立绘_21_流浪浪人.png": "portrait_wandering_ronin_21.png",
    "立绘_22_雾隐女忍.png": "portrait_mist_kunoichi_22.png",
    "立绘_24_异国符咒师.png": "portrait_foreign_talisman_24.png",
    "立绘_25_高原鹰猎手.png": "portrait_highland_falconer_25.png",
    "立绘_26_水乡渔娘刺客.png": "portrait_lakeside_fisher_26.png",
    "立绘_27_傀儡师.png": "portrait_puppeteer_27.png",
    "立绘_28_异端咒剑士.png": "portrait_heretic_blade_28.png",
    "立绘_29_密林捕兽学者.png": "portrait_jungle_beast_scholar_29.png",
    "立绘_30_商会掌柜千金.png": "portrait_guild_heiress_30.png",
    "立绘_龙娘剑士.png": "portrait_dragon_swordswoman.png",
    "立绘_龙娘法师.png": "portrait_dragon_mage.png",
    "立绘_龙娘游侠.png": "portrait_dragon_ranger.png",
    "立绘_14至16岁_龙娘战姬.png": "portrait_dragon_war_princess.png",
    "立绘_14至16岁_龙娘游侠.png": "portrait_dragon_ranger_14_16.png",
    "立绘_14至16岁_龙娘魔女.png": "portrait_dragon_witch_14_16.png",
    "立绘_16比9_龙娘剑士.png": "portrait_dragon_swordswoman_16x9.png",
    "立绘_16比9_龙娘法师.png": "portrait_dragon_mage_16x9.png",
    "立绘_16比9_龙娘游侠.png": "portrait_dragon_ranger_16x9.png",
    "立绘_9比16_龙娘剑士.png": "portrait_dragon_swordswoman_9x16.png",
    "立绘_9比16_龙娘法师.png": "portrait_dragon_mage_9x16.png",
    "立绘_9比16_龙娘游侠.png": "portrait_dragon_ranger_9x16.png",
    "立绘_矢量龙娘_剑士.png": "portrait_dragon_swordswoman_vector.png",
    "立绘_矢量龙娘_法师.png": "portrait_dragon_mage_vector.png",
    "立绘_矢量龙娘_游侠.png": "portrait_dragon_ranger_vector.png",
    "立绘_去噪版_龙娘_倾胯叉腰.png": "portrait_dragon_hips_stance_denoised.png",
    "立绘_去噪版_龙娘_回眸轻步.png": "portrait_dragon_glance_step_denoised.png",
    "立绘_去噪版_龙娘_驻枪.png": "portrait_dragon_spear_stance_denoised.png",
    "立绘_多姿势龙娘_交叉步漫步.png": "portrait_dragon_cross_stride.png",
    "立绘_多姿势龙娘_叉腰正面.png": "portrait_dragon_hands_hips_front.png",
    "立绘_多姿势龙娘_背身回眸.png": "portrait_dragon_look_back.png",
}

TEST_EN = {
    "立绘_叠加测试_女圣骑士.png": "test_overlay_paladin.png",
    "立绘_叠加测试_精灵游侠.png": "test_overlay_elf_ranger.png",
    "立绘_叠加测试_魔导魔女.png": "test_overlay_mage_witch.png",
    "立绘_测试_色块_女圣骑士.png": "test_colorblock_paladin.png",
    "立绘_测试_色块_精灵游侠.png": "test_colorblock_elf_ranger.png",
    "立绘_测试_色块_魔导魔女.png": "test_colorblock_mage_witch.png",
    "立绘_无边框_14至16岁_女仆.png": "test_borderless_maid_14_16.png",
    "立绘_无边框_14至16岁_猫娘.png": "test_borderless_catgirl_14_16.png",
    "立绘_无边框_14至16岁_精灵.png": "test_borderless_elf_14_16.png",
}

FRAME_EN = {
    "立绘边框素材_黑底.png": "portrait_frame_black.png",
}


def build_mapping():
    mapping = {}  # src_filename -> (dest_relpath, category)

    for f in glob.glob(os.path.join(PROJ, "立绘*.png")):
        base = os.path.basename(f)

        if base in SPECIAL_EN:
            mapping[base] = (os.path.join("special", SPECIAL_EN[base]), "special")
            continue
        if base in FRAME_EN:
            mapping[base] = (os.path.join("frame", FRAME_EN[base]), "frame")
            continue
        if base.startswith("立绘_怪物_"):
            zh = base[len("立绘_怪物_"):-len(".png")]
            en = MONSTER_EN.get(zh)
            if en:
                mapping[base] = (os.path.join("monster", f"monster_{en}.png"), "monster")
                continue
        if "_萌相_差分" in base:
            zh_id = base[len("立绘_"):base.index("_萌相_")]
            n = base[base.rindex("差分") + len("差分"):-len(".png")]
            en = IDENTITY_EN.get(zh_id)
            if en:
                mapping[base] = (os.path.join("identity_moe", f"{en}_moe_diff{n}.png"), "identity_moe")
                continue
        if "_差分" in base and base.startswith("立绘_"):
            zh_id = base[len("立绘_"):base.rindex("_差分")]
            n = base[base.rindex("差分") + len("差分"):-len(".png")]
            en = IDENTITY_EN.get(zh_id)
            if en:
                mapping[base] = (os.path.join("identity", f"{en}_diff{n}.png"), "identity")
                continue
        if base in TEST_EN:
            mapping[base] = (os.path.join("legacy", "test", TEST_EN[base]), "legacy_test")
            continue
        if base in LEGACY_EN:
            mapping[base] = (os.path.join("legacy", LEGACY_EN[base]), "legacy")
            continue
        mapping[base] = (None, "UNMAPPED")

    return mapping


def main():
    mapping = build_mapping()
    files = sorted(glob.glob(os.path.join(PROJ, "立绘*.png")))

    unmapped = [b for b in (os.path.basename(f) for f in files)
                if b not in mapping or mapping[b][1] == "UNMAPPED"]
    if unmapped:
        print("!!! 存在未映射文件，中止迁移：")
        for b in unmapped:
            print("   ", b)
        return

    # 冲突校验（目标 relpath 唯一性）
    dests = [v[0] for v in mapping.values()]
    dup = {d for d in dests if dests.count(d) > 1}
    if dup:
        print("!!! 目标命名冲突，中止迁移：")
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
        # .import 侧车文件一起移动（保留 Godot uid）
        imp_src = f + ".import"
        if os.path.exists(imp_src):
            shutil.move(imp_src, os.path.join(dest_dir, os.path.basename(rel)) + ".import")
        moved += 1
        counts[cat] = counts.get(cat, 0) + 1

    print(f"=== 迁移完成：{moved} 张 PNG（含 .import 侧车）已迁入 assets/portraits/ ===")
    for cat, n in sorted(counts.items()):
        print(f"  {cat:14s}: {n}")
    leftover = glob.glob(os.path.join(PROJ, "立绘*.png"))
    print(f"根目录残留 立绘*.png：{len(leftover)}")


if __name__ == "__main__":
    main()
