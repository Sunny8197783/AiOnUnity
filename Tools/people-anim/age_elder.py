"""Elder (Rocketbox Business_Female_02, prefix f015), second pass after review.
- hair: neutral, slightly warm grey with darker pepper strands and a compressed range (no chrome/silver sheen)
- skin stays warm: tiny desaturation plus a warm/yellow shift, never mixed toward grey
- wrinkles: soft folds (wider, blurred, lower amplitude), straight-ish forehead lines, symmetric about the measured
  face centre (x=1024 in the 2048 map; eye holes measured at x~882 and ~1165, y~578)
- no under-eye darkening; age spots only low on the cheeks and temples, faint
python -I age_elder2.py <raw Textures dir (tga)> <prepared Textures dir> <rbtools dir> <out dir>
writes f015_head_color.png (2048), f015_opacity_color.png (1024 RGBA), f015_head_normal.png (1024),
f015_head_metallicsmooth.png (prepared size)"""
import sys, os, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

raw, prep, rbtools, dst = sys.argv[1:5]
sys.path.insert(0, rbtools)
import hairmask
from prep_textures import push_pull, LASH_BOXES_F015
os.makedirs(dst, exist_ok=True)
CX = 1024.0
LUM = np.array([0.299, 0.587, 0.114], np.float32)


def grey_hair2(rgb, m, ref_mask):
    a = rgb.astype(np.float32) / 255
    L = a @ LUM
    sel = L[ref_mask]
    lo, hi = np.percentile(sel, 3), np.percentile(sel, 97)
    t = np.clip((L - lo) / max(hi - lo, 1e-3), 0, 1)
    g = 0.30 + 0.46 * t ** 1.1                         # 0.30..0.76 (was 0.36..0.92)
    pepper = np.clip((0.28 - t) / 0.18, 0, 1)          # the darkest strands stay darker: salt-and-pepper
    g = g * (1 - 0.30 * pepper)
    grey = np.stack([g * 1.00, g * 0.965, g * 0.915], -1)  # slightly warm, never blue
    out = a * (1 - m[..., None]) + grey * m[..., None]
    return np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8)


