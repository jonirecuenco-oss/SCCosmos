using System.Globalization;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace SCCosmos.Mobile.Views;

public partial class PersonaDetallePage : ContentPage
{
    private readonly int _personaId;

    private Persona? _persona;

    private List<Congregacion> _congregaciones =
        new();

    private string _fotoBase64 = "";

    private List<ActividadMensualMobile> _actividades =
        new();

    private DatabaseHelper.CamposExtraPersonaMobile? _extra;

    private bool _cargandoControles;
    private bool _paginaCargada;

    public PersonaDetallePage(int personaId)
    {
        InitializeComponent();

        // La esposa del SC no puede ver la actividad de la persona.
        SeccionActividad.IsVisible =
            PerfilAccesoService.AccesoCompleto;

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

            await CargarActividadesAsync();
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

        // La ficha es editable directamente: mostramos las acciones
        // de foto desde el principio.
        PanelBotonesFoto.IsVisible =
            true;

        // Campos de origen / aprobación / requisito.
        try
        {
            _extra =
                DatabaseHelper
                    .ObtenerCamposExtraPersonaMobile(
                        _personaId);

            LblOrigen.Text =
                _extra is null
                    ? "Origen: desconocido"
                    : (string.IsNullOrWhiteSpace(
                           _extra.OrigenRegistro)
                       || _extra.OrigenRegistro.Equals(
                           "DESCONOCIDO",
                           StringComparison.OrdinalIgnoreCase)
                        ? "Origen: desconocido"
                        : "Origen: " + _extra.OrigenRegistro);

            SwAprobado.IsToggled =
                _extra?.AprobadoExhibidores
                ?? false;

            TxtRequisito.Text =
                _extra is not null
                && _extra.RequisitoHoras > 0
                    ? _extra.RequisitoHoras.ToString()
                    : "";
        }
        catch
        {
            // Los campos extra no bloquean la ficha.
        }
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

    private async void BtnAnadirContacto_Click(
        object sender,
        EventArgs e)
    {
        if (_persona is null)
        {
            return;
        }

#if ANDROID
        try
        {
            SCCosmos.Mobile.Platforms.Android.ContactoAndroid
                .AbrirEditorContacto(
                    _persona.NombreMostrarTabla,
                    string.IsNullOrWhiteSpace(_persona.Telefono)
                        ? _persona.Telefono
                        : _persona.Telefono,
                    string.IsNullOrWhiteSpace(_persona.Email)
                        ? _persona.EmailJW
                        : _persona.Email,
                    _persona.Direccion);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Contacto",
                "No se pudo abrir el editor de contactos:\n" +
                ex.Message,
                "Aceptar");
        }
#else
        await DisplayAlert(
            "Contacto",
            "Añadir contacto solo está disponible en Android.",
            "Aceptar");
#endif
    }

