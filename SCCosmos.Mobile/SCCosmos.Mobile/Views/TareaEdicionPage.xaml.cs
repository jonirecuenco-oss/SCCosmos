using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

public partial class TareaEdicionPage : ContentPage
{
    private readonly Tarea _tarea;

    private readonly bool _esNueva;

    // «SC» o «Esposa», y el nombre del otro («Solo para Rebeca»).
    private readonly string _usuario =
        DatabaseHelper.UsuarioTareas();

    private readonly string _nombreConyuge =
        DatabaseHelper.NombreConyugeTareas();

    // Los mismos que en Windows (SincroCompartido/AvisosTarea.cs): hasta 1 mes antes.
    private static readonly string[] TextosAviso =
        SCCosmos.Sincro.AvisosTarea.Textos;

    private static readonly int[] MinutosAviso =
        SCCosmos.Sincro.AvisosTarea.Minutos;

    private static readonly string[] Repeticiones =
    {
        "Ninguna",
        "Diaria",
        "Semanal",
        "Mensual",
        "Anual"
    };


    /// <summary>Categoría con la que se abre una tarea nueva.</summary>
    public string CategoriaInicial { get; set; } = "Personal";

    /// <summary>Para quién es una tarea nueva (desde «Ver las tareas de …», para el cónyuge).</summary>
    public SCCosmos.Sincro.TareasConyuge.Para ParaInicial { get; set; } =
        SCCosmos.Sincro.TareasConyuge.Para.Mi;


    public TareaEdicionPage(
        Tarea? tarea)
    {
        InitializeComponent();

        _esNueva =
            tarea == null;

        // Las nuevas, a una hora (no de todo el día): la siguiente en punto.
        _tarea =
            tarea ?? new Tarea
            {
                FechaInicio =
                    DateTime.Today.ToString("yyyy-MM-dd"),
                TodoElDia = false,
                Hora =
                    new TimeSpan(Math.Min(DateTime.Now.Hour + 1, 23), 0, 0).ToString(@"hh\:mm"),
                CreadaPor =
                    _usuario
            };

        // La lista de siempre y, al final, «Personalizado…».
        PckAviso.ItemsSource =
            TextosAviso
                .Append(SCCosmos.Sincro.AvisosTarea.TextoPersonalizado)
                .ToList();

        PckAvisoUnidad.ItemsSource =
            SCCosmos.Sincro.AvisosTarea.TextosUnidades;

        PckPara.ItemsSource =
            SCCosmos.Sincro.TareasConyuge.TextosPara(_nombreConyuge);

        PckRepeticion.ItemsSource =
            Repeticiones;

        // Al tocar la fecha, la hora o un desplegable se quita el teclado,
        // que tapaba «Guardar» (que también está arriba, en la barra).
        foreach (View control in new View[] { PckCategoria, PckFecha, PckHora, PckFechaFin, PckAviso, PckAvisoUnidad, PckRepeticion, PckPara })
        {
            control.Focused += (_, _) => _ = OcultarTecladoAsync();
        }

        LblCabecera.Text =
            _esNueva
                ? "NUEVA TAREA"
                : "EDITAR TAREA";

        BtnEliminar.IsVisible =
            !_esNueva;

        // La esposa no ve la categoría: todo lo suyo es personal.
        if (!PerfilAccesoService.EsHombre)
        {
            PckCategoria.IsVisible = false;
        }

        CargarEnFormulario();

        // Para una tarea nueva se usa la pestaña desde la que se crea.
        Loaded += (s, e) =>
        {
            if (_esNueva)
            {
                SeleccionarCategoria(CategoriaInicial);

                PckPara.SelectedIndex =
                    (int)ParaInicial;
            }
        };
    }


    private void SeleccionarCategoria(string categoria)
    {
        string buscada = string.IsNullOrWhiteSpace(categoria)
            ? "Personal"
            : categoria.Trim();

        for (int i = 0; i < PckCategoria.Items.Count; i++)
        {
            if (string.Equals(
                    PckCategoria.Items[i],
                    buscada,
                    StringComparison.OrdinalIgnoreCase))
            {
                PckCategoria.SelectedIndex = i;
                return;
            }
        }

        PckCategoria.SelectedIndex = 0;
    }


    private string CategoriaSeleccionada() =>
        PckCategoria.SelectedItem as string ?? "Personal";


