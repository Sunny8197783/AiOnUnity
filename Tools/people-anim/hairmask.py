"""Hair mask for Business_Female_02 (f015_head_color). Hand-traced skin polygon (face+ears+neck+chest, in 768-px
thumbnail coords) is forced skin away from its edge; near the hairline a colour/texture classifier decides; outside it
every foreground pixel is hair except the mouth-interior, teeth and eyeball blocks and red lips."""
import sys, numpy as np
from PIL import Image, ImageFilter, ImageDraw
POLY=[(0,385),(140,388),(155,340),(165,310),(160,270),(168,238),(190,232),(238,215),(252,160),(282,120),(330,104),(450,104),(478,120),(510,165),(546,238),(566,262),(598,265),(602,285),(610,330),(625,380),(768,385),(768,472),(640,482),(560,512),(470,542),(380,558),(300,548),(200,522),(100,500),(0,474)]
def blur(x, r):
    k = 2*r+1; p = np.pad(x, r, mode='edge')
    c = np.cumsum(np.cumsum(np.pad(p, ((1,0),(1,0))), 0), 1)
    return (c[k:,k:]-c[:-k,k:]-c[k:,:-k]+c[:-k,:-k])/(k*k)
def mask_for(src, R=768):
    im = Image.open(src).convert('RGB'); W0 = im.size[0]
    a = np.asarray(im.resize((R, R), Image.LANCZOS)).astype(np.float32)/255
    L = a@np.array([0.299,0.587,0.114],np.float32)
    m1 = blur(L,2); m2 = blur(L*L,2); std = np.sqrt(np.clip(m2-m1*m1,0,1))
    r,g,b = a[...,0],a[...,1],a[...,2]
    mx = a.max(2); mn = a.min(2); s = (mx-mn)/np.maximum(mx,1e-4)
    hp = (g-b)/np.maximum(r-b,1e-4)
    skin_c = (std<0.04)&(L>0.42)&(s>0.2)&(s<0.7)&(hp<0.62)
    skin_c = np.asarray(Image.fromarray((skin_c*255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5)))>127
    pi = Image.new('L',(R,R),0); ImageDraw.Draw(pi).polygon(POLY, fill=255)
    poly = np.asarray(pi)>127
    inner = np.asarray(pi.filter(ImageFilter.MinFilter(21)))>127
    fg = np.asarray(Image.fromarray(((mx>0.04)*255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)))>127
    hair = fg & (~poly | (poly & ~inner & ~skin_c))
    yy,xx = np.mgrid[0:R,0:R]
    hair[(yy>460)&(xx<104)] = False                        # mouth interior / tongue
    hair[(yy>630)&(xx<152)] = False                        # teeth piece
    hair[(xx-203)**2+(yy-714)**2 < 56**2] = False          # eyeball
    hair[(r-g)/np.maximum(r,1e-4) > 0.5] = False            # lips / gums
    rim = np.asarray(pi.filter(ImageFilter.MaxFilter(9)))>127
    hair[rim & (yy>440)] = False                           # blurred chest-band edge below the polygon
    hi = Image.fromarray((hair*255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))
    hi = hi.resize((W0, W0), Image.BILINEAR).filter(ImageFilter.GaussianBlur(W0/768*1.2))
    return np.asarray(hi).astype(np.float32)/255
if __name__ == '__main__':
    m = mask_for(sys.argv[1]); Image.fromarray((m*255).astype(np.uint8)).save(sys.argv[2])
