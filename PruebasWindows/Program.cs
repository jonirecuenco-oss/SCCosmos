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
            PueblosDelTerritorio();
            RequisitoDeHoras();
            AjustesDeAvisos();
            ProgramaWebRecibido();
            Ver("Tailscale: qué hacer según su estado (conectado, desconectado, sin sesión, sin servicio)",
                string.Join("|", new[] { "{\"BackendState\":\"Running\"}", "{\"BackendState\":\"Stopped\"}", "{\"BackendState\":\"NeedsLogin\"}", "failed to connect to local tailscaled" }
                    .Select(s => TailscalePc.Interpretar(s).Accion)),
                "Nada|Conectar|Avisar|Avisar");
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

    // Los pueblos del territorio: su formato en Configuracion, las sugerencias por
    // las direcciones (de la provincia del salón y sin el suyo) y la búsqueda.
    private static void PueblosDelTerritorio()
    {
        string guardado = "10180|39.9876|-5.6012;10122|39.85|-5.58;10180|1|1";
        Ver("Pueblos: se leen sin repetir", string.Join(",", PueblosTerritorio.Leer(guardado).Select(p => p.Nombre)), "Talayuela,Millanes");
        Ver("Pueblos: se escriben igual", PueblosTerritorio.Escribir(PueblosTerritorio.Leer(guardado)), "10180|39.9876|-5.6012;10122|39.85|-5.58");
        Ver("Pueblos: clave en Configuracion", PueblosTerritorio.Clave(" 3004 "), "Tiempo_Pueblos_3004");

        string[] direcciones =
        {
            "Calle Mayor 1, 10300 Navalmoral de la Mata",
            "Calle Real 2, 10310 TALAYUELA",
            "C/ Luna 3, 10310 - Talayuela (Cáceres)",
            "Avenida 4, 10320 Millanes",
            "Calle Sol 5, 45600 Talavera de la Reina",
            "Sin código postal"
        };
        Ver("Pueblos: sugeridos por las direcciones", string.Join(", ", PueblosTerritorio.Sugerencias(direcciones, "10131").Select(s => $"{s.Nombre} ({s.Personas})")), "Talayuela (2), Millanes (1)");

        Ver("Pueblos: buscar, primero los de su provincia", TiempoAemet.BuscarMunicipios("talay", "10", 3).FirstOrDefault().Ine, "10180");
        Ver("Pueblos: provincia de un municipio", TiempoAemet.ProvinciaDe("10180"), "Cáceres");

        var ventana = new PueblosTerritorioWindow(new Congregacion { Codigo = "778", Nombre = "Almendralejo", Municipio = "Almendralejo", Provincia = "Badajoz" });
        Ver("Pueblos: la ventana dice el del salón", ((TextBlock)ventana.FindName("TxtSalon")).Text, "El del salón: Almendralejo.");
    }

    // El requisito de horas (50; 40 la esposa de 40 años o más) y si se va con atraso en el mes.
    private static void RequisitoDeHoras()
    {
        var nov = new DateTime(2026, 11, 1);
        Ver("Horas: requisito del SC", RequisitoHoras.Mensual(false, new DateTime(1970, 1, 1), nov), 50);
        Ver("Horas: esposa sin fecha", RequisitoHoras.Mensual(true, null, nov), 50);
        Ver("Horas: esposa de 36", RequisitoHoras.Mensual(true, new DateTime(1990, 1, 1), nov), 50);
        Ver("Horas: esposa que cumple 40 ese mes", RequisitoHoras.Mensual(true, new DateTime(1986, 11, 20), nov), 40);
        Ver("Horas: esposa que los cumple el mes siguiente", RequisitoHoras.Mensual(true, new DateTime(1986, 12, 1), nov), 50);

        // El 11 de noviembre hay 10 días terminados de 30: tocan 16,7 h.
        var hoy = new DateTime(2026, 11, 11);
        Ver("Horas: atraso", RequisitoHoras.Diferencia(15, 50, nov, hoy), ("−1,7", -1));
        Ver("Horas: adelanto", RequisitoHoras.Diferencia(20, 50, nov, hoy), ("+3,3", 1));
        Ver("Horas: el día 1, al día", RequisitoHoras.Diferencia(0, 50, nov, nov), ("±0", 0));
        Ver("Horas: mes pasado con atraso", RequisitoHoras.Diferencia(35, 50, new DateTime(2026, 10, 1), hoy), ("−15", -1));
        Ver("Horas: mes pasado con adelanto", RequisitoHoras.Diferencia(52.5, 50, new DateTime(2026, 10, 1), hoy), ("+2,5", 1));
        Ver("Horas: mes futuro, nada", RequisitoHoras.Diferencia(0, 50, new DateTime(2026, 12, 1), hoy).Texto, "");
        Ver("Horas: fecha de nacimiento, ida y vuelta", RequisitoHoras.EscribirNacimiento(RequisitoHoras.LeerNacimiento("1986-11-20")), "1986-11-20");

        // Tareas en el Calendario: los días de cada una dentro de un intervalo.
        DateTime lunes = new(2026, 11, 9), domingo = new(2026, 11, 15);
        string Dias(List<DateTime> fechas) => string.Join(",", fechas.Select(f => f.Day));
        Ver("Tareas: un día dentro", Dias(FechasTarea.EnIntervalo("2026-11-15", "", "Ninguna", lunes, domingo)), "15");
        Ver("Tareas: un día fuera", FechasTarea.EnIntervalo("2026-11-16", "", "Ninguna", lunes, domingo).Count, 0);
        Ver("Tareas: varios días, recortados", Dias(FechasTarea.EnIntervalo("2026-11-13", "2026-11-18", "Ninguna", lunes, domingo)), "13,14,15");
        Ver("Tareas: semanal que empezó antes", Dias(FechasTarea.EnIntervalo("2026-10-01", "", "Semanal", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30))), "5,12,19,26");
        Ver("Tareas: mensual del 31", Dias(FechasTarea.EnIntervalo("2026-01-31", "", "Mensual", new DateTime(2026, 2, 1), new DateTime(2026, 4, 30))), "28,31,30");
        Ver("Tareas: se repite hasta la fecha fin", Dias(FechasTarea.EnIntervalo("2026-11-10", "2026-11-12", "Diaria", lunes, domingo)), "10,11,12");
        Ver("Tareas: texto de la fila", FechasTarea.Texto(false, new[] { domingo }, "17:00", "Llamar a Pedro"), "☐ dom 15 · 17:00 · Llamar a Pedro");
        Ver("Tareas: varios días, hecha", FechasTarea.Texto(true, new[] { new DateTime(2026, 11, 16), new DateTime(2026, 11, 20) }, "", "Vacaciones"), "☑ lun 16 – vie 20 · Vacaciones");
        Ver("Tareas: puntos del día", FechasTarea.Puntos(5), "•••");

        // Crédito de los precursores: lo que computa de verdad (tope de 55 con las horas).
        Ver("Crédito: recortado por el tope", RequisitoPrecursor.TextoCreditoComputable(40, 20), "(15)");
        Ver("Crédito: entero", RequisitoPrecursor.TextoCreditoComputable(30, 10), "(10)");
        Ver("Crédito: con 55 horas no computa", RequisitoPrecursor.TextoCreditoComputable(60, 5), "(0)");
        Ver("Crédito: sin crédito, nada", RequisitoPrecursor.TextoCreditoComputable(20, 0), "");

        // Programa de la visita desde la web: lo enviado → los mismos eventos que el PDF.
        string envioWeb = """
        {"version":1,
         "reuniones":{"entreSemana":{"hora":"19:00","lugar":"Salón del Reino","lat":39.9,"lon":-5.5},
                      "finDeSemana":{"dia":"Domingo","hora":"11:00","lugar":""},
                      "ancianos":{"dia":"","hora":"20:00"},
                      "predicacionViernes":{"hora":"9.30","lugar":"Plaza"}},
         "comidas":[{"dia":"Miércoles","familia":"Familia García","telefono":"600","hora":"","direccion":"Calle Mayor 1"},{"dia":"Jueves","familia":""}],
         "alojamiento":{"familia":"Familia Ruiz","telefono":"","direccion":"Calle Sol 2"},
         "predicacion":[{"franja":"Miércoles mañana","hora":"10:00","lugar":"","sc":[{"nombre":"Juan Pérez","faceta":"Curso bíblico","leccion":12,"notas":"lleva el libro"},{"nombre":"Ana Ruiz","faceta":"Territorio"},{"nombre":"Tercero","faceta":"Territorio"}],"esposa":[]},
                        {"franja":"Jueves tarde","hora":"17:00","sc":[],"esposa":[]},
                        {"franja":"Viernes tarde","hora":"","sc":[],"esposa":[]}],
         "pastoreo":[{"nombre":"Familia López","dia":"Jueves","hora":"18:00","direccion":"Calle Luna 3","descripcion":"Ánimo"},{"nombre":"","dia":"Viernes"}],
         "asuntos":["Limpieza del salón",""," Grupos "]}
        """;
        var (eventosWeb, asuntosWeb) = ProgramaWeb.AEventos(envioWeb);
        Ver("Web: reuniones (la de A/SM, siempre el viernes; la del viernes para la predicación ya no)", string.Join("|", eventosWeb.Where(e => e.TipoActividad.StartsWith("Reunión") || (e.TipoActividad == "Predicación" && e.Turno == "General")).Select(e => $"{e.DiaSemana} {e.Hora} {e.TipoActividad}")), "Martes 19:00 Reunión entre semana|Viernes 20:00 Reunión de A/SM|Domingo 11:00 Reunión fin de semana");
        Ver("Web: total de actividades", eventosWeb.Count, 13);
        Ver("Web: comida (hora por defecto)", eventosWeb.Where(e => e.TipoActividad == "Comida").Select(e => $"{e.DiaSemana} {e.Hora} {e.Notas} @ {e.Ubicacion}").FirstOrDefault(), "Miércoles 13:30 Familia García · 600 @ Calle Mayor 1");
        Ver("Web: alojamiento, todos los días", eventosWeb.Count(e => e.TipoActividad == "Alojamiento"), 6);
        Ver("Web: predicación, 2 personas como mucho", eventosWeb.First(e => e.TipoActividad == "Predicación" && e.Responsable == "SC").Notas, "Juan Pérez (Curso bíblico, lección 12) — lleva el libro; Ana Ruiz (Territorio)");
        Ver("Web: salida con hora y sin nadie", eventosWeb.Count(e => e.TipoActividad == "Predicación" && e.Turno == "Tarde" && e.Responsable == ""), 1);
        Ver("Web: pastoreo", eventosWeb.Where(e => e.TipoActividad == "Visita de pastoreo").Select(e => $"{e.DiaSemana} {e.Hora} {e.Responsable} {e.Notas}").Single(), "Jueves 18:00 SC Familia López · Ánimo");
        Ver("Web: coordenadas", eventosWeb.First().Latitud, 39.9);
        Ver("Web: asuntos", string.Join("|", asuntosWeb), "Limpieza del salón|Grupos");
        Ver("Web: código del enlace", ProgramaWeb.CodigoValido(ProgramaWeb.NuevoCodigo()) && !ProgramaWeb.CodigoValido("../../etc/passwd"), true);
        string ochoFranjas = "{\"version\":1,\"predicacion\":[" + string.Join(",", new[] { "Miércoles mañana", "Miércoles tarde", "Jueves mañana", "Jueves tarde", "Viernes mañana", "Viernes tarde", "Sábado mañana", "Domingo mañana", "Domingo mañana" }.Select(f => "{\"franja\":\"" + f + "\",\"hora\":\"10:00\"}")) + "],\"pastoreo\":[{\"nombre\":\"Familia Gil\",\"dia\":\"Martes\",\"hora\":\"18:00\"}]}";
        Ver("Web: las 8 franjas sin repetir; pastoreo, no en martes", string.Join("|", ProgramaWeb.AEventos(ochoFranjas).Eventos.Select(e => e.DiaSemana[..2] + e.Turno[0])), "MiM|MiT|JuM|JuT|ViM|ViT|SáM|DoM");
        Ver("Web: medio día libre (sin elegir, el viernes por la mañana)", ProgramaWeb.MedioDiaLibre("") + "|" + ProgramaWeb.MedioDiaLibre("jueves tarde") + "|" + ProgramaWeb.MedioDiaLibre("Sábado mañana"), "Viernes mañana|Jueves tarde|Viernes mañana");
        Ver("Web: precursores, solo el miércoles o el sábado", string.Join("|", new[] { "Miércoles", "Jueves", "Sábado" }.Select(d => ProgramaWeb.AEventos("{\"version\":1,\"reuniones\":{\"precursores\":{\"dia\":\"" + d + "\",\"hora\":\"10:00\"}}}").Eventos.Count)), "1|0|1");
        Ver("Web: comidas, solo los días con la congregación", string.Join("|", ProgramaWeb.AEventos("{\"version\":1,\"comidas\":[{\"dia\":\"Jueves\",\"familia\":\"A\"},{\"dia\":\"Sábado\",\"familia\":\"B\"}]}", "", new[] { "Sábado" }).Eventos.Select(e => e.DiaSemana)), "Sábado");
        Ver("Archivos: nombres seguros", string.Join("|", ArchivosProgramaWeb.NombreSeguro("CON.pdf"), ArchivosProgramaWeb.NombreSeguro("a<b>:c.PDF"), ArchivosProgramaWeb.NombreSeguro("virus.exe") ?? "no", ArchivosProgramaWeb.NombreSeguro("C:\\x\\..\\S-21 G1.zip")), "archivo.pdf|a_b__c.pdf|no|S-21 G1.zip");
        Ver("Archivos: carpetas por tipo", string.Join("|", ArchivosProgramaWeb.Carpeta("s21-g3"), ArchivosProgramaWeb.Carpeta("s21-g03") ?? "no", ArchivosProgramaWeb.Carpeta("../x") ?? "no", ArchivosProgramaWeb.Carpeta("s88")), System.IO.Path.Combine("S-21", "G3") + "|no|no|S-88");
        Ver("Archivos: la S-61 es un tipo más", ArchivosProgramaWeb.Carpeta("s61") + " | " + ArchivosProgramaWeb.Resumen(new (string, string, long)[] { ("s21-g1", "a.pdf", 1), ("s21-g2", "b.pdf", 1), ("s61", "S-61.pdf", 1) }), "S-61 | S-21: 2 (2 grupos) · S-61: 1");
        Ver("Enlace: dirección, código y versión para el correo", ProgramaWeb.Enlace("https://pc.ts.net/", "abc") + " " + ProgramaWeb.EnlaceHtml("https://pc.ts.net/programa/abc"), "https://pc.ts.net/programa/abc <a href=\"https://pc.ts.net/programa/abc\">https://pc.ts.net/programa/abc</a>");
        Ver("Web: días de comida guardados", string.Join("|", ProgramaWeb.DiasComida("").Length, ProgramaWeb.DiasComida("Ninguno").Length, ProgramaWeb.TextoDiasComida(ProgramaWeb.DiasComida("sabado, Jueves"))), "5|0|Jueves,Sábado");
        Ver("Web: en el medio día libre no va nadie con ellos", string.Join("|", ProgramaWeb.AEventos(envioWeb, "Miércoles mañana").Eventos.Where(e => e.DiaSemana == "Miércoles" && e.TipoActividad == "Predicación").Select(e => $"{e.Hora} [{e.Responsable}] {e.Notas}")), "10:00 [] Medio día libre");
        Ver("Web: hora y día de la congregación", ProgramaWeb.Hora("19:30h") + " " + ProgramaWeb.Dia("sabado"), "19:30 Sábado");
        Ver("Comidas: resumen para la tabla de Circuito",
            string.Join("|", new[] { "", "Ninguno", "sabado,Jueves" }.Select(g => ProgramaWeb.ResumenDiasComida(ProgramaWeb.DiasComida(g)))),
            "Todos|Ninguno|Jue, Sáb");
        Ver("Comidas: las de la congregación o, sin elegir, las de Configuración",
            string.Join("|",
                string.Join(",", ProgramaWeb.DiasComidaDe("C1", c => c == ProgramaWeb.ClaveDiasComida ? "Jueves" : "")),
                string.Join(",", ProgramaWeb.DiasComidaDe("C1", c => c == ProgramaWeb.ClaveDiasComida ? "Jueves" : c == ProgramaWeb.ClaveDiasComidaCongregacion("C1") ? "Viernes,Domingo" : "")),
                ProgramaWeb.DiasComidaDe("C1", c => c == ProgramaWeb.ClaveDiasComida ? "Jueves" : "Ninguno").Length),
            "Jueves|Viernes,Domingo|0");

        // Aviso en el móvil al pulsar «Enviar»: cuándo y con cuántos archivos; el texto de la notificación.
        Ver("Enviado: ida y vuelta (y lo que no se entiende)",
            $"{ProgramaWeb.LeerEnviado(ProgramaWeb.TextoEnviado(new DateTime(2026, 11, 18, 10, 30, 5), 5))} {ProgramaWeb.LeerEnviado("2026-11-18T10:30:05")?.Archivos} {ProgramaWeb.LeerEnviado("") is null} {ProgramaWeb.LeerEnviado("ayer|3") is null}",
            $"{(new DateTime(2026, 11, 18, 10, 30, 5), 5)} 0 True True");
        Ver("Enviado: la notificación",
            string.Join(" / ",
                ProgramaWeb.AvisoEnviado("Navalmoral", "2026-12", 5, false).Titulo,
                ProgramaWeb.AvisoEnviado("Navalmoral", "2026-12", 5, false).Texto,
                ProgramaWeb.AvisoEnviado("Navalmoral", "2027-01", 1, true).Texto,
                ProgramaWeb.AvisoEnviado("Navalmoral", "2027-01", 0, false).Texto),
            "Programa recibido: Navalmoral / Los ancianos han enviado el programa de la visita de diciembre de 2026, con 5 archivos."
            + " / Los ancianos han vuelto a enviar el programa de la visita de enero de 2027, con 1 archivo."
            + " / Los ancianos han enviado el programa de la visita de enero de 2027.");

        // Enlaces enviados con la dirección de un servidor anterior: a la página del servidor elegido.
        Ver("Redirección: solo lo que llega por otra dirección, nunca en el propio servidor ni sin servidor elegido",
            string.Join("|",
                ProgramaWeb.DestinoRedireccion("pc-viejo.tail.ts.net", "https://pc-nuevo.tail.ts.net", "https://pc-viejo.tail.ts.net") ?? "aquí",
                ProgramaWeb.DestinoRedireccion("PC-NUEVO.tail.ts.net:443", "https://pc-nuevo.tail.ts.net", "") ?? "aquí",
                ProgramaWeb.DestinoRedireccion("localhost:47320", "https://pc-nuevo.tail.ts.net", "") ?? "aquí",
                ProgramaWeb.DestinoRedireccion("127.0.0.1:47320", "https://pc-nuevo.tail.ts.net", "") ?? "aquí",
                ProgramaWeb.DestinoRedireccion("pc-viejo.tail.ts.net", "", "") ?? "aquí",
                ProgramaWeb.DestinoRedireccion("otro.dominio.es", "https://pc-nuevo.tail.ts.net", "https://pc-nuevo.tail.ts.net") ?? "aquí",
                ProgramaWeb.DestinoRedireccion("", "https://pc-nuevo.tail.ts.net", "") ?? "aquí"),
            "https://pc-nuevo.tail.ts.net|aquí|aquí|aquí|aquí|aquí|aquí");

        // El servidor del formulario: solo /programa/{código}, y solo con un código que exista.
        string codigoPrueba = ProgramaWeb.NuevoCodigo();
        string recibidoPrueba = "";
        string carpetaPrueba = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sccosmos_archivos_" + Guid.NewGuid().ToString("N"));
        using (var servidor = new ServidorProgramaWeb(
                   47399,
                   c => c == codigoPrueba ? "{\"codigo\":\"x\"}" : null,
                   (c, json) => { recibidoPrueba = json; return true; },
                   "<html><script>window.VISITA = /*VISITA*/null;</script></html>",
                   c => c == codigoPrueba ? carpetaPrueba : null))
        {
            servidor.Redireccion = host => ProgramaWeb.DestinoRedireccion(host, "https://pc-nuevo.tail.ts.net", "");
            servidor.Estado = c => c == codigoPrueba ? "{\"enviado\":\"2026-11-18T10:30:00|2\"}" : null;
            servidor.Iniciar();
            using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://127.0.0.1:47399") };

            using var sinSeguir = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri("http://127.0.0.1:47399") };
            using var antigua = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "/programa/" + codigoPrueba);
            antigua.Headers.Host = "pc-viejo.tail.ts.net";
            var redirigida = sinSeguir.SendAsync(antigua).Result;
            Ver("Servidor: enlace con la dirección antigua, al servidor elegido",
                $"{(int)redirigida.StatusCode} {redirigida.Headers.Location} {redirigida.Headers.CacheControl?.NoStore}",
                $"302 https://pc-nuevo.tail.ts.net/programa/{codigoPrueba} True");
            var pagina = http.GetAsync("/programa/" + codigoPrueba).Result;
            Ver("Servidor: la página, con los datos de la visita", (int)pagina.StatusCode + " " + pagina.Content.ReadAsStringAsync().Result.Contains("window.VISITA = {\"codigo\":\"x\"};"), "200 True");
            Ver("Servidor: código que no existe", (int)http.GetAsync("/programa/" + ProgramaWeb.NuevoCodigo()).Result.StatusCode, 404);
            var estadoEnvio = http.GetAsync("/programa/" + codigoPrueba + "/estado").Result;
            Ver("Servidor: cuándo se envió, para el móvil (y nada con otro código)",
                $"{(int)estadoEnvio.StatusCode} {estadoEnvio.Content.ReadAsStringAsync().Result} {(int)http.GetAsync("/programa/" + ProgramaWeb.NuevoCodigo() + "/estado").Result.StatusCode}",
                "200 {\"enviado\":\"2026-11-18T10:30:00|2\"} 404");
            Ver("Servidor: otra ruta", (int)http.GetAsync("/admin").Result.StatusCode, 404);
            var envio = http.PostAsync("/programa/" + codigoPrueba, new System.Net.Http.StringContent("{\"version\":1}", System.Text.Encoding.UTF8, "application/json")).Result;
            Ver("Servidor: envío guardado", (int)envio.StatusCode + " " + recibidoPrueba, "200 {\"version\":1}");
            var seguido = http.PostAsync("/programa/" + codigoPrueba, new System.Net.Http.StringContent("{\"version\":1}", System.Text.Encoding.UTF8, "application/json")).Result;
            Ver("Servidor: otro envío seguido, a esperar", (int)seguido.StatusCode, 429);

            // Archivos: cada tipo a su carpeta (la S-21, por grupos), solo documentos y sin salir de la carpeta.
            string baseArchivos = "/programa/" + codigoPrueba + "/archivos/";
            string tarjeta = System.IO.Path.Combine(carpetaPrueba, "S-21", "G2", "Tarjeta Pérez.pdf");
            var subida = http.PutAsync(baseArchivos + "s21-g2/" + Uri.EscapeDataString("Tarjeta Pérez.pdf"), new System.Net.Http.ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 prueba"))).Result;
            Ver("Servidor: archivo subido a su grupo", (int)subida.StatusCode + " " + System.IO.File.Exists(tarjeta), "200 True");
            http.PutAsync(baseArchivos + "s21-g2/" + Uri.EscapeDataString("Tarjeta Pérez.pdf"), new System.Net.Http.ByteArrayContent(new byte[] { 37, 80 })).Wait();
            Ver("Servidor: mismo nombre, no se pisa", System.IO.File.Exists(System.IO.Path.Combine(carpetaPrueba, "S-21", "G2", "Tarjeta Pérez (2).pdf")), true);
            string listaSubidos = http.GetStringAsync(baseArchivos.TrimEnd('/')).Result;
            Ver("Servidor: lista de lo subido", listaSubidos.Contains("\"tipo\":\"s21-g2\"") && listaSubidos.Contains("(2).pdf"), true);
            Ver("Servidor: tipo de archivo no admitido", (int)http.PutAsync(baseArchivos + "otros/virus.exe", new System.Net.Http.ByteArrayContent(new byte[] { 1 })).Result.StatusCode, 415);
            var fuera = http.PutAsync(baseArchivos + "otros/" + Uri.EscapeDataString("../../fuera.pdf"), new System.Net.Http.ByteArrayContent(new byte[] { 1 })).Result;
            Ver("Servidor: nombre con ruta, dentro de su carpeta", (int)fuera.StatusCode + " " + System.IO.File.Exists(System.IO.Path.Combine(carpetaPrueba, "Otros", "fuera.pdf")), "200 True");
            Ver("Servidor: tipo inventado", (int)http.PutAsync(baseArchivos + "s21-g99/x.pdf", new System.Net.Http.ByteArrayContent(new byte[] { 1 })).Result.StatusCode, 404);
            var quitada = http.DeleteAsync(baseArchivos + "s21-g2/" + Uri.EscapeDataString("Tarjeta Pérez.pdf")).Result;
            Ver("Servidor: quitar un archivo", (int)quitada.StatusCode + " " + System.IO.File.Exists(tarjeta), "200 False");
            Ver("Servidor: archivos de un código que no existe", (int)http.GetAsync("/programa/" + ProgramaWeb.NuevoCodigo() + "/archivos").Result.StatusCode, 404);
        }

        try { System.IO.Directory.Delete(carpetaPrueba, true); } catch { }
    }

    // Ajustes de la página Avisos: sin nada guardado, lo de siempre; ida y vuelta; el día de quedar con el CCA.
    private static void AjustesDeAvisos()
    {
        var vacio = AjustesAvisos.Leer(_ => "");
        Ver("Avisos: sin nada guardado, como siempre", $"{vacio.AlertaActiva("S302")}|{vacio.DiasAlerta("S302")}|{vacio.DiasAlerta("Documentacion")}|{vacio.DiasAlerta("Recordatorio")}|{vacio.TiempoActivo}|{vacio.CcaActivo}|{vacio.S303Activo}", "True|90|42|60|True|False|False");
        Ver("Avisos: el S-303 pendiente ya no es alerta", AjustesAvisos.Alertas.Any(a => a.Tipo == "S303"), false);
        var sinFormulario = new AjustesAvisos { ProgramaWebActivo = false }.Valores().ToDictionary(v => v.Clave, v => v.Valor);
        Ver("Avisos: el del formulario de los ancianos, activo sin nada guardado; se puede apagar",
            $"{vacio.ProgramaWebActivo} {AjustesAvisos.Leer(c => sinFormulario.TryGetValue(c, out string v) ? v : "").ProgramaWebActivo}",
            "True False");

        var ajustes = new AjustesAvisos { CcaActivo = true, CcaDia = DayOfWeek.Monday, CcaHora = new TimeSpan(9, 30, 0), S303Activo = true, S303Hora = new TimeSpan(21, 0, 0), TiempoActivo = false };
        ajustes.PonerAlerta("Documentacion", false, 30);
        var guardado = ajustes.Valores().ToDictionary(v => v.Clave, v => v.Valor);
        var leido = AjustesAvisos.Leer(c => guardado.TryGetValue(c, out string v) ? v : "");
        Ver("Avisos: ida y vuelta", $"{leido.CcaActivo}|{leido.CcaDia}|{leido.CcaHora:hh\\:mm}|{leido.S303Activo}|{leido.S303Hora:hh\\:mm}|{leido.TiempoActivo}|{leido.AlertaActiva("Documentacion")}|{leido.DiasAlerta("Documentacion")}|{leido.DiasAlerta("S302")}", "True|Monday|09:30|True|21:00|False|False|30|90");

        var martes = new DateTime(2026, 11, 17);
        Ver("Avisos: CCA el jueves de antes", AjustesAvisos.DiaCca(martes, DayOfWeek.Thursday), new DateTime(2026, 11, 12));
        Ver("Avisos: CCA el lunes de antes", AjustesAvisos.DiaCca(martes, DayOfWeek.Monday), new DateTime(2026, 11, 16));
        Ver("Avisos: CCA en martes, el de la semana antes", AjustesAvisos.DiaCca(martes, DayOfWeek.Tuesday), new DateTime(2026, 11, 10));
    }

    // Lo que llega del formulario web: el borrador se guarda sin tocar el programa; lo enviado
    // completo pasa solo al programa, también al reenviarlo, salvo que se haya cambiado a mano.
    private static void ProgramaWebRecibido()
    {
        // Las bases reales tienen Ubicacion (EsquemaBase); la de pruebas, creada por Windows, no.
        if (Texto("SELECT COUNT(*) FROM pragma_table_info('ProgramaVisitaV2') WHERE name = 'Ubicacion'") == "0")
        {
            Exec("ALTER TABLE ProgramaVisitaV2 ADD COLUMN Ubicacion TEXT");
        }

        Exec("INSERT INTO Congregaciones (Codigo, Nombre, DireccionSR) VALUES ('WEB1', 'Congregación Web', 'Calle del Salón 1')");
        Exec("INSERT INTO Semanas (FechaMartes, Actividad, Congregacion) VALUES ('2026-12-01', 'Visita', 'Congregación Web')");
        string codigo = ProgramaWeb.NuevoCodigo();
        DatabaseHelper.GuardarConfiguracion(ProgramaWeb.ClaveCodigo("WEB1", "2026-12"), codigo);
        Ver("Enlace: ya creado para esa visita, no para otra (y sin crearlo al mirar)",
            $"{ProgramaWebPc.TieneEnlace("WEB1", "2026-12")} {ProgramaWebPc.TieneEnlace("WEB1", "2027-01")} {DatabaseHelper.VisitasConEnlaceProgramaWeb().Contains("WEB1_2026-12")} {ProgramaWebPc.TieneEnlace("WEB1", "2027-01")}",
            "True False True False");
        Ver("Calendario: enlace creado e importación hecha, en verde",
            $"{new SemanaCalendario { EnlaceGenerado = true }.ColorBotonEnlace} {new SemanaCalendario().ColorBotonEnlace} {new SemanaCalendario { TieneDatosImportados = true }.ColorBotonImportar}",
            "#10893E Transparent #10893E");

        string envio = """{"version":1,"reuniones":{"entreSemana":{"hora":"19:00","lugar":""},"ancianos":{"hora":"20:00","lugar":"Calle A 1","lat":39.9,"lon":-6.1}}}""";
        string borrador = envio.Replace("{\"version\":1,", "{\"version\":1,\"borrador\":true,");
        string Filas() => Texto("SELECT COUNT(*) FROM ProgramaVisitaV2 WHERE CongreCodigo = 'WEB1' AND MesReferencia = '2026-12'");
        string HoraAsm() => Texto("SELECT Hora FROM ProgramaVisitaV2 WHERE CongreCodigo = 'WEB1' AND TipoActividad = 'Reunión de A/SM'");

        // Los archivos de los ancianos, siempre en los datos de SCCosmos (nunca en el Archivo del circuito, aunque exista).
        string archivoCircuito = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sccosmos_arch_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(archivoCircuito, "1 - 2 - S-303 y S-2", "7 - Congregación Web"));
        DatabaseHelper.GuardarConfiguracion("RutaArchivoCircuito", archivoCircuito);
        Ver("Archivos de los ancianos: en %LocalAppData%\\SCCosmos, no en el Archivo del circuito",
            ProgramaWebPc.CarpetaArchivos("WEB1", "2026-12"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCCosmos", "Archivos de los ancianos", "Congregación Web", "Diciembre 2026"));
        DatabaseHelper.GuardarConfiguracion("RutaArchivoCircuito", "");
        try { System.IO.Directory.Delete(archivoCircuito, true); } catch { }

        string Enviado() => DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveEnviado("WEB1", "2026-12"), "");
        Ver("Web recibido: el borrador se guarda y no toca el programa",
            $"{ProgramaWebPc.Recibir(codigo, borrador)} {ProgramaWebPc.Recibido("WEB1", "2026-12")?.Completo} {Filas()} [{Enviado()}]", "True False 0 []");
        Ver("Web recibido: lo enviado pasa solo al programa",
            $"{ProgramaWebPc.Recibir(codigo, envio)} {ProgramaWebPc.Recibido("WEB1", "2026-12")?.Completo} {Filas()} {ProgramaWebPc.Aplicado("WEB1", "2026-12").HasValue}", "True True 2 True");
        string enviadoAntes = Enviado();
        Ver("Web recibido: al pulsar «Enviar» se apunta cuándo (para avisar en el móvil)",
            $"{ProgramaWeb.LeerEnviado(enviadoAntes)?.Cuando.Date == DateTime.Today} {ProgramaWebPc.EstadoJson(codigo) == "{\"enviado\":\"" + enviadoAntes + "\"}"} {ProgramaWebPc.EstadoJson(ProgramaWeb.NuevoCodigo()) is null}",
            "True True True");
        System.Threading.Thread.Sleep(1100);
        ProgramaWebPc.Recibir(codigo, borrador);
        Ver("Web recibido: lo que siguen escribiendo después no cambia cuándo se envió", Enviado(), enviadoAntes);
        Ver("Web recibido: sin lugar, el del salón",
            Texto("SELECT Direccion FROM ProgramaVisitaV2 WHERE CongreCodigo = 'WEB1' AND TipoActividad = 'Reunión entre semana'"), "Calle del Salón 1");
        ProgramaWebPc.Recibir(codigo, envio.Replace("20:00", "20:30"));
        Ver("Web recibido: al reenviarlo, el programa se actualiza", HoraAsm(), "20:30");
        Exec("UPDATE ProgramaVisitaV2 SET Hora = '21:00' WHERE CongreCodigo = 'WEB1' AND TipoActividad = 'Reunión de A/SM'");
        ProgramaWebPc.Recibir(codigo, envio);
        Ver("Web recibido: con cambios a mano, no se pisa", $"{HoraAsm()} {ProgramaWebPc.ProgramaSinTocar("WEB1", "2026-12")}", "21:00 False");
        DatabaseHelper.GuardarConfiguracion(ProgramaWeb.ClaveRecibido("WEB1", "2026-12"), "{\"recibido\":\"2026-11-17T10:00:00\",\"datos\":{\"version\":1}}");
        Ver("Web recibido: lo guardado antes de los borradores cuenta como enviado", ProgramaWebPc.Recibido("WEB1", "2026-12")?.Completo, true);

        // Servidor del formulario elegido en Configuración: todos los enlaces van a él y nadie lo cambia.
        Ver("Servidor: direcciones aceptadas",
            string.Join("|", ProgramaWeb.NormalizarDireccion("mi-pc.tail.ts.net/"), ProgramaWeb.NormalizarDireccion(" http://192.168.1.5:47320 "),
                ProgramaWeb.NormalizarDireccion("https://x.es/ruta"), ProgramaWeb.NormalizarDireccion("no es una dirección")),
            "https://mi-pc.tail.ts.net|http://192.168.1.5:47320||");
        DatabaseHelper.GuardarConfiguracion(ProgramaWeb.ClaveDireccion, "https://otro-pc.ts.net");
        ProgramaWebPc.ElegirServidor("servidor.ejemplo.org");
        Ver("Servidor elegido: los enlaces van a él (también en la clave que leen las versiones anteriores)",
            $"{ProgramaWebPc.DireccionPublica()} {ProgramaWebPc.Enlace("WEB1", "2026-12")} {DatabaseHelper.ObtenerConfiguracion(ProgramaWeb.ClaveDireccion, "")} {ProgramaWeb.DireccionEnlaces(c => DatabaseHelper.ObtenerConfiguracion(c, ""))}",
            $"https://servidor.ejemplo.org https://servidor.ejemplo.org/programa/{codigo} https://servidor.ejemplo.org https://servidor.ejemplo.org");
        Ver("Cambio de servidor: los enlaces que aún valen, con la dirección nueva",
            string.Join("|", ProgramaWebPc.EnlacesActivos().Select(a => $"{a.Congregacion} {a.Mes} {a.Enlace}")),
            $"WEB1 2026-12 https://servidor.ejemplo.org/programa/{codigo}");
        Ver("Cambio de servidor: este PC manda los enlaces antiguos al elegido (lo de dentro del PC, no)",
            $"{ProgramaWebPc.DestinoRedireccion("pc-viejo.tail.ts.net") ?? "aquí"} {ProgramaWebPc.DestinoRedireccion("servidor.ejemplo.org") ?? "aquí"} {ProgramaWebPc.DestinoRedireccion("localhost:47320") ?? "aquí"}",
            "https://servidor.ejemplo.org aquí aquí");
        ProgramaWebPc.ElegirServidor("");
        Ver("Servidor sin elegir: vuelve a ser automático", ProgramaWebPc.ServidorElegido(), "");
        Ver("Servidor sin elegir: no se redirige nada", ProgramaWebPc.DestinoRedireccion("pc-viejo.tail.ts.net") ?? "aquí", "aquí");

        // Comidas con la congregación: las de Configuración, salvo que la congregación tenga las suyas (Circuito).
        DatabaseHelper.GuardarConfiguracion(ProgramaWeb.ClaveDiasComida, "Jueves,Viernes");
        string ComidasWeb1() => string.Join(",", ProgramaWebPc.DiasComida("WEB1")) + " " + ProgramaWebPc.TieneDiasComidaPropios("WEB1");
        Ver("Comidas: sin elegir en la congregación, las de Configuración", ComidasWeb1(), "Jueves,Viernes False");
        ProgramaWebPc.GuardarDiasComida("WEB1", new[] { "Domingo", "Viernes" });
        Ver("Comidas: las propias de la congregación, también en el enlace",
            $"{ComidasWeb1()} {ProgramaWebPc.DatosVisitaJson(codigo, "WEB1", "2026-12").Contains("\"diasComida\":[\"Viernes\",\"Domingo\"]")}",
            "Viernes,Domingo True True");
        Ver("Comidas: al recibirlo, solo las de los días de esa congregación",
            string.Join(",", ProgramaWebPc.EventosRecibidos("WEB1", "{\"version\":1,\"comidas\":[{\"dia\":\"Jueves\",\"familia\":\"A\"},{\"dia\":\"Viernes\",\"familia\":\"B\"}]}").Select(e => e.DiaSemana)),
            "Viernes");
        ProgramaWebPc.GuardarDiasComida("WEB1", Array.Empty<string>());
        Ver("Comidas: ningún día con esa congregación", $"{ComidasWeb1()} {ProgramaWebPc.DatosVisitaJson(codigo, "WEB1", "2026-12").Contains("\"diasComida\":[]")}", " True True");
        ProgramaWebPc.GuardarDiasComida("WEB1", null);
        Ver("Comidas: «Como en Configuración» vuelve a las generales", ComidasWeb1(), "Jueves,Viernes False");
        DatabaseHelper.GuardarConfiguracion(ProgramaWeb.ClaveDiasComida, "");
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