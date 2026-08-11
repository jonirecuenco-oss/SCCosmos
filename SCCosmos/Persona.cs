namespace SCCosmos
{
    public class Persona
    {
        public int Id { get; set; }
        public int Grupo { get; set; }
        public string Nombre { get; set; }
        public string FechaNacimiento { get; set; }
        public string Sexo { get; set; }
        public string FechaBautismo { get; set; }
        public string Esperanza { get; set; }
        public string Nombramiento { get; set; }
        public string Servicio { get; set; }
        public string CongregacionCodigo { get; set; }
        public string CongregacionNombre { get; set; } // Almacena el nombre legible tras hacer el JOIN
        public string Telefono { get; set; }
        public string Email { get; set; }
        public string FechaInicioPrec { get; set; }
        public string FechaUltimaEscuela { get; set; }
        public string NombreJWHub { get; set; }
        public int RequisitoHoras { get; set; }
        public string FechaCreacion { get; set; }
        public string FechaEliminacion { get; set; }
        public string Solicitudes { get; set; }
        public string Privilegios { get; set; }
        public string PrivilegiosLista => string.IsNullOrEmpty(Privilegios) ? "" : Privilegios.Replace(", ", "\n");
        public string Foto { get; set; }
    }
}