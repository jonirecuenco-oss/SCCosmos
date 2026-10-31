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
| S14 | Sincro al vaciar: leía los archivos de los demás (~4 s en el móvil) | ✔️ solo publica lo propio; cierre de Windows 746 → 578 ms; Android por medir | 3 |
| S15 | Windows arranque: los 117 disparadores se rehacían con ~240 commits | ✔️ parcial: 131 → 57 ms; bloqueo al arrancar 625 → 548 ms (el resto es WPF/JIT: S10) | 2 |

Otros riesgos: 504 `catch {}` vacíos y 383 `async void` (desde la v2.1.3 hay manejador global: `RegistroErrores` en ambas apps); posibles `Id` duplicados entre dispositivos (❓); sincronización heredada (AutoSync y fusión) aún en Android; seguridad: APK firmado con la clave de depuración, `allowBackup=true`, clave de sincronización de 8 dígitos.

## Plan por bloques

| Bloque | Contenido | Estado |
|---|---|---|
| 0 | Medición: vigilante de bloqueos, recorrido de medición y línea base | hecho (31/10) |
| 1 | Lag rápido: S1, S7, S8 | hecho (31/10) |
| 2 | Pantallas pesadas: S13, S2, S3, S15 (v2.1.2) y S4 (v2.1.3) hechos; pendientes S2b, S13b y S9 | en curso |
| 3 | Sincronización y BD: S14 y S5 hechos (v2.1.3); pendientes S6, poda de borrados, `Id` duplicados e índices solo si se justifican (con copia) | en curso |
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

### v2.1.3 — 31/10/2026 (Windows; antes = 2.1.2 instalada, dos rondas alternas)

| Medida | Antes | Después |
|---|---|---|
| Cierre de la ventana (incluye el vaciado) | 737–755 ms | 568–588 ms |
| CPU en reposo (60 s) | 1,07–1,15 % | 1,30–1,33 % (sin explicar; a analizar) |
| Error provocado (`SCCOSMOS_PRUEBA_ERROR=1`) | la app se cerraba | anotado con su pila en `errores.log` y la app sigue |

