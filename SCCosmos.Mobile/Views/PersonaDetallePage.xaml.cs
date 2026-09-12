using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Views;

public partial class PersonaDetallePage : ContentPage
{
    private readonly int _personaId;

    private Persona? _persona;

    private List<Congregacion> _congregaciones =
        new();

    private string _fotoBase64 = "";

    private bool _cargandoControles;
    private bool _paginaCargada;

    public PersonaDetallePage(int personaId)
    {
        InitializeComponent();

        _personaId = personaId;

        PrepararPickers();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_paginaCargada)
            return;

        _paginaCargada = true;

        await CargarPersonaAsync();
    }

    private void PrepararPickers()
    {
        PickerSexo.ItemsSource =
            new List<string>
            {
                "(Sin especificar)",
                "Hombre",
                "Mujer"
            };

        PickerEsperanza.ItemsSource =
            new List<string>
            {
                "(Sin especificar)",
                "Otras ovejas",
                "Ungido"
            };

        PickerNombramiento.ItemsSource =
            new List<string>
            {
                "(Sin especificar)",
                "Publicador",
                "Siervo ministerial",
                "Anciano"
            };

        PickerServicio.ItemsSource =
            new List<string>
            {
                "(Sin especificar)",
                "Publicador",
                "Precursor regular",
                "Precursor especial",
                "Misionero"
            };
    }

    private async Task CargarPersonaAsync()
    {
        try
        {
            _cargandoControles = true;

            var resultado =
                await Task.Run(() =>
                {
                    Persona? persona =
                        DatabaseHelper
                            .ObtenerPersonaPorId(
                                _personaId);

                    List<Congregacion> congregaciones =
                        DatabaseHelper
                            .ObtenerCongregaciones();

                    return (
                        persona,
                        congregaciones);
                });

            _persona = resultado.persona;
            _congregaciones =
                resultado.congregaciones;

            if (_persona == null)
            {
                await DisplayAlert(
                    "Persona",
                    "No se encuentra esta persona en la base de datos.",
                    "Aceptar");

                await Navigation.PopAsync();
                return;
            }

            PickerCongregacion.ItemsSource =
                _congregaciones;

            CargarControles();

            await RefrescarConyugesAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Persona",
                $"No se pudo cargar la ficha:\n{ex.Message}",
                "Aceptar");
        }
        finally
        {
            _cargandoControles = false;
        }
    }

    private void CargarControles()
    {
        if (_persona == null)
            return;

        Title =
            _persona.NombreMostrarTabla;

        LblNombreCabecera.Text =
            _persona.NombreMostrarTabla;

        LblCongregacionCabecera.Text =
            _persona.CongregacionNombre;

        TxtNombre.Text =
            _persona.Nombre;

        TxtNombreComun.Text =
            _persona.NombreComun;

        TxtNombreJW.Text =
            _persona.NombreJWHub;

        TxtGrupo.Text =
            _persona.Grupo > 0
                ? _persona.Grupo.ToString()
                : "";

        TxtFechaNacimiento.Text =
            _persona.FechaNacimiento;

        TxtFechaBautismo.Text =
            _persona.FechaBautismo;

        TxtInicioPrec.Text =
            _persona.FechaInicioPrec;

        TxtUltimaEscuela.Text =
            _persona.FechaUltimaEscuela;

        TxtTelefono.Text =
            _persona.Telefono;

        TxtEmail.Text =
            _persona.Email;

        TxtEmailJW.Text =
            _persona.EmailJW;

        TxtDireccion.Text =
            _persona.Direccion;

        TxtPrivilegios.Text =
            _persona.Privilegios;

        TxtNotas.Text =
            _persona.Notas;

        SeleccionarTexto(
            PickerSexo,
            _persona.Sexo);

        SeleccionarTexto(
            PickerEsperanza,
            _persona.Esperanza);

        SeleccionarTexto(
            PickerNombramiento,
            _persona.Nombramiento);

        SeleccionarTexto(
            PickerServicio,
            _persona.Servicio);

        PickerCongregacion.SelectedItem =
            _congregaciones.FirstOrDefault(c =>
                string.Equals(
                    c.Codigo,
                    _persona.CongregacionCodigo,
                    StringComparison.OrdinalIgnoreCase));

        ChkA2.IsChecked =
            ContieneSolicitud(
                _persona.Solicitudes,
                "A-2");

        ChkA19.IsChecked =
            ContieneSolicitud(
                _persona.Solicitudes,
                "A-19");

        ChkA8.IsChecked =
            ContieneSolicitud(
                _persona.Solicitudes,
                "A-8");

        ChkG8.IsChecked =
            ContieneSolicitud(
                _persona.Solicitudes,
                "G-8");

        _fotoBase64 =
            _persona.Foto ?? "";

        RefrescarFoto();
    }

    private static bool ContieneSolicitud(
        string? solicitudes,
        string solicitud)
    {
        if (string.IsNullOrWhiteSpace(
                solicitudes))
        {
            return false;
        }

        return solicitudes
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Any(x =>
                string.Equals(
                    x,
                    solicitud,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static void SeleccionarTexto(
        Picker picker,
        string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            picker.SelectedIndex = 0;
            return;
        }

        for (int i = 0;
             i < picker.Items.Count;
             i++)
        {
            if (string.Equals(
                    picker.Items[i],
                    valor,
                    StringComparison.OrdinalIgnoreCase))
            {
                picker.SelectedIndex = i;
                return;
            }
        }

        picker.SelectedIndex = 0;
    }

    private static string ObtenerPicker(
        Picker picker)
    {
        string valor =
            picker.SelectedItem?.ToString()
            ?? "";

        return valor == "(Sin especificar)"
            ? ""
            : valor;
    }

    private async Task RefrescarConyugesAsync()
    {
        if (_persona == null)
            return;

        string codigo =
            (PickerCongregacion.SelectedItem
             as Congregacion)?.Codigo
            ?? _persona.CongregacionCodigo;

        if (string.IsNullOrWhiteSpace(codigo))
        {
            PickerConyuge.ItemsSource =
                new List<Persona>
                {
                    CrearNingunConyuge()
                };

            PickerConyuge.SelectedIndex = 0;
            return;
        }

        string sexo =
            ObtenerPicker(PickerSexo);

        string sexoOpuesto =
            sexo == "Hombre"
                ? "Mujer"
                : sexo == "Mujer"
                    ? "Hombre"
                    : "";

        List<Persona> candidatos =
            await Task.Run(() =>
                DatabaseHelper
                    .ObtenerPersonasPorCongregacion(
                        codigo));

        candidatos =
            candidatos
                .Where(p =>
                    p.Id != _persona.Id
                    &&
                    (string.IsNullOrWhiteSpace(
                         sexoOpuesto)
                     ||
                     string.Equals(
                         p.Sexo,
                         sexoOpuesto,
                         StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p =>
                    p.NombreMostrarTabla)
                .ToList();

        candidatos.Insert(
            0,
            CrearNingunConyuge());

        int conyugeId =
            _persona.ConyugeId;

        PickerConyuge.ItemsSource =
            candidatos;

        PickerConyuge.SelectedItem =
            candidatos.FirstOrDefault(p =>
                p.Id == conyugeId)
            ?? candidatos[0];
    }

    private static Persona CrearNingunConyuge()
    {
        return new Persona
        {
            Id = 0,
            Nombre = "-- Ninguno --",
            NombreComun = "-- Ninguno --"
        };
    }

    private async void PickerSexo_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (_cargandoControles)
            return;

        await RefrescarConyugesAsync();
    }

    private async void PickerCongregacion_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (_cargandoControles)
            return;

        await RefrescarConyugesAsync();
    }

    private void BtnEditar_Click(
        object sender,
        EventArgs e)
    {
        PanelFormulario.IsEnabled =
            true;

        PanelEdicion.IsVisible =
            true;

        PanelBotonesFoto.IsVisible =
            true;

        BtnEditar.IsEnabled =
            false;
    }

    private async void BtnCancelar_Click(
        object sender,
        EventArgs e)
    {
        bool descartar =
            await DisplayAlert(
                "Cancelar edición",
                "¿Quieres descartar los cambios realizados?",
                "Sí",
                "No");

        if (!descartar)
            return;

        _cargandoControles = true;

        try
        {
            _persona =
                await Task.Run(() =>
                    DatabaseHelper
                        .ObtenerPersonaPorId(
                            _personaId));

            if (_persona != null)
            {
                CargarControles();
                await RefrescarConyugesAsync();
            }
        }
        finally
        {
            _cargandoControles = false;
        }

        TerminarEdicion();
    }

    private async void BtnCambiarFoto_Click(
        object sender,
        EventArgs e)
    {
        try
        {
            var resultado =
                await FilePicker.Default.PickAsync(
                    new PickOptions
                    {
                        PickerTitle =
                            "Selecciona una fotografía",

                        FileTypes =
                            FilePickerFileType.Images
                    });

            if (resultado == null)
                return;

            using Stream stream =
                await resultado.OpenReadAsync();

            using var memoria =
                new MemoryStream();

            await stream.CopyToAsync(
                memoria);

            _fotoBase64 =
                Convert.ToBase64String(
                    memoria.ToArray());

            RefrescarFoto();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Fotografía",
                $"No se pudo cargar la fotografía:\n{ex.Message}",
                "Aceptar");
        }
    }

    private async void BtnBorrarFoto_Click(
        object sender,
        EventArgs e)
    {
        bool borrar =
            await DisplayAlert(
                "Borrar fotografía",
                "¿Quieres eliminar la fotografía de esta persona?",
                "Sí",
                "No");

        if (!borrar)
            return;

        _fotoBase64 = "";

        RefrescarFoto();
    }

    private void RefrescarFoto()
    {
        if (string.IsNullOrWhiteSpace(
                _fotoBase64))
        {
            ImgFoto.Source = null;
            LblSinFoto.IsVisible = true;

            return;
        }

        try
        {
            byte[] bytes =
                Convert.FromBase64String(
                    _fotoBase64);

            ImgFoto.Source =
                ImageSource.FromStream(
                    () => new MemoryStream(bytes));

            LblSinFoto.IsVisible = false;
        }
        catch
        {
            ImgFoto.Source = null;
            LblSinFoto.IsVisible = true;
        }
    }

    private async void BtnGuardar_Click(
        object sender,
        EventArgs e)
    {
        if (_persona == null)
            return;

        string nombre =
            TxtNombre.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(nombre))
        {
            await DisplayAlert(
                "Falta el nombre",
                "El nombre completo no puede quedar vacío.",
                "Aceptar");

            return;
        }

        _persona.Nombre =
            nombre;

        _persona.NombreComun =
            TxtNombreComun.Text?.Trim()
            ?? "";

        _persona.NombreJWHub =
            TxtNombreJW.Text?.Trim()
            ?? "";

        _persona.Sexo =
            ObtenerPicker(
                PickerSexo);

        _persona.Esperanza =
            ObtenerPicker(
                PickerEsperanza);

        _persona.Nombramiento =
            ObtenerPicker(
                PickerNombramiento);

        _persona.Servicio =
            ObtenerPicker(
                PickerServicio);

        _persona.CongregacionCodigo =
            (PickerCongregacion
             .SelectedItem
             as Congregacion)?.Codigo
            ?? "";

        _persona.ConyugeId =
            (PickerConyuge
             .SelectedItem
             as Persona)?.Id
            ?? 0;

        _persona.Telefono =
            TxtTelefono.Text?.Trim()
            ?? "";

        _persona.Email =
            TxtEmail.Text?.Trim()
            ?? "";

        _persona.EmailJW =
            TxtEmailJW.Text?.Trim()
            ?? "";

        _persona.Direccion =
            TxtDireccion.Text?.Trim()
            ?? "";

        _persona.FechaNacimiento =
            TxtFechaNacimiento.Text?.Trim()
            ?? "";

        _persona.FechaBautismo =
            TxtFechaBautismo.Text?.Trim()
            ?? "";

        _persona.FechaInicioPrec =
            TxtInicioPrec.Text?.Trim()
            ?? "";

        _persona.FechaUltimaEscuela =
            TxtUltimaEscuela.Text?.Trim()
            ?? "";

        _persona.Privilegios =
            TxtPrivilegios.Text?.Trim()
            ?? "";

        _persona.Notas =
            TxtNotas.Text?.Trim()
            ?? "";

        _persona.Foto =
            _fotoBase64;

        if (!int.TryParse(
                TxtGrupo.Text?.Trim(),
                out int grupo))
        {
            grupo = 0;
        }

        _persona.Grupo = grupo;

        var solicitudes =
            new List<string>();

        if (ChkA2.IsChecked)
            solicitudes.Add("A-2");

        if (ChkA19.IsChecked)
            solicitudes.Add("A-19");

        if (ChkA8.IsChecked)
            solicitudes.Add("A-8");

        if (ChkG8.IsChecked)
            solicitudes.Add("G-8");

        _persona.Solicitudes =
            string.Join(
                ", ",
                solicitudes);

        try
        {
            await Task.Run(() =>
                DatabaseHelper
                    .ActualizarPersona(
                        _persona));

            // Volvemos a consultar para obtener
            // también el nombre de congregación.
            _persona =
                await Task.Run(() =>
                    DatabaseHelper
                        .ObtenerPersonaPorId(
                            _personaId));

            TerminarEdicion();

            await DisplayAlert(
                "Guardado",
                "Los cambios se han guardado en la base de datos local del móvil.\n\nPara enviarlos al PC, usa Configuración → Guardar cambios en carpeta.",
                "Aceptar");

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Error al guardar",
                ex.Message,
                "Aceptar");
        }
    }

    private void TerminarEdicion()
    {
        PanelFormulario.IsEnabled =
            false;

        PanelEdicion.IsVisible =
            false;

        PanelBotonesFoto.IsVisible =
            false;

        BtnEditar.IsEnabled =
            true;
    }
}