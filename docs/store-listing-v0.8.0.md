# Ficha de Microsoft Store — Luma Profiles 0.8.0

Paquete: `dist/store/LumaProfiles.msixupload` (generado con `scripts/build-msix.ps1`, versión de paquete `0.8.0.0`).
Capturas: `store-assets/capturas-es-es/`, `capturas-en-us/`, `capturas-pt-br/` (1 biblioteca, 2 tema claro, 3 vista de lista, 4 categoría Pantallas VA).

## Español (España)

**Nombre:** Luma Profiles
**Subtítulo:** Color preciso para cada pantalla

**Descripción**
Luma Profiles es tu estudio de color para monitores Windows. Elige entre 60 perfiles ajustables, previsualiza el resultado antes de aplicarlo y cambia brillo, contraste, gamma, saturación, matiz y balance RGB en cada pantalla, sin cuentas ni conexión a Internet.

- 60 perfiles: color fiel, HDR, gamer, entretenimiento, cuidado visual y una categoría nueva pensada para paneles VA económicos.
- Vista previa antes/después al pasar el ratón sobre cada perfil y ajuste en tiempo real.
- Elige a qué pantallas se aplica: todas o solo las que quieras, con botón para identificarlas.
- Confirmación tipo Windows: si no conservas los cambios en 30 segundos, todo vuelve a como estaba.
- Punto de restauración guardado en el primer arranque para volver siempre a tu configuración original.
- Tema claro, oscuro o del sistema; español, inglés y portugués; cinco vistas de biblioteca.

**Novedades de la versión 0.8.0**
- Nueva categoría "Pantallas VA": 10 modos para paneles VA económicos (22" a 120 Hz): sombras abiertas, corregir azulado, gaming 120 Hz, competitivo, cine, oficina, lectura, nocturno y más.
- Punto de restauración reforzado: se guarda antes de cualquier cambio, con copia propia que sobrevive a un archivo de ajustes dañado; puedes reemplazarlo desde Configuración > Datos.
- Menú lateral con ancho ajustable arrastrando su borde.
- Los idiomas incluidos funcionan siempre, incluso si la carpeta de la aplicación es de solo lectura.
- Correcciones para que los perfiles no tiñan de amarillo, azul o verde otras pantallas.

**Palabras clave:** monitor, color, brillo, contraste, gamma, HDR, VA, gaming, cuidado visual, luz azul, perfiles

## English (United States)

**Name:** Luma Profiles
**Subtitle:** Precise color for every screen

**Description**
Luma Profiles is a color studio for Windows monitors. Pick from 60 adjustable profiles, preview the result before applying it, and tune brightness, contrast, gamma, saturation, hue and RGB balance on each display — no account, no internet connection.

- 60 profiles: accurate color, HDR, gaming, entertainment, eye care and a new category made for budget VA panels.
- Before/after preview when you hover a profile, plus real-time adjustment.
- Choose which displays a profile applies to: all of them or just the ones you pick, with an Identify button.
- Windows-style confirmation: if you don't keep the changes within 30 seconds, everything goes back to how it was.
- A restore point saved on first launch so you can always return to your original setup.
- Light, dark or system theme; Spanish, English and Portuguese; five library views.

**What's new in 0.8.0**
- New "VA displays" category: 10 modes for budget VA panels (22" at 120 Hz): open shadows, fix bluish tint, gaming 120 Hz, competitive, cinema, office, reading, night and more.
- Hardened restore point: saved before any change, with its own copy that survives a damaged settings file; replace it from Settings > Data.
- Side menu with a width you can change by dragging its edge.
- Bundled languages always work, even when the app folder is read-only.
- Fixes so profiles no longer tint other displays yellow, blue or green.

**Keywords:** monitor, color, brightness, contrast, gamma, HDR, VA, gaming, eye care, blue light, profiles

## Português (Brasil)

**Nome:** Luma Profiles
**Subtítulo:** Cor precisa para cada tela

**Descrição**
O Luma Profiles é o seu estúdio de cor para monitores Windows. Escolha entre 60 perfis ajustáveis, veja o resultado antes de aplicar e ajuste brilho, contraste, gama, saturação, matiz e balanço RGB em cada tela, sem conta e sem internet.

- 60 perfis: cor fiel, HDR, gamer, entretenimento, cuidado visual e uma nova categoria pensada para painéis VA econômicos.
- Pré-visualização antes/depois ao passar o mouse sobre cada perfil e ajuste em tempo real.
- Escolha em quais telas aplicar: todas ou só as que quiser, com botão para identificá-las.
- Confirmação no estilo Windows: se você não mantiver as alterações em 30 segundos, tudo volta ao que era.
- Ponto de restauração salvo na primeira abertura para sempre voltar à sua configuração original.
- Tema claro, escuro ou do sistema; espanhol, inglês e português; cinco modos de exibição da biblioteca.

**Novidades da versão 0.8.0**
- Nova categoria "Telas VA": 10 modos para painéis VA econômicos (22" a 120 Hz): sombras abertas, corrigir tom azulado, gaming 120 Hz, competitivo, cinema, escritório, leitura, noturno e mais.
- Ponto de restauração reforçado: salvo antes de qualquer alteração, com cópia própria que sobrevive a um arquivo de configurações danificado; pode ser substituído em Configurações > Dados.
- Menu lateral com largura ajustável arrastando a borda.
- Os idiomas incluídos sempre funcionam, mesmo com a pasta do aplicativo somente leitura.
- Correções para que os perfis não tinjam outras telas de amarelo, azul ou verde.

**Palavras-chave:** monitor, cor, brilho, contraste, gama, HDR, VA, gaming, cuidado visual, luz azul, perfis

## Notas de certificación (todos los idiomas)

Luma Profiles usa la capacidad `runFullTrust` para controlar el brillo y el contraste de los monitores por DDC/CI y ajustar la rampa de gamma de Windows. No recopila datos ni requiere cuenta; los perfiles se guardan en `%LOCALAPPDATA%\LumaProfiles`. Para los controles físicos, el monitor debe tener DDC/CI habilitado.
