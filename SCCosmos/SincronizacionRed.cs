using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// Motor de sincronización entre dispositivos en Windows: arranca
    /// el servicio de red y mantiene la configuración en AppData.
    /// </summary>
    public static class SincronizacionRed
    {
        private static SincroConfiguracion? _config;
        private static SincroAlmacen? _almacen;
        private static ServicioSincro? _servicio;


        private static string RutaConfig =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SCCosmos",
                "sincro.json");


        public static SincroConfiguracion? Config =>
            _config;


        public static ServicioSincro? Servicio =>
            _servicio;


        /// <summary>
        /// Cambios llegados de otro dispositivo (para refrescar las
        /// pantallas abiertas).
        /// </summary>
        public static event Action<int>? DatosRecibidos;


        // =========================================================
        // ARRANCAR
        // =========================================================

        public static void Iniciar()
        {
            if (_config is not null)
            {
                return;
            }

            try
            {
                _config =
                    SincroConfiguracion.Cargar(
                        RutaConfig,
                        Environment.MachineName);

                _config.Guardar(
                    RutaConfig);

                _almacen =
                    new SincroAlmacen(
                        DatabaseHelper.RutaBaseDatos,
                        _config.IdDispositivo);

                _almacen.Asegurar();

                // Canal por carpeta: los cambios viajan también por la
                // carpeta que el usuario ya sincroniza (sirve fuera de
                // casa, por datos móviles, sin añadir nada).
                ICanalArchivosSincro? canalArchivos =
                    null;

                try
                {
                    string carpetaSincro =
                        Path.Combine(
                            DatabaseHelper.RutaDirectorioDatos,
                            "Sincro");

                    canalArchivos =
                        new CanalArchivosPc(
                            carpetaSincro);
                }
                catch
                {
                }

                _servicio =
                    new ServicioSincro(
                        _config,
                        _almacen,
                        canalArchivos);

                _servicio.DatosRecibidos +=
                    n => DatosRecibidos?.Invoke(n);

                if (_config.Activo)
                {
                    _servicio.Iniciar();
                }
            }
            catch
            {
                // Si algo falla, la aplicación sigue funcionando
                // sin sincronización entre dispositivos.
            }
        }


        /// <summary>
        /// Envía ahora lo que quede pendiente (dispositivos vinculados y
        /// carpeta compartida) antes de cerrar la aplicación, para no
        /// perder los últimos cambios.
        /// </summary>
        public static async Task VaciarPendientesAsync(
            TimeSpan? limite = null)
        {
            ServicioSincro? servicio =
                _servicio;

            if (servicio is null)
            {
                return;
            }

            try
            {
                await servicio.VaciarPendientesAsync(
                    limite);
            }
            catch
            {
            }
        }


        // =========================================================
        // DETENER
        // =========================================================

        public static void Detener()
        {
            try
            {
                _servicio?.Detener();
            }
            catch
            {
            }

            _servicio =
                null;
        }


        /// <summary>
        /// Vuelve a arrancar con la configuración actual (después de
        /// cambiarla en la pantalla de Configuración).
        /// </summary>
        public static void Reiniciar()
        {
            Detener();

            _config =
                null;

            Iniciar();
        }


        public static void Guardar()
        {
            try
            {
                _config?.Guardar(
                    RutaConfig);
            }
            catch
            {
            }
        }


        /// <summary>
        /// Lista de dispositivos conocidos con su estado, para pintarla
        /// en Configuración con la casilla de sincronización.
        /// </summary>
        public static List<DispositivoSyncPc> ObtenerDispositivosVista()
        {
            var lista =
                new List<DispositivoSyncPc>();

            try
            {
                ServicioSincro? servicio =
                    _servicio;

                if (servicio is null)
                {
                    return lista;
                }

                foreach (InfoDispositivoSincro dispositivo in
                         servicio.ObtenerDispositivos())
                {
                    lista.Add(
                        new DispositivoSyncPc
                        {
                            Id =
                                dispositivo.Id,
                            Nombre =
                                dispositivo.Nombre,
                            Detalle =
                                (dispositivo.Conectado
                                    ? "Conectado"
                                    : "Visible")
                                + (string.IsNullOrWhiteSpace(
                                        dispositivo.Direccion)
                                    ? ""
                                    : " · " + dispositivo.Direccion),
                            Habilitado =
                                _config?.EstaHabilitado(
                                    dispositivo.Id)
                                ?? true
                        });
                }
            }
            catch
            {
            }

            return lista;
        }


        /// <summary>
        /// Marca o desmarca un dispositivo en la lista: si se desmarca,
        /// no se sincroniza con él y se corta la conexión.
        /// </summary>
        public static void EstablecerHabilitado(
            string id,
            string nombre,
            bool habilitado)
        {
            try
            {
                if (_config is null
                    || string.IsNullOrWhiteSpace(
                        id))
                {
                    return;
                }

                if (_config.EstaHabilitado(
                        id)
                    == habilitado)
                {
                    return;
                }

                _config.Deshabilitados.RemoveAll(
                    x => string.Equals(
                        x,
                        id,
                        StringComparison.OrdinalIgnoreCase));

                if (!habilitado)
                {
                    _config.Deshabilitados.Add(
                        id);

                    _servicio?.Desconectar(
                        id);
                }

                if (string.IsNullOrWhiteSpace(
                        nombre))
                {
                    try
                    {
                        nombre =
                            _servicio
                                ?.ObtenerDispositivos()
                                .FirstOrDefault(
                                    d => string.Equals(
                                        d.Id,
                                        id,
                                        StringComparison.OrdinalIgnoreCase))
                                ?.Nombre
                            ?? "";
                    }
                    catch
                    {
                    }
                }

                string etiqueta =
                    string.IsNullOrWhiteSpace(
                        nombre)
                        ? id
                        : nombre;

                DatabaseHelper.AnotarActividad(
                    "Configuración",
                    habilitado
                        ? $"Volvió a marcar la sincronización con «{etiqueta}»"
                        : $"Desmarcó la sincronización con «{etiqueta}»");

                Guardar();
            }
            catch
            {
            }
        }
    }


    /// <summary>
    /// Fila de la lista de dispositivos de sincronización.
    /// </summary>
    public sealed class DispositivoSyncPc
    {
        public string Id
        {
            get;
            set;
        } = "";

        public string Nombre
        {
            get;
            set;
        } = "";

        public string Detalle
        {
            get;
            set;
        } = "";

        public bool Habilitado
        {
            get;
            set;
        } = true;
    }
}