    private void CargarEnFormulario()
    {
        TxtTitulo.Text =
            _tarea.Titulo;

        TxtDescripcion.Text =
            _tarea.Descripcion;

        SeleccionarCategoria(_tarea.Categoria);

        if (DateTime.TryParse(
                _tarea.FechaInicio,
                out DateTime inicio))
        {
            PckFecha.Date =
                inicio.Date;
        }
        else
        {
            PckFecha.Date =
                DateTime.Today;
        }

        bool tieneRango =
            !string.IsNullOrWhiteSpace(
                _tarea.FechaFin);

        if (tieneRango
            && DateTime.TryParse(
                _tarea.FechaFin,
                out DateTime fin))
        {
            PckFechaFin.Date =
                fin.Date;
        }
        else
        {
            PckFechaFin.Date =
                PckFecha.Date ?? DateTime.Today;
        }

        SwRango.IsToggled =
            tieneRango;
        PanelRango.IsVisible =
            tieneRango;

        SwTodoElDia.IsToggled =
            _tarea.TodoElDia;
        PanelHora.IsVisible =
            !_tarea.TodoElDia;

        if (!_tarea.TodoElDia
            && TimeSpan.TryParse(
                _tarea.Hora,
                out TimeSpan hora))
        {
            PckHora.Time =
                hora;
        }

        int indiceAviso =
            Array.IndexOf(
                MinutosAviso,
                _tarea.AvisoMinutos);

        // Uno que no está en la lista: «Personalizado…», con su cantidad y unidad.
        if (indiceAviso < 0
            && _tarea.AvisoMinutos > 0)
        {
            var (cantidad, unidad) =
                SCCosmos.Sincro.AvisosTarea.Descomponer(
                    _tarea.AvisoMinutos);

            TxtAvisoCantidad.Text =
                cantidad.ToString();

            PckAvisoUnidad.SelectedIndex =
                unidad;

            indiceAviso =
                MinutosAviso.Length;
        }
        else
        {
            PckAvisoUnidad.SelectedIndex =
                0;
        }

        PckAviso.SelectedIndex =
            indiceAviso >= 0
                ? indiceAviso
                : 0;

        int indiceRepeticion =
            Array.IndexOf(
                Repeticiones,
                _tarea.Repeticion);

        PckRepeticion.SelectedIndex =
            indiceRepeticion >= 0
                ? indiceRepeticion
                : 0;

        PckPara.SelectedIndex =
            (int)SCCosmos.Sincro.TareasConyuge.ParaDe(
                _tarea.Compartida,
                _tarea.CreadaPor,
                _usuario);
    }


