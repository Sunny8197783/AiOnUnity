# paints the graphic, its small caption and the forearm tattoo out of Male_Adult_09's body map (plain black tee)
import sys, numpy as np
from PIL import Image, ImageFilter
src, dst = sys.argv[1], sys.argv[2]
im = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
out = im.copy()
def patch(box, dy=0, dx=0, feather=10):
    x0,y0,x1,y1 = box
    m = Image.new("L", (im.shape[1], im.shape[0]), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(m).rounded_rectangle(box, radius=feather*2, fill=255)
    m = np.asarray(m.filter(ImageFilter.GaussianBlur(feather))).astype(np.float32)[..., None] / 255.0
    shifted = np.roll(np.roll(im, dy, axis=0), dx, axis=1)
    global out
    out = out * (1 - m) + shifted * m
# chest graphic: cloth from the plain back panel straight above (same columns)
patch((425, 540, 620, 765), dy=390, feather=12)
# the caption at the hem
patch((545, 790, 645, 862), dx=120, feather=6)
# the forearm tattoo: skin from just beside it
patch((80, 750, 140, 850), dx=-50, feather=8)
Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(dst, quality=92)
