# Luma Profiles

Aplicación creada por **[Código Limpio](https://codigolimpio.com.co/)** · [Repositorio público](https://github.com/CodigoLimpioCo/luma-profiles)

Luma Profiles es una aplicación de escritorio para Windows que permite guardar, personalizar y aplicar perfiles de color a uno o varios monitores compatibles con DDC/CI.

[![Disponible en Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-9PJRZB8HFCSN-0078D4?logo=microsoft)](https://apps.microsoft.com/detail/9PJRZB8HFCSN) ![Versión](https://img.shields.io/badge/versi%C3%B3n-0.10.0-blue) [![Licencia: uso no comercial](https://img.shields.io/badge/licencia-uso%20no%20comercial-orange)](LICENSE)

![Biblioteca de perfiles](store-assets/capturas-es-es/1-biblioteca.png)

| Tema claro | Pantallas VA |
| --- | --- |
| ![Tema claro](store-assets/capturas-es-es/2-tema-claro.png) | ![Pantallas VA](store-assets/capturas-es-es/4-pantallas-va.png) |

## Instalación

<a href="https://apps.microsoft.com/detail/9PJRZB8HFCSN"><img src="https://get.microsoft.com/images/es%20dark.svg" alt="Obtener en Microsoft Store" width="200"/></a>

- **Microsoft Store (recomendado):** [instalar Luma Profiles](https://apps.microsoft.com/detail/9PJRZB8HFCSN). Se actualiza sola.
- **Descarga directa:** el ejecutable portátil o el ZIP de cada versión en [Releases](https://github.com/CodigoLimpioCo/luma-profiles/releases).
- **Desde el código:** consulta [Ejecutar desde el código](#ejecutar-desde-el-código).

## Funciones

### Perfiles
- 77 perfiles ajustables por finalidad: color fiel, entretenimiento, rendimiento, cuidado visual, HDR, creativos, Gamer (con diez perfiles por tipo de juego), modos inspirados en ASUS GameVisual y diez modos para paneles VA económicos.
- «¿Qué vas a hacer?»: Jugar, Ver series y cine, Trabajar, Leer, De noche y Foto y diseño filtran la biblioteca en un clic, y los perfiles recientes quedan a mano.
- Un perfil distinto en cada pantalla, modo juego automático (pantalla completa), perfil con batería y deshacer/rehacer en el panel de ajuste.
- Perfiles personalizados: guarda los valores del panel de ajuste como perfil propio, renómbralo o elimínalo.
- Favoritos persistentes con su propia categoría.
- Buscador avanzado (sin distinguir mayúsculas ni acentos) con filtros combinables y orden.
- Cinco vistas: tarjetas, tarjetas grandes, mosaico, lista y detalles.
- Vista previa antes/después al pasar el ratón sobre una tarjeta, sin tocar el monitor, y vista previa en tiempo real al ajustar.

### Ajuste y monitores
- Brillo, contraste, gamma, temperatura de color, saturación, matiz y balance RGB.
- Aplicación a todas las pantallas o a las que elijas, con botón «Identificar».
- Control DDC/CI opcional: apagado, solo se usa corrección por software.
- Botón para neutralizar una dominante de color y cambio opcional al plan de energía Alto rendimiento.

### Seguridad
- Confirmación «¿Conservar los cambios?» con reversión automática a los 30 s, también para atajos y bandeja (tarjeta pequeña siempre visible).
- Punto de restauración guardado antes del primer cambio, con archivo de respaldo propio.
- Los perfiles nunca escriben ganancias RGB ni nitidez en el monitor.

### Automatización
- Horario con tramos múltiples, amanecer/atardecer automáticos y transición suave.
- Perfiles por aplicación en primer plano y modo juego automático para cualquier aplicación a pantalla completa.
- Perfil con batería para portátiles.
- Atajos globales: Ctrl+Alt+→/←/0 y Ctrl+Alt + tecla por perfil.
- Icono en la bandeja, inicio con Windows en segundo plano e instancia única.

### Personalización
- Tema claro, oscuro o del sistema; 17 colores de acento y color propio.
- Tipo de letra y tamaño de la interfaz configurables, menú lateral ajustable.
- Español, inglés y portugués; idiomas extensibles con archivos `.lang`.
- Exportación e importación de perfiles, horario, reglas y atajos.

El historial completo está en [CHANGELOG.md](CHANGELOG.md).

## Requisitos

- Windows 10 (2004, compilación 19041) o Windows 11.
- .NET Desktop Runtime 10 (la versión de Microsoft Store lo gestiona sola).
- Monitor con DDC/CI habilitado para controlar brillo, contraste y saturación.

La corrección de gamma funciona mediante las API de Windows. Algunos controladores gráficos, perfiles ICC o aplicaciones de calibración pueden reemplazarla posteriormente.

El control de matiz depende del modo GameVisual y del soporte DDC/CI del monitor. Cuando el propio monitor lo bloquea, Luma Profiles conserva el resto de los ajustes y muestra un aviso.

## Ejecutar desde el código

```powershell
dotnet run --project .\src\LumaProfiles\LumaProfiles.csproj
```

## Pruebas

```powershell
dotnet test .\LumaProfiles.sln
```

## Compilar

```powershell
dotnet build .\src\LumaProfiles\LumaProfiles.csproj -c Release
```

Para generar el ejecutable y el ZIP de publicación, usa `scripts/release.ps1`.

## Agregar un idioma

1. Copia uno de los archivos de `src/LumaProfiles/Locales`.
2. Cambia `meta.code` y `meta.name`.
3. Traduce los valores ubicados después del signo `=` sin alterar las claves.
4. Compila o copia el archivo a la carpeta `Locales` junto al ejecutable y reinicia la aplicación.

El selector descubre automáticamente todos los archivos `.lang` válidos.

## Seguridad y precisión de color

Luma Profiles no instala controladores ni necesita privilegios de administrador. Para trabajo donde la precisión sea crítica se recomienda utilizar un colorímetro y un perfil ICC medido para cada panel. Los modos cálidos se ofrecen por comodidad; no constituyen tratamiento médico.

## Arquitectura

La estructura del código se describe en [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licencia

Luma Profiles es **gratuito pero no de código abierto**: el código es visible, pero no se puede vender. El texto completo está en [LICENSE](LICENSE).

| Puedes, gratis | No puedes sin una licencia comercial |
| --- | --- |
| Usarlo en casa, en el trabajo o en tu empresa | Venderlo o cobrar por descargarlo o instalarlo |
| Leer, estudiar y modificar el código | Publicarlo en una tienda con otro nombre o editor |
| Compartir copias gratis con el aviso de licencia y un enlace a este repositorio | Incluirlo en un producto, servicio o suscripción de pago, o monetizarlo con anuncios |

**¿Quieres usarlo comercialmente?** Es posible con una licencia comercial por escrito, con una regalía del **15 % de los ingresos brutos** que obtengas con él, pagada cada trimestre. Escríbenos desde [codigolimpio.com.co](https://codigolimpio.com.co/) o abre un issue.

Las versiones 0.10.0 y anteriores se publicaron con licencia MIT; quien ya las tenía las conserva con esa licencia. Las contribuciones son bienvenidas; consulta [CONTRIBUTING.md](CONTRIBUTING.md).

Código Limpio no está afiliado con ASUS. Las marcas mencionadas pertenecen a sus respectivos propietarios.
