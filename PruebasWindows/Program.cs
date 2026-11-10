using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using SCCosmos;
using SCCosmos.Sincro;

// Reglas de la app de Windows: código real sobre una base temporal con datos
// inventados. Nunca toca la base de la app ni arranca la sincronización.
internal static class Programa
{
    private static string _cadena = "";
    private static int _total, _fallos;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string carpeta = Path.Combine(Path.GetTempPath(), "PruebasWindows_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        string ruta = Path.Combine(carpeta, "pruebas.db");
        try
        {
            DatabaseHelper.RutaParaPruebas = ruta;
            _cadena = $"Data Source={ruta};Pooling=False";
            if (!string.Equals(DatabaseHelper.RutaBaseDatos, ruta, StringComparison.OrdinalIgnoreCase)) return 3;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SCCosmos;component/Recursos/Tema.xaml") });
            DatabaseHelper.InicializarDatabase();
            new SincroAlmacen(ruta, "pruebas").Asegurar();
            Visitas();
            Importar();
            Ventana();
            Formulario();
            Fusion();
            GuardarFicha();
            ActividadInicio();
            RegistroS13Excel();
            TiempoHorasPasadas();
            TiempoTercerDia();
            Ver("sin motor de sincronización", SincronizacionRed.Config is null, true);
            string log = Path.Combine(carpeta, "errores.log");
            Ver("sin errores anotados", File.Exists(log) ? File.ReadLines(log).First() : "", "");
        }
        catch (Exception ex) { Console.WriteLine($"[FALLO] {ex}"); _fallos++; }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(carpeta, true); } catch (IOException) { } }
        Console.WriteLine($"{_total - _fallos}/{_total} comprobaciones superadas.");
        return _fallos == 0 ? 0 : 1;
    }

    private static void Visitas()
    {
        long uno = Ficha("A1", "Uno Inventado Prueba");
        Guardar("A1", "2026-10", Tarjeta("Uno Inventado Prueba"));
        Exec($"UPDATE Personas SET Servicio = 'Precursor regular' WHERE Id = {uno}");
        Ver("la última visita muestra la ficha", Fila("A1", "2026-10", uno).Servicio, "Precursor regular");
        Guardar("A1", "2026-12", Tarjeta("Uno Inventado Prueba"));
        Exec($"UPDATE Personas SET Servicio = 'Misionero', FechaEliminacion = '15/11/2026' WHERE Id = {uno}");
        Ver("la anterior queda como se veía", Fila("A1", "2026-10", uno).Servicio, "Precursor regular");
        Ver("la nueva última, la ficha", Fila("A1", "2026-12", uno).Servicio, "Misionero");
        Ver("en su visita no consta la baja", Fila("A1", "2026-10", uno).FechaEliminacion ?? "", "");
        Ver("arrastrada a un mes posterior, sí", Fila("A1", "2026-11", uno).FechaEliminacion, "15/11/2026");
        long dos = Ficha("A2", "Dos Inventado Prueba", servicio: "Precursor regular");
        Guardar("A2", "2026-10", Tarjeta("Dos Inventado Prueba"));
        Exec($"UPDATE Personas SET CongregacionCodigo = 'A3', Servicio = 'Misionero' WHERE Id = {dos}");
        Ver("trasladada: sigue en su visita con lo que tenía", Fila("A2", "2026-10", dos).Servicio, "Precursor regular");
        Ver("pero no en los meses siguientes", DatabaseHelper.ObtenerPersonasHistoricasPorMes("A2", "2026-11").Count, 0);
    }

    private static void Importar()
    {
        long ana = Ficha("B1", "Ana Inventada Uno", hub: "Ana Inventada Uno", servicio: "Precursor regular", nacimiento: "01/01/1970");
        long zoilo = Ficha("B1", "Zoilo Ñáñez Inventado", hub: "Zoilo Ñáñez Inventado");
        long padre = Ficha("B1", "Ciro Inventado Mora", hub: "Ciro Inventado Mora", nacimiento: "01/01/1965");
        long eloy = Ficha("B1", "Eloy Inventado Ruiz", hub: "Eloy Inventado Ruiz", nombramiento: "Anciano");
        var sinTocar = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ruiz Inventado, Eloy" };
        var tarjetas = new List<PersonaJson> { Tarjeta("Ana Inventada Uno", nacimiento: "13/09/1969", horas: "30"), Tarjeta("Nañez Inventado, Zoilo", horas: "12"), Tarjeta("Mora Inventado, Ciro", nacimiento: "13/09/1999"), Tarjeta("Ruiz Inventado, Eloy", nombramiento: "Siervo ministerial", horas: "9") };
        DatabaseHelper.GuardarVersionHistoricaJSON("B1", "2026-10", tarjetas, null, sinTocar);
        Ver("la tarjeta vacía no borra el servicio", Campo(ana, "Servicio"), "Precursor regular");
        Ver("la fecha exacta sustituye a la aproximada", Campo(ana, "FechaNacimiento"), "13/09/1969");
        Ver("otro orden y sin acentos: la misma ficha", Texto($"SELECT Horas FROM ActividadPersonas WHERE PersonaId = {zoilo}"), "12");
        Ver("padre e hijo no se unen", Campo(padre, "FechaNacimiento"), "01/01/1965");
        Ver("el hijo tiene ficha e instantánea", Texto("SELECT COUNT(*) FROM Personas s JOIN Personas r ON r.Id = s.PersonaRaizId WHERE r.Nombre = 'Mora Inventado, Ciro' AND s.MesHistorico = '2026-10'"), "1");
        Ver("lo decidido no cambia", Campo(eloy, "Nombramiento"), "Anciano");
        Ver("pero guarda su actividad", Texto($"SELECT Horas FROM ActividadPersonas WHERE PersonaId = {eloy}"), "9");
        string marca = Marca();
        DatabaseHelper.GuardarVersionHistoricaJSON("B1", "2026-10", tarjetas, null, sinTocar);
        Ver("repetir no reescribe la actividad", Cambios(marca), "0");
    }

    private static void Ventana()
    {
        Ficha("V1", "Hub Inventado Uno", hub: "Hub Inventado Uno");
        Ver("con cuenta de Hub, un servicio nuevo se pregunta", Conflictos("V1", Tarjeta("Hub Inventado Uno", servicio: "Precursor regular")), "1");
        Ficha("V2", "Sin Cuenta Inventada");
        Ver("sin cuenta, se rellena", Conflictos("V2", Tarjeta("Sin Cuenta Inventada", servicio: "Precursor regular")), "0");
        Ficha("V3", "Fecha Inventada Uno", hub: "Fecha Inventada Uno", nacimiento: "01/01/1970");
        Ver("la fecha aproximada que encaja no es conflicto", Conflictos("V3", Tarjeta("Fecha Inventada Uno", nacimiento: "13/09/1969")), "0");
        Ficha("V4", "Fecha Inventada Dos", hub: "Fecha Inventada Dos", nacimiento: "13/09/1969");
        Ver("dos fechas exactas distintas, sí", Conflictos("V4", Tarjeta("Fecha Inventada Dos", nacimiento: "14/09/1969")), "1");
    }

    private static void Formulario()
    {
        string Elegido(FormularioPersonaWindow f, string combo) => ((f.FindName(combo) as ComboBox)?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var con = new FormularioPersonaWindow(new Persona { Id = (int)Ficha("F1", "Eva Inventada Uno", hub: "Eva Inventada Uno", nombramiento: "Anciano"), Nombre = "Eva Inventada Uno", NombreJWHub = "Eva Inventada Uno", Nombramiento = "Anciano", CongregacionCodigo = "F1" }, new List<Persona>());
        con.CargarConflictosDesdeJson(new Persona { Nombre = "Eva Inventada Uno", Servicio = "Precursor regular", Nombramiento = "" });
        Ver("con cuenta, propone dejar la ficha", Elegido(con, "CmbServicio"), "(Vacío)");
        Ver("la tarjeta vacía no vacía el campo", Elegido(con, "CmbNombramiento"), "Anciano");
        var sin = new FormularioPersonaWindow(new Persona { Id = (int)Ficha("F1", "Eva Inventada Dos"), Nombre = "Eva Inventada Dos", CongregacionCodigo = "F1" }, new List<Persona>());
        sin.CargarConflictosDesdeJson(new Persona { Nombre = "Eva Inventada Dos", Servicio = "Precursor regular" });
        Ver("sin cuenta, propone la tarjeta", Elegido(sin, "CmbServicio"), "Precursor regular");
    }

    private static void Fusion()
    {
        long destino = Ficha("D1", "Destino Inventado Uno", hub: "Destino Inventado Uno");
        long origen = Ficha("D1", "Origen Inventado Uno", origen: "S21");
        long conyuge = Ficha("D1", "Conyuge Inventada Uno");
        Exec($"UPDATE Personas SET ConyugeId = {origen} WHERE Id = {conyuge}");
        foreach (var (p, mes, h) in new[] { (destino, "septiembre", "10"), (origen, "septiembre", "99"), (origen, "octubre", "20") })
            Exec($"INSERT INTO ActividadPersonas (PersonaId, Mes, AnioServicio, Participacion, CursosBiblicos, PrecursorAuxiliar, Horas, Notas) VALUES ({p}, '{mes}', '2026', 1, 0, 0, '{h}', '')");
        foreach (var (p, mes, s) in new[] { (origen, "2026-10", "Precursor regular"), (origen, "2026-11", "Misionero"), (destino, "2026-11", "Anciano") })
            Exec($"INSERT INTO Personas (Nombre, CongregacionCodigo, Servicio, PersonaRaizId, MesHistorico) SELECT Nombre, 'D1', '{s}', {p}, '{mes}' FROM Personas WHERE Id = {p}");
        DatabaseHelper.FusionarYBorrarDefinitivo(DatabaseHelper.ObtenerPersonaPorId((int)destino), (int)origen);
        Ver("el mes repetido se queda el de la que queda", Texto($"SELECT Horas FROM ActividadPersonas WHERE PersonaId = {destino} AND Mes = 'septiembre'"), "10");
        Ver("el que no tenía pasa a ella", Texto($"SELECT Horas FROM ActividadPersonas WHERE PersonaId = {destino} AND Mes = 'octubre'"), "20");
        Ver("la instantánea que no tenía pasa a ella", Texto($"SELECT Servicio FROM Personas WHERE PersonaRaizId = {destino} AND MesHistorico = '2026-10'"), "Precursor regular");
        Ver("la que tenía no cambia", Texto($"SELECT group_concat(Servicio) FROM Personas WHERE PersonaRaizId = {destino} AND MesHistorico = '2026-11'"), "Anciano");
        Ver("no queda nada de la otra", Texto($"SELECT COUNT(*) FROM Personas WHERE Id = {origen} OR PersonaRaizId = {origen}"), "0");
        Ver("el cónyuge apunta a la que queda", Campo(conyuge, "ConyugeId"), destino.ToString(CultureInfo.InvariantCulture));
    }

    private static void GuardarFicha()
    {
        long p = Ficha("G1", "Gala Inventada Uno");
        Exec($"INSERT INTO ActividadPersonas (PersonaId, Mes, AnioServicio, Participacion, CursosBiblicos, PrecursorAuxiliar, Horas, Notas, Cursos) VALUES ({p}, 'septiembre', '2026', 1, 0, 0, '', NULL, ''), ({p}, 'octubre', '2026', 1, 2, 1, '12', 'una nota', '2'), ({p}, 'noviembre', '2026', 0, 0, 0, NULL, '', NULL)");
        string marca = Marca();
        List<ActividadMensual> leida = DatabaseHelper.ObtenerActividadPersona((int)p);
        DatabaseHelper.ReemplazarActividadPersona((int)p, leida);
        Ver("guardar sin tocar nada no cambia ninguna fila", Cambios(marca), "0");
        marca = Marca();
        leida.First(m => m.Mes == "octubre").Horas = "33";
        DatabaseHelper.ReemplazarActividadPersona((int)p, leida);
        Ver("tocar un mes cambia solo ese", Cambios(marca), "1");
        leida.First(m => m.Mes == "octubre").CursosBiblicos = "5";
        DatabaseHelper.ReemplazarActividadPersona((int)p, leida);
        Ver("los cursos se guardan", Texto($"SELECT Cursos || '|' || CursosBiblicos FROM ActividadPersonas WHERE PersonaId = {p} AND Mes = 'octubre'"), "5|5");
    }

    // Inicio: cursos bíblicos y media de auxiliares con la regla común de Windows y
    // Android. Las filas preparadas de los meses sin informar y el mes que acaba de
    // empezar a llegar no cuentan.
    private static void ActividadInicio()
    {
        var filas = new List<ResumenActividadInicio.Fila>();
        DateTime agosto = new(2026, 8, 1), septiembre = new(2026, 9, 1), octubre = new(2026, 10, 1);
        for (int p = 1; p <= 10; p++)
        {
            filas.Add(new(p, agosto, true, 2, p == 1));
            filas.Add(new(p, septiembre, p <= 8, p <= 8 ? 1 : 0, p <= 3));
            filas.Add(new(p, octubre, p == 1, p == 1 ? 5 : 0, false));
            for (DateTime mes = new(2026, 11, 1); mes <= new DateTime(2027, 8, 1); mes = mes.AddMonths(1))
                filas.Add(new(p, mes, false, 0, p <= 2));
        }
        var r = ResumenActividadInicio.Calcular(filas, new DateTime(2026, 11, 8));
        Ver("actividad: mes de los cursos", r.MesReferencia, (DateTime?)septiembre);
        Ver("actividad: cursos del mes", r.CursosBiblicos, 8);
        Ver("actividad: media de auxiliares", r.MediaAuxiliares, 2.0);
        Ver("actividad: meses de la media", r.MesesMediaAuxiliares, 2);
        Ver("actividad: mes del año de servicio", ResumenActividadInicio.MesCalendario("Septiembre", "2027"), (DateTime?)septiembre);
    }

    // S-13 en Excel con el formulario oficial: dos hojas (años de servicio), lo
    // mismo en las dos, una asignación que pasa de una hoja a otra, otra devuelta
    // sin completar y columnas propias a la derecha que no cuentan.
    private static void RegistroS13Excel()
    {
        static string T(string r, string texto) => $"<c r=\"{r}\" t=\"inlineStr\"><is><t>{texto}</t></is></c>";
        static string F(string r, int a, int m, int d) => $"<c r=\"{r}\"><v>{new DateTime(a, m, d).ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>";
        static string Fila(int n, params string[] celdas) => $"<row r=\"{n}\">{string.Concat(celdas)}</row>";
        static string Hoja(int anio, params string[] filas) =>
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
            + Fila(4, T("B4", "Año de servicio:"), $"<c r=\"C4\"><v>{anio}</v></c>")
            + Fila(6, T("B6", "Núm. de terr."), T("C6", "Última fecha en que se completó"), T("D6", "Asignado a"), T("F6", "Asignado a"), T("H6", "Meses totales"))
            + Fila(7, T("D7", "Fecha en que se asignó"), T("E7", "Fecha en que se completó"), T("F7", "Fecha en que se asignó"), T("G7", "Fecha en que se completó"), T("H7", "Asignado a"))
            + string.Concat(filas) + "</sheetData></worksheet>";

        string ruta = Path.Combine(Path.GetTempPath(), $"s13_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Create))
            {
                void Entrada(string nombre, string xml) { using var w = new StreamWriter(zip.CreateEntry(nombre).Open()); w.Write(xml); }
                Entrada("xl/worksheets/sheet1.xml", Hoja(2026,
                    Fila(8, T("B8", "T 1"), F("C8", 2025, 11, 1), T("D8", "Ana Ruiz"), T("F8", "Luis Pérez"), T("H8", "Luis Pérez")),
                    Fila(9, F("D9", 2025, 10, 1), F("E9", 2025, 11, 1), F("F9", 2026, 1, 10), "<c r=\"H9\"><v>3</v></c>"),
                    Fila(10, T("B10", "T 2"), T("D10", "Eva Gil")),
                    Fila(11, F("D11", 2025, 9, 5), F("E11", 2026, 2, 1)),
                    Fila(12, T("B12", "T 3"), F("C12", 2024, 5, 1))));
                Entrada("xl/worksheets/sheet2.xml", Hoja(2025,
                    Fila(8, T("B8", "T 1"), T("D8", "Ana Ruiz")),
                    Fila(9, F("D9", 2025, 10, 1), F("E9", 2025, 11, 1)),
                    Fila(10, T("B10", "T 2"), T("D10", "Pablo Sanz"), T("F10", "Eva Gil")),
                    Fila(11, F("D11", 2025, 3, 1), F("F11", 2025, 8, 1))));
            }

            var lista = ConvertidorS13.ConvertirArchivo(ruta);
            string Vueltas(string nombre) => string.Join(" / ", lista.Where(t => t.Nombre == nombre).Select(t => $"{t.AsignadoA} {t.FechaAsignado}-{t.FechaCompletado}"));

            Ver("S-13 Excel: filas (sin las columnas propias)", lista.Count, 4);
            Ver("S-13 Excel: lo de las dos hojas, una vez", Vueltas("T 1"), "Ana Ruiz 01/10/2025-01/11/2025 / Luis Pérez 10/01/2026-");
            Ver("S-13 Excel: pasa de una hoja a otra", Vueltas("T 2"), "Eva Gil 01/08/2025-01/02/2026");
            Ver("S-13 Excel: territorio sin asignaciones", lista.Where(t => t.Nombre == "T 3").Select(t => t.AsignadoA + "|" + t.UltimaFechaCompletado).FirstOrDefault(), "|01/05/2024");
        }
        finally { try { File.Delete(ruta); } catch (IOException) { } }
    }

    // AEMET quita de hoy los tramos pasados: se conservan los de la previsión anterior.
    private static void TiempoHorasPasadas()
    {
        static string Dia(string fecha, int desde, int hasta, string cielo) =>
            $"<dia fecha=\"{fecha}\">"
            + string.Concat(Enumerable.Range(desde, hasta - desde + 1).Select(h => $"<estado_cielo periodo=\"{h:00}\" descripcion=\"\">{cielo}</estado_cielo><temperatura periodo=\"{h:00}\">{h}</temperatura>"))
            + "</dia>";
        static XDocument Prevision(params string[] dias) => XDocument.Parse("<root><prediccion>" + string.Concat(dias) + "</prediccion></root>");

        // La de anoche traía hoy entero; la de esta tarde, hoy desde las 07h.
        var anterior = Prevision(Dia("2026-11-10", 0, 23, "11"), Dia("2026-11-11", 0, 18, "11"));
        var nueva = Prevision(Dia("2026-11-10", 7, 23, "15"), Dia("2026-11-11", 0, 23, "15"), Dia("2026-11-12", 0, 6, "15"));
        XElement DiaDe(string fecha) => nueva.Descendants("dia").First(d => (string?)d.Attribute("fecha") == fecha);
        string Periodos(string fecha, string dato) => string.Join(",", DiaDe(fecha).Elements(dato).Select(e => (string?)e.Attribute("periodo")).Order());
        string Cielo(string fecha, string hora) => DiaDe(fecha).Elements("estado_cielo").Single(e => (string?)e.Attribute("periodo") == hora).Value;

        Ver("Tiempo: añade lo que AEMET ha quitado", TiempoAemet.ConservarHorasPasadas(nueva, anterior), true);
        Ver("Tiempo: hoy, desde las 00h", Periodos("2026-11-10", "estado_cielo"), string.Join(",", Enumerable.Range(0, 24).Select(h => h.ToString("00"))));
        Ver("Tiempo: hoy, temperaturas desde las 00h", Periodos("2026-11-10", "temperatura").Split(',').Length, 24);
        Ver("Tiempo: lo nuevo manda", Cielo("2026-11-10", "06") + "/" + Cielo("2026-11-10", "07") + "/" + Cielo("2026-11-11", "05"), "11/15/15");
        Ver("Tiempo: otra descarga igual no añade nada", TiempoAemet.ConservarHorasPasadas(nueva, anterior), false);
    }

    // El tercer día por horas solo llega a las 06h: el resto, con los tramos del diario.
    // Sin red: una caché aparte con archivos inventados.
    private static void TiempoTercerDia()
    {
        string anterior = TiempoAemet.CarpetaCache;
        string carpeta = Path.Combine(Path.GetTempPath(), "PruebasTiempo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            TiempoAemet.CarpetaCache = carpeta;
            string horas = string.Concat(Enumerable.Range(0, 7).Select(h => $"<estado_cielo periodo=\"{h:00}\" descripcion=\"Despejado\">11n</estado_cielo><temperatura periodo=\"{h:00}\">{h}</temperatura>"));
            File.WriteAllText(Path.Combine(carpeta, "aemet_h_99999.xml"), $"<root><prediccion><dia fecha=\"2026-11-12\">{horas}</dia></prediccion></root>", System.Text.Encoding.Latin1);
            File.WriteAllText(Path.Combine(carpeta, "aemet_d_99999.xml"),
                "<root><prediccion><dia fecha=\"2026-11-12\">"
                + "<prob_precipitacion periodo=\"00-24\">40</prob_precipitacion><prob_precipitacion periodo=\"00-12\">10</prob_precipitacion><prob_precipitacion periodo=\"12-24\">40</prob_precipitacion>"
                + "<estado_cielo periodo=\"00-24\" descripcion=\"Nuboso\">14</estado_cielo><estado_cielo periodo=\"00-12\" descripcion=\"Poco nuboso\">12</estado_cielo><estado_cielo periodo=\"12-24\" descripcion=\"Nuboso con lluvia\">25</estado_cielo>"
                + "<temperatura><maxima>18</maxima><minima>6</minima></temperatura></dia></prediccion></root>", System.Text.Encoding.Latin1);

            TiempoDia tiempo = Task.Run(() => TiempoAemet.ObtenerAsync(new LugarTiempo("99999", "Prueba", "prueba", 0, 0), new DateTime(2026, 11, 12), true, default)).GetAwaiter().GetResult();

            Ver("Tiempo: tercer día, horas y luego tramos", string.Join(" ", tiempo.Horas.Select(h => h.Etiqueta)), "00h 01h 02h 03h 04h 05h 06h 7–12 h 12–24 h");
            Ver("Tiempo: el tramo empezado conserva sus datos", tiempo.Horas[7].Cielo + "/" + tiempo.Horas[7].ProbLluvia + "/" + tiempo.Horas[8].Cielo + "/" + tiempo.Horas[8].ProbLluvia, "12/10/25/40");
            Ver("Tiempo: el tramo empezado va de 07 a 12", $"{tiempo.Horas[7].Desde:HH}-{tiempo.Horas[7].Hasta:HH}", "07-12");
        }
        finally
        {
            TiempoAemet.CarpetaCache = anterior;
            try { Directory.Delete(carpeta, true); } catch (IOException) { }
        }
    }

    private static void Ver(string que, object obtenido, object esperado)
    {
        _total++;
        if (!Equals(obtenido, esperado)) { _fallos++; Console.WriteLine($"[FALLO] {que}: {obtenido} (se esperaba {esperado})"); }
    }

    private static void Exec(string sql) { using var c = new SqliteConnection(_cadena); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private static string Texto(string sql) { using var c = new SqliteConnection(_cadena); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? ""; }

    private static long Ficha(string congre, string nombre, string hub = "", string servicio = "", string nombramiento = "", string nacimiento = "", string origen = "JWHub")
    {
        Exec($"INSERT INTO Personas (Nombre, NombreJWHub, NombreComun, CongregacionCodigo, Servicio, Nombramiento, FechaNacimiento, OrigenRegistro) VALUES ('{nombre}', '{hub}', '{nombre}', '{congre}', '{servicio}', '{nombramiento}', '{nacimiento}', '{origen}')");
        return long.Parse(Texto("SELECT MAX(Id) FROM Personas"), CultureInfo.InvariantCulture);
    }

    private static PersonaJson Tarjeta(string nombre, string servicio = "", string nombramiento = "", string nacimiento = "", string horas = "") =>
        JsonSerializer.Deserialize<PersonaJson>(JsonSerializer.Serialize(new PersonaJson { Nombre = nombre, Servicio = servicio, Nombramiento = nombramiento, FechaNacimiento = nacimiento, FechaBautismo = "", Sexo = "", Esperanza = "", AnioServicio = "2026", Informe = new List<InformeJson> { new InformeJson { Mes = "septiembre", Participacion = true, CursosBiblicos = "", Horas = horas, Notas = "" } } }));

    private static void Guardar(string congre, string mes, params PersonaJson[] tarjetas) => DatabaseHelper.GuardarVersionHistoricaJSON(congre, mes, tarjetas.ToList());

    private static Persona Fila(string congre, string mes, long raiz) => DatabaseHelper.ObtenerPersonasHistoricasPorMes(congre, mes).First(p => p.Id == raiz || p.PersonaRaizId == raiz);

    private static string Campo(long id, string columna) => Texto($"SELECT {columna} FROM Personas WHERE Id = {id}");

    private static string Marca() => Texto("SELECT COALESCE(MAX(Seq), 0) FROM SCCosmos_Cambios");

    private static string Cambios(string desde) => Texto($"SELECT COUNT(*) FROM SCCosmos_Cambios WHERE Seq > {desde} AND Tabla = 'ActividadPersonas'");

    private static string Conflictos(string congre, params PersonaJson[] tarjetas) => ((TextBlock)new SyncJsonWindow(congre, tarjetas.ToList(), null, "2026-10").FindName("TxtNumConflictos")).Text;
}