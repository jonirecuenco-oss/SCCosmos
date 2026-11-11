using System.ComponentModel;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Services;
using SCCosmos.Sincro;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Qué avisa la app y cuándo: quedar con el CCA (día y hora de la semana
/// antes de una visita), el S-303 del domingo, las alertas meteorológicas y
/// las alertas de Inicio (activas y con su plazo). Se guarda al momento en
/// Configuracion (SincroCompartido/AjustesAvisos.cs), que se sincroniza.
/// </summary>
public partial class AvisosPage : ContentPage
{
    // En el desplegable, la semana de antes de la visita: de martes a lunes.
    private static readonly DayOfWeek[] OrdenDias =
    {
        DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
        DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday
    };

    private static readonly string[] NombresDias =
        { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

    // Cada alerta de Inicio: su interruptor y los días (sin días, la de JW Hub).
    private readonly Dictionary<string, (Switch Activa, Entry? Dias)> _alertas = new();

    private AjustesAvisos _ajustes = new();

    private bool _cargando;


    public AvisosPage()
    {
        InitializeComponent();

        PickerDiaCca.ItemsSource =
            OrdenDias.Select(d => NombresDias[(int)d]).ToList();

        foreach (var alerta in AjustesAvisos.Alertas)
        {
            ListaAlertas.Add(FilaAlerta(alerta.Tipo, alerta.Nombre, alerta.Plazo));
        }

        SwCca.Toggled += (_, _) => Guardar();
        SwS303.Toggled += (_, _) => Guardar();
        SwTiempo.Toggled += (_, _) => Guardar();
        PickerDiaCca.SelectedIndexChanged += (_, _) => Guardar();
        HoraCca.PropertyChanged += Hora_PropertyChanged;
        HoraS303.PropertyChanged += Hora_PropertyChanged;
        HoraRevision.PropertyChanged += HoraRevision_PropertyChanged;
    }


    protected override void OnAppearing()
    {
        base.OnAppearing();

        Cargar();
    }


    private void Cargar()
    {
        _cargando = true;

        try
        {
            _ajustes =
                AjustesAvisos.Leer(clave => DatabaseHelper.ObtenerConfiguracion(clave, ""));

            SwCca.IsToggled = _ajustes.CcaActivo;
            PickerDiaCca.SelectedIndex = Array.IndexOf(OrdenDias, _ajustes.CcaDia);
            HoraCca.Time = _ajustes.CcaHora;

            SwS303.IsToggled = _ajustes.S303Activo;
            HoraS303.Time = _ajustes.S303Hora;

            SwTiempo.IsToggled = _ajustes.TiempoActivo;

            foreach (var (tipo, fila) in _alertas)
            {
                fila.Activa.IsToggled = _ajustes.AlertaActiva(tipo);

                if (fila.Dias is not null)
                {
                    fila.Dias.Text = _ajustes.DiasAlerta(tipo).ToString();
                }
            }

            HoraRevision.Time =
                SCCosmos.Mobile.Platforms.Android.AlertasAlarmScheduler.HoraRevision;

            PintarEstados();
        }
        finally
        {
            _cargando = false;
        }
    }


    private void Guardar()
    {
        if (_cargando)
        {
            return;
        }

        try
        {
            _ajustes.CcaActivo = SwCca.IsToggled;
            _ajustes.CcaDia = PickerDiaCca.SelectedIndex >= 0 ? OrdenDias[PickerDiaCca.SelectedIndex] : DayOfWeek.Thursday;
            _ajustes.CcaHora = HoraCca.Time ?? new TimeSpan(18, 0, 0);

            _ajustes.S303Activo = SwS303.IsToggled;
            _ajustes.S303Hora = HoraS303.Time ?? new TimeSpan(20, 0, 0);

            _ajustes.TiempoActivo = SwTiempo.IsToggled;

            foreach (var (tipo, fila) in _alertas)
            {
                int dias = _ajustes.DiasAlerta(tipo);

                // Un número que no se entiende deja el plazo como estaba.
                if (fila.Dias is not null
                    && int.TryParse(fila.Dias.Text, out int escrito)
                    && escrito >= 0)
                {
                    dias = escrito;
                }

                _ajustes.PonerAlerta(tipo, fila.Activa.IsToggled, dias);

                if (fila.Dias is not null)
                {
                    fila.Dias.Text = _ajustes.DiasAlerta(tipo).ToString();
                }
            }

            foreach (var (clave, valor) in _ajustes.Valores())
            {
                DatabaseHelper.GuardarConfiguracion(clave, valor);
            }

            PintarEstados();

            // La alarma del siguiente recordatorio y la lista de alertas, al día.
            SCCosmos.Mobile.Platforms.Android.RecordatoriosVisitaScheduler.Revisar();

            _ = AlertasNotificacionService.ActualizarAsync();
        }
        catch (Exception ex)
        {
            _ = DisplayAlert("Avisos", "No se pudo guardar: " + ex.Message, "Aceptar");
        }
    }


    private void Hora_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimePicker.Time))
        {
            Guardar();
        }
    }


    // La hora de la revisión diaria de alertas es de este móvil (no se sincroniza).
    private void HoraRevision_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_cargando
            || e.PropertyName != nameof(TimePicker.Time)
            || HoraRevision.Time is not TimeSpan hora)
        {
            return;
        }

        Preferences.Default.Set("SCCosmos_HoraAlertas", hora.ToString(@"hh\:mm"));

        SCCosmos.Mobile.Platforms.Android.AlertasAlarmScheduler.ProgramarSiguiente();
    }


    // Lo de cada aviso apagado, atenuado.
    private void PintarEstados()
    {
        PanelCca.IsEnabled = SwCca.IsToggled;
        PanelCca.Opacity = SwCca.IsToggled ? 1 : 0.45;

        HoraS303.IsEnabled = SwS303.IsToggled;
        HoraS303.Opacity = SwS303.IsToggled ? 1 : 0.45;

        foreach (var (_, fila) in _alertas)
        {
            if (fila.Dias is not null)
            {
                fila.Dias.IsEnabled = fila.Activa.IsToggled;
                fila.Dias.Opacity = fila.Activa.IsToggled ? 1 : 0.45;
            }
        }
    }


    // «S-302 pendiente» con su interruptor y, debajo, «[90] días antes de la visita».
    private View FilaAlerta(string tipo, string nombre, string plazo)
    {
        var activa =
            new Switch
            {
                OnColor = Color.FromArgb("#2478B5"),
                VerticalOptions = LayoutOptions.Center
            };

        activa.Toggled += (_, _) => Guardar();

        var textos =
            new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center
            };

        textos.Add(new Label
        {
            Text = nombre,
            FontSize = 14.5,
            TextColor = Color.FromArgb("#F2F4F7")
        });

        Entry? dias = null;

        if (plazo.Length > 0)
        {
            dias =
                new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    WidthRequest = 56,
                    FontSize = 14,
                    TextColor = Color.FromArgb("#F2F4F7"),
                    HorizontalTextAlignment = TextAlignment.Center
                };

            dias.Completed += (_, _) => Guardar();
            dias.Unfocused += (_, _) => Guardar();

            var linea =
                new HorizontalStackLayout { Spacing = 6 };

            linea.Add(dias);
            linea.Add(new Label
            {
                Text = plazo,
                FontSize = 12.5,
                TextColor = Color.FromArgb("#A7AEB8"),
                VerticalOptions = LayoutOptions.Center
            });

            textos.Add(linea);
        }

        _alertas[tipo] = (activa, dias);

        var fila =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };

        fila.Add(textos, 0);
        fila.Add(activa, 1);

        return fila;
    }
}
