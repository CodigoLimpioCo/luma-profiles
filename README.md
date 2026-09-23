# Luma Profiles

Luma Profiles es una aplicación de escritorio para Windows que permite guardar, personalizar y aplicar perfiles de color a uno o varios monitores compatibles con DDC/CI.

![Captura de Luma Profiles](docs/app-screenshot.png)

## Funciones

- Ocho perfiles iniciales: Natural, Referencia, Entretenimiento, Rendimiento, Cine cálido y tres niveles de cuidado visual.
- Tarjetas con vista previa, descripción y valores principales.
- Categorías para color fiel, entretenimiento, rendimiento y cuidado visual.
- Ajuste individual de brillo, contraste, saturación, gamma y balance RGB.
- Aplicación a ambas pantallas o a una pantalla específica.
- Guardado de personalizaciones en `%LOCALAPPDATA%\LumaProfiles\profiles.json`.
- Botón para recuperar una señal RGB neutra cuando aparece una dominante de color.
- Cambio opcional al plan de energía Alto rendimiento.

## Requisitos

- Windows 10 u 11.
- .NET Desktop Runtime 10.
- Monitor con DDC/CI habilitado para controlar brillo, contraste y saturación.

La corrección de gamma funciona mediante las API de Windows. Algunos controladores gráficos, perfiles ICC o aplicaciones de calibración pueden reemplazarla posteriormente.

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
