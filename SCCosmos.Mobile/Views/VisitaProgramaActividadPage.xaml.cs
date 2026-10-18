using SCCosmos.Mobile.Data;

namespace SCCosmos.Mobile.Views;


public partial class VisitaProgramaActividadPage
    : ContentPage
{
    private readonly string _codigo;
    private readonly string _mesReferencia;
    private readonly VistaProgramaVisitaMobile? _existente;
    private readonly Func<Task> _alGuardar;
    private readonly List<VisitaPersonaSelectorMobile> _personas;

    private double _latitudUbicacion;
    private double _longitudUbicacion;
    private bool _actualizandoUbicacionAutomatica;
    private bool _inicializando;

    private static readonly string[] Dias =
    [
        "Miércoles",
        "Jueves",
        "Viernes",
        "Sábado",
        "Domingo"
    ];

    private static readonly string[] Turnos =
    [
        "MAÑANA",
        "MEDIODÍA",
        "TARDE",
        "NOCHE"
    ];


    public VisitaProgramaActividadPage(
        string codigo,
        string mesReferencia,
        IEnumerable<VistaPersonaVisitaMobile> personas,
        VistaProgramaVisitaMobile? existente,
        Func<Task> alGuardar)
    {
        InitializeComponent();

        _inicializando = true;

        _codigo = codigo;
        _mesReferencia = mesReferencia;
        _existente = existente;
        _alGuardar = alGuardar;

        _personas =
            personas
                .OrderBy(p => p.NombreVisual)
                .Select(p =>
                    new VisitaPersonaSelectorMobile
                    {
                        Id = p.Id,
                        Nombre = p.NombreVisual,
                        Direccion = p.Direccion,
                        Latitud = p.Latitud,
                        Longitud = p.Longitud
                    })
                .ToList();

        PickerDia.ItemsSource = Dias;
        PickerTurno.ItemsSource = Turnos;
        PickerCompanero.ItemsSource = _personas;

        if (_existente is null)
        {
            PickerDia.SelectedIndex = 0;
            PickerTurno.SelectedIndex = 0;
            LblTitulo.Text = "Programar actividad";
        }
        else
        {
            LblTitulo.Text = "Editar actividad";
            PickerDia.SelectedItem = _existente.Dia;
            PickerTurno.SelectedItem = _existente.Turno;
            ActualizarResponsables();
            PickerResponsable.SelectedItem = _existente.Responsable;
            PickerCompanero.SelectedItem =
                _personas.FirstOrDefault(p => p.Id == _existente.CompaneroId);
            TxtHora.Text = _existente.Hora;
            TxtTipo.Text = _existente.TipoActividad;
            TxtNotas.Text = _existente.Notas;
            TxtUbicacion.Text = _existente.Ubicacion;
            _latitudUbicacion = _existente.Latitud;
            _longitudUbicacion = _existente.Longitud;
        }

        ActualizarEtiquetaCoordenadas();
        _inicializando = false;
    }


    private void PickerCompanero_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (_inicializando)
        {
            return;
        }

        AutorrellenarUbicacionDesdeCompanero();
    }


    private void BtnUsarDireccionCompanero_Clicked(
        object sender,
        EventArgs e)
    {
        AutorrellenarUbicacionDesdeCompanero();
    }


    private void AutorrellenarUbicacionDesdeCompanero()
    {
        if (PickerCompanero.SelectedItem
            is not VisitaPersonaSelectorMobile persona)
        {
            return;
        }

        _actualizandoUbicacionAutomatica = true;

        try
        {
            TxtUbicacion.Text =
                persona.Direccion ?? "";

            _latitudUbicacion =
                persona.Latitud;

            _longitudUbicacion =
                persona.Longitud;

            ActualizarEtiquetaCoordenadas();
        }
        finally
        {
            _actualizandoUbicacionAutomatica = false;
        }
    }


    private void TxtUbicacion_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_inicializando
            ||
            _actualizandoUbicacionAutomatica)
        {
            return;
        }

        // Si el usuario cambia manualmente la dirección, las coordenadas
        // heredadas del acompañante dejan de ser fiables.
        _latitudUbicacion = 0;
        _longitudUbicacion = 0;
        ActualizarEtiquetaCoordenadas();
    }


    private void ActualizarEtiquetaCoordenadas()
    {
        bool tiene =
            Math.Abs(_latitudUbicacion) > 0.000001
            &&
            Math.Abs(_longitudUbicacion) > 0.000001;

        LblCoordenadas.Text =
            tiene
                ? $"Coordenadas: {_latitudUbicacion:0.#####}, {_longitudUbicacion:0.#####}"
                : "Sin coordenadas";

        LblCoordenadas.TextColor =
            tiene
                ? Color.FromArgb("#7ED889")
                : Color.FromArgb("#7F8791");
    }


    private void PickerTurno_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        ActualizarResponsables();
    }


    private void ActualizarResponsables()
    {
        string turno =
            PickerTurno.SelectedItem as string
            ?? "MAÑANA";

        string[] responsables =
            turno switch
            {
                "MEDIODÍA" => ["Comida"],
                "NOCHE" => ["Cenar", "Dormir"],
                _ => ["Joni", "Rebe"]
            };

        string? anterior =
            PickerResponsable.SelectedItem as string;

        PickerResponsable.ItemsSource = responsables;

        if (!string.IsNullOrWhiteSpace(anterior)
            &&
            responsables.Contains(anterior))
        {
            PickerResponsable.SelectedItem = anterior;
        }
        else
        {
            PickerResponsable.SelectedIndex =
                responsables.Length > 0 ? 0 : -1;
        }
    }


    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        string dia = PickerDia.SelectedItem as string ?? "";
        string turno = PickerTurno.SelectedItem as string ?? "";
        string responsable = PickerResponsable.SelectedItem as string ?? "";
        VisitaPersonaSelectorMobile? companero =
            PickerCompanero.SelectedItem as VisitaPersonaSelectorMobile;
        string hora = TxtHora.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(dia)
            ||
            string.IsNullOrWhiteSpace(turno)
            ||
            string.IsNullOrWhiteSpace(responsable)
            ||
            companero is null
            ||
            string.IsNullOrWhiteSpace(hora))
        {
            await DisplayAlert(
                "Programa de la visita",
                "Indica día, turno, responsable, acompañante y hora.",
                "Aceptar");
            return;
        }

        BtnGuardar.IsEnabled = false;

        try
        {
            var actividad =
                new VistaProgramaVisitaMobile
                {
                    Id = _existente?.Id ?? 0,
                    Dia = dia,
                    Turno = turno,
                    Responsable = responsable,
                    CompaneroId = companero.Id,
                    Hora = hora,
                    TipoActividad = TxtTipo.Text?.Trim() ?? "",
                    Notas = TxtNotas.Text?.Trim() ?? "",
                    Ubicacion = TxtUbicacion.Text?.Trim() ?? "",
                    Latitud = _latitudUbicacion,
                    Longitud = _longitudUbicacion
                };

            await Task.Run(() =>
                DatabaseHelper.GuardarActividadProgramaVisitaMobile(
                    _codigo,
                    _mesReferencia,
                    actividad));

            await _alGuardar();
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Programa de la visita",
                "No se pudo guardar la actividad:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            BtnGuardar.IsEnabled = true;
        }
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }
}


public sealed class VisitaPersonaSelectorMobile
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
}
