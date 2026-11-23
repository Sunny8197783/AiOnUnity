"""Repaints Medical_Female_01's white medical scrubs (f152_body_color) into the 304호 elder's everyday late-summer
clothes: a short-sleeved dusty-rose blouse with a small, low-contrast flower print (the scrub top's blue V-neck
undershirt becomes the blouse's button band), dark grey-brown trousers and plain dark brown shoes (they were white
clogs). The fabric's own folds and seams are kept: each texel keeps its lightness relative to the white cloth.
python -I dress_elder.py <raw Textures dir (tga)> <Textures dir (prep_textures output, updated in place)>
writes f152_body_color.jpg (1024)."""
import sys, os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

raw, dst = sys.argv[1:3]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from prep_textures import push_pull

LUM = np.array([0.299, 0.587, 0.114], np.float32)
src = np.asarray(Image.open(os.path.join(raw, 'f152_body_color.tga')).convert('RGB'))
H = W = src.shape[0]
assert W == 2048, src.shape
a = src.astype(np.float32) / 255
L = a @ LUM
mx = a.max(2); mn = a.min(2); sat = (mx - mn) / np.maximum(mx, 1e-4)
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
fg = src.max(2) > 3

# cloth = the white fabric and the blue undershirt/hem (everything that is not skin); skin is clearly orange
skin = (sat > 0.28) & (a[..., 0] > a[..., 2] * 1.25) & (a[..., 0] >= a[..., 1])
blue = (a[..., 2] > a[..., 0] * 1.08) & (sat > 0.10)
cloth = fg & ~skin
# regions of the Rocketbox female body UV (2048 px): shoes top-centre left/right, trouser legs bottom-left/right,
# everything else that is cloth is the top (front, back, sleeves)
shoes = ((xx > 230) & (xx < 700) | (xx > 1348) & (xx < 1818)) & (yy < 600)
legs = (yy > 1190) & ((xx < 652) | (xx > 1424))
top = cloth & ~shoes & ~legs

# shading of the white cloth relative to its own typical lightness (folds, seams, buttons, pocket edges)
white_ref = np.median(L[top & ~blue & (L > 0.5)])
shade_w = np.clip(L / white_ref, 0.25, 1.15)
blue_ref = np.median(L[top & blue]) if (top & blue).any() else white_ref
shade_b = np.clip(L / blue_ref, 0.25, 1.3)
shade = np.where(blue, shade_b, shade_w)
# soften the shading a little: the white cloth's folds are strong once multiplied into a mid colour
shade = 1 + (shade - 1) * 0.85

# flower print: small five-petal blossoms with two leaves, on a jittered grid (even spacing, no clumps that read as
# stains), petals a light rose-cream or a deeper rose, a muted gold centre and soft green leaves, all close in lightness
# to the dusty-rose ground so that from a few metres the blouse reads as a quiet print, not as white speckles
W2 = 2 * W                                                          # drawn at 2x, then reduced (smooth edges)
petal_l = Image.new('L', (W2, W2), 0); deep_l = Image.new('L', (W2, W2), 0)
centre_l = Image.new('L', (W2, W2), 0); leaf_l = Image.new('L', (W2, W2), 0)
dp, dd, dc, dl = (ImageDraw.Draw(i) for i in (petal_l, deep_l, centre_l, leaf_l))
rng = random.Random(304)
step = 64                                                           # grid step at 2x (32 px in the 2048 map)
for gy in range(0, W2, step):
    for gx in range(0, W2, step):
        x = gx + rng.uniform(0.1, 0.9) * step + (step / 2 if (gy // step) % 2 else 0)
        y = gy + rng.uniform(0.1, 0.9) * step
        r = rng.uniform(11, 15); rot = rng.uniform(0, 6.283)
        for side in (-1, 1):                                        # two leaves
            ang = rot + 0.6 + side * 0.55 + 3.1416
            lx, ly = x + np.cos(ang) * r * 1.55, y + np.sin(ang) * r * 1.55
            dl.ellipse([lx - r * 0.55, ly - r * 0.55, lx + r * 0.55, ly + r * 0.55], fill=255)
        tgt = dd if rng.random() < 0.35 else dp
        for k in range(5):
            ang = rot + k * 1.2566
            px, py = x + np.cos(ang) * r * 0.62, y + np.sin(ang) * r * 0.62
            tgt.ellipse([px - r * 0.50, py - r * 0.50, px + r * 0.50, py + r * 0.50], fill=255)
        dc.ellipse([x - r * 0.26, y - r * 0.26, x + r * 0.26, y + r * 0.26], fill=255)
m2 = lambda im: (np.asarray(im.resize((W, H), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6))).astype(np.float32) / 255)[..., None]
petal, deep, centre, leafm = m2(petal_l), m2(deep_l), m2(centre_l), m2(leaf_l)
leafm = leafm * (1 - np.clip(petal + deep, 0, 1))

rose = np.array([0.64, 0.49, 0.51], np.float32)       # dusty rose ground
cream = np.array([0.76, 0.66, 0.63], np.float32)      # light rose-cream petals
deep_rose = np.array([0.54, 0.37, 0.41], np.float32)  # deeper rose petals (about a third of the blossoms)
gold = np.array([0.66, 0.55, 0.40], np.float32)       # muted centre
leaf = np.array([0.47, 0.52, 0.44], np.float32)       # soft green leaves
fabric = rose * (1 - leafm) + leaf * leafm
fabric = fabric * (1 - petal) + cream * petal
fabric = fabric * (1 - deep) + deep_rose * deep
fabric = fabric * (1 - centre) + gold * centre
band = rose * 0.86                                       # button band (was the blue undershirt) and inner hem
top_col = np.where(blue[..., None], band, fabric) * shade[..., None]

trousers = np.array([0.25, 0.235, 0.22], np.float32) * shade_w[..., None]
shoe_col = np.array([0.24, 0.18, 0.14], np.float32) * np.clip(L / np.median(L[shoes & cloth]), 0.3, 1.2)[..., None]

out = a.copy()
for m, col in ((top, top_col), (legs & cloth, trousers), (shoes & cloth, shoe_col)):
    out[m] = col[m]
# soft edge between skin and cloth: blend 2 px where the classification flips
edge = np.asarray(Image.fromarray((cloth * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.0))).astype(np.float32)[..., None] / 255
out = a * (1 - edge) + out * edge
out = np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8)
out = push_pull(out, fg)
Image.fromarray(out).resize((1024, 1024), Image.LANCZOS).save(os.path.join(dst, 'f152_body_color.jpg'), 'JPEG', quality=90, optimize=True)
print('dressed f152 ->', dst)
