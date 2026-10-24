from PIL import Image, ImageDraw
import math, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
ASSETS = os.path.join(ROOT, 'src', 'HaloVolt', 'Assets')
os.makedirs(ASSETS, exist_ok=True)

S = 4  # supersample

def arc(draw, box, width, start, end, fill):
    draw.arc(box, start=start, end=end, fill=fill, width=width)

def make_logo(size=512, transparent=False):
    size = 512
    n = size * S
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if not transparent:
        # squircle-ish rounded square background
        pad = int(n * 0.035)
        radius = int(n * 0.24)
        d.rounded_rectangle([pad, pad, n - pad, n - pad], radius=radius, fill=(10, 20, 36, 255))
        # soft inner glow band
        for i in range(18):
            alpha = int(26 * (1 - i / 18))
            r = radius - int(n * 0.012) - i * 2
            d.rounded_rectangle([pad + i * 2, pad + i * 2, n - pad - i * 2, n - pad - i * 2],
                                radius=max(8, r), outline=(76, 194, 255, alpha), width=2)
    # rings
    margin = int(n * 0.19)
    outer_box = [margin, margin, n - margin, n - margin]
    arc(d, outer_box, int(n * 0.062), -90, 190, (76, 194, 255, 255))
    # inner ring
    margin2 = int(n * 0.315)
    inner_box = [margin2, margin2, n - margin2, n - margin2]
    arc(d, inner_box, int(n * 0.058), -90, 130, (255, 184, 77, 255))
    # center node
    cr = int(n * 0.038)
    c = n // 2
    d.ellipse([c - cr, c - cr, c + cr, c + cr], fill=(235, 247, 255, 255))
    d.ellipse([c - cr // 2, c - cr // 2, c + cr // 2, c + cr // 2], fill=(10, 20, 36, 255))
    return img.resize((size, size), Image.Resampling.LANCZOS)

logo = make_logo(512)
logo.save(os.path.join(ASSETS, 'logo.png'))
mark = make_logo(256, transparent=True)
mark.save(os.path.join(ASSETS, 'mark.png'))

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = [make_logo(s) for s in sizes]
frames[-1].save(os.path.join(ASSETS, 'app.ico'), format='ICO', sizes=[(s, s) for s in sizes], append_images=frames[:-1])
print('wrote', ASSETS)
