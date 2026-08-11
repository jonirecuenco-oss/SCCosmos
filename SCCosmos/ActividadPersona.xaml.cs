using System.Windows;

namespace SCCosmos
{
    public partial class ActividadPersona : Window
    {
        public ActividadPersona(Persona persona)
        {
            InitializeComponent();
            TxtTitulo.Text = $"Actividad de {persona.Nombre}";

            // Cargamos la actividad directamente desde SQLite
            var historial = DatabaseHelper.ObtenerActividadPersona(persona.Id);
            TablaActividad.ItemsSource = historial;
        }
    }
}