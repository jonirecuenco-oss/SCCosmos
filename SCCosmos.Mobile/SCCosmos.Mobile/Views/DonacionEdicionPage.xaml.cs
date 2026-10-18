using System.Globalization;
using System.Text;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Alta y edición de una donación. Devuelve por
/// <see cref="EsperarResultadoAsync"/> si hubo cambios para que la lista se
/// refresque.
/// </summary>
public partial class DonacionEdicionPage : ContentPage
{
    private readonly DonacionMobile _donacion;

    private readonly bool _esNueva;

    private readonly TaskCompletionSource<bool> _resultado =
        new();

    private List<OpcionPersonaDonacion> _personas =
        new();

    private List<OpcionPersonaDonacion> _personasFiltradas =
        new();

    private OpcionPersonaDonacion? _personaSeleccionada;

    private List<OpcionCongregacionDonacion> _congregaciones =
        new();

    private bool _cargando;

    public DonacionEdicionPage(
        DonacionMobile? donacion)
    {
        InitializeComponent();

        _esNueva =
            donacion is null;

        _donacion =
            donacion
            ?? new DonacionMobile
            {
                Fecha =
                    DateTime.Now.ToString("yyyy-MM-dd"),
                MedioPago =
                    DatabaseHelper.MedioEfectivo
            };

        FechaDonacion.Date =
            DateTime.TryParse(
                _donacion.Fecha,
                out DateTime fecha)
                ? fecha
                : DateTime.Now;

        TxtCantidad.Text =
            _donacion.Cantidad > 0
                ? _donacion.Cantidad.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)
                    .Replace('.', ',')
                : "";

        TxtNotas.Text =
            _donacion.Notas ?? "";

        BtnEliminar.IsVisible =
            !_esNueva
            && _donacion.Id > 0;

