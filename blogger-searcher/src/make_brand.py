"""Generate the app's vector-style B + magnifier logo (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent
assets = root / 'assets'
assets.mkdir(exist_ok=True)
scale = 4
im = Image.new('RGBA', (256 * scale, 256 * scale))
d = ImageDraw.Draw(im)
black, orange = '#1e1e20', '#ff681a'
def box(coords):
    return tuple(round(v * scale) for v in coords)
def rounded(coords, radius, fill):
    d.rounded_rectangle(box(coords), radius=radius * scale, fill=fill)
rounded((0, 0, 256, 256), 52, black)
rounded((73, 94, 180, 157), 28, orange)
rounded((73, 148, 186, 216), 32, orange)
d.rectangle(box((71, 94, 99, 216)), fill=orange)
rounded((99, 112, 151, 140), 10, black)
rounded((99, 168, 156, 197), 11, black)
d.ellipse(box((141, 30, 195, 84)), outline=orange, width=11 * scale)
d.line(box((188, 78, 213, 103)), fill=orange, width=11 * scale)
d.ellipse(box((207.5, 97.5, 218.5, 108.5)), fill=orange)
im = im.resize((256, 256), Image.Resampling.LANCZOS)
im.save(assets / 'app.png')
im.save(assets / 'app.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
