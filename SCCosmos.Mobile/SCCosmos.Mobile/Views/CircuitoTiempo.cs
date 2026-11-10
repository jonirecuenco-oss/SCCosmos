using System.ComponentModel;
using System.Globalization;

using SCCosmos.Mobile.Models;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Views;

// =============================================================================
// CIRCUITO: EL TIEMPO DE CADA CONGREGACIÓN
// =============================================================================
//
// Cada congregación de la lista lleva un botón que despliega debajo su tiempo
// de AEMET: los 7 días (al tocar uno se ven sus horas) y las horas del día
// elegido, con el icono del aviso debajo de cada una, como en Inicio.

/// <summary>Una congregación de la lista de Circuito, con su tiempo.</summary>
public sealed class CongregacionCircuito : INotifyPropertyChanged
{
    private int _carga;


    public CongregacionCircuito(Congregacion congregacion) =>
        Congregacion = congregacion;


    public Congregacion Congregacion { get; }

    public string Nombre =>
        Congregacion.Nombre;

    public string DetalleLista =>
        Congregacion.DetalleLista;

    public bool TiempoAbierto { get; private set; }

    /// <summary>«Mérida · Hoy · ↑ 24°  ↓ 15° · lluvia 60%».</summary>
    public string Resumen { get; private set; } = "";

    public string Mensaje { get; private set; } = "";

    public bool HayMensaje =>
        Mensaje.Length > 0;

    public List<DiaCelda> Dias { get; private set; } = new();

    public List<HoraCelda> Horas { get; private set; } = new();

    /// <summary>La hora actual entre las horas (hoy), para empezar a verlas por ella.</summary>
    public int IndiceActual { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;


    /// <summary>Despliega o pliega el tiempo; la primera vez lo descarga.</summary>
    public async Task AlternarAsync()
    {
        TiempoAbierto = !TiempoAbierto;
        Avisar(nameof(TiempoAbierto));

        if (TiempoAbierto && Dias.Count == 0)
        {
            await CargarAsync(DateTime.Today);
        }
    }


    /// <summary>Descarga (o lee de lo guardado) el tiempo, con las horas de ese día.</summary>
    public async Task CargarAsync(DateTime dia)
    {
        int carga = ++_carga;

        try
        {
            LugarTiempo? lugar =
                await Task.Run(() =>
                    TiempoAemet.LugarDe(
                        Congregacion.Municipio,
                        Congregacion.Provincia,
                        Congregacion.Latitud,
                        Congregacion.Longitud,
                        Congregacion.Nombre));

            if (lugar is null)
            {
                PonerMensaje($"No se encuentra el municipio «{Congregacion.Municipio}» en la lista del INE.");
                return;
            }

            if (Dias.Count == 0)
            {
                PonerMensaje("Cargando el tiempo…");
            }

            SemanaTiempo semana =
                await Task.Run(() => TiempoAemet.ObtenerSemanaAsync(lugar, false, CancellationToken.None));

            TiempoDia tiempo =
                await Task.Run(() => TiempoAemet.ObtenerAsync(lugar, dia, false, CancellationToken.None));

            if (carga == _carga)
            {
                Pintar(lugar, semana, tiempo);
            }
        }
        catch
        {
            if (carga == _carga)
            {
                PonerMensaje("No se ha podido descargar el tiempo.");
            }
        }
    }


    private void Pintar(
        LugarTiempo lugar,
        SemanaTiempo semana,
        TiempoDia tiempo)
    {
        bool esHoy = tiempo.Fecha == DateTime.Today;
        int horaActual = DateTime.Now.Hour;

        ResumenDia? elegido =
            semana.Dias.FirstOrDefault(d => d.Fecha == tiempo.Fecha);

        Resumen =
            string.Join(
                " · ",
                new[]
                {
                    lugar.Nombre,
                    elegido?.Etiqueta ?? tiempo.Fecha.ToString("d MMM", CultureInfo.GetCultureInfo("es-ES")),
                    tiempo.Maxima is int maxima && tiempo.Minima is int minima ? $"↑ {maxima}°  ↓ {minima}°" : "",
                    tiempo.ProbLluviaDia is int lluvia && lluvia > 0 ? $"lluvia {lluvia}%" : ""
                }
                .Where(s => s.Length > 0));

        Dias =
            semana.Dias
                .Select(d => new DiaCelda(d, d.Fecha == tiempo.Fecha, this))
                .ToList();

        // Todas las horas del día (desde las 6, o antes si ya es de madrugada).
        List<HoraTiempo> visibles =
            tiempo.Horas
                .Where(h => !tiempo.PorHoras || h.Hora >= (esHoy ? Math.Min(6, horaActual) : 6))
                .ToList();

        Horas =
            visibles
                .Select(h => new HoraCelda(h, esHoy && h.Hora == horaActual))
                .ToList();

        IndiceActual =
            esHoy ? Math.Max(0, visibles.FindIndex(h => h.Hora == horaActual)) : 0;

        Mensaje =
            tiempo.SinPrevision ? "AEMET todavía no tiene previsión para ese día."
            : semana.Dias.Count == 0 && tiempo.Horas.Count == 0 ? "No se ha podido descargar el tiempo."
            : "";

        Avisar(
            nameof(Resumen),
            nameof(Dias),
            nameof(IndiceActual),
            nameof(Horas),
            nameof(Mensaje),
            nameof(HayMensaje));
    }


    private void PonerMensaje(string mensaje)
    {
        Mensaje = mensaje;
        Avisar(nameof(Mensaje), nameof(HayMensaje));
    }


    private void Avisar(params string[] propiedades)
    {
        foreach (string propiedad in propiedades)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        }
    }
}


