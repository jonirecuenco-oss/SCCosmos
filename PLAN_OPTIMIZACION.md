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
| S2b | Android Visitas: tarjeta de persona pesada (unas 80 vistas), con un `Button` transparente en cada uno de sus 6 meses | ✔️ v2.1.19: toque directo en la casilla, sin botón ni envoltorios (18 vistas menos por tarjeta; 490 → 364 nodos en pantalla). Con 6 rondas por versión alternando en el móvil: primera pasada p99 75 → 57 ms; segunda, tirones 7,6 → 2,6 % y p99 59 → 43 ms. 0 píxeles distintos; cada casilla abre su mes, también en tarjetas recicladas | 2 |
| S3 | Windows Visitas: al abrir se cargaban todas las pestañas; Territorios costaba 300–380 ms. (La tabla no era el problema: solo muestra el grupo elegido, ~18 filas.) | ✔️ bloqueo 484 ms → ninguno en 4 de 6 aperturas | 2 |
| S4 | Windows: Visitas, Asambleas y Correspondencia se recreaban al llegar cambios de otro equipo | ✔️ ahora aviso «Hay cambios de otro dispositivo · Actualizar» (sin probar con cambios reales) | 2 |
| S5 | Sincro: cada 1 s por par (y cada 6 s en la carpeta) se recorría `SCCosmos_Cambios` aunque no hubiera cambios | ✔️ marca barata (fecha de escritura de la BD y su WAL) + consulta forzada cada 30 s; sin cambio de esquema. CPU en reposo del PC sin mejora medible (1,1 % → 1,3 %, pendiente de analizar); en el móvil, por medir | 3 |
| S6 | Guardados borrar + reinsertar (D 8.642 / I 5.716 / U 693 en el registro); borrados nunca podados | ✔️ v2.1.7: `ActividadPersonas` (57 % de los cambios), guardar una ficha sin cambios 48 → 0. v2.1.8: instantánea mensual de `Personas`, 2 → 1. v2.1.9: importación de territorios en Windows, reimportar igual 1.940 → 0. v2.1.10: programa de la visita en Windows, reimportar igual 56 → 0. v2.1.11: asambleas (partes y organigrama, 94 → 0 al guardar sin cambios) y reunión de precursores (sesiones y partes, 28 → 0). v2.1.24: RSC en Windows y Android con `RscGuardado` (SincroCompartido): cada registro conserva su `Id` y solo se escribe lo que cambia. Con la regla anterior, guardar lo mismo generaba 20 cambios y dos dispositivos guardando a la vez dejaban 8 asistentes y 4 grupos en vez de 4 y 2; ahora 0 cambios y sin duplicados (pruebas 20 a 22, y las llamadas reales de cada app sobre copias de la base con datos inventados; sin probar desde la pantalla, porque escribiría en la base real). v2.1.22: importación de territorios en Android, con la regla de S20. v2.1.26: importación del programa en Android, reimportar igual — 0 cambios. Poda de borrados analizada el 02/11 y sin cambios: 8.709 borrados, casi todos de este mes; podarlos arriesga resucitar filas en un dispositivo que vuelva tras mucho tiempo y apenas ahorra | 3 |
| S7 | `InicializarDatabase()` completo (~100 ms) en cada `ObtenerCongregaciones` | ✔️ Personas 450 → 220 ms | 1 |
| S8 | `PersonasPage` no se daba de baja de `ProgresoCambiado` | ✔️ | 1 |
| S9 | Circuito, Asambleas y Correspondencia: primera apertura 200—400 ms | ✔️ Analizado (01/11): los datos tardan menos de 10 ms; el tiempo es construir y dibujar la página (Circuito 450 ms solo la primera vez, por ser la primera tabla de la sesión). Sin cambios: mantener páginas en memoria alteraría el refresco y el aviso de cambios (S4), y aligerar el XAML tiene riesgo visual | 2 |
| S10 | Windows sin ReadyToRun: el código de la app se compilaba al usarse por primera vez | ✔️ v2.1.15: publicación win-x64 precompilada (solo en el script de publicación, no en el proyecto). Arranque hasta la ventana 177 → 156 ms, bloqueo del arranque ~550 → ~500 ms, bloqueos totales —18 %, primera apertura de páginas —3 %; ZIP 87,1 → 60,5 MB. Selenium (Firefox) encuentra `selenium-manager.exe` en la raíz: comprobado | 5 |
| S11 | `Thread.Sleep` fijos en Selenium (6—51 s; en segundo plano) | Analizado (02/11), sin cambios: 144 esperas fijas en los flujos contra webs externas (JW Hub, stream, envíos). Cambiarlas exige probar contra esas webs con la sesión del usuario y no bloquean la interfaz. Pendiente de poder probarlo con esa sesión | 5 |
| S12 | Fotos sin `DecodePixelWidth` / `Freeze` / caché | ✔️ Analizado (02/11), sin cambios: 479 fotos actuales, mediana 128×171. Decodificar las 46 de la congregación mayor cuesta 16,8 ms en total y con `DecodePixelWidth` 19,7—22,4 ms: más lento. Solo 2 fotos son grandes (la mayor, 6 ms y 3,9 MB) | 5 |
| S13 | Windows Calendario sin virtualizar: se creaban las 210 semanas | ✔️ 1.404 → 231 ms | 2 |
| S13b | Calendario: creaba 88 filas (caché de una página por arriba y por abajo) con ~9 `ui:Button` por fila, sin reciclar | ✔️ v2.1.16: `VirtualizationMode=Recycling` y caché de 6 filas por lado. Bloqueo al dibujar 204—247 ms → ninguno por encima de 200 ms; segunda navegación 488—516 → 316—322 ms. Comprobado con capturas: misma vista y misma posición de la semana actual, sin filas en blanco al desplazarse | 2 |
| S14 | Sincro por carpeta: vaciado al salir o cerrar y atasco del canal | ✔️ resuelto. v2.1.3: al vaciar ya no se leen los archivos de los demás. v2.1.4, causa real: al compactar, la posición publicada retrocedía hasta el dispositivo más atrasado (uno abandonado la dejaba en 0) y la carpeta republicaba lo mismo sin llegar nunca a lo reciente; ahora conserva los últimos 2.000 cambios y no retrocede. PC 16.192 → 55.097 de 55.097 en 40 s; envío del móvil al salir 3,7 s → 5 ms; en reposo la carpeta queda quieta | 3 |
| S15 | Windows arranque: los 117 disparadores se rehacían con ~240 commits | ✔️ parcial: 131 → 57 ms; bloqueo al arrancar 625 → 548 ms (el resto es WPF/JIT: S10) | 2 |
| S16 | Sincro por carpeta: el lector solo notaba que otro dispositivo había compactado su archivo si tenía menos líneas de las ya leídas; si volvía a crecer antes de leerlo, se saltaban líneas (cambios) | ✔️ v2.1.5: guarda también la huella (SHA-256 abreviado) de la última línea leída (`ArchivoHuella::`, estado local) y, si no coincide, relee desde el principio. Probado alterando la huella: releyó una vez (12 confirmaciones), contador sin cambios, huella restaurada y sin bucle | 3 |
| S17 | Sincro por carpeta: en cada pasada (cada 6 s con la app abierta) se leía entero el archivo de cada dispositivo aunque no hubiera cambiado | ✔️ v2.1.6: el listado trae tamaño y fecha de cada archivo (Windows: `DirectoryInfo`; Android: la misma consulta SAF) y solo se lee si cambia; relectura de seguridad cada 2 min. PC: cada pasada leía 1.277 KB, asignaba 6,4 MB y provocaba una recolección completa; CPU en reposo 1,15 % → 0,65 %. Emulador: 2.904 KB y 151-278 ms por pasada → 0 lecturas sin cambios | 3 |
| S18 | Android: no hay selector de la carpeta compartida en Configuración (quitado el 19/10, commit `6125493`) | intencionado: el usuario no quiere elegir la carpeta a mano; no se cambia | — |
| S19 | Android, instalación nueva sin base importada: Configuración muestra «no such table: Configuracion» | pendiente (visto en el emulador) | 4 |
| S20 | Windows: cada importación de territorios dejaba en blanco `NombreVinculado` en toda la congregación y el enlace automático los recalculaba al ver la tabla, así que se perdían los puestos a mano (110 hoy). Android los conservaba por nombre de territorio, lo que habría mezclado los de 212 territorios con varios vínculos. Además, la tabla solo guardaba la primera fila de cada nombre (615 filas se muestran sin vínculo guardado y no cuentan como devueltos) y podía sustituir un vínculo puesto a mano al mirar otro año | ✔️ v2.1.22: `TerritoriosImportados` (SincroCompartido), la misma regla en Windows y Android: conserva el vínculo de la misma asignación y, si es nueva, el de esa persona cuando es único. `CrearCeldaVuelta` solo guarda si la fila no tiene vínculo. Pruebas 18 y 19. Sin probar en real: esa rama de la tabla con filas pendientes y la importación desde el móvil | 3 |
| S21 | Fusión de duplicados (Windows, `FusionarYBorrarDefinitivo`): la actividad de la persona de origen se traspasaba con un único UPDATE; si las dos tenían actividad del mismo mes, la restricción UNIQUE lo hacía fallar entero (error oculto) y, al borrar la persona de origen, toda su actividad se perdía en cascada | ✔️ v2.1.14: se traspasan solo los meses que no tiene la persona que se conserva (comparando sin mayúsculas ni espacios); en los repetidos queda el suyo. Prueba con el bloque real en 5 casos: con 2 repetidos + 2 propios, antes se perdían los 2 propios y ahora pasan | 4 |
| S22 | Parches de datos (2 UPDATE + 1 DELETE) en cada `InicializarDatabase`, que también se llama al pedir las congregaciones (20 sitios en Windows): toman el bloqueo de escritura aunque no corrijan nada y esperan si la sincronización está escribiendo | ✔️ v2.1.17: lectura previa (`HayParchesPendientes`) y solo se escribe si hace falta. 6,3 → 3,2 ms por llamada; con otra conexión escribiendo, 3,3 s y 3 errores → 4 ms y ninguno (prueba con espera de 1 s por sentencia; la app espera hasta 30 s) | 3 |
| S23 | Red: un dispositivo nuevo (o reinstalado) recibía el registro entero (23.167 cambios) a un lote de 200 por segundo, y lo escrito entretanto iba detrás | ✔️ v2.1.18: el siguiente lote sale cuando el otro confirma haber aplicado el anterior (se comprueba cada 100 ms; si la confirmación no llega, un lote por segundo como antes). 117 → 16—17 s en el PC, con los datos ya presentes en el receptor; protocolo sin cambios. En Android las alertas se recalculan agrupadas (2 s) y no con cada lote. Sin medir en el móvil | 3 |
| S24 | Android Inicio: bloqueos de 225—417 ms al abrir (7 en dos días de uso real según el registro del móvil; `Inicio` mediana 308 ms, máximo 881 ms). Es la pantalla que más se abre. Hipótesis: S25 los agravaba (114 MB en memoria en cada arranque) | ✅ Tras S25, 8 arranques en frío: `Inicio` 147—324 ms y entre 1 y 3 bloqueos de 278 ms (el mismo valor en 3 rondas: puede ser una línea repetida). No concluyente; revisar con el registro de uso real | 2 |
| S25 | Android: el código antiguo de sincronización sigue activo en cada arranque en frío. `App.xaml.cs` llama a `ImportarSiHayCambiosAsync` antes de `SincronizacionMobile.Iniciar`, que es quien activa `NuevaSincroActiva`, así que sus protecciones no actúan. Lee las 4 copias de conflicto (114 MB), llama a la fusión sobre la base real y crea copias de seguridad de 32 MB; 2,3 s en cada arranque (mediana de 51). La fusión solo falla porque falta `SCCosmos_RegistroCambios` en ese instante. Si la app muere entre `AutoSyncService.Iniciar` (la crea) y `SincroAlmacen.Asegurar` (la borra), la siguiente vez se ejecuta: unas 390 filas resucitadas y 190 revertidas por copia, propagadas a los demás. `sccosmos_fusion.db` del 01/11 23:51 indica que ya estuvo cerca. Sin daño: 37 de 39 tablas idénticas al PC | ✔️ v2.1.20 (fase 0): puerta cerrada desde el inicio del proceso y sin crear el registro antiguo. `Arranque.SyncDiferido` 2.329 → 210—247 ms; sin copias `antes_de_fusion` nuevas. Fase 0b: `sccosmos.db` y las 4 copias de conflicto fuera de la carpeta | 4 |
| S26 | Motor: los binarios se envían en base64 (`SincroMensajes`) y nada los devolvía a binario al recibir, así que quedaban como texto. Afectaba a `Documentos.ContenidoArchivo`, la única columna `BLOB`; en el móvil, al S-303 Id 90. Ambas apps leen con `(byte[])reader.GetValue`, que con texto lanzaría `InvalidCastException` | ✔️ v2.1.21: `EscribirFila` restaura el binario según el tipo de la columna y `Asegurar` repara lo existente sin anotarlo como cambio. Protocolo sin cambios. Pruebas 16 y 17 (fallaban antes del arreglo). En el móvil real: 0 celdas distintas de 551 | 4 |
| S27 | `Congregaciones` Id 17: el PC tenía correo de AV (`IdZoom` y `ClaveZoom` vacíos) y el móvil NULL en las tres. Causa probable: el móvil editó la fila el 25/10 antes de tener esas columnas y no había vuelto a cambiar | ✔️ 02/11: fila reenviada desde el PC con un `UPDATE` que no cambia datos; comprobado en el móvil real | 4 |
| S28 | Windows y Android: `GuardarConfiguracion` usaba `INSERT OR REPLACE`, así que guardar una clave con el mismo valor se anotaba como cambio y viajaba a los demás dispositivos. Abrir Correspondencia en Windows reescribe 14 claves `Asunto_*` cada vez | ✔️ v2.1.23: si el valor no cambia no se escribe. Recorrido completo de la app sobre la base real: 28 cambios → 0. En una copia: clave nueva y valor distinto se siguen anotando | 3 |
| S29 | Motor: SQLite da a cada alta el mayor `Id` que conoce más uno, así que dos dispositivos que dan de alta sin verse eligen el mismo y, al sincronizarse, la fila más reciente sustituye a la otra, que se pierde sin aviso (y, en `Personas`, sus datos asociados pasarían a la otra). Reproducido en el banco con donaciones (pruebas 23 a 25). En los datos reales no hay indicios de pérdida: las 20 donaciones añadidas siguen ahí y las 5 tareas sin fila coinciden con 5 borrados | ✔️ v2.1.25: los móviles dejan un hueco de 100.000—199.900 `Id`, propio de cada dispositivo, antes de su siguiente alta; lo amplían al arrancar, al recibir y antes de enviar (`SaltarIds`, sobre `sqlite_sequence`, que no se sincroniza). El PC numera como siempre. Sin cambios en el protocolo ni en los puntos de alta. Límites: una ráfaga de 100.000 altas en una tabla sin sincronizar, y un móvil con una versión anterior sigue numerando como antes. En el emulador: secuencia 146.200, alta siguiente 146.201 (deshecha), 0 cambios en el registro. Sin comprobar en el móvil real | 3 |

