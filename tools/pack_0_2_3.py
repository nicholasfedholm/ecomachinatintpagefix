"""Pack 0.2.3: reuse the 0.2.2 dll (code unchanged), bump modinfo, take the bark-matched PNGs from src."""
import hashlib, json, os, re, zipfile

FIX = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OLD = os.path.join(FIX, "out", "ecomachinatintpagefix_0.2.2.zip")
NEW = os.path.join(FIX, "out", "ecomachinatintpagefix_0.2.3.zip")
MI = os.path.join(FIX, "src", "modinfo.json")
TEX = "assets/ecomachinatintpagefix/textures/branch/lopcore/sleepwillow-%d.png"

t = open(MI, encoding="utf-8").read()
t2 = re.sub(r'"version"\s*:\s*"0\.2\.2"', '"version": "0.2.3"', t, count=1)
assert t2 != t or '"0.2.3"' in t
open(MI, "w", encoding="utf-8").write(t2)
assert json.loads(t2)["version"] == "0.2.3"

old = zipfile.ZipFile(OLD)
dll = old.read("EcoMachinaTintPageFix.dll")
with zipfile.ZipFile(NEW, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("EcoMachinaTintPageFix.dll", dll)
    z.writestr("modinfo.json", t2)
    for i in range(1, 7):
        z.write(os.path.join(FIX, "src", *TEX.split("/")) % i if False else os.path.join(FIX, "src", TEX % i), TEX % i)

z = zipfile.ZipFile(NEW)
print("testzip", z.testzip(), "entries", len(z.namelist()), "bytes", os.path.getsize(NEW))
print("modinfo version", json.loads(z.read("modinfo.json"))["version"])
print("dll identical to 0.2.2:", z.read("EcoMachinaTintPageFix.dll") == dll)
changed = sum(1 for i in range(1, 7) if z.read(TEX % i) != old.read(TEX % i))
print("pngs changed vs 0.2.2:", changed, "of 6")
print("sha256", hashlib.sha256(open(NEW, "rb").read()).hexdigest()[:12].upper())
