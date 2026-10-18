# PLAN DE MIGRACIÓN A ANDROID — SCCosmos.Mobile

> Documento de seguimiento para continuar el trabajo en sesiones nuevas.
> Proyecto de referencia (NO modificar): `SCCosmos` (WPF, .NET 10).
> Proyecto de desarrollo: `SCCosmos.Mobile` (MAUI, .NET 10, Android prioritario).

---

## 0. Entorno y punto de recuperación

- SDK .NET: **10.0.401**. Workloads instalados: `android 36.1.69`, `ios`, `maccatalyst`, `maui-windows`.
- Repositorio Git: `SCCosmos.Mobile` (rama actual: `master`).
- **Cambios preexistentes del usuario** (NO descartar): gran parte de la app MAUI estaba sin commitear (Calendario, CicloVisitas, FormularioSemana, VisitaDetalle, Import JSON, etc.).
- **Punto de recuperación no destructivo**: ref `refs/backup/pre-migracion-android` → `f034b7b7f83f0cfe00a17763788ba58485dfc8ff` (creado con `git stash create`, la copia de trabajo quedó intacta).
- No se han hecho commits ni push. No se ha modificado `SCCosmos` WPF.
- Compilación base Android: **0 errores** (287 advertencias preexistentes).
  - Comando: `dotnet build SCCosmos.Mobile\SCCosmos.Mobile\SCCosmos.Mobile.csproj -f net10.0-android -c Debug`
- **Emulador de pruebas (operativo)**: `emulator-5554`, Android 16 / API 36, x86_64.
  - ADB: `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe` (ruta absoluta; no está en PATH).
  - Instalación/ejecución y resultados: ver §8.

---

## 1. Reglas permanentes de compatibilidad (SQLite / sincronización)

Ver auditoría. Resumen operativo:

1. Archivo de BD: siempre `sccosmos.db` (almacenamiento privado en móvil; carpeta sincronizada en WPF). No renombrar.
2. No eliminar ni renombrar tablas/columnas existentes. Crecer solo con `ALTER TABLE ADD COLUMN` (patrón `AsegurarColumna*`).
3. No sobrescribir columnas que MAUI aún no gestiona (`Personas.OrigenRegistro`, `AprobadoExhibidores`, `Traslado*`, `RequisitoHoras`).
4. Histórico: fila actual `MesHistorico IS NULL`; clones con `PersonaRaizId` + `MesHistorico` + `FechaExactaHistorico`.
5. Formatos: `dd/MM/yyyy`, `yyyy-MM-dd HH:mm:ss`, `yyyy-MM`; `01/01/YYYY` = fecha aproximada.
6. No activar WAL (rompe la copia por archivo completo).
7. Transacciones en escrituras múltiples. No usar BD real sincronizada para pruebas.
8. **Comunicaciones (correo/WhatsApp) NUNCA se envían automáticamente**: solo se preparan y abre la app del dispositivo.

---

## 2. Estado por prioridad

Leyenda de estado: `pendiente` · `en progreso` · `implementada (compila)` · `verificada (probada en dispositivo)` · `bloqueada`.

### PRIORIDAD 1 — Calendario y ruta

| Tarea | Estado | Archivos |
|---|---|---|
| Edición de semanas, solapamientos, ciclo, import JSON, ver visita | ya existía (verificada por compilación) | CalendarioPage, FormularioSemanaPage, CicloVisitasPage |
| Botón "Acciones" por semana de visita | **implementada (compila)** | CalendarioPage.xaml(.cs) |
| Generación de S-302 (plantilla + campos Text1..Text4) | **implementada (compila)** | Services/CorrespondenciaMobileService.cs, Views/AccionesVisitaPage.xaml(.cs) |
| Envío de S-302 y documentación por correo (preparado, no auto-enviado) | **implementada (compila)** | AccionesVisitaPage.xaml.cs |
| WhatsApp al coordinador (wa.me precargado, no auto-enviado) | **implementada (compila)** | CorrespondenciaMobileService, AccionesVisitaPage |
| Recordatorio (correo de asamblea) | **implementada (compila)** | AccionesVisitaPage |
| Estado de envíos (S302Enviado/DocEnviada/RecordatorioEnviado) | **implementada (compila)** | AccionesVisitaPage + GuardarSemana |
| Envío a representante de Betel (AC-BR) | **implementada (compila)** | AccionesVisitaPage, CorrespondenciaMobileService |
| **Notificación de asamblea de circuito** | **implementada (compila)** — usa la categoría "Notificación AC" (asunto/cuerpo/adjuntos) y envía a `CONG601<codigo>@jwpub.org` de la sección; estado `NotificacionEnviada` en `AccionesVisitaPage` | AccionesVisitaPage |

### PRIORIDAD 2 — Personas y nombramientos

| Tarea | Estado |
|---|---|
| Lista, filtros, detalle/edición, foto | ya existía |
| **Edición de actividad mensual en móvil** | **implementada (compila)** — `DatabaseHelper.Actividad.cs`, `Models/ActividadMensualMobile.cs`, `Views/ActividadMensualEdicionPage.xaml(.cs)`, sección en `PersonaDetallePage` |
| **Mapa y coordenadas (abrir en mapa + geocodificar con Nominatim)** | **implementada (compila)** — `Services/GeocodificacionService.cs`, `DatabaseHelper.Coordenadas.cs`, botones en `PersonaDetallePage` |
| **Recomendaciones de nombramientos (listar)** | **implementada (compila)** — `Views/NombramientosPage.xaml(.cs)`, `ObtenerRecomendacionesNombramientosMobile`, botón en `PersonasPage` |
| **Aprobación/cancelación de recomendaciones en JW Hub** | **implementada (compila)** — `Platforms/Android/JwHubAccionesAndroid` pulsa en Hub por WebView+JS ("Analizar hermano" → "Editar estado" → Aprobar/Cancelar → Guardar → Enviar), igual que la lectura de personas. Botones en `NombramientosPage`; al procesar, borra la recomendación (`EliminarRecomendacionNombramientoMobile`). **No verificable aquí** (requiere cuenta JW Hub real). El envío por OWA y la carta Word siguen siendo de escritorio, pero en Android se sustituyen por Gmail + carta PDF con iText |
| **Campos de origen (OrigenRegistro), AprobadoExhibidores y RequisitoHoras** | **implementada (compila)** — `Data/DatabaseHelper.PersonaExtra.cs`; sección "ORIGEN Y ESTADO" en `PersonaDetallePage` (guarda solo esas columnas, sin tocar los enlaces de traslado) |
| **Historial (instantáneas mensuales)** | **implementada (compila)** — `ObtenerHistorialPersonaMobile` + `Views/HistorialPersonaPage.xaml(.cs)` (solo lectura) |
| **Traslados** | **implementada y verificada en emulador** — `RegistrarTrasladoMobile` + botón en `PersonaDetallePage` |
| **Fusión de registros** | **implementada y verificada en emulador** — `FusionarYBorrarDefinitivoMobile` + botón en `PersonaDetallePage` |

### PRIORIDAD 3 — Visitas

| Tarea | Estado |
|---|---|
| Ministerio, programa, notas, territorios (lectura), contabilidad, S-303, S-2 | ya existía |
| Grupos/estadísticas | ya existía (pestaña "Grupos de predicación" en el detalle) |
| **Territorios (edición/vinculación y borrado)** | **implementada (compila)** — `DatabaseHelper.Territorios.cs`, `Views/TerritorioEdicionPage.xaml(.cs)`, `Id` en `VistaTerritorioMobile` |
| **Mapa de la visita (Salón del Reino)** | **implementada (compila)** — `LatitudSalon/LongitudSalon` en `VistaVisitaMobile` + botón en el detalle (geocodifica si faltan coordenadas) |
| **Generación masiva S-21** | **implementada (compila)** — `Services/S21MobileService.cs`, `Data/DatabaseHelper.S21.cs`, `Views/S21Page.xaml(.cs)` (requiere plantilla PDF cargada por el usuario; pendiente prueba real) |
| **Importar programa JSON desde la visita** | **implementada y verificada en emulador** — `Services/ProgramaImportService.cs`, `Data/DatabaseHelper.ProgramaImport.cs`, botón en el panel Programa de `VisitaDetallePage` |

### PRIORIDAD 4 — Resto de módulos

| Módulo | Estado |
|---|---|
| **Circuito y congregaciones** | **implementada (compila)** — `Data/DatabaseHelper.Circuito.cs`, `Views/CircuitoPage.xaml(.cs)`, `Views/CongregacionEdicionPage.xaml(.cs)`, `Views/SeccionesCircuitoPage.xaml(.cs)`, `DetalleLista` en `Congregacion`. Importación KMZ: pendiente (flujo de escritorio) |
| **Correspondencia (editor de plantillas/adjuntos)** | **implementada (compila)** — `Data/DatabaseHelper.Documentos.cs`, `Views/CorrespondenciaPage.xaml(.cs)` (claves `Asunto_*`/`Email_*` y tabla `Documentos`) |
| **Escuelas** | **implementada (compila)** — `Models/EstudianteEscuelaMobile.cs`, `Services/EscuelasService.cs`, `Views/EscuelasPage.xaml(.cs)`, ítem de flyout en `AppShell` |
| Correspondencia (editor de plantillas/adjuntos) | parcial (solo consumo de plantillas) |
| **Asambleas (edición completa)** | **implementada y verificada en emulador** — `Data/DatabaseHelper.Asambleas.cs`, `Models/AsambleaModels.cs`, `Views/AsambleasPage`, `Views/AsambleaDetallePage`, `Views/ParteAsambleaEdicionPage`, `Views/OrganigramaEdicionPage`, `Views/PrecursoresPage`, `Views/SesionPrecursoresEdicionPage`, `Views/SesionPrecursoresDetallePage`, `Views/ParteSesionPrecursoresEdicionPage`. Verificado: alta/borrado de partes del programa, alta de organigrama, alta de sesión de precursores, **alta de parte de sesión de precursores**. Borrados y ediciones restantes compilan y siguen el mismo patrón (confirmación por automatización pendiente). Solo lectura de datos de oradores/asignaciones ya existente |
| **Archivo del circuito** | **implementada (explorador, compila)** — `Views/ArchivoCircuitoPage.xaml(.cs)` lee de la carpeta sincronizada (SAF) la subcarpeta configurada y `ArchivosGuardados`, y abre los archivos. La **creación/edición/PDF** de documentos sigue en el PC |
| **Alertas y recordatorios** | **implementada (compila)** — `Services/AlertasMobileService.cs`, `Models/AlertaMobile.cs`, `Views/AlertasPage.xaml(.cs)` + toolbar en `InicioPage` |
| **JW Hub: extracción de Sexo y conflicto de foto** | **implementado (compila)** — `JwHubExtractorAndroid` lee Sexo de "Información personal" (no crítico) y `JwHubSyncAnalysisService` mapea `Sexo` y reactiva el conflicto de foto con `JwHubPhotoComparer` (dHash). **No verificable sin cuenta JW Hub real**; la app arranca sin regresiones |
| Descargas (JW Stream) | **no viable** en Android (Selenium/ffmpeg); documentado §6 |

---

## 3. Correcciones de compatibilidad realizadas

### 3.1 Divergencia `ProgramaVisitaV2.Direccion` (WPF) vs `Ubicacion` (MAUI) — RESUELTA

- Problema: MAUI escribía la ubicación en una columna propia `Ubicacion`; WPF lee/escribe `Direccion`. Provocaba pérdida silenciosa de datos entre plataformas.
- Solución (no destructiva):
  - Escritura (`DatabaseHelper.VisitaEdicion.cs`): ahora escribe `Direccion` (columna de WPF).
  - Se conserva `Ubicacion` como columna heredada; se crea `Direccion` si falta (`ALTER TABLE ... ADD COLUMN`) y `Ubicacion` sigue existiendo (nunca se elimina).
  - Lectura (`DatabaseHelper.VisitaLectura.cs`): `COALESCE(NULLIF(pv.Direccion,''), pv.Ubicacion, '')` → lee primero el campo de WPF y cae a la columna heredada móvil.
- Verificación: compila. **Pendiente prueba real** creando una actividad en móvil e importando la BD en WPF.

---

## 4. Archivos modificados/creados en esta sesión

Creados:
- `Services/CorrespondenciaMobileService.cs`
- `Services/GeocodificacionService.cs`
- `Views/AccionesVisitaPage.xaml(.cs)`
- `Views/ActividadMensualEdicionPage.xaml(.cs)`
- `Views/NombramientosPage.xaml(.cs)`
- `Data/DatabaseHelper.Actividad.cs`
- `Data/DatabaseHelper.Coordenadas.cs`
- `Models/ActividadMensualMobile.cs`
- `Models/EstudianteEscuelaMobile.cs`
- `Services/EscuelasService.cs`
- `Views/EscuelasPage.xaml(.cs)`
- `Data/DatabaseHelper.Territorios.cs`
- `Data/DatabaseHelper.S21.cs`
- `Data/DatabaseHelper.Circuito.cs`
- `Data/DatabaseHelper.Documentos.cs`
- `Data/DatabaseHelper.Asambleas.cs`
- `Services/S21MobileService.cs`
- `Services/AlertasMobileService.cs`
- `Models/AsambleaModels.cs`
- `Models/AlertaMobile.cs`
- `Views/TerritorioEdicionPage.xaml(.cs)`
- `Views/S21Page.xaml(.cs)`
- `Views/CircuitoPage.xaml(.cs)`
- `Views/CongregacionEdicionPage.xaml(.cs)`
- `Views/SeccionesCircuitoPage.xaml(.cs)`
- `Views/CorrespondenciaPage.xaml(.cs)`
- `Views/AsambleasPage.xaml(.cs)`
- `Views/AsambleaDetallePage.xaml(.cs)`
- `Views/PrecursoresPage.xaml(.cs)`
- `Views/AlertasPage.xaml(.cs)`
- `Services/ProgramaImportService.cs`
- `Data/DatabaseHelper.ProgramaImport.cs`
- `Models/ActividadProgramaMobile.cs`
- `Views/ParteAsambleaEdicionPage.xaml(.cs)`
- `Views/OrganigramaEdicionPage.xaml(.cs)`
- `Views/SesionPrecursoresEdicionPage.xaml(.cs)`
- `Views/SesionPrecursoresDetallePage.xaml(.cs)`
- `Views/ParteSesionPrecursoresEdicionPage.xaml(.cs)`
- `Data/DatabaseHelper.PersonaExtra.cs`
- `Views/HistorialPersonaPage.xaml(.cs)`
- `Services/AutoSyncService.cs`
- `Data/DatabaseHelper.Estadisticas.cs`
- `Views/ArchivoCircuitoPage.xaml(.cs)` (reescrito con edición VFS)
- `Models/VfsModels.cs`
- `Services/AsambleaPdfService.cs`
- `Services/CorreoService.cs`
- `Services/CartaNombramientosPdfService.cs`
- `Services/ArchivoEspecialPdfService.cs`
- `Models/ArchivoEspecialModels.cs`
- `Views/ArchivoEspecialPage.xaml(.cs)`
- `Services/VfsService.cs`
- `Views/CarpetaVfsPage.xaml(.cs)`
- `Platforms/Android/JwHubAccionesAndroid.cs`
- `Services/PerfilAccesoService.cs`
- `Views/PerfilInicialPage.xaml(.cs)`
- (WPF) `PerfilAcceso.cs`, `PerfilInicialWindow.xaml(.cs)`

Modificados:
- `Data/DatabaseHelper.VisitaEdicion.cs` (Direccion + `ObtenerDocumentosVisitaMobile` + recomendaciones nombramientos + modelo)
- `Data/DatabaseHelper.VisitaLectura.cs` (lectura con respaldo + Id/coords del Salón)
- `Views/CalendarioPage.xaml(.cs)` (botón Acciones + `TieneAcciones`/`EsAsamblea`)
- `Views/PersonasPage.xaml(.cs)` (toolbar Nombramientos)
- `Views/PersonaDetallePage.xaml(.cs)` (actividad mensual + mapa/coordenadas)
- `Views/VisitaDetallePage.xaml(.cs)` (territorios editables, mapa del Salón, tarjetas S-21)
- `Models/Congregacion.cs` (propiedad `DetalleLista`)
- `Views/InicioPage.xaml(.cs)` (toolbar "Alertas")
- `Data/DatabaseHelper.Asambleas.cs` (edición de partes) y `Data/DatabaseHelper.Circuito.cs` (inserción de sección con `last_insert_rowid` en comando aparte)
- `Views/VisitaDetallePage.xaml(.cs)` (botón e importación del programa JSON)
- `Views/AsambleaDetallePage.xaml(.cs)` (alta/edición/borrado de partes)
- `Models/Persona.cs` (campos de origen/traslado/aprobación)
- `Views/PersonaDetallePage.xaml(.cs)` (origen/estado, historial, traslado, fusión)
- `Views/AccionesVisitaPage.xaml(.cs)` (notificación de asamblea)
- `Platforms/Android/JwHubExtractorAndroid.cs` (Sexo)
- `Platforms/Android/JwHubSyncAnalysisService.cs` (Sexo + foto con comparador)
- `AppShell.xaml` (ítems de flyout: Correspondencia, Circuito, Asambleas, Escuelas)

---

## 5. Compilaciones y pruebas

