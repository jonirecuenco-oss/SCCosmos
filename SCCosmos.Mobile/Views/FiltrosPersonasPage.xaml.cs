using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

public partial class FiltrosPersonasPage : ContentPage
{
    private readonly Action<FiltrosPersonas> _alAplicar;
    private readonly FiltrosPersonas _filtros;

    private readonly Dictionary<string, CheckBox>
        _checksCongregaciones = new();

    private readonly Dictionary<string, CheckBox>
        _checksServicios = new();

    private readonly Dictionary<string, CheckBox>
        _checksSolicitudes = new();

    private readonly Dictionary<string, CheckBox>
        _checksPrivilegios = new();

    private static readonly List<string> Servicios =
    [
        "Precursor regular",
        "Precursor especial",
        "Misionero"
    ];

    private static readonly List<string> Solicitudes =
    [
        "A-2",
        "A-19",
        "A-8",
        "G-8"
    ];

    private static readonly List<string> Privilegios =
    [
        "Instructor de la Escuela del Servicio de Precursor",
        "Superintendente de circuito sustituto",
        "Miembro del CEH",
        "Superintendente de asamblea",
        "Superintendente auxiliar de asamblea",
        "Coordinador",
        "Secretario",
        "Miembro del GVP",
        "Persona que pertenece a una entidad del LDC"
    ];

    public FiltrosPersonasPage(
        List<string> congregaciones,
        FiltrosPersonas filtrosActuales,
        Action<FiltrosPersonas> alAplicar)
    {
        InitializeComponent();

        _alAplicar = alAplicar;
        _filtros = filtrosActuales.Clonar();

        CargarPickers();

        CrearChecks(
            PanelCongregaciones,
            congregaciones,
            _filtros.Congregaciones,
            _checksCongregaciones);

        CrearChecks(
            PanelServicios,
            Servicios,
            _filtros.Servicios,
            _checksServicios);

        CrearChecks(
            PanelSolicitudes,
            Solicitudes,
            _filtros.Solicitudes,
            _checksSolicitudes);

        CrearChecks(
            PanelPrivilegios,
            Privilegios,
            _filtros.Privilegios,
            _checksPrivilegios);
    }

    private void CargarPickers()
    {
        PickerNombramiento.ItemsSource =
            new List<string>
            {
                "Todos",
                "Anciano",
                "Siervo ministerial"
            };

        PickerSexo.ItemsSource =
            new List<string>
            {
                "Todos",
                "Hombre",
                "Mujer"
            };

        PickerEsperanza.ItemsSource =
            new List<string>
            {
                "Todas",
                "Otras ovejas",
                "Ungido"
            };

        PickerNombramiento.SelectedIndex =
            BuscarIndice(
                (List<string>)PickerNombramiento.ItemsSource,
                _filtros.Nombramiento,
                0);

        PickerSexo.SelectedIndex =
            BuscarIndice(
                (List<string>)PickerSexo.ItemsSource,
                _filtros.Sexo,
                0);

        PickerEsperanza.SelectedIndex =
            BuscarIndice(
                (List<string>)PickerEsperanza.ItemsSource,
                _filtros.Esperanza,
                0);
    }

    private static int BuscarIndice(
        List<string> opciones,
        string valor,
        int indicePorDefecto)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return indicePorDefecto;

        int indice =
            opciones.FindIndex(x =>
                string.Equals(
                    x,
                    valor,
                    StringComparison.OrdinalIgnoreCase));

        return indice >= 0
            ? indice
            : indicePorDefecto;
    }

    private static void CrearChecks(
        VerticalStackLayout panel,
        IEnumerable<string> opciones,
        IEnumerable<string> seleccionadas,
        Dictionary<string, CheckBox> destino)
    {
        var seleccion =
            new HashSet<string>(
                seleccionadas,
                StringComparer.OrdinalIgnoreCase);

        foreach (string texto in opciones)
        {
            var check =
                new CheckBox
                {
                    IsChecked =
                        seleccion.Contains(texto),

                    Color =
                        Color.FromArgb("#4BA3E3"),

                    VerticalOptions =
                        LayoutOptions.Center
                };

            var label =
                new Label
                {
                    Text = texto,
                    TextColor = Colors.White,
                    FontSize = 14,
                    VerticalOptions =
                        LayoutOptions.Center
                };

            var fila =
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition
                        {
                            Width =
                                GridLength.Auto
                        },

                        new ColumnDefinition
                        {
                            Width =
                                GridLength.Star
                        }
                    },

                    Padding =
                        new Thickness(0, 3)
                };

            fila.Add(check, 0, 0);
            fila.Add(label, 1, 0);

            panel.Children.Add(fila);

            destino[texto] = check;
        }
    }

    private async void BtnCancelar_Click(
        object sender,
        EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private void BtnLimpiar_Click(
        object sender,
        EventArgs e)
    {
        PickerNombramiento.SelectedIndex = 0;
        PickerSexo.SelectedIndex = 0;
        PickerEsperanza.SelectedIndex = 0;

        DesmarcarTodos(_checksCongregaciones);
        DesmarcarTodos(_checksServicios);
        DesmarcarTodos(_checksSolicitudes);
        DesmarcarTodos(_checksPrivilegios);
    }

    private static void DesmarcarTodos(
        Dictionary<string, CheckBox> checks)
    {
        foreach (CheckBox check in checks.Values)
        {
            check.IsChecked = false;
        }
    }

    private async void BtnAplicar_Click(
        object sender,
        EventArgs e)
    {
        var resultado =
            new FiltrosPersonas
            {
                Nombramiento =
                    ObtenerValorPicker(
                        PickerNombramiento),

                Sexo =
                    ObtenerValorPicker(
                        PickerSexo),

                Esperanza =
                    ObtenerValorPicker(
                        PickerEsperanza),

                Congregaciones =
                    ObtenerSeleccionados(
                        _checksCongregaciones),

                Servicios =
                    ObtenerSeleccionados(
                        _checksServicios),

                Solicitudes =
                    ObtenerSeleccionados(
                        _checksSolicitudes),

                Privilegios =
                    ObtenerSeleccionados(
                        _checksPrivilegios)
            };

        _alAplicar(resultado);

        await Navigation.PopModalAsync();
    }

    private static string ObtenerValorPicker(
        Picker picker)
    {
        string valor =
            picker.SelectedItem?.ToString()
            ?? "";

        if (valor == "Todos" ||
            valor == "Todas")
        {
            return "";
        }

        return valor;
    }

    private static List<string>
        ObtenerSeleccionados(
            Dictionary<string, CheckBox> checks)
    {
        return checks
            .Where(x => x.Value.IsChecked)
            .Select(x => x.Key)
            .ToList();
    }
}