Otros riesgos: 504 `catch {}` vacíos y 383 `async void` (desde la v2.1.3 hay manejador global: `RegistroErrores` en ambas apps); sincronización heredada (AutoSync y fusión) aún en Android; seguridad: APK firmado con la clave de depuración, `allowBackup=true`, clave de sincronización de 8 dígitos.

## Plan por bloques

| Bloque | Contenido | Estado |
|---|---|---|
| 0 | Medición: vigilante de bloqueos, recorrido de medición y línea base | hecho (31/10) |
| 1 | Lag rápido: S1, S7, S8 | hecho (31/10) |
| 2 | Pantallas pesadas: S13, S2, S3, S15 (v2.1.2), S4 (v2.1.3), S13b (v2.1.16) y S2b (v2.1.19) hechos; S9 analizado sin cambios; pendiente S24 (Inicio de Android) | en curso |
| 3 | Sincronización y BD: S23 (v2.1.18), S22 (v2.1.17), S5 (v2.1.3), S14 con el atasco de la carpeta (v2.1.3—2.1.4), S16 (v2.1.5) y S17 (v2.1.6) hechos; S6 en curso (actividad, personas, territorios, programa de la visita y asambleas en v2.1.7—2.1.11; RSC en v2.1.24); `Id` duplicados entre dispositivos resueltos (S29, v2.1.25); poda de borrados analizada y sin cambios (S6); pendientes índices solo si se justifican (con copia) | en curso |
| 4 | Estabilidad: manejador global de errores (v2.1.3); errores del motor de sincronización (v2.1.12) y de guardados de Windows y sincronización de Android (v2.1.13) anotados; pérdida de actividad al fusionar duplicados corregida (S21, v2.1.14); pendientes reglas comunes en SincroCompartido con pruebas y retirar la sincro heredada | en curso |
| 5 | Compilación: ReadyToRun en Windows hecho (S10, v2.1.15) | hecho |
| 6 | Visual: estilos comunes, componentes, accesibilidad y escalado; pantalla a pantalla | pendiente |
| 7 | Pruebas: banco `SincroPruebas` en el repositorio (02/11): 26 pruebas de extremo a extremo del motor sobre copias de la BD (red A/B/C y un dispositivo nuevo; carpeta D/E con el `CanalArchivosPc` real). Comprobado que detecta los fallos que protege: sin la huella de S16 falla la 12 y con el atasco original falla la 14. Unos 2,5 min, 20 rondas seguidas estables. `publicar_version.ps1` lo ejecuta antes de publicar si cambió el código de la sincronización y no publica si falla (`Herramientas\probar_sincro.ps1`; escape `-SinPruebas`). Pendiente: las reglas de `DatabaseHelper` (necesitan poder elegir la ruta de la base) | en curso |

