import json
def st(kind, stat='None', pct=0, power=0, rounds=0): return dict(status=kind, statusStat=stat, statusPercent=pct, statusPower=power, statusRounds=rounds)
def base(i, label, kind, target, power, rng='Melee', chant=0, hit=0, ctrl=False, status=None, desc=''):
    d = dict(defType='SkillDef', defName=i, label=label, description=desc, kind=kind, target=target, power=power, hitMod=hit,
             range=rng, chantRounds=chant, control=ctrl)
    d.update(status or dict(status=None, statusStat='None', statusPercent=0, statusPower=0, statusRounds=0))
    return d
def S(i,l,p,rng='Melee',hit=0,t='Enemy',status=None,ctrl=False,d=''): return base(i,l,'Strike',t,p,rng,0,hit,ctrl,status,d)
def P(i,l,p,chant=1,t='Enemy',status=None,ctrl=False,d=''): return base(i,l,'Spell',t,p,'Ranged',chant,0,ctrl,status,d)
def H(i,l,p,chant=0,t='Ally',d=''): return base(i,l,'Heal',t,p,'Ranged',chant,0,False,None,d)
def U(i,l,t,stat,pct,rounds,chant=0,kind='StatMod',power=0,d=''): return base(i,l,'Buff',t,100,'Ranged',chant,0,False,st(kind,stat,pct,power,rounds),d)
def eff(stat,pct=0,flat=0): return dict(stat=stat,percent=pct,flat=flat)
def stance(i,l,effects,d,t='Self'): 
    x=base(i,l,'Buff',t,100,'Melee',0,0,False,None,d); x.update(core='Stance',effects=effects); return x
def aura(i,l,effects,d,chant=0):
    x=base(i,l,'Buff','AllAllies',100,'Ranged',chant,0,False,None,d); x.update(core='Aura',effects=effects); return x
def charge(i,l,trigger,effects,stacks,rounds,d):
    x=base(i,l,'Buff','Self',100,'Melee',0,0,False,None,d); x.update(core='Charge',trigger=trigger,effects=effects,maxStacks=stacks,statusRounds=rounds); return x
def react(i,l,trigger,kind,target,power,rounds,uses,d,effects=None):
    x=base(i,l,'Buff','Self',100,'Melee',0,0,False,None,d); x.update(core='Reaction',trigger=trigger,reactKind=kind,reactTarget=target,reactPower=power,statusRounds=rounds,reactUses=uses)
    if effects: x['effects']=effects
    return x
