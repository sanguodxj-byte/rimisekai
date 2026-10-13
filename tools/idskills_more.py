"""身份技能池扩容：每身份在原 20 式基础（knight_01…20 等）之后续编 17 式（_21…_37），凑成 37 基础＋3 核心。

读 content/defs/identity_skills.json，删掉已有的 _21 以后的基础式（可重复跑），按本表追加到核心三式之前，
门槛取 tools/idskill_gates.py 的语义表（属性＋武器熟练，按等阶给数值）。写法与 tools/idskills.py 相同。
"""
import json, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
PATH = os.path.join(ROOT, 'content/defs/identity_skills.json')


def st(kind, stat='None', pct=0, power=0, rounds=0):
    return dict(status=kind, statusStat=stat, statusPercent=pct, statusPower=power, statusRounds=rounds)


def base(i, label, kind, target, power, rng='Melee', chant=0, hit=0, ctrl=False, status=None, desc=''):
    d = dict(defType='SkillDef', defName=i, label=label, description=desc, kind=kind, target=target, power=power, hitMod=hit,
             range=rng, chantRounds=chant, control=ctrl)
    d.update(status or dict(status=None, statusStat='None', statusPercent=0, statusPower=0, statusRounds=0))
    return d


def S(i, l, p, rng='Melee', hit=0, t='Enemy', status=None, ctrl=False, d=''): return base(i, l, 'Strike', t, p, rng, 0, hit, ctrl, status, d)
def P(i, l, p, chant=1, t='Enemy', status=None, ctrl=False, d=''): return base(i, l, 'Spell', t, p, 'Ranged', chant, 0, ctrl, status, d)
def H(i, l, p, chant=0, t='Ally', d=''): return base(i, l, 'Heal', t, p, 'Ranged', chant, 0, False, None, d)
def U(i, l, t, stat, pct, rounds, chant=0, kind='StatMod', power=0, d=''): return base(i, l, 'Buff', t, 100, 'Ranged', chant, 0, False, st(kind, stat, pct, power, rounds), d)


DOT = lambda p, r: st('Dot', power=p, rounds=r)
MOD = lambda s, p, r: st('StatMod', s, p, 0, r)

