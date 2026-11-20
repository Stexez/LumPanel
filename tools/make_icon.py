"""Pixel-art LumPanel icon (amethyst star on a dark tile) -> app.ico. No external assets."""
from PIL import Image, ImageDraw
import sys

def render(size):
    unit = size / 16
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=max(2, size // 6), fill=(24, 18, 36, 255), outline=(122, 77, 204, 255), width=max(1, size // 32))
    cells = [(dx, dy) for dy in range(-6, 7) for dx in range(-6, 7) if (abs(dx) + .5) * (abs(dy) + .5) <= 5.5]
    for dx, dy in cells:
        x, y = (7.5 + dx) * unit, (7.5 + dy) * unit
        color = (255, 255, 255, 255) if dx == dy == 0 else (216, 194, 255, 255) if dx + dy < 0 else (167, 123, 243, 255)
        d.rectangle([x, y, x + unit - 1, y + unit - 1], fill=color)
    return img

big = render(256)
big.save(sys.argv[1], sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
