using System;

namespace SCCosmos
{
    /// <summary>
    /// Una tarea o evento programado, compartible con la esposa del SC.
    /// </summary>
    public class Tarea
    {
        public int Id { get; set; }

        public string Titulo { get; set; } = "";

        public string Descripcion { get; set; } = "";

        public string FechaInicio { get; set; } = "";

        public string FechaFin { get; set; } = "";

        public bool TodoElDia { get; set; } = true;

        public string Hora { get; set; } = "";

        public int AvisoMinutos { get; set; } = -1;

        public string Repeticion { get; set; } = "Ninguna";

        public bool Compartida { get; set; }

        public bool Completada { get; set; }

        public string CreadaPor { get; set; } = "";

        public string FechaCreacion { get; set; } = "";

        public string UltimaModificacion { get; set; } = "";


        public string TituloVisual =>
            string.IsNullOrWhiteSpace(Titulo)
                ? "(Sin título)"
                : Titulo;


        public string FechasVisual
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FechaInicio))
                {
                    return "";
                }

                if (!DateTime.TryParse(FechaInicio, out DateTime inicio))
                {
                    return FechaInicio;
                }

                string texto =
                    inicio.ToString("ddd d MMM yyyy").Replace(".", "");

                if (!string.IsNullOrWhiteSpace(FechaFin)
                    && DateTime.TryParse(FechaFin, out DateTime fin)
                    && fin.Date != inicio.Date)
                {
                    texto +=
                        "  →  " + fin.ToString("ddd d MMM").Replace(".", "");
                }

                if (!TodoElDia
                    && !string.IsNullOrWhiteSpace(Hora))
                {
                    texto += "  ·  " + Hora;
                }

                return texto;
            }
        }


        public bool MostrarRepeticion =>
            Repeticion != "Ninguna";

        public string RepeticionVisual =>
            MostrarRepeticion
                ? "🔁 " + Repeticion
                : "";


        public bool MostrarAviso =>
            AvisoMinutos >= 0;

        public string AvisoVisual =>
            AvisoMinutos switch
            {
                0 => "🔔 A la hora",
                10 => "🔔 10 min antes",
                30 => "🔔 30 min antes",
                60 => "🔔 1 hora antes",
                1440 => "🔔 1 día antes",
                _ => "🔔 " + AvisoMinutos + " min antes"
            };


        public bool MostrarEstado =>
            Completada;

        public string EstadoVisual =>
            Completada
                ? "✔ Completada"
                : "";

        public double Opacidad =>
            Completada
                ? 0.45
                : 1.0;
    }
}
