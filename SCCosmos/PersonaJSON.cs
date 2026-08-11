using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SCCosmos
{
    public class PersonaJson
    {
        [JsonPropertyName("nombre")]
        public string Nombre { get; set; }

        [JsonPropertyName("grupo")]
        public int Grupo { get; set; }

        [JsonPropertyName("fecha_nacimiento")]
        public string FechaNacimiento { get; set; }

        [JsonPropertyName("sexo")]
        public string Sexo { get; set; }

        [JsonPropertyName("fecha_bautismo")]
        public string FechaBautismo { get; set; }

        [JsonPropertyName("esperanza")]
        public string Esperanza { get; set; }

        [JsonPropertyName("nombramiento")]
        public string Nombramiento { get; set; }

        [JsonPropertyName("servicio")]
        public string Servicio { get; set; }

        [JsonPropertyName("anio_servicio")]
        public string AnioServicio { get; set; }

        [JsonPropertyName("informe")]
        public List<InformeJson> Informe { get; set; }

    }

    public class InformeJson
    {
        [JsonPropertyName("mes")]
        public string Mes { get; set; }

        [JsonPropertyName("participacion")]
        public bool Participacion { get; set; }

        [JsonPropertyName("cursos_biblicos")]
        public object CursosBiblicos { get; set; }

        [JsonPropertyName("precursor_auxiliar")]
        public bool PrecursorAuxiliar { get; set; }

        [JsonPropertyName("horas")]
        public object Horas { get; set; }

        [JsonPropertyName("notas")]
        public string Notas { get; set; }
    }
}