## Retirada de la sincronización heredada (plan, 02/11/2026)

Solo afecta a Android: `AutoSyncService` (905 líneas), `FusionBaseDatosService` (1.440), `DatabaseSyncService` (312) y los métodos de la base de `SyncFolderService`. En Windows solo queda la copia inicial de un uso en `DatabaseHelper.RutaBaseDatos`.

Datos medidos: el registro nuevo está completo (39 tablas, 14.321 filas, 0 sin entrada). Android no sabe crear 20 de sus 39 tablas y desde el 19/10 no hay selector de carpeta, así que hoy un dispositivo nuevo no se puede preparar (S19). `EliminarArchivo` nunca borra (`ContentResolver.Delete` sobre una URI de documento), por eso las copias de conflicto siguen ahí.

| Fase | Qué | Riesgo | Autorización |
|---|---|---|---|
| 0 | Cerrar la puerta: `NuevaSincroActiva` activa desde el arranque y no crear el registro antiguo | Bajo | ✔️ Hecha (v2.1.20) |
| 0b | Sacar `sccosmos.db` y las 4 copias de conflicto de la carpeta compartida. Copia verificada por SHA-256 en `%LOCALAPPDATA%\SCCosmos\copia_base_carpeta_20261102`. Syncthing lo propagó al móvil en 71 s (allí quedan en `.stversions` hasta que se limpien) | Bajo; reversible | ✔️ Hecha (02/11) |
| 1 | Quitar el código muerto (unas 2.600 líneas). Antes, mover los avisos de tareas (`VerificarAsync`, cada 30 s) a su propio temporizador: hoy viven en el bucle de `AutoSyncService`. Conservar `MigrarDesdeCarpetaAsync` e `ImportarAsync`. Borrar en el móvil `sccosmos_fusion.db` y las copias `antes_de_fusion` (unos 128 MB) | Medio: toca el arranque | A partir del 09/11, una semana con la fase 0 |
| 2 | Dispositivo nuevo sin copia de la base: incluir el esquema de las 39 tablas en la app (resuelve S19) y recibir todo por red (S23). Añadir al banco una prueba con un dispositivo vacío. El esquema debe crearse antes de la primera sincronización: las filas de una tabla que no existe se descartan y no se vuelven a enviar | Medio | Decisión del usuario |

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
- 01/11/2026 · S6, 1.ª parte: `DatabaseHelper.ReemplazarActividadPersona` (guardado de la ficha de persona) ya no borra y reinserta toda la actividad: borra solo los meses que ya no están, actualiza los que difieren (compara SQLite con `IS NOT`, con los mismos tipos que al guardar) e inserta los nuevos; un mes repetido sigue fallando por UNIQUE. Prueba de equivalencia con el método antiguo (copiado de git) sobre dos copias de la base, en un proyecto desechable en `%TEMP%\prueba_s6`: mismo resultado final en 10 casos.
- 01/11/2026 · Publicada la v2.1.7.
- 01/11/2026 · S6, 2.ª parte (Personas): `ActualizarPersonaDesdeVisitas` ya no borra y reinserta la instantánea del mes; el nuevo `ReemplazarInstantaneaMes` actualiza la que hay (conserva su Id) y reproduce el resto: borra las duplicadas, borra la actividad que colgara de ella (como hacía la cascada) y, sin ficha raíz, borra las del mes sin crear otra. Prueba de equivalencia (bloque antiguo de git contra el nuevo, dos copias de la base, `%TEMP%\prueba_s6_personas`): 6 casos y tablas completas iguales.
- 01/11/2026 · Publicada la v2.1.8.
- 01/11/2026 · S6, 3.ª parte (Territorios, Windows): `GuardarTerritoriosImportados` reutiliza los registros de la congregación en orden de Id (el i-ésimo importado ocupa el i-ésimo existente; los que sobran se borran y los que faltan se añaden al final) y solo actualiza lo que difiere; se conserva el resto del comportamiento (fecha de creación por nombre, `NombreVinculado` en blanco, `FechaEliminacion` nula). Prueba de equivalencia sobre dos copias (`%TEMP%\prueba_s6_territorios`): 10 casos y tabla completa iguales y en el mismo orden.
- 01/11/2026 · Publicada la v2.1.9.
- 01/11/2026 · S6, 4.ª parte (programa de la visita, Windows): `ReemplazarProgramaMesVisita` (PDF) e `ImportarProgramaVisita` (al reemplazar) reutilizan las filas del mes en orden de Id y solo actualizan lo que difiere; se conserva lo demás (sin transacción y mismo total en el PDF; transacción y modo añadir en la importación). Única diferencia, a propósito y solo en errores: el PDF lee la lista de eventos antes de tocar la base, así un fallo al leerla ya no borra el mes. Prueba de equivalencia (`%TEMP%\prueba_s6_programa`): 12 casos y tabla completa iguales y en el mismo orden.
- 01/11/2026 · Publicada la v2.1.10.
- 01/11/2026 · S6, 5.ª parte (asambleas, Windows): `GuardarProgramaAsamblea` (partes y organigrama) y `GuardarReunionPrecursores` (sesiones y, dentro de cada una, sus partes) reutilizan las filas en orden de Id con los nuevos ayudantes `IdsEnOrden` y `BorrarFilasSobrantes`; las partes de precursores reutilizadas quedan sin `ReunionId`, como las insertadas. Efecto visible: en Android, la lista de asignaciones de todas las asambleas (ordenada por Id) ya no cambia de orden al guardar una asamblea en el PC. Prueba de equivalencia (`%TEMP%\prueba_s6_asambleas`): 14 casos y tabla de semanas iguales y en el mismo orden.
- 01/11/2026 · Publicada la v2.1.11.
- 01/11/2026 · S6, RSC: analizado y sin cambios (tablas vacías y sin tráfico desde el 19/10).
- 01/11/2026 · Bloque 4, 1.ª parte: nuevo `SincroDiagnostico` (SincroCompartido), enganchado al registro de errores de cada app (`App.OnStartup` y `MainApplication`). 21 `catch` vacíos del motor ahora anotan los errores inesperados sin cambiar el comportamiento: `ServicioSincro` 11 (red, carpeta y vaciado), `SincroAlmacen` 5 (incluido `AplicarLote`, lo recibido de otros dispositivos), `SincroConfiguracion.Cargar` 1 (configuración dañada) y `CanalArchivosSincro` 4. Filtra cortes de red y cancelaciones y anota como mucho 1 vez cada 10 min por sitio. Sin tocar: los de cierre (`Detener`, `Desconectar`, `Dispose`), uno que ya captura solo cancelaciones y los de los conversores de PDF. Prueba del filtro (`%TEMP%\prueba_diag`): 12 casos; PC 60 s y emulador 40 s sin errores ocultos.
- 01/11/2026 · Publicada la v2.1.12.
- 01/11/2026 · Bloque 4, 2.ª parte: `RegistroErrores.AnotarSilencioso` (Windows; 1 apunte cada 10 min por sitio). 12 `catch` de `DatabaseHelper` (copia inicial de la base, 3 parches de datos, 5 de la fusión de duplicados, programa PDF, datos extra de congregación y semanas de asamblea) y 11 de `SincronizacionMobile` (inicio, creación del servicio y del canal por carpeta, migración, aplicar, guardar configuración, reiniciar y vaciado) anotan sin cambiar el comportamiento. Se dejan los de lecturas, migraciones de esquema que fallan a propósito y limpieza. Nota: `RepararActividadPersonas` ejecuta `PRAGMA foreign_keys=OFF` dentro de una transacción, donde SQLite lo ignora; las conexiones de la app mantienen las claves ajenas activas.
- 01/11/2026 · Publicada la v2.1.13.
- 01/11/2026 · S21 (autorizado): en `FusionarYBorrarDefinitivo` el traspaso de actividad solo mueve los meses que no tiene la persona que se conserva (`NOT EXISTS` con `lower(trim(Mes))` y `trim(AnioServicio)`); en los repetidos se queda el suyo. Prueba (`%TEMP%\prueba_s21`): bloque antiguo de git contra el nuevo, con el mismo borrado en cascada de la fusión, en 5 casos; sin actividad huérfana.
- 01/11/2026 · Publicada la v2.1.14.
- 01/11/2026 · Bloque 5 (S10): `publicar_version.ps1` publica Windows en limpio con `-r win-x64 --self-contained false -p:PublishReadyToRun=true -o` (misma carpeta de salida). Medido con el recorrido de medición, 3 rondas alternas: arranque —12 %, bloqueos —18 %, primera apertura —3 %; ZIP —30 % (sin las piezas de otros sistemas). Las librerías nativas (SQLite, SkiaSharp, WebView2) y `selenium-manager.exe` pasan a la raíz; Selenium 4.45 lo encuentra ahí (prueba con su propia búsqueda). En las instalaciones existentes queda la carpeta `runtimes` antigua, sin efecto.
- 01/11/2026 · Publicada la v2.1.15.
- 01/11/2026 · S13b: `CalendarioPage.xaml` (TablaRuta) recicla filas (`VirtualizingPanel.VirtualizationMode=Recycling`) y solo reserva 6 filas fuera de la vista por cada lado (`CacheLengthUnit=Item`, `CacheLength=6,6`). La lógica de ir a la semana calcula la posición con los datos y ajusta con la fila real visible (compatible con filas recicladas). Medido con el recorrido (ReadyToRun, 2 rondas) y comprobado con capturas y automatización de Windows.
- 01/11/2026 · Publicada la v2.1.16.
- 01/11/2026 · S9 analizado con marcas temporales (retiradas después): en Circuito, Asambleas y Correspondencia los datos tardan menos de 10 ms; el coste es construir y dibujar la página. Sin cambios.
- 01/11/2026 · S22: `DatabaseHelper.AplicarParchesDeDatos` comprueba antes con una sola lectura (`HayParchesPendientes`: un recorrido de Personas y los duplicados de recomendaciones) y solo lanza los UPDATE/DELETE si hay algo que corregir; si la lectura falla, se aplican como antes. Se descartó espaciarlos cada 5 minutos porque retrasaría la limpieza de recomendaciones duplicadas. Probado con un banco de consola (`%TEMP%\prueba_parches`) sobre copias de la BD con el texto exacto de las dos versiones, y con el recorrido completo sobre la base real (30 pantallas, sin errores).
- 02/11/2026 · Publicada la v2.1.17.
- 02/11/2026 · Bloque 7: `SincroPruebas` (banco del 16/10, excluido de git) vuelve a compilar y a pasar. Cambios: excluye los conversores (iText); usa el `CanalArchivosPc` real de Windows; copia la base de `%LOCALAPPDATA%` (o la ruta dada) con la API de copia de SQLite; desactiva el descubrimiento UDP de sus dispositivos (antes se anunciaban en la red); da por sincronizados A/B/C al arrancar (si no, el intercambio inicial de 23.167 cambios tapaba las pruebas 1—6); borra sus copias al terminar. Nuevas: 12 (compactación ajena, S16) y 13 (ráfaga de 2.600 cambios; no reproduce el atasco original). Descartado con un experimento que `MarcaCambios` no detecte escrituras de otra conexión (5/5 con y sin pool).
- 02/11/2026 · Uso: `dotnet run --project SincroPruebas -c Release` (13/13, unos 2 min). Ejecutarlo antes de publicar cualquier cambio en `SincroCompartido`.
- 02/11/2026 · Bloque 7: el banco pasa al repositorio (autorizado; se quita `SincroPruebas/` del `.gitignore`, sin datos personales). `Herramientas\probar_sincro.ps1` lo ejecuta si cambió el código de la sincronización desde la última vez que pasó (huella SHA-256 de lo que compila, sin conversores ni el número de versión) y `publicar_version.ps1` no publica si falla, antes de tocar nada (`-SinPruebas` para emergencias). `Herramientas/` sigue fuera del repositorio.
- 02/11/2026 · `publicar_version.ps1` guardado con BOM: sin él, PowerShell 5.1 leía mal sus acentos y los mensajes de commit (`Versión`) y el pie de las notas de cada release salían con caracteres raros. Corregido para las próximas versiones; los anteriores se quedan como están.
- 02/11/2026 · Banco: prueba 14 (atasco real: archivo de más de 2 MB y un dispositivo abandonado; la posición publicada no retrocede y lo posterior llega) y 15 (dispositivo nuevo por red, con datos idénticos a A al terminar). Con el atasco original reproducido a propósito, la 14 falla.
- 02/11/2026 · Banco: corregida una inestabilidad de la prueba 12 (fallaba 2 de cada 11 rondas). Daba un cambio por publicado cuando el archivo crecía, pero D también añade líneas con sus confirmaciones, y a veces se le detenía antes de publicar; no era un fallo del motor. Ahora espera a `ArchivoSeqEscrito`: 20 rondas seguidas sin fallos y sin la huella de S16 sigue fallando. `SINCRO_SOLO=10,11,12` ejecuta solo esas pruebas.
- 02/11/2026 · S23: `ServicioSincro.EmpujarAsync` devuelve si quedan más lotes y espera a la confirmación del anterior; `BucleEmpujeAsync` vuelve a los 100 ms (`PausaPuestaAlDiaMs`) mientras un dispositivo se pone al día. Android: `ActualizacionPaginasMobile.ProgramarAlertas` agrupa el recálculo de alertas.
- 02/11/2026 · Publicada la v2.1.18.
- 02/11/2026 · Publicada la v2.1.18. Instalada también en el móvil real: arranca sin errores y no existe `sccosmos_errores.log` (ningún error registrado desde que hay manejador global).
- 02/11/2026 · S2b: `VisitaDetallePage.xaml` (`ListaPersonas`): cada casilla de mes usa un `TapGestureRecognizer` en su `Border` en vez de un `Button` transparente, sin el `Grid` envoltorio ni el `HorizontalStackLayout` del mes. `MinimumHeightRequest=45` conserva la altura que imponía el botón (44 dp + 1 de margen). Eliminado el `ActividadMes_Tapped` antiguo, sin uso. Probado en el móvil real: cada casilla abre su mes (primera, última y recicladas), al cerrar se sigue en la lista, y foto y nombre abren la persona.
- 02/11/2026 · Publicada la v2.1.19.
- 02/11/2026 · Publicada la v2.1.19.
- 02/11/2026 · S25 hallado con un diagnóstico temporal en el móvil real (no publicado y ya retirado): la fusión se simuló sobre copias y la base real solo se leyó. Comparación móvil-PC con huella de todas las filas: 37 de 39 tablas idénticas y todos los recuentos iguales.
- 02/11/2026 · S25, fases 0 y 0b: `SincronizacionMobile.NuevaSincroActiva` vale `true` desde el inicio del proceso y `AutoSyncService.AsegurarRegistroDeCambios` ya no crea el registro antiguo. Comprobado en el móvil real antes de publicar. Comparación celda a celda de las dos tablas que diferían: 4 celdas de 551, ver S26 y S27.
- 02/11/2026 · Publicada la v2.1.20.
- 02/11/2026 · S26: `SincroAlmacen.EscribirFila`, `RestaurarBinario` y `RepararBinarios`; pruebas 16 y 17 del banco, que primero fallaron con el motor anterior. S27: fila reenviada desde el PC.
- 02/11/2026 · Publicada la v2.1.21.
- 02/11/2026 · S20: `TerritoriosImportados` compartido y `GuardarVinculoAutomatico` en `VisitasPage`; pruebas 18 y 19. Medido sobre la base real: de 957 vínculos, 110 se habrían perdido al reimportar.
- 02/11/2026 · Publicada la v2.1.22.
- 02/11/2026 · S28: `GuardarConfiguracion` en Windows y Android, sin escribir si el valor no cambia.
- 02/11/2026 · Publicada la v2.1.23.
- 02/11/2026 · S6, RSC: `RscGuardado` compartido, también sin reescribir `RSC_Reuniones` si no cambia; pruebas 20, 21 y 22 del banco, que fallaron simulando la regla anterior.
- 02/11/2026 · Publicada la v2.1.24.
- 02/11/2026 · S29: `SaltarIds` en `SincroAlmacen`, solo en Android; pruebas 23, 24 y 25 del banco, que fallaron con el motor anterior, y 26.
- 02/11/2026 · Publicada la v2.1.25.
- 02/11/2026 · S6, importación del programa en Android: reutiliza las filas del mes, como Windows. S11, S12 y poda de borrados: analizados y sin cambios.
- 02/11/2026 · Publicada la v2.1.26.

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

