using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// Dónde se va a estar cada día, para el tiempo de AEMET en Inicio: el
    /// salón de la congregación que se visita (de martes a domingo) o, si no,
    /// donde está el móvil del SC (el móvil lo deja en Configuracion, que se
    /// sincroniza). Lo de AEMET es común: SincroCompartido/TiempoAemet.cs.
    /// </summary>
    public static class TiempoPc
    {
        // La misma clave que usa el móvil (TiempoAemetService.ClaveUbicacionCompartida).
        private const string ClaveUbicacionMovil = "TiempoUbicacionMovil";

        static TiempoPc()
        {
            TiempoAemet.CarpetaCache =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos",
                    "tiempo");
        }


        public static LugarTiempo LugarParaDia(DateTime dia) =>
            LugarVisita(dia) ?? UbicacionMovil();


        /// <summary>El sitio de una congregación, para su tiempo en Circuito.</summary>
        public static LugarTiempo LugarDe(Congregacion congregacion) =>
            congregacion == null
                ? null
                : TiempoAemet.LugarDe(
                    congregacion.Municipio,
                    congregacion.Provincia,
                    congregacion.Latitud,
                    congregacion.Longitud,
                    congregacion.Nombre);


        private static LugarTiempo LugarVisita(DateTime dia)
        {
            // El lunes ya no se está en la congregación (la visita acaba el domingo).
            if (dia.DayOfWeek == DayOfWeek.Monday)
            {
                return null;
            }

            DateTime martes =
                dia.Date.AddDays(-(((int)dia.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7));

            try
            {
                using var conexion =
                    new SqliteConnection($"Data Source={DatabaseHelper.RutaBaseDatos};Pooling=True");

                conexion.Open();

                using var cmd =
                    conexion.CreateCommand();

                cmd.CommandText = @"
                    SELECT s.Congregacion,
                           COALESCE(c.Municipio, ''),
                           COALESCE(c.Provincia, ''),
                           COALESCE(c.Latitud, 0),
                           COALESCE(c.Longitud, 0)
                    FROM Semanas s
                    LEFT JOIN Congregaciones c
                        ON c.Nombre = s.Congregacion
                    WHERE date(s.FechaMartes) = date($martes)
                      AND s.Actividad LIKE '%isita%'
                    LIMIT 1;";

                cmd.Parameters.AddWithValue(
                    "$martes",
                    martes.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                using var lector =
                    cmd.ExecuteReader();

                if (!lector.Read() || lector.IsDBNull(0))
                {
                    return null;
                }

                string municipio = lector.GetString(1);
                string ine = TiempoAemet.BuscarIne(municipio, lector.GetString(2));

                return ine == null
                    ? null
                    : new LugarTiempo(
                        ine,
                        TiempoAemet.NombreMunicipio(ine, municipio),
                        "Visita a " + lector.GetString(0),
                        Convert.ToDouble(lector.GetValue(3), CultureInfo.InvariantCulture),
                        Convert.ToDouble(lector.GetValue(4), CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }


        private static LugarTiempo UbicacionMovil()
        {
            string[] partes =
                (DatabaseHelper.ObtenerConfiguracion(ClaveUbicacionMovil, "") ?? "").Split('|');

            if (partes.Length < 4
                || !double.TryParse(partes[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud)
                || !double.TryParse(partes[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud))
            {
                return null;
            }

            return new LugarTiempo(
                partes[0],
                partes[1],
                "Ubicación del móvil",
                latitud,
                longitud);
        }
    }


    /// <summary>
    /// El tiempo de una congregación, debajo de su fila en Circuito: los 7
    /// días (al pulsar uno se ven sus horas) y las horas del día elegido, con
    /// el icono del aviso debajo de cada una, como en Inicio.
    /// </summary>
    public sealed class TiempoCongregacionVista : INotifyPropertyChanged
    {
        private readonly Congregacion _congregacion;

        private int _carga;

        // El salón y los pueblos de su territorio; el elegido (0, el del salón).
        private List<LugarTiempo> _lugares;

        private int _pueblo;

        private DateTime _dia = DateTime.Today;

        private readonly Dictionary<string, SemanaTiempo> _semanas = new Dictionary<string, SemanaTiempo>();

        public TiempoCongregacionVista(Congregacion congregacion)
        {
            _congregacion = congregacion;
        }

        public Congregacion Congregacion => _congregacion;

        /// <summary>El del salón y los pueblos de su territorio: al pulsar uno, su tiempo.</summary>
        public List<PuebloTiempoUI> Pueblos { get; private set; } = new List<PuebloTiempoUI>();

        public string Titulo { get; private set; } = "TIEMPO";

        public string Datos { get; private set; } = "";

        public string Mensaje { get; private set; } = "Cargando el tiempo…";

        public Visibility VerMensaje =>
            string.IsNullOrEmpty(Mensaje) ? Visibility.Collapsed : Visibility.Visible;

        public List<DiaTiempoUI> Dias { get; private set; } = new List<DiaTiempoUI>();

        public List<InicioPage.CeldaTiempo> Horas { get; private set; } = new List<InicioPage.CeldaTiempo>();

        /// <summary>La hora actual entre las horas (hoy), para empezar a verlas por ella.</summary>
        public int IndiceActual { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;


        /// <summary>Descarga (o lee de lo guardado) el tiempo, con las horas de ese día.</summary>
        public async Task CargarAsync(DateTime dia)
        {
            int carga = ++_carga;

            _dia = dia;

            try
            {
                if (_lugares == null)
                {
                    _lugares = await Task.Run(Lugares);
                }

                if (_lugares.Count == 0)
                {
                    Mensaje = $"No se encuentra el municipio «{_congregacion.Municipio}» en la lista del INE.";
                    Avisar(nameof(Mensaje), nameof(VerMensaje));
                    return;
                }

                LugarTiempo lugar =
                    _lugares[Math.Max(0, Math.Min(_pueblo, _lugares.Count - 1))];

                SemanaTiempo semana =
                    await Task.Run(() => TiempoAemet.ObtenerSemanaAsync(lugar, false, CancellationToken.None));

                _semanas[lugar.Ine] = semana;

                TiempoDia tiempo =
                    await Task.Run(() => TiempoAemet.ObtenerAsync(lugar, dia, false, CancellationToken.None));

                if (carga != _carga)
                {
                    return;
                }

                var es = CultureInfo.GetCultureInfo("es-ES");
                bool esHoy = tiempo.Fecha == DateTime.Today;
                int horaActual = DateTime.Now.Hour;

                ResumenDia elegido =
                    semana.Dias.FirstOrDefault(d => d.Fecha == tiempo.Fecha);

                Titulo =
                    $"{lugar.Nombre.ToUpper(es)} · {(elegido?.Etiqueta ?? tiempo.Fecha.ToString("d MMM", es)).ToUpper(es)}";

                Datos =
                    (tiempo.Maxima is int maxima && tiempo.Minima is int minima ? $"↑ {maxima}°   ↓ {minima}°" : "")
                    + (tiempo.ProbLluviaDia is int lluvia && lluvia > 0 ? $"   ·   lluvia {lluvia}%" : "");


                Dias =
                    semana.Dias
                        .Select(d => new DiaTiempoUI(d, d.Fecha == tiempo.Fecha, this))
                        .ToList();

                // Todas las horas del día, desde las 00h.
                List<HoraTiempo> visibles =
                    tiempo.Horas.ToList();

                Horas =
                    visibles
                        .Select(h => new InicioPage.CeldaTiempo(h, esHoy && h.Hora == horaActual))
                        .ToList();

                IndiceActual =
                    esHoy ? Math.Max(0, visibles.FindIndex(h => h.Hora == horaActual)) : 0;

                Mensaje =
                    tiempo.SinPrevision ? "AEMET todavía no tiene previsión para ese día."
                    : semana.Dias.Count == 0 && tiempo.Horas.Count == 0 ? "No se ha podido descargar el tiempo."
                    : "";

                PintarPueblos();

                Avisar(
                    nameof(Titulo),
                    nameof(Datos),
                    nameof(Dias),
                    nameof(IndiceActual),
                    nameof(Horas),
                    nameof(Mensaje),
                    nameof(VerMensaje));

                // Los demás pueblos, para su resumen en la fila (después, sin esperar por ellos).
                foreach (LugarTiempo otro in _lugares.Where(l => !_semanas.ContainsKey(l.Ine)).ToList())
                {
                    try
                    {
                        _semanas[otro.Ine] =
                            await Task.Run(() => TiempoAemet.ObtenerSemanaAsync(otro, false, CancellationToken.None));
                    }
                    catch
                    {
                    }
                }

                if (carga == _carga)
                {
                    PintarPueblos();
                }
            }
            catch
            {
                if (carga == _carga)
                {
                    Mensaje = "No se ha podido descargar el tiempo.";
                    Avisar(nameof(Mensaje), nameof(VerMensaje));
                }
            }
        }


        /// <summary>Al pulsar un pueblo de la fila, su tiempo (el mismo día).</summary>
        public async Task ElegirPuebloAsync(int indice)
        {
            if (indice == _pueblo)
            {
                return;
            }

            _pueblo = indice;
            PintarPueblos();

            await CargarAsync(_dia);
        }


        /// <summary>Tras cambiar sus pueblos: se vuelven a leer y se ve el del salón.</summary>
        public async Task RecargarPueblosAsync()
        {
            _lugares = null;
            _pueblo = 0;

            await CargarAsync(_dia);
        }


        // El salón (si su municipio está en la lista del INE) y los pueblos de su territorio.
        private List<LugarTiempo> Lugares()
        {
            var lista = new List<LugarTiempo>();

            if (TiempoPc.LugarDe(_congregacion) is LugarTiempo salon)
            {
                lista.Add(salon);
            }

            foreach (PuebloTerritorio pueblo in DatabaseHelper.ObtenerPueblosTerritorio(_congregacion.Codigo))
            {
                if (lista.All(l => l.Ine != pueblo.Ine))
                {
                    lista.Add(new LugarTiempo(pueblo.Ine, pueblo.Nombre, _congregacion.Nombre, pueblo.Latitud, pueblo.Longitud));
                }
            }

            return lista;
        }


        private void PintarPueblos()
        {
            Pueblos =
                (_lugares ?? new List<LugarTiempo>())
                    .Select((l, i) => new PuebloTiempoUI(
                        l,
                        i,
                        i == _pueblo,
                        _semanas.TryGetValue(l.Ine, out SemanaTiempo semana) ? semana.Dias.FirstOrDefault(d => d.Fecha == _dia) : null,
                        this))
                    .ToList();

            Avisar(nameof(Pueblos));
        }


        private void Avisar(params string[] propiedades)
        {
            foreach (string propiedad in propiedades)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
            }
        }
    }


    /// <summary>
    /// Un pueblo de la fila del tiempo (el del salón, el primero), con su día
    /// elegido: icono, máxima/mínima y el aviso de más nivel.
    /// </summary>
    public sealed class PuebloTiempoUI
    {
        public PuebloTiempoUI(LugarTiempo lugar, int indice, bool elegido, ResumenDia dia, TiempoCongregacionVista vista)
        {
            Vista = vista;
            Indice = indice;
            Nombre = lugar.Nombre;
            Temperaturas = dia?.Maxima is int maxima && dia.Minima is int minima ? $"{maxima}°/{minima}°" : "";
            (Simbolo, ColorIcono) = InicioPage.SimboloTiempo(dia?.Icono ?? "");
            VerIcono = dia == null ? Visibility.Collapsed : Visibility.Visible;
            VerAviso = dia?.Aviso == null ? Visibility.Collapsed : Visibility.Visible;
            ColorAviso = InicioPage.Pincel(dia?.Aviso?.Color ?? "#E5C07B");
            PuntosAviso = dia?.Aviso?.Puntos ?? "";
            Fondo = elegido ? InicioPage.Pincel("#1FFFFFFF") : Brushes.Transparent;
            Borde = InicioPage.Pincel(elegido ? "#4BA3E3" : "#33FFFFFF");
            Descripcion = dia == null
                ? lugar.Nombre
                : $"{lugar.Nombre}: {dia.Descripcion}" + (dia.Aviso == null ? "" : "\n" + dia.Aviso.Titulo + " · " + dia.Aviso.Periodo);
        }

        public TiempoCongregacionVista Vista { get; }
        public int Indice { get; }
        public string Nombre { get; }
        public string Temperaturas { get; }
        public Wpf.Ui.Controls.SymbolRegular Simbolo { get; }
        public Brush ColorIcono { get; }
        public Visibility VerIcono { get; }
        public Visibility VerAviso { get; }
        public Brush ColorAviso { get; }
        public string PuntosAviso { get; }
        /// <summary>El elegido, con fondo y el borde del acento.</summary>
        public Brush Fondo { get; }
        public Brush Borde { get; }
        public string Descripcion { get; }
    }


    /// <summary>Uno de los 7 días del tiempo de una congregación: al pulsarlo, sus horas.</summary>
    public sealed class DiaTiempoUI
    {
        public DiaTiempoUI(ResumenDia dia, bool elegido, TiempoCongregacionVista vista)
        {
            Vista = vista;
            Fecha = dia.Fecha;
            Etiqueta = dia.Etiqueta;
            Maxima = dia.Maxima is int maxima ? $"{maxima}°" : "";
            Minima = dia.Minima is int minima ? $"{minima}°" : "";
            Lluvia = dia.TextoLluvia;
            (Simbolo, ColorIcono) = InicioPage.SimboloTiempo(dia.Icono);
            Fondo = elegido ? InicioPage.Pincel("#1FFFFFFF") : Brushes.Transparent;
            VerAviso = dia.Aviso == null ? Visibility.Collapsed : Visibility.Visible;
            ColorAviso = InicioPage.Pincel(dia.Aviso?.Color ?? "#E5C07B");
            PuntosAviso = dia.Aviso?.Puntos ?? "";
            Descripcion = dia.Descripcion + (dia.Aviso == null ? "" : "\n" + dia.Aviso.Titulo + " · " + dia.Aviso.Periodo);
        }

        public TiempoCongregacionVista Vista { get; }
        public DateTime Fecha { get; }
        public string Etiqueta { get; }
        public Wpf.Ui.Controls.SymbolRegular Simbolo { get; }
        public Brush ColorIcono { get; }
        public string Maxima { get; }
        public string Minima { get; }
        public string Lluvia { get; }
        public Brush Fondo { get; }
        public Visibility VerAviso { get; }
        public Brush ColorAviso { get; }
        public string PuntosAviso { get; }
        public string Descripcion { get; }
    }
}
