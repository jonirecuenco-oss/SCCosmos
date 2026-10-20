using System.ComponentModel;

namespace SCCosmos.Mobile.Models;

public class ActividadHoy : INotifyPropertyChanged
{
    public string Hora { get; set; } = "";
    public string TipoActividad { get; set; } = "";

    public string Responsable { get; set; } = "";
    public bool MostrarResponsable => !string.IsNullOrWhiteSpace(Responsable);

    public string CompaneroNombre { get; set; } = "";

    // Sustituye a Visibility de WPF
    public bool VerCompanero { get; set; }

    public string Ubicacion { get; set; } = "";
    public bool MostrarUbicacion => !string.IsNullOrWhiteSpace(Ubicacion);

    public string Direccion { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }

    public string Notas { get; set; } = "";
    public bool MostrarNotas => !string.IsNullOrWhiteSpace(Notas);

    public bool MostrarMapa =>
        Latitud != 0
        || Longitud != 0
        || !string.IsNullOrWhiteSpace(Direccion);

    private string _tiempoCocheTexto = "";

    /// <summary>Tiempo en coche («🚗 18 min»), si se conoce.</summary>
    public string TiempoCocheTexto
    {
        get => _tiempoCocheTexto;
        set
        {
            _tiempoCocheTexto = value ?? "";
            OnPropertyChanged(nameof(TiempoCocheTexto));
            OnPropertyChanged(nameof(VerTiempoCoche));
        }
    }

    public bool VerTiempoCoche =>
        !string.IsNullOrWhiteSpace(_tiempoCocheTexto);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string nombre) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nombre));
}
