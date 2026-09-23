# Luma Profiles 0.4.1

Aplicación de escritorio para Windows creada por [Código Limpio](https://codigolimpio.com.co/) para personalizar, guardar y aplicar perfiles de imagen en uno o varios monitores.

## Descargas

- **`LumaProfiles-v0.4.1-win-x64.exe`**: ejecutable único y autocontenido. Incluye .NET; basta con descargarlo y abrirlo.
- **`LumaProfiles-v0.4.1-win-x64.zip`**: paquete ligero para equipos que ya tienen instalado .NET Desktop Runtime 10.

## Biblioteca de 43 perfiles

- **Color fiel (5):** Natural, Referencia, Estudio neutro, Fotografía diurna y Prueba de impresión.
- **ASUS GameVisual (7):** RTS/RPG, FPS, Cine ASUS, Escenario, Carrera, sRGB y MOBA.
- **HDR (5):** Gaming HDR, Cine HDR, Consola HDR, HDR luminoso y HDR sala oscura.
- **Color creativo (10):** Vibrante realista, Piel natural, Atardecer dorado, Océano frío, Monocromo editorial, Bosque profundo, Cyber nocturno, Pastel suave, Sepia documental e Invierno limpio.
- **Entretenimiento (5):** Entretenimiento, Cine cálido, Anime, Deportes y Documental.
- **Rendimiento (3):** Rendimiento, eSports y Respuesta rápida.
- **Cuidado visual (8):** Ojos suave, Ojos descanso, Ojos noche, Lectura cálida, Oficina suave, Atardecer, Noche roja y Sensibilidad suave.

Todos los perfiles son editables y las instalaciones existentes reciben automáticamente los perfiles nuevos sin perder sus ajustes guardados.

## Ajustes disponibles

- Brillo y contraste.
- Gamma.
- Saturación y matiz.
- Temperatura de color: usuario RGB, cálida 5000 K, neutra 6500 K y fría 7500 K.
- Balance independiente de rojo, verde y azul.
- Restauración de los valores originales de cada perfil.

## Pantallas y aplicación de cambios

- Aplicación simultánea a ambas pantallas.
- Selección individual de Pantalla 1 o Pantalla 2.
- Control físico mediante DDC/CI cuando el monitor lo permite.
- Corrección de gamma mediante las API de Windows.
- Botón **Neutralizar color** para recuperar una señal RGB equilibrada y eliminar dominantes accidentales.
- Algunos perfiles de rendimiento pueden activar el plan de energía Alto rendimiento.

## HDR

- Perfiles específicos para juegos, cine, consola, habitaciones luminosas y salas oscuras.
- Aviso cuando HDR necesita activarse en Windows.
- Acceso directo a la configuración HDR del sistema.
- El programa informa cuando el monitor bloquea ajustes SDR mientras HDR está activo.

## Interfaz

- Diseño oscuro integrado con Windows.
- Búsqueda por nombre, categoría o descripción.
- Filtros por categoría.
- Tarjetas con vista previa fotográfica, descripción y valores principales.
- Indicador del perfil activo.
- Editor dividido en controles de Imagen y Color.
- Selector visual de monitor con marca de selección.
- Estado local y contador total de perfiles en la cabecera.
- Icono propio multirresolución en el ejecutable, la ventana, la barra de tareas y el acceso directo.
- Enlaces integrados al sitio web y GitHub de Código Limpio.

## Guardado y seguridad

- Las personalizaciones se guardan en `%LOCALAPPDATA%\LumaProfiles\profiles.json`.
- No instala controladores.
- No requiere privilegios de administrador para su uso normal.
- No envía los perfiles a servicios externos: el control se realiza localmente.
- Para precisión profesional se recomienda calibrar cada panel con un colorímetro y un perfil ICC medido.

## Requisitos

- Windows 10 u 11 de 64 bits.
- DDC/CI habilitado en el monitor para controlar brillo, contraste y saturación compatibles.
- El `.exe` autocontenido no requiere instalar .NET por separado.
- El paquete `.zip` requiere .NET Desktop Runtime 10.

## Integridad del ejecutable

SHA-256 de `LumaProfiles-v0.4.1-win-x64.exe`:

`50718FEFC80028903AA7010524F64D03F7260BE124C7B77A6A1138DA654C1674`

Luma Profiles es software MIT. Código Limpio no está afiliado con ASUS; las marcas mencionadas pertenecen a sus respectivos propietarios.
