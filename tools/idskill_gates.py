"""身份技能门槛表：每式按语义定一项属性与（可无的）武器熟练（主人 2026-10-13 定：撤掉流派限制，门槛只看属性＋武器熟练；
武器只给语义上确实用那件武器的招式，徒手只给真正赤手的招式，不滥用）。

门槛数值按等阶：一到五阶 属性下限 6/9/12/15/18、武器熟练 1/1/2/5/8；核心三式 属性 11、武器 1。
本脚本把表写回 content/defs/identity_skills.json 的 gate（可重复跑）；tools/idskills_more.py 也从这里取门槛。
"""
import json, os, re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
PATH = os.path.join(ROOT, 'content/defs/identity_skills.json')

# 每式：(属性, 武器或None)。属性：Str力量 Dex敏捷 Con体质 Int智力 Per感知 Cha魅力
S,D,C,I,P,H='Strength','Dexterity','Constitution','Intellect','Perception','Charm'
Sw,Ax,Sp,Bo,St,Dg,Cb,Ua='Sword','Axe','Spear','Bow','Staff','Dagger','Crossbow','Unarmed'
M={
# 骑士：剑招力量；盾招体质无武器；号令类魅力
'knight':{'01':(S,Sw),'02':(C,None),'03':(S,Sp),'04':(S,Sw),'05':(S,Sw),'06':(S,Sw),'07':(C,None),'08':(H,None),'09':(S,Sw),'10':(S,Sw),
 '11':(D,Sw),'12':(C,None),'13':(S,Sp),'14':(H,None),'15':(S,Sw),'16':(I,None),'17':(S,Sw),'18':(S,Sw),'19':(C,None),'20':(P,Sw),
 '21':(S,None),'22':(H,None),'23':(S,Ua),'24':(S,Sw),'25':(H,None),'26':(S,Sw),'27':(C,None),'28':(H,None),'29':(S,Sw),'30':(S,Sw),
 '31':(C,None),'32':(S,Sw),'33':(C,None),'34':(S,Sw),'35':(H,None),'36':(S,Sp),'37':(S,Sw),'c1':(C,None),'c2':(H,None),'c3':(C,None)},
# 魔剑士：剑招敏捷，剑上附魔/剑气智力+剑，纯法术智力无武器
'spellblade':{'01':(I,Sw),'02':(I,Sw),'03':(D,Sw),'04':(I,Sw),'05':(D,Sw),'06':(I,Sw),'07':(I,None),'08':(D,Sw),'09':(I,Sw),'10':(I,None),
 '11':(D,Sw),'12':(D,None),'13':(D,Sw),'14':(I,None),'15':(D,Sw),'16':(P,None),'17':(I,Sw),'18':(S,Sw),'19':(I,None),'20':(D,Sw),
 '21':(I,Sw),'22':(I,None),'23':(I,None),'24':(I,Sw),'25':(I,Sw),'26':(D,Sw),'27':(D,Sw),'28':(I,Sw),'29':(I,Sw),'30':(I,None),
 '31':(S,Sw),'32':(I,None),'33':(D,Sw),'34':(I,None),'35':(I,Sw),'36':(S,Sw),'37':(I,None),'c1':(I,Sw),'c2':(D,None),'c3':(D,Sw)},
# 魔法师：攻击法术智力+杖，增益/封言智力无武器
'mage':{k:(I,St) for k in ['01','02','03','04','05','06','10','11','13','16','17','19','20','21','23','24','25','27','28','30','31','32','33','35','36','37','14','26']},
# 刺客：匕首敏捷，投掷敏捷+匕首，袖箭弩，锁喉绞杀徒手力量
'assassin':{'01':(D,Dg),'02':(D,Dg),'03':(D,Dg),'04':(D,Dg),'05':(D,Dg),'06':(D,Dg),'07':(D,None),'08':(S,Ua),'09':(D,Dg),'10':(I,None),
 '11':(D,Dg),'12':(C,None),'13':(D,Dg),'14':(S,Ua),'15':(D,Cb),'16':(I,None),'17':(D,Dg),'18':(D,Dg),'19':(D,Dg),'20':(D,Dg),
 '21':(D,Dg),'22':(H,None),'23':(D,None),'24':(D,Dg),'25':(D,Dg),'26':(D,Dg),'27':(D,Dg),'28':(S,Dg),'29':(D,Dg),'30':(D,Dg),
 '31':(D,Dg),'32':(P,None),'33':(D,Dg),'34':(D,Dg),'35':(D,Dg),'36':(D,Dg),'37':(D,Dg),'c1':(D,Dg),'c2':(D,None),'c3':(D,Dg)},
# 弓手：射箭敏捷+弓，瞄准/狙击感知+弓，判读/鹰眼感知无武器
'archer':{'01':(D,Bo),'02':(S,Bo),'03':(P,Bo),'04':(D,Bo),'05':(P,Bo),'06':(D,Bo),'07':(P,None),'08':(D,Bo),'09':(D,Bo),'10':(D,Bo),
 '11':(S,Bo),'12':(P,None),'13':(D,Bo),'14':(P,Bo),'15':(D,None),'16':(D,Bo),'17':(P,Bo),'18':(D,Bo),'19':(D,Bo),'20':(D,Bo),
 '21':(P,Bo),'22':(P,None),'23':(D,Bo),'24':(D,Bo),'25':(P,Bo),'26':(D,Bo),'27':(D,Bo),'28':(P,Bo),'29':(D,Bo),'30':(D,Bo),
 '31':(S,Bo),'32':(P,None),'33':(D,Bo),'34':(P,Bo),'35':(D,Bo),'36':(P,Bo),'37':(P,Bo),'c1':(P,Bo),'c2':(D,Bo),'c3':(P,Bo)},
# 女仆：器物重击力量，刀叉投掷敏捷+匕首，照料感知，礼仪魅力
'maid':{'01':(S,None),'02':(S,None),'03':(D,Dg),'04':(P,None),'05':(P,None),'06':(H,None),'07':(H,None),'08':(D,None),'09':(H,None),'10':(D,Dg),
 '11':(D,None),'12':(S,None),'13':(D,None),'14':(P,None),'15':(H,None),'16':(P,None),'17':(D,None),'18':(D,None),'19':(C,None),'20':(S,None),
 '21':(D,None),'22':(P,None),'23':(H,None),'24':(S,None),'25':(D,Dg),'26':(P,None),'27':(D,None),'28':(D,Dg),'29':(H,None),'30':(S,None),
 '31':(D,Dg),'32':(P,None),'33':(S,None),'34':(C,None),'35':(D,Dg),'36':(C,None),'37':(D,Dg),'c1':(H,None),'c2':(C,None),'c3':(D,None)},
}
# 神官：全部感知（信仰），钉锤击力量；无武器
M['priest']={f'{i:02d}':(P,None) for i in range(1,38)}|{'c1':(P,None),'c2':(P,None),'c3':(P,None),'17':(S,None),'26':(C,None)}
# 吟游诗人：全部魅力；乐器砸击力量，弓弦/笛刺敏捷；无武器
M['bard']={f'{i:02d}':(H,None) for i in range(1,38)}|{'c1':(H,None),'c2':(H,None),'c3':(D,None),'08':(D,None),'11':(S,None),'18':(D,None),'23':(D,None),'29':(S,None),'35':(D,None)}
# 圣骑士：剑招力量+剑，圣术魅力，盾招体质，锤招力量无武器
M['paladin']={'01':(S,Sw),'02':(C,None),'03':(H,None),'04':(H,None),'05':(S,None),'06':(S,Sw),'07':(C,None),'08':(H,None),'09':(H,Sw),'10':(S,Sw),
 '11':(H,None),'12':(S,Sw),'13':(H,None),'14':(H,None),'15':(S,Sw),'16':(H,None),'17':(H,None),'18':(C,Sw),'19':(C,None),'20':(S,Sw),
 '21':(H,Sw),'22':(H,None),'23':(H,None),'24':(C,None),'25':(H,None),'26':(S,Sw),'27':(S,Sw),'28':(H,None),'29':(C,None),'30':(S,None),
 '31':(H,None),'32':(S,Sw),'33':(S,Sw),'34':(S,Sw),'35':(H,None),'36':(H,None),'37':(H,None),'c1':(C,None),'c2':(H,Sw),'c3':(C,None)}
