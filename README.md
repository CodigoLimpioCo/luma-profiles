# Luma Profiles

Aplicación creada por **[Código Limpio](https://codigolimpio.com.co/)** · [GitHub](https://github.com/CodigoLimpioCo)

Luma Profiles es una aplicación de escritorio para Windows que permite guardar, personalizar y aplicar perfiles de color a uno o varios monitores compatibles con DDC/CI.

![Captura de Luma Profiles](docs/app-screenshot.png)

## Funciones

- 43 perfiles iniciales organizados por finalidad.
- Siete modos inspirados en ASUS GameVisual: RTS/RPG, FPS, Cine, Escenario, Carrera, sRGB y MOBA.
- Tres puntos de partida HDR: Gaming HDR, Cine HDR y Consola HDR.
- Cinco estilos creativos: Vibrante realista, Piel natural, Atardecer dorado, Océano frío y Monocromo editorial.
- Veinte perfiles adicionales para fotografía, diseño, impresión, HDR, anime, deportes, documentales, eSports, estilos creativos y comodidad visual.
- Natural, Referencia, Entretenimiento, Rendimiento, Cine cálido y tres niveles de cuidado visual.
- Tarjetas con vista previa, descripción y valores principales.
- Categorías para color fiel, entretenimiento, rendimiento y cuidado visual.
- Ajuste individual de brillo, contraste, gamma, temperatura de color, saturación, matiz y balance RGB.
- Interfaz oscura integrada, tarjetas fotográficas, tres columnas y editor dividido en Imagen y Color.
- Aplicación a ambas pantallas o a una pantalla específica.
- Guardado de personalizaciones en `%LOCALAPPDATA%\LumaProfiles\profiles.json`.
- Botón para recuperar una señal RGB neutra cuando aparece una dominante de color.
- Cambio opcional al plan de energía Alto rendimiento.

## Requisitos

- Windows 10 u 11.
- .NET Desktop Runtime 10.
- Monitor con DDC/CI habilitado para controlar brillo, contraste y saturación.

La corrección de gamma funciona mediante las API de Windows. Algunos controladores gráficos, perfiles ICC o aplicaciones de calibración pueden reemplazarla posteriormente.

El control de matiz depende del modo GameVisual y del soporte DDC/CI del monitor. Cuando el propio monitor lo bloquea, Luma Profiles conserva el resto de los ajustes y muestra un aviso.

## Ejecutar desde el código

```powershell
dotnet run --project .\src\LumaProfiles\LumaProfiles.csproj
```

## Compilar

```powershell
dotnet build .\src\LumaProfiles\LumaProfiles.csproj -c Release
```

## Seguridad y precisión de color

Luma Profiles no instala controladores ni necesita privilegios de administrador. Para trabajo donde la precisión sea crítica se recomienda utilizar un colorímetro y un perfil ICC medido para cada panel. Los modos cálidos se ofrecen por comodidad; no constituyen tratamiento médico.

## Diseño

La interfaz implementada se encuentra en `docs/app-screenshot.png`. El concepto visual inicial está conservado en `docs/design-concept.png`.

## Licencia

MIT. Las contribuciones y mejoras son bienvenidas.

Código Limpio no está afiliado con ASUS. Las marcas mencionadas pertenecen a sus respectivos propietarios.
