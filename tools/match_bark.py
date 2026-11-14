"""Shift baked branch textures to the tree's bark colour (Nick's request 2026-11-14: branches lighter, matching the trunk).
Run after bake_branches.py:
  python -I match_bark.py <LoP-Core zip> [woodType] [strength]
Per channel, every pixel is scaled by (bark mean / branch mean), so the EM connector grain and shading stay,
while the average colour moves to the bark's. strength 1.0 = full match, 0.0 = unchanged.
Bark source: assets/lopcore/textures/block/wood/bark/<wood>.png inside the given zip.
"""
import glob, io, os, sys, zipfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "assets", "ecomachinatintpagefix", "textures", "branch", "lopcore")


def mean_rgb(img):
    px = [p for p in img.get_flattened_data() if p[3] > 0] if hasattr(img, "get_flattened_data") else [p for p in img.getdata() if p[3] > 0]
    n = len(px) or 1
    return [sum(p[i] for p in px) / n for i in range(3)]


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    bark_zip = sys.argv[1]
    wood = sys.argv[2] if len(sys.argv) > 2 else "sleepwillow"
    strength = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
    entry = "assets/lopcore/textures/block/wood/bark/%s.png" % wood
    bark = Image.open(io.BytesIO(zipfile.ZipFile(bark_zip).read(entry))).convert("RGBA")
    target = mean_rgb(bark)
    print("bark %s mean %.1f %.1f %.1f lum %.1f" % (entry, target[0], target[1], target[2], lum(target)))
    files = sorted(glob.glob(os.path.join(OUT, wood + "-*.png")))
    if not files:
        sys.exit("no baked files for " + wood)
    for p in files:
        img = Image.open(p).convert("RGBA")
        src = mean_rgb(img)
        k = [1.0 + strength * (target[i] / src[i] - 1.0) if src[i] > 0 else 1.0 for i in range(3)]
        bands = list(img.split())
        for i in range(3):
            bands[i] = bands[i].point(lambda v, f=k[i]: max(0, min(255, int(round(v * f)))))
        out = Image.merge("RGBA", bands)
        out.save(p)
        new = mean_rgb(out)
        clip = sum(1 for q in (out.get_flattened_data() if hasattr(out, "get_flattened_data") else out.getdata()) if q[3] > 0 and max(q[:3]) == 255)
        print("%s mean %.1f %.1f %.1f (lum %.1f) -> %.1f %.1f %.1f (lum %.1f), clipped px %d" % (
            os.path.basename(p), src[0], src[1], src[2], lum(src), new[0], new[1], new[2], lum(new), clip))


if __name__ == "__main__":
    main()
