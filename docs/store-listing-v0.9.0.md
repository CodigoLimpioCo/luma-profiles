# Ficha de Microsoft Store — Luma Profiles 0.9.0

Paquete: `dist/store/LumaProfiles.msixupload` (generado con `scripts/build-msix.ps1`, versión de paquete `0.9.0.0`).
Capturas: `store-assets/capturas-es-es/`, `capturas-en-us/`, `capturas-pt-br/` (1 biblioteca, 2 tema claro, 3 vista de lista, 4 categoría Pantallas VA, 5 configuración, 6 datos y restauración; las mismas de 0.8.0; conviene renovar la 1 porque ahora la biblioteca muestra 67 perfiles).

## Español (España)

**Nombre:** Luma Profiles
**Subtítulo:** Color preciso para cada pantalla

**Descripción**
Luma Profiles es tu estudio de color para monitores Windows. Elige entre 67 perfiles ajustables, previsualiza el resultado antes de aplicarlo y cambia brillo, contraste, gamma, saturación, matiz y balance RGB en cada pantalla, sin cuentas ni conexión a Internet.

- 67 perfiles: color fiel, HDR, gamer, entretenimiento, cuidado visual y una categoría nueva pensada para paneles VA económicos.
- Vista previa antes/después al pasar el ratón sobre cada perfil y ajuste en tiempo real.
- Elige a qué pantallas se aplica: todas o solo las que quieras, con botón para identificarlas.
- Confirmación tipo Windows: si no conservas los cambios en 30 segundos, todo vuelve a como estaba.
- Punto de restauración guardado en el primer arranque para volver siempre a tu configuración original.
- Tema claro, oscuro o del sistema; español, inglés y portugués; cinco vistas de biblioteca.

**Novedades de la versión 0.9.0**
- Siete perfiles nuevos por tipo de juego: Shooter militar, Shooter táctico, Battle royale, Terror y noche, Mundo abierto, Carreras y Colorido cartoon. La biblioteca llega a 67 perfiles.
- Nuevo botón Aplicar: prueba unos valores en pantalla sin cambiar el perfil guardado; Guardar y aplicar los conserva.
- Gamma más fiable: si Windows rechaza una curva de color muy fuerte, se aplica la versión más intensa que acepta y se avisa.

**Palabras clave:** monitor, color, brillo, contraste, gamma, HDR, VA, gaming, cuidado visual, luz azul, perfiles

## English (United States)

**Name:** Luma Profiles
**Subtitle:** Precise color for every screen

**Description**
Luma Profiles is a color studio for Windows monitors. Pick from 67 adjustable profiles, preview the result before applying it, and tune brightness, contrast, gamma, saturation, hue and RGB balance on each display — no account, no internet connection.

- 67 profiles: accurate color, HDR, gaming, entertainment, eye care and a new category made for budget VA panels.
- Before/after preview when you hover a profile, plus real-time adjustment.
- Choose which displays a profile applies to: all of them or just the ones you pick, with an Identify button.
- Windows-style confirmation: if you don't keep the changes within 30 seconds, everything goes back to how it was.
- A restore point saved on first launch so you can always return to your original setup.
- Light, dark or system theme; Spanish, English and Portuguese; five library views.

**What's new in 0.9.0**
- Seven new profiles by game type: Military shooter, Tactical shooter, Battle royale, Horror and night, Open world, Racing and Colorful cartoon. The library reaches 67 profiles.
- New Apply button: try values on screen without changing the saved profile; Save and apply keeps them.
- More reliable gamma: if Windows rejects a very strong color curve, the strongest version it accepts is applied and you are told.

**Keywords:** monitor, color, brightness, contrast, gamma, HDR, VA, gaming, eye care, blue light, profiles

## Português (Brasil)

**Nome:** Luma Profiles
**Subtítulo:** Cor precisa para cada tela

**Descrição**
O Luma Profiles é o seu estúdio de cor para monitores Windows. Escolha entre 67 perfis ajustáveis, veja o resultado antes de aplicar e ajuste brilho, contraste, gama, saturação, matiz e balanço RGB em cada tela, sem conta e sem internet.

- 67 perfis: cor fiel, HDR, gamer, entretenimento, cuidado visual e uma nova categoria pensada para painéis VA econômicos.
- Pré-visualização antes/depois ao passar o mouse sobre cada perfil e ajuste em tempo real.
- Escolha em quais telas aplicar: todas ou só as que quiser, com botão para identificá-las.
- Confirmação no estilo Windows: se você não mantiver as alterações em 30 segundos, tudo volta ao que era.
- Ponto de restauração salvo na primeira abertura para sempre voltar à sua configuração original.
- Tema claro, escuro ou do sistema; espanhol, inglês e português; cinco modos de exibição da biblioteca.

