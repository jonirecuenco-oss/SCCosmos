using System;
using System.Linq;
using System.Windows;

namespace SCCosmos
{
    public class SemanaCalendario
    {
        public int Id { get; set; }
        public DateTime FechaMartes { get; set; }

        // Calculamos los días límite de la semana
        public DateTime FechaLunes => FechaMartes.AddDays(-1);
        public DateTime FechaDomingo => FechaMartes.AddDays(5);

        // Retorna la clave para la Base de Datos (Ej: "2026-06-30")
        public string IdIso => FechaMartes.ToString("yyyy-MM-dd");

        // =========================================================
        // MEJORA 1: SEMANAS PASADAS GRISÁCEAS
        // =========================================================
        // Si la semana ya ha pasado (hoy es mayor que el domingo), reducimos su opacidad al 40% (0.4)
        public double OpacidadSemana => DateTime.Today > FechaDomingo ? 0.4 : 1.0;

        // Formato para la tabla: "Del 3-ago al 8-ago" (AHORA EMPEZANDO EN LUNES)
        public string RangoFechas
        {
            get
            {
                string formatoFechas = $"Del {FechaLunes.ToString("dd-MMM").ToLower()} al {FechaDomingo.ToString("dd-MMM").ToLower()}";

                // Si es asamblea y hemos rellenado la fecha, se la pegamos al final entre paréntesis
                if (Actividad == "Asamblea de circuito" && !string.IsNullOrEmpty(FechaAsamblea))
                {
                    return $"{formatoFechas} ({FechaAsamblea})";
                }

                return formatoFechas;
            }
        }

        // =========================================================
        // MEJORA 2: MES BASADO EN EL LUNES (Agrupación correcta)
        // =========================================================
        public string Banner => FechaLunes.ToString("MMMM yyyy").ToUpper();

        public string Actividad { get; set; }
        public string Congregacion { get; set; }
        public string Acompanante { get; set; }
        public string Tipo { get; set; }
        public string Alojamiento { get; set; }
        public bool TieneDatosImportados { get; set; }

        // NUEVAS VARIABLES PARA ASAMBLEA
        public string SeccionAsamblea { get; set; }
        public string FechaAsamblea { get; set; }
        public bool NotificacionEnviada { get; set; }
        public bool RecordatorioEnviado { get; set; }

        // TRUCO VISUAL: Mostrará la sección en la columna "Congregación" si es asamblea
        public string CongregacionVisual => Actividad == "Asamblea de circuito" && !string.IsNullOrEmpty(SeccionAsamblea) ? $"Sección {SeccionAsamblea}" : Congregacion;

        public bool IsActual
        {
            get
            {
                DateTime hoy = DateTime.Today;
                return hoy >= FechaLunes && hoy <= FechaDomingo;
            }
        }

        public bool S302Enviado { get; set; }
        public bool DocEnviada { get; set; }

        // Trucos visuales para los colores de los botones
        public string ColorBotonNotificacion => NotificacionEnviada ? "#10893E" : "Transparent";
        public string ColorBotonRecordatorio => RecordatorioEnviado ? "#10893E" : "Transparent";
        public string ColorBotonS302 => S302Enviado ? "#10893E" : "Transparent";
        public string ColorBotonDoc => DocEnviada ? "#10893E" : "Transparent";

        // Trucos visuales controlados desde C# para la visibilidad de los botones
        public Visibility VisibilidadVisita => Actividad == "Visita" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility VisibilidadAsamblea => Actividad == "Asamblea de circuito" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility VisibilidadVerVisita => (Actividad == "Visita" && TieneDatosImportados) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EsClonada { get; set; } = Visibility.Collapsed;

        // Truco visual para mostrar solo al marido con formato "Apellido, Nombre"
        public string AcompananteVisual
        {
            get
            {
                if (string.IsNullOrEmpty(Acompanante)) return "";

                // 1. Separamos a la esposa (nos quedamos con lo que hay antes del " y ")
                string marido = Acompanante.Split(new[] { " y " }, StringSplitOptions.None)[0].Trim();

                // 2. Comprobamos si ya tiene el formato correcto con coma
                if (marido.Contains(","))
                {
                    return marido;
                }
                else
                {
                    // Si viene sin coma (ej: "Juan López"), le damos la vuelta
                    var partes = marido.Split(' ');
                    if (partes.Length > 1)
                    {
                        string apellido = partes[partes.Length - 1]; // Toma la última palabra como apellido
                        string nombre = string.Join(" ", partes.Take(partes.Length - 1)); // El resto es el nombre
                        return $"{apellido}, {nombre}";
                    }

                    return marido; // Por si solo hubiera una palabra
                }
            }
        }
    }
}