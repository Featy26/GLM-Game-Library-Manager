"""Generate GLM icon as proper multi-size ICO."""
from PIL import Image, ImageDraw, ImageFont
import struct, io, os

def create_icon_image(size):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    margin = int(size * 0.08)
    r = int(size * 0.18)

    bg_color = (45, 55, 120)
    x0, y0, x1, y1 = margin, margin, size - margin, size - margin
    draw.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=bg_color)

    # GLM text
    center_x = size / 2
    center_y = size * 0.38
    font_size = int(size * 0.22)
    try:
        font_bold = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", font_size)
    except:
        font_bold = ImageFont.load_default()

    text = "GLM"
    bbox = draw.textbbox((0, 0), text, font=font_bold)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    tx, ty = center_x - tw / 2, center_y - th / 2
    draw.text((tx + 1, ty + 1), text, fill=(0, 0, 0, 80), font=font_bold)
    draw.text((tx, ty), text, fill=(255, 255, 255), font=font_bold)

    # Arrow
    arrow_y = size * 0.65
    arrow_len = size * 0.32
    arrow_x_start = center_x - arrow_len / 2
    arrow_x_end = center_x + arrow_len / 2
    line_w = max(2, int(size * 0.04))
    arrow_color = (100, 200, 255)
    draw.line([(arrow_x_start, arrow_y), (arrow_x_end, arrow_y)], fill=arrow_color, width=line_w)
    head_size = size * 0.08
    draw.polygon([
        (arrow_x_end, arrow_y),
        (arrow_x_end - head_size, arrow_y - head_size),
        (arrow_x_end - head_size, arrow_y + head_size),
    ], fill=arrow_color)

    # Controller
    ctrl_y = size * 0.78
    ctrl_w, ctrl_h = size * 0.18, size * 0.08
    ctrl_x = center_x - ctrl_w / 2
    ctrl_color = (140, 220, 255)
    draw.rounded_rectangle([ctrl_x, ctrl_y, ctrl_x + ctrl_w, ctrl_y + ctrl_h],
                           radius=ctrl_h / 2, fill=ctrl_color)

    return img

# Build proper ICO file manually
sizes = [16, 32, 48, 256]
ico_path = os.path.join(os.path.dirname(__file__), "src", "GameTransfer.App", "glm.ico")

# For sizes <= 48, use BMP format in ICO; for 256, use PNG
entries = []
for s in sizes:
    img = create_icon_image(s)
    if s == 256:
        buf = io.BytesIO()
        img.save(buf, format='PNG')
        data = buf.getvalue()
    else:
        # Convert to BGRA BMP data
        pixels = img.tobytes("raw", "BGRA")
        # BMP header for ICO (BITMAPINFOHEADER, height doubled for AND mask)
        header = struct.pack('<IiiHHIIiiII',
            40,       # header size
            s,        # width
            s * 2,    # height (doubled for XOR + AND)
            1,        # planes
            32,       # bits per pixel
            0,        # compression
            len(pixels), # image size
            0, 0, 0, 0)
        # Flip rows (BMP is bottom-up)
        row_size = s * 4
        flipped = b''
        for row in range(s - 1, -1, -1):
            flipped += pixels[row * row_size:(row + 1) * row_size]
        # AND mask (all zeros = fully opaque)
        and_row = ((s + 31) // 32) * 4
        and_mask = b'\x00' * and_row * s
        data = header + flipped + and_mask
    entries.append((s, data))

# Write ICO file
with open(ico_path, 'wb') as f:
    # ICO header
    num = len(entries)
    f.write(struct.pack('<HHH', 0, 1, num))

    # Calculate offsets
    offset = 6 + num * 16  # header + directory entries

    # Write directory entries
    for s, data in entries:
        w = 0 if s == 256 else s
        h = 0 if s == 256 else s
        f.write(struct.pack('<BBBBHHII', w, h, 0, 0, 1, 32, len(data), offset))
        offset += len(data)

    # Write image data
    for _, data in entries:
        f.write(data)

print(f"Icon saved to {ico_path} ({os.path.getsize(ico_path)} bytes)")

# Preview
preview = create_icon_image(256)
png_path = os.path.join(os.path.dirname(__file__), "src", "GameTransfer.App", "glm_preview.png")
preview.save(png_path)
print(f"Preview saved to {png_path}")
