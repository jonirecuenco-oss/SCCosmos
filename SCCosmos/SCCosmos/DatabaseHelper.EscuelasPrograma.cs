using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SCCosmos
{
    /// <summary>
    /// Programa de la escuela: los 4 talleres (asignado, acompañante y
    /// hora de ensayo por punto) y el horario de la semana (oraciones;
    /// las horas de las clases son fijas).
    /// </summary>
    public static partial class DatabaseHelper
    {
        private const string SqlTablaEscuelasTalleres =
            @"CREATE TABLE IF NOT EXISTS EscuelasTalleres (
                Id TEXT PRIMARY KEY,
                EscuelaId TEXT DEFAULT '',
                Taller INTEGER DEFAULT 1,
                Numero INTEGER DEFAULT 0,
                Minutos TEXT DEFAULT '',
                Tema TEXT DEFAULT '',
                AsignadoId INTEGER DEFAULT 0,
                AsignadoNombre TEXT DEFAULT '',
                AcompananteId INTEGER DEFAULT 0,
                AcompananteNombre TEXT DEFAULT '',
                HoraEnsayo TEXT DEFAULT ''
            );";

        private const string SqlTablaEscuelasHorario =
            @"CREATE TABLE IF NOT EXISTS EscuelasHorario (
                Id TEXT PRIMARY KEY,
                EscuelaId TEXT DEFAULT '',
                Dia TEXT DEFAULT '',
                OracionInicialId INTEGER DEFAULT 0,
                OracionInicialNombre TEXT DEFAULT '',
                OracionIntermediaId INTEGER DEFAULT 0,
                OracionIntermediaNombre TEXT DEFAULT '',
                OracionFinalId INTEGER DEFAULT 0,
                OracionFinalNombre TEXT DEFAULT ''
            );";


        public static void AsegurarTablasEscuelasProgramaInterno(
            SqliteConnection connection)
        {
            EjecutarQuery(
                connection,
                SqlTablaEscuelasTalleres);

            EjecutarQuery(
                connection,
                SqlTablaEscuelasHorario);
        }


        // =====================================================
        // DATOS FIJOS DEL PROGRAMA (una sola transcripción)
        // =====================================================

        public static List<string> DiasEscuela { get; } =
            new()
            {
                "Lunes",
                "Martes",
                "Miércoles",
                "Jueves",
                "Viernes",
                "Sábado"
            };


        /// <summary>
        /// Los 18 puntos fijos de los 4 talleres: (taller, nº, minutos, tema).
        /// </summary>
        public static List<(int Taller, int Numero, string Minutos, string Tema)>
            TalleresFijos() =>
            new()
            {
                (1, 1, "1", "Un matrimonio se prepara para el ministerio"),
                (1, 2, "2", "Primera conversación en el ministerio de casa en casa"),
                (1, 3, "3", "Primera conversación en el ministerio de casa en casa"),
                (2, 4, "1", "En el transporte público o sentado en un lugar público"),
                (2, 5, "2x3", "Predicando en un lugar público (calle, parque, parada de autobús, negocio u otro sitio)"),
                (2, 6, "4", "El estudiante puede elegir un marco que sea común en la zona"),
                (3, 7, "4", "Después de la 1ª conversación, un publicador experimentado ayuda uno nuevo a planear la revista"),
                (3, 8, "2+3", "Predicación de casa en casa o cualquier forma de predicación pública"),
                (3, 9, "3", "Dos publicadores se preparan para hacer sus revisitas ese día"),
                (3, 10, "2", "Primera revisita a alguien que mostró interés"),
                (3, 11, "3", "Un pub. quedó en volver a visitar a una persona a una hora específica, pero no lo ha encontrado"),
                (3, 12, "3", "Un pub. vuelve para llevarle a una persona una publicación que trata de un tema que le interesó"),
                (3, 13, "3", "Un pub. suele tener conversaciones de la Biblia con una persona interesada sin dejarle publicaciones"),
                (3, 14, "3", "Un pub. revisa, un pub. comienza un CB empleando el folleto Disfrute (electrónico o impreso)"),
                (4, 15, "4", "Un publicador comienza un curso bíblico con el folleto Disfrute de la vida"),
                (4, 16, "3", "Un publicador dirige de forma eficaz una clase bíblica"),
                (4, 17, "4", "Un publicador da una clase bíblica con el libro Disfrute de la vida"),
                (4, 18, "2", "Un publicador da una clase bíblica con el libro Disfrute de la vida")
            };


        /// <summary>
        /// Canción del principio del día.
        /// </summary>
        public static string CancionInicialDe(
            string dia)
        {
            switch (dia)
            {
                case "Lunes": return "Canción 81";
                case "Martes": return "Canción 67";
                case "Miércoles": return "Canción 70";
                case "Jueves": return "Canción 77";
                case "Viernes": return "Canción 68";
                case "Sábado": return "Canción 76";
                default: return "";
            }
        }


        /// <summary>
        /// Canción del final del día.
        /// </summary>
        public static string CancionFinalDe(
            string dia)
        {
            switch (dia)
            {
                case "Lunes": return "Canción 73";
                case "Martes": return "Canción 58";
                case "Miércoles": return "Canción 57";
                case "Jueves": return "Canción 60";
                case "Viernes": return "Canción 79";
                case "Sábado": return "Canción 84";
                default: return "";
            }
        }


        /// <summary>
        /// Bloques de clase del día, con las horas fijas del programa.
        /// </summary>
        public static List<(string Nombre, string Horario, bool EsPalabrasFinales)>
            LeccionesDe(
                string dia)
        {
            switch (dia)
            {
                case "Lunes":
                    return new()
                    {
                        ("Lección 1A", "8:25-9:00", false),
                        ("Lección 1B", "9:00-10:15", false),
                        ("Lección 2A", "10:30-12:00", false),
                        ("Lección 2B", "13:00-14:25", false),
                        ("Lección 3A", "14:40-16:00", false),
                        ("Lección 3B", "16:00-16:35", false)
                    };

                case "Martes":
                    return new()
                    {
                        ("Lección 4A", "8:25-9:30", false),
                        ("Lección 4B", "9:30-10:30", false),
                        ("Lección 5A", "10:45-12:00", false),
                        ("Lección 5B", "13:00-14:25", false),
                        ("Lección 6A", "14:40-16:00", false),
                        ("Lección 6B", "16:00-16:35", false)
                    };

                case "Miércoles":
                    return new()
                    {
                        ("Lección 7A", "8:25-9:30", false),
                        ("Lección 7B", "9:30-10:30", false),
                        ("Lección 8A", "10:45-12:00", false),
                        ("Lección 8B", "13:00-14:25", false),
                        ("Lección 9A", "14:40-16:00", false),
                        ("Lección 9B", "16:00-16:35", false)
                    };

                case "Jueves":
                    return new()
                    {
                        ("Lección 10A", "8:25-9:30", false),
                        ("Lección 10B", "9:30-10:30", false),
                        ("Lección 11A", "10:45-12:00", false),
                        ("Lección 11B", "13:00-14:25", false),
                        ("Lección 12A", "14:40-16:00", false),
                        ("Lección 12B", "16:00-16:35", false)
                    };

                case "Viernes":
                    return new()
                    {
                        ("Lección 13A", "8:25-9:30", false),
                        ("Lección 13B", "9:30-10:30", false),
                        ("Lección 14A", "10:45-12:00", false),
                        ("Lección 14B", "13:00-14:25", false),
                        ("Lección 15A", "14:40-16:00", false),
                        ("Lección 15B", "16:00-16:35", false)
                    };

                case "Sábado":
                    return new()
                    {
                        ("Lección 16A", "8:25-9:30", false),
                        ("Lección 16B", "9:30-10:30", false),
                        ("Lección 17A", "10:45-12:00", false),
                        ("Lección 17B", "13:00-14:00", false),
                        ("Palabras finales y conclusión", "14:10-15:15", true)
                    };

                default:
                    return new();
            }
        }


        // =====================================================
        // TALLERES
        // =====================================================

        /// <summary>
        /// Crea los 18 puntos la primera vez que se abren los talleres
        /// de una escuela (auto-reparable).
        /// </summary>
        public static void AsegurarTalleresEscuela(
            string escuelaId)
        {
            if (string.IsNullOrWhiteSpace(
                    escuelaId))
            {
                return;
            }

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var contar =
                    connection.CreateCommand();

                contar.CommandText =
                    "SELECT COUNT(*) FROM EscuelasTalleres WHERE EscuelaId = $e;";

                contar.Parameters.AddWithValue(
                    "$e",
                    escuelaId);

                long existentes =
                    (long)contar.ExecuteScalar();

                if (existentes > 0)
                {
                    return;
                }

                foreach ((int taller, int numero, string minutos, string tema) in
                         TalleresFijos())
                {
                    var cmd =
                        connection.CreateCommand();

                    cmd.CommandText =
                        @"INSERT INTO EscuelasTalleres
                              (Id, EscuelaId, Taller, Numero, Minutos, Tema,
                               AsignadoId, AsignadoNombre,
                               AcompananteId, AcompananteNombre, HoraEnsayo)
                          VALUES
                              ($id, $e, $t, $n, $m, $tema, 0, '', 0, '', '');";

                    cmd.Parameters.AddWithValue(
                        "$id",
                        Guid.NewGuid().ToString("N"));

                    cmd.Parameters.AddWithValue(
                        "$e",
                        escuelaId);

                    cmd.Parameters.AddWithValue(
                        "$t",
                        taller);

                    cmd.Parameters.AddWithValue(
                        "$n",
                        numero);

                    cmd.Parameters.AddWithValue(
                        "$m",
                        minutos);

                    cmd.Parameters.AddWithValue(
                        "$tema",
                        tema);

                    cmd.ExecuteNonQuery();
                }
            }
        }


        public static List<EscuelaTallerPunto> ObtenerTalleresEscuela(
            string escuelaId)
        {
            AsegurarTalleresEscuela(
                escuelaId);

            var lista =
                new List<EscuelaTallerPunto>();

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"SELECT Id, EscuelaId, Taller, Numero, Minutos, Tema,
                             AsignadoId, AsignadoNombre,
                             AcompananteId, AcompananteNombre, HoraEnsayo
                        FROM EscuelasTalleres
                       WHERE EscuelaId = $e
                       ORDER BY Numero ASC;";

                cmd.Parameters.AddWithValue(
                    "$e",
                    escuelaId ?? "");

                using (var reader =
                       cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            new EscuelaTallerPunto
                            {
                                Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
                                EscuelaId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Taller = reader.IsDBNull(2) ? 1 : reader.GetInt32(2),
                                Numero = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                Minutos = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Tema = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                AsignadoId = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                                AsignadoNombre = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                AcompananteId = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                                AcompananteNombre = reader.IsDBNull(9) ? "" : reader.GetString(9),
                                HoraEnsayo = reader.IsDBNull(10) ? "" : reader.GetString(10)
                            });
                    }
                }
            }

            return lista;
        }


        public static void GuardarTallerPunto(
            EscuelaTallerPunto punto,
            string descripcionActividad)
        {
            if (punto == null
                || string.IsNullOrWhiteSpace(
                    punto.Id))
            {
                return;
            }

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"UPDATE EscuelasTalleres
                         SET AsignadoId = $aid,
                             AsignadoNombre = $anom,
                             AcompananteId = $cid,
                             AcompananteNombre = $cnom,
                             HoraEnsayo = $h
                       WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$aid", punto.AsignadoId);
                cmd.Parameters.AddWithValue("$anom", punto.AsignadoNombre ?? "");
                cmd.Parameters.AddWithValue("$cid", punto.AcompananteId);
                cmd.Parameters.AddWithValue("$cnom", punto.AcompananteNombre ?? "");
                cmd.Parameters.AddWithValue("$h", punto.HoraEnsayo ?? "");
                cmd.Parameters.AddWithValue("$id", punto.Id);

                cmd.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(
                    descripcionActividad))
            {
                AnotarActividad(
                    "Escuelas",
                    descripcionActividad);
            }
        }


        // =====================================================
        // HORARIO
        // =====================================================

        /// <summary>
        /// Crea las seis filas del horario la primera vez que se abre
        /// (auto-reparable). Las canciones y las horas son fijas.
        /// </summary>
        public static void AsegurarHorarioEscuela(
            string escuelaId)
        {
            if (string.IsNullOrWhiteSpace(
                    escuelaId))
            {
                return;
            }

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var contar =
                    connection.CreateCommand();

                contar.CommandText =
                    "SELECT COUNT(*) FROM EscuelasHorario WHERE EscuelaId = $e;";

                contar.Parameters.AddWithValue(
                    "$e",
                    escuelaId);

                long existentes =
                    (long)contar.ExecuteScalar();

                if (existentes > 0)
                {
                    return;
                }

                foreach (string dia in
                         DiasEscuela)
                {
                    var cmd =
                        connection.CreateCommand();

                    cmd.CommandText =
                        @"INSERT INTO EscuelasHorario
                              (Id, EscuelaId, Dia,
                               OracionInicialId, OracionInicialNombre,
                               OracionIntermediaId, OracionIntermediaNombre,
                               OracionFinalId, OracionFinalNombre)
                          VALUES
                              ($id, $e, $d, 0, '', 0, '', 0, '');";

                    cmd.Parameters.AddWithValue(
                        "$id",
                        Guid.NewGuid().ToString("N"));

                    cmd.Parameters.AddWithValue(
                        "$e",
                        escuelaId);

                    cmd.Parameters.AddWithValue(
                        "$d",
                        dia);

                    cmd.ExecuteNonQuery();
                }
            }
        }


        public static List<EscuelaHorarioDia> ObtenerHorarioEscuela(
            string escuelaId)
        {
            AsegurarHorarioEscuela(
                escuelaId);

            var lista =
                new List<EscuelaHorarioDia>();

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"SELECT Id, EscuelaId, Dia,
                             OracionInicialId, OracionInicialNombre,
                             OracionIntermediaId, OracionIntermediaNombre,
                             OracionFinalId, OracionFinalNombre
                        FROM EscuelasHorario
                       WHERE EscuelaId = $e;";

                cmd.Parameters.AddWithValue(
                    "$e",
                    escuelaId ?? "");

                using (var reader =
                       cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(
                            new EscuelaHorarioDia
                            {
                                Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
                                EscuelaId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Dia = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                OracionInicialId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                OracionInicialNombre = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                OracionIntermediaId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                                OracionIntermediaNombre = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                OracionFinalId = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                                OracionFinalNombre = reader.IsDBNull(8) ? "" : reader.GetString(8)
                            });
                    }
                }
            }

            // Orden fijo de la semana.
            lista.Sort(
                (a, b) =>
                    DiasEscuela.IndexOf(a.Dia)
                        .CompareTo(
                            DiasEscuela.IndexOf(b.Dia)));

            return lista;
        }


        public static void GuardarHorarioDia(
            EscuelaHorarioDia dia,
            string descripcionActividad)
        {
            if (dia == null
                || string.IsNullOrWhiteSpace(
                    dia.Id))
            {
                return;
            }

            using (var connection =
                   new SqliteConnection(
                       ConnectionString))
            {
                connection.Open();

                AsegurarTablasEscuelasProgramaInterno(
                    connection);

                var cmd =
                    connection.CreateCommand();

                cmd.CommandText =
                    @"UPDATE EscuelasHorario
                         SET OracionInicialId = $oi,
                             OracionInicialNombre = $oin,
                             OracionIntermediaId = $om,
                             OracionIntermediaNombre = $omn,
                             OracionFinalId = $of,
                             OracionFinalNombre = $ofn
                       WHERE Id = $id;";

                cmd.Parameters.AddWithValue("$oi", dia.OracionInicialId);
                cmd.Parameters.AddWithValue("$oin", dia.OracionInicialNombre ?? "");
                cmd.Parameters.AddWithValue("$om", dia.OracionIntermediaId);
                cmd.Parameters.AddWithValue("$omn", dia.OracionIntermediaNombre ?? "");
                cmd.Parameters.AddWithValue("$of", dia.OracionFinalId);
                cmd.Parameters.AddWithValue("$ofn", dia.OracionFinalNombre ?? "");
                cmd.Parameters.AddWithValue("$id", dia.Id);

                cmd.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(
                    descripcionActividad))
            {
                AnotarActividad(
                    "Escuelas",
                    descripcionActividad);
            }
        }
    }


    /// <summary>
    /// Un punto de un taller (asignado, acompañante y hora de ensayo).
    /// </summary>
    public class EscuelaTallerPunto
    {
        public string Id { get; set; } = "";
        public string EscuelaId { get; set; } = "";
        public int Taller { get; set; } = 1;
        public int Numero { get; set; }
        public string Minutos { get; set; } = "";
        public string Tema { get; set; } = "";
        public int AsignadoId { get; set; }
        public string AsignadoNombre { get; set; } = "";
        public int AcompananteId { get; set; }
        public string AcompananteNombre { get; set; } = "";
        public string HoraEnsayo { get; set; } = "";

        public string TallerTexto =>
            "T" + Taller;

        public string Titulo =>
            Numero + " · " + Tema + " (" + Minutos + " min)";

        public string AsignadoTexto =>
            string.IsNullOrWhiteSpace(
                AsignadoNombre)
                ? "— nadie —"
                : AsignadoNombre;

        public string AcompananteTexto =>
            string.IsNullOrWhiteSpace(
                AcompananteNombre)
                ? "— nadie —"
                : AcompananteNombre;

        public string EnsayoTexto =>
            string.IsNullOrWhiteSpace(
                HoraEnsayo)
                ? "—"
                : HoraEnsayo;
    }


    /// <summary>
    /// Un día del horario, con sus tres oraciones.
    /// </summary>
    public class EscuelaHorarioDia
    {
        public string Id { get; set; } = "";
        public string EscuelaId { get; set; } = "";
        public string Dia { get; set; } = "";
        public int OracionInicialId { get; set; }
        public string OracionInicialNombre { get; set; } = "";
        public int OracionIntermediaId { get; set; }
        public string OracionIntermediaNombre { get; set; } = "";
        public int OracionFinalId { get; set; }
        public string OracionFinalNombre { get; set; } = "";

        public string OracionInicialTexto =>
            string.IsNullOrWhiteSpace(
                OracionInicialNombre)
                ? "— asignar —"
                : OracionInicialNombre;

        public string OracionIntermediaTexto =>
            string.IsNullOrWhiteSpace(
                OracionIntermediaNombre)
                ? "— asignar —"
                : OracionIntermediaNombre;

        public string OracionFinalTexto =>
            string.IsNullOrWhiteSpace(
                OracionFinalNombre)
                ? "— asignar —"
                : OracionFinalNombre;
    }
}
