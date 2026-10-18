# SCCosmos

Aplicación de gestión del circuito para el superintendente de circuito:
versión de escritorio (Windows) y aplicación móvil (Android), con un motor
compartido de sincronización entre dispositivos.

## Carpetas

- `SCCosmos/` — aplicación de Windows (WPF).
- `SCCosmos.Mobile/` — aplicación de Android (MAUI).
- `SincroCompartido/` — motor compartido: sincronización entre dispositivos,
  conversión de tarjetas S-21 (PDF) y comprobación y descarga de versiones.

## Versiones y actualizaciones

Cada versión se publica en la sección **Releases** de este repositorio con:

- `SCCosmos-android.apk` — aplicación Android (arm64).
- `SCCosmos-windows.zip` — aplicación Windows (descomprimir y ejecutar
  `SCCosmos.exe`).

Las aplicaciones comprueban al arrancar si hay una versión nueva y ofrecen
actualizarse (o lo hacen solas si está activado «Actualizar automáticamente»
en Configuración).
