using System.Globalization;

using Microsoft.Data.Sqlite;

using SCCosmos.Sincro;

namespace SCCosmos.Pruebas;

// ============================================================
// PRUEBAS DEL MOTOR DE SINCRONIZACIÓN
//
// Simula varios dispositivos en el mismo equipo (puertos
// distintos) con copias REALES de la base de datos y comprueba
// que los cambios llegan, se fusionan y nada se pierde.
//
// Uso:  dotnet run --project SincroPruebas -c Release [-- ruta.db]
// Sin argumento copia la base de la app de Windows
// (%LOCALAPPDATA%\SCCosmos\sccosmos.db). Devuelve 0 si todo pasa.
//
// Aislamiento: la base de origen solo se lee (copias con la API de
// SQLite en %TEMP%\sccosmos_pruebas_sincro, que se borra al
// terminar). Los dispositivos de prueba tienen su propia clave, se
// conectan entre sí por 127.0.0.1 (puertos 47401-47405 y
// 47411-47412) y no hacen anuncios UDP: los dispositivos reales ni
// los ven ni podrían emparejarse con ellos.
// ============================================================

internal static class Program
{
    private static string _carpeta =
        "";

    private static string _dbA =
        "";

    private static string _dbB =
        "";

    private static string _dbC =
        "";

    private static ServicioSincro? _servicioA;

    private static ServicioSincro? _servicioB;

    private static ServicioSincro? _servicioC;

    private static int _superadas;

    private static int _total;


    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding =
            System.Text.Encoding.UTF8;