M['mage']|={k:(I,None) for k in ['07','08','09','12','15','18','22','29','34','c1','c2','c3']}


ATTR_MIN = {1: 6, 2: 9, 3: 12, 4: 15, 5: 18}
WEAPON_LEVEL = {1: 1, 2: 1, 3: 2, 4: 5, 5: 8}
CORE_ATTR_MIN, CORE_WEAPON_LEVEL = 11, 1


def gate(def_name, tier):
    """tier：1–5 为基础技能等阶，0 为核心技能。"""
    ident, key = def_name.rsplit('_', 1)
    attr, weapon = M[ident][key]
    g = dict(attribute=attr, core=[dict(stat=attr, min=CORE_ATTR_MIN if tier == 0 else ATTR_MIN[tier])])
    if weapon:
        g.update(weapon=weapon, weaponLevel=CORE_WEAPON_LEVEL if tier == 0 else WEAPON_LEVEL[tier])
    return g


def main():
    data = json.load(open(PATH, encoding='utf-8'))
    from idskills_more import tier
    for pool in data['defs']:
        for s in pool['skills']:
            s['gate'] = gate(s['defName'], 0 if 'core' in s else tier(s))
    open(PATH, 'w', encoding='utf-8').write(json.dumps(data, ensure_ascii=False, indent=1) + '\n')


if __name__ == '__main__':
    main()