### v2.1.7 — S6, actividad de personas (01/11/2026; copias de la base real, persona con 24 meses)

| Caso | Antes (cambios a sincronizar) | Ahora | Resultado final |
|---|---|---|---|
| Guardar la ficha sin cambios | 48 (24 borrados + 24 altas) | 0 | igual, `Id` conservados |
| Cambiar las horas de un mes | 48 | 1 | igual |
| Quitar un mes / añadir uno | 47 | 1 | igual |
| Un mes guardado con mayúscula | 48 | 2 | igual |
| Mes repetido en la lista | error, sin cambios | error, sin cambios | igual |
| Vaciar la actividad / volver a ponerla | 24 / 24 | 24 / 24 | igual |
| Otras 2 personas, guardar sin cambios | 48 | 0 | igual, `Id` conservados |

### v2.1.8 — S6, instantánea mensual de Personas (01/11/2026; copias de la base real)

| Caso | Antes (cambios a sincronizar) | Ahora | Resultado final |
|---|---|---|---|
| Guardar desde Visitas (instantánea existente) | 2 (borrado + alta) | 1 (actualización, mismo Id) | igual |
| Instantáneas duplicadas del mes | 3 | 2 | igual (queda 1) |
| Mes sin instantánea | 1 alta | 1 alta | igual |
| Actividad colgada de la instantánea | 2 + 1 | 1 + 1 | igual |
| Ficha raíz borrada | 1 borrado | 1 borrado | igual |
| Tablas `Personas` y `ActividadPersonas` completas | — | — | iguales |