        // Base de origen: la indicada como argumento o la de la app de
        // Windows. Solo se lee: las pruebas trabajan sobre copias.
        string origen =
            args.Length > 0
                ? args[0]
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "SCCosmos",
                    "sccosmos.db");

        if (!File.Exists(
                origen))
        {
            Console.WriteLine(
                "No encuentro una base de datos para las pruebas.");

            return 1;
        }

        _carpeta =
            Path.Combine(
                Path.GetTempPath(),
                "sccosmos_pruebas_sincro");

        if (Directory.Exists(
                _carpeta))
        {
            Directory.Delete(
                _carpeta,
                true);
        }

        Directory.CreateDirectory(
            _carpeta);

        _dbA =
            Path.Combine(
                _carpeta,
                "A.db");

        _dbB =
            Path.Combine(
                _carpeta,
                "B.db");

        _dbC =
            Path.Combine(
                _carpeta,
                "C.db");

        CopiarBase(
            origen,
            _dbA);

        CopiarBase(
            origen,
            _dbB);

        CopiarBase(
            origen,
            _dbC);

        Console.WriteLine(
            $"Base de prueba: {origen}");

        // ---- Dispositivos ----

        var configA =
            new SincroConfiguracion
            {
                IdDispositivo =
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",

                NombreDispositivo =
                    "Dispositivo A",

                Clave =
                    "12345678",

                Puerto =
                    47401,


                // Sin anuncios UDP: el banco no se deja ver en la red.

                Descubrimiento =
                    false
            };

        var configB =
            new SincroConfiguracion
            {
                IdDispositivo =
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",

                NombreDispositivo =
                    "Dispositivo B",

                Clave =
                    "12345678",

                Puerto =
                    47402,


                // Sin anuncios UDP: el banco no se deja ver en la red.

                Descubrimiento =
                    false
            };

        var configC =
            new SincroConfiguracion
            {
                IdDispositivo =
                    "cccccccccccccccccccccccccccccccc",

                NombreDispositivo =
                    "Dispositivo C",

                Clave =
                    "12345678",

                Puerto =
                    47403,


                // Sin anuncios UDP: el banco no se deja ver en la red.

                Descubrimiento =
                    false
            };

        // Direcciones manuales (en el mismo equipo).
        configA.Direcciones.Add(
            "127.0.0.1:47402");

        configB.Direcciones.Add(
            "127.0.0.1:47401");

        configC.Direcciones.Add(
            "127.0.0.1:47401");

        var almacenA =
            new SincroAlmacen(
                _dbA,
                configA.IdDispositivo);

        // A hace de PC; B y C, de móviles.
        var almacenB =
            new SincroAlmacen(
                _dbB,
                configB.IdDispositivo,
                saltarIds: true);

        var almacenC =
            new SincroAlmacen(
                _dbC,
                configC.IdDispositivo,
                saltarIds: true);

        almacenA.Asegurar();
        almacenB.Asegurar();
        almacenC.Asegurar();

        // Las tres copias salen de la misma base: se dan por sincronizadas
        // entre sí, como dispositivos ya emparejados. Si no, al conectarse
        // se enviarían el registro entero (~23.000 cambios, 200 por
        // segundo) antes que los cambios de las pruebas.
        DarPorSincronizados(
            (almacenA, configA.IdDispositivo),
            (almacenB, configB.IdDispositivo),
            (almacenC, configC.IdDispositivo));

        _servicioA =
            new ServicioSincro(
                configA,
                almacenA);

        _servicioB =
            new ServicioSincro(
                configB,
                almacenB);

        _servicioC =
            new ServicioSincro(
                configC,
                almacenC);

        _servicioA.Iniciar();
        _servicioB.Iniciar();
        _servicioC.Iniciar();

        // ---- Filas de trabajo ----

        List<long> filas =
            LeerLista(
                _dbA,
                "SELECT Id FROM Personas ORDER BY Id LIMIT 6;");

        if (filas.Count < 6)
        {
            Console.WriteLine(
                "La base de prueba no tiene suficientes personas.");

            _servicioA.Detener();
            _servicioB.Detener();
            _servicioC.Detener();

            return 1;
        }

        long fila1 =
            filas[0];

        long fila2 =
            filas[1];

        long fila3 =
            filas[2];

        long fila4 =
            filas[3];

        long fila5 =
            filas[4];

        long fila6 =
            filas[5];

        Console.WriteLine();

        await Probar(
            "1. Un cambio en A llega a B",
            async () =>
            {
                Escribir(
                    _dbA,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T1' WHERE Id = {fila1};");

                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            _dbB,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila1};")
                        ==
                        "SINCRO-T1");
            });

        await Probar(
            "2. Un cambio en B llega a A",
            async () =>
            {
                Escribir(
                    _dbB,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T2' WHERE Id = {fila2};");

                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            _dbA,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila2};")
                        ==
                        "SINCRO-T2");
            });

        await Probar(
            "3. Ediciones simultáneas a la misma ficha se fusionan (el último gana)",
            async () =>
            {
                Escribir(
                    _dbA,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T3-A' WHERE Id = {fila3};");

                Escribir(
                    _dbB,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T3-B' WHERE Id = {fila3};");

                bool convergen =
                    await EsperarAsync(
                        () =>
                        {
                            string valorA =
                                LeerTexto(
                                    _dbA,
                                    $"SELECT NombreComun FROM Personas WHERE Id = {fila3};");

                            string valorB =
                                LeerTexto(
                                    _dbB,
                                    $"SELECT NombreComun FROM Personas WHERE Id = {fila3};");

                            return valorA == valorB
                                   &&
                                   (valorA == "SINCRO-T3-A"
                                    ||
                                    valorA == "SINCRO-T3-B");
                        },
                        20);

                if (!convergen)
                {
                    return false;
                }

                // Y también debe llegar al tercero.
                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            _dbC,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila3};")
                        ==
                        LeerTexto(
                            _dbA,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila3};"),
                    20);
            });

        await Probar(
            "4. Alta de una persona nueva en A llega a B y C",
            async () =>
            {
                long nueva =
                    InsertarPersona(
                        _dbA,
                        "SINCRO-T4");

                bool enB =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                _dbB,
                                $"SELECT Nombre FROM Personas WHERE Id = {nueva};")
                            ==
                            "SINCRO-T4");

                bool enC =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                _dbC,
                                $"SELECT Nombre FROM Personas WHERE Id = {nueva};")
                            ==
                            "SINCRO-T4");

                return enB
                       &&
                       enC;
            });

        await Probar(
            "5. Borrado en A llega a B",
            async () =>
            {
                // Antes de borrar, B ya tiene que tener la ficha.
                if (LeerEntero(
                        _dbB,
                        "SELECT COUNT(*) FROM Personas WHERE Nombre = 'SINCRO-T4';")
                    !=
                    1)
                {
                    Console.WriteLine(
                        "      (B no recibió antes el alta de la prueba 4)");

                    return false;
                }

                Escribir(
                    _dbA,
                    $"DELETE FROM Personas WHERE Nombre = 'SINCRO-T4';");

                return await EsperarAsync(
                    () =>
                        LeerEntero(
                            _dbB,
                            "SELECT COUNT(*) FROM Personas WHERE Nombre = 'SINCRO-T4';")
                        ==
                        0);
            });

        await Probar(
            "6. Tercer dispositivo recibe cambios por relevo (B -> A -> C)",
            async () =>
            {
                Escribir(
                    _dbB,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T6' WHERE Id = {fila4};");

                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            _dbC,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila4};")
                        ==
                        "SINCRO-T6",
                    25);
            });

        await Probar(
            "7. Puesta al día al volver a conectarse (C estuvo caído)",
            async () =>
            {
                _servicioC!.Detener();

                await Task.Delay(
                    1500);

                Escribir(
                    _dbA,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T7' WHERE Id = {fila5};");

                await Task.Delay(
                    3000);

                _servicioC.Iniciar();

                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            _dbC,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila5};")
                        ==
                        "SINCRO-T7",
                    25);
            });

        await Probar(
            "8. Sin duplicación: los recuentos coinciden en los tres",
            async () =>
            {
                await Task.Delay(
                    3000);

                long personasA =
                    LeerEntero(
                        _dbA,
                        "SELECT COUNT(*) FROM Personas;");

                long personasB =
                    LeerEntero(
                        _dbB,
                        "SELECT COUNT(*) FROM Personas;");

                long personasC =
                    LeerEntero(
                        _dbC,
                        "SELECT COUNT(*) FROM Personas;");

                long actA =
                    LeerEntero(
                        _dbA,
                        "SELECT COUNT(*) FROM ActividadPersonas;");

                long actB =
                    LeerEntero(
                        _dbB,
                        "SELECT COUNT(*) FROM ActividadPersonas;");

                long actC =
                    LeerEntero(
                        _dbC,
                        "SELECT COUNT(*) FROM ActividadPersonas;");

                return personasA == personasB
                       &&
                       personasB == personasC
                       &&
                       actA == actB
                       &&
                       actB == actC;
            });

        await Probar(
            "9. Clave incorrecta: se rechaza el dispositivo",
            async () =>
            {
                var configMalo =
                    new SincroConfiguracion
                    {
                        IdDispositivo =
                            "dddddddddddddddddddddddddddddddd",

                        NombreDispositivo =
                            "Intruso",

                        Clave =
                            "99999999",

                        Puerto =
                            47404,


                        // Sin anuncios UDP: el banco no se deja ver en la red.

                        Descubrimiento =
                            false
                    };

                configMalo.Direcciones.Add(
                    "127.0.0.1:47401");

                var almacenMalo =
                    new SincroAlmacen(
                        _dbA,
                        configMalo.IdDispositivo);

                // (No se le instala nada: solo intenta hablar.)

                using var servicioMalo =
                    new ServicioSincro(
                        configMalo,
                        almacenMalo);

                servicioMalo.Iniciar();

                bool detectado =
                    await EsperarAsync(
                        () =>
                            _servicioA!.UltimoEstado
                                .Contains(
                                    "Clave"),
                        25);

                servicioMalo.Detener();

                return detectado;
            });

        // ====================================================
        // CANAL POR CARPETA (FUERA DE CASA)
        // ====================================================

        string carpetaCanal =
            Path.Combine(
                _carpeta,
                "canal");

        Directory.CreateDirectory(
            carpetaCanal);

        string dbD =
            Path.Combine(
                _carpeta,
                "D.db");

        string dbE =
            Path.Combine(
                _carpeta,
                "E.db");

        CopiarBase(
            origen,
            dbD);

        CopiarBase(
            origen,
            dbE);

        var configD =
            new SincroConfiguracion
            {
                IdDispositivo =
                    "11111111111111111111111111111111",

                NombreDispositivo =
                    "Archivo D",

                Clave =
                    "12345678",

                Puerto =
                    47411,

                Descubrimiento =
                    false
            };

        var configE =
            new SincroConfiguracion
            {
                IdDispositivo =
                    "22222222222222222222222222222222",

                NombreDispositivo =
                    "Archivo E",

                Clave =
                    "12345678",

                Puerto =
                    47412,

                Descubrimiento =
                    false
            };

        var almacenD =
            new SincroAlmacen(
                dbD,
                configD.IdDispositivo);

        var almacenE =
            new SincroAlmacen(
                dbE,
                configE.IdDispositivo);

        almacenD.Asegurar();
        almacenE.Asegurar();

        using var servicioD =
            new ServicioSincro(
                configD,
                almacenD,
                new CanalArchivosPc(
                    carpetaCanal));

        using var servicioE =
            new ServicioSincro(
                configE,
                almacenE,
                new CanalArchivosPc(
                    carpetaCanal));

        servicioD.Iniciar();
        servicioE.Iniciar();

        await Probar(
            "10. Canal por carpeta: cambio en D llega a E y se confirma",
            async () =>
            {
                Escribir(
                    dbD,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T10' WHERE Id = {fila6};");

                bool llego =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                dbE,
                                $"SELECT NombreComun FROM Personas WHERE Id = {fila6};")
                            ==
                            "SINCRO-T10",
                        50);

                if (!llego)
                {
                    return false;
                }

                // La confirmación también viaja por la carpeta: la
                // marca de enviado de D hacia E debe avanzar.
                return await EsperarAsync(
                    () =>
                        almacenD
                            .ObtenerPar(
                                configE.IdDispositivo)
                            .UltimoEnviado > 0,
                    35);
            });

        await Probar(
            "11. Canal por carpeta: cambio en E llega a D",
            async () =>
            {
                Escribir(
                    dbE,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T11' WHERE Id = {fila6};");

                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            dbD,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila6};")
                        ==
                        "SINCRO-T11",
                    50);
            });

        await Probar(
            "12. Canal por carpeta: si D compacta su archivo, E no pierde cambios",
            async () =>
            {
                string nombreD =
                    $"cambios-{configD.IdDispositivo}.jsonl";

                string archivoD =
                    Path.Combine(
                        carpetaCanal,
                        nombreD);

                string Estado()
                {
                    string firma =
                        new DirectoryInfo(
                                carpetaCanal)
                            .EnumerateFiles(
                                nombreD)
                            .Select(a => $"{a.Length}:{a.LastWriteTimeUtc:HH:mm:ss.fff}")
                            .FirstOrDefault()
                        ?? "sin archivo";

                    return $"archivo de D: {ContarLineas(archivoD)} líneas, firma {firma}; "
                           + $"E leyó {almacenE.ObtenerEstado("ArchivoLineas::" + nombreD)}; "
                           + $"D publicó {almacenD.ObtenerEstado("ArchivoSeqEscrito")} de {almacenD.ObtenerUltimoSeq()}; "
                           + $"ahora {DateTime.UtcNow:HH:mm:ss.fff}";
                }

                // Primero D publica tres cambios de uno en uno y E los lee:
                // así E tiene leídas varias líneas que la compactación puede
                // quitar.
                for (int i = 1; i <= 3; i++)
                {
                    string previo =
                        $"SINCRO-T12-{i}";

                    Escribir(
                        dbD,
                        $"UPDATE Personas SET NombreComun = '{previo}' WHERE Id = {fila3};");

                    if (!await EsperarAsync(
                            () =>
                                LeerTexto(
                                    dbE,
                                    $"SELECT NombreComun FROM Personas WHERE Id = {fila3};")
                                == previo,
                            50))
                    {
                        Console.WriteLine(
                            $"      (E no recibió {previo}: {Estado()})");

                        return false;
                    }
                }

                // Con E parado, D publica dos cambios, cada uno en una
                // línea nueva.
                servicioE.Detener();

                await Task.Delay(
                    1500);

                foreach (var (fila, texto) in new[] { (fila4, "SINCRO-T12A"), (fila5, "SINCRO-T12B") })
                {
                    Escribir(
                        dbD,
                        $"UPDATE Personas SET NombreComun = '{texto}' WHERE Id = {fila};");

                    // Se espera a que esté publicado, no a que el archivo
                    // crezca: D también añade líneas con sus confirmaciones.
                    long seq =
                        almacenD.ObtenerUltimoSeq();

                    if (!await EsperarAsync(
                            () =>
                                long.TryParse(
                                    almacenD.ObtenerEstado(
                                        "ArchivoSeqEscrito"),
                                    out long publicado)
                                && publicado >= seq,
                            45))
                    {
                        Console.WriteLine(
                            $"      (D no publicó {texto} en 45 s: {Estado()})");

                        servicioE.Iniciar();

                        return false;
                    }
                }

                servicioD.Detener();

                await Task.Delay(
                    1500);

                // Compactación simulada: se quitan del principio las líneas
                // añadidas desde la última lectura de E, así que el archivo
                // vuelve a tener tantas líneas como E ya había leído.
                // Contándolas no se notaría y se perderían los cambios
                // nuevos; la huella de la última línea leída (S16) hace que
                // E lo relea desde el principio.
                int leidasE =
                    int.TryParse(
                        almacenE.ObtenerEstado(
                            "ArchivoLineas::" + nombreD),
                        out int valor)
                        ? valor
                        : 0;

                string[] lineas =
                    File.ReadAllText(
                            archivoD)
                        .Split(
                            '\n',
                            StringSplitOptions.RemoveEmptyEntries);

                int sobran =
                    lineas.Length - leidasE;

                if (sobran < 2
                    || sobran >= leidasE)
                {
                    Console.WriteLine(
                        $"      (archivo de D: {lineas.Length} líneas; E había leído {leidasE})");

                    servicioD.Iniciar();
                    servicioE.Iniciar();

                    return false;
                }

                File.WriteAllText(
                    archivoD,
                    string.Join(
                        "\n",
                        lineas.Skip(sobran))
                    + "\n");

                servicioE.Iniciar();

                bool llegaron =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                dbE,
                                $"SELECT NombreComun FROM Personas WHERE Id = {fila4};")
                            == "SINCRO-T12A"
                            && LeerTexto(
                                dbE,
                                $"SELECT NombreComun FROM Personas WHERE Id = {fila5};")
                            == "SINCRO-T12B",
                        50);

                if (!llegaron)
                {
                    Console.WriteLine(
                        $"      (tras compactar, E leía {leidasE} de {lineas.Length} y se quitaron {sobran}; {Estado()})");
                }

                servicioD.Iniciar();

                return llegaron;
            });

        await Probar(
            "13. Canal por carpeta: una ráfaga de más de 2.000 cambios no atasca la carpeta",
            async () =>
            {
                // Más cambios de golpe que la ventana de la carpeta (2.000).
                // Antes la carpeta se quedaba republicando lo antiguo y no
                // llegaba nunca a lo último. Se tocan 2.600 actividades sin
                // cambiar sus datos: basta para que se registren.
                Escribir(
                    dbD,
                    "UPDATE ActividadPersonas SET Notas = Notas WHERE Id IN (SELECT Id FROM ActividadPersonas ORDER BY Id LIMIT 2600);");

                Escribir(
                    dbD,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T13' WHERE Id = {fila6};");

                return await EsperarAsync(
                    () =>
                        LeerTexto(
                            dbE,
                            $"SELECT NombreComun FROM Personas WHERE Id = {fila6};")
                        ==
                        "SINCRO-T13",
                    90);
            });

        await Probar(
            "14. Canal por carpeta: compactar con un dispositivo abandonado no atasca la carpeta",
            async () =>
            {
                string archivoD =
                    Path.Combine(
                        carpetaCanal,
                        $"cambios-{configD.IdDispositivo}.jsonl");

                // Límite de compactación de CanalArchivosSincro.
                const long limite =
                    2 * 1024 * 1024;

                long PosicionPublicada() =>
                    long.TryParse(
                        almacenD.ObtenerEstado(
                            "ArchivoSeqEscrito"),
                        out long valor)
                        ? valor
                        : 0;

                // Un dispositivo abandonado, que se quedó en 0 y no volverá.
                // Antes, la compactación rehacía el archivo desde el más
                // atrasado y la posición publicada retrocedía: la carpeta
                // republicaba lo mismo una y otra vez y nunca llegaba a lo
                // reciente.
                almacenD.GuardarUltimoEnviado(
                    "ffffffffffffffffffffffffffffffff",
                    0);

                servicioD.Detener();

                await Task.Delay(
                    1500);

                // El archivo de D se lleva por encima del límite repitiendo
                // sus líneas (leerlas otra vez no cambia nada): con el
                // siguiente cambio, D lo compactará.
                string bloque =
                    string.Join(
                        "\n",
                        File.ReadAllText(
                                archivoD)
                            .Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries)
                            .Skip(1))
                    + "\n";

                if (bloque.Length < 1000)
                {
                    Console.WriteLine(
                        $"      (archivo de D demasiado pequeño: {bloque.Length} caracteres)");

                    servicioD.Iniciar();

                    return false;
                }

                File.AppendAllText(
                    archivoD,
                    string.Concat(
                        Enumerable.Repeat(
                            bloque,
                            (int)(limite / bloque.Length) + 1)));

                long posicionAntes =
                    PosicionPublicada();

                bool retrocedio =
                    false;

                bool Vigilando(
                    Func<bool> condicion)
                {
                    if (PosicionPublicada() < posicionAntes)
                    {
                        retrocedio =
                            true;
                    }

                    return condicion();
                }

                servicioD.Iniciar();

                Escribir(
                    dbD,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T14A' WHERE Id = {fila1};");

                bool compacto =
                    await EsperarAsync(
                        () =>
                            Vigilando(
                                () =>
                                    TamanoArchivo(
                                        archivoD)
                                    <= limite),
                        50);

                bool llegoA =
                    await EsperarAsync(
                        () =>
                            Vigilando(
                                () =>
                                    LeerTexto(
                                        dbE,
                                        $"SELECT NombreComun FROM Personas WHERE Id = {fila1};")
                                    == "SINCRO-T14A"),
                        50);

                // Lo que se escribe después de compactar también tiene que
                // llegar.
                Escribir(
                    dbD,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T14B' WHERE Id = {fila2};");

                bool llegoB =
                    await EsperarAsync(
                        () =>
                            Vigilando(
                                () =>
                                    LeerTexto(
                                        dbE,
                                        $"SELECT NombreComun FROM Personas WHERE Id = {fila2};")
                                    == "SINCRO-T14B"),
                        50);

                bool correcto =
                    compacto
                    && llegoA
                    && llegoB
                    && !retrocedio;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (compactó: {compacto}; llegó A: {llegoA}; llegó B: {llegoB}; retrocedió: {retrocedio})");
                }

                return correcto;
            });

        await Probar(
            "15. Red: un dispositivo nuevo recibe todo el historial y queda igual",
            async () =>
            {
                string dbG =
                    Path.Combine(
                        _carpeta,
                        "G.db");

                CopiarBase(
                    origen,
                    dbG);

                var configG =
                    new SincroConfiguracion
                    {
                        IdDispositivo =
                            "33333333333333333333333333333333",

                        NombreDispositivo =
                            "Nuevo G",

                        Clave =
                            "12345678",

                        Puerto =
                            47405,

                        // Sin anuncios UDP: el banco no se deja ver en la red.
                        Descubrimiento =
                            false
                    };

                configG.Direcciones.Add(
                    "127.0.0.1:47401");

                var almacenG =
                    new SincroAlmacen(
                        dbG,
                        configG.IdDispositivo);

                almacenG.Asegurar();

                using var servicioG =
                    new ServicioSincro(
                        configG,
                        almacenG);

                // Es el último cambio del registro de A: cuando llega a G, ya
                // ha recibido todo lo anterior.
                Escribir(
                    _dbA,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T15' WHERE Id = {fila1};");

                var reloj =
                    System.Diagnostics.Stopwatch.StartNew();

                servicioG.Iniciar();

                bool llego =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                dbG,
                                $"SELECT NombreComun FROM Personas WHERE Id = {fila1};")
                            == "SINCRO-T15",
                        180);

                Console.WriteLine(
                    $"      (puesta al día de un dispositivo nuevo: {reloj.Elapsed.TotalSeconds:N0} s)");

                servicioG.Detener();

                if (!llego)
                {
                    return false;
                }

                long distintas =
                    FilasDistintas(
                        _dbA,
                        dbG,
                        "Personas")
                    + FilasDistintas(
                        _dbA,
                        dbG,
                        "ActividadPersonas");

                if (distintas > 0)
                {
                    Console.WriteLine(
                        $"      (filas distintas entre A y G: {distintas})");
                }

                return distintas == 0;
            });

        await Probar(
            "16. Red: un documento binario llega como binario y con el mismo contenido",
            async () =>
            {
                long documento =
                    LeerEntero(
                        _dbA,
                        "SELECT Id FROM Documentos ORDER BY Id LIMIT 1;");

                // Con bytes que no son texto válido: ceros incluidos.
                const string contenido =
                    "00FF10FE7F80C3280061626300DEADBEEF";

                Escribir(
                    _dbA,
                    $"UPDATE Documentos SET ContenidoArchivo = x'{contenido}' WHERE Id = {documento};");

                bool llego =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                _dbB,
                                $"SELECT hex(ContenidoArchivo) FROM Documentos WHERE Id = {documento};")
                            == contenido
                            || LeerTexto(
                                _dbB,
                                $"SELECT ContenidoArchivo FROM Documentos WHERE Id = {documento} AND typeof(ContenidoArchivo) = 'text';")
                            == Convert.ToBase64String(
                                Convert.FromHexString(
                                    contenido)));

                string tipo =
                    LeerTexto(
                        _dbB,
                        $"SELECT typeof(ContenidoArchivo) FROM Documentos WHERE Id = {documento};");

                if (!llego
                    || tipo != "blob")
                {
                    Console.WriteLine(
                        $"      (llegó: {llego}; tipo en B: {tipo})");

                    return false;
                }

                return true;
            });

        await Probar(
            "17. Un documento que quedó como texto se repara al arrancar, sin registrarse como cambio",
            () =>
            {
                string dbH =
                    Path.Combine(
                        _carpeta,
                        "H.db");

                CopiarBase(
                    origen,
                    dbH);

                var almacenH =
                    new SincroAlmacen(
                        dbH,
                        "44444444444444444444444444444444");

                almacenH.Asegurar();

                long documento =
                    LeerEntero(
                        dbH,
                        "SELECT Id FROM Documentos ORDER BY Id LIMIT 1;");

                const string contenido =
                    "00FF10FE7F80C3280061626300DEADBEEF";

                // Como quedaban en los dispositivos: base64 guardado como
                // texto, sin pasar por el registro de cambios.
                Escribir(
                    dbH,
                    $"INSERT OR IGNORE INTO SCCosmos_Silencio (Id) VALUES (1); "
                    + $"UPDATE Documentos SET ContenidoArchivo = '{Convert.ToBase64String(Convert.FromHexString(contenido))}' WHERE Id = {documento}; "
                    + "DELETE FROM SCCosmos_Silencio;");

                // Un texto que no es base64 no se toca.
                long otro =
                    LeerEntero(
                        dbH,
                        $"SELECT Id FROM Documentos WHERE Id <> {documento} ORDER BY Id LIMIT 1;");

                Escribir(
                    dbH,
                    $"INSERT OR IGNORE INTO SCCosmos_Silencio (Id) VALUES (1); "
                    + $"UPDATE Documentos SET ContenidoArchivo = 'esto no es base64!' WHERE Id = {otro}; "
                    + "DELETE FROM SCCosmos_Silencio;");

                string registroAntes =
                    LeerTexto(
                        dbH,
                        "SELECT MAX(Seq) || '/' || COUNT(*) FROM SCCosmos_Cambios;");

                almacenH.Asegurar();

                string tipo =
                    LeerTexto(
                        dbH,
                        $"SELECT typeof(ContenidoArchivo) FROM Documentos WHERE Id = {documento};");

                string bytes =
                    LeerTexto(
                        dbH,
                        $"SELECT hex(ContenidoArchivo) FROM Documentos WHERE Id = {documento};");

                string tipoOtro =
                    LeerTexto(
                        dbH,
                        $"SELECT typeof(ContenidoArchivo) FROM Documentos WHERE Id = {otro};");

                string registroDespues =
                    LeerTexto(
                        dbH,
                        "SELECT MAX(Seq) || '/' || COUNT(*) FROM SCCosmos_Cambios;");

                bool correcto =
                    tipo == "blob"
                    && bytes == contenido
                    && tipoOtro == "text"
                    && registroAntes == registroDespues;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (tipo: {tipo}; contenido correcto: {bytes == contenido}; texto no base64: {tipoOtro}; registro: {registroAntes} -> {registroDespues})");
                }

                return Task.FromResult(
                    correcto);
            });

        await Probar(
            "18. Importar territorios: reimportar lo mismo conserva los vínculos y no genera cambios",
            () =>
            {
                string dbT =
                    Path.Combine(
                        _carpeta,
                        "T.db");

                CopiarBase(
                    origen,
                    dbT);

                new SincroAlmacen(
                        dbT,
                        "55555555555555555555555555555555")
                    .Asegurar();

                string congregacion =
                    LeerTexto(
                        dbT,
                        "SELECT CongregacionCodigo FROM TerritoriosVisitaV3 GROUP BY CongregacionCodigo ORDER BY COUNT(*) DESC LIMIT 1;");

                var lista =
                    new List<TerritorioImportado>();

                using (var conexion =
                       Abrir(
                           dbT))
                using (var cmd =
                       conexion.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT Nombre, UltimaFechaCompletado, AsignadoA, FechaAsignado, FechaCompletado FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $c ORDER BY Id;";

                    cmd.Parameters.AddWithValue(
                        "$c",
                        congregacion);

                    using var lector =
                        cmd.ExecuteReader();

                    while (lector.Read())
                    {
                        lista.Add(
                            new TerritorioImportado
                            {
                                Nombre = lector.IsDBNull(0) ? "" : lector.GetString(0),
                                UltimaFechaCompletado = lector.IsDBNull(1) ? "" : lector.GetString(1),
                                AsignadoA = lector.IsDBNull(2) ? "" : lector.GetString(2),
                                FechaAsignado = lector.IsDBNull(3) ? "" : lector.GetString(3),
                                FechaCompletado = lector.IsDBNull(4) ? "" : lector.GetString(4)
                            });
                    }
                }

                Dictionary<long, string> vinculosAntes =
                    LeerVinculos(
                        dbT,
                        congregacion);

                ImportarTerritorios(
                    dbT,
                    congregacion,
                    lista);

                Dictionary<long, string> vinculosDespues =
                    LeerVinculos(
                        dbT,
                        congregacion);

                // Un vínculo que ya había nunca cambia; uno que faltaba puede
                // completarse si esa persona lo tiene sin duda.
                int conservados =
                    vinculosAntes.Count(
                        v => v.Value.Length > 0
                             && vinculosDespues[v.Key] == v.Value);

                int cambiados =
                    vinculosAntes.Count(
                        v => v.Value.Length > 0
                             && vinculosDespues[v.Key] != v.Value);

                int completados =
                    vinculosAntes.Count(
                        v => v.Value.Length == 0
                             && vinculosDespues[v.Key].Length > 0);

                Console.WriteLine(
                    $"      (de {lista.Count} filas: {conservados} vínculos conservados, {cambiados} cambiados, {completados} completados)");

                // La primera vez puede limpiar FechaEliminacion; la segunda no
                // tiene nada que escribir.
                string registroAntes =
                    LeerTexto(
                        dbT,
                        "SELECT MAX(Seq) || '/' || COUNT(*) FROM SCCosmos_Cambios;");

                ImportarTerritorios(
                    dbT,
                    congregacion,
                    lista);

                string registroDespues =
                    LeerTexto(
                        dbT,
                        "SELECT MAX(Seq) || '/' || COUNT(*) FROM SCCosmos_Cambios;");

                bool correcto =
                    lista.Count > 0
                    && vinculosAntes.Count == vinculosDespues.Count
                    && cambiados == 0
                    && registroAntes == registroDespues;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (filas: {vinculosAntes.Count} -> {vinculosDespues.Count}; registro: {registroAntes} -> {registroDespues})");
                }

                return Task.FromResult(
                    correcto);
            });

        await Probar(
            "19. Importar territorios: cada asignación conserva su vínculo, sin mezclarlos",
            () =>
            {
                string dbT =
                    Path.Combine(
                        _carpeta,
                        "T.db");

                // Datos inventados, en una congregación que no existe.
                const string congregacion =
                    "ZZZ-PRUEBA";

                Escribir(
                    dbT,
                    "INSERT INTO TerritoriosVisitaV3 (CongregacionCodigo, Nombre, AsignadoA, FechaAsignado, NombreVinculado, FechaCreacion) VALUES "
                    + $"('{congregacion}', 'T1', 'Ana Prueba', '01/01/2026', 'Ana S21', '01/02/2020'), "
                    + $"('{congregacion}', 'T2', 'Ana Prueba', '02/02/2026', 'Ana S21', '01/02/2020'), "
                    + $"('{congregacion}', 'T1', 'Luis Prueba', '03/03/2026', 'Luis S21', '01/02/2020'), "
                    + $"('{congregacion}', 'T3', 'Mario Prueba', '04/04/2026', 'Mario Uno', '01/02/2020'), "
                    + $"('{congregacion}', 'T4', 'Mario Prueba', '05/05/2026', 'Mario Dos', '01/02/2020'), "
                    + $"('{congregacion}', 'T5', 'Sin Vinculo', '06/06/2026', '', '01/02/2020');");

                static TerritorioImportado Fila(string nombre, string asignado, string fecha) =>
                    new()
                    {
                        Nombre = nombre,
                        AsignadoA = asignado,
                        FechaAsignado = fecha
                    };

                ImportarTerritorios(
                    dbT,
                    congregacion,
                    new List<TerritorioImportado>
                    {
                        Fila("T1", "Ana Prueba", "01/01/2026"),
                        Fila("T1", "Luis Prueba", "03/03/2026"),
                        Fila("T3", "Mario Prueba", "04/04/2026"),
                        Fila("T4", "Mario Prueba", "05/05/2026"),
                        Fila("T6", "Ana Prueba", "07/07/2026"),
                        Fila("T7", "Mario Prueba", "08/08/2026"),
                        Fila("T8", "Persona Nueva", "09/09/2026")
                    });

                string consulta =
                    "SELECT group_concat(v, ';') FROM (SELECT Nombre || '=' || NombreVinculado AS v "
                    + $"FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = '{congregacion}' ORDER BY Id);";

                string resultado =
                    LeerTexto(
                        dbT,
                        consulta);

                const string esperado =
                    "T1=Ana S21;T1=Luis S21;T3=Mario Uno;T4=Mario Dos;T6=Ana S21;T7=;T8=";

                string creacionConocido =
                    LeerTexto(
                        dbT,
                        $"SELECT FechaCreacion FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = '{congregacion}' AND Nombre = 'T3';");

                ImportarTerritorios(
                    dbT,
                    congregacion,
                    new List<TerritorioImportado>
                    {
                        Fila("T1", "Ana Prueba", "01/01/2026"),
                        Fila("T1", "Luis Prueba", "03/03/2026")
                    });

                string trasRecortar =
                    LeerTexto(
                        dbT,
                        consulta);

                bool correcto =
                    resultado == esperado
                    && creacionConocido == "01/02/2020"
                    && trasRecortar == "T1=Ana S21;T1=Luis S21";

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (resultado: {resultado}; creación: {creacionConocido}; tras recortar: {trasRecortar})");
                }

                return Task.FromResult(
                    correcto);
            });

        // Datos inventados: personas y reuniones que no existen.
        const string reunionRsc =
            "ZZZPRUEBARSC";

        string dbR =
            Path.Combine(
                _carpeta,
                "R.db");

        await Probar(
            "20. RSC: guardar lo mismo no genera cambios y cada registro conserva su Id",
            () =>
            {
                CopiarBase(
                    origen,
                    dbR);

                new SincroAlmacen(
                        dbR,
                        "66666666666666666666666666666666")
                    .Asegurar();

                GuardarRsc(
                    dbR,
                    reunionRsc,
                    AsistentesRsc(),
                    GruposRsc());

                string filasAntes =
                    LeerTexto(
                        dbR,
                        ConsultaFilasRsc(reunionRsc));

                long seqAntes =
                    LeerEntero(
                        dbR,
                        "SELECT MAX(Seq) FROM SCCosmos_Cambios;");

                GuardarRsc(
                    dbR,
                    reunionRsc,
                    AsistentesRsc(),
                    GruposRsc());

                long seqDespues =
                    LeerEntero(
                        dbR,
                        "SELECT MAX(Seq) FROM SCCosmos_Cambios;");

                bool correcto =
                    filasAntes.Length > 0
                    && filasAntes
                    == LeerTexto(
                        dbR,
                        ConsultaFilasRsc(reunionRsc))
                    && seqAntes == seqDespues;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (cambios al guardar lo mismo: {seqDespues - seqAntes})");
                }

                return Task.FromResult(
                    correcto);
            });

        await Probar(
            "21. RSC: mover, quitar y añadir personas escribe solo lo necesario, sin dejar restos",
            () =>
            {
                string IdDe(string tabla, string condicion) =>
                    LeerTexto(
                        dbR,
                        $"SELECT Id FROM {tabla} WHERE RscReunionId = '{reunionRsc}' AND {condicion};");

                string miembroLuis =
                    IdDe("RSC_Miembros", "PersonaId = 900002");

                string grupoUno =
                    IdDe("RSC_Grupos", "Numero = 1");

                string grupoDos =
                    IdDe("RSC_Grupos", "Numero = 2");

                string asistenteAna =
                    IdDe("RSC_Asistentes", "PersonaId = 900001");

                long seqAntes =
                    LeerEntero(
                        dbR,
                        "SELECT MAX(Seq) FROM SCCosmos_Cambios;");

                // Luis pasa al grupo 2 detrás de Mara, Pepe se va, llega Noa
                // en un grupo nuevo y cambian las notas de Ana.
                List<RscAsistenteAGuardar> asistentes =
                    AsistentesRsc();

                asistentes.RemoveAll(
                    a => a.PersonaId == 900004);

                asistentes[0].Notas =
                    "Nota nueva";

                asistentes.Add(
                    new RscAsistenteAGuardar
                    {
                        PersonaId = 900005,
                        Nombre = "Noa Inventada",
                        Asiste = true,
                        AceptaGrupo = true
                    });

                var grupos =
                    new List<RscGrupoAGuardar>
                    {
                        GrupoRsc(1, "T-1", (900001, "Ana Inventada")),
                        GrupoRsc(2, "T-2", (900003, "Mara Inventada"), (900002, "Luis Inventado")),
                        GrupoRsc(3, "", (900005, "Noa Inventada")),
                        GrupoRsc(4, "T-4")
                    };

                GuardarRsc(
                    dbR,
                    reunionRsc,
                    asistentes,
                    grupos);

                long escrituras =
                    LeerEntero(
                        dbR,
                        "SELECT MAX(Seq) FROM SCCosmos_Cambios;")
                    - seqAntes;

                string resultado =
                    LeerTexto(
                        dbR,
                        ConsultaGruposRsc(reunionRsc));

                const string esperado =
                    "1[T-1]900001#0;2[T-2]900003#0;2[T-2]900002#1;3[]900005#0";

                string asistentesFinales =
                    LeerTexto(
                        dbR,
                        $"SELECT group_concat(PersonaId) FROM (SELECT PersonaId FROM RSC_Asistentes WHERE RscReunionId = '{reunionRsc}' ORDER BY PersonaId);");

                bool idsConservados =
                    IdDe("RSC_Miembros", "PersonaId = 900002") == miembroLuis
                    && IdDe("RSC_Grupos", "Numero = 1") == grupoUno
                    && IdDe("RSC_Grupos", "Numero = 2") == grupoDos
                    && IdDe("RSC_Asistentes", "PersonaId = 900001") == asistenteAna;

                // Asistentes: notas de Ana, Noa y Pepe. Grupos: el 3. Miembros:
                // Luis, Noa y Pepe.
                bool primeraParte =
                    resultado == esperado
                    && asistentesFinales == "900001,900002,900003,900005"
                    && idsConservados
                    && escrituras == 7;

                // Se quita el grupo 3 con su miembro: no queda ningún resto.
                grupos.RemoveAt(
                    2);

                asistentes.RemoveAll(
                    a => a.PersonaId == 900005);

                GuardarRsc(
                    dbR,
                    reunionRsc,
                    asistentes,
                    grupos);

                string trasQuitar =
                    LeerTexto(
                        dbR,
                        ConsultaGruposRsc(reunionRsc));

                long grupos2 =
                    LeerEntero(
                        dbR,
                        $"SELECT COUNT(*) FROM RSC_Grupos WHERE RscReunionId = '{reunionRsc}';");

                long huerfanos =
                    LeerEntero(
                        dbR,
                        $"SELECT COUNT(*) FROM RSC_Miembros WHERE RscReunionId = '{reunionRsc}' AND RscGrupoId NOT IN (SELECT Id FROM RSC_Grupos);");

                bool correcto =
                    primeraParte
                    && trasQuitar == "1[T-1]900001#0;2[T-2]900003#0;2[T-2]900002#1"
                    && grupos2 == 2
                    && huerfanos == 0;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (resultado: {resultado}; asistentes: {asistentesFinales}; Id conservados: {idsConservados}; escrituras: {escrituras}; tras quitar: {trasQuitar}; grupos: {grupos2}; huérfanos: {huerfanos})");
                }

                return Task.FromResult(
                    correcto);
            });

        await Probar(
            "22. Red: dos dispositivos que guardan a la vez la misma RSC no duplican a nadie",
            async () =>
            {
                string Contar(string db) =>
                    LeerTexto(
                        db,
                        $"SELECT (SELECT COUNT(*) FROM RSC_Asistentes WHERE RscReunionId = '{reunionRsc}') || '/' || (SELECT COUNT(*) FROM RSC_Grupos WHERE RscReunionId = '{reunionRsc}') || '/' || (SELECT COUNT(*) FROM RSC_Miembros WHERE RscReunionId = '{reunionRsc}');");

                GuardarRsc(
                    _dbA,
                    reunionRsc,
                    AsistentesRsc(),
                    GruposRsc());

                bool llego =
                    await EsperarAsync(
                        () => Contar(_dbB) == "4/2/4",
                        30);

                // Cada uno cambia algo distinto en el mismo momento.
                List<RscGrupoAGuardar> gruposA =
                    GruposRsc();

                gruposA[0].Territorio =
                    "Desde A";

                List<RscGrupoAGuardar> gruposB =
                    GruposRsc();

                gruposB[1].Territorio =
                    "Desde B";

                await Task.WhenAll(
                    Task.Run(
                        () => GuardarRsc(
                            _dbA,
                            reunionRsc,
                            AsistentesRsc(),
                            gruposA)),
                    Task.Run(
                        () => GuardarRsc(
                            _dbB,
                            reunionRsc,
                            AsistentesRsc(),
                            gruposB)));

                bool iguales =
                    await EsperarAsync(
                        () => FilasDistintas(_dbA, _dbB, "RSC_Asistentes") == 0
                              && FilasDistintas(_dbA, _dbB, "RSC_Grupos") == 0
                              && FilasDistintas(_dbA, _dbB, "RSC_Miembros") == 0,
                        30);

                string enA =
                    Contar(_dbA);

                string enB =
                    Contar(_dbB);

                bool correcto =
                    llego
                    && iguales
                    && enA == "4/2/4"
                    && enB == "4/2/4";

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (llegó: {llego}; iguales: {iguales}; asistentes/grupos/miembros en A: {enA}, en B: {enB})");
                }

                return correcto;
            });

        await Probar(
            "23. Altas sin verse: el PC y un móvil dan de alta a la vez y no se pierde ninguna",
            async () =>
            {
                _servicioC!.Detener();

                await Task.Delay(
                    1500);

                InsertarDonacion(
                    _dbA,
                    "ALTA-T23-PC");

                InsertarDonacion(
                    _dbC,
                    "ALTA-T23-MOVIL");

                _servicioC.Iniciar();

                return await EsperarAltasAsync(
                    "ALTA-T23-%",
                    2);
            });

        await Probar(
            "24. Altas sin verse: dos móviles dan de alta a la vez y no se pierde ninguna",
            async () =>
            {
                _servicioB!.Detener();
                _servicioC!.Detener();

                await Task.Delay(
                    1500);

                InsertarDonacion(
                    _dbB,
                    "ALTA-T24-MOVIL1");

                InsertarDonacion(
                    _dbC,
                    "ALTA-T24-MOVIL2");

                _servicioB.Iniciar();
                _servicioC.Iniciar();

                return await EsperarAltasAsync(
                    "ALTA-T24-%",
                    2);
            });

        await Probar(
            "25. Altas sin verse tras sincronizar lo propio y lo ajeno: tampoco se pierde ninguna",
            async () =>
            {
                // El alta del móvil ya ha llegado al PC, que seguiría por el
                // Id siguiente: el móvil no debe seguir por el mismo.
                InsertarDonacion(
                    _dbC,
                    "ALTA-T25-1");

                bool propia =
                    await EsperarAltasAsync(
                        "ALTA-T25-%",
                        1);

                _servicioC!.Detener();

                await Task.Delay(
                    1500);

                InsertarDonacion(
                    _dbA,
                    "ALTA-T25-2");

                InsertarDonacion(
                    _dbC,
                    "ALTA-T25-3");

                _servicioC.Iniciar();

                bool trasPropia =
                    await EsperarAltasAsync(
                        "ALTA-T25-%",
                        3);

                // Lo mismo con un alta que el móvil solo ha recibido.
                InsertarDonacion(
                    _dbA,
                    "ALTA-T25-4");

                bool ajena =
                    await EsperarAltasAsync(
                        "ALTA-T25-%",
                        4);

                _servicioC.Detener();

                await Task.Delay(
                    1500);

                InsertarDonacion(
                    _dbA,
                    "ALTA-T25-5");

                InsertarDonacion(
                    _dbC,
                    "ALTA-T25-6");

                _servicioC.Iniciar();

                bool trasAjena =
                    await EsperarAltasAsync(
                        "ALTA-T25-%",
                        6);

                if (!propia
                    || !trasPropia
                    || !ajena
                    || !trasAjena)
                {
                    Console.WriteLine(
                        $"      (propia: {propia}; tras la propia: {trasPropia}; ajena: {ajena}; tras la ajena: {trasAjena})");
                }

                return propia
                       && trasPropia
                       && ajena
                       && trasAjena;
            });

        await Probar(
            "26. Los huecos de Id no se anotan como cambio ni se reescriben, y el PC no los deja",
            () =>
            {
                string dbS =
                    Path.Combine(
                        _carpeta,
                        "S.db");

                CopiarBase(
                    origen,
                    dbS);

                const string idDispositivo =
                    "77777777777777777777777777777777";

                const string secuencias =
                    "SELECT group_concat(name || '=' || seq, ';') FROM (SELECT name, seq FROM sqlite_sequence ORDER BY name);";

                const string registro =
                    "SELECT MAX(Seq) || '/' || COUNT(*) FROM SCCosmos_Cambios;";

                new SincroAlmacen(
                        dbS,
                        idDispositivo)
                    .Asegurar();

                string comoPc =
                    LeerTexto(
                        dbS,
                        secuencias);

                string registroAntes =
                    LeerTexto(
                        dbS,
                        registro);

                long mayor =
                    LeerEntero(
                        dbS,
                        "SELECT MAX(Id) FROM Donaciones;");

                var movil =
                    new SincroAlmacen(
                        dbS,
                        idDispositivo,
                        saltarIds: true);

                movil.Asegurar();

                string comoMovil =
                    LeerTexto(
                        dbS,
                        secuencias);

                movil.Asegurar();

                string otraVez =
                    LeerTexto(
                        dbS,
                        secuencias);

                string registroDespues =
                    LeerTexto(
                        dbS,
                        registro);

                InsertarDonacion(
                    dbS,
                    "ALTA-T26-MOVIL");

                long altaMovil =
                    LeerEntero(
                        dbS,
                        "SELECT Id FROM Donaciones WHERE Notas = 'ALTA-T26-MOVIL';");

                // El PC sigue por el siguiente, sin huecos.
                new SincroAlmacen(
                        dbS,
                        idDispositivo)
                    .Asegurar();

                InsertarDonacion(
                    dbS,
                    "ALTA-T26-PC");

                long altaPc =
                    LeerEntero(
                        dbS,
                        "SELECT Id FROM Donaciones WHERE Notas = 'ALTA-T26-PC';");

                bool correcto =
                    comoPc != comoMovil
                    && comoMovil == otraVez
                    && registroAntes == registroDespues
                    && altaMovil > mayor + 100_000
                    && altaMovil <= mayor + 200_001
                    && altaPc == altaMovil + 1;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (secuencias cambian: {comoPc != comoMovil}; estables: {comoMovil == otraVez}; registro: {registroAntes} -> {registroDespues}; mayor: {mayor}; alta del móvil: {altaMovil}; alta del PC: {altaPc})");
                }

                return Task.FromResult(
                    correcto);
            });

        await Probar(
            "27. Red: un dispositivo vacío prepara el esquema, recibe todo y queda igual",
            async () =>
            {
                string dbV =
                    Path.Combine(
                        _carpeta,
                        "V.db");

                bool preparado =
                    EsquemaBase.CrearSiHaceFalta(
                        dbV);

                var configV =
                    new SincroConfiguracion
                    {
                        IdDispositivo =
                            "88888888888888888888888888888888",

                        NombreDispositivo =
                            "Vacío V",

                        Clave =
                            "12345678",

                        Puerto =
                            47406,

                        Descubrimiento =
                            false
                    };

                configV.Direcciones.Add(
                    "127.0.0.1:47401");

                var almacenV =
                    new SincroAlmacen(
                        dbV,
                        configV.IdDispositivo,
                        saltarIds: true);

                almacenV.Asegurar();

                using var servicioV =
                    new ServicioSincro(
                        configV,
                        almacenV);

                // Es el último cambio del registro de A: cuando llega a V, ya
                // ha recibido todo lo anterior.
                Escribir(
                    _dbA,
                    $"UPDATE Personas SET NombreComun = 'SINCRO-T27' WHERE Id = {fila1};");

                var reloj =
                    System.Diagnostics.Stopwatch.StartNew();

                servicioV.Iniciar();

                bool llego =
                    await EsperarAsync(
                        () =>
                            LeerTexto(
                                dbV,
                                $"SELECT NombreComun FROM Personas WHERE Id = {fila1};")
                            == "SINCRO-T27",
                        300);

                Console.WriteLine(
                    $"      (puesta al día de un dispositivo vacío: {reloj.Elapsed.TotalSeconds:N0} s)");

                servicioV.Detener();

                var distintas =
                    new List<string>();

                foreach (string tabla in
                         LeerTablas(
                             _dbA))
                {
                    long filasDistintas =
                        FilasDistintas(
                            _dbA,
                            dbV,
                            tabla);

                    if (filasDistintas > 0)
                    {
                        distintas.Add(
                            $"{tabla}={filasDistintas}");
                    }
                }

                bool correcto =
                    preparado
                    && llego
                    && distintas.Count == 0;

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (preparado: {preparado}; llegó: {llego}; distintas: {string.Join(", ", distintas)})");
                }

                return correcto;
            });

        await Probar(
            "28. El esquema de un dispositivo nuevo coincide con el real y no toca una base en uso",
            () =>
            {
                const string firma =
                    "SELECT group_concat(v, char(10)) FROM (SELECT m.name || '.' || p.name || ' ' || p.type || ' ' || p.\"notnull\" || ' ' || COALESCE(p.dflt_value, '-') || ' ' || p.pk AS v "
                    + "FROM sqlite_master m JOIN pragma_table_info(m.name) p "
                    + "WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%' AND m.name NOT LIKE 'SCCosmos_%' ORDER BY m.name, p.cid);";

                string dbE =
                    Path.Combine(
                        _carpeta,
                        "Nuevo.db");

                bool creado =
                    EsquemaBase.CrearSiHaceFalta(
                        dbE);

                bool otraVez =
                    EsquemaBase.CrearSiHaceFalta(
                        dbE);

                bool enUso =
                    EsquemaBase.CrearSiHaceFalta(
                        _dbA);

                string[] real =
                    LeerTexto(
                        _dbA,
                        firma).Split('\n');

                string[] incluido =
                    LeerTexto(
                        dbE,
                        firma).Split('\n');

                string[] diferencias =
                    real.Except(incluido)
                        .Concat(
                            incluido.Except(real))
                        .ToArray();

                // Una base a medias: una tabla con columnas de menos y una fila
                // que no se debe perder.
                string dbM =
                    Path.Combine(
                        _carpeta,
                        "M.db");

                Escribir(
                    dbM,
                    "CREATE TABLE Tareas (Id INTEGER PRIMARY KEY AUTOINCREMENT, Titulo TEXT); "
                    + "INSERT INTO Tareas (Titulo) VALUES ('Tarea inventada');");

                bool completada =
                    EsquemaBase.CrearSiHaceFalta(
                        dbM);

                long columnas =
                    LeerEntero(
                        dbM,
                        "SELECT COUNT(*) FROM pragma_table_info('Tareas');");

                long columnasReales =
                    LeerEntero(
                        _dbA,
                        "SELECT COUNT(*) FROM pragma_table_info('Tareas');");

                string fila =
                    LeerTexto(
                        dbM,
                        "SELECT Titulo FROM Tareas;");

                bool correcto =
                    creado
                    && !otraVez
                    && !enUso
                    && diferencias.Length == 0
                    && completada
                    && columnas == columnasReales
                    && fila == "Tarea inventada";

                if (!correcto)
                {
                    Console.WriteLine(
                        $"      (creado: {creado}; otra vez: {otraVez}; en uso: {enUso}; diferencias: {string.Join(" | ", diferencias.Take(6))}; a medias: {completada}, {columnas} de {columnasReales} columnas, fila «{fila}»)");
                }

                return Task.FromResult(
                    correcto);
            });

        servicioD.Detener();
        servicioE.Detener();

        _servicioA.Detener();
        _servicioB.Detener();
        _servicioC.Detener();

        // Las copias de la base no se dejan en la carpeta temporal. Los
        // servicios pueden tardar un momento en soltar sus archivos.
        for (int intento = 0; intento < 10 && Directory.Exists(_carpeta); intento++)
        {
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(
                    _carpeta,
                    true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(
                    1000);
            }
        }

        if (Directory.Exists(
                _carpeta))
        {
            Console.WriteLine(
                $"Aviso: no se pudo borrar {_carpeta}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{_superadas}/{_total} comprobaciones superadas.");

        return _superadas == _total
            ? 0
            : 1;
    }


    // ========================================================
    // UTILIDADES
    // ========================================================

    private static List<string> LeerTablas(
        string ruta)
    {
        var tablas =
            new List<string>();

        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'SCCosmos_%' ORDER BY name;";

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            tablas.Add(
                lector.GetString(0));
        }

        return tablas;
    }

    /// <summary>
    /// Alta inventada con el Id que reparte SQLite, como las de las apps.
    /// </summary>
    private static void InsertarDonacion(
        string ruta,
        string notas)
    {
        Escribir(
            ruta,
            $"INSERT INTO Donaciones (Fecha, NombrePersona, Cantidad, Notas) VALUES ('2030-01-01', 'Persona inventada', 1, '{notas}');");
    }

    /// <summary>
    /// Espera a que los tres dispositivos tengan esas altas. Si no, dice cuántas
    /// tiene cada uno y con qué Id.
    /// </summary>
    private static async Task<bool> EsperarAltasAsync(
        string patron,
        int cuantas)
    {
        string consulta =
            $"SELECT COUNT(*) FROM Donaciones WHERE Notas LIKE '{patron}';";

        bool correcto =
            await EsperarAsync(
                () => LeerEntero(_dbA, consulta) == cuantas
                      && LeerEntero(_dbB, consulta) == cuantas
                      && LeerEntero(_dbC, consulta) == cuantas,
                30);

        if (!correcto)
        {
            string detalle =
                $"SELECT group_concat(Id || '=' || Notas, ' ') FROM Donaciones WHERE Notas LIKE '{patron}';";

            Console.WriteLine(
                $"      (se esperaban {cuantas}. A: {LeerTexto(_dbA, detalle)} | B: {LeerTexto(_dbB, detalle)} | C: {LeerTexto(_dbC, detalle)})");
        }

        return correcto;
    }

    private static List<RscAsistenteAGuardar> AsistentesRsc()
    {
        static RscAsistenteAGuardar Asistente(int persona, string nombre, string arreglos, string sexo) =>
            new()
            {
                PersonaId = persona,
                Nombre = nombre,
                Asiste = true,
                SaleAPredicar = true,
                AceptaGrupo = true,
                ArregloConPersonaIds = arreglos,
                Sexo = sexo
            };

        return new List<RscAsistenteAGuardar>
        {
            Asistente(900001, "Ana Inventada", "900002", "M"),
            Asistente(900002, "Luis Inventado", "", "H"),
            Asistente(900003, "Mara Inventada", "", "M"),
            Asistente(900004, "Pepe Inventado", "", "H")
        };
    }

    private static List<RscGrupoAGuardar> GruposRsc()
    {
        return new List<RscGrupoAGuardar>
        {
            GrupoRsc(1, "T-1", (900001, "Ana Inventada"), (900002, "Luis Inventado")),
            GrupoRsc(2, "T-2", (900003, "Mara Inventada"), (900004, "Pepe Inventado"))
        };
    }

    private static RscGrupoAGuardar GrupoRsc(
        int numero,
        string territorio,
        params (int Persona, string Nombre)[] miembros)
    {
        return new RscGrupoAGuardar
        {
            Numero = numero,
            Territorio = territorio,
            Miembros = miembros
                .Select(
                    m => new RscMiembroAGuardar
                    {
                        PersonaId = m.Persona,
                        Nombre = m.Nombre
                    })
                .ToList()
        };
    }

    private static void GuardarRsc(
        string ruta,
        string reunion,
        List<RscAsistenteAGuardar> asistentes,
        List<RscGrupoAGuardar> grupos)
    {
        using var conexion =
            Abrir(
                ruta);

        using var transaccion =
            conexion.BeginTransaction();

        RscGuardado.GuardarAsistentes(
            conexion,
            transaccion,
            reunion,
            asistentes);

        RscGuardado.GuardarGrupos(
            conexion,
            transaccion,
            reunion,
            grupos);

        transaccion.Commit();
    }

    private static string ConsultaFilasRsc(
        string reunion)
    {
        return $"""
            SELECT (SELECT group_concat(v, ';') FROM (SELECT Id || ':' || PersonaId || ':' || Nombre || ':' || Asiste || ':' || SaleAPredicar || ':' || AceptaGrupo || ':' || ArregloConPersonaIds || ':' || Sexo || ':' || Notas AS v FROM RSC_Asistentes WHERE RscReunionId = '{reunion}' ORDER BY Id))
                || '|' || (SELECT group_concat(v, ';') FROM (SELECT Id || ':' || Numero || ':' || Notas || ':' || Territorio || ':' || Orden AS v FROM RSC_Grupos WHERE RscReunionId = '{reunion}' ORDER BY Id))
                || '|' || (SELECT group_concat(v, ';') FROM (SELECT Id || ':' || RscGrupoId || ':' || PersonaId || ':' || Nombre || ':' || Orden AS v FROM RSC_Miembros WHERE RscReunionId = '{reunion}' ORDER BY Id));
            """;
    }

    private static string ConsultaGruposRsc(
        string reunion)
    {
        return $"""
            SELECT group_concat(v, ';') FROM (
                SELECT g.Numero || '[' || g.Territorio || ']' || m.PersonaId || '#' || m.Orden AS v
                FROM RSC_Miembros m
                JOIN RSC_Grupos g ON g.Id = m.RscGrupoId
                WHERE m.RscReunionId = '{reunion}'
                ORDER BY g.Orden, m.Orden);
            """;
    }

    private static Dictionary<long, string> LeerVinculos(
        string ruta,
        string congregacion)
    {
        var vinculos =
            new Dictionary<long, string>();

        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            "SELECT Id, NombreVinculado FROM TerritoriosVisitaV3 WHERE CongregacionCodigo = $c;";

        cmd.Parameters.AddWithValue(
            "$c",
            congregacion);

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            vinculos[lector.GetInt64(0)] =
                lector.IsDBNull(1)
                    ? ""
                    : lector.GetString(1).Trim();
        }

        return vinculos;
    }

    private static void ImportarTerritorios(
        string ruta,
        string congregacion,
        List<TerritorioImportado> territorios)
    {
        using var conexion =
            Abrir(
                ruta);

        using var transaccion =
            conexion.BeginTransaction();

        TerritoriosImportados.Guardar(
            conexion,
            transaccion,
            congregacion,
            territorios);

        transaccion.Commit();
    }

    /// <summary>
    /// Filas de una tabla que no coinciden entre dos bases (en cualquiera de
    /// los dos sentidos).
    /// </summary>
    private static long FilasDistintas(
        string dbUna,
        string dbOtra,
        string tabla)
    {
        using var conexion =
            Abrir(
                dbUna);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            $"""
            ATTACH DATABASE $otra AS otra;
            SELECT (SELECT COUNT(*) FROM (SELECT * FROM main.{tabla} EXCEPT SELECT * FROM otra.{tabla}))
                 + (SELECT COUNT(*) FROM (SELECT * FROM otra.{tabla} EXCEPT SELECT * FROM main.{tabla}));
            """;

        cmd.Parameters.AddWithValue(
            "$otra",
            dbOtra);

        return Convert.ToInt64(
            cmd.ExecuteScalar());
    }

    /// <summary>
    /// Tamaño de un archivo; si en ese momento no se puede consultar (su dueño
    /// lo está reescribiendo), el mayor posible.
    /// </summary>
    private static long TamanoArchivo(
        string ruta)
    {
        try
        {
            return new FileInfo(
                    ruta)
                .Length;
        }
        catch (IOException)
        {
            return long.MaxValue;
        }
    }

    /// <summary>
    /// Líneas de un archivo del canal por carpeta, aunque su dueño lo esté
    /// escribiendo en ese momento (-1 si no se pudo leer).
    /// </summary>
    private static int ContarLineas(
        string ruta)
    {
        for (int intento = 0; intento < 10; intento++)
        {
            try
            {
                using var flujo =
                    new FileStream(
                        ruta,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                using var lector =
                    new StreamReader(
                        flujo);

                return lector
                    .ReadToEnd()
                    .Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Length;
            }
            catch (IOException)
            {
                Thread.Sleep(
                    100);
            }
        }

        return -1;
    }

    /// <summary>
    /// Deja a cada dispositivo al día con los demás: lo enviado hasta su
    /// último cambio y lo recibido hasta el último cambio del otro.
    /// </summary>
    private static void DarPorSincronizados(
        params (SincroAlmacen Almacen, string Id)[] dispositivos)
    {
        foreach (var (almacen, id) in dispositivos)
        {
            foreach (var otro in dispositivos)
            {
                if (otro.Id == id)
                {
                    continue;
                }

                almacen.GuardarUltimoEnviado(
                    otro.Id,
                    almacen.ObtenerUltimoSeq());

                almacen.GuardarUltimoRecibido(
                    otro.Id,
                    otro.Almacen.ObtenerUltimoSeq());
            }
        }
    }

    /// <summary>
    /// Copia coherente con la API de copia de SQLite: vale aunque la app
    /// tenga la base abierta (WAL) y nunca escribe en la base de origen.
    /// </summary>
    private static void CopiarBase(
        string origen,
        string destino)
    {
        using var lectura =
            new SqliteConnection(
                $"Data Source={origen};Mode=ReadOnly;Pooling=False");

        using var copia =
            new SqliteConnection(
                $"Data Source={destino};Pooling=False");

        lectura.Open();
        copia.Open();

        lectura.BackupDatabase(
            copia);
    }

    private static async Task Probar(
        string nombre,
        Func<Task<bool>> prueba)
    {
        // Para depurar: SINCRO_SOLO=10,11,12 ejecuta solo esas pruebas.
        string? solo =
            Environment.GetEnvironmentVariable(
                "SINCRO_SOLO");

        if (!string.IsNullOrWhiteSpace(
                solo)
            && !solo
                .Split(
                    ',')
                .Contains(
                    nombre.Split(
                        '.')[0]))
        {
            return;
        }

        _total++;

        bool correcto;

        try
        {
            correcto =
                await prueba();
        }
        catch (Exception ex)
        {
            correcto =
                false;

            Console.WriteLine(
                $"      excepción: {ex.Message}");
        }

        if (correcto)
        {
            _superadas++;
        }

        Console.WriteLine(
            $"{(correcto ? "[OK]   " : "[FALLO]")} {nombre}");
    }


    private static async Task<bool> EsperarAsync(
        Func<bool> condicion,
        int segundos = 15)
    {
        DateTime fin =
            DateTime.UtcNow
                .AddSeconds(
                    segundos);

        while (DateTime.UtcNow < fin)
        {
            if (condicion())
            {
                return true;
            }

            await Task.Delay(
                400);
        }

        return condicion();
    }


    private static SqliteConnection Abrir(
        string ruta)
    {
        var conexion =
            new SqliteConnection(
                $"Data Source={ruta};Pooling=False");

        conexion.Open();

        return conexion;
    }


    private static void Escribir(
        string ruta,
        string sql)
    {
        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            sql;

        cmd.ExecuteNonQuery();
    }


    private static long InsertarPersona(
        string ruta,
        string nombre)
    {
        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            "INSERT INTO Personas (Nombre) VALUES ($n); SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue(
            "$n",
            nombre);

        return Convert.ToInt64(
            cmd.ExecuteScalar()
            ?? 0L,
            CultureInfo.InvariantCulture);
    }


    private static string LeerTexto(
        string ruta,
        string sql)
    {
        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            sql;

        object? valor =
            cmd.ExecuteScalar();

        return valor is null
               ||
               valor == DBNull.Value
            ? ""
            : Convert.ToString(
                valor,
                CultureInfo.InvariantCulture)
              ?? "";
    }


    private static long LeerEntero(
        string ruta,
        string sql)
    {
        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            sql;

        return Convert.ToInt64(
            cmd.ExecuteScalar()
            ?? 0L,
            CultureInfo.InvariantCulture);
    }


    private static List<long> LeerLista(
        string ruta,
        string sql)
    {
        var lista =
            new List<long>();

        using var conexion =
            Abrir(
                ruta);

        using var cmd =
            conexion.CreateCommand();

        cmd.CommandText =
            sql;

        using var lector =
            cmd.ExecuteReader();

        while (lector.Read())
        {
            lista.Add(
                lector.GetInt64(0));
        }

        return lista;
    }
}