    private async void BtnCancelar_Click(
        object sender,
        EventArgs e)
    {
        // Cancelar = volver al registro de personas sin guardar.
        await Navigation.PopAsync();
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

    // ========================================================================
    // MAPA Y COORDENADAS
    // ========================================================================

    private async void BtnVerMapa_Click(
        object sender,
        EventArgs e)
    {
        if (_persona == null)
        {
            return;
        }

        if (_persona.Latitud != 0
            ||
            _persona.Longitud != 0)
        {
            await AbrirMapaCoordenadasAsync(
                _persona.Latitud,
                _persona.Longitud);

            return;
        }

        string direccion =
            TxtDireccion.Text?.Trim()
            ?? _persona.Direccion;

        if (string.IsNullOrWhiteSpace(
                direccion))
        {
            await DisplayAlert(
                "Mapa",
                "Esta persona no tiene dirección ni coordenadas. " +
                "Puedes escribir la dirección y pulsar " +
                "«Localizar dirección».",
                "Aceptar");

            return;
        }

        await AbrirMapaDireccionAsync(
            direccion);
    }

    private async void BtnLocalizar_Click(
        object sender,
        EventArgs e)
    {
        if (_persona == null)
        {
            return;
        }

        string direccion =
            TxtDireccion.Text?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(
                direccion))
        {
            await DisplayAlert(
                "Localizar dirección",
                "Escribe primero una dirección.",
                "Aceptar");

            return;
        }

        try
        {
            GeocodificacionService.ResultadoGeocodificacion
                resultado =
                    await GeocodificacionService
                        .BuscarAsync(
                            direccion);

            if (!resultado.Ok)
            {
                await DisplayAlert(
                    "Localizar dirección",
                    resultado.Error,
                    "Aceptar");

                return;
            }

            _persona.Latitud =
                resultado.Latitud;

            _persona.Longitud =
                resultado.Longitud;

            await Task.Run(
                () =>
                    DatabaseHelper
                        .ActualizarCoordenadasPersonaMobile(
                            _personaId,
                            resultado.Latitud,
                            resultado.Longitud));

            await DisplayAlert(
                "Localizar dirección",
                "Coordenadas actualizadas. Se abrirá el mapa.",
                "Aceptar");

            await AbrirMapaCoordenadasAsync(
                resultado.Latitud,
                resultado.Longitud);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Localizar dirección",
                ex.Message,
                "Aceptar");
        }
    }

    private static async Task AbrirMapaCoordenadasAsync(
        double latitud,
        double longitud)
    {
        string lat =
            latitud.ToString(
                CultureInfo.InvariantCulture);

        string lon =
            longitud.ToString(
                CultureInfo.InvariantCulture);

        string url =
            "https://www.google.com/maps/search/?api=1" +
            $"&query={lat},{lon}";

        await Launcher.Default.OpenAsync(
            new Uri(url));
    }

    private static async Task AbrirMapaDireccionAsync(
        string direccion)
    {
        string url =
            "https://www.google.com/maps/search/?api=1" +
            "&query=" +
            Uri.EscapeDataString(
                direccion);

        await Launcher.Default.OpenAsync(
            new Uri(url));
    }


    // ========================================================================
    // HISTORIAL, TRASLADO Y FUSIÓN
    // ========================================================================

    private async void BtnHistorial_Click(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new HistorialPersonaPage(
                _personaId,
                _persona?.NombreMostrarTabla ?? ""));
    }

    private async void BtnTrasladar_Click(
        object sender,
        EventArgs e)
    {
        if (_persona is null)
        {
            return;
        }

        try
        {
            List<Congregacion> congregaciones =
                await Task.Run(
                    () =>
                        DatabaseHelper.ObtenerCongregaciones());

            var opciones =
                congregaciones
                    .Where(c =>
                        !string.Equals(
                            c.Codigo,
                            _persona.CongregacionCodigo,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (opciones.Count == 0)
            {
                await DisplayAlert(
                    "Traslado",
                    "No hay otras congregaciones registradas.",
                    "Aceptar");

                return;
            }

            var etiquetas =
                opciones
                    .Select(c => $"{c.Nombre} ({c.Codigo})")
                    .ToArray();

            string eleccion =
                await DisplayActionSheet(
                    "Trasladar a la congregación:",
                    "Cancelar",
                    null,
                    etiquetas);

            int indice =
                Array.IndexOf(
                    etiquetas,
                    eleccion);

            if (indice < 0)
            {
                return;
            }

            Congregacion destino =
                opciones[indice];

            bool confirmar =
                await DisplayAlert(
                    "Confirmar traslado",
                    $"Se creará una nueva ficha en " +
                    $"«{destino.Nombre}» y la ficha actual " +
                    "quedará marcada como baja (trasladada).",
                    "Trasladar",
                    "Cancelar");

            if (!confirmar)
            {
                return;
            }

            await Task.Run(
                () =>
                    DatabaseHelper
                        .RegistrarTrasladoMobile(
                            _personaId,
                            destino.Codigo,
                            DateTime.Today));

            await DisplayAlert(
                "Traslado",
                "Traslado registrado correctamente.",
                "Aceptar");

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Traslado",
                "No se pudo registrar el traslado:\n" +
                ex.Message,
                "Aceptar");
        }
    }

    private async void BtnFusionar_Click(
        object sender,
        EventArgs e)
    {
        if (_persona is null)
        {
            return;
        }

        try
        {
            List<Persona> candidatos =
                await Task.Run(
                    () =>
                        DatabaseHelper.ObtenerPersonas());

            candidatos =
                candidatos
                    .Where(p => p.Id != _personaId)
                    .OrderBy(p => p.NombreMostrarTabla)
                    .ToList();

            if (candidatos.Count == 0)
            {
                await DisplayAlert(
                    "Fusión",
                    "No hay otras fichas con las que fusionar.",
                    "Aceptar");

                return;
            }

            var etiquetas =
                candidatos
                    .Select(p =>
                        $"{p.NombreMostrarTabla} " +
                        $"({p.CongregacionNombre})")
                    .ToArray();

            string eleccion =
                await DisplayActionSheet(
                    "Fusionar (borra la ficha elegida):",
                    "Cancelar",
                    null,
                    etiquetas);

            int indice =
                Array.IndexOf(
                    etiquetas,
                    eleccion);

            if (indice < 0)
            {
                return;
            }

            Persona origen =
                candidatos[indice];

            bool confirmar =
                await DisplayAlert(
                    "Confirmar fusión",
                    $"La ficha «{origen.NombreMostrarTabla}» se " +
                    $"fusionará en «{_persona.NombreMostrarTabla}» " +
                    "y se eliminará. Esta acción no se puede " +
                    "deshacer.",
                    "Fusionar",
                    "Cancelar");

            if (!confirmar)
            {
                return;
            }

            await Task.Run(
                () =>
                    DatabaseHelper
                        .FusionarYBorrarDefinitivoMobile(
                            _personaId,
                            origen.Id));

            await DisplayAlert(
                "Fusión",
                "Fusión completada.",
                "Aceptar");

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Fusión",
                "No se pudo fusionar:\n" +
                ex.Message,
                "Aceptar");
        }
    }


    // ========================================================================
    // ACTIVIDAD MENSUAL
    // ========================================================================

    private async Task CargarActividadesAsync()
    {
        try
        {
            _actividades =
                await Task.Run(
                    () =>
                        DatabaseHelper
                            .ObtenerActividadPersonaMobile(
                                _personaId));
        }
        catch
        {
            _actividades =
                new List<ActividadMensualMobile>();
        }

        BindableLayout.SetItemsSource(
            ListaActividad,
            _actividades);

        LblSinActividad.IsVisible =
            _actividades.Count == 0;
    }

    private async void BtnAnadirActividad_Clicked(
        object sender,
        EventArgs e)
    {
        await AbrirEditorActividadAsync(
            null);
    }

    private async void BtnEditarActividad_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is Button boton
            &&
            boton.CommandParameter is
                ActividadMensualMobile actividad)
        {
            await AbrirEditorActividadAsync(
                actividad);
        }
    }

    private async void Actividad_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is
            ActividadMensualMobile actividad)
        {
            await AbrirEditorActividadAsync(
                actividad);
        }
    }

    private async Task AbrirEditorActividadAsync(
        ActividadMensualMobile? actividad)
    {
        var editor =
            new ActividadMensualEdicionPage(
                _personaId,
                actividad);

        await Navigation.PushAsync(editor);

        await editor.EsperarResultadoAsync();

        await CargarActividadesAsync();
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
            int.TryParse(
                TxtRequisito.Text?.Trim(),
                out int requisito);

            await Task.Run(() =>
            {
                DatabaseHelper
                    .ActualizarPersona(
                        _persona);

                DatabaseHelper
                    .ActualizarCamposExtraPersonaMobile(
                        _personaId,
                        SwAprobado.IsToggled,
                        requisito);
            });

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
        // La ficha es editable directamente; no hay que desactivar nada.
    }
}