### v2.1.9 — S6, importación de territorios en Windows (01/11/2026; congregación de 970 registros, copias de la base real)

| Caso | Antes (cambios a sincronizar) | Ahora | Resultado final |
|---|---|---|---|
| Reimportar la misma lista (1.ª vez; se vacían 397 vínculos) | 1.940 | 397 | igual y en el mismo orden |
| Reimportar la misma lista otra vez | 1.940 | 0 | igual |
| Cambiar un dato | 1.940 | 1 | igual |
| Registro nuevo a mitad de lista / al final | 1.941 | 486 / 1 | igual |
| Quitar un registro de en medio | 1.941 | 648 | igual |
| Lista vacía / volver a la original | 971 / 970 | 971 / 970 | igual |

### v2.1.10 — S6, programa de la visita en Windows (01/11/2026; mes de 28 actividades, copias de la base real)

| Caso | Antes (cambios a sincronizar) | Ahora | Resultado final |
|---|---|---|---|
| PDF: reimportar el mismo programa | 56 | 0 (la 1.ª vez, 10) | igual y en el mismo orden |
| PDF: cambiar una hora | 56 | 1 | igual |
| PDF: actividad nueva a mitad / 3 menos | 57 / 55 | 15 / 3 | igual |
| Importar reemplazando, mismas actividades | 56 | 0 (la 1.ª vez, 22) | igual |
| Importar añadiendo | 26 | 26 | igual |
| PDF con lista nula (error) | borraba el mes | el mes queda intacto | diferencia a propósito |