DOT=lambda p,r: st('Dot',power=p,rounds=r)
MOD=lambda s,p,r: st('StatMod',s,p,0,r)
pools = {}
pools['骑士'] = [
 S('knight_01','直刺',110), S('knight_02','盾击',90,ctrl=True,d='以盾面撞击，打断咏唱'), S('knight_03','冲锋',140,hit=-10),
 S('knight_04','回旋斩',80,t='FoesColumn'), S('knight_05','破阵',120,status=MOD('Defence',-20,2)), S('knight_06','压制',100,status=MOD('Attack',-15,2)),
 U('knight_07','坚守','Self','Defence',40,2), U('knight_08','号令','AllAllies','Attack',15,2), S('knight_09','重击',140,hit=-10),
 S('knight_10','斩铁',130,status=MOD('Defence',-10,3)), S('knight_11','突刺连击',95,hit=10), U('knight_12','掩护','Ally','Defence',30,2),
 S('knight_13','骑枪架击',125), U('knight_14','挑衅','Enemy','Attack',-20,2), S('knight_15','崩山击',140,hit=-10),
 U('knight_16','整备','Self','Attack',25,2), S('knight_17','压步斩',105,status=MOD('Speed',-20,2)), S('knight_18','誓约一击',135),
 U('knight_19','铁之意志','Self','Dodge',20,2), S('knight_20','残心',115,hit=15),
 stance('knight_c1','不动姿态',[eff('Defence',50),eff('Speed',0,-3)],'防御 +50%，速度 -3，持续到战斗结束'),
 charge('knight_c2','荣誉','Hurt',[eff('Attack',10)],5,4,'每受击一次积累 1 层，持续 4 回合，每层攻击 +10%，至多 5 层'),
 react('knight_c3','守护反击','Hurt','Strike','Enemy',200,3,2,'受击时以 200% 威力反击来者，挂 3 回合，可发动 2 次'),
]
pools['魔剑士'] = [
 S('spellblade_01','附魔斩',115), P('spellblade_02','雷光剑气',140), S('spellblade_03','双重斩',85,hit=10),
 P('spellblade_04','炎刃',120,status=DOT(6,3)), S('spellblade_05','剑舞',75,t='FoesColumn'), P('spellblade_06','冰封剑痕',110,status=MOD('Speed',-25,2)),
 U('spellblade_07','魔力灌注','Self','SpellPower',30,3,chant=1), S('spellblade_08','闪斩',100,hit=20), S('spellblade_09','破魔斩',105,ctrl=True,d='斩断术式，打断咏唱'),
 P('spellblade_10','星屑',90,t='AllEnemies',chant=2), S('spellblade_11','逆袈裟',125), U('spellblade_12','残影','Self','Dodge',25,2),
 S('spellblade_13','月牙斩',135,rng='Ranged',hit=-10), P('spellblade_14','虚空裂隙',170,chant=2), S('spellblade_15','连斩',90,hit=5),
 U('spellblade_16','剑意','Self','Attack',20,3), P('spellblade_17','魔刃风暴',100,t='FoesColumn',chant=1), S('spellblade_18','断罪',140,hit=-10),
 U('spellblade_19','魔力护膜','Self','Defence',30,2,chant=1), S('spellblade_20','收刀',110,status=MOD('Dodge',-15,2)),
 charge('spellblade_c1','剑光','Hit',[eff('Damage',30)],3,5,'攻击命中积累 1 层，持续五回合，每层技能伤害 +30%，至多 3 层'),
 stance('spellblade_c2','迅捷姿态',[eff('Dodge',30),eff('Speed',0,5)],'闪避 +30%，速度 +5，持续到战斗结束'),
 react('spellblade_c3','天星剑返','Dodge','Strike','AllEnemies',300,3,1,'闪避对方攻击时发动，对所有敌人造成 300% 伤害'),
]
pools['魔法师'] = [
 P('mage_01','火球',130), P('mage_02','冰枪',120,status=MOD('Speed',-20,2)), P('mage_03','雷击',150,chant=2),
 P('mage_04','连锁闪电',90,t='AllEnemies',chant=2), P('mage_05','魔力飞弹',95), P('mage_06','酸雾',60,t='FoesColumn',status=DOT(5,3)),
 U('mage_07','奥术增幅','Self','SpellPower',35,3,chant=1), U('mage_08','减速术','Enemy','Speed',-30,3,chant=1), U('mage_09','虚弱术','Enemy','Attack',-25,3,chant=1),
 P('mage_10','流星',200,chant=3), P('mage_11','冰霜新星',80,t='AllEnemies',chant=1,status=MOD('Speed',-15,2)), U('mage_12','魔法盾','Self','Defence',40,3,chant=1),
 P('mage_13','烈焰柱',140,t='FoesColumn',chant=2), P('mage_14','禁言',40,ctrl=True,d='封住喉咙，打断咏唱'), U('mage_15','加速术','Ally','Speed',30,3,chant=1),
 P('mage_16','暗影箭',115,status=MOD('Defence',-15,2)), P('mage_17','魔力爆发',170,chant=2), U('mage_18','冥想','Self','SpellPower',20,4,chant=1),
 P('mage_19','火焰之雨',100,t='AllEnemies',chant=3), P('mage_20','元素冲击',125),
 charge('mage_c1','魔力共鸣','Hit',[eff('SpellPower',15)],4,4,'法术命中积累 1 层，持续 4 回合，每层法强 +15%，至多 4 层'),
 stance('mage_c2','奥术领域',[eff('SpellPower',40),eff('Defence',-20)],'法强 +40%，防御 -20%，持续到战斗结束'),
 react('mage_c3','法术反制','FoeChant','Spell','Enemy',150,4,2,'敌人开始咏唱时以 150% 法术轰击之，挂 4 回合，可发动 2 次'),
]
pools['刺客'] = [
 S('assassin_01','背刺',140,hit=-5), S('assassin_02','割喉',120,status=DOT(6,3)), S('assassin_03','飞刀',90,rng='Ranged',hit=10),
 S('assassin_04','连刺',80,hit=15), S('assassin_05','致残',100,status=MOD('Speed',-25,2)), S('assassin_06','暗器',70,rng='Ranged',status=DOT(4,4)),
 U('assassin_07','潜影','Self','Dodge',35,2), S('assassin_08','锁喉',85,ctrl=True,d='扼住喉咙，打断咏唱'), S('assassin_09','穿心',140,hit=-10),
 S('assassin_10','毒雾弹',50,rng='Ranged',t='FoesColumn',status=DOT(5,3)), S('assassin_11','影刃',110,hit=5), U('assassin_12','屏息','Self','Attack',25,2),
 S('assassin_13','断筋',95,status=MOD('Dodge',-20,2)), S('assassin_14','绞杀',130), S('assassin_15','袖箭',100,rng='Ranged'),
 U('assassin_16','烟幕','AllAllies','Dodge',15,2), S('assassin_17','处刑',145,hit=-10), S('assassin_18','疾风刺',90,hit=20),
 S('assassin_19','见血封喉',115,status=DOT(8,2)), S('assassin_20','回身刺',105),
 charge('assassin_c1','杀意','Kill',[eff('Damage',40),eff('Speed',0,2)],3,6,'击倒敌人积累 1 层，持续 6 回合，每层技能伤害 +40%、速度 +2，至多 3 层'),
 stance('assassin_c2','暗影姿态',[eff('Dodge',25),eff('Crit',0,10)],'闪避 +25%，暴击率 +10，持续到战斗结束'),
 react('assassin_c3','影返','Dodge','Strike','Enemy',250,3,2,'闪避对方攻击时以 250% 刺回来者，挂 3 回合，可发动 2 次'),
]
pools['弓箭手'] = [
 S('archer_01','速射',90,rng='Ranged',hit=10), S('archer_02','重箭',140,rng='Ranged',hit=-10), S('archer_03','穿杨',120,rng='Ranged',hit=20),
 S('archer_04','散射',70,rng='Ranged',t='FoesColumn'), S('archer_05','钉足箭',90,rng='Ranged',status=MOD('Speed',-25,2)), S('archer_06','火矢',100,rng='Ranged',status=DOT(5,3)),
 U('archer_07','屏息瞄准','Self','Attack',30,2), S('archer_08','惊弦',60,rng='Ranged',ctrl=True,d='一箭惊散术式，打断咏唱'), S('archer_09','连珠箭',85,rng='Ranged',hit=5),
 S('archer_10','箭雨',60,rng='Ranged',t='AllEnemies'), S('archer_11','破甲箭',110,rng='Ranged',status=MOD('Defence',-20,2)), U('archer_12','鹰眼','Self','Crit',0,3),
 S('archer_13','回马射',105,rng='Ranged'), S('archer_14','贯日',145,rng='Ranged',hit=-10), U('archer_15','轻身','Self','Dodge',25,2),
 S('archer_16','毒箭',80,rng='Ranged',status=DOT(6,3)), S('archer_17','狙杀',140,rng='Ranged',hit=-10), S('archer_18','流矢',95,rng='Ranged'),
 S('archer_19','牵制射击',75,rng='Ranged',status=MOD('Attack',-15,2)), S('archer_20','近身弓击',100),
 charge('archer_c1','专注','Hit',[eff('Crit',0,6)],5,4,'命中积累 1 层，持续 4 回合，每层暴击率 +6，至多 5 层'),
 stance('archer_c2','游射姿态',[eff('Speed',0,4),eff('Dodge',15)],'速度 +4，闪避 +15%，持续到战斗结束'),
 react('archer_c3','截击','FoeChant','Strike','Enemy',180,4,2,'敌人开始咏唱时一箭射去，180% 威力，挂 4 回合，可发动 2 次'),
]
pools['神官'] = [
 H('priest_01','治愈',60,chant=1), H('priest_02','大治愈',100,chant=2), H('priest_03','圣歌',35,chant=2,t='AllAllies'),
 P('priest_04','圣光',110), P('priest_05','裁决',150,chant=2), U('priest_06','祝福','Ally','Attack',20,3,chant=1),
 U('priest_07','护佑','Ally','Defence',35,3,chant=1), U('priest_08','神圣护盾','Ally','None',0,3,chant=1,kind='Points',power=60), P('priest_09','驱邪',90,t='FoesColumn',chant=1),
 P('priest_10','神罚',80,t='AllEnemies',chant=2), U('priest_11','迟滞祷言','Enemy','Speed',-25,2,chant=1), P('priest_12','沉默祷言',30,ctrl=True,d='以祷词压住对方的咒文，打断咏唱'),
 H('priest_13','急救祈祷',40,chant=1), U('priest_14','坚信','AllAllies','Defence',15,3,chant=2), P('priest_15','圣火',100,status=DOT(5,3)),
 U('priest_16','启示','Self','SpellPower',30,3,chant=1), S('priest_17','钉锤击',100), U('priest_18','虔敬','Self','Defence',25,2),
 H('priest_19','复苏之光',70,chant=2), U('priest_20','战歌祷词','AllAllies','Attack',12,3,chant=2),
 aura('priest_c1','圣域',[eff('Defence',20),eff('SpellPower',10)],'全体友方防御 +20%、法强 +10%，持续到战斗结束',chant=1),
 charge('priest_c2','信仰','Hurt',[eff('SpellPower',12)],5,5,'每受击一次积累 1 层，持续 5 回合，每层法强 +12%，至多 5 层'),
 react('priest_c3','神恩','Hurt','Heal','Self',80,3,2,'受击时为自己回复 80% 威力的生命，挂 3 回合，可发动 2 次'),
]
pools['女仆'] = [
 S('maid_01','扫帚横扫',80,t='FoesColumn'), S('maid_02','托盘拍击',90,ctrl=True,d='一托盘拍在脸上，打断咏唱'), S('maid_03','裙下飞刀',95,rng='Ranged',hit=10),
 H('maid_04','包扎',45), H('maid_05','红茶时间',25,t='AllAllies'), U('maid_06','整理仪容','Ally','Defence',25,3),
 U('maid_07','侍奉','Ally','Attack',20,3), S('maid_08','清扫',110), U('maid_09','冷眼','Enemy','Attack',-20,2),
 S('maid_10','餐刀连投',70,rng='Ranged',hit=15), U('maid_11','敏捷步法','Self','Dodge',25,2), S('maid_12','熨斗重击',130,hit=-10),
 S('maid_13','拂尘',60,status=MOD('Dodge',-20,2)), H('maid_14','提神点心',35), U('maid_15','催促','Ally','Speed',25,2),
 S('maid_16','银盘反光',50,rng='Ranged',t='AllEnemies'), S('maid_17','缠线',75,status=MOD('Speed',-25,2)), S('maid_18','钥匙串',100),
 U('maid_19','打扫干净','Self','Attack',25,2), S('maid_20','送客',130),
 aura('maid_c1','完美侍奉',[eff('Speed',0,2),eff('Defence',10)],'全体友方速度 +2、防御 +10%，持续到战斗结束'),
 charge('maid_c2','勤勉','Hit',[eff('Attack',8),eff('Dodge',5)],5,5,'命中积累 1 层，持续 5 回合，每层攻击 +8%、闪避 +5%，至多 5 层'),
 react('maid_c3','护主','Hurt','Strike','Enemy',180,3,2,'受击时以 180% 威力还击来者，挂 3 回合，可发动 2 次'),
]
pools['吟游诗人'] = [
 U('bard_01','激昂之歌','AllAllies','Attack',15,3), U('bard_02','安魂曲','AllAllies','Defence',15,3), H('bard_03','治愈之歌',30,t='AllAllies'),
 U('bard_04','急板','AllAllies','Speed',15,2), U('bard_05','催眠曲','Enemy','Speed',-30,2), P('bard_06','音爆',100),
 P('bard_07','不和弦',70,t='AllEnemies',status=MOD('Attack',-10,2)), S('bard_08','琴弦勒',85,ctrl=True,d='琴弦勒喉，打断咏唱'), U('bard_09','嘲弄','Enemy','Defence',-20,2),
 P('bard_10','回响',120,chant=2), S('bard_11','鲁特琴砸',110), U('bard_12','英雄叙事诗','Ally','Attack',30,2),
 H('bard_13','轻吟',45), U('bard_14','鼓舞','Ally','Dodge',20,3), P('bard_15','刺耳高音',90,t='FoesColumn'),
 U('bard_16','安神曲','Self','Defence',30,2), P('bard_17','终章',160,chant=2), S('bard_18','笛刺',95,hit=10),
 U('bard_19','狂想曲','AllAllies','Crit',0,3), P('bard_20','悲歌',80,status=DOT(5,3)),
 aura('bard_c1','行军曲',[eff('Attack',15),eff('Speed',0,2)],'全体友方攻击 +15%、速度 +2，持续到战斗结束'),
 charge('bard_c2','余韵','Hit',[eff('Damage',15),eff('SpellPower',10)],4,4,'命中积累 1 层，持续 4 回合，每层技能伤害 +15%、法强 +10%，至多 4 层'),
 stance('bard_c3','即兴姿态',[eff('Dodge',20),eff('Crit',0,8)],'闪避 +20%，暴击率 +8，持续到战斗结束'),
]
pools['圣骑士'] = [
 S('paladin_01','圣剑斩',115), S('paladin_02','盾击',90,ctrl=True,d='以盾面撞击，打断咏唱'), P('paladin_03','圣光弹',110),
 H('paladin_04','圣疗术',60,chant=1), S('paladin_05','审判之锤',140,hit=-10), S('paladin_06','十字斩',80,t='FoesColumn'),
 U('paladin_07','信仰之盾','Self','Defence',40,2), U('paladin_08','战意祷言','AllAllies','Attack',15,3,chant=1), P('paladin_09','神圣冲击',90,t='AllEnemies',chant=2),
 S('paladin_10','破邪斩',125,status=MOD('Defence',-15,2)), U('paladin_11','圣佑','Ally','Defence',30,3,chant=1), S('paladin_12','冲锋',135,hit=-10),
 P('paladin_13','制裁',150,chant=2), H('paladin_14','按手礼',40), S('paladin_15','连斩',95,hit=10),
 U('paladin_16','誓言','Self','Attack',25,2), P('paladin_17','灼罪',90,status=DOT(6,3)), S('paladin_18','守护一击',105,status=MOD('Attack',-15,2)),
 U('paladin_19','坚忍','Self','Dodge',15,3), S('paladin_20','天罚斩',140,hit=-10),
 stance('paladin_c1','守护者姿态',[eff('Defence',40),eff('Attack',10)],'防御 +40%，攻击 +10%，持续到战斗结束'),
 charge('paladin_c2','圣印','Hit',[eff('Damage',20),eff('SpellPower',10)],3,5,'命中积累 1 层，持续 5 回合，每层技能伤害 +20%、法强 +10%，至多 3 层'),
 react('paladin_c3','以牙还牙','Hurt','Spell','Enemy',220,3,2,'受击时以 220% 圣光回击来者，挂 3 回合，可发动 2 次'),
]
out = []
for ident, skills in pools.items():
    assert len([s for s in skills if 'core' not in s]) == 20 and len([s for s in skills if 'core' in s]) == 3, ident
    out.append(dict(defType='IdentitySkillPoolDef', defName=ident, label=ident, skills=skills))
# crit stat with pct 0 in archer_12/bard_19: use pct as flat via statusPower? fix: Crit via percent semantics -> flat in StatMod not supported; replace with Attack
for p in out:
    for s in p['skills']:
        if s.get('statusStat') == 'Crit':
            s['statusStat']='Attack'; s['statusPercent']=20
json.dump(dict(defType='IdentitySkillPoolDef', defs=out), open('/workspace/work/rimisekai/content/defs/identity_skills.json','w'), ensure_ascii=False, indent=1)
print(sum(len(p['skills']) for p in out))