**Novidades da versão 0.9.0**
- Sete novos perfis por tipo de jogo: Shooter militar, Shooter tático, Battle royale, Terror e noite, Mundo aberto, Corrida e Cartoon colorido. A biblioteca chega a 67 perfis.
- Novo botão Aplicar: teste valores na tela sem alterar o perfil salvo; Salvar e aplicar os mantém.
- Gama mais confiável: se o Windows rejeitar uma curva de cor muito forte, é aplicada a versão mais intensa que ele aceita, com aviso.

**Palavras-chave:** monitor, cor, brilho, contraste, gama, HDR, VA, gaming, cuidado visual, luz azul, perfis

## Notas de certificación (todos los idiomas)

Luma Profiles usa la capacidad `runFullTrust` para controlar el brillo y el contraste de los monitores por DDC/CI y ajustar la rampa de gamma de Windows. Declara una tarea de inicio (`windows.startupTask`) desactivada por defecto: solo se activa si el usuario lo pide en la aplicación, y puede desactivarla en Configuración de Windows > Aplicaciones > Inicio. No recopila datos ni requiere cuenta; los perfiles se guardan en `%LOCALAPPDATA%\LumaProfiles`. Para los controles físicos, el monitor debe tener DDC/CI habilitado.

## Español: descripción y características propuestas para Partner Center

Actualizadas para 0.9.0 (67 perfiles y perfiles por tipo de juego).

**Descripción**

Luma Profiles es una aplicación de escritorio para Windows que permite guardar, personalizar y aplicar perfiles de color a uno o varios monitores compatibles con DDC/CI.

Ajusta brillo, contraste, gamma, temperatura de color, saturación, matiz y balance RGB desde una interfaz clara, con vista previa antes/después y en tiempo real, y temas claro y oscuro.

Incluye 67 perfiles listos para usar (y puedes guardar los tuyos) organizados por categoría: color fiel y referencia, entretenimiento y cine, rendimiento y gaming (Competitivo, Inmersivo, Sombras, Arcade vibrante, siete perfiles por tipo de juego y siete estilos inspirados en GameVisual), HDR, fotografía y diseño, cuidado visual (filtro azul, lectura, blanco y negro) y diez modos pensados para paneles VA económicos.

Si un cambio no te convence, no tienes que deshacerlo a mano: Luma Profiles pregunta si quieres conservarlo y, pasados 30 segundos sin respuesta, vuelve a como estaba. Además guarda un punto de restauración la primera vez que se abre, para regresar siempre a tu configuración original.

Luma Profiles no instala controladores ni requiere privilegios de administrador. Todas las personalizaciones se guardan localmente en tu equipo. Disponible en español, inglés y portugués.

Una aplicación de Código Limpio (codigolimpio.com.co).

**Características del producto (una por línea, máx. 200 caracteres)**

1. 67 perfiles de color listos para usar, organizados por categoría y con buscador y filtros avanzados
2. Vista previa antes/después al pasar el ratón sobre cada perfil y en tiempo real, antes de aplicar los cambios
3. Ajuste manual de brillo, contraste, gamma, temperatura de color, saturación, matiz y balance RGB
4. Diez modos para paneles VA económicos: sombras abiertas, corrección del tono azulado, gaming a 120 Hz, lectura y nocturno
5. Modos Gamer: Competitivo, Inmersivo, Sombras, Arcade vibrante, siete perfiles por tipo de juego (shooters, battle royale, terror, mundo abierto, carreras, cartoon) y siete estilos GameVisual
6. Modos HDR, fotografía y diseño para un trabajo de color más preciso
7. Cuidado visual: filtro de luz azul, lectura, blanco y negro y baja intensidad nocturna
8. Confirmación tipo Windows: si no conservas los cambios en 30 segundos, todo vuelve a como estaba
9. Punto de restauración guardado al primer arranque para volver siempre a tu configuración original
10. Compatible con uno o varios monitores DDC/CI: elige todas las pantallas o solo las que quieras, con botón para identificarlas
11. Reglas por aplicación, horarios con varios tramos (también al amanecer y atardecer), transición suave, atajos globales y por perfil, y bandeja del sistema
12. Guarda tus propios perfiles y expórtalos o impórtalos junto con tu horario, reglas y atajos
13. Cinco vistas de la biblioteca, menú lateral ajustable, tipo de letra y tamaño configurables; interfaz en español, inglés y portugués, con tema claro, oscuro o el de Windows
14. Puede iniciarse con Windows (en la bandeja, si lo eliges) y no instala controladores ni requiere permisos de administrador; todo se guarda localmente
