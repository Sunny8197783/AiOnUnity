"""Convert Rocketbox TGA textures into the committed Unity/URP maps (Art/People/Rocketbox/<Avatar>/Textures/).
usage: python -I prep_textures.py <raw Avatars root> <out root> [Name ...]
writes <out root>/<Name>/Textures/:
- <prefix>_<slot>_color.jpg      RGB sRGB, q90: 2048 for the heroes' heads (HEAD_2K) and the bodies of the two people seen
                                  closest (BODY_2K), 1024 otherwise; every iris recoloured dark brown (Korean cast)
- <prefix>_<slot>_normal.jpg     RGB linear tangent-space normal, OpenGL (+Y), q95 4:4:4, 1024
- <prefix>_<slot>_metallicsmooth.png  RGBA linear: R metallic 0, A smoothness 0.20+0.60*sqrt(spec), 512
- <prefix>_opacity_color.png (and glasses)  RGBA sRGB hair/lash cards, alpha clip 0.5, 1024
The elder's head, hair and body maps are then replaced by age_elder.py and dress_elder.py (see README)."""
import sys, os, glob
import numpy as np
from PIL import Image, ImageFilter

HEAD_2K = {'Male_Adult_10', 'Business_Male_02', 'Male_Adult_09', 'Female_Adult_12', 'Business_Female_03',
           'Medical_Female_01', 'Security_Male_01'}      # heroes seen up close (the patient lies with his eyes shut: 1024)
BODY_2K = {'Male_Adult_10', 'Female_Adult_12'}           # jeongmin, seoyeon
LUM = np.array([0.299, 0.587, 0.114], np.float32)
IRIS_BROWN = np.array([0.33, 0.20, 0.12], np.float32)   # dark brown at mid iris luminance


def push_pull(rgb, mask):
    """Fill pixels where mask==0 with a smooth extrapolation of the masked colours (avoids black/odd seams in mips)."""
    rgb = rgb.astype(np.float32); w = mask.astype(np.float32)
    levels = [(rgb*w[..., None], w)]
    c, ww = levels[0]
    while c.shape[0] > 1:
        h2, w2 = c.shape[0]//2, c.shape[1]//2
        c = c[:h2*2, :w2*2].reshape(h2, 2, w2, 2, -1).sum((1, 3)); ww = ww[:h2*2, :w2*2].reshape(h2, 2, w2, 2).sum((1, 3))
        levels.append((c, ww))
    col = levels[-1][0]/np.maximum(levels[-1][1], 1e-6)[..., None]
    for c, ww in reversed(levels[:-1]):
        H, W = c.shape[0], c.shape[1]
        up = np.dstack([np.asarray(Image.fromarray(np.ascontiguousarray(col[..., k]).astype(np.float32), 'F').resize((W, H), Image.BILINEAR)) for k in range(col.shape[2])])
        col = np.where(ww[..., None] > 0, c/np.maximum(ww, 1e-6)[..., None], up)
    # Keep the extrapolated colour only in a 16 px band around the islands; flat average beyond (smaller files).
    band = Image.fromarray((mask > 0).astype(np.uint8)*255)
    for _ in range(4): band = band.filter(ImageFilter.MaxFilter(9))
    band = np.asarray(band) > 127
    flat = levels[-1][0][0, 0]/max(levels[-1][1][0, 0], 1e-6)
    col = np.where(band[..., None], col, flat)
    out = np.where(mask[..., None] > 0, rgb, col)
    return np.clip(out, 0, 255).astype(np.uint8)