### v2.1.11 — S6, asambleas en Windows (01/11/2026; copias de la base real)

| Caso | Antes (cambios a sincronizar) | Ahora | Resultado final |
|---|---|---|---|
| Asamblea sin cambios (19 partes + 28 del organigrama) | 94 | 0 | igual y en el mismo orden |
| Cambiar una parte / 2 menos en el organigrama | 94 | 1 / 2 | igual |
| Parte nueva a mitad | 95 | 11 | igual |
| Reunión de precursores sin cambios (13 partes) | 28 | 0 | igual |
| Cambiar o añadir una parte / sesión nueva con una parte | 28—29 / 32 | 1 / 2 | igual |
| (Aparte, en los dos: la cabecera y las semanas del calendario) | 1—3 | 1—3 | igual |

### v2.1.12 — Bloque 4, errores del motor de sincronización (01/11/2026)

| Prueba | Resultado |
|---|---|
| PC, 60 s con la sincronización activa | 0 errores ocultos anotados |
| Emulador, 40 s con la carpeta de prueba | 0 errores, sin caídas |
| Filtro de `SincroDiagnostico` (12 casos) | anota lo inesperado, ignora cortes de red y cancelaciones, 1 apunte cada 10 min por sitio, no falla aunque falle el registro |

### v2.1.13 — Bloque 4, guardados de Windows y sincronización de Android (01/11/2026)

