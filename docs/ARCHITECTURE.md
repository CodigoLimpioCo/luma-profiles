# Arquitectura

La aplicación está construida con WPF y .NET para conservar una integración nativa con Windows sin dependencias externas.

## Capas

- **Modelos** (`Models/`): `DisplayProfile` (editable y serializable), `ApplicationSettings` y sus tipos auxiliares.
- **Servicios** (`Services/`):
  - `ProfileStore`: catálogo predeterminado (`Data/default-profiles.json`, recurso incrustado) y personalizaciones por usuario, con versión de esquema, importación y exportación.
  - `ApplicationSettingsStore`: ajustes de usuario. Ambos almacenes escriben de forma atómica (`AtomicFile`) y, si un archivo está dañado, lo apartan como `*.corrupt-*.bak` en lugar de descartarlo.
  - `IMonitorService` / `MonitorService`: DDC/CI, rampa de gamma de Windows y plan de energía.
  - `ScheduleResolver`, `SolarTimes`, `ProfileBlend` y `AppRuleEngine`: lógica pura del horario por tramos (horas fijas y amanecer/atardecer), de la transición entre perfiles y de los perfiles por aplicación.
  - `ForegroundWatcher`, `HotkeyService`, `TrayIconService`: integración con Windows (aplicación en primer plano, atajos globales, bandeja).
  - `AppLog`: registro en `%LOCALAPPDATA%\LumaProfiles\logs` (14 días).
- **ViewModels** (`ViewModels/`): `MainViewModel` concentra el estado y los comandos, repartido en archivos parciales (horario y transición, atajos por perfil, perfiles personalizados, reglas, biblioteca…). Depende solo de interfaces (`IMonitorService`, `IShellService`), por eso se prueba sin hardware.
- **Vista**: `MainWindow` conserva el cromo de la ventana, el diseño adaptable y los hooks Win32. Los temas viven en `Themes/DarkTheme.xaml` y `Themes/LightTheme.xaml`.

Los ajustes físicos utilizan `SetVCPFeature` de `dxva2.dll`. La gamma utiliza `SetDeviceGammaRamp` de `gdi32.dll`. Los datos se guardan fuera del repositorio para que una actualización no sobrescriba las personalizaciones del usuario.

## Pruebas

```powershell
dotnet test .\LumaProfiles.sln
```

`tests/LumaProfiles.Tests` cubre almacenes, lógica de gamma, horario, reglas por aplicación y el `MainViewModel` con servicios simulados.
