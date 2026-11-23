"""Convert Rocketbox TGA textures into Unity/URP-ready PNGs.
usage: python -I prep_textures.py <raw Avatars root> <prepared root> [Name ...]
"""
import sys, os, glob, json, numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hairmask

HEROES = {'Male_Adult_10','Business_Male_02','Business_Male_04','Female_Adult_05','Female_Adult_12','Business_Female_02','Security_Male_01'}
ELDER = 'Business_Female_02'
LASH_BOXES_F015 = [(1730, 580, 1870, 1000), (1000, 1910, 1460, 2048)]  # x0,y0,x1,y1 in the 2048 opacity map

def push_pull(rgb, mask):
    """Fill pixels where mask==0 with a smooth extrapolation of the masked colours (avoids black/odd seams in mips)."""
    rgb = rgb.astype(np.float32); w = mask.astype(np.float32)
    levels = [(rgb*w[...,None], w)]
    c, ww = levels[0]
    while c.shape[0] > 1:
        h2, w2 = c.shape[0]//2, c.shape[1]//2
        c = c[:h2*2,:w2*2].reshape(h2,2,w2,2,-1).sum((1,3)); ww = ww[:h2*2,:w2*2].reshape(h2,2,w2,2).sum((1,3))
        levels.append((c, ww))
    col = levels[-1][0]/np.maximum(levels[-1][1],1e-6)[...,None]
    for c, ww in reversed(levels[:-1]):
        H, W = c.shape[0], c.shape[1]
        up = np.dstack([np.asarray(Image.fromarray(np.ascontiguousarray(col[...,k]).astype(np.float32), 'F').resize((W, H), Image.BILINEAR)) for k in range(col.shape[2])])
        col = np.where(ww[...,None] > 0, c/np.maximum(ww,1e-6)[...,None], up)
    # Keep the extrapolated colour only in a 16 px band around the islands; flat average beyond (smaller PNGs).
    from PIL import ImageFilter
    band = Image.fromarray((mask > 0).astype(np.uint8)*255)
    for _ in range(4): band = band.filter(ImageFilter.MaxFilter(9))
    band = np.asarray(band) > 127
    flat = levels[-1][0][0,0]/max(levels[-1][1][0,0],1e-6)
    col = np.where(band[...,None], col, flat)
    out = np.where(mask[...,None] > 0, rgb, col)
    return np.clip(out, 0, 255).astype(np.uint8)

def grey_hair(rgb, m, ref_mask):
    a = rgb.astype(np.float32)/255
    L = a@np.array([0.299,0.587,0.114],np.float32)
    sel = L[ref_mask]
    lo, hi = np.percentile(sel, 3), np.percentile(sel, 97)
    t = np.clip((L-lo)/max(hi-lo,1e-3), 0, 1)**0.9
    g = 0.36 + 0.56*t
    grey = np.stack([g*1.0, g*0.98, g*0.95], -1)
    out = a*(1-m[...,None]) + grey*m[...,None]
    return np.clip(out*255+0.5, 0, 255).astype(np.uint8)

def save(im, path):
    im.save(path, optimize=True)
    return os.path.getsize(path)

def smooth_from_spec(spec):
    s = spec.astype(np.float32)/255
    return np.clip(0.20 + 0.60*np.sqrt(s), 0, 1)

def process(avdir, outroot):
    name = os.path.basename(os.path.normpath(avdir))
    hero = name in HEROES
    tex = os.path.join(avdir, 'Textures'); out = os.path.join(outroot, name, 'Textures'); os.makedirs(out, exist_ok=True)
    report = []
    for f in sorted(glob.glob(os.path.join(tex, '*.tga'))):
        base = os.path.splitext(os.path.basename(f))[0]
        im = Image.open(f); im.load()
        kind = 'normal' if base.endswith('_normal') else 'specular' if base.endswith('_specular') else 'opacity' if 'opacity' in base else 'color'
        size = im.size
        def cap(n):  # longest side <= n, aspect kept
            f = min(1.0, n/max(size)); return (max(1, round(size[0]*f)), max(1, round(size[1]*f)))
        # Size policy: heroes keep 2048 colour maps; everything else (normals, smoothness, hair/lash opacity) is 1024.
        # Crowd/spare avatars are 1024 throughout.
        if kind == 'color' and hero and 'equipment' not in base:
            tgt = size
        else:
            tgt = cap(1024)
        if kind == 'color':
            rgb = np.asarray(im.convert('RGB'))
            if name == ELDER and base.endswith('_head_color'):
                m = hairmask.mask_for(f)
                rgb = grey_hair(rgb, m, m > 0.9)
                Image.fromarray((m*255).astype(np.uint8)).resize((512,512)).save(os.path.join(outroot, name, 'elder_hairmask_512.png'))
            fgm = (rgb.max(2) > 3)
            rgb = push_pull(rgb, fgm)
            o = Image.fromarray(rgb).resize(tgt, Image.LANCZOS) if tgt != size else Image.fromarray(rgb)
            op = os.path.join(out, base + '.png'); n = save(o, op)
            report.append((base+'.png', 'RGB sRGB base colour', o.size, n))
        elif kind == 'opacity':
            rgba = np.asarray(im.convert('RGBA'))
            rgb, al = rgba[...,:3], rgba[...,3]
            if name == ELDER and base == 'f015_opacity_color':
                m = (np.ones(al.shape, np.float32))
                for x0,y0,x1,y1 in LASH_BOXES_F015: m[y0:y1, x0:x1] = 0
                rgb = grey_hair(rgb, m, (al > 128) & (m > 0))
            rgb = push_pull(rgb, al > 127)
            o = Image.fromarray(np.dstack([rgb, al]), 'RGBA')
            if tgt != size: o = o.resize(tgt, Image.LANCZOS)
            op = os.path.join(out, base + '.png'); n = save(o, op)
            report.append((base+'.png', 'RGBA sRGB colour + alpha (alpha clip ~0.5)', o.size, n))
        elif kind == 'normal':
            o = im.convert('RGB')
            if tgt != size: o = o.resize(tgt, Image.LANCZOS)
            op = os.path.join(out, base + '.png'); n = save(o, op)
            report.append((base+'.png', 'RGB linear tangent-space normal, OpenGL (+Y)', o.size, n))
        else:
            sp = np.asarray(im.convert('L').resize(tgt, Image.LANCZOS) if tgt != size else im.convert('L'))
            sm = (smooth_from_spec(sp)*255+0.5).astype(np.uint8)
            z = np.zeros_like(sm)
            o = Image.fromarray(np.dstack([z, z, z, sm]), 'RGBA')
            nb = base[:-len('_specular')] + '_metallicsmooth'
            op = os.path.join(out, nb + '.png'); n = save(o, op)
            report.append((nb+'.png', 'RGBA linear: R metallic=0, A smoothness=0.20+0.60*sqrt(spec)', o.size, n))
    total = sum(r[3] for r in report)
    json.dump({'name': name, 'hero': hero, 'files': report, 'total_bytes': total}, open(os.path.join(outroot, name, 'textures.json'), 'w'), indent=1)
    print(name, 'hero' if hero else 'crowd', f'{total/1e6:.1f} MB', flush=True)

if __name__ == '__main__':
    raw, outroot = sys.argv[1], sys.argv[2]
    names = sys.argv[3:]
    for d in sorted(glob.glob(os.path.join(raw, '*', '*'))):
        if names and os.path.basename(d) not in names: continue
        process(d, outroot)
