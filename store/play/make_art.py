"""Builds the Play Store icon (512x512) and feature graphic (1024x500) from the app's own artwork.

    python store/play/make_art.py <repo root> <output dir> <tagline> <line 1|line 2|line 3> <feature graphic file name>

For instance: make_art.py . out "Offrez la zik" "Le son de votre téléphone,|partagé avec ceux d'à côté." feature-fr.png
Needs Pillow. The icon is written as icon-512.png next to the feature graphic.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

repo, out = Path(sys.argv[1]), Path(sys.argv[2])
resources = repo / "src/AuraMusic.Mobile/Resources"
BACKGROUND = (0, 11, 6)        # the launcher icon's own black-green
GREEN = (0, 255, 102)          # MatrixGreen
MINT = (210, 255, 226)         # MatrixMint

out.mkdir(parents=True, exist_ok=True)

# Icon: the launcher foreground on its background. The store masks the corners itself, so the artwork may fill
# more of the square than on a launcher (0.62 there, for round masks).
foreground = Image.open(resources / "AppIcon/appiconfg.png").convert("RGBA")
icon = Image.new("RGBA", (512, 512), BACKGROUND + (255,))
size = int(512 * 0.86)
scaled = foreground.resize((size, size), Image.LANCZOS)
icon.alpha_composite(scaled, ((512 - size) // 2, (512 - size) // 2))
icon.save(out / "icon-512.png")

# Feature graphic: the falling characters of the app behind, the artwork on the left, the name and what it does.
graphic = Image.new("RGB", (1024, 500), BACKGROUND)
rain = Image.open(resources / "Images/rain_far.png").convert("RGBA")
rain = rain.resize((500, int(rain.height * 500 / rain.width)), Image.LANCZOS)
layer = Image.new("RGBA", graphic.size, (0, 0, 0, 0))
for x in range(0, 1024, rain.width):
    layer.alpha_composite(rain.crop((0, 300, min(rain.width, 1024 - x), 800)), (x, 0))
layer.putalpha(layer.getchannel("A").point(lambda a: int(a * 0.45)))
graphic.paste(layer, (0, 0), layer)

art = Image.open(resources / "Images/splash_screen.png").convert("RGBA")
art = art.crop((0, 0, art.width, int(art.height * 0.80)))  # the phones and the note; the name is set in type instead
height = 470
art = art.resize((int(art.width * height / art.height), height), Image.LANCZOS)
# Fade the artwork's own dark background into the graphic's on its edges.
mask = Image.new("L", art.size, 0)
ImageDraw.Draw(mask).ellipse((10, 10, art.width - 10, art.height - 10), fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(38))
graphic.paste(art.convert("RGB"), (18, 15), mask)


def font(names, size):
    for name in names:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default(size)


title_font = font(["segoeuib.ttf", "arialbd.ttf"], 78)
tagline_font = font(["consolab.ttf", "courbd.ttf"], 40)
line_font = font(["segoeui.ttf", "arial.ttf"], 27)

draw = ImageDraw.Draw(graphic)
left = 560
# A soft glow behind the title, as in the app.
glow = Image.new("RGBA", graphic.size, (0, 0, 0, 0))
ImageDraw.Draw(glow).text((left, 120), "AuraMusic", font=title_font, fill=GREEN + (150,))
graphic.paste(glow.filter(ImageFilter.GaussianBlur(14)), (0, 0), glow.filter(ImageFilter.GaussianBlur(14)))
draw.text((left, 120), "Aura", font=title_font, fill=(255, 255, 255))
draw.text((left + draw.textlength("Aura", font=title_font), 120), "Music", font=title_font, fill=GREEN)
draw.text((left + 4, 222), "> " + sys.argv[3], font=tagline_font, fill=GREEN)
for row, line in enumerate(sys.argv[4].split("|")):
    draw.text((left + 4, 296 + row * 38), line, font=line_font, fill=MINT)
graphic.save(out / sys.argv[5])
print("written:", [p.name for p in sorted(out.iterdir())])
