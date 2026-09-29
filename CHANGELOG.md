# Changelog

## Sin publicar

### Añadido
- Selector de pantallas con botones toggle: "Todas" y uno por cada monitor conectado (multiselección: por ejemplo 1 y 3 sin la 2). Se recuerda la elección, se actualiza al conectar o desconectar monitores y nunca deja todas desactivadas.
- Botón "Identificar" que muestra el número de cada pantalla sobre ella durante unos segundos.
- Configuración por categorías (Apariencia, Diseño, General, Idioma, Automatización, Datos y restauración) con "Restablecer sección" en cada una.
- Tema Claro / Oscuro / Sistema (sigue a Windows), color de acento (cian, índigo, azul, esmeralda, rosa, ámbar) y grosor de la barra de desplazamiento configurables.
- Confirmación al estilo Windows: tras Aplicar, Guardar y aplicar, Neutralizar o Restaurar aparece "¿Conservar los cambios?" y, si no confirmas en 30 s (o cierras la app), se revierte todo: valores DDC/CI, gamma, plan de energía, correcciones guardadas y perfil activo. Se puede desactivar en Configuración.
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
- Configuración es una página dentro de la app (con secciones General, Automatización y Datos) en lugar de un modal.
- Pulsar "Ajustar" abre el panel de ajustes si está oculto, también en ventanas estrechas.
- Barras de desplazamiento muy finas que se engrosan al pasar el ratón.
- Neutralizar color ahora devuelve la configuración natural de Windows y del monitor (estado original capturado); si no existe, usa el perfil neutro anterior.
- Cabecera de la biblioteca rediseñada: el buscador ocupa todo el ancho y es redondeado; barras de desplazamiento más finas.
- Diseño adaptable revisado: paneles y modales se ajustan a ventanas pequeñas.
- La interfaz sigue MVVM: `MainViewModel` + `IMonitorService`; `MainWindow` solo gestiona la ventana.
- Los 50 perfiles predeterminados pasan de C# a `Data/default-profiles.json`.
- Los temas claro y oscuro son `ResourceDictionary` independientes.
- Los archivos de usuario se guardan de forma atómica y llevan `SchemaVersion`.
- Al cargar, los textos de los perfiles predeterminados se actualizan sin perder tus ajustes ni favoritos.

### Corregido
- Restaurar el estado original verifica cada valor DDC/CI leyéndolo de vuelta, reintenta los que el monitor descarta y avisa de los que no acepta.
- Una reaplicación de correcciones en curso ya no puede pisar un Aplicar, Neutralizar o Restaurar posterior (se cancela).
- Un `profiles.json` o `settings.json` dañado ya no se descarta en silencio: se aparta como `.corrupt-*.bak` y se avisa.
- Una temperatura de color `null` heredada de versiones anteriores ya no deja vacío el selector.
