"""Ages a dark-haired Rocketbox woman into the 304호 elder (an older Korean woman in her 70s; with the avatar's own face
shape she reads late 60s to early 70s close up), after prep_textures.py.
python -I age_elder.py <raw Textures dir (tga)> <Textures dir (prep_textures output, updated in place)> <prefix>
Profiles: f152 (Medical_Female_01, the cast elder: hair in a bun) and f003 (Female_Adult_03, the alternative that was
rendered for comparison). Both share the Rocketbox female head UV (face centre x 1024 in the 2048 map, eye holes at
x ~882 / ~1165, y ~578), so the wrinkle strokes below fit either.
- hair (head map and hair cards): neutral, slightly warm grey-white with darker pepper strands kept on the scalp (no
  silver sheen); the bun's core and cards (f152) a narrower mid grey, the card rims toned to the card's own grey; the
  lash strips keep their dark colour; the bun's gold hair tie becomes a dark elastic
- skin stays warm: a touch less saturation, evener tone, 10 % less gloss
- wrinkles, symmetric about the face centre: forehead and frown lines, fanned crow's feet, a lower-lid fold and the
  under-eye bag, deep nasolabial and marionette folds, a soft jowl and under-chin shadow, faint upper-lip lines, three
  neck rings and two cords; drawn into the colour map and as relief in the normal map
- lips thinner and muted, brows warm grey-brown, faint age spots on the cheekbones, low on the cheeks and at the temples
writes <prefix>_head_color.jpg (2048), <prefix>_opacity_color.png (1024), <prefix>_head_normal.jpg (1024) and
<prefix>_head_metallicsmooth.png (512)."""
import sys, os, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

raw, dst, prefix = sys.argv[1:4]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hairmask
from prep_textures import push_pull, brown_iris, smooth_from_spec, fit_rgba

PROFILES = {
    # lash boxes (x0, y0, x1, y1) in the 2048 opacity map: the lash strips stay dark
    # tie: the gold hair-tie blocks at the top of the head map (x0, y0, x1, y1, as elastic): the side blocks wrap the bun
    # base and become a dark elastic, the middle one lies on the crown and becomes hair
    # bun_y: the bun's core islands in the head map lie below this row (the scalp above it)
    'f152': {'lashes': [(800, 1220, 1180, 1880)], 'tie': [(0, 0, 560, 260, True), (1490, 0, 2048, 260, True), (790, 0, 1258, 262, False)],
             'bun_y': 1230},
    'f003': {'lashes': [(0, 0, 150, 470), (1870, 1250, 2048, 1750)], 'tie': []},
}
prof = PROFILES[prefix]
CX = 1024.0
LUM = np.array([0.299, 0.587, 0.114], np.float32)


