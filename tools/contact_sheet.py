import sys, zipfile, io
from PIL import Image, ImageDraw
S=128
em=r"C:\Work\vs\master-unpack\vsecomachina\assets\vsecomachina\textures\block\wood\foliage-connector\em_connector_1.png"
z=zipfile.ZipFile(r"C:\Work\vs\vs_scratch\sleep-willow-design-20261112\test-mod\sleepwillowtest-0.1.12.zip")
bark=Image.open(io.BytesIO(z.read("assets/lopcore/textures/block/wood/bark/sleepwillow.png"))).convert("RGBA")
tiles=[("bark",bark),("EM brown",Image.open(em).convert("RGBA"))]
for i in range(1,7):
    tiles.append((f"baked {i}",Image.open(rf"src\assets\ecomachinatintpagefix\textures\branch\lopcore\sleepwillow-{i}.png").convert("RGBA")))
W=len(tiles)*(S+8)+8; sheet=Image.new("RGBA",(W,S+28),(60,90,60,255)); d=ImageDraw.Draw(sheet)
for k,(n,im) in enumerate(tiles):
    x=8+k*(S+8); sheet.alpha_composite(im.resize((S,S),Image.NEAREST),(x,4)); d.text((x,S+8),n,fill=(255,255,255,255))
out=sys.argv[1]; sheet.save(out); print(out, sheet.size)
