#!/usr/bin/env python3
import sys
import os
import struct
from io import BytesIO
from PIL import Image

def write_ico(output_path: str, img: Image.Image, sizes=[16, 24, 32, 48, 64, 128, 256]):
    frames = []
    # Ensure square RGBA
    w, h = img.size
    min_dim = min(w, h)
    left = (w - min_dim) // 2
    top = (h - min_dim) // 2
    cropped = img.crop((left, top, left + min_dim, top + min_dim)).convert("RGBA")

    for s in sorted(sizes):
        resized = cropped.resize((s, s), Image.Resampling.LANCZOS)
        buf = BytesIO()
        resized.save(buf, format="PNG")
        frames.append((s, s, buf.getvalue()))

    count = len(frames)
    offset = 6 + 16 * count
    entries = []
    for s_w, s_h, png in frames:
        bw = s_w if s_w < 256 else 0
        bh = s_h if s_h < 256 else 0
        entries.append(
            struct.pack(
                "<BBBBHHII",
                bw, bh, 0, 0, 0, 32, len(png), offset
            )
        )
        offset += len(png)

    with open(output_path, "wb") as fp:
        fp.write(struct.pack("<HHH", 0, 1, count))
        for e in entries:
            fp.write(e)
        for _, _, png in frames:
            fp.write(png)
    print(f"Successfully generated multi-res ICO at {output_path} with sizes {sizes}")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: apply_icon.py <input_image> <output_ico>")
        sys.exit(1)
    in_img = sys.argv[1]
    out_ico = sys.argv[2]
    img = Image.open(in_img)
    write_ico(out_ico, img)