more = {}
more['骑士'] = [
    S('knight_21', '盾缘劈', 70), U('knight_22', '列阵', 'AllAllies', 'Defence', 10, 2), S('knight_23', '铁靴践踏', 80, status=MOD('Speed', -10, 2)),
    S('knight_24', '剑柄砸', 75, ctrl=True, d='以剑柄猛砸面门，打断咏唱'), U('knight_25', '骑士礼', 'Self', 'Attack', 15, 3), S('knight_26', '横扫千军', 70, t='FoesColumn'),
    S('knight_27', '盾墙推进', 95, status=MOD('Attack', -10, 2)), U('knight_28', '护旗', 'AllAllies', 'Attack', 10, 3), S('knight_29', '城门破', 145, hit=-5),
    S('knight_30', '白银斩', 120, hit=5), U('knight_31', '不屈', 'Self', 'Defence', 30, 3), S('knight_32', '长剑处决', 150, hit=-5),
    S('knight_33', '盾牌猛袭', 100, status=MOD('Defence', -15, 3)), S('knight_34', '斩首', 160, hit=-10), U('knight_35', '战吼', 'Enemy', 'Defence', -15, 2),
    S('knight_36', '骑士冲击', 110, hit=-5), S('knight_37', '誓剑', 90),
]
more['魔剑士'] = [
    S('spellblade_21', '符文刺', 70), P('spellblade_22', '暗焰飞弹', 85), U('spellblade_23', '刃上符文', 'Self', 'Attack', 15, 2),
    S('spellblade_24', '霜咬', 80, status=MOD('Speed', -10, 1)), P('spellblade_25', '雷鸣斩', 115), S('spellblade_26', '影缝', 85, hit=10),
    S('spellblade_27', '秘银斩', 110), P('spellblade_28', '灰烬剑雨', 85, t='FoesColumn'), S('spellblade_29', '咒缚斩', 90, ctrl=True, d='斩落时咒文缠身，打断咏唱'),
    P('spellblade_30', '奥术穿刺', 155), S('spellblade_31', '血月斩', 125), U('spellblade_32', '魔纹铠', 'Self', 'Defence', 35, 3, chant=1),
    S('spellblade_33', '雷霆一闪', 130, hit=5), P('spellblade_34', '冥火吞噬', 180, chant=2, status=DOT(5, 3)), S('spellblade_35', '魔剑解放', 150, hit=-5),
    S('spellblade_36', '裂空斩', 100, t='FoesColumn'), U('spellblade_37', '灵剑护身', 'Self', 'Dodge', 20, 3),
]
more['魔法师'] = [
    P('mage_21', '火花', 80), U('mage_22', '迟钝咒', 'Enemy', 'Attack', -15, 2, chant=1), P('mage_23', '寒霜之触', 85, status=MOD('Speed', -15, 1)),
    P('mage_24', '奥术之矢', 115), P('mage_25', '腐蚀射线', 90, status=DOT(6, 3)), P('mage_26', '静默咒', 80, ctrl=True, d='咒言封口，打断咏唱'),
    P('mage_27', '雷枪', 130), P('mage_28', '黑曜石雨', 85, t='FoesColumn'), U('mage_29', '秘法屏障', 'AllAllies', 'Defence', 15, 3, chant=1),
    P('mage_30', '灵魂灼烧', 160), P('mage_31', '冰棺', 140, status=MOD('Speed', -20, 2)), P('mage_32', '暴风雪', 110, t='AllEnemies', chant=2),
    P('mage_33', '陨火', 190, chant=2), U('mage_34', '魔力潮汐', 'Self', 'SpellPower', 40, 3, chant=1), P('mage_35', '虚无之握', 150),
    P('mage_36', '星界坍缩', 250, chant=3), P('mage_37', '幽光', 90),
]
more['刺客'] = [
    S('assassin_21', '匕首轻挑', 70), U('assassin_22', '伪装', 'Self', 'Dodge', 20, 2), S('assassin_23', '毒针', 50, rng='Ranged', status=DOT(3, 3)),
    S('assassin_24', '夜袭', 95), S('assassin_25', '剜目', 75, status=MOD('Attack', -10, 1)), S('assassin_26', '双刃乱舞', 60, t='FoesColumn', hit=5),
    S('assassin_27', '蛇吻', 85, status=DOT(4, 3)), S('assassin_28', '掏心', 115), S('assassin_29', '封喉刺', 90, ctrl=True, d='一刺封喉，打断咏唱'),
    S('assassin_30', '影杀', 130), S('assassin_31', '毒牙双击', 100, status=DOT(6, 3)), U('assassin_32', '杀机', 'Self', 'Attack', 30, 3),
    S('assassin_33', '千刃', 95, t='FoesColumn'), S('assassin_34', '死神之吻', 150, hit=-5), S('assassin_35', '断魂', 120, status=DOT(8, 3)),
    S('assassin_36', '暗影步刺', 115, hit=10), S('assassin_37', '飞镖连掷', 75, rng='Ranged', hit=10),
]
more['弓箭手'] = [
    S('archer_21', '试射', 70, rng='Ranged'), U('archer_22', '风向判读', 'Self', 'Attack', 15, 2), S('archer_23', '弓梢刺', 65),
    S('archer_24', '鸣镝', 75, rng='Ranged', ctrl=True, d='尖啸的鸣镝惊散术式，打断咏唱'), S('archer_25', '猎鹿箭', 95, rng='Ranged'), S('archer_26', '棘刺箭', 70, rng='Ranged', status=DOT(4, 3)),
    S('archer_27', '银矢', 110, rng='Ranged'), S('archer_28', '落星箭', 70, rng='Ranged', t='FoesColumn'), S('archer_29', '寒鸦箭', 90, rng='Ranged', status=MOD('Speed', -10, 1)),
    S('archer_30', '三连矢', 120, rng='Ranged', hit=5), S('archer_31', '碎盾箭', 100, rng='Ranged', status=MOD('Defence', -15, 1)), U('archer_32', '猎人本能', 'Self', 'Dodge', 30, 3),
    S('archer_33', '天穹箭雨', 75, rng='Ranged', t='AllEnemies'), S('archer_34', '穿心箭', 155, rng='Ranged', hit=-5), S('archer_35', '焚城火箭', 110, rng='Ranged', status=DOT(8, 3)),
    S('archer_36', '暗夜狙击', 135, rng='Ranged'), S('archer_37', '追猎', 85, rng='Ranged', hit=5),
]
more['神官'] = [
    H('priest_21', '轻抚', 40), U('priest_22', '晨祷', 'Self', 'Defence', 15, 3), P('priest_23', '辉光', 80),
    H('priest_24', '净化之泉', 80, chant=1), U('priest_25', '圣言', 'Ally', 'SpellPower', 25, 3, chant=1), P('priest_26', '苦修之鞭', 90, status=DOT(4, 2)),
    H('priest_27', '赐福之雨', 55, chant=1, t='AllAllies'), P('priest_28', '赎罪', 130), U('priest_29', '殉道誓言', 'AllAllies', 'Defence', 20, 3, chant=2),
    P('priest_30', '天使之矛', 160), H('priest_31', '神迹', 150, chant=1), P('priest_32', '灰烬圣礼', 70, t='AllEnemies'),
    U('priest_33', '圣痕', 'Ally', 'None', 0, 3, chant=1, kind='Points', power=100), P('priest_34', '末日审判', 230, chant=2), H('priest_35', '群体复苏', 60, chant=1, t='AllAllies'),
    P('priest_36', '炼狱之焰', 150, status=DOT(6, 3)), P('priest_37', '驱魔咒', 80, ctrl=True, d='圣名压下邪咒，打断咏唱'),
]
more['女仆'] = [
    S('maid_21', '掸尘', 65), H('maid_22', '递手帕', 40), U('maid_23', '屈膝礼', 'Self', 'Dodge', 15, 2),
    S('maid_24', '烛台敲击', 95), S('maid_25', '剪刀突刺', 80, status=MOD('Defence', -10, 1)), H('maid_26', '热汤', 70),
    S('maid_27', '桌布翻飞', 70, t='FoesColumn'), S('maid_28', '银叉刺', 105, hit=5), U('maid_29', '训诫', 'Enemy', 'Attack', -25, 3),
    S('maid_30', '铁壶重砸', 140, hit=-10), S('maid_31', '茶刀乱舞', 95, status=DOT(5, 3)), H('maid_32', '晚餐盛宴', 50, t='AllAllies'),
    S('maid_33', '门闩一击', 120, ctrl=True, d='一记门闩敲在后脑，打断咏唱'), S('maid_34', '大扫除', 75, t='AllEnemies'), S('maid_35', '终末侍奉', 155, hit=-5),
    U('maid_36', '彻夜值守', 'AllAllies', 'Defence', 15, 3), S('maid_37', '碎瓷片', 70, rng='Ranged', hit=10),
]
more['吟游诗人'] = [
    U('bard_21', '小调', 'AllAllies', 'Dodge', 10, 2), P('bard_22', '颤音', 85), S('bard_23', '琴弓抽打', 70),
    H('bard_24', '摇篮曲', 35, t='AllAllies'), U('bard_25', '挽歌', 'Enemy', 'Defence', -20, 3), P('bard_26', '尖啸', 75, ctrl=True, d='一声尖啸搅乱咒文，打断咏唱'),
    P('bard_27', '哀伤咏叹', 85, t='FoesColumn'), U('bard_28', '凯旋曲', 'AllAllies', 'Attack', 20, 3, chant=1), S('bard_29', '琴颈重击', 110),
    H('bard_30', '圣咏', 60, chant=1, t='AllAllies'), P('bard_31', '共鸣震荡', 120, t='FoesColumn', chant=2), U('bard_32', '赋格', 'AllAllies', 'Speed', 15, 3),
    P('bard_33', '亡者序曲', 160, status=MOD('Attack', -10, 2)), P('bard_34', '破碎和弦', 90, t='AllEnemies'), S('bard_35', '弦刃', 125),
    U('bard_36', '夜曲', 'Enemy', 'Speed', -30, 3), P('bard_37', '低吟', 95),
]
more['圣骑士'] = [
    S('paladin_21', '祷言斩', 70), H('paladin_22', '慰藉', 50), U('paladin_23', '洁身', 'Self', 'Defence', 15, 2),
    S('paladin_24', '银盾猛击', 75, ctrl=True, d='镀银盾面撞上去，打断咏唱'), P('paladin_25', '圣焰', 100), S('paladin_26', '赦罪斩', 95),
    S('paladin_27', '光辉斩', 110), H('paladin_28', '圣愈之触', 110, chant=1), U('paladin_29', '光之铠', 'AllAllies', 'Defence', 15, 3, chant=1),
    S('paladin_30', '圣锤', 140, hit=-10), P('paladin_31', '圣光洪流', 85, t='AllEnemies'), S('paladin_32', '驱魔斩', 100, status=DOT(6, 3)),
    S('paladin_33', '神圣审判', 160, hit=-10), S('paladin_34', '光明十字', 100, t='FoesColumn'), H('paladin_35', '圣血', 130, chant=1),
    P('paladin_36', '末日圣光', 220, chant=2), U('paladin_37', '圣誓', 'Self', 'Attack', 20, 3),
]


