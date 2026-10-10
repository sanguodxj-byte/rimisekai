# -*- coding: utf-8 -*-
"""
生成 3 张不同萌系面相的女仆立绘（不覆盖原图，叠加保存）
差分1：垂目治愈相（Tareme + 泪痣 + 腼腆抿嘴）
差分2：吊目猫系相（Tsurime + 小虎牙 + 傲娇坏笑）
差分3：半睁三无相（Jitome + 平嘴一字 + 清冷冷面）
"""
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request

PROJ = r"D:\123\rimisekai"
API = "https://sakiko.dev/v1/images/edits"
import os as _os, sys as _sys
_sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from _keys import get as _getkey
API_KEY = _getkey("sakiko")
MODEL = "image/chat2api-gpt-image-2.5"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

STYLE = (
    "Match the art style, stroke texture, and contrast palette of the reference image: "
    "clean graphic ink manga illustration, pure pitch black background (#000000), bone-white ink (#E9EFEA) only. "
    "STRICTLY BORDERLESS: completely borderless, clean unbordered, absolutely no frame, no border lines, "
    "no corner flourishes, no outer box, no decorative borders of any kind. The character stands freely and "
    "completely unconstrained on a pure pitch-black negative space extending edge-to-edge, full body visible "
    "from head to feet, nothing cropped. "
    "NOISE ELIMINATION AND CLEAN LINEWORK: crisp continuous ink outlines, broad smooth solid filled tonal "
    "blocks, uncluttered surfaces. Shadows and contours are rendered with clean graphic solid shapes and "
    "widely-spaced neat parallel lines, NEVER dense scratchy hatching, NO stippling, NO cross-hatching, "
    "NO grainy textures, NO visual noise. Pure low-frequency graphic clarity, smooth anime surfaces with "
    "crisp edges. "
)

NEGATIVE = (
    "Negative prompt: flags, banners, pennants, heraldry, coat of arms, crests, emblems, insignias, "
    "frame, border, double lines, ornamental frame, corner flourish, outer box, rectangular border, "
    "high frequency noise, scratchy hatching, dense micro-hatching, stippling, gritty texture, messy sketch "
    "lines, film grain, speckles, dust, gradients, airbrush, 3d render, color, colored. "
    "Negative: mature, adult, elderly, tall heavy build, realistic human, ugly face. "
    "CRITICAL ANATOMY: no missing arm, no hidden arm, no amputated arm, no single-arm pose, "
    "no extra limbs, no three hands, no floating hands, no severed hands, no claw-like fingers, "
    "no floating props, no broken legs, no three legs. "
    "No East Asian elements, no curved tile roofs, no paper lanterns."
)

BASE_AGE = (
    "Full-body vertical 9:16 character portrait. "
    "Young anime maiden of 16 to 18 years old, late-teenage build with slender graceful proportions, bishoujo. "
    "Subtle three-quarter head tilt so the far eye is visibly smaller than the near eye, both eyes angle "
    "slightly inward, not a flat symmetrical head-on stare. "
)

JOBS = [
    (
        "立绘_女仆_萌相_差分1.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - TAREME GENTLE HEALING TYPE: gentle drooping anime eyes with soft downturned outer corners (tareme eyes), delicate long eyelashes, a tiny teardrop beauty mark mole under her left eye, shy gentle smile with closed lips, blushing cheeks, rounded youthful chin, kind and bashful facial expression. "
        "A young anime human maid with a neat black bob haircut and straight bangs, wearing a classic ankle-length black maid gown with a crisp white apron, frilled headband and lace cuffs. "
        "BOTH ARMS AND HANDS FULLY VISIBLE: one hand carrying a small silver tea tray at her hip, the other hand gently lifting her apron hem in a polite curtsy. Both legs and black shoes grounded. Clean graceful posture."
    ),
    (
        "立绘_女仆_萌相_差分2.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - TSURIME CAT-LIKE TSUMERE TYPE: sharp upward-slanted anime eyes with upturned outer corners (tsurime cat eyes), crisp cat-like eyelashes, confident playful smirk showing a single cute snaggletooth fang (yaeba) on one side, pointed sharp chin, lively mischievous and feisty facial expression. "
        "A young anime cat-eared maid with long bone-white hair in twin tails, cat ears on head and striped cat tail behind her, wearing a cute dark maid uniform with a short pleated skirt, white apron and lace trims. "
        "BOTH ARMS FULLY VISIBLE: standing facing forward, both hands holding a feather duster in front of her chest, playful energetic stance, both legs clearly separated in black stockings, two mary-jane shoes planted firmly."
    ),
    (
        "立绘_女仆_萌相_差分3.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - JITOME KUUDERE DEADPAN TYPE: calm half-closed anime eyes with heavy upper eyelids (jitome sleepy cool gaze), small restrained pupils with minimal highlight, straight neutral flat mouth line (kuudere deadpan expression), cool emotionless but adorable expression, stoic quiet composure. "
        "A young anime rabbit-eared maid with a high pale ponytail and long white rabbit ears, in an elegant floor-length maid gown with puffed sleeves and a wide white apron. "
        "BOTH ARMS FULLY VISIBLE: holding a silver teapot on a tray with both hands in front of her, calm poised gliding stance, dress hem revealing the tips of two black shoes."
    ),
]


def main():
    ref_path = os.path.join(PROJ, "assets", "title_reference.png")
    with open(ref_path, "rb") as f:
        ref_b64 = base64.b64encode(f.read()).decode("ascii")

    for out_name, prompt in JOBS:
        out_path = os.path.join(PROJ, out_name)
        full_prompt = prompt + " " + STYLE + NEGATIVE
        data = json.dumps({
            "model": MODEL,
            "prompt": full_prompt,
            "image": "data:image/png;base64," + ref_b64,
            "size": "1024x1792",
            "n": 1,
        }).encode("utf-8")

        for attempt in range(1, 5):
            try:
                req = urllib.request.Request(
                    API, data=data,
                    headers={
                        "Content-Type": "application/json",
                        "Authorization": "Bearer " + API_KEY,
                        "User-Agent": USER_AGENT,
                    },
                    method="POST",
                )
                with urllib.request.urlopen(req, timeout=200) as resp:
                    obj = json.loads(resp.read().decode("utf-8"))
                png = base64.b64decode(obj["data"][0]["b64_json"])
                with open(out_path, "wb") as f:
                    f.write(png)
                print(f"done {out_name} attempt {attempt}", flush=True)
                break
            except urllib.error.HTTPError as e:
                print(f"http {e.code} {out_name} attempt {attempt}", flush=True)
                time.sleep(15 if e.code == 429 else 8)
            except Exception as e:
                print(f"err {e} {out_name} attempt {attempt}", flush=True)
                time.sleep(6)
        time.sleep(3)


if __name__ == "__main__":
    main()