| Fecha | Acción | Resultado |
|---|---|---|
| sesión | Build Debug net10.0-android (base) | 0 errores, 287 warnings |
| sesión | Build tras fix Direccion | 0 errores |
| sesión | Build tras AccionesVisitaPage | 0 errores |
| sesión | Build tras actividad mensual + Nombramientos | 0 errores |
| sesión | Build tras mapa/coordenadas | 0 errores |
| sesión | Build tras Escuelas + flyout | 0 errores |
| sesión 2 | Build tras territorios editables | 0 errores |
| sesión 2 | Build tras mapa del Salón | 0 errores |
| sesión 2 | Build tras tarjetas S-21 | 0 errores |
| sesión 2 | Build tras Circuito + Congregaciones + Secciones | 0 errores |
| sesión 2 | Build tras Correspondencia | 0 errores |
| sesión 2 | Build tras Alertas | 0 errores |
| sesión 2 | Build tras Asambleas (lectura) | 0 errores |
| sesión 3 | Build tras importar programa JSON desde la visita | 0 errores |
| sesión 3 | Install + prueba en `emulator-5554` (importar programa) | 0 errores; import verificado |
| sesión 3 | Build tras edición de partes de asamblea | 0 errores |
| sesión 3 | Install + prueba en `emulator-5554` (alta/borrado de parte) | 0 errores; verificado |
| sesión 3 | Build + install tras edición de organigrama | 0 errores |
| sesión 3 | Prueba en `emulator-5554` (alta de organigrama) | 0 errores; verificado |
| sesión 4 | Build + install + prueba en `emulator-5554` (alta de sesión de precursores) | 0 errores; verificado |
| sesión 4 | Build + install + prueba en `emulator-5554` (alta de parte de sesión de precursores) | 0 errores; verificado |
| sesión 5 | Build tras JW Hub (Sexo + foto) | 0 errores (sin verificación real posible) |
| sesión 5 | Build + install + prueba en `emulator-5554` (traslado) | 0 errores; verificado |
| sesión 5 | Build + install + prueba en `emulator-5554` (fusión) | 0 errores; verificado |
| sesión 5 | Build tras campos de origen + historial + notificación de asamblea | 0 errores |
| sesión 6 | Build + install + prueba en `emulator-5554` (iconos calendario, Inicio ampliado, Config datos, Archivo) | 0 errores; render y navegación OK, sin excepciones |
| sesión 6 | Build tras sincronización automática (`AutoSyncService`) | 0 errores (auto-export no verificable sin carpeta SAF configurada) |
| sesión 7 | Build tras integrar cambios WPF (S-2 tolerante) + Archivo VFS + PDF Asambleas | 0 errores |
| sesión 7 | Prueba en `emulator-5554` (PDF de programa de asamblea) | **verificado**: `Asamblea_1_programa.pdf` válido (`%PDF-1.7`), sin excepciones |
| sesión 7 | Build + install + prueba (botones de envío de Asambleas) | 0 errores; render OK, sin excepciones. El compositor de Gmail no se puede verificar sin cuenta configurada en el emulador |
| sesión 7 | Build tras renombrado en Archivo | 0 errores (no verificable sin carpeta SAF) |
| sesión 8 | Build + install + prueba (carta PDF iText + secciones especiales del Archivo) | 0 errores; páginas render y navegan, sin excepciones |
| sesión 9 | Build + install (carpetas anidadas del Archivo) | 0 errores; página del Archivo OK, sin excepciones (VFS real requiere carpeta SAF) |
| sesión 10 | Build + install (aprobar/cancelar nombramientos en Hub desde Android) | 0 errores; botones renderizados, sin excepciones. La acción real requiere cuenta JW Hub |
| sesión 11 | Fix APK real: arm64-v8a + ensamblados embebidos | Verificado: APK contiene arm64 y arranca |
| sesión 11 | Perfil de usuario + código (Android) | **Verificado en emulador**: Esposa → 4 páginas; Hombre + código → todas |
| sesión 11 | Perfil de usuario + código (Windows) | **Compila (0 errores)**; no ejecutable aquí |
| sesión 11 | Interfaz: tema oscuro forzado | Compila y arranca sin excepciones |

Pruebas automatizadas: no hay proyecto de tests en MAUI. Pruebas **manuales en dispositivo/emulador**: ver §8 (emulador Android 16 ya operativo).

---

## 6. Problemas conocidos / bloqueos

- No se ha probado en dispositivo Android real: toda verificación es de compilación.
- `Email.Default` y `Launcher` dependen de apps instaladas; el código muestra mensajes si no existen.
- El extractor JW Hub Android no obtiene `Sexo` (auditoría).
- `JwHubPhotoComparer` implementado pero sin uso (conflicto de foto desactivado).

### 6.1 Archivo del circuito — BLOQUEADO por el diseño de sincronización
La página WPF `ArchivoCircuitoPage` guarda todo su contenido en **archivos junto a la BD**
(`Archivo_SeccionesVFS.json`, `Archivo_CarpetasVFS.json`, `Archivo_FicherosVFS.json`,
`Archivo_HermanosPotencial.json`, `Archivo_ProblemasPendientes.json` y la carpeta
`ArchivosGuardados`). **No hay nada en SQLite.** La sincronización móvil solo copia
`sccosmos.db`, así que esos datos nunca llegan al teléfono. Implementarlo exigiría ampliar
el mecanismo de sincronización (prohibido sin necesidad), por lo que **queda bloqueado** hasta
que el usuario decida ampliar el transporte (p. ej. un ZIP de la carpeta del circuito).

### 6.2 Descargas (JW Stream) — NO VIABLE en Android
WPF usa Selenium/Firefox + `ffmpeg.exe` para descargar audio/HLS de `stream.jw.org` y `jw.org`.
No hay equivalente razonable en Android sin introducir dependencias nuevas y flujos frágiles.
Se documenta como **no viable**; el usuario descarga en el PC y sincroniza la BD.

### 6.3 Ídem para `local_settings.json` y `HubSync_Pendiente.json`
Son archivos del PC; el móvil dispone de sus propios equivalentes en `Preferences`.

---

## 8. PRUEBAS EN EMULADOR ANDROID (operativo)

Entorno de pruebas:
- Emulador `emulator-5554` — Android 16 (API 36), x86_64.
- ADB: `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe` (no está en PATH; se usa ruta absoluta).
- Instalación: `dotnet build ... -f net10.0-android -c Debug -t:Install`.
- Lanzamiento/observación: `adb shell monkey -p com.companyname.sccosmos.mobile -c android.intent.category.LAUNCHER 1`, `adb logcat`, `uiautomator dump` (no hay captura visual disponible en este entorno: la lectura de imágenes no es posible, así que la inspección visual se hace vía jerarquía de vistas).

**Base de datos de pruebas:** se generó una BD **sintética** (`seed.sql` → `sccosmos.db`) con el esquema compartido y datos de ejemplo (1 congregación, 3 personas, actividades, 1 visita, 1 asamblea, territorios, contabilidad, recomendaciones, reunión de precursores). Se copió a la carpeta privada de la app vía `run-as`. **Nunca se usó una BD real sincronizada ni cuentas JW Hub.**

Verificado EN EJECUCIÓN (0 excepciones en logcat durante toda la sesión):

| Funcionalidad | Resultado |
|---|---|
| Arranque de la app | Correcto (MainActivity en foreground, sin FATAL) |
| Inicio: saludo, semana actual y estadísticas desde BD | Correcto |
| Toolbar "Alertas" en Inicio + `AlertasPage` | Correcto: 3 alertas coherentes (S-302, documentación, recordatorio) |
| Calendario: semanas, mes, semana actual | Correcto |
| Botón **Acciones** en semana de visita + `AccionesVisitaPage` | Correcto (S-302, documentación, WhatsApp, recordatorio, estado) |
| Personas: lista, búsqueda, toolbar "Nombramientos" | Correcto |
| `NombramientosPage` | Correcto (recomendación de ejemplo) |
| `PersonaDetallePage`: datos + **Actividad mensual** desde BD | Correcto (oct-2026, sep-2026 con horas/cursos) |
| Correspondencia: categorías desde BD, asunto/cuerpo, adjuntos | Correcto |
| Circuito: lista de congregaciones y **editor** de congregación | Correcto (todos los campos, selección de sección) |
| Asambleas: lista, **detalle** (programa + organigrama con nombres) | Correcto |
| Reunión de precursores (página) | Presente y accesible |
| **Escuelas**: cálculo de candidatos | Correcto (1 candidato esperado: precursora regular) |
| **Escritura en BD**: botón "Descartar" en Escuelas | Correcto: se guardó `DescartadosEscuelas_2026=2`; `PRAGMA integrity_check = ok` |
| **Escritura en BD**: "Guardar plantilla" en Correspondencia | Correcto: creó `Asunto_/Email_Asignación asamblea circuito` en `Configuracion` |
| **Importar programa JSON desde la visita** (`VisitaDetallePage`) | Correcto: importó 2 actividades; resolvió acompañante (`CompaneroId=2`), guardó en la columna `Direccion` de WPF y avisó del registro con día inválido |
| **Asambleas: alta de parte** (`AsambleaDetallePage` → editor) | Correcto: insertó fila en `ProgramaAsambleasPartes` (Turno/Hora/Título/Asignado) |
| **Asambleas: borrado de parte** | Correcto: eliminó la fila de la BD y refrescó la lista |
| **Asambleas: alta de organigrama** (`AsambleaDetallePage` → editor) | Correcto: insertó fila en `ProgramaAsambleasOrganigrama` |
| **Precursores: alta de sesión** (`PrecursoresPage` → editor) | Correcto: insertó fila en `ReunionPrecursoresSesiones` |
| **Precursores: alta de parte de sesión** (`SesionPrecursoresDetallePage` → editor) | Correcto: insertó fila en `ReunionPrecursoresPartes` |
| **Traslado** (`PersonaDetallePage` → Trasladar) | Correcto: creó la ficha destino (`TrasladoAnteriorId`), marcó la anterior con `TrasladoSiguienteId` + `FechaEliminacion` |
| **Fusión** (`PersonaDetallePage` → Fusionar) | Correcto: borró la ficha origen y consolidó `OrigenRegistro` en la destino |

Correcciones derivadas de las pruebas:
- Alertas: eliminada la duplicación "del del" en las descripciones.
- `Congregacion.DetalleLista`: evitada la duplicación "Sección Sección X".

Nota de automatización (para futuras sesiones): al escribir en campos con `adb shell input text`, el
**teclado virtual cubre los botones inferiores**; hay que ocultarlo (`adb shell input keyevent 4`)
antes de pulsar "Guardar". Además, en `uiautomator` el orden de los grupos de `bounds` es
`[x1,y1][x2,y2]`.

Pendiente de probar en emulador (no se ha podido verificar aún):
- Flujos de escritura completos: crear/editar persona, guardar semana, contabilidad, territorios, añadir mes de actividad.
- **JW Hub**: requiere una cuenta/sesión real → **no se puede probar** aquí (prohibido usar cuentas reales).
- Generación de PDF (S-21, S-302, S-303) y compartir: depende de plantillas y de apps del dispositivo.
- Correo (`Email.Default`) y WhatsApp (`wa.me`): dependen de apps instaladas en el emulador.
- Importación de BD real por carpeta SAF y sincronización (requiere carpeta elegida por el usuario).

---

## 10. SINCRONIZACIÓN AUTOMÁTICA Y PARIDAD (última tanda)

### 10.1 Sincronización automática de la base de datos
- **`Services/AutoSyncService.cs`**: vigila la BD local (hash SHA-256) y, cuando cambia por
  cualquier escritura del móvil, la **exporta sola** a la carpeta sincronizada mediante
  `DatabaseSyncService.ExportarAsync` (que conserva la protección anti-sobrescritura si el PC
  cambió la BD). Bucle cada 20 s + exportación al pasar a segundo plano (`App.OnSleep`).
- **Importación al arrancar**: si el PC cambió la BD y el móvil no tiene cambios locales
  pendientes, se importa automáticamente (`AutoSyncService.ImportarAlIniciarAsync`, invocado en
  `App.CreateWindow`).
- Interruptor y estado en **Configuración** ("Sincronización automática", por defecto activada).
- Se descarta el hash local al importar/exportar manualmente para no reenviar sin cambios.

### 10.2 Ruta del Archivo del circuito configurable
- En **Configuración → Datos del superintendente y circuito** hay un campo
  **"Carpeta del Archivo del circuito"** (clave `RutaArchivoCircuito`), subcarpeta relativa a la
  carpeta sincronizada (vacío = misma carpeta).

### 10.3 Calendario con botones de icono
- Cada semana muestra botones de icono: ✏️ Editar, 👁️ Ver visita, 📨 Acciones, 🗂️ Importar/actualizar
  JSON, ➕ Añadir actividad, 🗑️ Eliminar (con `SemanticProperties.Description` para accesibilidad).

### 10.4 Inicio con los mismos datos que Windows
- Añadidas tarjetas: **Publicadores, Hombres, Mujeres, No bautizados, Cursos bíblicos (último mes),
  Media de edad, Media de precursores auxiliares, Betel** (agregados vía
  `DatabaseHelper.Estadisticas.cs`), además de las 8 existentes.
- Botón **"Ver visita"** en la tarjeta de la semana cuando es visita (abre `VisitaDetallePage`).

### 10.5 Configuración con los mismos datos que Windows
- Añadidos: **Nombre/Apellidos del SC, Nombre/Apellidos de la esposa, Número de circuito,
  Carpeta del Archivo del circuito** (mismas claves que WPF).

### 10.6b Integración de cambios recientes de WPF (DatabaseHelper, VisitasPage, SincronizadorJWHub)
Revisados los cambios sin commitear en WPF:
- `SincronizadorJWHub`: `AprobarRecomendacionHubSinCorreo`, `ValidarUrlRecomendacion`, `VerificarPaginaHubSinError` y refuerzo de `CancelarRecomendacionHub`. **Corrección**: la interacción con Hub (aprobar/cancelar) **SÍ es posible en Android** con WebView+JavaScript (es el mismo mecanismo que ya lee personas); lo que es exclusivo de escritorio es el **envío por OWA** (en Android se usa Gmail) y la **carta DOCX→PDF** (en Android se genera con iText). Implementado en `JwHubAccionesAndroid`.
- `VisitasPage`: envío de S-303 con aprobación/cancelación por fila, carta de nombramientos DOCX→PDF (Word COM) y `EliminarRecomendacionProcesada`. Todo es **de escritorio** salvo una parte **portable**: el **emparejamiento tolerante de congregación** de las recomendaciones S-2 (Hub añade "(código)"). **Integrado en Android**: `ObtenerRecomendacionesS2VisitaMobile` ahora filtra en memoria con `EsRecomendacionDeCongregacionMobile` (misma normalización y recorte de sufijo que WPF).
- `DatabaseHelper.EliminarRecomendacionProcesada`: sin uso en Android (no se procesan recomendaciones en Hub).

### 10.7 Archivo del circuito: edición (Android)
- `Views/ArchivoCircuitoPage` ahora **edita** el VFS: crear **secciones**, **subir archivo** a una sección (se guarda en `ArchivosGuardados` y se registra en `Archivo_FicherosVFS.json`), **abrir** y **eliminar** archivos. Usa el mismo esquema JSON que WPF (`Archivo_SeccionesVFS.json`, `Archivo_CarpetasVFS.json`, `Archivo_FicherosVFS.json`).
- `Services/SyncFolderService`: añadidos `ListarNombres`, `LeerTextoAsync/LeerArchivoAsync`, `EscribirTextoAsync/EscribirBytesAsync`, `EliminarArchivo`, `AsegurarCarpetaRaiz` (SAF). La carpeta/subcarpeta se define en Configuración (`RutaArchivoCircuito`).
- **Renombrado** de secciones y de archivos (nombre visible). Usa el mismo JSON del VFS.
- **Secciones especiales**: "Hermanos con potencial" (`Archivo_HermanosPotencial.json`) y "Problemas pendientes" (`Archivo_ProblemasPendientes.json`) en `Views/ArchivoEspecialPage`: ver, **añadir**, **eliminar** y **generar PDF** (`Services/ArchivoEspecialPdfService`, iText). Mismos JSON que WPF.
- **Carpetas anidadas**: `Views/CarpetaVfsPage` + `Services/VfsService` permiten navegar/editar carpetas dentro de una sección (`CarpetaVFS.ParentId`): crear subcarpeta, renombrar, eliminar (recursivo, con sus archivos físicos) y subir/abrir/renombrar/eliminar archivos dentro. Mismos JSON que WPF.
- Ya no queda pendiente funcionalidad del Archivo salvo verificarlo contra una carpeta SAF real.

### 10.10 Carta de nombramientos en PDF (Android, sin Word)
- `Services/CartaNombramientosPdfService` (iText) genera la **carta de nombramientos en PDF** a partir de las decisiones S-2 marcadas como aprobadas (nombrados como ancianos / siervos / ceses), con fecha, congregación, circuito y SC.
- Se **adjunta automáticamente al correo del S-303** (`VisitaDetallePage`) cuando hay aprobaciones. Sustituye en Android al flujo DOCX→Word→PDF de WPF.
- Sigue siendo **de escritorio** la aprobación/cancelación real en JW Hub (Selenium).

### 10.9 Correo por la app del dispositivo (Gmail) — Android
- Nuevo `Services/CorreoService.cs`: centraliza el envío. **Nunca envía solo**: abre el compositor (Gmail en Android) con **destinatarios, asunto, cuerpo HTML y adjuntos** ya rellenados; el usuario elige la cuenta (la de JW, ya sincronizada en Android) y confirma.
- Ya usaban este cauce: S-302, documentación, representante de Betel, notificación de asamblea y recordatorio (Acciones de la semana), y S-303 (VisitaDetalle).
- **Añadidos ahora en Asambleas**: **"Enviar asignación"** (categoría "Asignación asamblea circuito", a los correos de los hermanos asignados) y **"Enviar organigrama"** (categoría "Envío de organigrama", adjuntando el organigrama en PDF generado con iText + los documentos de la categoría).
- Nota: Android no permite fijar el remitente por código; se usa la cuenta JW ya presente en Gmail al redactar.

### 10.8 PDF de Asambleas (Android)
- `Services/AsambleaPdfService` (iText) genera **PDF del programa** y del **organigrama**; botones en `AsambleaDetallePage` que comparten el PDF. **Verificado en emulador**: se generó `Asamblea_1_programa.pdf` válido (`%PDF-1.7`).

### 10.6 Revisión de paridad (funciones de Windows vs Android)
Revisadas las páginas/servicios de WPF. Resultado:
- **Ya en Android**: Inicio, Calendario, Personas (+actividad, mapa, historial, traslado, fusión,
  nombramientos), Visitas (ministerio, programa+importar JSON, notas, grupos, territorios con
  edición/vinculación, contabilidad, S-303, S-2, S-21), Circuito, Correspondencia, Asambleas
  (partes, organigrama, precursores), Escuelas, Alertas, Configuración, sincronización BD.
- **Pendiente en Android (por dependencia de escritorio o decisión)**: envío real de correo/WhatsApp
  (depende de apps/navegador), descarga JW Stream (no viable), aprobar/cancelar nombramientos (OWA),
  generación de PDF de Asambleas/Archivo (iText ya está; trabajo pendiente), y la edición completa
  del Archivo (crear/renombrar/borrar/PDF) que se mantiene en el PC.

---