| Prueba | Resultado |
|---|---|
| PC, 30 s (incluye los parches de datos al arrancar) | 0 errores ocultos anotados |
| Emulador, 25 s (inicio de la sincronización y canal por carpeta) | 0 errores, sin caídas |

### v2.1.14 — S21, fusión de duplicados (01/11/2026; copias de la base real, persona con 24 meses)

| Caso | Antes | Ahora |
|---|---|---|
| Sin meses repetidos (3 propios) | pasan 3 | pasan 3 |
| 2 repetidos + 2 propios | pasan 0 y se pierden los 2 propios (error oculto UNIQUE) | pasan los 2 propios |
| Todos repetidos | pasan 0 (error oculto) | pasan 0, sin error |
| Repetido con otra mayúscula + 1 propio | pasan 2 (mes duplicado) | pasa 1 |
| Sin actividad | 0 | 0 |

### v2.1.15 — Bloque 5, ReadyToRun en Windows (01/11/2026; recorrido de medición, medianas de 3 rondas alternas)

| Medida | Antes (JIT) | Después (ReadyToRun) |
|---|---|---|
| Arranque hasta la ventana | 177 ms | 156 ms |
| Bloqueo del arranque (Inicio) | 545—555 ms | 491—519 ms |
| Bloqueos de la interfaz (suma) | 1.698 ms | 1.396 ms |
| Primera / segunda apertura de páginas (suma) | 2.633 / 2.461 ms | 2.564 / 2.296 ms |
| ZIP de la versión | 87,1 MB | 60,5 MB |

