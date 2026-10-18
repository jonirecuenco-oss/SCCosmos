using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;


// ============================================================================
// FILA PERSISTIDA DE LA MATRIZ
//
// Conservamos exactamente los nombres de propiedades de Windows para que
// "MatrizCicloVisitas" sea compatible entre ambas aplicaciones.
// ============================================================================

public sealed class FilaCiclo
    : INotifyPropertyChanged
{
    private string _v1 = "";
    private string _v2 = "";
    private string _v3 = "";
    private string _v4 = "";
    private string _v5 = "";
    private string _v6 = "";

    private bool _l1;
    private bool _l2;
    private bool _l3;
    private bool _l4;
    private bool _l5;
    private bool _l6;

    public int Orden
    { get; set; }

    public string Visita1
    {
        get => _v1;
        set => Establecer(
            ref _v1,
            value ?? "");
    }

    public string Visita2
    {
        get => _v2;
        set => Establecer(
            ref _v2,
            value ?? "");
    }

    public string Visita3
    {
        get => _v3;
        set => Establecer(
            ref _v3,
            value ?? "");
    }

    public string Visita4
    {
        get => _v4;
        set => Establecer(
            ref _v4,
            value ?? "");
    }

    public string Visita5
    {
        get => _v5;
        set => Establecer(
            ref _v5,
            value ?? "");
    }

    public string Visita6
    {
        get => _v6;
        set => Establecer(
            ref _v6,
            value ?? "");
    }

    public bool L1
    {
        get => _l1;
        set => Establecer(
            ref _l1,
            value);
    }

    public bool L2
    {
        get => _l2;
        set => Establecer(
            ref _l2,
            value);
    }

    public bool L3
    {
        get => _l3;
        set => Establecer(
            ref _l3,
            value);
    }

    public bool L4
    {
        get => _l4;
        set => Establecer(
            ref _l4,
            value);
    }

    public bool L5
    {
        get => _l5;
        set => Establecer(
            ref _l5,
            value);
    }

    public bool L6
    {
        get => _l6;
        set => Establecer(
            ref _l6,
            value);
    }

    public event PropertyChangedEventHandler?
        PropertyChanged;

    private void Establecer<T>(
        ref T campo,
        T valor,
        [CallerMemberName]
        string? propiedad = null)
    {
        if (EqualityComparer<T>
            .Default
            .Equals(
                campo,
                valor))
        {
            return;
        }

        campo =
            valor;

        PropertyChanged
            ?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propiedad));
    }
}


// ============================================================================
// PÁGINA
// ============================================================================