## 11. APK PARA MÓVIL REAL, PERFIL DE USUARIO E INTERFAZ

### 11.1 APK que no abría en un móvil arm64 (RESUELTO)
- Causa: el APK de Debug se generaba **solo para x86_64** (emulador) y con **"fast deployment"** (0 ensamblados dentro del APK, se copiaban por ADB). Al copiarlo a mano al móvil, no encontraba librerías nativas arm64 ni las DLLs → no abría.
- Solución en `SCCosmos.Mobile.csproj` (solo Android):
  - `<RuntimeIdentifiers>android-arm64;android-arm;android-x64</RuntimeIdentifiers>` (incluye **arm64-v8a**).
  - `<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>` y `<AndroidFastDeploymentType>None</AndroidFastDeploymentType>` (ensamblados dentro del APK).
  - `<AndroidPackageFormats>apk</AndroidPackageFormats>`.
- Verificado: el APK ahora contiene `lib/arm64-v8a/...` y `lib/arm64-v8a/lib_SCCosmos.Mobile.dll.so`; se instala y arranca en el emulador.
- APKs generados:
  - Debug (todas las ABIs, autónomo): `bin/Debug/net10.0-android/com.companyname.sccosmos.mobile-Signed.apk`.
  - Release arm64 (recomendado para repartir): `dotnet publish -f net10.0-android -c Release -p:RuntimeIdentifier=android-arm64` → `bin/Release/net10.0-android/android-arm64/publish/com.companyname.sccosmos.mobile-Signed.apk`.

### 11.2 Perfil de usuario (Hombre/Esposa) y código de acceso
- Regla: acceso completo solo con perfil **Hombre** y el código **CODIGO_OCULTO**. En caso contrario (Esposa o sin código), solo **Inicio, Calendario, Personas y Configuración**.
- **Android** (`Services/PerfilAccesoService`, `Views/PerfilInicialPage`, gating en `AppShell`, sección en `ConfiguracionPage`): **verificado en emulador** — primer arranque pregunta; Esposa → menú reducido; Hombre + código → menú completo. Editable en Configuración.
- **Windows** (`PerfilAcceso.cs`, `PerfilInicialWindow.xaml`, gating en `MainWindow`, sección en `ConfiguracionPage`): **implementado y compila** (0 errores). Se guarda por equipo en `%LocalAppData%\SCCosmos\perfil_usuario.json` (no en la BD compartida, para que el perfil de cada uno no se mezcle al sincronizar).
- Valores por defecto seguros: si aún no hay perfil, se concede acceso (se pregunta al arrancar), de modo que no bloquea a nadie por error.