def grey_hair(rgb, m, ref_mask, base=0.34, span=0.46, pepper_k=0.42, local=0):
    """Grey-white hair from the source strands' own light/dark pattern: lightness spread over base..base+span, the
    darkest strands pulled down again (salt and pepper), slightly warm, never blue. local > 0 (the scalp in the head
    map): only the strand detail is kept (lightness against its own blur over that radius), so the source's broad
    shadow (a dark band along the nape) does not stay dark grey. Texels only partly hair (wisps over the skin) take the
    middle grey, so the hairline does not end in a bright rim."""
    a = rgb.astype(np.float32) / 255
    L = a @ LUM
    if local:
        w = (m > 0.9).astype(np.float32)
        blur = lambda x: hairmask.blur(hairmask.blur(x.astype(np.float32), local // 2), local // 2)   # two box passes
        Lb = blur(L * w) / np.maximum(blur(w), 1e-3)
        det = L - Lb
        sd = float(np.std(det[ref_mask]))
        t = np.clip(0.5 + det / (3.2 * max(sd, 1e-3)), 0, 1)
    else:
        sel = L[ref_mask]
        lo, hi = np.percentile(sel, 3), np.percentile(sel, 97)
        t = np.clip((L - lo) / max(hi - lo, 1e-3), 0, 1)
    full = np.clip((m - 0.8) / 0.15, 0, 1)
    t = t * full + 0.45 * (1 - full)
    g = base + span * t ** 0.9
    pepper = np.clip((0.26 - t) / 0.18, 0, 1)
    g = g * (1 - pepper_k * pepper)
    grey = np.stack([g * 1.00, g * 0.965, g * 0.915], -1)
    out = a * (1 - m[..., None]) + grey * m[..., None]
    return np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8)


# ---------------------------------------------------------------- hair colour (head map + cards)
src_head = os.path.join(raw, prefix + '_head_color.tga')
rgb = np.asarray(Image.open(src_head).convert('RGB')).copy()
H = W = rgb.shape[0]
assert W == 2048, rgb.shape
hm = hairmask.mask_dark(src_head)
for x0, y0, x1, y1, as_elastic in prof['tie']:
    # the gold hair tie: a dark grey-brown elastic (its shading kept) or hair
    f = rgb[y0:y1, x0:x1].astype(np.float32) / 255
    mx = f.max(2); sat = (mx - f.min(2)) / np.maximum(mx, 1e-4)
    gold = ((f[..., 0] > f[..., 2] * 1.6) & (f[..., 1] > f[..., 2] * 1.25) & (sat > 0.45) & (mx > 0.35)).astype(np.float32)
    gold = np.asarray(Image.fromarray((gold * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(1.5))).astype(np.float32)[..., None] / 255
    Lg = (f @ LUM)[..., None]
    elastic = np.clip(0.16 + 0.30 * Lg, 0, 1) * np.array([1.0, 0.95, 0.90], np.float32)
    if as_elastic:
        rgb[y0:y1, x0:x1] = np.clip((f * (1 - gold) + elastic * gold) * 255 + 0.5, 0, 255).astype(np.uint8)
        hm[y0:y1, x0:x1] *= (1 - gold[..., 0])
    else:
        hm[y0:y1, x0:x1] = np.maximum(hm[y0:y1, x0:x1], gold[..., 0])
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
bun = (yy > prof['bun_y']).astype(np.float32) if prof.get('bun_y') else np.zeros((H, W), np.float32)
if prof.get('bun_y'):
    # in the bun's core islands every dark texel is hair: mask_dark leaves out the near-black and the reddish-black
    # ones (its background and lips/gums rules), and those stayed as black blotches. The mouth-interior, teeth and
    # eyeball blocks there are kept out; the chest skin above the islands is light and stays skin.
    s768 = W / 768
    a0 = rgb.astype(np.float32) / 255
    blocks = ((yy > 460*s768) & (xx < 104*s768)) | ((yy > 630*s768) & (xx < 152*s768)) | ((xx-203*s768)**2 + (yy-714*s768)**2 < (64*s768)**2)
    core_dark = (bun > 0) & ((a0 @ LUM) < 0.30) & ~blocks
    hm = np.where(core_dark, 1.0, hm).astype(np.float32)
scalp = grey_hair(rgb, hm, hm > 0.9, base=0.38, span=0.36, pepper_k=0.30, local=24)   # lighter, so the gaps between the
                                                                                     # cards do not read as dark stripes
# the bun's core (the lower hair islands, seen between the bun's cards): the same mid grey as the cards' and low contrast,
# no dark pepper blotches, so the gaps between the cards do not read as stripes
if prof.get('bun_y'):
    core = grey_hair(rgb, hm, (hm > 0.9) & (bun > 0), base=0.43, span=0.20, pepper_k=0.08, local=24)
    rgb = np.where(bun[..., None] > 0, core, scalp)
else:
    rgb = scalp
for x0, y0, x1, y1, as_elastic in prof['tie']:
    if as_elastic: continue
    # the crown block's streaked padding and the dark V between the two hair islands under it sit on the parting's UV
    # seam (a dark dot on top of the forehead): every dark, unsaturated texel there becomes a plain mid grey
    f = rgb[y0:y1, x0:x1].astype(np.float32) / 255
    mx = f.max(2); sat = (mx - f.min(2)) / np.maximum(mx, 1e-4)
    dark = ((f @ LUM) < 0.42) & (sat < 0.30)
    rgb[y0:y1, x0:x1][dark] = np.array([148, 143, 135], np.uint8)


op = np.asarray(Image.open(os.path.join(raw, prefix + '_opacity_color.tga')).convert('RGBA'))
orgb, al = op[..., :3], op[..., 3]
om = np.ones(al.shape, np.float32)
for x0, y0, x1, y1 in prof['lashes']: om[y0:y1, x0:x1] = 0
if prof.get('bun_y'):
    # bun cards: a narrow, mid grey range (few dark pepper strands, no near-white tips), like the bun's core under them
    orgb = grey_hair(orgb, om, (al > 128) & (om > 0), base=0.42, span=0.22, pepper_k=0.10)
    # the cards' borders: the straight root edges hold smeared padding streaks (dark ones rendered as dark lines between
    # the cards) and the curly fringe holds the light tips (a pale halo round the bun). Within ~10 px of the alpha edge
    # the colour goes most of the way to the card's local mean grey, a touch darker at the very rim.
    solid = (al > 127).astype(np.float32)
    blur2 = lambda x: hairmask.blur(hairmask.blur(x, 12), 12)
    lm = np.dstack([blur2(orgb[..., k].astype(np.float32) * solid) for k in range(3)])
    lw = blur2(solid)
    lmean = lm / np.maximum(lw, 1e-3)[..., None]
    inner = np.asarray(Image.fromarray((solid * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(21))).astype(np.float32) / 255
    rim = np.clip(1 - hairmask.blur(inner, 4), 0, 1) * solid
    rim = rim * (om > 0)
    fade = 0.75 * rim
    orgb = np.clip(orgb.astype(np.float32) * (1 - fade[..., None]) + lmean * 0.94 * fade[..., None] + 0.5, 0, 255).astype(np.uint8)
else:
    orgb = grey_hair(orgb, om, (al > 128) & (om > 0), base=0.36, span=0.36)   # the curls' light tips stay off-white
orgb = push_pull(orgb, al > 127)
opi = Image.fromarray(np.dstack([orgb, al]), 'RGBA')
opi = fit_rgba(opi, 1024)
opi.save(os.path.join(dst, prefix + '_opacity_color.png'), optimize=True)

# ---------------------------------------------------------------- aging
c = rgb.astype(np.float32) / 255.0


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
fine, creases, shades = [], [], []
# forehead: three gently curved lines, broken in the middle
for y, s, half in ((462, 0.80, 160), (434, 0.70, 142), (406, 0.52, 120)):
    gap = 14 + rng.uniform(0, 10)
    sym(fine, bez((CX - half, y + 6), (CX - half*0.55, y - 3), (CX - gap, y + 1)), 5, s)
# glabella (frown lines): two short soft vertical lines
sym(fine, bez((CX - 28, 530), (CX - 31, 500), (CX - 26, 470)), 4.5, 0.66)
# crow's feet from the outer eye corners: four lines fanning out
for p in (bez((818, 562), (794, 550), (764, 540)), bez((816, 576), (790, 574), (758, 572)),
          bez((818, 590), (792, 598), (762, 606)), bez((824, 604), (802, 620), (778, 638))):
    sym(fine, p, 4, 0.76)
# lower lid: a fold under each eye and, below it, the crease that closes the under-eye bag (with a soft shadow)
sym(fine, bez((846, 626), (884, 650), (932, 628)), 5, 0.55)
sym(creases, bez((838, 650), (880, 690), (940, 662)), 6, 0.52)
sym(shades, bez((846, 662), (884, 688), (934, 664)), 16, 0.30)
# nasolabial folds (soft, deep)
sym(creases, bez((964, 712), (924, 774), (906, 862)), 9, 1.0)
# marionette lines: from the mouth corners down toward the jaw
sym(creases, bez((938, 852), (922, 896), (924, 950)), 7, 0.80)
# jowls and the jaw's underside: a soft sagging shadow at the jaw corners and under the chin
sym(shades, bez((900, 900), (860, 950), (800, 960)), 26, 0.42)
shades.append((bez((CX - 190, 968), (CX, 1004), (CX + 190, 968)), 40, 0.42))
# upper lip: faint vertical lines
for x in (980, 996, 1012, 1036, 1052, 1068):
    fine.append(([(x, 790), (x + rng.uniform(-3, 3), 808)], 2, 0.26))
# neck: three soft rings and two vertical cords
for y, s in ((1085, 0.44), (1135, 0.36), (1188, 0.26)):
    fine.append((bez((CX - 180, y + 8), (CX, y - 6), (CX + 180, y + 8)), 6.5, s))
sym(shades, bez((CX - 70, 1030), (CX - 78, 1110), (CX - 70, 1200)), 14, 0.22)

fine_m = layer(fine, 2.6)
crease_m = layer(creases, 6)
shade_m = layer(shades, 12)
ridge_src = []
for pts, w, s in creases:
    side = -1 if pts[0][0] < CX else 1
    ridge_src.append(([(x + side*11, y - 2) for x, y in pts], w*1.6, s*0.6))
ridge_m = layer(ridge_src, 7)

lum = (c @ LUM)[..., None]
mx = c.max(-1, keepdims=True); mn = c.min(-1, keepdims=True)
sat = (mx - mn) / np.maximum(mx, 1e-4)
skin = np.clip((sat - 0.10) / 0.12, 0, 1) * np.clip((lum - 0.30) / 0.10, 0, 1) * (1 - hm[..., None])
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
face = np.clip(1.4 - np.sqrt(((xx-CX)/330)**2 + ((yy-680)/420)**2), 0, 1)[..., None]
core = (skin[..., 0] > 0.9) & (face[..., 0] > 0.6)
mean_skin = np.median(c[core], axis=0).astype(np.float32)

# 1) skin: keep it warm. A little less saturation, slightly evener tone.
out = c*(1-0.06*skin) + lum*(0.06*skin)
out = out*(1-0.06*skin*face) + mean_skin*(0.06*skin*face)

# 2) lips: muted rose, a little paler than the skin around them would make them
rg = c[..., 0] - c[..., 1]
rg_skin = float(np.median(rg[core]))
lipzone = np.clip(1.0 - np.sqrt(((xx-CX)/125)**2 + ((yy-832)/48)**2), 0, 1)*3
lip = np.clip((rg - rg_skin - 0.01) / 0.06, 0, 1) * np.clip(lipzone, 0, 1)
lip = (np.asarray(Image.fromarray((lip*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))).astype(np.float32)/255)[..., None]
lipcol = (c*0.45 + lum*0.55)*np.array([1.07, 0.95, 0.90], np.float32)*0.98
out = out*(1-0.72*lip) + lipcol*(0.72*lip)
# thinner lips: the outer rim of the lip shape (top of the upper lip, bottom of the lower lip) goes back toward the skin
lip_in = np.asarray(Image.fromarray((lip[..., 0]*255).astype(np.uint8)).filter(ImageFilter.MinFilter(9)).filter(ImageFilter.GaussianBlur(2))).astype(np.float32)/255
lip_rim = np.clip(lip[..., 0] - lip_in, 0, 1)[..., None]
out = out*(1-0.55*lip_rim) + mean_skin*0.97*(0.55*lip_rim)

# 3) brows: lighter, warm grey-brown (about 110/100/92) with low contrast; a neutral grey reads blue next to the skin
browz = np.zeros((H, W), np.float32)
for bx in (CX - 140, CX + 140):
    browz = np.maximum(browz, np.clip(1.2 - np.sqrt(((xx-bx)/110)**2 + ((yy-506)/34)**2), 0, 1))
local = np.asarray(Image.fromarray(rgb).filter(ImageFilter.GaussianBlur(18))).astype(np.float32)/255
dark = np.clip((local.mean(-1) - c.mean(-1) - 0.02) / 0.10, 0, 1)
bw = (browz*dark)[..., None]
brow_l = float(np.median(lum[..., 0][(browz*dark) > 0.5])) if ((browz*dark) > 0.5).any() else 0.15
browcol = np.array([110, 100, 92], np.float32)/255 * np.clip(1 + 0.6*(lum - brow_l), 0.8, 1.2)
out = out*(1-0.78*bw) + browcol*(0.78*bw)

# 4) age spots: faint, on the cheekbones, low on the cheeks and at the temples (nothing right under the eyes)
spots = Image.new('L', (W, H), 0); sd = ImageDraw.Draw(spots)
for (x0, y0, x1, y1), n in (((790, 720, 900, 800), 5), ((730, 640, 830, 720), 4), ((690, 470, 770, 580), 3), ((540, 1050, 790, 1190), 3)):
    for zx0, zx1 in ((x0, x1), (2*CX - x1, 2*CX - x0)):
        for _ in range(n):
            x, y = rng.uniform(zx0, zx1), rng.uniform(y0, y1); r = rng.uniform(4, 8)
            sd.ellipse([x-r, y-r*rng.uniform(0.7, 1.0), x+r, y+r*rng.uniform(0.7, 1.0)], fill=int(rng.uniform(30, 60)))
sp = (np.asarray(spots.filter(ImageFilter.GaussianBlur(4))).astype(np.float32)/255)[..., None]*skin
out = out*(1-sp) + out*np.array([0.84, 0.74, 0.64], np.float32)*sp

# 5) wrinkles in colour: soft warm-brown shading, ridge a touch lighter
fm = fine_m[..., None]*skin; cm = crease_m[..., None]*skin; rm = ridge_m[..., None]*skin; shm = shade_m[..., None]*skin
warm = np.array([0.80, 1.0, 1.12], np.float32)      # darkens blue most -> brown, never grey
out = out*(1 - 0.30*shm*warm)
out = out*(1 - 0.40*fm*warm)
out = out*(1 - 0.48*cm*warm)
out = out + 0.04*rm*np.array([1.0, 0.95, 0.85], np.float32)
out = np.clip(out*255 + 0.5, 0, 255).astype(np.uint8)
out = brown_iris(out)
out = push_pull(out, np.asarray(Image.open(src_head).convert('RGB')).max(2) > 3)
Image.fromarray(out).save(os.path.join(dst, prefix + '_head_color.jpg'), 'JPEG', quality=90, optimize=True)

# ---------------------------------------------------------------- normal map (1024): soft relief
nimg = Image.open(os.path.join(raw, prefix + '_head_normal.tga')).convert('RGB')
if nimg.size[0] > 1024: nimg = nimg.resize((1024, 1024), Image.LANCZOS)
nw, nh = nimg.size
n = np.asarray(nimg).astype(np.float32)/255*2 - 1
height = -(0.8*fine_m + 1.6*crease_m + 0.5*shade_m) + 0.7*ridge_m
hs = np.asarray(Image.fromarray(np.clip(height*127 + 128, 0, 255).astype(np.uint8)).resize((nw, nh), Image.LANCZOS)).astype(np.float32)/127 - 1
hs = hs*np.asarray(Image.fromarray((skin[..., 0]*255).astype(np.uint8)).resize((nw, nh), Image.LANCZOS)).astype(np.float32)/255
gy, gx = np.gradient(hs)
k = 2.0
n[..., 0] += -k*gx
n[..., 1] += k*gy
n /= np.linalg.norm(n, axis=-1, keepdims=True)
Image.fromarray(((n*0.5+0.5)*255+0.5).clip(0, 255).astype(np.uint8)).save(os.path.join(dst, prefix + '_head_normal.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)

# ---------------------------------------------------------------- smoothness: a touch less gloss on the skin
sp_img = Image.open(os.path.join(raw, prefix + '_head_specular.tga')).convert('L').resize((512, 512), Image.LANCZOS)
sm = smooth_from_spec(np.asarray(sp_img))      # from the raw map again, so a second run does not compound
skin_s = np.asarray(Image.fromarray((skin[..., 0]*255).astype(np.uint8)).resize((512, 512), Image.LANCZOS)).astype(np.float32)/255
sm = (sm*(1 - 0.10*skin_s)*255 + 0.5).astype(np.uint8); z = np.zeros_like(sm)
Image.fromarray(np.dstack([z, z, z, sm]), 'RGBA').save(os.path.join(dst, prefix + '_head_metallicsmooth.png'), optimize=True)
print('aged', prefix, '->', dst)