/// <summary>Uno de los 7 días: al tocarlo, se ven sus horas debajo.</summary>
public sealed class DiaCelda
{
    public DiaCelda(
        ResumenDia dia,
        bool elegido,
        CongregacionCircuito congregacion)
    {
        Congregacion = congregacion;
        Fecha = dia.Fecha;
        Etiqueta = dia.Etiqueta;
        Icono = dia.Icono + ".png";
        Maxima = dia.Maxima is int maxima ? $"{maxima}°" : "";
        Minima = dia.Minima is int minima ? $"{minima}°" : "";
        Lluvia = dia.TextoLluvia;
        HayAviso = dia.Aviso is not null;
        IconoAviso = $"icono_aviso_{dia.Aviso?.NivelIcono ?? "amarillo"}.png";
        Fondo = elegido ? Color.FromArgb("#1E2228") : Colors.Transparent;
    }

    public CongregacionCircuito Congregacion { get; }

    public DateTime Fecha { get; }

    public string Etiqueta { get; }

    public string Icono { get; }

    public string Maxima { get; }

    public string Minima { get; }

    public string Lluvia { get; }

    /// <summary>El aviso de más nivel de ese día: su icono, del color del nivel.</summary>
    public bool HayAviso { get; }

    public string IconoAviso { get; }

    public Color Fondo { get; }
}


/// <summary>Una hora (o un tramo) del día elegido, con el icono de su aviso.</summary>
public sealed class HoraCelda
{
    public HoraCelda(
        HoraTiempo hora,
        bool destacada)
    {
        Etiqueta = hora.Etiqueta;
        Icono = hora.Icono + ".png";
        Temperatura = hora.Temperatura is int t ? $"{t}°" : "";
        Lluvia = hora.TextoLluvia;
        HayAviso = hora.Aviso is not null;
        IconoAviso = $"icono_aviso_{hora.Aviso?.NivelIcono ?? "amarillo"}.png";
        TextoAvisos = hora.TextoAvisos;
        Fondo = destacada ? Color.FromArgb("#1E2228") : Colors.Transparent;

        // Los tramos («12–24 h», «Todo el día») necesitan más sitio que una hora.
        Ancho = hora.Hora < 0 ? 74 : 54;
    }

    public string Etiqueta { get; }

    public string Icono { get; }

    public string Temperatura { get; }

    public string Lluvia { get; }

    public bool HayAviso { get; }

    public string IconoAviso { get; }

    /// <summary>Qué avisos son (al tocar la hora).</summary>
    public string TextoAvisos { get; }

    public Color Fondo { get; }

    public double Ancho { get; }
}