def brown_iris(rgb):
    """rgb uint8 head colour map (Rocketbox head UV: eyeball near u 0.264, v 0.93). Returns a copy with the iris dark brown."""
    a = rgb.astype(np.float32) / 255; W = a.shape[1]
    L = a @ LUM
    x0, y0, x1, y1 = int(.235*W), int(.905*W), int(.295*W), int(.965*W)
    ys, xs = np.where(L[y0:y1, x0:x1] < 0.10)
    cx, cy = xs.mean() + x0, ys.mean() + y0
    r0 = int(0.022*W)
    sx0, sy0 = int(cx) - r0, int(cy) - r0
    sub = a[sy0:sy0+2*r0, sx0:sx0+2*r0]; Ls = L[sy0:sy0+2*r0, sx0:sx0+2*r0]
    yy, xx = np.mgrid[0:2*r0, 0:2*r0].astype(np.float32)
    r = np.hypot(xx + sx0 - cx, yy + sy0 - cy) / W
    edge = np.clip((0.0178 - r) / 0.0018, 0, 1) * np.clip((0.60 - Ls) / 0.10, 0, 1)  # iris disc, soft limbus, sclera untouched
    keep = np.clip((Ls - 0.62) / 0.12, 0, 1)                     # baked highlight stays
    pupil = np.clip((0.09 - Ls) / 0.05, 0, 1)
    w = (edge * (1 - keep) * (1 - pupil))[..., None]
    Lc = 0.16 + 0.55 * (Ls - 0.15)                               # compress: blue/grey irises are bright
    new = IRIS_BROWN * np.clip(Lc / 0.26, 0.25, 1.6)[..., None]
    sub[:] = sub * (1 - w) + new * w
    return np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8)


def jag_root(rgba, box, depth, seed=16):
    """Cuts the straight top (root) edge of one hair-card island into strand-like tips (Business_Female_03's nape card)."""
    a = np.array(rgba); x0, y0, x1, y1 = box
    rng = np.random.default_rng(seed)
    u = rng.random(x1 - x0) ** 1.6
    u = np.convolve(u, np.ones(3) / 3, mode="same")
    for i, x in enumerate(range(x0, x1)):
        ys = np.where(a[y0:y1, x, 3] > 127)[0]
        if len(ys) == 0: continue
        a[y0:y0 + ys[0] + 5 + int(depth * u[i]), x, 3] = 0
    return a


def calm_skin(rgb):
    """Less orange (jeongmin): skin-hued pixels lose 10 % saturation and a touch of red."""
    a = rgb.astype(np.float32) / 255
    mx = a.max(-1); mn = a.min(-1); sat = (mx - mn) / np.maximum(mx, 1e-4)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    hue_ok = (r >= g) & (g >= b) & ((g - b) / np.maximum(r - b, 1e-4) < 0.75)
    m = (hue_ok & (mx > 0.25) & (sat > 0.18)).astype(np.float32)
    m = np.asarray(Image.fromarray((m*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))).astype(np.float32)[..., None] / 255
    L = (a @ LUM)[..., None]
    calm = (a * 0.90 + L * 0.10) * np.array([0.99, 1.0, 1.01], np.float32)
    return np.clip((a * (1 - m) + calm * m) * 255 + 0.5, 0, 255).astype(np.uint8)


def no_tattoo(rgb, box=(515, 730, 675, 915)):
    """Paints out Female_Adult_03's upper-arm tattoo (a crowd woman): inside the box, texels that differ from the
    local skin are filled from the surrounding skin (push-pull), twice, with a soft edge."""
    x0, y0, x1, y1 = box
    a = np.array(rgb)
    pad = 30
    cur = a[y0-pad:y1+pad, x0-pad:x1+pad].astype(np.float32)
    inner = np.zeros(cur.shape[:2], bool); inner[pad:-pad, pad:-pad] = True
    est = np.asarray(Image.fromarray(cur.astype(np.uint8)).filter(ImageFilter.MedianFilter(41))).astype(np.float32)
    for _ in range(2):
        ink = (np.abs(cur - est).max(2) > 12) & inner
        ink = np.asarray(Image.fromarray((ink * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))) > 127
        est = push_pull(cur.astype(np.uint8), ~ink).astype(np.float32)
        est = np.asarray(Image.fromarray(est.astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))).astype(np.float32)
    m = (np.asarray(Image.fromarray((ink * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5))).astype(np.float32) / 255)[..., None]
    a[y0-pad:y1+pad, x0-pad:x1+pad] = np.clip(cur * (1 - m) + est * m + 0.5, 0, 255).astype(np.uint8)
    return a