public partial class CicloVisitasPage
    : ContentPage
{
    private readonly ObservableCollection<string>
        _listaCongregaciones =
            new();

    private readonly ObservableCollection<FilaCiclo>
        _filasCiclo =
            new();

    private readonly ObservableCollection<CicloFilaVisual>
        _filasVisuales =
            new();

    private readonly SemaphoreSlim
        _semaforoGuardado =
            new(
                1,
                1);

    private CancellationTokenSource?
        _ctsIndicadorGuardado;

    private bool _cargando =
        true;

    private bool _inicializado;

    private int _visitaActual =
        1;


    public CicloVisitasPage()
    {
        InitializeComponent();

        ListaFilas.ItemsSource =
            _filasVisuales;

        ActualizarCabeceraVisita();
        ActualizarBotonesVisita();
    }


    protected override async void
        OnAppearing()
    {
        base.OnAppearing();

        if (_inicializado)
        {
            return;
        }

        _inicializado =
            true;

        await InicializarAsync();
    }


    // ========================================================================
    // INICIALIZACIÓN
    // ========================================================================

    private async Task InicializarAsync()
    {
        MostrarCarga(
            true,
            "Cargando ciclo de visitas...");

        try
        {
            DatosIniciales datos =
                await Task.Run(
                    PrepararDatosIniciales);

            _listaCongregaciones
                .Clear();

            foreach (
                string congregacion
                in datos.Congregaciones)
            {
                _listaCongregaciones
                    .Add(
                        congregacion);
            }

            _filasCiclo
                .Clear();

            foreach (
                FilaCiclo fila
                in datos.Filas)
            {
                SuscribirFila(
                    fila);

                _filasCiclo
                    .Add(
                        fila);
            }

            AsegurarFilasMinimas();

            // Igual que Windows:
            // al abrir la matriz se sincroniza silenciosamente
            // el historial real sobre las celdas que no estén bloqueadas.
            await Task.Run(
                SincronizarHistorialSilencioso);

            _cargando =
                false;

            RefrescarColumnaActual();

            LblEstado.Text =
                "Los cambios se guardan automáticamente.";
        }
        catch (Exception ex)
        {
            _cargando =
                false;

            LblEstado.Text =
                "No se pudo cargar la matriz.";

            await DisplayAlert(
                "Ciclo de visitas",
                "No se pudo cargar la matriz:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            MostrarCarga(
                false);
        }
    }


    private DatosIniciales
        PrepararDatosIniciales()
    {
        List<string> congregaciones =
            DatabaseHelper
                .ObtenerCongregaciones()
                .Select(
                    c =>
                        c.GetType()
                            .GetProperty("Nombre")
                            ?.GetValue(c)
                            ?.ToString()
                        ??
                        c.ToString()
                        ??
                        "")
                .Select(
                    x =>
                        x.Trim())
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x))
                .Distinct(
                    StringComparer.Ordinal)
                .ToList();

        List<FilaCiclo> filas =
            CargarMatrizDesdeDB();

        if (filas.Count == 0)
        {
            for (int i = 0;
                 i < congregaciones.Count;
                 i++)
            {
                filas.Add(
                    new FilaCiclo
                    {
                        Orden =
                            i + 1
                    });
            }
        }

        return
            new DatosIniciales
            {
                Congregaciones =
                    congregaciones,

                Filas =
                    filas
            };
    }


    private static List<FilaCiclo>
        CargarMatrizDesdeDB()
    {
        string jsonGuardado =
            DatabaseHelper
                .ObtenerConfiguracion(
                    "MatrizCicloVisitas",
                    "");

        if (string.IsNullOrWhiteSpace(
                jsonGuardado))
        {
            return new();
        }

        try
        {
            List<FilaCiclo>? guardadas =
                JsonSerializer
                    .Deserialize<
                        List<FilaCiclo>>(
                        jsonGuardado,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });

            if (guardadas is null)
            {
                return new();
            }

            foreach (
                FilaCiclo f
                in guardadas)
            {
                // Misma corrección que Windows:
                // un candado no puede quedar activo sobre
                // una celda vacía.
                if (f.L1 &&
                    string.IsNullOrEmpty(
                        f.Visita1))
                {
                    f.L1 = false;
                }

                if (f.L2 &&
                    string.IsNullOrEmpty(
                        f.Visita2))
                {
                    f.L2 = false;
                }

                if (f.L3 &&
                    string.IsNullOrEmpty(
                        f.Visita3))
                {
                    f.L3 = false;
                }

                if (f.L4 &&
                    string.IsNullOrEmpty(
                        f.Visita4))
                {
                    f.L4 = false;
                }

                if (f.L5 &&
                    string.IsNullOrEmpty(
                        f.Visita5))
                {
                    f.L5 = false;
                }

                if (f.L6 &&
                    string.IsNullOrEmpty(
                        f.Visita6))
                {
                    f.L6 = false;
                }
            }

            return guardadas;
        }
        catch
        {
            return new();
        }
    }


    private void AsegurarFilasMinimas()
    {
        int necesarias =
            _listaCongregaciones.Count;

        while (_filasCiclo.Count <
               necesarias)
        {
            var nueva =
                new FilaCiclo
                {
                    Orden =
                        _filasCiclo.Count
                        + 1
                };

            SuscribirFila(
                nueva);

            _filasCiclo
                .Add(
                    nueva);
        }
    }


    private void SuscribirFila(
        FilaCiclo fila)
    {
        fila.PropertyChanged -=
            Fila_PropertyChanged;

        fila.PropertyChanged +=
            Fila_PropertyChanged;
    }


    private void Fila_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        _ =
            GuardarSilenciosoAsync();
    }


    // ========================================================================
    // VISITA / RUEDA SELECCIONADA
    // ========================================================================

    private void BtnVisita_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            !int.TryParse(
                boton.CommandParameter
                    ?.ToString(),
                out int visita)
            ||
            visita < 1
            ||
            visita > 6)
        {
            return;
        }

        _visitaActual =
            visita;

        ActualizarCabeceraVisita();
        ActualizarBotonesVisita();
        RefrescarColumnaActual();
    }


    private void ActualizarCabeceraVisita()
    {
        LblVisitaActual.Text =
            $"VISITA {_visitaActual}";

        LblPeriodoActual.Text =
            _visitaActual is 1 or 3 or 5
                ? "Septiembre"
                : "Febrero / Marzo";
    }


    private void ActualizarBotonesVisita()
    {
        Button[] botones =
        [
            BtnVisita1,
            BtnVisita2,
            BtnVisita3,
            BtnVisita4,
            BtnVisita5,
            BtnVisita6
        ];

        for (int i = 0;
             i < botones.Length;
             i++)
        {
            bool activo =
                i + 1 ==
                _visitaActual;

            botones[i]
                .BackgroundColor =
                    Microsoft.Maui.Graphics.Color
                        .FromArgb(
                            activo
                                ? "#0B6FC2"
                                : "#242830");

            botones[i]
                .TextColor =
                    Microsoft.Maui.Graphics.Color
                        .FromArgb(
                            activo
                                ? "#FFFFFF"
                                : "#BFC5CC");
        }
    }


    private void RefrescarColumnaActual()
    {
        _filasVisuales
            .Clear();

        foreach (
            FilaCiclo fila
            in _filasCiclo)
        {
            _filasVisuales
                .Add(
                    new CicloFilaVisual(
                        fila,
                        _visitaActual,
                        _listaCongregaciones));
        }

        LblTotalFilas.Text =
            $"{_filasVisuales.Count} filas";
    }


    // ========================================================================
    // GUARDADO AUTOMÁTICO
    // ========================================================================

    private async Task GuardarSilenciosoAsync()
    {
        if (_cargando)
        {
            return;
        }

        try
        {
            await _semaforoGuardado
                .WaitAsync();

            try
            {
                string json =
                    JsonSerializer
                        .Serialize(
                            _filasCiclo
                                .ToList());

                await Task.Run(
                    () =>
                        DatabaseHelper
                            .GuardarConfiguracion(
                                "MatrizCicloVisitas",
                                json));
            }
            finally
            {
                _semaforoGuardado
                    .Release();
            }

            MostrarIndicadorGuardado();
        }
        catch
        {
            LblEstado.Text =
                "No se pudo guardar el último cambio.";
        }
    }


    private void MostrarIndicadorGuardado()
    {
        _ctsIndicadorGuardado
            ?.Cancel();

        _ctsIndicadorGuardado
            ?.Dispose();

        _ctsIndicadorGuardado =
            new CancellationTokenSource();

        CancellationToken token =
            _ctsIndicadorGuardado.Token;

        LblGuardado.IsVisible =
            true;

        _ =
            OcultarIndicadorGuardadoAsync(
                token);
    }


    private async Task
        OcultarIndicadorGuardadoAsync(
            CancellationToken token)
    {
        try
        {
            await Task.Delay(
                2000,
                token);

            if (!token
                .IsCancellationRequested)
            {
                LblGuardado.IsVisible =
                    false;
            }
        }
        catch (
            OperationCanceledException)
        {
        }
    }


    // ========================================================================
    // HISTORIAL REAL
    // ========================================================================

    private void
        SincronizarHistorialSilencioso()
    {
        if (_listaCongregaciones.Count ==
            0)
        {
            return;
        }

        var historialReal =
            new List<SemanaCalendario>();

        DateTime fechaBucle =
            new(
                2020,
                1,
                1);

        while (fechaBucle.DayOfWeek !=
               DayOfWeek.Tuesday)
        {
            fechaBucle =
                fechaBucle.AddDays(
                    1);
        }

        DateTime limite =
            DateTime.Today
                .AddDays(
                    365);

        while (fechaBucle <=
               limite)
        {
            string isoId =
                fechaBucle
                    .ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture);

            List<SemanaCalendario> semanas =
                DatabaseHelper
                    .ObtenerTodasSemanasPorFecha(
                        isoId);

            foreach (
                SemanaCalendario sem
                in semanas)
            {
                if (!string.IsNullOrEmpty(
                        sem.Congregacion)
                    &&
                    sem.Tipo is not null
                    &&
                    sem.Tipo.Contains(
                        "Visita",
                        StringComparison
                            .Ordinal)
                    &&
                    _listaCongregaciones
                        .Contains(
                            sem.Congregacion
                                .Trim()))
                {
                    historialReal.Add(
                        sem);
                }
            }

            fechaBucle =
                fechaBucle.AddDays(
                    7);
        }

        if (historialReal.Count ==
            0)
        {
            return;
        }

        _cargando =
            true;

        try
        {
            int tamañoRueda =
                _listaCongregaciones.Count;

            for (int i = 0;
                 i < historialReal.Count;
                 i++)
            {
                int numeroVisitaGlobal =
                    i;

                int ruedaActual =
                    (numeroVisitaGlobal /
                     tamañoRueda)
                    + 1;

                int posicionEnRueda =
                    numeroVisitaGlobal
                    %
                    tamañoRueda;

                if (ruedaActual > 6)
                {
                    break;
                }

                if (posicionEnRueda >=
                    _filasCiclo.Count)
                {
                    break;
                }

                string nombreCongregacion =
                    historialReal[i]
                        .Congregacion
                        .Trim();

                FilaCiclo fila =
                    _filasCiclo[
                        posicionEnRueda];

                switch (ruedaActual)
                {
                    case 1:
                        if (!fila.L1)
                        {
                            fila.Visita1 =
                                nombreCongregacion;
                        }
                        break;

                    case 2:
                        if (!fila.L2)
                        {
                            fila.Visita2 =
                                nombreCongregacion;
                        }
                        break;

                    case 3:
                        if (!fila.L3)
                        {
                            fila.Visita3 =
                                nombreCongregacion;
                        }
                        break;

                    case 4:
                        if (!fila.L4)
                        {
                            fila.Visita4 =
                                nombreCongregacion;
                        }
                        break;

                    case 5:
                        if (!fila.L5)
                        {
                            fila.Visita5 =
                                nombreCongregacion;
                        }
                        break;

                    case 6:
                        if (!fila.L6)
                        {
                            fila.Visita6 =
                                nombreCongregacion;
                        }
                        break;
                }
            }
        }
        finally
        {
            // El llamador decide cuándo volver a habilitar
            // el guardado automático.
        }
    }


    // ========================================================================
    // LIMPIAR FUTURAS
    // ========================================================================

    private async void BtnLimpiar_Clicked(
        object sender,
        EventArgs e)
    {
        bool confirmar =
            await DisplayAlert(
                "Limpiar matriz",
                "¿Seguro que deseas vaciar las celdas futuras no bloqueadas?",
                "Sí, limpiar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        MostrarCarga(
            true,
            "Limpiando matriz...");

        try
        {
            _cargando =
                true;

            foreach (
                FilaCiclo fila
                in _filasCiclo)
            {
                if (!fila.L1)
                {
                    fila.Visita1 = "";
                }

                if (!fila.L2)
                {
                    fila.Visita2 = "";
                }

                if (!fila.L3)
                {
                    fila.Visita3 = "";
                }

                if (!fila.L4)
                {
                    fila.Visita4 = "";
                }

                if (!fila.L5)
                {
                    fila.Visita5 = "";
                }

                if (!fila.L6)
                {
                    fila.Visita6 = "";
                }
            }

            await Task.Run(
                SincronizarHistorialSilencioso);

            _cargando =
                false;

            RefrescarColumnaActual();

            await GuardarSilenciosoAsync();
        }
        catch (Exception ex)
        {
            _cargando =
                false;

            await DisplayAlert(
                "Ciclo de visitas",
                "No se pudo limpiar la matriz:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            MostrarCarga(
                false);
        }
    }


    // ========================================================================
    // MOTOR DE RESOLUCIÓN (BACKTRACKING)
    //
    // Port directo de la lógica Windows:
    // - salto ideal de 3 posiciones;
    // - rebote en los extremos;
    // - tolerancia progresiva ante choques;
    // - máximo 2000 iteraciones por nivel.
    // ========================================================================

    private bool Backtrack(
        int idx,
        List<string> porAsignar,
        Dictionary<string, int>
            prevPositions,
        Dictionary<string, int>
            direcciones,
        string?[] asignaciones,
        int n,
        int tolerancia,
        ref int iteraciones)
    {
        iteraciones++;

        if (iteraciones > 2000)
        {
            return false;
        }

        if (idx ==
            porAsignar.Count)
        {
            return true;
        }

        string cong =
            porAsignar[idx];

        int prev =
            prevPositions
                .TryGetValue(
                    cong,
                    out int posicionPrevia)
                ? posicionPrevia
                : -1;

        int dir =
            direcciones
                .TryGetValue(
                    cong,
                    out int direccion)
                ? direccion
                : 1;

        int ideal =
            prev;

        if (prev != -1)
        {
            ideal =
                prev +
                (3 * dir);

            if (ideal >= n)
            {
                int rebote =
                    ideal -
                    (n - 1);

                ideal =
                    (n - 1)
                    -
                    rebote;
            }
            else if (ideal < 0)
            {
                ideal =
                    Math.Abs(
                        ideal);
            }
        }
        else
        {
            ideal =
                n / 2;
        }

        var slots =
            new List<int>();

        for (int i = 0;
             i < n;
             i++)
        {
            if (!string.IsNullOrEmpty(
                    asignaciones[i]))
            {
                continue;
            }

            if (prev != -1)
            {
                int dist =
                    Math.Abs(
                        i -
                        prev);

                bool valido =
                    tolerancia switch
                    {
                        0 =>
                            dist >= 2
                            &&
                            dist <= 4,

                        1 =>
                            dist >= 1
                            &&
                            dist <= 5,

                        2 =>
                            dist >= 1
                            &&
                            dist <= 6,

                        3 =>
                            dist >= 1,

                        _ =>
                            true
                    };

                if (valido)
                {
                    slots.Add(
                        i);
                }
            }
            else
            {
                slots.Add(
                    i);
            }
        }

        slots =
            slots
                .OrderBy(
                    s =>
                        Math.Abs(
                            s -
                            ideal))
                .ToList();

        foreach (
            int s
            in slots)
        {
            asignaciones[s] =
                cong;

            if (Backtrack(
                    idx + 1,
                    porAsignar,
                    prevPositions,
                    direcciones,
                    asignaciones,
                    n,
                    tolerancia,
                    ref iteraciones))
            {
                return true;
            }

            asignaciones[s] =
                null;
        }

        return false;
    }


    // ========================================================================
    // AUTO-COMPLETAR
    // ========================================================================

    private async void
        BtnAutoGenerar_Clicked(
            object sender,
            EventArgs e)
    {
        if (_listaCongregaciones.Count ==
            0)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Auto-configurar",
                "Se aplicará una rotación progresiva en acordeón. " +
                "Las congregaciones subirán y bajarán rebotando en los " +
                "extremos. En caso de choques se buscará el hueco libre " +
                "más cercano, priorizando saltos de 2 a 4 posiciones.\n\n" +
                "¿Continuar?",
                "Continuar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        MostrarCarga(
            true,
            "Generando rotación...");

        try
        {
            _cargando =
                true;

            await Task.Run(
                AutoGenerarMatriz);

            _cargando =
                false;

            RefrescarColumnaActual();

            await GuardarSilenciosoAsync();
        }
        catch (Exception ex)
        {
            _cargando =
                false;

            await DisplayAlert(
                "Ciclo de visitas",
                "No se pudo generar la rotación:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            MostrarCarga(
                false);
        }
    }


    private void AutoGenerarMatriz()
    {
        int n =
            _listaCongregaciones.Count;

        if (n == 0)
        {
            return;
        }

        // Limpiar celdas sin candado.
        foreach (
            FilaCiclo fila
            in _filasCiclo)
        {
            if (!fila.L1)
            {
                fila.Visita1 = "";
            }

            if (!fila.L2)
            {
                fila.Visita2 = "";
            }

            if (!fila.L3)
            {
                fila.Visita3 = "";
            }

            if (!fila.L4)
            {
                fila.Visita4 = "";
            }

            if (!fila.L5)
            {
                fila.Visita5 = "";
            }

            if (!fila.L6)
            {
                fila.Visita6 = "";
            }
        }

        SincronizarHistorialSilencioso();

        // VISITA 1: completar las congregaciones que falten.
        var usadasCol1 =
            new HashSet<string>(
                StringComparer.Ordinal);

        for (int r = 0;
             r < n;
             r++)
        {
            string val =
                _filasCiclo[r]
                    .Visita1;

            if (!string.IsNullOrEmpty(
                    val))
            {
                usadasCol1.Add(
                    val);
            }
        }

        var faltantesCol1 =
            new Queue<string>(
                _listaCongregaciones
                    .Where(
                        c =>
                            !usadasCol1
                                .Contains(
                                    c)));

        for (int r = 0;
             r < n;
             r++)
        {
            if (string.IsNullOrEmpty(
                    _filasCiclo[r]
                        .Visita1)
                &&
                faltantesCol1.Count >
                0)
            {
                _filasCiclo[r]
                    .Visita1 =
                        faltantesCol1
                            .Dequeue();
            }
        }

        // Dirección inicial:
        // mitad superior baja y mitad inferior sube.
        var direcciones =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        for (int r = 0;
             r < n;
             r++)
        {
            string cong =
                _filasCiclo[r]
                    .Visita1;

            if (!string.IsNullOrEmpty(
                    cong))
            {
                direcciones[cong] =
                    r < n / 2
                        ? 1
                        : -1;
            }
        }

        // VISITAS 2 A 6.
        for (int col = 2;
             col <= 6;
             col++)
        {
            var prevPositions =
                new Dictionary<
                    string,
                    int>(
                    StringComparer.Ordinal);

            var usadasEnCol =
                new HashSet<string>(
                    StringComparer.Ordinal);

            string?[] asignaciones =
                new string?[n];

            for (int r = 0;
                 r < n;
                 r++)
            {
                string valAnterior =
                    GetValorColumna(
                        _filasCiclo[r],
                        col - 1);

                if (!string.IsNullOrEmpty(
                        valAnterior))
                {
                    prevPositions[
                        valAnterior] =
                            r;
                }

                string valActual =
                    GetValorColumna(
                        _filasCiclo[r],
                        col);

                if (!string.IsNullOrEmpty(
                        valActual))
                {
                    asignaciones[r] =
                        valActual;

                    usadasEnCol.Add(
                        valActual);
                }
            }

            List<string> porAsignar =
                _listaCongregaciones
                    .Where(
                        c =>
                            !usadasEnCol
                                .Contains(
                                    c))
                    .ToList();

            // Primero las congregaciones que están cerca de un extremo.
            porAsignar =
                porAsignar
                    .OrderBy(
                        c =>
                        {
                            if (!prevPositions
                                .TryGetValue(
                                    c,
                                    out int p))
                            {
                                return 0;
                            }

                            return Math.Min(
                                p,
                                n - 1 - p);
                        })
                    .ToList();

            for (int tolerancia = 0;
                 tolerancia <= 4;
                 tolerancia++)
            {
                int iteraciones =
                    0;

                if (Backtrack(
                        0,
                        porAsignar,
                        prevPositions,
                        direcciones,
                        asignaciones,
                        n,
                        tolerancia,
                        ref iteraciones))
                {
                    break;
                }
            }

            for (int r = 0;
                 r < n;
                 r++)
            {
                string? cong =
                    asignaciones[r];

                if (cong is null)
                {
                    continue;
                }

                SetValorColumna(
                    _filasCiclo[r],
                    col,
                    cong);

                if (prevPositions
                    .TryGetValue(
                        cong,
                        out int prev))
                {
                    if (r > prev)
                    {
                        direcciones[cong] =
                            1;
                    }
                    else if (r < prev)
                    {
                        direcciones[cong] =
                            -1;
                    }
                }
            }
        }
    }


    // ========================================================================
    // ACCESO A COLUMNAS
    // ========================================================================

    private static string
        GetValorColumna(
            FilaCiclo fila,
            int col)
    {
        return col switch
        {
            1 => fila.Visita1,
            2 => fila.Visita2,
            3 => fila.Visita3,
            4 => fila.Visita4,
            5 => fila.Visita5,
            6 => fila.Visita6,
            _ => ""
        };
    }


    private static void
        SetValorColumna(
            FilaCiclo fila,
            int col,
            string valor)
    {
        switch (col)
        {
            case 1:
                fila.Visita1 =
                    valor;
                break;

            case 2:
                fila.Visita2 =
                    valor;
                break;

            case 3:
                fila.Visita3 =
                    valor;
                break;

            case 4:
                fila.Visita4 =
                    valor;
                break;

            case 5:
                fila.Visita5 =
                    valor;
                break;

            case 6:
                fila.Visita6 =
                    valor;
                break;
        }
    }


    // ========================================================================
    // CERRAR
    // ========================================================================

    private async void BtnCerrar_Clicked(
        object sender,
        EventArgs e)
    {
        // Aseguramos que el último cambio haya terminado
        // de escribirse antes de cerrar.
        await _semaforoGuardado
            .WaitAsync();

        _semaforoGuardado
            .Release();

        await Navigation
            .PopAsync();
    }


    // ========================================================================
    // OVERLAY
    // ========================================================================

    private void MostrarCarga(
        bool mostrar,
        string texto =
            "Cargando...")
    {
        LblCargando.Text =
            texto;

        OverlayCargando.IsVisible =
            mostrar;
    }


    // ========================================================================
    // DTO DE INICIALIZACIÓN
    // ========================================================================

    private sealed class DatosIniciales
    {
        public List<string>
            Congregaciones
        { get; set; } = new();

        public List<FilaCiclo>
            Filas
        { get; set; } = new();
    }
}


