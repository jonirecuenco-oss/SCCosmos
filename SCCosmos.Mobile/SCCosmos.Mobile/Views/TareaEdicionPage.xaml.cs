using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;

public partial class TareaEdicionPage : ContentPage
{
    private readonly Tarea _tarea;

    private readonly bool _esNueva;

    private static readonly string[] TextosAviso =
    {
        "Sin aviso",
        "A la hora",
        "10 minutos antes",
        "30 minutos antes",
        "1 hora antes",
        "1 día antes"
    };

    private static readonly int[] MinutosAviso =
    {
        -1,
        0,
        10,
        30,
        60,
        1440
    };

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


    public TareaEdicionPage(
        Tarea? tarea)
    {
        InitializeComponent();

        _esNueva =
            tarea == null;

        _tarea =
            tarea ?? new Tarea
            {
                FechaInicio =
                    DateTime.Today.ToString("yyyy-MM-dd"),
                CreadaPor =
                    PerfilAccesoService.EsHombre
                        ? "SC"
                        : "Esposa"
            };

        PckAviso.ItemsSource =
            TextosAviso;

        PckRepeticion.ItemsSource =
            Repeticiones;

        string nombreCompartir =
            PerfilAccesoService.EsHombre
                ? DatabaseHelper.ObtenerConfiguracion(
                    "NombreEsposaSC",
                    "mi esposa")
                : DatabaseHelper.ObtenerConfiguracion(
                    "NombreSC",
                    "el SC");

        LblCompartir.Text =
            "Compartir con " + nombreCompartir;

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

        SwCompartir.IsToggled =
            _tarea.Compartida;
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

            tarea.AvisoMinutos =
                indiceAviso >= 0
                && indiceAviso < MinutosAviso.Length
                    ? MinutosAviso[indiceAviso]
                    : -1;

            int indiceRepeticion =
                PckRepeticion.SelectedIndex;

            tarea.Repeticion =
                indiceRepeticion >= 0
                && indiceRepeticion < Repeticiones.Length
                    ? Repeticiones[indiceRepeticion]
                    : "Ninguna";

            tarea.Compartida =
                SwCompartir.IsToggled;

            tarea.Categoria =
                PerfilAccesoService.EsHombre
                    ? CategoriaSeleccionada()
                    : "Personal";

            if (string.IsNullOrWhiteSpace(tarea.CreadaPor))
            {
                tarea.CreadaPor =
                    PerfilAccesoService.EsHombre
                        ? "SC"
                        : "Esposa";
            }

            await Task.Run(
                () => DatabaseHelper.GuardarTareaMobile(
                    tarea));

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
            () => DatabaseHelper.EliminarTareaMobile(
                _tarea.Id));

        await Navigation.PopAsync();
    }


    private void SwTodoElDia_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        PanelHora.IsVisible =
            !e.Value;
    }


    private void SwRango_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        PanelRango.IsVisible =
            e.Value;
    }
}