### v2.1.16 — S13b, Calendario (01/11/2026; recorrido de medición, ReadyToRun, 2 rondas)

| Medida | Antes | Después |
|---|---|---|
| Bloqueo de la interfaz al dibujar la tabla | 204—247 ms (cada vez) | ninguno por encima de 200 ms |
| Segunda navegación al Calendario | 488—516 ms | 316—322 ms |
| Relleno de datos | 24—49 ms | 25—48 ms |
| Posición al abrir (semana actual) | 58,6 % | 58,3 % (misma vista) |

### v2.1.17 — S22, parches de datos (01/11/2026; banco de consola sobre copias de la BD, texto exacto de las dos versiones)

| Medida | Antes | Después |
|---|---|---|
| Coste por llamada sin nada que corregir (abrir conexión + parches; 300 llamadas x 3 rondas) | 6,24—6,51 ms | 3,22—3,26 ms |
| Con otra conexión escribiendo (espera de 1 s por sentencia) | 3.274—3.291 ms y 3 errores | 4 ms y ninguno |
| Comprobación previa (limpia, origen, nombre, duplicado, inocuo) | — | 5 de 5 aciertos |
| Resultado con datos que corregir (Personas y recomendaciones) | referencia | 0 filas distintas |

### Bloque 7 — banco `SincroPruebas` (02/11/2026)

| Ejecución | Resultado | Duración |
|---|---|---|
| Tal cual estaba (16/10) | no compila (46 errores) | — |
| Revivido, sin dar por sincronizados A/B/C | 5/11 | 151 s |
| Revivido | 11/11 | 42 s |
| Con las pruebas 12 y 13 (dos rondas) | 13/13 y 13/13 | 113 y 112 s |
| Sin la huella de S16 (a propósito, revertido) | 12/13: falla la 12 | 160 s |

### Bloque 7 — comprobación antes de publicar (02/11/2026; con una copia recortada del script, sin publicar nada)

| Caso | Resultado |
|---|---|
| Primera vez, sin marca | ejecuta el banco: 13/13, 119 s |
| Sin cambios | se salta, 0 s |
| S16 desactivado a propósito | 12/13: no publica (código 1) y la marca no se mueve |
| Código restaurado | se salta y continúa |
| `-SinPruebas` | avisa y continúa |

### v2.1.18 — S23, puesta al día de un dispositivo nuevo por red (02/11/2026; banco, prueba 15, en el PC)

| Medida | Antes | Después |
|---|---|---|
| Recibir el registro entero (23.167 cambios) | 117 s | 16—17 s |
| Filas distintas entre A y el dispositivo nuevo al terminar | 0 | 0 |

### v2.1.19 — S2b, tarjeta de persona en Android (02/11/2026; Galaxy Z Fold7, pantalla exterior, Release arm64; 6 rondas por versión alternando, desde arranque en frío; 8 desplazamientos rápidos por pasada)

| Medida (mediana) | v2.1.18 | v2.1.19 |
|---|---|---|
| Primera pasada, creando tarjetas: p90 / p99 | 14,5 / 75 ms | 13,5 / 57 ms |
| Primera pasada: tirones | 6,2 % | 8,2 % (ruido: oscila entre 4 y 16 %) |
| Segunda pasada, ya creadas: p90 / p99 | 13,5 / 59 ms | 12 / 43 ms |
| Segunda pasada: tirones | 7,6 % | 2,6 % |
| Nodos en pantalla | 490 | 364 |
| Píxeles distintos (de 2.570.400) | — | 0 |

### v2.1.20 — S25, arranque en frío de Android (02/11/2026; Galaxy Z Fold7)

| Medida | v2.1.19 | v2.1.20 |
|---|---|---|
| `Arranque.SyncDiferido` | 2.346 ms (mediana de 51 arranques) | 210—247 ms |
| Lectura de copias de conflicto por arranque | 114 MB | 0 |
| Copias de seguridad de 32 MB por arranque | 2 | 0 |
