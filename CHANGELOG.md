# Changelog

## Sin publicar

### Añadido
- Cinco vistas de la biblioteca: tarjetas, tarjetas grandes, mosaico compacto, lista y detalles (se recuerda la elegida).
- Menú lateral contraíble: por defecto muestra iconos y texto; contraído, solo iconos. En ventanas estrechas pasa a iconos automáticamente.
- Icono en la bandeja del sistema con favoritos, neutralizar y salir; opción de minimizar a la bandeja.
- Atajos globales: Ctrl+Alt+→ / ← cambian de perfil (favoritos primero) y Ctrl+Alt+0 neutraliza el color.
- Horario automático: perfil de día y de noche a horas configurables.
- Perfiles por aplicación: se aplica un perfil mientras una aplicación está en primer plano y se restaura al salir.
- Exportar e importar perfiles (ajustes y favoritos).
- Registro en archivo (`%LOCALAPPDATA%\LumaProfiles\logs`).
- Proyecto de pruebas `tests/LumaProfiles.Tests` y solución `LumaProfiles.sln`.
- Nombres de accesibilidad (`AutomationProperties`) en controles principales.

### Cambiado
- Neutralizar color ahora devuelve la configuración natural de Windows y del monitor (estado original capturado); si no existe, usa el perfil neutro anterior.
- Cabecera de la biblioteca rediseñada: el buscador ocupa todo el ancho y es redondeado; barras de desplazamiento más finas.
- Diseño adaptable revisado: paneles y modales se ajustan a ventanas pequeñas.
- La interfaz sigue MVVM: `MainViewModel` + `IMonitorService`; `MainWindow` solo gestiona la ventana.
- Los 50 perfiles predeterminados pasan de C# a `Data/default-profiles.json`.
- Los temas claro y oscuro son `ResourceDictionary` independientes.
- Los archivos de usuario se guardan de forma atómica y llevan `SchemaVersion`.
- Al cargar, los textos de los perfiles predeterminados se actualizan sin perder tus ajustes ni favoritos.

### Corregido
- Un `profiles.json` o `settings.json` dañado ya no se descarta en silencio: se aparta como `.corrupt-*.bak` y se avisa.
- Una temperatura de color `null` heredada de versiones anteriores ya no deja vacío el selector.
