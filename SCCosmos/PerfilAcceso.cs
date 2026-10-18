using System;
using System.IO;
using System.Text.Json;

namespace SCCosmos
{
    // =====================================================================
    // PERFIL DE USUARIO Y NIVEL DE ACCESO (Windows)
    //
    // Igual que en Android: solo con perfil "Hombre" y el código correcto
    // se accede a todas las funciones. En caso contrario, solo Inicio,
    // Calendario, Personas y Configuración.
    //
    // Se guarda por equipo (no en la base de datos compartida) para que
    // el perfil del SC y el de su esposa no se mezclen al sincronizar.
    // =====================================================================

    public static class PerfilAcceso
    {
        public const string CodigoCompleto = "CODIGO_OCULTO";

        /// <summary>
        /// Código especial: da acceso completo y además activa para
        /// siempre la página de Descargas (que si no, queda oculta).
        /// </summary>
        public const string CodigoDescargas = "CODIGO_OCULTO_DESCARGAS";

        public const string PerfilHombre = "Hombre";
        public const string PerfilEsposa = "Esposa";

        public class DatosPerfil
        {
            public string Rol { get; set; } = "";
            public bool CodigoOk { get; set; }
            public bool DescargasActivadas { get; set; }
        }

        private static string RutaArchivo
        {
            get
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos");

                return Path.Combine(carpeta, "perfil_usuario.json");
            }
        }

        public static DatosPerfil Cargar()
        {
            try
            {
                string ruta = RutaArchivo;

                if (File.Exists(ruta))
                {
                    DatosPerfil? datos =
                        JsonSerializer.Deserialize<DatosPerfil>(
                            File.ReadAllText(ruta));

                    if (datos != null)
                    {
                        return datos;
                    }
                }
            }
            catch
            {
            }

            return new DatosPerfil();
        }

        public static void Guardar(DatosPerfil datos)
        {
            try
            {
                string ruta = RutaArchivo;

                string? carpeta = Path.GetDirectoryName(ruta);

                if (!string.IsNullOrWhiteSpace(carpeta))
                {
                    Directory.CreateDirectory(carpeta);
                }

                File.WriteAllText(
                    ruta,
                    JsonSerializer.Serialize(datos));
            }
            catch
            {
            }
        }

        public static bool Configurado =>
            !string.IsNullOrWhiteSpace(Cargar().Rol);

        public static bool EsHombre =>
            string.Equals(
                Cargar().Rol,
                PerfilHombre,
                StringComparison.OrdinalIgnoreCase);

        public static bool CodigoValidado =>
            Cargar().CodigoOk;

        /// <summary>
        /// La página de Descargas se activa para siempre al usar el
        /// código especial en el perfil.
        /// </summary>
        public static bool DescargasActivadas =>
            Cargar().DescargasActivadas;

        /// <summary>
        /// Acceso a todas las funciones. Si aún no hay perfil, se concede
        /// (se preguntará al arrancar la primera vez).
        /// </summary>
        public static bool AccesoCompleto =>
            !Configurado
            || (EsHombre && CodigoValidado);

        public static void EstablecerRol(string rol)
        {
            DatosPerfil datos = Cargar();

            bool esHombre =
                string.Equals(
                    rol,
                    PerfilHombre,
                    StringComparison.OrdinalIgnoreCase);

            datos.Rol =
                esHombre ? PerfilHombre : PerfilEsposa;

            if (!esHombre)
            {
                datos.CodigoOk = false;
            }

            Guardar(datos);
        }

        public static bool ValidarCodigo(string codigo)
        {
            DatosPerfil datos = Cargar();

            string limpio =
                (codigo ?? "").Trim();

            datos.CodigoOk =
                string.Equals(
                    limpio,
                    CodigoCompleto,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    limpio,
                    CodigoDescargas,
                    StringComparison.OrdinalIgnoreCase);

            // El código especial activa la página de Descargas para siempre.
            if (string.Equals(
                    limpio,
                    CodigoDescargas,
                    StringComparison.OrdinalIgnoreCase))
            {
                datos.DescargasActivadas =
                    true;
            }

            Guardar(datos);

            return datos.CodigoOk;
        }
    }
}
