# -*- coding: utf-8 -*-
"""
tools/gen_moefaces_batch2.py
使用 sakiko.dev API 并发生成 3 套职业身份的萌相差分立绘（共 9 张）：
- 骑士（差分1~3）：坚毅元气目、好胜猫系吊目+虎牙、清冷三无半睁目
- 商人（差分1~3）：精明月牙笑眼、活泼小恶魔吊目+虎牙、纯真垂目仓鼠相
- 学者（差分1~3）：弱气腼腆垂目+泪痣、自信眼镜傲娇吊目、困顿三无书呆目
绝不覆盖原有立绘，独立命名为 立绘_<身份>_萌相_差分<1~3>.png
"""

import os
import sys
import json
import time
import base64
import urllib.request
import urllib.error
from concurrent.futures import ThreadPoolExecutor, as_completed

PROJ = r"D:\123\rimisekai"
API = "https://sakiko.dev/v1/images/edits"
API_KEY = "REDACTED_API_KEY"
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
    "no floating shields, no detached shields, no floating items, no broken legs, no three legs. "
    "No East Asian elements, no curved tile roofs, no paper lanterns."
)

BASE_AGE = (
    "Full-body vertical 9:16 character portrait. "
    "Young anime maiden of 16 to 18 years old, late-teenage build with slender graceful proportions, bishoujo. "
    "Subtle three-quarter head tilt so the far eye is visibly smaller than the near eye, both eyes angle "
    "slightly inward, not a flat symmetrical head-on stare. "
)

JOBS = [
    # --- 骑士 ---
    (
        "立绘_骑士_萌相_差分1.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - MARU-ME RESOLUTE INNOCENT TYPE: large round sparkling manga eyes with firm clear pupils and starry highlights (maru-me eyes), clean neat straight eyebrows, gentle closed lips in a resolute and dutiful expression, youthful rounded cheeks, heroic yet adorable knight maiden face. "
        "A young anime human female knight with short silver bob hair, in plain bone-white full plate armour, standing straight and dignified. "
        "BOTH ARMS AND SHIELD CLEARLY HELD: one gauntleted hand firmly gripping the hilt of a longsword planted point-down before her, the other gauntleted arm and hand firmly gripping the inner handle of a completely PLAIN UNADORNED smooth heater shield braced against her hip. Both legs armoured and grounded. Absolutely no heraldry, completely plain blank shield."
    ),
    (
        "立绘_骑士_萌相_差分2.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - TSURIME SPIRITED CAT/WOLF TYPE: sharp upward-slanted eyes with crisp upturned outer corners (tsurime eyes), spirited feline gaze, a confident playful smirk showing a single cute snaggletooth fang (yaeba), sharp slender chin, lively feisty and competitive facial expression. "
        "A young anime wolf-eared female knight with long black ponytail hair and wolf ears on head, in dark plate armour over a surcoat, tail behind her. "
        "BOTH ARMS CLEARLY ENGAGED: raising her longsword in a crisp formal salute with one hand, her other arm firmly holding a plain smooth heater shield against her side. Stable wide stance, feet grounded."
    ),
    (
        "立绘_骑士_萌相_差分3.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - JITOME COOL STOIC TYPE: half-closed calm anime eyes with heavy straight upper eyelids (jitome cool gaze), small restrained pupils, straight flat unsmiling mouth line (stoic kuudere expression), serene and emotionless composure. "
        "A young anime horse-eared female knight with a high pale ponytail and horse ears, wearing polished plate armour with a short cape. "
        "BOTH ARMS AND SHIELD CLEARLY ACCOUNTED FOR: one hand holding her longsword resting gently over her shoulder, the other arm firmly holding and strapped into a completely PLAIN BLANK smooth heater shield in front of her. Confident contrapposto stance, both boots firmly planted. Absolutely no fleur-de-lis, no emblems on shield."
    ),

    # --- 商人 ---
    (
        "立绘_商人_萌相_差分1.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - KITSUNE-ME SLY SMILING TYPE: cheerful curved crescent-moon smiling eyes (kitsune-me squinting smiling eyes), long dark lashes, playful mysterious and sly knowing smile with curved lips, clever charming and slightly teasing facial expression. "
        "A young anime human female merchant girl with a neat shoulder-length dark bob, in a smart travelling coat over a blouse and long skirt. "
        "BOTH ARMS CLEARLY ENGAGED: holding a leather ledger book in one hand and a silver quill pen in the other hand, presenting her accounts with both hands active. Both shoes visible under skirt hem. Friendly brisk business posture."
    ),
    (
        "立绘_商人_萌相_差分2.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - TSURIME PLAYFUL MISCHIEVOUS TYPE: vibrant upturned anime cat/fox eyes (tsurime eyes), lively sparkling pupils, cheerful open-mouthed grin showing a cute little tooth, pointed energetic chin, clever mischievous expression. "
        "A young anime fox-eared merchant girl with long pale hair, fox ears on head and fluffy bushy fox tail behind her, in a layered merchant robe with deep pockets. "
        "BOTH ARMS FULLY VISIBLE: holding a small brass balance scale aloft in one hand, while her other hand rests on a coin pouch at her waist. Both feet grounded in leather boots."
    ),
    (
        "立绘_商人_萌相_差分3.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - TAREME GENTLE NAIVE TYPE: sweet gentle downturned anime eyes (tareme puppy eyes), soft lowered lashes, bashful little smile with slightly chubby youthful cheeks, kind, innocent and hardworking expression. "
        "A young anime mouse-eared merchant girl with short dark hair and round mouse ears on head, in a neat vest and white work apron over a skirt. "
        "BOTH ARMS ACCOUNTED FOR: holding a small coin pouch with both hands in front of her chest in an earnest gesture. Both boots planted on the floor."
    ),

    # --- 学者 ---
    (
        "立绘_学者_萌相_差分1.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - TAREME SHY BOOKWORM TYPE: timid drooping anime eyes with soft downturned outer corners (tareme eyes), a tiny delicate beauty mark mole under her right eye, slightly worried shy eyebrows, bashful pursed mouth, intellectual gentle bookworm face. "
        "A young anime human female scholar with a neat black bob haircut and straight bangs, in a long dark academic gown with a high collar. "
        "BOTH ARMS CAREFULLY SHOWN: holding a heavy open book with BOTH hands in front of her chest, fingers firmly supporting the book spine and pages. Both feet visible below robe hem."
    ),
    (
        "立绘_学者_萌相_差分2.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - MEGANE TSURIME CONFIDENT TYPE: wearing delicate round thin-frame spectacles/glasses, behind the glasses are sharp confident upturned anime eyes (tsurime eyes), small intellectual smirk, high intelligent forehead, proud inquisitive expression. "
        "A young anime elf-eared scholar with long pale hair and slender elf ears, in layered academic robes with wide sleeves. "
        "BOTH ARMS FULLY ENGAGED: holding a rolled scroll in one hand, while her other hand raises a circular magnifying lens toward the viewer. Both boots grounded. Scholarly poised stance."
    ),
    (
        "立绘_学者_萌相_差分3.png",
        BASE_AGE +
        "DISTINCTIVE MOE FACE - JITOME DROWSY KUUDERE TYPE: drowsy half-open anime eyes with heavy eyelids (jitome sleepy gaze), small dark pupils, flat neutral emotionless mouth line, quiet calm deadpan expression as if up all night reading. "
        "A young anime mouse-eared scholar girl with twin dark braids and round mouse ears on head, in a practical scholar tunic with pockets, long slender mouse tail behind her. "
        "BOTH ARMS SUPPORTING STACK: both arms wrapped securely around a tall stack of books, both hands visible clasping and supporting the stack from bottom and side. Both boots firmly grounded."
    ),
]


