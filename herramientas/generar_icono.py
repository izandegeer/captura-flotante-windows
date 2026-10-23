"""Genera icono.ico: marco de captura con esquinas, sobre un cuadrado redondeado azul."""
import sys
from PIL import Image, ImageDraw

def dibujar(lado):
    escala = 4
    grande = lado * escala
    imagen = Image.new("RGBA", (grande, grande), (0, 0, 0, 0))
    lienzo = ImageDraw.Draw(imagen)
    lienzo.rounded_rectangle([0, 0, grande - 1, grande - 1], radius=grande * 0.22, fill=(38, 120, 255, 255))
    margen, largo, grosor = grande * 0.22, grande * 0.2, max(escala, int(grande * 0.08))
    blanco = (255, 255, 255, 255)
    for x, y, dx, dy in [(margen, margen, 1, 1), (grande - margen, margen, -1, 1),
                         (margen, grande - margen, 1, -1), (grande - margen, grande - margen, -1, -1)]:
        lienzo.line([(x, y), (x + dx * largo, y)], fill=blanco, width=grosor)
        lienzo.line([(x, y), (x, y + dy * largo)], fill=blanco, width=grosor)
    centro = grande / 2
    radio = grande * 0.11
    lienzo.ellipse([centro - radio, centro - radio, centro + radio, centro + radio], fill=blanco)
    return imagen.resize((lado, lado), Image.LANCZOS)

base = dibujar(256)
base.save(sys.argv[1], sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
