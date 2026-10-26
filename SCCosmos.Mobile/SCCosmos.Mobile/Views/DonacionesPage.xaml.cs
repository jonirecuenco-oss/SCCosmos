using System.Globalization;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Donaciones recibidas: registro por persona (con la congregación rellenada
/// automáticamente), fecha, cantidad y medio de pago (efectivo o
/// bizum/transferencia), con donantes desconocidos y búsquedas por año, mes,
/// congregación, persona y medio. El total se calcula sobre lo que se ve.
/// </summary>
public partial class DonacionesPage : ContentPage, IActualizableMobile
{
    private List<DonacionMobile> _donaciones =
        new();

    private List<OpcionCongregacionDonacion> _congregaciones =
        new();

    private bool _cargando = true;

    public DonacionesPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        await CargarAsync();
        Services.Perf.Registrar(
            "Rendimientos",
            reloj.ElapsedMilliseconds);
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarAsync();
        }
        catch
        {
        }
    }


    private async Task CargarAsync()
    {
        try
        {
            _cargando = true;

            await Task.Run(
                () =>
                    DatabaseHelper
                        .AsegurarTablaDonacionesMobile());

            if (_congregaciones.Count == 0)
            {
                List<Congregacion> congregaciones =
                    await Task.Run(
                        () =>
                            DatabaseHelper
                                .ObtenerCongregacionesCompletasMobile());

                _congregaciones =
                    new List<OpcionCongregacionDonacion>
                    {
                        new()
                        {
                            Codigo = "",
                            Visual = "(Todas)"
                        }
                    };

                foreach (
                    Congregacion congre
                    in congregaciones.OrderBy(c => c.Nombre))
                {
                    _congregaciones.Add(
                        new OpcionCongregacionDonacion
                        {
                            Codigo = congre.Codigo ?? "",
                            Visual = congre.Nombre ?? ""
                        });
                }

                PickerCongregacion.ItemsSource =
                    _congregaciones;

                PickerCongregacion.SelectedIndex = 0;

                PickerMedio.ItemsSource =
                    new List<string>
                    {
                        "(Todos)",
                        DatabaseHelper.MedioEfectivo,
                        DatabaseHelper.MedioBizumTransferencia
                    };

                PickerMedio.SelectedIndex = 0;

                PickerAnio.ItemsSource =
                    CrearListaAnios();

                PickerAnio.SelectedIndex = 0;

                PickerMes.ItemsSource =
                    CrearListaMeses();

                PickerMes.SelectedIndex = 0;
            }

            _cargando = false;

            await RecargarAsync();
        }
        catch (Exception ex)
        {
            _cargando = false;

            await DisplayAlert(
                "Rendimientos",
                "No se pudieron cargar los registros: " +
                ex.Message,
                "Aceptar");
        }
    }

    private List<string> CrearListaAnios()
    {
        var anios =
            new List<string> { "(Todos)" };

        var añosConDatos =
            DatabaseHelper
                .ObtenerDonacionesMobile()
                .Select(d =>
                    (d.Fecha ?? "").Length >= 4
                        ? d.Fecha.Substring(0, 4)
                        : "")
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct()
                .OrderByDescending(a => a)
                .ToList();

        string anioActual =
            DateTime.Now.Year.ToString();

        if (!añosConDatos.Contains(anioActual))
        {
            añosConDatos.Insert(0, anioActual);
        }

        anios.AddRange(añosConDatos);

        return anios;
    }

    private static List<string> CrearListaMeses()
    {
        var meses =
            new List<string> { "(Todos los meses)" };

        for (int mes = 1;
             mes <= 12;
             mes++)
        {
            meses.Add(
                new DateTime(2000, mes, 1)
                    .ToString(
                        "MMMM",
                        CultureInfo.GetCultureInfo("es-ES")));
        }

        return meses;
    }

    private async Task RecargarAsync()
    {
        string anio =
            PickerAnio.SelectedIndex > 0
                ? PickerAnio.SelectedItem as string ?? ""
                : "";

        string mes =
            PickerMes.SelectedIndex > 0
                ? PickerMes.SelectedIndex.ToString("00")
                : "";

        string congregacion =
            (PickerCongregacion.SelectedItem
                as OpcionCongregacionDonacion)
            ?.Codigo ?? "";

        string persona =
            TxtPersona.Text ?? "";

        string medio =
            PickerMedio.SelectedIndex > 0
                ? PickerMedio.SelectedItem as string ?? ""
                : "";

        try
        {
            _donaciones =
                await Task.Run(
                    () =>
                        DatabaseHelper.ObtenerDonacionesMobile(
                            anio,
                            mes,
                            congregacion,
                            persona,
                            medio));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Rendimientos",
                "No se pudo consultar: " + ex.Message,
                "Aceptar");

            return;
        }

        Lista.ItemsSource =
            _donaciones;

        var cultura =
            CultureInfo.GetCultureInfo("es-ES");

        double total =
            _donaciones.Sum(d => d.Cantidad);

        double efectivo =
            _donaciones
                .Where(d => d.MedioPago ==
                            DatabaseHelper.MedioEfectivo)
                .Sum(d => d.Cantidad);

        double bizum =
            total - efectivo;

        LblTotal.Text =
            $"Total: {total.ToString("N2", cultura)} €";

        LblDesglose.Text =
            $"{_donaciones.Count} registros · " +
            $"efectivo: {efectivo.ToString("N2", cultura)} € · " +
            $"bizum/transferencia: {bizum.ToString("N2", cultura)} €";
    }

    private async void Filtro_Changed(
        object sender,
        EventArgs e)
    {
        if (!_cargando
            &&
            PickerAnio.SelectedIndex >= 0
            &&
            PickerMes.SelectedIndex >= 0)
        {
            await RecargarAsync();
        }
    }

    private async void TxtPersona_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!_cargando
            &&
            PickerAnio.SelectedIndex >= 0)
        {
            await RecargarAsync();
        }
    }

    private async void BtnLimpiar_Clicked(
        object sender,
        EventArgs e)
    {
        _cargando = true;

        PickerAnio.SelectedIndex = 0;
        PickerMes.SelectedIndex = 0;
        PickerCongregacion.SelectedIndex = 0;
        PickerMedio.SelectedIndex = 0;
        TxtPersona.Text = "";

        _cargando = false;

        await RecargarAsync();
    }

    private async void BtnNueva_Clicked(
        object sender,
        EventArgs e)
    {
        await AbrirEditorAsync(
            null);
    }

    private async void Donacion_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not
            DonacionMobile donacion)
        {
            return;
        }

        await AbrirEditorAsync(
            donacion);
    }

    private async Task AbrirEditorAsync(
        DonacionMobile? donacion)
    {
        var editor =
            new DonacionEdicionPage(
                donacion);

        // Se abre como página normal (no modal): en Android las páginas
        // modales desplazan el área táctil y los botones inferiores no
        // reciben los toques.
        await Navigation.PushAsync(
            editor);

        bool cambiado =
            await editor.EsperarResultadoAsync();

        if (cambiado)
        {
            // Puede haber aparecido un año nuevo en el filtro.
            string anioSeleccionado =
                PickerAnio.SelectedItem as string
                ?? "(Todos)";

            _cargando = true;

            PickerAnio.ItemsSource =
                CrearListaAnios();

            PickerAnio.SelectedItem =
                anioSeleccionado;

            _cargando = false;

            await RecargarAsync();
        }
    }
}