def mute_top(rgb, target=(0.24, 0.25, 0.31)):
    """Female_Adult_03's saturated purple top (a crowd woman): every purple texel (straps and trims too) becomes a dusty
    navy with its own folds kept (lightness relative to the purple's median); skin and the dark trousers are untouched."""
    a = rgb.astype(np.float32) / 255
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1); mn = a.min(-1); sat = (mx - mn) / np.maximum(mx, 1e-4)
    m = ((b > g * 1.15) & (r > g * 1.05) & (sat > 0.20)).astype(np.float32)
    L = a @ LUM
    ref = float(np.median(L[m > 0]))
    m = (np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1))).astype(np.float32) / 255)[..., None]
    new = np.array(target, np.float32) * np.clip(L / ref, 0.3, 1.7)[..., None]
    return np.clip((a * (1 - m) + new * m) * 255 + 0.5, 0, 255).astype(np.uint8)


def smooth_from_spec(spec):
    s = spec.astype(np.float32)/255
    return np.clip(0.20 + 0.60*np.sqrt(s), 0, 1)


def fit(im, n):
    return im.resize((n, n), Image.LANCZOS) if im.size[0] > n else im


def fit_rgba(im, n):
    """fit() for the hair/lash cards: colour and alpha are resized separately. Pillow resizes RGBA premultiplied, which
    leaves black under every transparent texel, and that black bleeds into the card edges through bilinear filtering
    and the mipmaps (dark rims; invisible on dark hair, dark lines between the elder's grey bun cards)."""
    if im.size[0] <= n: return im
    rgb = im.convert('RGB').resize((n, n), Image.LANCZOS)
    rgb.putalpha(im.getchannel('A').resize((n, n), Image.LANCZOS))
    return rgb


def process(avdir, outroot):
    name = os.path.basename(os.path.normpath(avdir))
    tex = os.path.join(avdir, 'Textures'); out = os.path.join(outroot, name, 'Textures'); os.makedirs(out, exist_ok=True)
    total = 0
    for f in sorted(glob.glob(os.path.join(tex, '*.tga'))):
        base = os.path.splitext(os.path.basename(f))[0]
        im = Image.open(f); im.load()
        if base.endswith('_normal'):
            dst = os.path.join(out, base + '.jpg')
            fit(im.convert('RGB'), 1024).save(dst, 'JPEG', quality=95, subsampling=0, optimize=True)
        elif base.endswith('_specular'):
            sp = np.asarray(fit(im.convert('L'), 512))
            sm = (smooth_from_spec(sp)*255+0.5).astype(np.uint8); z = np.zeros_like(sm)
            dst = os.path.join(out, base[:-len('_specular')] + '_metallicsmooth.png')
            Image.fromarray(np.dstack([z, z, z, sm]), 'RGBA').save(dst, optimize=True)
        elif 'opacity' in base:
            rgba = np.asarray(im.convert('RGBA'))
            rgb, al = rgba[..., :3], rgba[..., 3]
            rgba = np.dstack([push_pull(rgb, al > 127), al])
            if name == 'Business_Female_03' and base == 'f016_opacity_color': rgba = jag_root(rgba, (84, 585, 893, 700), 90)
            dst = os.path.join(out, base + '.png')
            fit_rgba(Image.fromarray(rgba, 'RGBA'), 1024 if '_glasses_' not in base else 512).save(dst, optimize=True)
        else:
            rgb = np.asarray(im.convert('RGB'))
            if '_head_' in base: rgb = brown_iris(rgb)
            if name == 'Male_Adult_10' and ('_head_' in base or '_body_' in base): rgb = calm_skin(rgb)
            if name == 'Female_Adult_03' and '_body_' in base: rgb = mute_top(no_tattoo(rgb))
            rgb = push_pull(rgb, rgb.max(2) > 3)
            n = 2048 if (('_head_' in base and name in HEAD_2K) or ('_body_' in base and name in BODY_2K)) else 1024
            dst = os.path.join(out, base + '.jpg')
            fit(Image.fromarray(rgb), n).save(dst, 'JPEG', quality=90, optimize=True)
        total += os.path.getsize(dst)
    print(name, f'{total/1e6:.2f} MB', flush=True)


if __name__ == '__main__':
    raw, outroot = sys.argv[1], sys.argv[2]
    names = sys.argv[3:]
    for d in sorted(glob.glob(os.path.join(raw, '*', '*'))):
        if names and os.path.basename(d) not in names: continue
        process(d, outroot)