def generate_single(out_name, prompt, ref_b64):
    out_path = os.path.join(PROJ, out_name)
    if os.path.exists(out_path):
        print(f"[SKIP] {out_name} already exists", flush=True)
        return out_name, True

    full_prompt = prompt + " " + STYLE + NEGATIVE
    data = json.dumps({
        "model": MODEL,
        "prompt": full_prompt,
        "image": "data:image/png;base64," + ref_b64,
        "size": "1024x1792",
        "n": 1,
    }).encode("utf-8")

    for attempt in range(1, 6):
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
            with urllib.request.urlopen(req, timeout=180) as resp:
                obj = json.loads(resp.read().decode("utf-8"))
            png = base64.b64decode(obj["data"][0]["b64_json"])
            with open(out_path, "wb") as f:
                f.write(png)
            print(f"[SUCCESS] {out_name} completed on attempt {attempt}", flush=True)
            return out_name, True
        except urllib.error.HTTPError as e:
            err_body = e.read().decode("utf-8", "ignore")
            print(f"[HTTP {e.code}] {out_name} attempt {attempt}: {err_body[:100]}", flush=True)
            time.sleep(10 if e.code == 429 else 5)
        except Exception as e:
            print(f"[ERR] {out_name} attempt {attempt}: {e}", flush=True)
            time.sleep(5)
    return out_name, False


def main():
    ref_path = os.path.join(PROJ, "assets", "title_reference.png")
    with open(ref_path, "rb") as f:
        ref_b64 = base64.b64encode(f.read()).decode("ascii")

    print(f"=== 开始通过 sakiko.dev 并发生成 9 张全新萌相立绘（骑士、商人、学者）===", flush=True)
    t0 = time.time()

    # 3 并发线程池
    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = {executor.submit(generate_single, name, p, ref_b64): name for name, p in JOBS}
        for future in as_completed(futures):
            name, ok = future.result()

    print(f"=== 9 张立绘并发生成全部结束，耗时 {time.time()-t0:.1f} 秒 ===", flush=True)


if __name__ == "__main__":
    main()
