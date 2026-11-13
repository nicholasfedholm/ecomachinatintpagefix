"""Bake sleepwillow-colored EM branch textures, reproducing Eco Machina's tint algorithm.
Run: python -I bake_branches.py [woodType] [barkSource]
Defaults: sleepwillow, sleepwillowtest-0.1.12.zip bark. Output: src/assets/ecomachinatintpagefix/textures/branch/<domain>/<wood>-N.png
Algorithm refs: BarkColorExtractor.cs, TextureTinter.cs, HsvUtils.cs (EM 0.6.1 decompile).
Config (shipped ecomachina.json): GradientBlend 0.7, SaturationMul 1.0, ValueMul 1.0.
"""
import io, math, os, sys, zipfile
from PIL import Image

EM = r"C:\Work\vs\master-unpack\vsecomachina\assets\vsecomachina\textures\block\wood\foliage-connector\em_connector_%d.png"
BARK_ZIP = r"C:\Work\vs\vs_scratch\sleep-willow-design-20261112\test-mod\sleepwillowtest-0.1.12.zip"
BARK_ENTRY = "assets/lopcore/textures/block/wood/bark/%s.png"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "assets", "ecomachinatintpagefix", "textures", "branch", "lopcore")
GRADIENT, SATMUL, VALMUL = 0.7, 1.0, 1.0

def rgb2hsv(r, g, b):
    mx, mn = max(r, g, b), min(r, g, b); d = mx - mn
    if d < 1e-6: h = 0.0
    elif mx == r: h = math.fmod((g - b) / d, 6.0)
    elif mx == g: h = (b - r) / d + 2.0
    else: h = (r - g) / d + 4.0
    h /= 6.0
    if h < 0: h += 1.0
    return h, (0.0 if mx < 1e-6 else d / mx), mx

def hsv2rgb(h, s, v):
    h = frac(h); s = c01(s); v = c01(v)
    if s < 1e-6: return v, v, v
    x = h * 6.0; i = int(math.floor(x)) % 6; f = x - math.floor(x)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    return [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)][i]

def frac(x):
    n = x - math.floor(x)
    return n + 1 if n < 0 else n

def c01(x): return 0.0 if x < 0 else (1.0 if x > 1 else x)

def samples(px, w, h, center=0.6, count=256, minb=0.15):
    m = (1 - center) * 0.5
    x0 = max(0, int(math.floor(w * m))); x1 = min(w, int(math.ceil(w * (1 - m))))
    if x1 <= x0: x1 = x0 + 1
    step = max(1, ((x1 - x0) * h) // count)
    out = []
    for i in range(h):
        for j in range(x0, x1, step):
            r, g, b, a = px[i * w + j]
            if a == 0: continue
            r, g, b = r / 255.0, g / 255.0, b / 255.0
            l = r * 0.299 + g * 0.587 + b * 0.114
            if l < minb: continue
            out.append((r, g, b, l))
    return out

FALLBACK = (1 / 12, 0.5, 0.5)

def dominant(px, w, h, center):
    s = samples(px, w, h, center)
    if not s: return FALLBACK
    n = len(s)
    return rgb2hsv(sum(t[0] for t in s) / n, sum(t[1] for t in s) / n, sum(t[2] for t in s) / n)

def dominant_bright(px, w, h, frac_=0.25):
    s = samples(px, w, h, 0.6)
    if not s: return FALLBACK
    n = len(s)
    mean = rgb2hsv(sum(t[0] for t in s) / n, sum(t[1] for t in s) / n, sum(t[2] for t in s) / n)
    s.sort(key=lambda t: t[3])
    k = min(max(int(math.floor(n * (1 - frac_))), 0), n - 1)
    top = s[k:]
    if len(top) < 4: return mean
    m = len(top)
    return rgb2hsv(sum(t[0] for t in top) / m, sum(t[1] for t in top) / m, sum(t[2] for t in top) / m)

def main():
    wood = sys.argv[1] if len(sys.argv) > 1 else "sleepwillow"
    z = zipfile.ZipFile(BARK_ZIP)
    bark = Image.open(io.BytesIO(z.read(BARK_ENTRY % wood))).convert("RGBA")
    bpx = list(bark.getdata())
    base_img = Image.open(EM % 1).convert("RGBA")
    base = dominant(list(base_img.getdata()), *base_img.size, 1.0)
    tgt = dominant_bright(bpx, *bark.size)
    dh = tgt[0] - base[0]
    if dh > 0.5: dh -= 1
    if dh < -0.5: dh += 1
    sr = (tgt[1] / base[1]) if base[1] > 0.01 else 1.0
    vr = (tgt[2] / base[2]) if base[2] > 0.01 else 1.0
    sat, val = sr * SATMUL, vr * VALMUL
    insig = abs(dh) < 0.02 and abs(sat - 1) < 0.05 and abs(val - 1) < 0.05
    print("bark", BARK_ZIP, BARK_ENTRY % wood, bark.size)
    print("base HSV %.4f %.4f %.4f  target HSV %.4f %.4f %.4f" % (*base, *tgt))
    print("hueShift=%.4f satMul=%.4f valMul=%.4f insignificant(EM would skip)=%s" % (dh, sat, val, insig))
    os.makedirs(OUT, exist_ok=True)
    for n in range(1, 7):
        img = Image.open(EM % n).convert("RGBA"); w, h = img.size
        src = list(img.getdata()); res = []
        for i in range(h):
            t = 0.0 if False else (1.0 if h == 1 else i / (h - 1))
            wgt = 1 - (1 - t) * GRADIENT
            hs, sm, vm = dh * wgt, 1 + (sat - 1) * wgt, 1 + (val - 1) * wgt
            for j in range(w):
                r, g, b, a = src[i * w + j]
                if a == 0: res.append((r, g, b, a)); continue
                H, S, V = rgb2hsv(r / 255.0, g / 255.0, b / 255.0)
                R, G, B = hsv2rgb(frac(H + hs), c01(S * sm), c01(V * vm))
                res.append((int(round(c01(R) * 255)), int(round(c01(G) * 255)), int(round(c01(B) * 255)), a))
        out = Image.new("RGBA", (w, h)); out.putdata(res)
        p = os.path.join(OUT, "%s-%d.png" % (wood, n)); out.save(p)
        op = [q for q in res if q[3] > 0]
        print("%s %s mean RGB (alpha>0) %.1f %.1f %.1f" % (os.path.basename(p), out.size,
              *(sum(q[c] for q in op) / len(op) for c in range(3))))

main()
