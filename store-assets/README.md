# Recursos de Microsoft Store

Estos PNG se generan con `scripts/generate-store-assets.ps1` a partir de la marca de Luma Profiles. Se incluyen variantes cuadradas para el paquete MSIX, el logo de ficha y un recurso panorámico para el mosaico de inicio.

Para regenerarlos:

```powershell
.\scripts\generate-store-assets.ps1
```

La identidad de publicador, el nombre reservado y la firma del paquete se configuran durante el flujo de Partner Center; estos recursos no contienen certificados ni credenciales.

Las capturas preparadas para la ficha están en `screenshots/`. Puedes cargarlas en Partner Center junto con el paquete y sustituirlas por capturas de equipos adicionales si la certificación lo requiere.