// ============================================================================
// FILA VISUAL DE LA COLUMNA ACTUAL
// ============================================================================

public sealed class CicloFilaVisual
    : INotifyPropertyChanged
{
    private readonly FilaCiclo
        _fila;

    private readonly int
        _columna;

    public int Orden =>
        _fila.Orden;

    public IReadOnlyList<string>
        Congregaciones
    { get; }


    public string ValorActual
    {
        get =>
            _columna switch
            {
                1 => _fila.Visita1,
                2 => _fila.Visita2,
                3 => _fila.Visita3,
                4 => _fila.Visita4,
                5 => _fila.Visita5,
                6 => _fila.Visita6,
                _ => ""
            };

        set
        {
            string nuevo =
                value ?? "";

            switch (_columna)
            {
                case 1:
                    _fila.Visita1 =
                        nuevo;
                    break;

                case 2:
                    _fila.Visita2 =
                        nuevo;
                    break;

                case 3:
                    _fila.Visita3 =
                        nuevo;
                    break;

                case 4:
                    _fila.Visita4 =
                        nuevo;
                    break;

                case 5:
                    _fila.Visita5 =
                        nuevo;
                    break;

                case 6:
                    _fila.Visita6 =
                        nuevo;
                    break;
            }

            PropertyChanged
                ?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(
                            ValorActual)));

            // Igual que Windows:
            // un bloqueo vacío no debe sobrevivir.
            if (string.IsNullOrEmpty(
                    nuevo)
                &&
                BloqueadoActual)
            {
                BloqueadoActual =
                    false;
            }
        }
    }


    public bool BloqueadoActual
    {
        get =>
            _columna switch
            {
                1 => _fila.L1,
                2 => _fila.L2,
                3 => _fila.L3,
                4 => _fila.L4,
                5 => _fila.L5,
                6 => _fila.L6,
                _ => false
            };

        set
        {
            // No permitimos bloquear una celda vacía.
            bool nuevo =
                !string.IsNullOrWhiteSpace(
                    ValorActual)
                &&
                value;

            switch (_columna)
            {
                case 1:
                    _fila.L1 =
                        nuevo;
                    break;

                case 2:
                    _fila.L2 =
                        nuevo;
                    break;

                case 3:
                    _fila.L3 =
                        nuevo;
                    break;

                case 4:
                    _fila.L4 =
                        nuevo;
                    break;

                case 5:
                    _fila.L5 =
                        nuevo;
                    break;

                case 6:
                    _fila.L6 =
                        nuevo;
                    break;
            }

            PropertyChanged
                ?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(
                            BloqueadoActual)));
        }
    }


    public event PropertyChangedEventHandler?
        PropertyChanged;


    public CicloFilaVisual(
        FilaCiclo fila,
        int columna,
        IReadOnlyList<string>
            congregaciones)
    {
        _fila =
            fila;

        _columna =
            columna;

        Congregaciones =
            congregaciones;
    }
}