# ---------------------------------------------------------------- hair colour (head map + cards)
src_head = os.path.join(raw, 'f015_head_color.tga')
rgb = np.asarray(Image.open(src_head).convert('RGB'))
hm = hairmask.mask_for(src_head)
# hairmask erodes the foreground by a pixel or two, which left blonde slivers at the island borders outside the skin
# polygon (one showed through the back of her hair): everything outside the polygon is hair except its fixed blocks.
_s = 2048 / 768
_pi = Image.new('L', (2048, 2048), 0); ImageDraw.Draw(_pi).polygon([(x*_s, y*_s) for x, y in hairmask.POLY], fill=255)
_out = np.asarray(_pi) < 128
_yy, _xx = np.mgrid[0:2048, 0:2048]
_rf = rgb.astype(np.float32)
_keep = ((_yy > 460*_s) & (_xx < 104*_s)) | ((_yy > 630*_s) & (_xx < 152*_s)) | ((_xx-203*_s)**2 + (_yy-714*_s)**2 < (56*_s)**2)     | ((_rf[..., 0]-_rf[..., 1])/np.maximum(_rf[..., 0], 1) > 0.5) | (rgb.max(2) <= 3)
hm = np.maximum(hm, (_out & ~_keep).astype(np.float32))
# the nape seam at the back (rows 370-420 of the 768 layout, outside the face columns): darker-than-skin pixels there
# are hair ends and blonde specks, never skin
_L = _rf @ LUM / 255
_nape = (_yy > 370*_s) & (_yy < 420*_s) & ((_xx < 200*_s) | (_xx > 568*_s)) & (_L < 0.55) & (rgb.max(2) > 3)
hm = np.maximum(hm, _nape.astype(np.float32))
# the lower hair islands (back of the head) meet the neck band along the polygon's lower edge, where hairmask's rim
# rule forced skin: warm texels on that seam next to hair (one showed as an orange line in the back hair) go grey
_pin = Image.fromarray((~_out).astype(np.uint8)*255)
_rim = (np.asarray(_pin.filter(ImageFilter.MaxFilter(33))) > 127) & ~(np.asarray(_pin.filter(ImageFilter.MinFilter(33))) > 127)
_near = np.asarray(Image.fromarray(((hm > 0.5)*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(31))) > 127
_mx = _rf.max(2); _sat = (_mx - _rf.min(2)) / np.maximum(_mx, 1)
_seam = _rim & _near & (_yy > 440*_s) & (_sat > 0.25) & (rgb.max(2) > 3)
hm = np.maximum(hm, _seam.astype(np.float32))
# hairmask keeps a 149 px circle round the eyeball block un-greyed, which cut into the hair island beside it and left
# a tan strip (it showed as an orange line in her back hair): in that circle only the red eyeball block keeps its colour
_eye = ((_xx-203*_s)**2 + (_yy-714*_s)**2 < (60*_s)**2) & ((_xx-203*_s)**2 + (_yy-714*_s)**2 > (40*_s)**2)
_red = _rf[..., 0] > 1.8*np.maximum(_rf[..., 1], 1)
hm = np.maximum(hm, (_eye & ~_red & (rgb.max(2) > 3)).astype(np.float32))
rgb = grey_hair2(rgb, hm, hm > 0.9)
rgb = push_pull(rgb, rgb.max(2) > 3)
col = Image.fromarray(rgb)
W, H = col.size
assert (W, H) == (2048, 2048), col.size

op = np.asarray(Image.open(os.path.join(raw, 'f015_opacity_color.tga')).convert('RGBA'))
orgb, al = op[..., :3], op[..., 3]
om = np.ones(al.shape, np.float32)
_Lr = orgb.astype(np.float32) @ LUM / 255
for x0, y0, x1, y1 in LASH_BOXES_F015:   # inside the lash boxes only the dark lashes keep their colour (a hair strand
    om[y0:y1, x0:x1] = (_Lr[y0:y1, x0:x1] > 0.30)  # crossing a box stayed blonde: an orange sliver in her back hair)
orgb = grey_hair2(orgb, om, (al > 128) & (om > 0))
orgb = push_pull(orgb, al > 127)
opi = Image.fromarray(np.dstack([orgb, al]), 'RGBA')
if opi.size[0] > 1024: opi = opi.resize((1024, 1024), Image.LANCZOS)
opi.save(os.path.join(dst, 'f015_opacity_color.png'), optimize=True)

# ---------------------------------------------------------------- aging
c = np.asarray(col).astype(np.float32) / 255.0


def bez(p0, p1, p2, n=40):
    return [((1-t)**2*p0[0] + 2*(1-t)*t*p1[0] + t*t*p2[0], (1-t)**2*p0[1] + 2*(1-t)*t*p1[1] + t*t*p2[1]) for t in (i/n for i in range(n+1))]


def layer(strokes, blur):
    im = Image.new('L', (W*2, H*2), 0); d = ImageDraw.Draw(im)
    for pts, w, s in strokes:
        pp = [(x*2, y*2) for x, y in pts]
        n = len(pp) - 1
        for i in range(n):
            t = (i + 0.5) / n
            taper = math.sin(math.pi * t) ** 0.8
            d.line([pp[i], pp[i+1]], fill=int(255*s*taper), width=max(1, int(round(w*2*(0.4+0.6*taper)))))
    im = im.resize((W, H), Image.LANCZOS).filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(im).astype(np.float32) / 255.0


def mirror(pts):
    return [(2*CX - x, y) for x, y in pts]


def sym(lst, pts, w, s):
    lst.append((pts, w, s)); lst.append((mirror(pts), w, s))


rng = random.Random(1557)
fine, creases = [], []
# forehead: three gently curved lines, broken in the middle, symmetric
for y, s, half in ((460, 0.42, 150), (432, 0.34, 132), (405, 0.24, 112)):
    gap = 14 + rng.uniform(0, 10)
    sym(fine, bez((CX - half, y + 6), (CX - half*0.55, y - 3), (CX - gap, y + 1)), 4.5, s)
# glabella: two short soft vertical lines
sym(fine, bez((CX - 28, 528), (CX - 31, 500), (CX - 26, 474)), 4, 0.32)
# crow's feet from the outer eye corners (left hole ~x 830, right ~x 1218 = mirror of 830)
for p in (bez((818, 566), (796, 556), (770, 548)), bez((816, 582), (792, 584), (764, 588)), bez((820, 598), (798, 610), (774, 622))):
    sym(fine, p, 3.5, 0.36)
# lower lid: one soft fold under each eye (no darkening of the area)
sym(fine, bez((846, 626), (884, 648), (930, 628)), 5, 0.26)
# nasolabial folds (soft, deep)
sym(creases, bez((964, 712), (926, 772), (910, 856)), 7, 0.55)
# marionette lines: short and curved
sym(creases, bez((938, 852), (926, 884), (930, 918)), 6, 0.30)
# upper lip: very faint
for x in (985, 1003, 1045, 1063):
    fine.append(([(x, 792), (x + rng.uniform(-2, 2), 806)], 2, 0.14))
# neck: two soft rings
for y, s in ((1090, 0.22), (1140, 0.16)):
    fine.append((bez((CX - 170, y + 8), (CX, y - 6), (CX + 170, y + 8)), 6, s))

fine_m = layer(fine, 2.6)
crease_m = layer(creases, 4.5)
ridge_src = []
for pts, w, s in creases:
    side = -1 if pts[0][0] < CX else 1
    ridge_src.append(([(x + side*11, y - 2) for x, y in pts], w*1.6, s*0.6))
ridge_m = layer(ridge_src, 7)

lum = (c @ LUM)[..., None]
mx = c.max(-1, keepdims=True); mn = c.min(-1, keepdims=True)
sat = (mx - mn) / np.maximum(mx, 1e-4)
skin = np.clip((sat - 0.10) / 0.12, 0, 1) * (1 - hm[..., None])   # grey hair -> 0, skin -> 1
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
face = np.clip(1.4 - np.sqrt(((xx-CX)/330)**2 + ((yy-680)/420)**2), 0, 1)[..., None]

# 1) skin: keep it warm. A little less saturation, a touch of yellow, slightly evener tone.
out = c*(1-0.04*skin) + lum*(0.04*skin)
out = out + skin*np.array([0.010, 0.006, -0.012], np.float32)
mean_skin = np.array([0.82, 0.64, 0.52], np.float32)
out = out*(1-0.06*skin*face) + mean_skin*(0.06*skin*face)

# 2) lips: muted rose instead of bright lipstick
red = np.clip((c[..., 0] - np.maximum(c[..., 1], c[..., 2]) - 0.22) / 0.15, 0, 1)
lipzone = np.clip(1.0 - np.sqrt(((xx-CX)/130)**2 + ((yy-828)/55)**2), 0, 1)*3
lip = np.clip(red*np.clip(lipzone, 0, 1), 0, 1)
lip = (np.asarray(Image.fromarray((lip*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))).astype(np.float32)/255)[..., None]
lipcol = (c*0.55 + lum*0.45)*np.array([1.08, 0.93, 0.88], np.float32)*0.98
out = out*(1-0.6*lip) + lipcol*(0.6*lip)

# 3) brows: lighter grey-brown, symmetric zones
browz = np.zeros((H, W), np.float32)
for bx in (CX - 140, CX + 140):
    browz = np.maximum(browz, np.clip(1.2 - np.sqrt(((xx-bx)/105)**2 + ((yy-512)/34)**2), 0, 1))
local = np.asarray(col.filter(ImageFilter.GaussianBlur(18))).astype(np.float32)/255
dark = np.clip((local.mean(-1) - c.mean(-1) - 0.02) / 0.10, 0, 1)
bw = (browz*dark)[..., None]
browgrey = np.clip(lum*1.05 + 0.08, 0, 1)*np.array([1.0, 0.95, 0.90], np.float32)
out = out*(1-0.5*bw) + browgrey*(0.5*bw)

# 4) age spots: few and faint, low on the cheeks and at the temples only (nothing under the eyes)
spots = Image.new('L', (W, H), 0); sd = ImageDraw.Draw(spots)
for (x0, y0, x1, y1), n in (((790, 720, 900, 800), 4), ((690, 470, 770, 580), 3), ((540, 1050, 790, 1190), 3)):
    for zx0, zx1 in ((x0, x1), (2*CX - x1, 2*CX - x0)):
        for _ in range(n):
            x, y = rng.uniform(zx0, zx1), rng.uniform(y0, y1); r = rng.uniform(4, 8)
            sd.ellipse([x-r, y-r*rng.uniform(0.7, 1.0), x+r, y+r*rng.uniform(0.7, 1.0)], fill=int(rng.uniform(25, 50)))
sp = (np.asarray(spots.filter(ImageFilter.GaussianBlur(4))).astype(np.float32)/255)[..., None]*skin
out = out*(1-sp) + out*np.array([0.84, 0.74, 0.64], np.float32)*sp

# 5) wrinkles in colour: soft warm-brown shading, ridge a touch lighter
fm = fine_m[..., None]*skin; cm = crease_m[..., None]*skin; rm = ridge_m[..., None]*skin
warm = np.array([0.80, 1.0, 1.12], np.float32)      # darkens blue most -> brown, never grey
out = out*(1 - 0.30*fm*warm)
out = out*(1 - 0.36*cm*warm)
out = out + 0.03*rm*np.array([1.0, 0.95, 0.85], np.float32)
out = np.clip(out, 0, 1)
Image.fromarray((out*255+0.5).astype(np.uint8)).save(os.path.join(dst, 'f015_head_color.png'))

# ---------------------------------------------------------------- normal map (1024): soft relief
nimg = Image.open(os.path.join(raw, 'f015_head_normal.tga')).convert('RGB')
if nimg.size[0] > 1024: nimg = nimg.resize((1024, 1024), Image.LANCZOS)
nw, nh = nimg.size
n = np.asarray(nimg).astype(np.float32)/255*2 - 1
height = -(0.7*fine_m + 1.4*crease_m) + 0.6*ridge_m
hs = np.asarray(Image.fromarray(np.clip(height*127 + 128, 0, 255).astype(np.uint8)).resize((nw, nh), Image.LANCZOS)).astype(np.float32)/127 - 1
hs = hs*np.asarray(Image.fromarray((skin[..., 0]*255).astype(np.uint8)).resize((nw, nh), Image.LANCZOS)).astype(np.float32)/255
gy, gx = np.gradient(hs)
k = 2.0
n[..., 0] += -k*gx
n[..., 1] += k*gy
n /= np.linalg.norm(n, axis=-1, keepdims=True)
Image.fromarray(((n*0.5+0.5)*255+0.5).clip(0, 255).astype(np.uint8)).save(os.path.join(dst, 'f015_head_normal.png'))

# ---------------------------------------------------------------- smoothness: a touch less gloss on the skin
ms = Image.open(os.path.join(prep, 'f015_head_metallicsmooth.png')).convert('RGBA')
a = np.asarray(ms).astype(np.float32)
skin_s = np.asarray(Image.fromarray((skin[..., 0]*255).astype(np.uint8)).resize(ms.size, Image.LANCZOS)).astype(np.float32)/255
a[..., 3] = a[..., 3]*(1 - 0.10*skin_s)
Image.fromarray(a.clip(0, 255).astype(np.uint8), 'RGBA').save(os.path.join(dst, 'f015_head_metallicsmooth.png'))
print('aged2 ->', dst)
