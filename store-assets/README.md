# Recursos de Microsoft Store — todo organizado para subir

## 1) Capturas de pantalla (obligatorio, mínimo 1, recomendado 4)

Ya están tomadas de la versión actual de la app y copiadas en 3 carpetas (mismas 4 imágenes en los 3 idiomas):

| Carpeta | Va en Partner Center → Store listings → idioma → Screenshots (Desktop) |
|---|---|
| `capturas-es-es/` | Spanish (Spain) |
| `capturas-en-us/` | English (United States) |
| `capturas-pt-br/` | Portuguese (Brazil) |

Cada carpeta contiene (versión 0.8.0, 1560 x 900):
- `1-biblioteca.png` — Biblioteca de 60 perfiles (tema oscuro)
- `2-tema-claro.png` — Tema claro
- `3-vista-lista.png` — Vista de lista de la categoría Pantallas VA
- `4-pantallas-va.png` — Categoría Pantallas VA en tarjetas

Los textos de la ficha (descripción, novedades y palabras clave en los tres idiomas) están en `docs/store-listing-v0.8.0.md`.
Al enviar la nueva versión, reemplaza en cada idioma las capturas anteriores por estas cuatro.

## 2) Logos de Store — NUEVOS, generados con las medidas exactas

Carpeta: **`logos-microsoft-store/`**

| Archivo | Sección en Partner Center | Medida |
|---|---|---|
| `poster-art-720x1080.png` | Store logos → 9:16 Poster art | 720 x 1080 |
| `poster-art-1440x2160.png` | Store logos → 9:16 Poster art (alta resolución) | 1440 x 2160 |
| `box-art-1080x1080.png` | Store logos → 1:1 Box art | 1080 x 1080 |
| `box-art-2160x2160.png` | Store logos → 1:1 Box art (alta resolución) | 2160 x 2160 |
| `app-tile-300x300.png` | Store display images → 1:1 App tile icon | 300 x 300 |
| `app-tile-150x150.png` | Store display images → 1:1 | 150 x 150 |
| `app-tile-71x71.png` | Store display images → 1:1 | 71 x 71 |

Diseño: fondo degradado oscuro de marca (igual al que usa el paquete .msix), ícono de Luma Profiles centrado, y en el Poster art el nombre "Luma Profiles" + "Monitor Studio · Código Limpio". Estas imágenes son **opcionales** (Partner Center toma el logo del paquete si no subes nada), pero ya están listas por si prefieres usarlas.

Solo se sube **una sola vez por idioma** — a diferencia de las capturas, los logos son a nivel de producto, no por idioma (se configuran en la misma pantalla donde apareció el bloque "Store logos" / "Store display images").

## Regenerar

```powershell
.\scripts\generate-store-assets.ps1
```

(esto solo regenera los assets del paquete .msix; los logos de `logos-microsoft-store/` se generaron aparte a partir del mismo ícono fuente `src/LumaProfiles/Assets/LumaProfilesIcon.png`)
