# Plan de optimización de SCCosmos

Documento vivo para continuar entre sesiones. Empezado el 31/10/2026.

## Reglas de trabajo

- Bloques pequeños e independientes, autorizados por el usuario; cada uno termina con un informe.
- Cada versión que se saque se publica (desde el 31/10/2026, a petición del usuario): `Herramientas\publicar_version.ps1 -Version X.Y.Z -NotasArchivo notas.md` (sube versión, compila APK y ZIP, commit + push y release en GitHub para actualizar otros dispositivos desde Configuración) y se instala directamente:
  - PC: copiar `SCCosmos\SCCosmos\bin\Release\net10.0-windows\publish\*` a `C:\Users\Usuario\Documents\2 - ESP-BA-01\8 - Otros\SCCosmos\` con la app cerrada (como hace ActualizacionPc con robocopy /E).
  - Móvil: `adb install -r` del APK si está conectado; si no, avisar (o actualizar desde Configuración).
  - Antes de publicar, compilar Android a mano: el script oculta los errores de compilación y podría subir un APK antiguo.
- Sin cambios de esquema, índices ni datos reales sin autorización y sin copia de seguridad previa.
- Respetar `CLAUDE.md`: sin dependencias nuevas, sin actualizar .NET/MAUI/NuGet, sin tocar `bin/`, `obj/` ni archivos generados.
- Ninguna mejora se da por buena sin medirla antes y después con el protocolo de abajo; si no mejora, se revierte.
- Cuota de Claude Pro: lecturas dirigidas, sin subagentes en paralelo, pocas vueltas, sin repetir análisis ya hechos.

## Arquitectura

| Parte | Tecnología | Notas |
|---|---|---|
| `SCCosmos/SCCosmos` | .NET 10, WPF, WPF-UI 4.3 | code-behind; `DatabaseHelper` estático |
| `SCCosmos.Mobile` | .NET MAUI 10.0.90 (Android) | code-behind; `Data/DatabaseHelper.*` (15 parciales); servicios estáticos; sin DI |
| `SincroCompartido` | código fuente compilado en ambas apps | sincronización por registro de cambios (disparadores) por red local y carpeta; convertidores; actualizador |

- Acceso a datos: Microsoft.Data.Sqlite con SQL a mano, síncrono, una conexión por operación.
- BD del PC: `%LOCALAPPDATA%\SCCosmos\sccosmos.db` (31 MB, WAL). 45 tablas, 117 disparadores, 0 índices explícitos.
- Reglas de negocio duplicadas en Windows y Android (causa de discrepancias pasadas).
- WPF-UI envuelve cada página en un `ScrollViewer` (altura infinita): las páginas con tablas que se desplazan solas lo desactivan con `ScrollExteriorPagina`. Su estilo de `DataGrid` deja `CanContentScroll=False` (sin virtualización) si no se fija `ScrollViewer.CanContentScroll="True"`.
- En el PC el motor de sincronización trabaja conectándose a los demás (no queda escuchando en un puerto); si no arranca, se anota `Error.Sincro.Iniciar` en el registro.

## Protocolo de medición

Vigilante de bloqueos (ambas apps, siempre activo): `Bloqueo.UI <pantalla> N ms` cuando la interfaz tarda más de 200 ms en responder.

Windows (Release): `dotnet build SCCosmos\SCCosmos\SCCosmos.csproj -c Release` y ejecutar `bin\Release\net10.0-windows\SCCosmos.exe` con `SCCOSMOS_PERF=1` y `SCCOSMOS_PERF_SALIR=1`: recorre cada página dos veces (`Nav1` con JIT, `Nav2` en caliente) y la visita de referencia (Mérida - Centro, 2026-10) y se cierra. Registro en `%TEMP%\sccosmos_perf.log`; tres pasadas y mediana. Para probar pestañas concretas: UI Automation (`SelectionItemPattern` sobre el `TabItem`).

Android (Release arm64): `dotnet publish SCCosmos.Mobile\SCCosmos.Mobile\SCCosmos.Mobile.csproj -f net10.0-android -c Release -p:RuntimeIdentifier=android-arm64` + `adb install -r` del `*-Signed.apk`. Arranque: `am force-stop` + `am start -W`. Visita de la semana: `am start -n com.companyname.sccosmos.mobile/crc6419d9fc6104e377e1.MainActivity --es sccosmos_destino visita`. Fluidez: `dumpsys gfxinfo` antes y después de pestañas y desplazamientos (pestañas localizadas con `uiautomator dump`). Segundo plano: `input keyevent KEYCODE_HOME` y líneas `Ciclo.*`. Registro: `adb logcat -s SCCosmosPerf` o `adb pull /sdcard/Android/data/com.companyname.sccosmos.mobile/files/sccosmos_perf.log`. El Fold7 tiene dos pantallas: buscar una línea `mCurrentFocus=` con el paquete.

Herramientas: adb en `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`; sqlite3 de solo lectura en `C:\Users\Usuario\miniconda3\Library\bin\sqlite3.exe -readonly`. Resultados y APK de cada medición en `%TEMP%\sccosmos_medicion`. En PowerShell, no llamar `R` a una función (es alias de `Invoke-History`) y no usar `|` como marcador de salto de línea en reemplazos de código.

## Hallazgos

✅ medido · 🔎 visto en el código · ❓ hipótesis · ✔️ corregido

| # | Hallazgo | Estado | Bloque |
|---|---|---|---|
| S1 | Android: `.Wait(3 s)` en `OnStop`/`OnSleep` al salir a segundo plano | ✔️ 6,1 s → 0 ms | 1 |
| S2 | Android Visitas: listas dentro de `ScrollView` (sin virtualización) | ✔️ abrir 653 → 264 ms | 2 |
| S2b | Android Visitas: tarjeta de persona pesada; el primer desplazamiento rápido tiene más tirones (p99 113 ms) | ✅ | 2 |
| S3 | Windows Visitas: al abrir se cargaban todas las pestañas; Territorios costaba 300–380 ms. (La tabla no era el problema: solo muestra el grupo elegido, ~18 filas.) | ✔️ bloqueo 484 ms → ninguno en 4 de 6 aperturas | 2 |
| S4 | Windows: Visitas, Asambleas y Correspondencia se recreaban al llegar cambios de otro equipo | ✔️ ahora aviso «Hay cambios de otro dispositivo · Actualizar» (sin probar con cambios reales) | 2 |
| S5 | Sincro: cada 1 s por par (y cada 6 s en la carpeta) se recorría `SCCosmos_Cambios` aunque no hubiera cambios | ✔️ marca barata (fecha de escritura de la BD y su WAL) + consulta forzada cada 30 s; sin cambio de esquema. CPU en reposo del PC sin mejora medible (1,1 % → 1,3 %, pendiente de analizar); en el móvil, por medir | 3 |
| S6 | Guardados borrar + reinsertar (D 8.497 / I 5.678 / U 684); borrados nunca podados | ✅ | 3 |
| S7 | `InicializarDatabase()` completo (~100 ms) en cada `ObtenerCongregaciones` | ✔️ Personas 450 → 220 ms | 1 |
| S8 | `PersonasPage` no se daba de baja de `ProgresoCambiado` | ✔️ | 1 |
| S9 | Circuito, Asambleas y Correspondencia: primera apertura 200–400 ms | ✅ | 2 |
| S10 | Windows sin ReadyToRun: JIT en la primera apertura y en el arranque (requiere cambiar la ruta de publicación del script) | ✅ indicio | 5 |
| S11 | `Thread.Sleep` fijos en Selenium (6–51 s; en segundo plano) | 🔎 | 5 |
| S12 | Fotos sin `DecodePixelWidth` / `Freeze` / caché | 🔎 | 5 |
| S13 | Windows Calendario sin virtualizar: se creaban las 210 semanas | ✔️ 1.404 → 231 ms | 2 |
| S13b | Calendario: aún crea 88 filas (caché de una página) con ~9 `ui:Button` por fila | ✅ | 2 |
| S14 | Sincro por carpeta: vaciado al salir o cerrar y atasco del canal | ✔️ resuelto. v2.1.3: al vaciar ya no se leen los archivos de los demás. v2.1.4, causa real: al compactar, la posición publicada retrocedía hasta el dispositivo más atrasado (uno abandonado la dejaba en 0) y la carpeta republicaba lo mismo sin llegar nunca a lo reciente; ahora conserva los últimos 2.000 cambios y no retrocede. PC 16.192 → 55.097 de 55.097 en 40 s; envío del móvil al salir 3,7 s → 5 ms; en reposo la carpeta queda quieta | 3 |
| S15 | Windows arranque: los 117 disparadores se rehacían con ~240 commits | ✔️ parcial: 131 → 57 ms; bloqueo al arrancar 625 → 548 ms (el resto es WPF/JIT: S10) | 2 |
| S16 | Sincro por carpeta: el lector solo notaba que otro dispositivo había compactado su archivo si tenía menos líneas de las ya leídas; si volvía a crecer antes de leerlo, se saltaban líneas (cambios) | ✔️ v2.1.5: guarda también la huella (SHA-256 abreviado) de la última línea leída (`ArchivoHuella::`, estado local) y, si no coincide, relee desde el principio. Probado alterando la huella: releyó una vez (12 confirmaciones), contador sin cambios, huella restaurada y sin bucle | 3 |
| S17 | Sincro por carpeta: en cada pasada (cada 6 s con la app abierta) se leía entero el archivo de cada dispositivo aunque no hubiera cambiado | ✔️ v2.1.6: el listado trae tamaño y fecha de cada archivo (Windows: `DirectoryInfo`; Android: la misma consulta SAF) y solo se lee si cambia; relectura de seguridad cada 2 min. PC: cada pasada leía 1.277 KB, asignaba 6,4 MB y provocaba una recolección completa; CPU en reposo 1,15 % → 0,65 %. Emulador: 2.904 KB y 151-278 ms por pasada → 0 lecturas sin cambios | 3 |
| S18 | Android: desde el 19/10 (commit `6125493`) no hay forma de elegir la carpeta compartida en una instalación nueva (se quitó el selector de Configuración); los móviles que ya la tenían la conservan | pendiente de decidir con el usuario (comprobado en el emulador) | 4 |
| S19 | Android, instalación nueva sin base importada: Configuración muestra «no such table: Configuracion» | pendiente (visto en el emulador) | 4 |

Otros riesgos: 504 `catch {}` vacíos y 383 `async void` (desde la v2.1.3 hay manejador global: `RegistroErrores` en ambas apps); posibles `Id` duplicados entre dispositivos (❓); sincronización heredada (AutoSync y fusión) aún en Android; seguridad: APK firmado con la clave de depuración, `allowBackup=true`, clave de sincronización de 8 dígitos.

## Plan por bloques

| Bloque | Contenido | Estado |
|---|---|---|
| 0 | Medición: vigilante de bloqueos, recorrido de medición y línea base | hecho (31/10) |
| 1 | Lag rápido: S1, S7, S8 | hecho (31/10) |
| 2 | Pantallas pesadas: S13, S2, S3, S15 (v2.1.2) y S4 (v2.1.3) hechos; pendientes S2b, S13b y S9 | en curso |
| 3 | Sincronización y BD: S5 (v2.1.3), S14 con el atasco de la carpeta (v2.1.3—2.1.4), S16 (v2.1.5) y S17 (v2.1.6) hechos; pendientes S6, poda de borrados, `Id` duplicados e índices solo si se justifican (con copia) | en curso |
| 4 | Estabilidad: manejador global de errores hecho (v2.1.3); pendientes `catch {}` críticos, reglas comunes en SincroCompartido con pruebas y retirar la sincro heredada | en curso |
| 5 | CPU y memoria: S10 (ReadyToRun), S11, S12 | pendiente |
| 6 | Visual: estilos comunes, componentes, accesibilidad y escalado; pantalla a pantalla | pendiente |
| 7 | Pruebas: banco de consola versionado para reglas y sincro sobre una copia de la BD | pendiente |

## Mediciones

### Línea base — 31/10/2026 (Release; Windows: mediana de 3 pasadas; Android: Galaxy Z Fold7, pantalla exterior)

| Medida | Resultado |
|---|---|
| Windows: arranque (ventana) / bloqueo al arrancar | 263 ms / 625 ms |
| Windows: bloqueo al abrir Calendario / Visitas / Personas | 1.404 / 483 / 387 ms |
| Windows: 1ª → 2ª apertura | Circuito 401 → 145; Correspondencia 200 → 140; Archivo 242 → 163; Asambleas 240 → 198 |
| Android: arranque en frío | 415 ms; sin bloqueos |
| Android: abrir la visita / volver a Ministerio | bloqueo 653 / 315 ms |
| Android: desplazar la lista | 5,98 % con tirones; p90 7 ms; p99 48 ms |
| Android: salir a segundo plano | bloqueo 6,1 s (envío sin terminar) |

### Después (v2.1.2) — 31/10/2026

| Medida | Antes | Después |
|---|---|---|
| Android: salir a segundo plano | 6,1 s | 0 ms; el envío termina en segundo plano en 4,0–4,4 s |
| Android: abrir la visita / cambio de pestaña | 653 / 315 ms | 264 / 240 ms |
| Android: desplazamiento con tarjetas ya creadas | p90 7 ms | p90 5 ms; p99 44 ms |
| Windows: Calendario | 1.404 ms | 231 ms (88 filas creadas de 210) |
| Windows: Personas | 387 ms de bloqueo; apertura ~450 ms | sin bloqueo; ~220–250 ms |
| Windows: Visitas | 484 ms en 6 de 6 aperturas | ninguno > 200 ms en 4 de 6 (208 y 305 ms en las otras) |
| Windows: Territorios (al abrir su pestaña) | — | 305 ms |
| Windows: preparación de la sincro al arrancar / bloqueo al arrancar | 131 / 625 ms | 57 / 548 ms |

## Registro de cambios

- 31/10/2026 · Bloque 0: vigilante de bloqueos y registro en archivo (`Perf.cs` en ambas apps), recorrido de medición en `MainWindow` (`SCCOSMOS_PERF`), medidas `Ciclo.*` en Android.
- 31/10/2026 · S1: el vaciado al salir se lanza en segundo plano (`SincronizacionMobile`, `App.OnSleep`, `MainActivity.OnStop`).
- 31/10/2026 · S7: `InicializarDatabase` asegura el esquema una vez por ejecución; los parches de datos siguen en cada llamada.
- 31/10/2026 · S8: `PersonasPage` se suscribe a `ProgresoCambiado` al cargarse y se da de baja al descargarse.
- 31/10/2026 · S13: `TablaRuta` con `CanContentScroll="True"` y `ScrollUnit="Pixel"`; `AjustarFilaArriba` coloca la semana con su posición real.
- 31/10/2026 · S2: Visitas de Android con Ministerio fuera de los `ScrollView` (lista virtualizada) y panel Datos fijo.
- 31/10/2026 · S3: `VisitasPage` carga Territorios al abrir su pestaña (`_territoriosPendientes`) y mide `Visitas.Territorios`. Se probó y se revirtió una reestructuración de la tabla (sin mejora medible).
- 31/10/2026 · S15: `SincroAlmacen.Asegurar` rehace los disparadores en una sola transacción; `SincronizacionRed` anota `Error.Sincro.Iniciar`.
- 31/10/2026 · `ScrollExteriorPagina`: el Calendario desactiva y restaura el scroll exterior de WPF-UI sin pisar a otras páginas.
- 31/10/2026 · Publicada la v2.1.2 (GitHub) e instalada en el PC; móvil pendiente de conectar.
- 31/10/2026 · S14: `CanalArchivosSincro.PublicarPendientesAsync` (solo escribir) y `ServicioSincro.VaciarPendientesAsync` sin leer a los demás ni reintentar si no hay nada.
- 31/10/2026 · S5: `SincroAlmacen.MarcaCambios` y marca por conexión y en el canal por carpeta (consulta forzada cada 30 s).
- 31/10/2026 · Errores: `RegistroErrores` (Windows: `App.OnStartup`; Android: `MainApplication`). Prueba con `SCCOSMOS_PRUEBA_ERROR=1`.
- 31/10/2026 · S4: `SincronizacionPaginas` muestra el aviso `AvisoCambios` de `MainWindow` en lugar de recargar.
- 31/10/2026 · Instalación del PC: la carpeta (sincronizada) tenía restos de una instalación autocontenida de junio (`hostfxr.dll`…) que impedían arrancar; 245 archivos movidos a `%LOCALAPPDATA%\SCCosmos\copia_instalacion_20261031`. Si vuelve a fallar con «Failed to load hostfxr.dll», revisar sobrantes .dll en la raíz.
- 31/10/2026 · Atasco de la carpeta: `CanalArchivosSincro.CompactarAsync` ya no hace retroceder `ArchivoSeqEscrito`, ignora los dispositivos desmarcados y conserva como mucho `VentanaCarpeta` (2.000) cambios ya publicados (quita lotes antiguos si pasa de 1 MB); `EscribirPendientesAsync` no publica lo anterior a la ventana (`SincroAlmacen.ObtenerUltimoSeq`). Límite: un dispositivo que se quede más de ~2.000 cambios atrás y solo use la carpeta se pone al día por la red (antes tampoco llegaba a lo reciente).
- 01/11/2026 · Cierre de Windows: `MainWindow_Closing` llamaba a `Close()` dentro del propio evento cuando el vaciado terminaba al instante (InvalidOperationException; desde la 2.1.3 el registro de errores la capturaba, avisaba y la ventana no se cerraba). Arreglo: `await Task.Yield()` antes de seguir.
- 01/11/2026 · Publicada la v2.1.4.
- 01/11/2026 · S16: `CanalArchivosSincro.LeerParesAsync` guarda `ArchivoHuella::<archivo>` (huella de la última línea leída) y relee desde el principio si cambia; la primera vez toma la referencia sin releer. Copia de la BD de la prueba: `%LOCALAPPDATA%\SCCosmos\copia_prueba_S16_20261101`.
- 01/11/2026 · Publicada la v2.1.5.
- 01/11/2026 · S17: `ICanalArchivosSincro.ListarConFirmasAsync` (tamaño:fecha de cada archivo; `CanalArchivosPc` con `DirectoryInfo`, `CanalArchivosMobile` con `SyncFolderService.ListarConFirmas`, una sola consulta SAF); `LeerParesAsync` no lee los archivos cuya firma no cambió (relectura de seguridad cada 2 min) y anota `Carpeta.Lectura` en Perf solo cuando lee (enganche `CanalArchivosSincro.Medicion`). Probado en el PC y en el emulador `sccosmos_test` (carpeta de prueba `Documents/PruebaSCCosmos`, elegida con un enganche temporal por adb ya retirado).
- 01/11/2026 · Publicada la v2.1.6.

### v2.1.3 — 31/10/2026 (Windows; antes = 2.1.2 instalada, dos rondas alternas)

| Medida | Antes | Después |
|---|---|---|
| Cierre de la ventana (incluye el vaciado) | 737–755 ms | 568–588 ms |
| CPU en reposo (60 s) | 1,07–1,15 % | 1,30–1,33 % (sin explicar; a analizar) |
| Error provocado (`SCCOSMOS_PRUEBA_ERROR=1`) | la app se cerraba | anotado con su pila en `errores.log` y la app sigue |

Móvil (v2.1.3, 31/10/2026): salir a segundo plano 0 ms (sin bloqueo); envío en segundo plano 3.576–3.934 ms (antes 4.031–4.403); arranque de la sincro 1.820 ms (antes ~3.300); sin errores anotados; el PC conecta con el móvil (192.168.1.39:47311). v2.1.3 instalada en el móvil y en el PC.

### v2.1.4 — atasco de la carpeta y cierre de Windows (31/10 y 01/11/2026)

| Medida | Antes | Después |
|---|---|---|
| PC: posición publicada en la carpeta / contador | 16.192 / 55.097 (retrocedía al compactar) | 55.097 / 55.097 a los 40 s |
| Móvil: envío al salir (segundo plano) | 3.576-3.934 ms | 5 ms (3 de 3) |
| Reposo 60 s con PC y móvil abiertos: líneas nuevas en la carpeta | PC +6 en 40 s (respondiendo al bucle del móvil) | PC 0; móvil 2 confirmaciones |
| Cierre de Windows sin nada pendiente | aviso de error y la ventana no se cerraba | 61-73 ms |
| Errores anotados en el móvil | 0 | 0 |

### v2.1.5 — S16 (01/11/2026, PC con el móvil abierto)

| Prueba | Resultado |
|---|---|
| Primera vez con S16 | huellas de referencia de los 3 dispositivos; sin releer (archivo del PC 26 líneas, contador 55.133, sin cambios) |
| Huella del móvil alterada a propósito | relee una vez: +12 confirmaciones (26 → 38 líneas), contador 55.133 sin cambios, huella restaurada (707126BC···) |
| 30 s después | 38 líneas: sin bucle |
| Errores (PC y móvil) | ninguno |

### v2.1.6 — S17 (01/11/2026)

| Medida | Antes | Después |
|---|---|---|
| PC: lecturas de la carpeta en reposo | 1 cada 6 s (1.277 KB, 6,4 MB de memoria temporal, 12 ms) | 1 al arrancar + 1 de seguridad cada 2 min |
| PC: recolecciones completas de memoria | 1 por pasada (13 en 72 s) | 3 en 2 min |
| PC: CPU en reposo (60 s) | 688 ms (1,15 %) | 391 ms (0,65 %) |
| Emulador Android (x86_64, SAF): cada pasada | 2.904 KB leídos, 151-278 ms | 0 lecturas si nada cambia |
| Cambio en un archivo | — | se lee solo ese archivo en la pasada siguiente (PC y emulador) |