### 11.3 Interfaz (revisión estética)
- **Corrección clave**: la interfaz está diseñada en oscuro; se fuerza `UserAppTheme = AppTheme.Dark` en la app para que, con el móvil en modo claro, ningún control quede con texto oscuro sobre fondo oscuro.
- El tema ya es coherente (paleta #101216/#171A1F, tarjetas con borde #343942, acento #4BA3E3, botones con radio 9-10). Pendiente (opcional): una pasada de diseño más profunda unificando estilos globales y tipografía en `Resources/Styles`.

## 12. LOTE DE MEJORAS SOLICITADO (estado)

Solicitado: pasada de diseño + selección de perfil en primera pantalla de Windows + 8 cambios.

### Hecho en esta iteración (compila; Android 0 errores, WPF 0 errores)
- **#1 Quitar WhatsApp al coordinador** del Calendario: eliminado el botón en Android
  (`AccionesVisitaPage`). En Windows ya estaba comentado en `CalendarioPage.xaml` (no se muestra).
- **#4 Personas (Android)**: al abrir una persona la ficha es **editable directamente** (sin "Editar");
  **"Cancelar"** vuelve al listado; el botón "Editar" se sustituye por **"Añadir contacto"**, que abre el
  **editor de contactos de Android** con nombre/teléfono/email/dirección precargados (editable).
  Nuevo `Platforms/Android/ContactoAndroid.cs` (Intent ACTION_INSERT).
- **#8 Alertas: "marcar todas como leídas"**:
  - Android: botón en `AlertasPage`; se persisten las firmas leídas (`AlertasMobileService`) y
    desaparecen.
  - Windows: botón "✔ Marcar todas como leídas" en el popup de alertas (`MainWindow`); se guardan en
    `%LocalAppData%\SCCosmos\alertas_leidas.json` (por equipo) y se filtran en `VerificarAlertas`.
- **Perfil de usuario en la primera pantalla de Windows**: al abrir, si no hay perfil, aparece
  `PerfilInicialWindow` (modal) antes de usar la app; ya implementado en la iteración anterior.
- **Interfaz**: tema oscuro forzado (evita contrastes erróneos en modo claro).

### Añadido tras el lote anterior
- **Interfaz (parcial)**: además del tema oscuro forzado, se **desactiva el autoescalado de fuente**
  (`FontAutoScalingEnabled=False`) en Label/Button/Entry/Editor/Picker, para que el escalado de fuente
  de Android no recorte textos en celdas densas (Calendario, Ministerio…). Esto es también la
  **corrección probable de #2**.
- **Dispositivo real**: `RFCY70XT1XJ` (arm64-v8a, API 36) detectado; el APK **se instala** correctamente
  (valida el arreglo del APK). No se pudo inspeccionar la UI porque el dispositivo estaba **bloqueado**.

### Verificado en el MÓVIL REAL `RFCY70XT1XJ` (arm64-v8a, API 36) — sesión actual
- **#2 Visitas — horas/X/check por mes: RESUELTO Y VERIFICADO.**
  Causa real: cada celda del Ministerio era un `Border` con **dos hijos** (la pila de etiquetas y un
  `Button` transparente superpuesto con `ZIndex`/`InputTransparent`); MAUI solo conservaba uno, por lo
  que se pintaba el fondo de color pero **nunca el texto**. Arreglo: envolver el contenido en un `Grid`
  (un único hijo del `Border`) y quitar `ZIndex`/`InputTransparent`. Comprobado en el móvil con
  `uiautomator dump` (aparecen `May 26 … Oct 26` y los valores ✓/✕) **y** por análisis de píxeles
  (los 6 recuadros pasan de 0 a >100 píxeles de texto).
  No había ningún otro `ZIndex`/`InputTransparent` en el proyecto (revisado).
- **#3 Visitas — territorios paginados: HECHO Y VERIFICADO.** 25 por página con `‹ Anterior`,
  `Siguiente ›` y contador (`1–25 de 970`), en lugar de pintar 970 filas de golpe.
- **#5 Correspondencia — cuerpo con formato: HECHO Y VERIFICADO.** El `Editor` se sustituye por un
  **WebView contenteditable** con barra de formato (N, C, S, Título, Lista, Enlace, Quitar formato).
  El texto se guarda en la misma clave `Email_*` (HTML, igual que WPF). Verificado en el móvil: la
  barra aparece y el editor **carga el HTML existente** (texto pintado sobre el WebView).
- **#6 Archivo del circuito — todas las secciones: HECHO Y VERIFICADO.** Si no hay secciones, se
  **siembran las 15/17 de Windows** (con carpetas congregación × visitas en la sección 1) y se guardan
  en los JSON compartidos. Al **tocar una sección** se entra en ella (`CarpetaVfsPage`: CARPETAS /
  ARCHIVOS). Verificado en el móvil con la carpeta sincronizada real del usuario (15 secciones).
- **#7 Asambleas — las 4 secciones de Windows: HECHO Y VERIFICADO (2ª revisión).**
  Se estudió a fondo `SCCosmos/AsambleasPage.xaml(.cs)` (597 + 2380 líneas) y la página Android se
  rehízo como las **4 pestañas** de Windows:
  1. **Programa** (Año + Tipo): solo lista las secciones que **realmente tienen programa**
     ("2 secciones con programa · 20 partes · 9 con participantes"), en lugar de las 10 filas de la
     tabla. Tocar una abre el programa para editar.
  2. **Organigrama** (Año + Tipo): solo las secciones con organigrama ("2 secciones · 26 y 28
     asignaciones"). Tocar abre el organigrama para editar.
  3. **Oradores (matriz)**: reescrita con la lógica de Windows (antes mostraba los 410 ancianos):
     base = ancianos (+ SC y representante de Betel); se muestran **solo los que tienen participación
     real** en AC-BR / AC-CO o en la reunión de precursores, o los añadidos manualmente
     (`OradoresMatrizManuales` − `OradoresMatrizOcultos`). Columnas por **año con datos** con EV,
     AC-BR, R. Prec y AC-CO (colores por tipo de parte, igual que Windows), BOS editable por tocar,
     evaluación con interruptor, valores manuales editables (`MANUAL_*`), "＋ Añadir participante" y
     "Quitar". Verificado: **37 oradores con participación · años 2026, 2027**.
  4. **Reunión precursores** (Año): sesiones y sus partes del año, con acceso al editor completo.
  Nuevos métodos Android: `ObtenerAsambleasConDatosMobile()` (con contadores de partes/
  participantes/organigrama) y `ObtenerParticipacionesOradoresMobile()` (participaciones de asambleas
  y reuniones de precursores), y modelos `ParticipacionOradorMobile`, `FilaAnioOradorMobile`,
  `OradorMatrizMobile`. La matriz vive en `MatrizOradoresView` (ContentView) y se comparte entre la
  pestaña de Asambleas y su página propia.
- Compatibilidad: **0 errores** de compilación Android y WPF; `logcat` sin `FATAL EXCEPTION`.


### Todas las ventanas emergentes pasan a página normal (Android)
- Motivo: en Android 16 el **área táctil de las páginas modales va desplazada** (~100 px) y los botones
  inferiores (Cancelar/Guardar) **no respondían**: quedaban bajo la barra del sistema.
- Convertidas **todas** las llamadas `PushModalAsync(new NavigationPage(...))` → `PushAsync(...)` y
  `PopModalAsync()` → `PopAsync()` (15 archivos): editores de parte de asamblea, organigrama,
  congregación, secciones, actividad mensual, territorios, sesión/parte de precursores, filtros de
  personas, ciclo de visitas, formulario de semana, acciones de visita, importación JSON, visita
  detalle y programa de actividad.
- Las páginas que **ocultan su propia barra** (visita, ciclo de visitas e importación JSON) ahora
  llevan `Shell.NavBarIsVisible="False"` para no duplicar cabecera, y siguen saliendo con su botón
  «Cerrar»/«Cancelar».
- **Única excepción**: la **selección de perfil** del primer arranque sigue siendo modal, porque es una
  barrera que no debe poder saltarse con el botón Atrás (sus botones están en el centro, fuera de la
  zona conflictiva).
- Verificado en el emulador: editor de congregación (Cancelar), detalle de visita (una sola cabecera y
  Cerrar), y filtros de personas (Cancelar) responden y vuelven atrás sin excepciones.

### Notificaciones, perfil, Inicio y sincronización (Android y Windows)
1. **Notificaciones del sistema (Android)**: nuevo `AlertasNotificacionAndroid` + `AlertasNotificacionService`
   + `AlertasAlarmScheduler`/`AlertasAlarmReceiver`:
   - Se crea el canal `sccosmos_alertas` (con insignia activada), se pide el permiso de
     notificaciones en Android 13+ y se publica **una notificación por alerta pendiente** con
     `SetNumber(total)`, que es lo que muestran Samsung One UI y otros lanzadores como **contador en
     el icono**. Se actualiza al arrancar, al abrir Inicio y Alertas, al marcar como leídas (entonces
     se retiran) y con una **revisión automática diaria** (alarma inexacta, sin permisos extra).
   - **Verificado en el emulador**: permiso concedido, 3 alertas → 3 notificaciones con `number=3` y
     los textos reales («Documentación pendiente · Congregación Prueba», «Recordatorio de asamblea…»).
   - **Windows**: comprobación de alertas cada 15 minutos y el aviso de la campana **se abre solo**
     cuando aparece una alerta nueva (sin molestar al arrancar).
2. **Código de administrador (SC)**: en Configuración el campo del código solo aparece cuando el
   perfil elegido es **Hombre** (Android `PanelCodigoPerfil` + `PickerPerfil_SelectedIndexChanged`;
   Windows `PanelCodigoPerfil` + `CmbPerfilUsuario_SelectionChanged`). Verificado en el emulador:
   con Hombre se ve, al elegir Esposa desaparece.
3. **Inicio (Android) — botón «Ver visita»**: ahora aparece siempre en semanas de visita, aceptando
   cualquier variante de mayúsculas/espacios y el tipo «Visita SC» (como Windows). Además se
   encontró y corrigió el **fallo que lo impedía**: `DatabaseHelper.ObtenerSemana` leía la columna
   `TipoAsamblea` sin comprobar que existiera y lanzaba una excepción en bases anteriores a ese
   campo, dejando la tarjeta de la semana en «Cargando…». `ObtenerString`/`ObtenerBool` ahora buscan
   la columna y devuelven vacío/falso si falta (también admite columnas numéricas o de fecha).
   Verificado: Inicio muestra «VISITA A CONGREGACIÓN», congregación, acompañante y **Ver visita**.
4. **Cambios irreales del sincronizador de personas**: se comparan los textos **ignorando espacios
   sobrantes** (al principio, al final y duplicados por dentro) en los dos programas:
   - Android: 12 comparaciones de `JwHubSyncAnalysisService` usan el nuevo `MismoTexto`.
   - Windows: lo mismo en `SincronizadorJWHub.DetectarConflictoSincronizador` y en
     `SyncHubWindow.HayConflicto` (con `MismoTexto` / `MismoTextoSinMayusculas`).
   - En la pantalla de conflictos de Android la **fotografía aparece una sola vez, arriba**, junto al
     nombre; se han quitado los recuadros de foto que salían en cada campo (la foto solo se compara
     como campo en Windows, donde ya se mostraba aparte).
   - **Nombre común**: si la base de datos ya tiene un valor (puede haberlo puesto el superintendente
     a mano) **no se compara ni se ofrece como diferencia**; solo se toma de JW Hub cuando el campo
     está vacío (y entonces se propone el valor de Hub por defecto). Windows ya se comportaba así
     (`FormularioPersonaWindow`, `SyncHubWindow` y `SincronizadorJWHub`); se ha alineado Android
     (`JwHubSyncAnalysisService` y `JwHubSyncConflictPage`).

### Adaptación a tablet y plegables (Android)
- Nuevo `Services/AdaptadorPantalla.cs`, aplicado **desde un solo sitio** (`AppShell.OnNavigated` +
  `SizeChanged` de la página), así que vale para todas las páginas sin tocar cada XAML. Se aplica al
  vuelo cuando cambia la ventana (girar el móvil, **desplegar un plegable**, cambiar de tamaño en la
  tableta) y es idempotente.
- Reglas por ancho disponible (dp):
  - **< 600** (móvil / plegable cerrado): como hasta ahora, una columna.
  - **600-899** (plegable abierto / tableta pequeña): márgenes laterales mayores, formularios
    centrados con ancho máximo legible y **listas a 2 columnas**.
  - **>= 900** (tableta / escritorio): igual con **listas a 3 columnas**.
  - **>= 1000**: además el **menú lateral queda fijo** (`FlyoutBehavior.Locked`).
- Las listas que pertenecen a vistas de detalle (matriz de oradores, revisiones de importación,
  detalle de visita/persona) se mantienen a una columna a propósito: sus filas ya llevan mucha
  información.
- Conserva los márgenes originales de cada página y los restaura al volver a un tamaño estrecho
  (guarda el padding y la disposición de cada lista la primera vez que se toca).
- **Verificado en el emulador a tres tamaños**: 411 dp (móvil → 1 columna), 610 dp (→ 2 columnas,
  p. ej. «S-303 Y S-2» y «ARCHIVOS JUDICIALES» lado a lado) y 914 dp (→ 3 columnas: «S-303 Y S-2»,
  «ARCHIVOS JUDICIALES» y «ORADORES ASAMBLEAS» en la misma fila) con el contenido centrado; al
  volver al tamaño de móvil recupera la columna única. Sin excepciones en `logcat`.

### Archivo del circuito: estructura de carpetas como Windows (Android)
**Fallo real encontrado** (comparando con las 638 líneas de `ArchivoCircuitoPage.xaml.cs` de WPF):
- Windows guarda las carpetas y archivos de **primer nivel con `ParentId`/`CarpetaId` = `null`**
  (comprobado en `Archivo_CarpetasVFS.json` real: 19 raíces con `null`). Android usaba `""` como
  valor de raíz, así que **descartaba todas las carpetas de primer nivel**: la sección aparecía vacía
  o con las carpetas creadas desde el móvil invisibles para Windows (y al revés). Ahora los modelos
  usan `string?` (nulo = raíz), se tratan `null` y `""` como raíz al leer y se escribe `null` como
  Windows.
- Además: en la sección solo se listan los **archivos de primer nivel** (antes también salían los que
  estaban dentro de carpetas), se respeta el **orden del JSON de secciones** como WPF (sin ordenar),
  y los archivos subidos usan `Guid` **con guiones** + extensión, igual que WPF.
- **Nueva interfaz tipo Windows**: cada sección muestra su **árbol de carpetas y archivos** con
  iconos, plegable al tocar la carpeta, y con las mismas acciones que el menú contextual de Windows:
  ➕ nueva subcarpeta, ⬆️ subir archivo aquí, ✏️ renombrar y 🗑️ eliminar; en la cabecera de la
  sección, ➕/⬆️/✏️ (renombrar sección) y 🗑️ (eliminar sección y todo su contenido). Las carpetas
  de la sección SCS se listan en línea y las secciones «Hermanos con potencial» y «Problemas
  pendientes» tienen «✏️ Editar tabla» y «🧾 Generar PDF» (el PDF se añade a la sección, como WPF).
- **Verificado con los datos reales del usuario** (copiados al emulador): 15 secciones · 152 carpetas;
  despliegue de «Almendralejo» → «Entre visitas» + «Visita 1…6»; creación y borrado de una
  subcarpeta; y el JSON reescrito por Android mantiene `"ParentId":null` en las 19 raíces, de modo
  que Windows sigue viendo lo mismo. Sin excepciones en `logcat`.
- Diferencias que se mantienen conscientemente: en Android los archivos se **abren con la app del
  sistema** (no hay panel de previsualización integrado como el de Windows) y la generación de PDF
  solo está para las secciones 12 y 14 (las 4, 8 y 13 de Windows usan datos de otros módulos).

### Módulo nuevo: DONACIONES (Windows y Android)
- **Tabla compartida `Donaciones`** (mismas columnas en ambas apps, creada de forma defensiva como el
  resto de tablas opcionales): `Id, Fecha, PersonaId, NombrePersona, CongregacionCodigo,
  CongregacionNombre, Cantidad, MedioPago, Notas, FechaCreacion`. Al ser la misma tabla y las mismas
  columnas, la sincronización de `sccosmos.db` la transporta igual que el resto de módulos.
- **Registro**: quién dona (la **congregación se rellena automáticamente** con la de la persona),
  fecha, cantidad y medio de pago (**Efectivo** o **Bizum/Transferencia**). Si no se sabe quién dona
  se elige **Desconocido** (y entonces la congregación se puede elegir a mano).
- **Búsquedas**: por **año, mes, congregación, persona** (texto) y medio de pago, con botón de limpiar.
  La cabecera muestra el **total** de lo filtrado y el desglose por medio de pago.
- **Editar/eliminar**: tocar una donación abre el editor (con "Eliminar donación").
- **Windows**: `DonacionesPage.xaml(.cs)` + `DatabaseHelper.Donaciones.cs` (se añadió `partial` a la
  clase), con DataGrid, editor en línea y `ItemDonaciones` en el menú (icono Money) dentro del gating
  del perfil.
- **Android**: `DonacionesPage.xaml(.cs)` + `DonacionEdicionPage.xaml(.cs)` +
  `DatabaseHelper.Donaciones.cs` + `Models/DonacionModels.cs`, entrada **Donaciones** 💰 en el menú
  lateral y en el gating del perfil.
- **Aprendizajes/arreglos de esta tanda**:
  - Las cadenas `"""` de C# **no incluyen** el salto antes del cierre: en el SELECT se generaba
    `FROM DonacionesORDER BY …` y daba error de sintaxis. Corregido añadiendo el separador.
  - **Cantidades**: lector tolerante que acepta coma o punto decimal y separadores de millar
    (`30,50`, `30.50`, `1.500,25`), en lugar de fiarse de la cultura.
  - En este Android, las **páginas modales** desplazan el área táctil (~100 px) y los botones
    inferiores no reciben los toques: los editores nuevos se abren como **página normal**.
- **Verificado**: Android en emulador de extremo a extremo (alta con persona → congregación
  automática, 30,50 € en efectivo, total 61,00 € con dos altas, borrado de ambas y total 0,00 €,
  filtros y buscador visibles, sin excepciones). Windows: compilación 0 errores y SQL de la página
  (CREATE/SELECT con filtros/INSERT/DELETE/SUMA) probado contra una copia de la base compartida.

### Lote de correcciones y ajustes (sesión anterior)
- **Letras mal (ñ, acentos): CORREGIDO.** Causa real: las ediciones por script de la pasada visual
  volvieron a codificar 40 XAML (texto UTF-8 leído como Windows-1252), de modo que
  `CONGREGACIÓN` quedaba como `CONGREGACIÃ“N`. Se repararon los 40 archivos (reversión de la
  doble codificación + guardado UTF-8 con BOM) y se comprobó por puntos de código en el móvil:
  `Jonatán` (U+00E1) y `Sábado` (U+00E1) correctos. Ya no queda ningún archivo con mojibake.
- **Inicio**: fuera el subtítulo "Este es el resumen de tu circuito" y el botón de texto "Alertas"
  pasa a una **campana** 🔔 en la cabecera (abre la misma página de Alertas).
- **Calendario**: el antetítulo pasa a "CALENDARIO" (subtítulo "Ruta semanal del circuito").
- **Visitas**: nueva sección **Mapa** (igual que la pestaña Mapa de Windows): geocodifica las
  direcciones pendientes con Nominatim, guarda `Personas.Latitud/Longitud` y dibuja un mapa Leaflet
  con un marcador por publicador y otro rojo para el **Salón del Reino**; se retiró el bloque de
  **Tarjetas S-21-S**. Verificado en el móvil: "82 publicadores · todas las direcciones están
  geocodificadas" y las teselas del mapa cargan (25.813 colores distintos en el WebView).
- **Correspondencia**: antetítulo "CORRESPONDENCIA". **Circuito**: "CIRCUITO". **Escuelas**: "ESCUELAS".
- **Archivo del circuito**: fuera los botones "Hermanos con potencial" y "Problemas pendientes"
  (siguen accesibles como secciones); los botones de sección pasan a **iconos**: 📁 carpetas,
  ✏️ renombrar y ⬆️ subir (verificado: el icono se pinta en color, no es un cuadro).
- **Configuración**: eliminado todo el apartado de sincronización (Actualizar desde carpeta,
  Guardar cambios, Probar base de datos, Comprobar y el interruptor de sincronización automática).
  Queda "Seleccionar carpeta" con el estado **"✓ sccosmos.db encontrado y accesible" debajo**, que se
  calcula automáticamente al abrir la página y al elegir carpeta. La sincronización es siempre
  automática (`AutoSyncService.Activo = true`); además, la limpieza del análisis de JW Hub al
  importar una base nueva se movió al servicio automático para no perder ese control.

### Mejora visual página a página (sesión anterior)
- **Sistema de diseño nuevo**: `Resources/Styles/Diseno.xaml` con la paleta única (fondo, superficies,
  bordes, textos, acento, éxito, peligro) y estilos reutilizables: `Antetitulo`, `TituloPagina`,
  `SubtituloPagina`, `TituloTarjeta`, `TextoTarjeta`, `TextoNota`, `Tarjeta`, `TarjetaPlana`,
  `BotonPrincipal`, `BotonSecundario`, `BotonPeligro`, `BotonExito`, `BotonPestana`, `Chip`,
  `TextoAccion`, `Campo`, `CampoEditor`, `CampoPicker`.
- **Estilos globales** (`Styles.xaml`): campos (`Entry`/`Editor`/`Picker`/`DatePicker`/`TimePicker`/
  `SearchBar`) con fondo visible `#14181E` y marcador de agua, botones con métricas comunes, acentos
  en `Switch`, `CheckBox`, `ActivityIndicator` y `RefreshView`. Verificado por píxeles en el móvil:
  el fondo del campo es `#14181E` (antes era transparente).
- **Menú lateral con iconos** (`IconoMenuConverter` + `Shell.ItemTemplate`): cada módulo con su icono
  y tipografía uniforme.
- **Barrido en 42 archivos XAML**: antetítulos de sección unificados (`Antetitulo`, 11 px, con
  espaciado) y colores de tarjetas/bordes por tokens (`Superficie`, `SuperficieAlta`, `Borde`).
- **Retoques manuales** en las páginas principales: Inicio, Calendario, Personas, Correspondencia,
  Circuito, Archivo, Escuelas, Alertas, Asambleas (pestañas), Matriz de oradores, VisitaDetalle y
  Configuración (títulos, subtítulos, botones primario/secundario y tarjetas).
- Verificado en el móvil real: Escuelas, Correspondencia, Circuito, Asambleas e Inicio se pintan con
  el nuevo estilo y `logcat` sin excepciones.

## 9. AUDITORÍA FINAL

Realizada sobre `emulator-5554` con la BD sintética:

- **Navegación**: recorridos los 8 módulos del flyout (Inicio, Calendario, Personas,
  Correspondencia, Circuito, Asambleas, Escuelas, Configuración) más Alertas, detalle de persona,
  detalle de visita, programa, territorios, S-21, Asamblea (programa/organigrama), Precursores,
  sesión de precursores y sus partes. **0 excepciones** en `logcat`; proceso vivo.
- **Compatibilidad de esquema**: `PRAGMA integrity_check = ok`. Las escrituras móviles usan exactamente
  las columnas de WPF (incluida `ProgramaVisitaV2.Direccion`). No se crean ni renombran columnas de las
  tablas propiedad del PC salvo creación defensiva ya existente.
- **Regresiones**: `dotnet build -f net10.0-android` → **0 errores** en cada bloque; app estable.
- **Sin datos reales**: uso exclusivo de BD sintética; cuentas JW Hub no empleadas.

Cobertura de pruebas por escritura a BD (ver §8): Escuelas (descartar), Correspondencia (guardar
plantilla), programa de la visita (importar JSON), Asambleas (alta/borrado de partes, alta de
organigrama, alta de sesión y alta de parte de sesión), **Personas (traslado y fusión)**.

Actualización posterior: añadidas extracción de `Sexo` y conflicto de foto en JW Hub (compila, no
verificable sin cuenta real), campos de origen + historial en Personas, y notificación de asamblea.
Tras estos cambios, la app arranca y navega por los 8 módulos sin excepciones (reauditoría de
navegación OK, proceso vivo).

Riesgos/bloqueos abiertos: §6.1 (Archivo), §6.2 (Descargas), JW Hub no verificable, y campos de
persona de alto riesgo de compatibilidad.

---

## 7. PRÓXIMA TAREA

Todas las prioridades 1–3 están implementadas; de la prioridad 4 quedan:

Implementadas en la última tanda (solicitadas por el usuario):

- **JW Hub**: extracción de `Sexo` (página "Información personal", no crítica) y **conflicto de foto
  reactivado** usando `JwHubPhotoComparer` (dHash). Compila; **no verificable sin cuenta JW Hub real**.
- **Personas**: campos de origen (`OrigenRegistro`) + `AprobadoExhibidores` + `RequisitoHoras`,
  **historial** (instantáneas), **traslados** y **fusión** (estos dos verificados en emulador).
- **Notificación de asamblea de circuito**: desde `AccionesVisitaPage` (categoría "Notificación AC",
  con adjuntos y destinatarios de la sección; estado `NotificacionEnviada`).

Quedan únicamente, y **requieren decisión del usuario**:

1. **Archivo del circuito** (§6.1) — bloqueado: sus datos no están en SQLite; habría que ampliar el
   transporte de sincronización (p. ej. un ZIP de la carpeta del circuito).
2. **Descargas JW Stream** (§6.2) — no viable en Android (Selenium/ffmpeg).

Recomendación: al no quedar funcionalidades implementables de forma segura sin intervención, procede
la **auditoría final** (§9) y la validación manual en dispositivo de los flujos ya probados por
automatización.
2. **JW Hub pendiente** (mejoras): extracción de `Sexo`, reactivar conflicto de foto
   (`JwHubPhotoComparer`). Toca archivos Android grandes y **no es verificable sin cuenta real**;
   dejar para el final o bajo indicación del usuario.
3. **"Notificación de asamblea de circuito"**: en WPF no hay una clave de plantilla clara (se
   gestiona dentro del módulo de Asambleas). Pendiente de aclarar; baja prioridad.
4. **Archivo del circuito** y **Descargas** → bloqueadas/no viables (§6). Requieren decisión del usuario.

Ciclo de trabajo actualizado: **compilar → instalar en `emulator-5554` → lanzar → `uiautomator dump`
+ `adb logcat` → corregir → repetir** (ver §8).

Notas:
- El emulador ya permite ver la app en ejecución; quedan por ejercitar los flujos de escritura largos.
- La aprobación/cancelación de nombramientos sigue **bloqueada** (requiere Selenium/OWA de WPF).
- Probado end-to-end el round-trip de escritura a la BD (Escuelas "Descartar"); conviene repetirlo con
  más formularios cuando se estabilicen.

### Búsqueda de persona en Donaciones y página GASTOS (sesión actual)

- **Donaciones - búsqueda de persona** (Android `DonacionEdicionPage` y Windows
  `DonacionesPage`): buscador encima del selector de persona; filtra por nombre o
  congregación sin distinguir mayúsculas ni tildes (`NormalizarBusqueda` con
  `NormalizationForm.FormD`); `(Desconocido)` sigue siempre disponible; la selección
  no se pierde al filtrar (`_personaSeleccionada`) y es la que se guarda.
- **Página GASTOS (Contabilidad) con entrada directa en el menú** — pedida para que la
  esposa del SC también la vea; visible para ambos perfiles (no se oculta en el filtro
  de acceso):
  - **Windows**: `GastosPage.xaml(.cs)`; entrada **Gastos** en el menú (icono
    Calculator), selector de congregación, año de servicio (12 meses de septiembre a
    agosto) y DataGrid con las mismas columnas que la pestaña Contabilidad de Visitas
    (reutiliza `VisitasPage.FilaContabilidad` y `DatabaseHelper.ObtenerContabilidad` /
    `GuardarContabilidad`); guarda al terminar de editar cada celda.
  - **Android**: `Views/GastosPage.xaml(.cs)`; entrada **Gastos** (🧾) en el menú
    lateral; selector de congregación, mes (últimos 6 meses) y tarjetas editables con
    `ObtenerContabilidadVisitaEditableMobile` / `GuardarContabilidadVisitaMobile`;
    promedio por publicador calculado para la congregación elegida.
### Visitas: perfil de la esposa, edición de personas y mes de actividad (sesión actual)

1. **Perfil limitado (esposa) en la página de visitas** — solo ve *datos de la
   congregación*, *resumen de la congregación*, *Ministerio* (sin los cuadros de las
   horas) y *Programa de la visita*.
   - Android (`VisitaDetallePage`): `AplicarPerfilAcceso()` oculta Notas, Territorios,
     Mapa, Contabilidad y S-303; `PersonaVisitaUi.MostrarCuadrosHoras` oculta los 6
     cuadros de horas y la advertencia ⚠ de cada persona.
   - Windows (`VisitasPage`): `AplicarPerfilAccesoVisita()` oculta las pestañas Mapa,
     Notas, Territorios, Contabilidad y S-303; `AjustarVisibilidadColumnas` oculta las
     columnas de los 6 meses y "Terrs." (`EsColumnaDeHoras`).
2. **Editar una persona desde la visita (Android)**: al pulsar el nombre o la foto de una
   persona se abre `PersonaDetallePage`. `PersonaVisitaUi.PersonaId` guarda la raíz
   (`PersonaActividadId`) y se resuelve la ficha actual con
   `DatabaseHelper.ObtenerIdPersonaActualMobile` (instantáneas históricas incluidas); al
   volver se recarga el mes (`_recargarMinisterioAlVolver`).
3. **Cuadros de los 6 meses = último mes con actividad** (igual que Windows):
   - Nuevo `DatabaseHelper.ObtenerMaximoMesActividadMobile` (participación u horas > 0).
   - `ObtenerPersonasMinisterioVisitaMobile` acepta `mesFinCuadros` y los cuadros
     terminan en `min(mes seleccionado, último mes con actividad)`. Antes terminaban
     siempre en el mes de la visita: en octubre se veían meses sin datos y al retroceder
     un mes no aparecía nada.
**Verificado en el emulador con la BD real copiada** (perfil Esposa y perfil Hombre):
- Esposa: en la visita solo aparecen *Datos*, *Resumen de la congregación*, *Grupos de
  predicación*, *Ministerio* (sin los cuadros de meses ni la advertencia ⚠) y *Programa*;
  Notas, Territorios, Mapa, Contabilidad y S-303 no aparecen.
- Pulsar el nombre (o la foto) de una persona abre su ficha para editarla; al volver se
  recarga el mes.
- Ministerio con la congregación Cáceres - Norte (visita de octubre): los cuadros terminan
  en **Ago 26** (último mes con registro) en vez de Oct 26; al pulsar ‹ el mes pasa a
  Septiembre/Agosto 2026 y los cuadros siguen con datos; en Julio 2026 los cuadros pasan a
  Feb 26–Jul 26. La lista de personas sigue apareciendo en todos los meses.
### Corrección: personas que faltaban en meses anteriores + Calendario de la esposa

- **Ministerio, meses anteriores**: solo aparecía una persona (p. ej. "Sonia Arrabal").
  Causa: en `CargarPersonasVistaMobile` se usaba la *primera instantánea histórica*
  como si fuera la fecha de alta; como la mayoría de fichas no tiene `FechaCreacion`,
  su primera instantánea (2026-10) era posterior al mes consultado y se descartaba a
  todos. Ahora el filtro de alta solo se aplica cuando la `FechaCreacion` es conocida
  (y la instantánea más antigua solo rebaja ese límite). Verificado con Cáceres - Norte:
  Septiembre 2026 vuelve a mostrar a todos (grupo 1: 21 personas).
- **Calendario con perfil limitado (esposa)**: en la semana solo queda el botón
  **Ver visita**. Se ocultan editar semana (✏️), acciones (📨), importar JSON (🗂️),
  añadir (➕) y eliminar (🗑️); también "Ciclo", "Hoy" y "Cargar 6 meses más" (el rango
  que se carga ya cubre ±6 meses) y la tarjeta de la semana ya no abre la edición.
  Android: propiedades `MostrarBoton*` en `CalendarioItem` + `BtnCiclo`/`BtnHoy`/
  `BtnCargarPasado`/`BtnCargarFuturo`. Windows: `VisibilidadAccionesSoloSC` en
  `SemanaCalendario` + botones superiores y `FilaSemana_Click`.- **Verificado en el móvil real** (APK Release arm64 instalada el 14/10 17:00): en la visita
  de Cáceres - Norte, Julio 2026 muestra varias personas (Ramón Ramos, Sonia Arrabal,
  Steven Ronquillo...) con los cuadros Feb–Jul; en Octubre (mes de la visita) los cuadros
  terminan en Ago 26. Calendario con perfil Esposa (emulador): solo aparecen los botones
  «Ver visita»; ni ✏️ 📨 🗂️ ➕ 🗑️ ni Ciclo/Hoy/Cargar meses, y la tarjeta de la semana no
  abre la edición.
### Al elegir la carpeta de la base de datos, los datos se usan al momento

- **Android**: al pulsar "Seleccionar carpeta" solo se comprobaba que existiera
  `sccosmos.db`: la importación se hacía únicamente al arrancar la app, así que había que
  reiniciarla para ver los datos de la carpeta. Ahora, tras elegir la carpeta, se importa
  al momento (`AutoSyncService.ImportarAhoraAsync`), se dejan los hashes al día y se abre
  Inicio con los datos nuevos. Si en el móvil hay cambios sin enviar, se pregunta si usar
  la base de la carpeta o mantener la del móvil.
- **Android (automático)**: la sincronización ya no solo exporta; cada ~2 minutos comprueba
  también si la carpeta (el PC) dejó una base de datos más reciente y la importa sola
  (`ImportarSiHayCambiosAsync`), siempre que no haya cambios locales pendientes.
- **Windows**: al cambiar la carpeta de datos se prepara la base de la nueva ubicación
  (`InicializarDatabase`) y se recarga la ventana principal (Inicio), sin reiniciar el
  programa.
**Detalle del arreglo de la sincronización automática** (probado en el emulador con la BD real):
- El ciclo automático comprobaba "¿el móvil tiene cambios pendientes?" comparando el hash de
  la base local con el hash *del archivo de la carpeta*; como al importar/exportar la base
  local se reescribe (copia SQLite), los hashes nunca coincidían y el ciclo entendía que había
  cambios locales pendientes: por eso los cambios del PC no entraban nunca solos. Ahora se
  compara con el último estado local sincronizado (`SCCosmos_UltimoHashLocal`) y, si nunca se
  ha sincronizado, se importa cuando la base local está vacía.
- Probado en vivo: con la app abierta, cambié la base de la carpeta (nombre "PRUEBA VIVO") y
  en ~2 minutos la app la importó sola, sin reiniciar.
- Si la importación falla, el estado de Configuración lo dice ("No se pudo actualizar desde la
  carpeta: ...").
### Saludo de Inicio con el nombre de la esposa

- Si el dispositivo está configurado con el perfil **Esposa**, en Inicio el saludo usa la
  configuración "NombreEsposaSC" (Configuración → *Nombre de la esposa*); si está vacía, se
  usa el nombre del SC. Con perfil Hombre (o sin perfil) se sigue usando "NombreSC".
- Android: `InicioPage` (se recalcula al entrar en Inicio, así que cambia al instante al
  cambiar de perfil). Windows: `InicioPage` (constructor).
### Lote «vista de la esposa» (Android y Windows)

1. **Visitas**: la esposa no ve el selector de meses del Ministerio (Android: `CintaMesMinisterio`; Windows: `CintaMesMinisterio` en VisitasPage).
2. **Fichas de personas**: al editar una persona (desde Visitas o desde Personas) no ve la *actividad mensual* (Android: `SeccionActividad` en PersonaDetallePage; Windows: pestaña «Actividad» de FormularioPersonaWindow).
3. **Personas**: no ve los nombramientos (Android: se quita el elemento de barra «Nombramientos» y los botones de JW Hub; Windows: pestaña «Nombramientos y bajas» y `BtnSincronizarHub`).
4. **Gastos eliminado**: se borró `GastosPage` en las dos apps (menú incluido) y **Donaciones** queda visible para ambos perfiles (Android `ItemDonaciones.IsVisible = true`; Windows `ItemDonaciones.Visibility = Visible`).
5. **Notificaciones**: la esposa no ve la campana ni recibe avisos (Android: `PanelCampana` oculto + `AlertasNotificacionService` cancela y no publica; Windows: `BtnAlertas` oculto + `VerificarAlertas` no hace nada).
6. **Calendario**: el botón «Ver visita» solo aparece si la semana es visita **y** tiene datos introducidos (Android `CalendarioItem.MostrarVerVisita`; Windows ya lo hacía con `VisibilidadVerVisita`). Vale para los dos perfiles.
7. **Configuración**: la esposa no ve el apartado JW Hub ni los datos del SC/circuito (solo su nombre): Android `PanelJwHub`/`TituloJwHub`/`PanelDatosSC`/`PanelDatosSCFinal`; Windows `CardJwHub`, `CardArchivoCircuito`, `ToggleSincronizacion` y los campos Nº circuito / nombre y apellido del SC.
**Verificado en el móvil real** (perfil Esposa, cambiado desde la propia app y restaurado después a Hombre + código):
- Menú: Inicio, Calendario, Personas y Donaciones (sin Correspondencia, Circuito, Archivo, Asambleas, Escuelas ni Gastos).
- Inicio: «¡Hola, Rebeca!» y sin campana; sin notificaciones de la app (`dumpsys notification` limpio).
- Calendario: solo los botones «Ver visita» de las semanas con datos (Jerez 29-sep y Cáceres 6-oct), sin ningún otro botón; comprobado también con el perfil Hombre que San Vicente (20-oct, sin datos) no muestra el ojo.
- Visita: sin selector de meses, sin cuadros de meses, sin Notas/Territorios/Mapa/Contabilidad/S-303.
- Personas: sin «Nombramientos» ni «Sincronizar JW Hub»; la ficha de una persona no muestra «ACTIVIDAD MENSUAL».
- Configuración: sin JW Hub y sin los datos del SC/circuito, solo «Nombre de la esposa» y «Apellidos de la esposa» con el botón de guardar.
### Arreglo: la página de Configuración de Windows se cerraba al abrirla

Dos fallos en `ConfiguracionPage`:
1. **XAML corrupto**: el botón «Guardar perfil» terminaba en `/>/>` (un `/>` de más), lo que
   lanzaba `XamlParseException` («no se puede agregar una instancia de tipo String a una
   colección de tipo UIElementCollection») al cargar la página.
2. **NullReferenceException de orden de carga**: el `ComboBox` de perfil dispara
   `SelectionChanged` mientras se construye el XAML, antes de que exista
   `PanelCodigoPerfil`; `ActualizarPanelCodigoPerfil` lo tocaba sin comprobarlo.

Arreglado: quitado el `/>` sobrante y añadidas comprobaciones de `null` en
`ActualizarPanelCodigoPerfil` y `ActualizarEstadoPerfil`. Verificado lanzando la app y
abriendo Configuración y Personas (la app ya no se cierra).
### Error "attempt to write a readonly database" y guardado por cambio (Android)

- **Causa**: la base local se abría en modo `Mode=ReadOnly` en dos sitios (la comprobación de
  base vacía y el snapshot de la exportación) y, al importar, la app **sustituye el archivo**
  (`File.Move`) sin vaciar el *pool* de conexiones de Microsoft.Data.Sqlite: las conexiones
  siguientes reutilizaban la del archivo anterior → las escrituras fallaban con
  "attempt to write a readonly database" (lo que se veía al abrir la app y volver a escribir).
- **Arreglo**: la base local ya no se abre nunca en solo lectura y, justo después de sustituirla,
  se llama a `SqliteConnection.ClearAllPools()` (se descartan las conexiones viejas).
- **Guardar por cambio**: el ciclo de sincronización pasa de 20 s a **5 s**, y antes de nada
  compara la **fecha del archivo** de la base (barato): si no ha cambiado desde el último envío
  no hace nada; si ha cambiado, la sube a la carpeta al momento. La comprobación costosa (hash
  del archivo de la carpeta) sigue cada ~2 minutos.
### Comportamiento con cambios simultáneos en dos dispositivos

- Cada dispositivo tiene su copia local y sube/baja el archivo completo de la carpeta compartida.
- Si dos dispositivos cambian casi a la vez, el primero que sube gana; el segundo detecta
  "la base de la carpeta cambió desde mi última actualización" y **no sube** (protección para no
  pisar los cambios del otro). Sus cambios quedan a salvo en su copia local y Configuración
  muestra "Cambios locales pendientes de enviar…".
- No hay mezcla automática por filas (las claves/Id de cada dispositivo chocarían), así que el
  segundo debe resolverse desde Configuración: usar la de la carpeta (pierde sus cambios) o
  mantener los suyos (no sube hasta que la carpeta vuelva al estado esperado).
- Ajuste añadido: cuando el envío se rechaza, se apunta la marca del archivo para no recalcular
  el hash (27 MB) cada 5 s; se reintenta cuando haya un cambio local nuevo.
### Fusión automática de cambios entre dispositivos (Syncthing)

La carpeta compartida se sincroniza con Syncthing, que puede tardar hasta un minuto: antes,
si dos dispositivos cambiaban algo a la vez, el segundo dejaba de subir y había que
resolverlo a mano. Ahora se combina solo:

- **Registro de cambios**: la base local lleva una tabla `SCCosmos_RegistroCambios` y
  disparadores que anotan qué fila se crea (I), modifica (U) o borra (D) desde la última
  sincronización. Solo vive en el móvil: antes de subir se limpia de la copia.
- **Fusión** (`FusionBaseDatosService`): se descarga la base de la carpeta, se parte de ella
  (los cambios del otro dispositivo se conservan) y encima se aplican los cambios de aquí:
  - filas modificadas aquí → ganan las de aquí;
  - filas borradas aquí que ya existían → se borran;
  - filas nuevas de aquí y del otro → se conservan las dos; si el identificador coincide, la
    de aquí se renumera y se corrigen las claves ajenas (`PersonaId`, `ConyugeId`,
    `SemanaId`, `AsignadoId`…);
  - lo que solo tocó el otro dispositivo se queda tal cual.
- **Cuándo se ejecuta**: al detectar el conflicto (otro dispositivo cambió la carpeta mientras
  aquí había cambios) y cada ~2 minutos mientras quede algo pendiente. Después sube el
  resultado. Se guarda copia de seguridad de la base antes de fusionar (carpeta `copias`,
  las 3 últimas).
- **Probado** con un escenario de dos dispositivos (proyecto de prueba aparte, sin tocar datos
  reales): 9 de 9 comprobaciones, incluida la renumeración de altas coincidentes y la
  corrección de la clave ajena de una donación nueva.
### Calendario: carga por scroll, sin recordatorio, correo por Gmail y volver a la semana

1. **Carga automática**: fuera los botones «Cargar 6 meses más». Al acercarse al principio o al
   final de la lista se cargan tres meses más solos (`ListaCalendario_Scrolled` +
   `ExtenderRangoAsync`); al cargar hacia atrás se mantiene la fecha que se estaba viendo.
   Con protección (`_cargandoCalendario`/`_extendiendo`) para no encadenar recargas.
2. **Recordatorio eliminado**: en Acciones de la semana (visitas y asambleas) ya no aparecen
   «Recordatorio», «Preparar recordatorio por correo» ni el interruptor «Recordatorio enviado».
   En Windows se ha quitado también el botón «Enviar recordatorio asamblea» del Calendario.
3. **Correo con la app de Gmail**: `CorreoService` intenta abrir Gmail
   (`com.google.android.gm`) con destinatarios, asunto, cuerpo HTML y adjuntos (por el
   FileProvider de MAUI); si Gmail no está instalado, usa el comportamiento normal de Android.
4. **Volver a la semana**: al editar una semana, ver una visita o abrir sus acciones se guarda
   esa semana (`_anclaSemana`) y al volver se deja en pantalla esa misma semana; «Hoy» sigue
   llevando a la semana actual.
### Archivo del circuito: plegar cualquier sección + organigrama igual que Windows

- **Archivador**: cada sección se puede plegar y desplegar pulsando su nombre (indicador ▾/▸);
  también la que aparece abierta al entrar. Nuevas propiedades `Abierta`, `IconoFlecha`,
  `MostrarResumen`/`MostrarScs`/`MostrarTablaEspecial` en `SeccionVfsUi` y
  `Seccion_Tapped`.
- **Organigrama de asambleas**: `AsambleaPdfService.GenerarOrganigrama` era una tabla simple;
  ahora es el **mismo diseño que el PDF de Windows** (portado del código de `AsambleasPage`):
  título centrado, cabecera fecha/lugar, dos columnas al 50 %, cajas por departamento con los
  colores de Windows (superintendente, auxiliar, departamentos azul/naranja, auditor,
  seguridad), personas con nombre común o formateado (+ año), congregación, teléfono y correo,
  y las líneas de unión del árbol (`LineaArbolRenderer`/`HuecoRaizRenderer`/
  `HuecoApiladoRenderer`). Datos de persona con
  `DatabaseHelper.ObtenerInfoHermanoOrganigramaMobile` (nuevo). Fuente: Roboto del sistema
  (Segoe UI no existe en Android) con Helvetica como respaldo.
- Pendiente: comprobar en dispositivo la generación del PDF; el **programa** de la asamblea
  sigue con el formato anterior (falta portarlo igual que el organigrama).
### Programa de asamblea portado + pruebas en el móvil real (15/10/2026)

- **Programa**: `AsambleaPdfService.GenerarPrograma` ya es el mismo diseño que Windows
  (título centrado, texto temático entre paréntesis, y tabla maestra 1:3:2:3 con los turnos
  MAÑANA/TARDE y cualquier otro turno al final). Mismo motor y misma fuente que el organigrama.
- **Probado en el móvil** (APK Release 15/10 10:19):
  - Archivador: las secciones se pliegan y despliegan pulsando su nombre (▸/▾ y el árbol se
    oculta). El resumen propio de la sección también se oculta.
  - Organigrama y programa: se generan en el teléfono sin errores y aparecen en el panel de
    compartir como `Asamblea_7_organigrama.pdf` y `Asamblea_7_programa.pdf`.
  - Gmail: «Enviar asignación» abre directamente Gmail
    (`com.google.android.gm/.ComposeActivityGmailExternal`) con los destinatarios, el asunto
    y el cuerpo ya puestos.
  - Calendario: sin botones «Cargar 6 meses más»; al subir por la lista se cargaron solos 12
    meses más (de octubre 2026 hasta abril 2025).
  - Comprobado también que en semanas sin datos (agosto 2026) no aparece el botón «Ver visita».
- Pendiente de ver en el móvil: volver a la semana que se estaba viendo (no dio tiempo a
  llegar a una semana con visita en la prueba) y el aspecto de los PDF comparado con Windows.
### Calendario: la carga por scroll ya no salta de sitio

- **Causa**: al ampliar el rango se reconstruía la lista entera (`ItemsSource = null` y
  volver a asignarla), así que la posición del scroll se perdía y saltaba al principio; eso
  volvía a disparar la carga automática (parecía que cargaba meses sin parar).
- **Arreglo**: `_grupos` pasa a ser `ObservableCollection` y la ampliación **solo añade los
  grupos nuevos**:
  - Bajando (futuro): se **añaden al final**, así la lista no se mueve y no hay salto.
  - Subiendo (pasado): se **insertan al principio** y después se vuelve a poner arriba la misma
    semana que se estaba viendo (`PrimeraSemanaVisible` calcula la fecha a partir de la
    posición de la lista aplanada).
- Pendiente: probarlo en el móvil (se desconectó durante la prueba).
**Verificado en el emulador (15/10/2026, APK Debug con el arreglo)**:
- Bajando (futuro) con deslizamientos lentos: la primera semana visible fue avanzando sin retroceder
  (19-oct → 18-ene → 24-may → 11-oct → 21-feb) y los meses visibles NOV 2026 → FEB 2027 → JUN 2027
  → NOV 2027 → MAR 2028: se amplió varias veces (más de un año) sin que la lista saltara.
- Subiendo (pasado): la primera semana fue retrocediendo de forma continua (21-feb → 11-oct →
  24-may → 18-ene → 19-oct y luego hasta agosto 2025), también al cruzar el borde donde se amplía:
  la vista se queda en la misma semana.
- Nota para las pruebas: los `adb shell input swipe` rápidos abren el editor de la semana (la
  tarjeta tiene un gesto de pulsación); con deslizamientos lentos (800 ms) no ocurre.
### Carga por scroll también en Windows (Calendario)

- `CalendarioPage` (WPF): se engancha `ScrollChanged` al ScrollViewer del DataGrid y, al
  acercarse al principio (offset ≤ 2) o al final (≤ 120 px restantes), se llaman los cargadores
  que ya existían (`BtnCargarPasado_Click`/`BtnCargarFuturo_Click`): insertan 26 semanas
  sin reconstruir la lista y el de pasado compensa la posición (`ScrollToVerticalOffset`).
- Dos guardas para no encadenar cargas: solo se reacciona a movimientos reales de la barra
  (`VerticalChange != 0`) y hay una espera mínima de 1,5 s entre ampliaciones. (Sin ellas, el
  primer intento entraba en bucle y la ventana se quedaba colgada).
- Los botones «Cargar 6 meses más» siguen ahí (ya no hacen falta, pero se pueden usar).
- **Verificado** con automatización sobre la app: al llevar la barra al 100 % el porcentaje baja
  (68,6 % → 76,1 % → 80,7 %) = se añaden 26 semanas cada vez y la posición se mantiene; al
  llevarla al 0 % el porcentaje sube (16,2 % → 13,9 %) = se añaden por arriba con la posición
  compensada; la ventana sigue respondiendo.
### Scroll de Windows más fluido y sin botones (corrección)

- Al llegar arriba o abajo había un tirón: la ampliación corría entera en el hilo de la
  interfaz (26 consultas + `_vistaAgrupada.Refresh()`, que reagrupaba toda la tabla).
- Ahora `AmpliarRangoAsync`: consulta las 26 semanas en segundo plano (`Task.Run`),
  inserta en la lista sin `Refresh` y, al añadir por arriba, compensa la posición tras
  el layout (`Dispatcher.InvokeAsync` + `ScrollToVerticalOffset`).
- Se quitaron los botones «Cargar 6 meses más» (XAML y code-behind).
- **Verificado** con automatización: al 100 % → 67,7 % → 75,6 % → 80,4 % (26 semanas por
  vez y posición mantenida); al 0 % → 16,4 % → 14,1 % (por arriba con posición
  compensada); `SetScrollPercent` ya no bloquea (0-13 ms).
### Calendario de Windows sin tirones (solución definitiva)

- El tirón al llegar arriba o abajo venía de las **cargas al vuelo**: cada alta de semanas
  obligaba al panel agrupado de WPF a re-medir toda la tabla (medido: 160-460 ms por carga,
  y crecía con los meses ya cargados). Ya no hay ninguna carga durante el scroll.
- Ahora se carga **todo el rango de una vez** al abrir la página: desde un año antes de los
  datos hasta un año después (base real: sep-2025 → ago-2027). Una sola consulta nueva
  (`DatabaseHelper.ObtenerTodasLasSemanas`, 93 filas en ~13 ms) y la tabla se rellena antes
  de enlazarse.
- Se eliminó todo el mecanismo de carga automática (`ScrollChanged`, rueda, márgenes,
  encadenado) que se había añadido antes; el aviso «Cargando…» cubre la primera medida de la
  tabla al abrir la página.
- **Verificado** con rueda real por automatización, recorriendo la lista entera en ambos
  sentidos: 0 congelaciones >40 ms y respuesta de la interfaz 0-32 ms; abrir y cerrar el
  editor de una semana (con Cancelar, sin guardar) sigue funcionando.
- Los extremos del calendario ahora son los del rango cargado (datos ±1 año); para ampliarlo
  basta cambiar los márgenes `-52 * 7` / `52 * 7` de `InicializarCalendarioRangoFijo`.
### Calendario de Windows: apertura instantánea con «Cargando…»

- La página ya no bloquea la navegación: antes se rellenaba y medía la tabla *antes* de que la
  página apareciera. Ahora se abre al instante con el aviso «Cargando…» y los datos se preparan
  en segundo plano (`ConstruirSemanasAsync` con `Task.Run`).
- Cronología real medida (hitos internos): constructor + Loaded ≈ 30 ms; datos de la BD
  preparados (210 semanas) ≈ 4-6 ms; volcado (con la tabla desenlazada: `ItemsSource = null`
  → relleno → reenlazar) ≈ 12 ms; primera medida de la tabla ≈ 2,4 s (Release) / 2,8 s (Debug).
  Esa primera medida es lo único que tarda y ocurre detrás del aviso.
- Después de la medida se fuerza `UpdateLayout` y se navega al mes actual, todo antes de
  retirar el aviso.
- Nota: con la agrupación activada (encabezados de mes), WPF realiza **todas** las filas al
  medir (210 de 210 contenedores); sin agrupar realizaría solo ~36 y la primera medida bajaría
  a ~800 ms, pero habría que convertir los encabezados de mes en filas normales.
- **Verificado** en la compilación definitiva: apertura instantánea, mes actual colocado
  (60,3 %), barrido con rueda sin picos y editor de semana abre/cierra con Cancelar.
### Calendario de Windows: encabezados de mes como filas (sin agrupación)

- Se quitó la agrupación del DataGrid (`GroupDescriptions`) y se retiró el `GroupStyle`: los
  encabezados de mes ahora son **filas propias** (`SemanaCalendario.EsBanner`, `Id = -1`) que
  se insertan justo antes de la primera semana de cada mes (misma regla de corte de antes: el mes
  del lunes de la semana).
- La franja del encabezado se dibuja dentro de la primera celda con un `Border` cuyo ancho se
  enlaza a `ScrollViewer.ViewportWidth`; las demás celdas de esa fila se ocultan con el estilo
  `CeldaSoloSemana`. Verificado por automatización: el texto queda centrado en el ancho total de
  la tabla.
- Las filas de encabezado no abren el editor (guarda en `FilaSemana_Click`) y
  `RecargarFechaEspecifica` ya no las borra al recargar una fecha.
- **Resultado medido**: con la agrupación, WPF realizaba las 259 filas al medir; ahora virtualiza
  (~38 contenedores). La apertura pasa de ~4,7 s (método externo) a ~1,1 s.
- Verificado: encabezados visibles y centrados, mes inicial en 60,1 %, barrido con rueda,
  editor abre/cierra con Cancelar y pulsar un encabezado no abre nada.
### Revertido: encabezados de mes como filas (se mantiene la apertura con «Cargando…»)

- La prueba de quitar la agrupación (para acelerar la apertura) se revirtió por completo: el
  aspecto de los encabezados de mes y el tacto del scroll no eran aceptables (con la lista
  virtualizada, el avance por muesca de rueda se volvía impreciso y a saltos).
- Todo vuelve a la agrupación original (`GroupDescriptions`, `GroupStyle`, columna «Semana»
  de texto) y se eliminaron `EsBanner`, el estilo `CeldaSoloSemana` y la inserción de filas
  de encabezado.
- Se mantiene lo que sí se pidió: la página se abre al instante con el aviso «Cargando…» y los
  datos se preparan en segundo plano (la primera medida de la tabla agrupada, ~2,4-2,8 s, ocurre
  detrás del aviso).
- Verificado tras el revert: mes inicial en 60,3 %, 48 encabezados de mes en pantalla, barrido
  con rueda en ambos sentidos, editor abre/cierra.
### Calendario de Windows: entrar primero, cargar después (arreglo de la entrada lenta)

- El problema real: el trabajo pesado (volcado + primera medida de la tabla agrupada, ~2,4 s)
  se ejecutaba **antes de que la página llegara a dibujarse**, así que se veían 3-4 s de nada
  y el aviso «Cargando…» no llegaba a aparecer.
- Arreglo: `CargarCalendarioAsync` espera a que la página se pinte
  (`await Dispatcher.InvokeAsync(…, DispatcherPriority.Background)`) y solo entonces forma la
  tabla. La espera corre a prioridad inferior a la del dibujado, así que el primer fotograma
  (página + «Cargando…») se pinta antes de empezar.
- Cronología interna medida: clic → página dibujada con el aviso **~310 ms**; datos preparados
  +30 ms; primera medida de la tabla 2,44 s (detrás del aviso); total utilizable ~2,8 s.
- Verificado además: mes inicial en 60,3 %, barrido con rueda en ambos sentidos y la app
  responde.
### Carga de todas las páginas del menú (medición + aceleración + pintar primero)

- Se midió el bloqueo de entrada de las 10 páginas del menú con registro de fotogramas.
  Resultados iniciales: Inicio y Personas instantáneas; Correspondencia 160 ms, Circuito 221 ms,
  Escuelas 169 ms, Asambleas 125 ms, Archivo 106 ms, Donaciones 103 ms, Configuración 137 ms y
  **Descargas 501 ms** (la única realmente lenta).
- **Descargas**: el árbol de carpetas se construía entero y recursivo al entrar. Ahora es
  perezoso (`ConstruirNivel` + `NodoCarpeta_Expanded`): solo se lista cada carpeta al
  abrirla. Bloqueo de entrada: 501 → 126 ms. La expansión se probó por automatización
  (carpeta con contenido → +4 hijos, visual idéntico).
- **Todas las páginas pesadas pintan primero**: el trabajo se movió del constructor a
  `Loaded` con una espera a `DispatcherPriority.Background` antes de cargar
  (Circuito, Donaciones, Documentación, Configuración, Asambleas, Archivo, Descargas) o se
  añadió esa espera a las que ya cargaban en Loaded (Escuelas, Personas).
- **Verificado con capturas de pantalla** de las 9 páginas tras el cambio: todas cargan sus
  datos (tablas, filtros, totales, árboles) y el aspecto es idéntico al de antes.
- Visitas ya tenía su propio `PanelCargando` + espera; no se tocó. El panel «Cargando» grande
  no se añadió a estas páginas porque tras pintar tardan <0,2 s (parpadearía).
### Lote: Inicio tablet, Archivo (colapso), Calendario (bandas y rango completo), Visitas (separadores) y actividad del día

- **Inicio (Android)**: la rejilla de «Resumen del circuito» se adapta al ancho: 2 columnas en
  móvil, 3 a partir de ~600 dp y 4 a partir de ~900 dp (tablet o fold abierto). El resto del
  contenido se estira a lo ancho. Verificado en pantalla ancha simulada (pasó a 3 columnas).
- **Archivo del circuito (Android)**: la flecha de colapsar es más grande (19) con área de
  toque de 36 de alto y todo el lateral izquierdo es pulsable; pulsar el **nombre** de la
  sección colapsa/expande (verificado por automatización: la lista se oculta).
- **Calendario (Android)**: el separador de mes ahora es una **banda a ancho completo**
  (borde a borde, sin esquinas redondeadas, texto azul centrado), claramente distinta de las
  tarjetas de semana. Además se carga **todo el periodo de una vez** como en Windows
  (datos ± 1 año): desaparecen los cargadores de meses al hacer scroll. Verificado: tras
  deslizar 18 pantallas está en agosto de 2028 (final del rango) sin avisos de carga.
- **Visitas (Android)**: los seis separadores de sección («Programa de la visita», «Notas
  personales», «Territorios (S-13)», «Mapa», «Contabilidad», «S-303 y S-2») ahora son bandas
  a ancho completo (fondo #1B2433, texto azul) distintas de las tarjetas de personas.
- **Actividad del día (Inicio)**:
  - Android tenía un fallo: solo consultaba los responsables «Joni/Rebe/Comida/Cenar/Dormir»,
    pero los datos usan sobre todo «SC», «Esposa» y «Ambos», así que casi nada aparecía. Ahora
    se leen **todas** las actividades del día (como en Windows) con insignia SC/Esposa/Ambos,
    acompañante y ubicación; las filas de comida/cena/dormir no llevan insignia.
    Verificado en el emulador con el programa de un miércoles: 7 actividades con SC/Esposa/
    Ambos.
  - Windows ya leía todo y mostraba ambas; se mantiene y además se oculta la insignia en las
    filas de comida/cena/dormir (antes ponía «SC»).
- **APK de Release** regenerado para el teléfono.
### Nueva función: Tareas y eventos (Windows + Android)

- Tabla nueva `Tareas` en la base compartida (Id, Titulo, Descripcion, FechaInicio,
  FechaFin, TodoElDia, Hora, AvisoMinutos, Repeticion, Compartida, Completada, CreadaPor,
  fechas). La fusión la cubre automáticamente (descubre tablas por su clave primaria) y
  la creación de la tabla es **auto-reparable**: se ejecuta antes de cada lectura/escritura
  porque la sincronización con el otro equipo puede sustituir la base por una copia sin ella.
- Página **Tareas** en ambos menús (visible para ambos perfiles) con lista (pendientes
  primero, chips de aviso/repetición/compartida, casilla para completar) y formulario:
  título, descripción, fecha o rango, todo el día u hora, aviso (sin aviso / a la hora /
  10-30 min / 1 hora / 1 día antes), repetición (diaria/semanal/mensual/anual) y
  **compartir con la esposa** (ella ve las compartidas en su app; las tareas no se filtran
  por perfil).
- **Avisos**: se calcula la ocurrencia vigente (con repeticiones y rango) y, al vencer el
  aviso, entra en el motor de alertas: aparece en la campana/contador (Android: también como
  notificación del sistema con su número; Windows: en el popup de la campana, con
  comprobación cada minuto) hasta completar la tarea. El registro de "ya avisado" es local
  de cada dispositivo (no se sincroniza). La esposa no recibe notificaciones (su regla).
- **Verificado**: Android en emulador (crear tarea, chips, aviso "A la hora" → notificación
  del sistema con "⏰ Prueba tareas — Hoy 00:00" y 4 alertas pendientes en la campana;
  completar → Completada=1 en BD). Windows (página, formulario, guardar → fila en la BD real,
  lista con la tarjeta; fila de prueba borrada después). Arreglado de paso: el formulario de
  Windows se cerraba al abrirse (evento Checked antes de crearse los paneles → guardas).

### Sincronización con Syncthing: sin bloqueos y sin perder conflictos (Windows + Android)

- **Causa (comprobada en el registro de Syncthing del PC)**: la app de Windows mantenía
  `sccosmos.db` abierta entre operación y operación (agrupación de conexiones de SQLite), así
  que Syncthing no podía reemplazar el archivo al sincronizar ("Failed to sync ... El proceso
  no tiene acceso al archivo", en bucle desde las 16:26). Además Syncthing había creado copias
  de conflicto (`sccosmos.sync-conflict-*.db`) que ninguna app leía: los cambios de la versión
  que perdía el conflicto quedaban fuera. Y la carpeta `SCCosmos` está **anidada** dentro de
  `ESP-BA-01` en el PC y en DESKTOP-792PF3K (el mismo archivo se sincroniza por dos carpetas).
- **Windows**: todas las conexiones pasan a `Pooling=False` (DatabaseHelper, InicioPage,
  MainWindow, PersonasPage, VisitasPage x2). La app ya no bloquea el archivo: Syncthing puede
  actualizarlo con la app abierta y el PC ve los cambios del móvil sin reiniciar.
- **Android**: fusión automática de copias de conflicto.
  `FusionBaseDatosService.FusionarCopiaDeConflicto(local, copia)`: añade las filas que solo
  están en la copia y aplica su versión en las filas que aquí no se han tocado desde la última
  sincronización; nunca pisa cambios locales pendientes (registro) ni borra filas; hace copia
  de seguridad antes. `AutoSyncService.FusionarCopiasConflictoAsync()`: en cada ciclo de 2 min
  lista la carpeta, combina cada `sccosmos.sync-conflict-*.db` (verificando integridad y hash:
  si la copia es el contenido ya sincronizado no se fusiona) y la retira; si algo falla deja la
  copia intacta para el siguiente intento. Se llama al principio de `ImportarSiHayCambiosAsync`
  (los cambios fusionados quedan protegidos por el registro y se exportan solos).
- **Pendiente (configuración del usuario)**: desanidar la carpeta SCCosmos de ESP-BA-01 (una
  sola ruta por dispositivo) y activar versionado escalonado en Syncthing.
- **Sustituido más adelante** por un nuevo sistema de sincronización por cambios (sin la base
  como archivo compartido): ver sección siguiente.

### Sincronización JW Hub en Android: traslados + fotografías (paridad con Windows)

- **Traslados**: Android solo clasificaba Conflictos/Nuevos/Bajas; una persona que pasaba a otra
  congregación quedaba como "Nuevo" + "Baja" por separado. Ahora se detectan igual que en Windows
  (`DetectarPosiblesTraslados` / `EsMismaPersonaParaTraslado` / `FechasIncompatiblesTraslado`:
  email JW, email o nombre + fechas compatibles, congregación distinta; solo con una única
  candidata para evitar homónimos) y aparece la pestaña **Traslados** con:
  - «Confirmar traslado» → nuevo `DatabaseHelper.RegistrarTrasladoConHubMobile` (réplica del de
    WPF: crea la ficha nueva con los datos actuales del Hub, enlaza cadena de traslados y cónyuge,
    conserva la anterior como baja histórica, actividad NO se copia) y la ficha nueva pasa a
    "Sin cambios".
  - «No es traslado» → el registro de Hub vuelve a Nuevos y la ficha antigua a Bajas.
  - "Finalizar revisión" bloquea si quedan traslados sin revisar (igual que Windows).
  - La revisión persistida guarda la categoría nueva (sobrevive a cierres de la app).
- **Fotografías**: el análisis ya detectaba el cambio real con dHash (`JwHubPhotoComparer`), pero
  la página de conflicto no lo mostraba (seguía "excluida temporalmente") y no había forma de
  aplicarla. Ahora, cuando la foto cambia, la página de conflicto muestra una tarjeta con las dos
  versiones (BASE DE DATOS / JW HUB con marca de selección), se toca la que se quiera conservar
  (JW Hub por defecto) y al guardar se aplica. El resto de campos sigue igual.
- **APK de Release** regenerado e instalado en el teléfono.

### Escuelas: pestañas Estudiantes y Clases (16/10) — Windows y Android

- **Pestañas nuevas en Escuelas**: «Estudiantes» (la lista de candidatos de siempre, intacta) y
  «Clases» (las escuelas creadas). En Windows, dentro de Clases cada escuela se abre en un panel
  de detalle; en Android, el detalle es una página.
- **Una clase = una escuela de una semana (lunes a sábado)**: se elige cualquier día y se ajusta
  al lunes; se muestra como «Semana del 12 al 17 de octubre». Campos: instructor (solo
  precursores regulares/especiales o misioneros, hombres y ancianos), instructor sustituto
  (opcional), salón del reino (congregación, con opción de escribir otra), número de butacas
  (12 por defecto, editable) y notas.
- **Dentro de cada clase**:
  - **Añadir estudiantes**: selector con búsqueda y casillas (precursores y misioneros que aún no
    están apuntados).
  - **Distribuir asientos**: rejilla de butacas (vacía = «Libre», ocupada con nombre y color);
    al tocar una butaca se elige quién se sienta (o se vacía). Un alumno por butaca: si dos
    personas comparten asiento, el anterior queda libre solo.
  - **Programa de limpieza**: resumen por días (lunes a sábado: quién limpia cada día) y elección
    del día de cada estudiante (varios alumnos por día).
  - **Editar el horario** y **Talleres**: botones preparados («Próximamente», se programará más
    adelante).
- **Datos**: tablas nuevas `EscuelasClases` y `EscuelasAlumnos` (Id GUID sin colisiones) con el
  mismo sistema auto-reparable que Tareas; se crean al arrancar (antes de los disparadores del
  motor) y antes de cada lectura/escritura. Viajan solas entre dispositivos (6 disparadores
  verificados + entradas en SCCosmos_Cambios).
- **Registro de actividad** (todo en lenguaje natural): «Creó/Editó/Eliminó la escuela de la
  semana del …», «Añadió N estudiante(s)…», «Quitó a …», «Sentó a … en el asiento N…», «Dejó sin
  asiento a …», «Puso a … para limpiar el lunes…», «Quitó a … del programa de limpieza…».
- **Verificado en Windows (16/10)**: creada una escuela (semana, instructor, congregación, notas),
  editada, añadidos 2 estudiantes, butaca 1 asignada y limpieza del lunes asignada y resumen por
  días; todo comprobado en la base de datos y en el registro de actividad; capturas de cada paso.
  Datos de prueba limpiados después.
- **Verificado en Android (emulador, 16/10)**: pestañas Estudiantes/Clases ✓; creada una escuela
  (semana del 12 al 17 de octubre, Almendralejo, Alejandro Fernández, notas) ✓; añadidos 2
  estudiantes desde el selector con casillas ✓; **butacas en rejilla de 3 en fila** con la 1
  ocupada por Adrián Martín (hoja de acción al tocar: elegir persona o vaciar) ✓; **programa de
  limpieza** con resumen por días («Lunes → Adrián Martín») ✓; eliminada la escuela con
  confirmación y lista vacía después ✓. Todo comprobado en la base del emulador (0/0 tras la
  limpieza) y en el registro de actividad («Creó…», «Añadió 2 estudiante(s)…», «Sentó a … en el
  asiento 1…», «Puso a … para limpiar el lunes…», «Eliminó… (Almendralejo)») con el dispositivo
  «Móvil sdk_gphone64_x86_64». 6 disparadores de sincronización presentes y 6 cambios del motor.
- **Datos de prueba limpiados** en ambos lados (PC y emulador).
- **APK de Release (arm64) regenerada** con recompilación completa
  (`-p:RuntimeIdentifier=android-arm64 -t:Rebuild`); lista para instalar en el teléfono cuando
  el usuario lo reconecte.

### Retoques de Escuelas (16/10, tarde)

- **Butacas fuera del formulario** (Windows y Android): el número de butacas ya no se rellena al
  crear/editar la clase; se ajusta en la vista de asientos con un contador «Butacas: − 12 +»
  (crea o recorta la rejilla al momento; las butacas que desaparecen dejan libres a sus ocupantes).
  Nueva operación `EstablecerAsientosEscuela`/`…Mobile` (con registro «Cambió las butacas a N…»).
- **Formulario modal de verdad (Windows)**: los paneles (detalle, formulario, estudiantes,
  asientos, limpieza) ya no empujaban las pestañas hacia abajo: ahora viven en una capa aparte
  sobre la página (el contenido quedó en una rejilla interior). El formulario sale centrado, tipo
  modal, sin mover nada.
- **Semana resaltada al escoger la fecha (Windows)**: el selector es un calendario en el que al
  tocar cualquier día se marca **la semana completa de lunes a sábado** en azul (Calendar
  `MultipleRange` + ajuste a la semana; estilos oscuros propios).
- **Semana resaltada (Android)**: bajo el selector de fecha hay una tira con los 6 días de la
  semana (LUN..SÁB) resaltados en azul y botones ‹ › para pasar de semana.
- **Instructor y sustituto con congregación**: en ambos selectores aparece «Nombre (Congregación)»;
  al guardar se conserva solo el nombre (estilo propio `NombreSolo`).
- **Verificado en Windows (16/10)**: capturas del modal sin desplazar pestañas, calendario con la
  semana 12–17 en azul, desplegable de ancianos con congregación entre paréntesis, contador de
  butacas (12→13 con la rejilla actualizada) y registro de actividad («Cambió las butacas a 13…»).
  Escuela de prueba eliminada y base limpia (0/0).
- **Pendiente**: misma verificación en el emulador Android.
- **Verificado en Android (emulador, 16/10)**: formulario sin butacas y con la **tira de la
  semana LUN..SÁB resaltada en azul** (con ‹ › para cambiar de semana) ✓; desplegable de
  instructores con la congregación entre paréntesis ✓ (al guardar se conserva solo el nombre);
  creada la clase, contador «Butacas: − 13 +» con la rejilla de 13 actualizada ✓, y eliminación
  de la clase ✓. Registro: «Creó…», «Cambió las butacas a 13…», «Eliminó… (Almendralejo)».
  Datos de prueba limpiados (0/0 en la base del emulador).
- **APK de Release (arm64) regenerada** para el teléfono.

### Retoques de Escuelas, ronda 2 (16/10, tarde)

- **Instructor con buscador**: al elegir instructor o sustituto hay campo de búsqueda (filtra por
  nombre o congregación al escribir). En Windows, caja de búsqueda encima de cada desplegable; en
  Android, botón «Elegir instructor…» que abre una página con buscador y lista (tocar elige).
- **Disposición de la sala**: en la vista de asientos se indican **filas** y **columnas** con
  contadores «− n +», y los **pasillos** se marcan con botones entre columna y columna
  («col. 2|3»). Todo se guarda al momento (`EstablecerDisposicion`/`…Mobile`, registro «Cambió la
  disposición… a F filas × C columnas (pasillos tras las columnas X)»). Columnas nuevas en la
  tabla (`Filas`, `Columnas`, `Pasillos`, con ALTER automático en bases ya creadas); por defecto
  4×3 (=12 butacas como antes).
- **Mesa del instructor**: aparece centrada delante de la primera fila, tanto en la vista como en
  el PDF.
- **PDF de la disposición**: botón «PDF de la disposición» (iText, igual en ambos): título,
  semana e instructor, barra «MESA DEL INSTRUCTOR», la rejilla con pasillos (ocupadas sombreadas
  con el nombre; libres, solo el número). En Windows se guarda en `%LOCALAPPDATA%\SCCosmos\PDFs`
  y se abre solo; en Android en la caché y se abre con el visor (Drive). Queda registrado
  «Generó el PDF de la disposición de butacas…».
- **Verificado (16/10)**: Windows (buscador filtrando «Zafra», 5 filas × 3 columnas con pasillo
  2|3, mesa centrada, PDF abierto en Acrobat con el mismo dibujo) y Android en el emulador
  (buscador «Zafra», 4 filas × 4 columnas con pasillo, mesa en su sitio, PDF abierto en Drive).
  Escuelas de prueba eliminadas (la del usuario, «Azuaga», intacta) y registro de actividad
  completo.

### Talleres, Horario y PDFs del programa (16/10, tarde) — Windows y Android

- **Talleres**: cada escuela tiene los **4 talleres con sus 18 puntos fijos** (tema y minutos del
  programa, transcripción única en el código). De cada punto se edita: **asignado**,
  **acompañante** y **hora de ensayo**. Los selectores de persona llevan **buscador** (nombre o
  congregación) y muestran la congregación entre paréntesis; hay opción «nadie» para vaciar.
  En Windows, un selector compartido con lista filtrable; en Android, la página de búsqueda.
- **Horario**: vista por días (lunes a sábado) con la estructura exacta del programa: canción
  inicial y final (fijas), lecciones con **horas fijas** (no editables) y pausas de 15/60/10
  minutos; se asignan las **tres oraciones** de cada día (inicial, de la mañana y final) con el
  mismo buscador. Sábado incluye «Palabras finales y conclusión» y su pausa de 10 minutos.
- **PDFs nuevos** (iText, idénticos en ambos): **PDF de los talleres** (tabla con taller, min,
  tema, asignado y acompañante con congregación, ensayo), **PDF del horario** (rejilla de 6 días
  como el modelo: canciones, oraciones, lecciones con horas y pausas; Sábado con la fila
  partida 15/10) y **PDF del programa de limpieza** (día → quién limpia).
- **Tablas nuevas** `EscuelasTalleres` y `EscuelasHorario` (GUID, auto-reparables, se siembran al
  abrir cada vista) y **sincronizan** con el motor. Al eliminar una escuela se borran también sus
  talleres y horario (corregido tras verlo en la prueba).
- **Registro de actividad**: «Asignó a X al punto N (taller Tk)…», «Puso a X como acompañante…»,
  «Cambió la hora de ensayo…», «Asignó a X para la oración inicial del lunes…», «Generó el PDF de
  los talleres/horario/programa de limpieza…».
- **Verificado (16/10)**: Windows (talleres con asignación y ensayo, horario con oración asignada,
  los tres PDFs abiertos en Acrobat con el dibujo correcto — horario idéntico al modelo) y Android
  en el emulador (mismo recorrido, PDFs abiertos en Drive; los cuatro PDFs en caché verificados
  como %PDF-1.7). Escuelas de prueba eliminadas (la del usuario, «Azuaga», intacta).
- **APK de Release (arm64) regenerada** para el teléfono.

### Descargas oculta + código alternativo (16/10, tarde) — Windows y Android

- La página **Descargas queda oculta** en el menú de Windows por defecto. Se activa escribiendo el
  **código alternativo** en Configuración → Perfil (perfil «Hombre»): **`CODIGO_OCULTO_DESCARGAS`**
  (vale en mayúsculas o minúsculas y también como código de acceso completo en Android). Al usarlo
  queda **activada para siempre** (se guarda por equipo; luego aparece siempre, aunque después se
  use el código normal; la esposa sigue sin verla).
- El **campo del código va enmascarado** (puntos) y editable, en Windows (PasswordBox sin botón de
  revelar) y en Android (entrada con `IsPassword`). El campo admite letras (ya no teclado
  numérico en el móvil).
- La comparación de códigos es **sin distinguir mayúsculas** (el tema oscuro de Windows escribe en
  mayúsculas dentro del campo).
- Verificado: sin activar → Descargas oculta ✓; con `CODIGO_OCULTO_DESCARGAS` → aparece y queda
  guardada la activación ✓; restaurada a oculta para que la active el usuario.

### Esquema de ventanas de Escuelas (16/10, tarde, Windows)

- El **editor de clase vuelve a ser modal** (tarjeta centrada, con «← Volver», Cancelar y
  Guardar).
- El **detalle de la escuela y sus vistas (asientos, talleres, horario, limpieza)** ahora ocupan
  **toda la página**, con «← Volver» arriba a la izquierda y sus botones de PDF a la derecha.
- Verificado con capturas: editor modal con las pestañas visibles detrás; detalle y asientos a
  página completa; «Volver» funciona en todos.
- Android no cambia: allí los modales ya son páginas (regla de la app) y todo ocupa la pantalla.

### Sección RSC · Reuniones para el Servicio del Campo (17/10)

**Qué es:** reunión breve antes de predicar, por congregación, día y turno (mañana/tarde).
Se marcan asistentes, quién sale a predicar, arreglos (va con alguien / solo, acepta grupo
mayor o no) y se forman grupos automáticamente.

**Windows** (`RscPage.xaml(.cs)`, `RscModelos.cs`, `RscMotor.cs`, `DatabaseHelper.Rsc.cs`):
- Menú «RSC» (oculto para el perfil Esposa, como Escuelas).
- Lista + buscador por día («Ver un día» muestra las RSC de ese día, mañana y tarde, con
  sus grupos y miembros).
- Editor a página completa con «← Volver»: asistentes con interruptor «Predica», arreglo
  por persona, grupos con territorio y chips de miembros (mover ↔ / sacar ✕).
- Modal «Nueva RSC» (día, turno, congregación), modal de arreglo y modal de mover.
- «Formar grupos» pide confirmación al rehacer y muestra avisos si un grupo queda por
  debajo del mínimo.

**Android** (`Views/RscPage`, `RscNuevaPage`, `RscEditorPage`, `RscPersonasPickerPage`,
`RscArregloPage`, `RscMoverPage`; `Models/RscMobileModels.cs`; `Services/RscMotor.cs`;
`Data/DatabaseHelper.Rsc.cs`): mismas funciones, todo como páginas normales; icono 🤝.

**Motor de grupos (portado por plataforma):** solo entran quienes asisten y salen; las
parejas con arreglo mutuo van juntas; si no aceptan grupo mayor forman su propio grupo
(aunque tenga 2); tamaños objetivo 6 y 4 (5 admitido; 7 en un solo grupo si no cuadra);
nunca por debajo de 4 salvo arreglos; se reparten las personas con territorio.

**Arreglos ya apuntados:** se detectan en «Programa de la visita» (ProgramaVisitaV2) las
predicaciones del SC o su esposa con acompañante para ese día (el turno se compara en C#:
el UPPER de SQLite no conoce la ñ) y se ofrecen en el editor para añadirlos a la RSC de un
toque. El SC y su esposa no tienen ficha en Personas: van con PersonaId −1/−2 y el nombre
guardado en la propia fila de la RSC.

**Datos y sincro:** tablas nuevas `RSC_Reuniones`, `RSC_Asistentes`, `RSC_Grupos` y
`RSC_Miembros` (Id GUID de texto, como las escuelas). Los disparadores del motor de cambios
se crean solos al arrancar. Cada cambio se guarda al momento (sin botón de guardar) y la
página abierta se refresca sola cuando llegan cambios de otro dispositivo.

**Pruebas (17/10):**
- Banco temporal `RscPruebas` (consola que referencia al proyecto de Windows): 36
  comprobaciones en verde — motor (n=4/5/6/7/8/10/12/13/18; parejas abiertas y cerradas;
  exclusiones; arreglo externo) y capa de datos completa (crear → leer → borrar; arreglos
  detectados el 01/07/2026 en la 760: Joni→Rosa Nieto, Joni→Ana Belén, Rebe→José Luis).
- Windows verificado en vivo hasta donde el equipo lo permitió (lista, buscador y modal
  «Nueva RSC» con capturas). El pase visual del editor quedó pendiente: el conmutador USB
  «Switch USB» del usuario bloqueaba el ratón sintético mientras estaba ausente.
- Android verificado de punta a punta en el emulador: crear → añadir (SC, esposa y
  publicadores) → formar (2 grupos de 4) → mover a sin grupo (con aviso ⚠) → eliminar;
  base limpia tras las pruebas y 12 disparadores RSC presentes.
- APK arm64 regenerada (17/10 19:28): instalación en el móvil pendiente de que vuelva a
  conectarse por USB (el conmutador se llevó el teléfono al otro equipo).

### RSC · retoques (17/10, tarde)

- **Grupos por sexo (a ser posible):** hombres con hombres y mujeres con mujeres. Si un lado
  no llega a 4, se mezcla lo justo (p. ej. 9 hombres + 3 mujeres → dos grupos de 6). El
  comportamiento se aplica en Windows y Android.
- **Arreglos con varias personas:** el editor de arreglo ahora es una lista con casillas
  (se marca una o varias). Al guardar, todos los elegidos quedan como un conjunto que predica
  junto (vínculos mutuos de malla completa) y los enlaces antiguos se limpian. Columnas nuevas
  `ArregloConPersonaIds` y `Sexo` en RSC_Asistentes, con migración automática.
- **Icono propio:** la RSC usa PeopleTeam en Windows y 🤝 en el móvil (ya no repite el de
  Personas).
- **Retoques visuales:** nombres largos se truncan en filas y chips; el botón de arreglo
  muestra «Con Nombre +n».
- **Pruebas:** el banco `RscPruebas` pasa a 49 comprobaciones en verde (sexos: 10H+6M →
  6+4+6; 9H+3M → 6+6; 4H+4M → 4+4; tríos abiertos y cerrados de arreglo). Android verificado
  en el emulador: 1 grupo de 6 hombres + 2 grupos de 4 mujeres, y arreglo múltiple con malla
  mutua comprobada en la base (Fermín ↔ Eugenia ↔ María Teresa); datos de prueba borrados.
- APK arm64 regenerada (17/10 20:07): instalación en el móvil pendiente de que vuelva a
  conectarse por USB.

### Conversor de tarjetas S-21 a datos_s21.json (18/10)

- Herramienta `Herramientas\convertir_s21.py` (+ `convertir_s21.bat` para arrastrar la
  carpeta encima); copia lista para usar en
  `Documents\2 - ESP-BA-01\8 - Otros\SCCosmos\Convertidor S21\`.
- Soporta tres formatos de tarjeta:
  1. PDF **rellenable** (campos 900_1, 901_20…): año de la tarjeta + 1.
  2. PDF **impreso nuevo** («Últimos 12 Meses»): casillas vectoriales (cuadrado + trazo
     diagonal), tabla por columnas, fechas «Ene 20 1959»; año = ciclo actual (`--anio`).
  3. PDF **impreso antiguo** («REGISTRO DE PUBLICADOR», una o dos secciones): casillas por
     glifos (WtOnOrnament/ZapfDingbats) o vectoriales, **incluidas marcas sueltas (✓ sin
     casilla)**; se elige la sección más reciente y su año se usa tal cual.
- Omite solo los resúmenes (Totales, Asistencia, Teléfonos, programas…), evita duplicados
  entre listas (publicadores/precursores) y descarta nombres basura (códigos de congregación
  del S-303 y similares).
- Detalles finos aprendidos en la auditoría del 18/10: el año puede venir como «2025/26»
  (se usa el año final tal cual) o «2025» (año de inicio: +1); fechas con año de 2 cifras se
  expanden (90 → 1990); las marcas de participación pueden ser un ✓ suelto sin casilla.
- Validado contra los `datos_s21.json` reales del usuario: San Vicente V2 36/36 idéntico;
  Cáceres V3 82/82 nombres con solo 4 diferencias de año; Jerez V3 32/32 idéntico (+7
  personas que faltaban en su JSON). Auditoría total de las 3 visitas el 18/10.
- Pendiente de decidir: integrar un botón «Convertir tarjetas S-21 (PDF)» dentro de SCCosmos.

### Nota de compilación (16/10)

- En el **emulador** se instaló por error una APK x64 del 13/10 (menú e Inicio antiguos) porque
  `dotnet build` sin `RuntimeIdentifier` no regenera la carpeta `android-x64` (queda la última
  compilada con `-p:RuntimeIdentifier=android-x64`). Para probar en el emulador, compilar
  siempre con `-p:RuntimeIdentifier=android-x64`; para el teléfono, con `android-arm64`.
- La base del emulador estaba limpia tras la instalación; se copió la base del PC a
  `/data/local/tmp` y, con `run-as` (build de depuración), a `files/sccosmos.db` para probar.

### Nuevo sistema de sincronización por cambios (en construcción)

- Objetivo: la base deja de viajar como archivo. Cada app tiene su base local (la de Windows
  salió de la carpeta compartida a `%LOCALAPPDATA%\SCCosmos\sccosmos.db`) y los cambios viajan
  **directamente entre dispositivos** (TCP cifrado con AES-256-GCM, clave compartida), sin
  Syncthing para la base.
- **Motor construido y probado**: `SincroCompartido/` (SincroConfiguracion, SincroCripto,
  SincroMensajes, SincroAlmacen, ServicioSincro) + arnés `SincroPruebas` con **9/9 comprobaciones**
  (cambios bidireccionales, ediciones simultáneas con "el último gana", altas, bajas, relevo por
  un tercer dispositivo, puesta al día tras apagón, sin duplicados, rechazo de clave incorrecta).
- Descubrimiento automático en red local (UDP 47312) + lista de direcciones manuales para fuera
  de casa (Tailscale, DDNS…). Puertos TCP 47311. Sin servicio en la nube: nada se guarda fuera
  de los propios dispositivos.
- Windows: enganchado (motor arranca con la app, sección nueva en Configuración con nombre,
  clave, direcciones, dispositivos y estado; verificado con captura).
- Recuperación de umbral: la base local del PC se publicó una vez en la carpeta para que los
  móviles (versión antigua) se pongan al día antes de actualizar al sistema nuevo.
- **Android enganchado**: `Services/SincronizacionMobile.cs` (configuración en AppData, migración
  única desde la carpeta, motor arrancado en `App.CreateWindow`), sección nueva «Sincronización
  entre dispositivos» en Configuración (visible solo para el perfil Hombre) y sistema antiguo de
  carpeta apagado para la base (`AutoSyncService` se salta import/export/fusión cuando el motor
  nuevo está activo).
- **Unificación de bases**: antes de arrancar el motor se compararon las bases PC/móvil (diferían:
  13 personas y 204 actividades, secuela de días con la sincronización rota) y se adoptó en el PC
  la base del móvil (la más trabajada: reparada y con las personas nuevas), con `quick_check` ✓ y
  copia de seguridad de la anterior.
- **Prueba real PC ↔ móvil (16/10)**: emparejados por wifi (clave común CLAVE_OCULTA). Tarea creada en
  el PC → apareció en el móvil en segundos; casilla marcada en el móvil → `Completada=1` en el PC
  en ~5 s; borrado en el PC → desapareció del móvil; marcas de agua 4/4 en ambos sentidos. APK de
  Release instalado y app verificada. Datos de prueba limpiados después.
- **Canal por carpeta (sin configurar nada, fuera de casa)**: `CanalArchivosSincro` compartido +
  implementaciones `CanalArchivosPc` (Windows) y `CanalArchivosMobile` (SAF). Cada dispositivo
  publica sus cambios **cifrados** (mismos mensajes del protocolo TCP en líneas AES-GCM) en
  `Sincro/cambios-<id>.jsonl` dentro de la carpeta que el usuario ya sincroniza; cada archivo tiene
  un único escritor (imposible conflicto) y las confirmaciones viajan igual. El motor lee/cita cada
  6 s. Compactación automática a 2 MB. Segunda tanda de pruebas en el arnés: **11/11** (canal por
  carpeta bidireccional con confirmación).
- **Prueba real por datos móviles (16/10)**: con la **wifi del móvil apagada** (solo datos), tarea
  creada en el PC → apareció en el móvil (vía relé de Syncthing); casilla marcada en el móvil →
  `Completada=1` en el PC en ~20 s; borrado en el PC → desapareció del móvil. Wifi restaurada tras
  la prueba y datos de prueba limpiados.

### Actualización automática de las pantallas (16/10)

- El motor avisa cuando aplica cambios de otro dispositivo: `ServicioSincro.DatosRecibidos`
  (dispara en el canal TCP y en el canal por carpeta).
- **Windows**: `SincronizacionPaginas` refresca la página abierta (interfaz `IActualizablePorSincro`
  implementada en Inicio, Personas, Tareas, Rendimientos, Calendario, Escuelas y Circuito;
  Asambleas/Visitas/Documentación se reabren solas). Retardo de 2 s; solo la página visible; si
  llegó algo mientras estabas en otra pantalla, se aplica al volver.
- **Android**: `ActualizacionPaginasMobile` (interfaz `IActualizableMobile` en Inicio, Tareas,
  Personas, Rendimientos, Calendario, Circuito, Escuelas, Asambleas, Correspondencia, Precursores,
  Nombramientos y Alertas), con recarga al volver a una página si quedó algo pendiente.
- Los formularios abiertos no se tocan (para no borrar lo que se está escribiendo).
- **Verificado en vivo (16/10)**: tarea creada en el PC → apareció sola en el móvil (~10 s, sin
  tocar el móvil); casilla marcada en el móvil → el PC actualizó casilla y orden sin tocarlo
  (leído por accesibilidad); borrado en el PC → desapareció del móvil solo (~12 s). Datos de
  prueba limpiados y estado del usuario restaurado.

### Registro de actividad (historial de acciones) — 16/10

- Tabla nueva `RegistroActividad` (Id GUID — sin colisiones entre dispositivos, FechaHora,
  Categoria, Dispositivo, Perfil, Descripcion) que **viaja con el motor de sincronización**:
  cualquier dispositivo lee el historial completo, hecho en cualquiera.
- Anotadores: `DatabaseHelper.AnotarActividad(…)` en Windows y `AnotarActividadMobile(…)` en
  Android. **Para funciones nuevas basta una línea**: `AnotarActividad("Categoría", "frase…")`.
- Instrumentados (ambas apps, frases en lenguaje natural con nombre de la persona/tarea):
  personas (añadir/editar/borrar/baja), traslados, actividad mensual, tareas (crear/editar/
  completar/reabrir/borrar), rendimientos (añadir/editar/eliminar), semanas del calendario,
  visitas (notas, programa: añadir/editar/eliminar, contabilidad), territorios importados,
  congregaciones y ajustes (carpeta, sincronización). Las operaciones de JW Hub (añadir,
  actualizar, baja, traslado) quedan registradas automáticamente al usar esas mismas funciones.
- Página **Actividad** en ambos menús (solo perfil SC): lista con las más recientes primero,
  filtro por categoría y botón Actualizar. En Windows se refresca sola con los cambios de otros
  dispositivos (la propia página se recarga).
- **Verificado en vivo (16/10)**: desde el móvil se marcó/desmarcó la tarea «Comida…» → el PC
  registró «Completó…» y «Reabrió…» con dispositivo «Móvil SM-F966B (SC)»; desde el PC se pulsó
  «Aplicar sincronización» → «Actualizó los ajustes de sincronización» con «DESKTOP-792PF3K (SC)».
  El móvil mostraba las 3 acciones unificadas («3 acciones, más recientes primero») y la página
  de Windows también. Estado del usuario restaurado tras las pruebas.

### Menú, configuración automática y campana (16/10, lote de ajustes)

- **Menú Android en el mismo orden que Windows** (Inicio, Correspondencia, Circuito, Calendario,
  Personas, Escuelas, Asambleas, Archivo del circuito, Rendimientos, Tareas, Actividad), sin
  «Descargas» (el módulo no está en el móvil). **Configuración va abajo del todo**, separada por
  una línea, como en Windows: el elemento queda oculto de la lista (`Shell.FlyoutItemIsVisible`)
  y se llega por un pie pulsable (`Shell.FlyoutFooter` + `Configuracion_Tapped` → `//configuracion`).
- **Configuración sin «Direcciones añadidas» ni botones de guardar/aplicar** (Windows y Android):
  - Todo se guarda solo: datos del circuito y rutas al salir de cada campo
    (`LostFocus`/`Unfocused`), interruptores al cambiarlos, nombre/clave de sincronización al
    salir del campo (aplica el motor solo). Métodos `GuardarTodo()`/`GuardarDatosGlobales()` y
    `AplicarSincroAutomatico()`, con la guarda `_autoguardando` para que las cargas no guarden.
  - Se conserva «Guardar perfil» (valida el código del SC; solo se aplica al pulsar).
- **Lista de dispositivos con casilla** (Windows y Android): cada dispositivo conocido tiene su
  casilla; al desmarcarlo, **este** dispositivo deja de sincronizar con él (se descarta en el
  descubrimiento, en el saludo TCP y al leer sus archivos del canal por carpeta) y **se corta la
  conexión abierta** (`ServicioSincro.Desconectar`). La lista de ids deshabilitados vive en la
  configuración (`Deshabilitados` + `EstaHabilitado`) y se guarda en `sincro.json`. El
  desmarcar/marcar queda en el registro de actividad con el nombre del dispositivo.
- **Campana de Inicio (Android)**: sin alertas → campana apagada/gris (🔕 atenuada, sin número);
  con alertas → campana dorada (🔔) con la insignia del número de pendientes (se recalcula al
  abrir Inicio, al volver de Alertas y con los cambios de otros dispositivos).
- **Verificado en Windows**: captura (página sin direcciones, sin botones, lista con casilla) y
  automatización: al desenfocar un campo se reescribe `local_settings.json` (autoguardado ✓);
  desmarcar el móvil escribe `Deshabilitados` y lo registra, y volver a marcarlo lo limpia ✓.
- **Verificado en el teléfono (APK de Release regenerado e instalado)**: menú en el orden de
  Windows con Configuración abajo y su línea ✓; Configuración sin direcciones ni botones y con la
  casilla del PC ✓ (capturas); autoguardado del interruptor (apagar/encender → «Desactivó/Activó
  la sincronización» registrado y sincronizado al PC ✓; los cambios pendientes mientras estuvo
  apagada llegaron al volver a encender — nada se pierde); **casilla**: al desmarcar el PC, una
  tarea creada en el PC NO llegó al móvil (35 s), al volver a marcarla llegó sola en ~25 s ✓, y
  ambos movimientos quedaron registrados con el nombre del dispositivo; **campana**: sin alertas →
  🔕 gris, con la tarea de aviso → 🔔 dorada con «1», al borrarla → volvió a gris sola ✓.
  Datos de prueba limpiados (tarea, registros y notificación del sistema) en ambos dispositivos.
### Conversión de tarjetas S-21 (PDF) dentro de la app (18/10, lote S-21)

- **Motor compartido en C#** (`SincroCompartido\ConvertidorS21.cs`, lo compilan Windows y Android):
  port a iText del conversor de escritorio (`Herramientas\convertir_s21.py`). Soporta los tres
  formatos (formulario 900_*_SanSerif, impreso «Últimos 12 Meses» y «Registro de publicador» con
  casillas vectoriales o glifos) y además:
  - widgets cuyo valor vive en el appearance: se dibujan en el contenido y se leen los /V como
    hace PyMuPDF (nombres y datos de formularios rellenados);
  - campos con nombres antiguos (`900_1_Text`, `901_1_S21_Value`) y nuevos en inglés
    (`Name`, `Date of birth`, `Check Box1`...);
  - PDFs con recursos rotos (ExtGState desconocido): se conserva lo leído y las apariencias se
    procesan por separado;
  - marcas sintéticas para casillas con /V distinto de /Off (sexo, esperanza, participación...).
- **Windows** (`ImportarArchivosActividad`): bloque nuevo «S-21 (PDF) - Convertir tarjetas» con
  botón «Convertir…» (elige carpeta), genera `datos_s21.json` allí (con copia .bak) y lo deja
  listo como S-21 a importar; botón «Ver JSON» para abrirlo. Verificado: la ventana muestra el
  bloque; el conversor es el mismo motor validado por consola (los diálogos nativos no se pueden
  automatizar en este equipo por el utilitario «Switch USB»).
- **Android** (`JsonImportSelectPage`): bloque «S-21 (PDF) · Convertir tarjetas» con botón
  «Convertir PDFs» (selección múltiple), genera el JSON en la caché y ofrece «Compartir JSON»
  (hoja de compartir) para verificarlo; «Analizar archivos» continúa el flujo normal.
  **Verificado en el emulador**: 3 PDFs (incluidos los casos raros Angulo y Remy) → «25 tarjetas
  de 3 PDFs» ✓; «Compartir JSON» abre la hoja con datos_s21.json ✓; «Analizar» abre la revisión
  con 25 nuevos ✓.
- **Validación del port** (C# vs Python, carpetas reales): San Vicente V3 36/36, Trujillo V3
  38/38, Cáceres V3 82/82, Jerez V3 y V1 39/39, Mérida Centro V1 135/135 y V2 146/146 — **cero
  tarjetas de diferencia**; las diferencias de campo que quedan son datos que C# lee mejor
  (casillas del S-21 oficial, sexo correcto en inactivos, participación desde los campos), y las
  únicas tarjetas «de más» de Python eran duplicados del mismo nombre con y sin tilde o espacio
  (C# los une).
- **Auditoría completa de las 42 congregaciones** (motor C# vs el informe de la herramienta
  Python): **0 carpetas con tarjetas de más** y todas las diferencias a la baja son duplicados de
  Python (mismo nombre con tilde/mayúsculas distintas, p. ej. `Lórenza`/`Lorenza`,
  `CATALINA HERNÁNDEZ`/`HERNANDEZ`, `Badenas …`/`BADENAS …`), que C# ya une. Los únicos «fallos de
  lectura» son 14 recibos bancarios cifrados con contraseña en Ciudad Jardín (no son tarjetas).

### Importar S-13 (territorios) desde la página de Visita (18/10, lote territorios)

- **Windows** (`VisitasPage`, pestaña Territorios): botón **«Importar JSON (S-13)»** a la izquierda
  del selector de año. Elige el archivo, avisa si ya hay territorios (se sustituyen), recarga la
  pestaña y registra la actividad; usa el mismo guardado que el flujo de importación
  (`GuardarTerritoriosImportados`, que conserva fecha de creación y vínculo). Verificado: el botón
  se pinta en su sitio (captura) y todo compila sin errores.
- **Android** (`VisitaDetallePage`): botón **«⬆ Importar JSON (S-13)»** dentro del panel de
  Territorios (al desplegarlo). Selector de archivos JSON, confirmación si ya hay territorios y
  recarga automática; nuevo `DatabaseHelper.GuardarTerritoriosImportadosMobile` (misma mecánica
  que la importación del Calendario) + registro en Actividad.
  **Verificado en el emulador**: 970 territorios existentes → elegir el JSON de Cáceres Norte →
  «Se importaron 970 territorios del S-13» ✓ y el recuento se mantiene en 970 (sustituye, no
  duplica).

### «Próxima» actividad en Inicio con resaltado en el Calendario (18/10, lote próxima)

- **Inicio (Windows y Android)**: debajo de la actividad de la semana aparece un enlace
  **«Próxima: <actividad> (<rango>)»** con la siguiente semana que tiene algo programado
  (visitas con congregación, asambleas con sección o cualquier otra actividad). Si no hay
  ninguna próxima, no se muestra.
  - Windows (`InicioPage`): texto azul subrayado en la tarjeta de la semana; al pulsarlo fija
    `CalendarioPage.SemanaDestacadaPendiente` y navega al calendario.
  - Android (`InicioPage`): etiqueta azul subrayada; al pulsarla fija
    `CalendarioPage.SemanaDestacadaPendiente` y hace `GoToAsync("//Calendario")`.
    Consulta nueva `DatabaseHelper.ObtenerProximaActividad` (primera semana con actividad
    después de la actual).
- **Calendario — resaltado temporal**: al abrirse (carga nueva o página reutilizada) se
  desplaza hasta esa semana y **resalta su tarjeta/fila un momento** (borde dorado #FFC83C +
  fondo azulado) y **vuelve sola a su estado normal** a los ~1,8 s.
  - Windows: sobre la fila del DataGrid (`ResaltarSemanaPendienteAsync`), con reintentos si
    hay recargas de sincronización en curso (el aviso «Cargando…» manda; el resaltado corre
    **después** de ocultarlo) y búsqueda de la fila por contenedor o por árbol visual.
  - Android: sobre la tarjeta del `CollectionView` (`ScrollTo` con grupo, sin animación) y
    búsqueda de la tarjeta por árbol visual; reintentos si la tarjeta aún no está creada.
- **Verificado en el emulador**: «Próxima: Visita · San Vicente de Alcántara (del 19-oct al
  25-oct)» → al pulsar, el calendario se coloca en esa semana con **borde dorado** y después
  vuelve a la normalidad (capturas antes/después). **Verificado en Windows**: mismo texto, la
  fila de San Vicente queda resaltada (borde dorado + fondo) y se restaura (captura).

### Vaciar la sincronización al cerrar la app (18/10, lote cierre)

- **Motor compartido** (`SincroCompartido\ServicioSincro.VaciarPendientesAsync`): envía ahora mismo
  lo que quede pendiente — empujón TCP a cada dispositivo conectado y pases del canal por carpeta
  (Syncthing, hasta 400 cambios por pase, con reintento si la carpeta está bloqueada un momento y
  límite de tiempo). Devuelve cuando termina o al agotar el límite.
- **Windows** (`MainWindow`): al cerrar la ventana se cancela el cierre una vez, se ejecuta el
  vaciado (máx. 4 s) y la app se cierra después. Si no hay nada pendiente tarda un instante.
- **Android** (`App.OnSleep` + `MainActivity.OnStop`, con guarda para no repetirlo dos veces
  seguidas): al salir a segundo plano o cerrar se vacían los pendientes (máx. 3 s) antes de que el
  sistema suspenda la app. Antes solo se llamaba al sistema antiguo de carpeta, que ya no hace nada
  cuando el motor nuevo está activo: por eso al cerrar podían quedarse cambios sin propagar.
- **Verificado**:
  - Prueba determinista en el arnés (`--sync-flush`): 1.200 cambios apuntados → el vaciado publicó
    en 3 pases, la secuencia escrita alcanzó la máxima y no quedó nada pendiente ✓.
  - Windows en vivo: con cambios pendientes reales, al cerrar la ventana el archivo de cambios de
    la carpeta se reescribió en ese mismo instante (mtime del cierre) y la app se cerró en ~0,5 s ✓.
  - Emulador: ciclo Inicio → segundo plano → reabrir → cerrar sin excepciones en `logcat` ✓.

### Publicación en GitHub y actualización desde la app (18/10, lote GitHub)

- **Repositorios públicos** (cuenta `jonirecuenco-oss`): `SCCosmos` (Windows), `SCCosmos.Mobile`
  (Android) y `SCCosmos.Compartido` (motor compartido, nuevo: antes vivía fuera de los repos y sin
  él no compilan). Todo el trabajo pendiente quedó subido con su mensaje de cambios.
- **Release v1.0.0** en `jonirecuenco-oss/SCCosmos` con los dos archivos que la app usa para
  actualizarse: `SCCosmos-android.apk` (arm64) y `SCCosmos-windows.zip` (app de Windows compilada).
- **Motor compartido** (`SincroCompartido\ActualizadorSccosmos.cs`): número de versión de la app
  (`VersionApp`), consulta de la última versión publicada en GitHub (`releases/latest`, sin cuenta ni
  token: el proyecto es público), comparación de versiones y descarga con progreso.
- **Windows** (`ActualizacionPc`): sección «Versión y actualizaciones» en Configuración (versión
  instalada, interruptor «Actualizar automáticamente», botón «Buscar actualizaciones»). Al arrancar
  comprueba en segundo plano (a los ~15 s): si hay versión nueva pregunta (o, con el interruptor
  activado, descarga en silencio y la deja preparada). Al cerrar la app, un pequeño actualizador
  espera a que termine, copia los archivos nuevos (`robocopy`) y la vuelve a abrir.
- **Android** (`ActualizacionMobileService`): misma sección en Configuración y misma comprobación al
  abrir Inicio (~10 s). Descarga el APK y abre el instalador del sistema
  (`REQUEST_INSTALL_PACKAGES` + FileProvider); Android pide permiso la primera vez («instalar apps
  desconocidas») y muestra su confirmación (y el aviso de Play Protect, normal al instalar fuera de
  la tienda).
- **Publicar una versión nueva**: `Herramientas\publicar_version.ps1 -Version 1.0.1 -Notas "..."`.
  Actualiza el número de versión, compila el APK y el ZIP, sube los tres repos y crea el release con
  las notas y los archivos (lee la credencial de GitHub del Administrador de credenciales).
- **Verificado**:
  - Windows de punta a punta: se puso la etiqueta temporal v9.9.9 al release → la app avisó al
    arrancar, se aceptó, descargó 87 MB, se cerró, se reemplazó y **se volvió a abrir sola** ✓.
  - Android de punta a punta (emulador): «Estás al día (v1.0.0)» ✓ → con v9.9.9 avisó, descargó
    62,5 MB, abrió el instalador, se concedió el permiso una vez y la actualización quedó
    **instalada** (los datos se conservan) ✓.
  - Después se devolvió la etiqueta del release a v1.0.0.
