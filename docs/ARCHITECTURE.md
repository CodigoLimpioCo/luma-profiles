# Arquitectura

La aplicación está construida con WPF y .NET para conservar una integración nativa con Windows sin dependencias externas.

- `DisplayProfile`: modelo editable y serializable.
- `ProfileStore`: valores predeterminados y persistencia por usuario.
- `MonitorService`: DDC/CI, rampa de gamma de Windows y plan de energía.
- `MainWindow`: interfaz, filtros y coordinación de acciones.

Los ajustes físicos utilizan `SetVCPFeature` de `dxva2.dll`. La gamma utiliza `SetDeviceGammaRamp` de `gdi32.dll`. Los perfiles se guardan fuera del repositorio para que una actualización no sobrescriba las personalizaciones del usuario.
