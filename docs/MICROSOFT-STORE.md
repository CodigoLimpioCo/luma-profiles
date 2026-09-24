# Publicación de Luma Profiles en Microsoft Store

## Formato correcto

Luma Profiles es una aplicación WPF de escritorio. Para Microsoft Store se prepara un paquete **MSIX** y, preferiblemente, un archivo **`.msixupload`**. Un `.msi` no es el formato recomendado para el flujo de publicación de Partner Center.

El paquete generado por `scripts/build-msix.ps1` queda sin firma para poder asociarlo a la identidad que Microsoft asigna al reservar el nombre de la aplicación. Microsoft firma el paquete durante la publicación de Store.

## Antes de compilar

1. Crea una cuenta de desarrollador en [Partner Center](https://partner.microsoft.com/dashboard).
2. Crea una nueva aplicación y reserva el nombre **Luma Profiles**.
3. En la página de identidad del producto copia el `Package/Identity Name`, `Publisher` y `Publisher display name`.
4. Instala el Windows 10/11 SDK para disponer de `MakeAppx.exe`.

## Generar el paquete

Desde la raíz del repositorio:

```powershell
.\scripts\build-msix.ps1 `
  -IdentityName 'IDENTITY_NAME_DE_PARTNER_CENTER' `
  -Publisher 'PUBLISHER_DE_PARTNER_CENTER' `
  -PublisherDisplayName 'Código Limpio'
```

Se generan `dist/store/LumaProfiles.msix`, `dist/store/LumaProfiles.msixupload` y `dist/store/assets`.

El paquete usa x64, Windows 10 19041 como mínimo y declara únicamente la capacidad `runFullTrust`, necesaria para una aplicación WPF de escritorio. La aplicación necesita que el usuario habilite DDC/CI en el monitor para los controles físicos.

## Pruebas antes de enviar

```powershell
Add-AppxPackage .\dist\store\LumaProfiles.msix
```

Para pruebas locales el paquete debe estar firmado con un certificado confiable. No uses un certificado autofirmado para producción. Ejecuta Windows App Certification Kit (WACK) y prueba instalación, actualización, desinstalación, modo claro/oscuro, tres idiomas, vista previa en tiempo real y ambos monitores.

## Ficha de Store propuesta

- **Nombre:** Luma Profiles
- **Subtítulo:** Color preciso para cada pantalla
- **Descripción corta:** Personaliza y aplica perfiles de color para tus monitores Windows con controles precisos, vista previa en tiempo real y modos para trabajo, juegos y cuidado visual.
- **Categoría sugerida:** Utilidades
- **Sitio web:** https://codigolimpio.com.co/
- **Soporte:** https://github.com/CodigoLimpioCo/luma-profiles/issues
- **Repositorio:** https://github.com/CodigoLimpioCo/luma-profiles
- **Privacidad:** La aplicación no recopila datos ni requiere cuenta. Los perfiles se guardan localmente en `%LOCALAPPDATA%\LumaProfiles`.

## Recursos de la ficha

La Store requiere al menos una captura y recomienda cuatro o más por familia de dispositivo. En `docs/` se incluyen capturas de la aplicación; en `store-assets/` se generan el logo cuadrado, el logo panorámico y la imagen de ficha.

Capturas recomendadas: `docs/app-screenshot.png`, `docs/luma-profiles-v0.7.0-live-preview.png`, `docs/luma-profiles-v0.7.0-about-modal.png` y `docs/luma-profiles-v0.7.2-light-contrast.png`.

## Datos que debe completar el publicador

- Identidad reservada y Publisher de Partner Center.
- Precio, disponibilidad regional y clasificación por edades.
- Texto legal de licencia y política de privacidad pública.
- Certificación WACK y pruebas en equipos limpios.
- Cuenta bancaria/fiscal y aceptación del acuerdo de desarrollador.

No se puede enviar la aplicación a la cuenta de Microsoft Store desde este repositorio sin acceso autenticado a Partner Center y sin la identidad reservada del producto.

## Referencias oficiales

- [Cargar paquetes MSIX](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
- [Requisitos de paquetes MSIX](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Información de la ficha de Store](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)
- [Capturas y logos](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/screenshots-and-images)