def score(s):
    """SkillTier.Score 的同一算法。"""
    spread = {'AllEnemies': 2, 'AllAllies': 2, 'FoesColumn': 1.5}.get(s['target'], 1)
    v = 0.0
    if s['kind'] in ('Strike', 'Spell'):
        v = s['power'] * spread * (1 + s['hitMod'] / 100)
    elif s['kind'] == 'Heal':
        v = s['power'] * spread * 1.2
    if s['status'] == 'StatMod':
        v += abs(s['statusPercent']) * s['statusRounds'] * 1.5 * (spread if s['kind'] == 'Buff' else 1)
    elif s['status'] == 'Dot':
        v += s['statusPower'] * s['statusRounds'] * 2
    elif s['status'] == 'Points':
        v += s['statusPower'] * s['statusRounds'] * 0.5
    if s['control']:
        v += 20
    return v / (1 + 0.25 * s['chantRounds'])


def tier(s):
    return 1 + sum(score(s) >= t for t in (75, 100, 120, 140))


def gate(def_name, t):
    from idskill_gates import gate as semantic_gate
    return semantic_gate(def_name, t)


def main():
    data = json.load(open(PATH, encoding='utf-8'))
    for pool in data['defs']:
        skills = pool['skills']
        core = [s for s in skills if 'core' in s]
        old = [s for s in skills if 'core' not in s and int(re.search(r'_(\d+)$', s['defName']).group(1)) <= 20]
        new = more[pool['defName']]
        assert len(old) == 20 and len(new) == 17 and len(core) == 3, pool['defName']
        for s in new:
            s['gate'] = gate(s['defName'], tier(s))
        pool['skills'] = old + new + core
    out = json.dumps(data, ensure_ascii=False, indent=1) + '\n'
    open(PATH, 'w', encoding='utf-8').write(out)
    for pool in data['defs']:
        new = pool['skills'][20:37]
        print(pool['defName'], ' '.join(f"{s['label']}{tier(s)}" for s in new))


if __name__ == '__main__':
    main()
