# Changelog

## 0.8.0 — Pantallas VA y punto de restauración

### Añadido
- Categoría "Pantallas VA" con 10 modos pensados para paneles VA económicos (como los de 22" a 120 Hz): equilibrado, sombras abiertas, gaming 120 Hz, competitivo, cine, oficina, lectura, nocturno, corregir azulado y color vivo. Levantan las sombras con gamma (los VA aplastan los negros), recortan el azul de fábrica y mantienen el contraste del monitor por debajo de donde se queman los blancos; la biblioteca pasa a 60 perfiles.
- Punto de restauración reforzado: se guarda al primer arranque (antes de cualquier cambio) también en un archivo propio, `restore-point.json`, que se recupera solo si `settings.json` se daña; se muestra su fecha en Configuración > Datos y se puede reemplazar con el estado actual ("Guardar estado actual", con confirmación). Las pantallas que no permiten leer su estado vuelven a la gamma neutra de Windows al restaurar.
- El menú lateral se puede ensanchar o estrechar (180–420 px) arrastrando su borde; también con las flechas del teclado cuando el borde tiene el foco. Doble clic o Inicio lo restablece, y el ancho se recuerda. Nunca deja el contenido con menos de 520 px.
- Vista previa antes/después: al pasar el ratón sobre la imagen de una tarjeta (normal o grande) aparece una comparación con divisor que sigue al puntero, simulando por software el perfil (brillo, contraste, saturación, matiz, gamma, RGB y temperatura) sin tocar el monitor. Se actualiza en vivo al mover los deslizadores.
- Registro de errores no controlados en `%LOCALAPPDATA%\LumaProfiles\logs` para diagnosticar cierres en otros equipos.
- Interruptor "Controlar el monitor (DDC/CI)" en Configuración > General: apagado, los perfiles solo usan la corrección por software y no se escribe nada en el monitor.
- Tipo de letra (12 fuentes, las instaladas en el equipo) y tamaño de la interfaz (80 %–150 %) configurables en Apariencia, con vista previa; la escala se aplica al soltar el control y el diseño adaptable la tiene en cuenta.
- Buscador avanzado: la búsqueda ignora mayúsculas y acentos y exige todas las palabras (en cualquier orden). El botón "Filtros" abre un panel con categorías combinables, estado (favoritos, HDR, alto rendimiento, modificados), temperatura de color, rangos de brillo/contraste/saturación y orden; muestra cuántos filtros hay activos, "N de M perfiles" y un botón para limpiar todo.
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
- Aplicar, Neutralizar, Restaurar y Revertir se ejecutan en segundo plano y en cola: la ventana ya no se congela. Mientras trabajan se muestra una barra de progreso superior, un indicador junto al mensaje de estado, el cursor de espera y los botones de acción quedan atenuados.
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
- Tinte amarillo/azul/verde en otros monitores: la app forzaba ganancias RGB a 100, el preajuste de color, la nitidez a 0 y asumía rangos 0–100, lo que solo es neutro en monitores con ese balance de fábrica. Ahora nunca escribe ganancias ni nitidez, solo envía el preajuste de color si eliges Cálido/Frío, escala brillo/contraste/saturación/matiz al rango real que informa el monitor, omite lo que el monitor no expone y devuelve al valor original lo que un perfil deja en neutro. En monitores con ganancias de fábrica a 100 y rango 100 el resultado es idéntico al anterior.
- Al pulsar Aplicar varias veces seguidas, Revertir (o el tiempo agotado) vuelve al estado anterior al primer cambio sin confirmar, no solo al anterior.
- Las categorías del menú lateral ya responden también al teclado (flechas) y reflejan los filtros activos.
- Restaurar el estado original verifica cada valor DDC/CI leyéndolo de vuelta, reintenta los que el monitor descarta y avisa de los que no acepta.
- Una reaplicación de correcciones en curso ya no puede pisar un Aplicar, Neutralizar o Restaurar posterior (se cancela).
- Un `profiles.json` o `settings.json` dañado ya no se descarta en silencio: se aparta como `.corrupt-*.bak` y se avisa.
- Una temperatura de color `null` heredada de versiones anteriores ya no deja vacío el selector.