        _ = CargarListasAsync();
    }

    public Task<bool> EsperarResultadoAsync() =>
        _resultado.Task;

    private async Task CargarListasAsync()
    {
        _cargando = true;

        try
        {
            List<Persona> personas =
                await Task.Run(
                    () =>
                        DatabaseHelper.ObtenerPersonas());

            List<Congregacion> congregaciones =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerCongregacionesCompletasMobile());

            _personas =
                new List<OpcionPersonaDonacion>
                {
                    new()
                    {
                        Id = 0,
                        Nombre = "",
                        Visual = "(Desconocido)"
                    }
                };

            foreach (
                Persona persona
                in personas
                    .Where(p => p is not null)
                    .OrderBy(p => p.NombreMostrarTabla))
            {
                _personas.Add(
                    new OpcionPersonaDonacion
                    {
                        Id = persona.Id,
                        Nombre = persona.NombreMostrarTabla,
                        Visual =
                            string.IsNullOrWhiteSpace(
                                persona.CongregacionNombre)
                                ? persona.NombreMostrarTabla
                                : $"{persona.NombreMostrarTabla} " +
                                  $"({persona.CongregacionNombre})",
                        CongregacionCodigo =
                            persona.CongregacionCodigo ?? "",
                        CongregacionNombre =
                            persona.CongregacionNombre ?? ""
                    });
            }

            _congregaciones =
                new List<OpcionCongregacionDonacion>
                {
                    new()
                    {
                        Codigo = "",
                        Visual = "(Sin congregación)"
                    }
                };

            foreach (
                Congregacion congre
                in congregaciones
                    .OrderBy(c => c.Nombre))
            {
                _congregaciones.Add(
                    new OpcionCongregacionDonacion
                    {
                        Codigo = congre.Codigo ?? "",
                        Visual = congre.Nombre ?? ""
                    });
            }

            AplicarFiltroPersonas();

            PickerCongregacion.ItemsSource =
                _congregaciones;

            PickerMedio.ItemsSource =
                new List<string>
                {
                    DatabaseHelper.MedioEfectivo,
                    DatabaseHelper.MedioBizumTransferencia
                };

            // ---- Selección actual ----
            _personaSeleccionada =
                _personas.FirstOrDefault(
                    p => p.Id == _donacion.PersonaId)
                ?? _personas[0];

            PickerPersona.SelectedItem =
                _personaSeleccionada;

            PickerCongregacion.SelectedItem =
                _congregaciones.FirstOrDefault(
                    c => c.Codigo == _donacion.CongregacionCodigo)
                ?? _congregaciones[0];

            PickerMedio.SelectedItem =
                (_donacion.MedioPago ==
                    DatabaseHelper.MedioBizumTransferencia)
                    ? DatabaseHelper.MedioBizumTransferencia
                    : DatabaseHelper.MedioEfectivo;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Registro",
                "No se pudieron cargar los datos: " +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            _cargando = false;
        }
    }

    private void TxtBuscarPersona_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        AplicarFiltroPersonas();
    }


    /// <summary>
    /// Filtra la lista de personas con el texto del buscador. La opción
    /// "(Desconocido)" sigue siempre disponible.
    /// </summary>
    private void AplicarFiltroPersonas()
    {
        string texto =
            NormalizarBusqueda(
                TxtBuscarPersona.Text);

        _personasFiltradas =
            string.IsNullOrWhiteSpace(texto)
                ? new List<OpcionPersonaDonacion>(
                    _personas)
                : _personas
                    .Where(
                        p => p.Id <= 0
                             || NormalizarBusqueda(
                                     p.Visual)
                                 .Contains(texto))
                    .ToList();

        PickerPersona.ItemsSource =
            _personasFiltradas;

        if (_personaSeleccionada is not null
            &&
            _personasFiltradas.Contains(
                _personaSeleccionada))
        {
            PickerPersona.SelectedItem =
                _personaSeleccionada;
        }

        LblCoincidencias.Text =
            string.IsNullOrWhiteSpace(texto)
                ? ""
                : _personasFiltradas
                        .Count(p => p.Id > 0)
                        .ToString() +
                  " personas encontradas";
    }


    private void PickerPersona_Changed(
        object sender,
        EventArgs e)
    {
        // La congregación se rellena automáticamente con la de la persona.
        if (_cargando
            ||
            PickerPersona.SelectedItem
                is not OpcionPersonaDonacion persona)
        {
            return;
        }

        _personaSeleccionada =
            persona;

        if (persona.Id <= 0)
        {
            return;
        }

        PickerCongregacion.SelectedItem =
            _congregaciones.FirstOrDefault(
                c => c.Codigo == persona.CongregacionCodigo)
            ?? _congregaciones[0];
    }

    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        if (!IntentarLeerCantidad(
                TxtCantidad.Text,
                out double cantidad))
        {
            await DisplayAlert(
                "Registro",
                "Escribe una cantidad válida, por ejemplo 25,50.",
                "Aceptar");

            return;
        }

        if (cantidad <= 0)
        {
            await DisplayAlert(
                "Registro",
                "La cantidad debe ser mayor que cero.",
                "Aceptar");

            return;
        }

        try
        {
            var persona =
                _personaSeleccionada
                ?? PickerPersona.SelectedItem
                    as OpcionPersonaDonacion;

            var congregacion =
                PickerCongregacion.SelectedItem
                    as OpcionCongregacionDonacion;

            _donacion.Fecha =
                (FechaDonacion.Date ?? DateTime.Now)
                    .ToString("yyyy-MM-dd");

            _donacion.PersonaId =
                persona?.Id ?? 0;

            _donacion.NombrePersona =
                persona is null || persona.Id <= 0
                    ? DatabaseHelper.DonanteDesconocido
                    : persona.Nombre;

            _donacion.CongregacionCodigo =
                congregacion?.Codigo ?? "";

            _donacion.CongregacionNombre =
                string.IsNullOrWhiteSpace(
                    congregacion?.Codigo)
                    ? ""
                    : congregacion?.Visual ?? "";

            _donacion.Cantidad =
                cantidad;

            _donacion.MedioPago =
                PickerMedio.SelectedItem as string
                ?? DatabaseHelper.MedioEfectivo;

            _donacion.Notas =
                (TxtNotas.Text ?? "").Trim();

            await Task.Run(
                () =>
                    DatabaseHelper.GuardarDonacionMobile(
                        _donacion));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Registro",
                "No se pudo guardar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnEliminar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_donacion.Id <= 0)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Eliminar donación",
                $"¿Eliminar la donación de " +
                $"{_donacion.PersonaVisual} del " +
                $"{_donacion.FechaVisual} " +
                $"({_donacion.CantidadVisual})?",
                "Sí",
                "No");

        if (!confirmar)
        {
            return;
        }

        try
        {
            await Task.Run(
                () =>
                    DatabaseHelper.EliminarDonacionMobile(
                        _donacion.Id));

            _resultado.TrySetResult(
                true);

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Registro",
                "No se pudo eliminar: " +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        _resultado.TrySetResult(
            false);

        await Navigation.PopAsync();
    }


    /// <summary>
    /// Normaliza texto para buscar: sin tildes y en minúsculas.
    /// </summary>
    private static string NormalizarBusqueda(
        string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "";
        }

        string descompuesto =
            texto.Normalize(
                NormalizationForm.FormD);

        var limpias =
            new StringBuilder();

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(
                    caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                limpias.Append(
                    caracter);
            }
        }

        return limpias
            .ToString()
            .Normalize(
                NormalizationForm.FormC)
            .ToLowerInvariant();
    }


    /// <summary>
    /// Lee la cantidad escrita admitiendo coma o punto como separador
    /// decimal y también separadores de millar (1.500,25 / 1,500.25).
    /// </summary>
    private static bool IntentarLeerCantidad(
        string? texto,
        out double cantidad)
    {
        cantidad = 0;

        string limpio =
            (texto ?? "")
                .Replace("€", "")
                .Replace(" ", "")
                .Trim();

        if (string.IsNullOrWhiteSpace(limpio))
        {
            return false;
        }

        int ultimaComa =
            limpio.LastIndexOf(',');

        int ultimoPunto =
            limpio.LastIndexOf('.');

        string normalizado;

        if (ultimaComa >= 0
            &&
            ultimoPunto >= 0)
        {
            // El último separador es el decimal; el otro son millares.
            char decimalSeparador =
                ultimaComa > ultimoPunto
                    ? ','
                    : '.';

            char milesSeparador =
                decimalSeparador == ','
                    ? '.'
                    : ',';

            normalizado =
                limpio
                    .Replace(milesSeparador.ToString(), "")
                    .Replace(decimalSeparador, '.');
        }
        else if (ultimaComa >= 0
                 ||
                 ultimoPunto >= 0)
        {
            char separador =
                ultimaComa >= 0
                    ? ','
                    : '.';

            string[] partes =
                limpio.Split(separador);

            if (partes.Length == 2
                &&
                partes[1].Length > 0
                &&
                partes[1].Length <= 2)
            {
                // 30,50 o 30.50 -> decimal
                normalizado =
                    limpio.Replace(separador, '.');
            }
            else
            {
                // 1.500 -> millares
                normalizado =
                    limpio.Replace(separador.ToString(), "");
            }
        }
        else
        {
            normalizado = limpio;
        }

        return double.TryParse(
            normalizado,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out cantidad);
    }
}


public sealed class OpcionPersonaDonacion
{
    public int Id { get; set; }

    public string Nombre { get; set; } = "";

    public string Visual { get; set; } = "";

    public string CongregacionCodigo { get; set; } = "";

    public string CongregacionNombre { get; set; } = "";
}


public sealed class OpcionCongregacionDonacion
{
    public string Codigo { get; set; } = "";

    public string Visual { get; set; } = "";
}