    private void PckAviso_SelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        PanelAvisoPersonalizado.IsVisible =
            PckAviso.SelectedIndex == MinutosAviso.Length;
    }


    // «Solo para Rebeca»: la tarea pasa a ser suya, y después solo se ve
    // aquí si ella deja ver sus tareas.
    private void PckPara_SelectedIndexChanged(
        object? sender,
        EventArgs e)
    {
        bool delConyuge =
            PckPara.SelectedIndex == (int)SCCosmos.Sincro.TareasConyuge.Para.Conyuge;

        LblPara.IsVisible =
            delConyuge;

        if (delConyuge)
        {
            LblPara.Text =
                DatabaseHelper.ConyugePermiteVerTareas()
                    ? $"Será de {_nombreConyuge}. La verás en «Ver las tareas de {_nombreConyuge}»."
                    : $"Será de {_nombreConyuge}: solo la verás si te deja ver sus tareas.";
        }
    }


    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            string titulo =
                (TxtTitulo.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(titulo))
            {
                await DisplayAlert(
                    "Tareas",
                    "Pon un título a la tarea o evento.",
                    "Aceptar");

                return;
            }

            Tarea tarea =
                _tarea;

            tarea.Titulo =
                titulo;

            tarea.Descripcion =
                (TxtDescripcion.Text ?? "").Trim();

            DateTime fechaInicio =
                PckFecha.Date ?? DateTime.Today;

            tarea.FechaInicio =
                fechaInicio.ToString("yyyy-MM-dd");

            tarea.TodoElDia =
                SwTodoElDia.IsToggled;

            if (!tarea.TodoElDia)
            {
                TimeSpan hora =
                    PckHora.Time ?? TimeSpan.Zero;

                tarea.Hora =
                    hora.ToString(@"hh\:mm");
            }
            else
            {
                tarea.Hora =
                    "";
            }

            if (SwRango.IsToggled)
            {
                DateTime fechaFin =
                    PckFechaFin.Date ?? fechaInicio;

                if (fechaFin.Date < fechaInicio.Date)
                {
                    await DisplayAlert(
                        "Tareas",
                        "La fecha final es anterior a la fecha inicial.",
                        "Aceptar");

                    return;
                }

                tarea.FechaFin =
                    fechaFin.ToString("yyyy-MM-dd");
            }
            else
            {
                tarea.FechaFin =
                    "";
            }

            int indiceAviso =
                PckAviso.SelectedIndex;

            if (indiceAviso == MinutosAviso.Length)
            {
                int minutos =
                    int.TryParse(
                        (TxtAvisoCantidad.Text ?? "").Trim(),
                        out int cantidad)
                        ? SCCosmos.Sincro.AvisosTarea.MinutosDe(
                            cantidad,
                            PckAvisoUnidad.SelectedIndex)
                        : -1;

                if (minutos < 0)
                {
                    await DisplayAlert(
                        "Tareas",
                        "El aviso personalizado tiene que ser de 1 minuto a 1 año antes.",
                        "Aceptar");

                    return;
                }

                tarea.AvisoMinutos =
                    minutos;
            }
            else
            {
                tarea.AvisoMinutos =
                    indiceAviso >= 0
                    && indiceAviso < MinutosAviso.Length
                        ? MinutosAviso[indiceAviso]
                        : -1;
            }

            int indiceRepeticion =
                PckRepeticion.SelectedIndex;

            tarea.Repeticion =
                indiceRepeticion >= 0
                && indiceRepeticion < Repeticiones.Length
                    ? Repeticiones[indiceRepeticion]
                    : "Ninguna";

            var para =
                PckPara.SelectedIndex >= 0
                    ? (SCCosmos.Sincro.TareasConyuge.Para)PckPara.SelectedIndex
                    : SCCosmos.Sincro.TareasConyuge.Para.Mi;

            (tarea.CreadaPor, tarea.Compartida) =
                SCCosmos.Sincro.TareasConyuge.Aplicar(
                    para,
                    _usuario,
                    tarea.CreadaPor);

            // La esposa no elige categoría: las suyas son personales, pero
            // las de él que edite (compartidas o vistas) conservan la suya.
            tarea.Categoria =
                PerfilAccesoService.EsHombre
                    ? CategoriaSeleccionada()
                    : _esNueva
                        ? "Personal"
                        : tarea.Categoria;

            // Y el widget al momento (antes, solo al salir de la app).
            await Task.Run(
                () =>
                {
                    DatabaseHelper.GuardarTareaMobile(
                        tarea);

                    SCCosmos.Mobile.Platforms.Android.SccosmosWidgetProvider.ActualizarTodos();
                });

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Tareas",
                "No se pudo guardar la tarea: " +
                ex.Message,
                "Aceptar");
        }
    }


    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_esNueva)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Eliminar tarea",
                "¿Seguro que quieres eliminar esta tarea o evento?",
                "Sí",
                "No");

        if (!confirmar)
        {
            return;
        }

        await Task.Run(
            () =>
            {
                DatabaseHelper.EliminarTareaMobile(
                    _tarea.Id);

                SCCosmos.Mobile.Platforms.Android.SccosmosWidgetProvider.ActualizarTodos();
            });

        await Navigation.PopAsync();
    }


    private void SwTodoElDia_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        PanelHora.IsVisible =
            !e.Value;

        _ = OcultarTecladoAsync();
    }


    private void SwRango_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        PanelRango.IsVisible =
            e.Value;

        _ = OcultarTecladoAsync();
    }


    // El modo de teclado de la ventana antes de abrir esta página.
    private global::Android.Views.SoftInput? _modoTeclado;


    // Mientras está abierta, el teclado encoge la página (no la tapa): así
    // «Guardar» y lo de abajo siguen a la vista. Al salir, como estaba.
    protected override void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            global::Android.Views.Window? ventana =
                Platform.CurrentActivity?.Window;

            if (ventana?.Attributes is global::Android.Views.WindowManagerLayoutParams atributos)
            {
                _modoTeclado ??= atributos.SoftInputMode;

                ventana.SetSoftInputMode(global::Android.Views.SoftInput.AdjustResize);
            }
        }
        catch
        {
        }
    }


    protected override void OnDisappearing()
    {
        try
        {
            if (_modoTeclado is global::Android.Views.SoftInput modo)
            {
                Platform.CurrentActivity?.Window?.SetSoftInputMode(modo);
            }
        }
        catch
        {
        }

        base.OnDisappearing();
    }


    // Fuera el teclado del título o la descripción.
    private async Task OcultarTecladoAsync()
    {
        try
        {
            foreach (InputView campo in new InputView[] { TxtTitulo, TxtDescripcion })
            {
                if (campo.IsSoftInputShowing())
                {
                    await campo.HideSoftInputAsync(CancellationToken.None);
                }

                campo.Unfocus();
            }
        }
        catch
        {
        }
    }
}
