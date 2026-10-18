using System.Windows;

namespace SCCosmos
{
    public partial class PerfilInicialWindow : Window
    {
        public PerfilInicialWindow()
        {
            InitializeComponent();
        }

        private void BtnHombre_Click(
            object sender,
            RoutedEventArgs e)
        {
            PerfilAcceso.EstablecerRol(
                PerfilAcceso.PerfilHombre);

            PanelOpciones.Visibility =
                Visibility.Collapsed;

            PanelCodigo.Visibility =
                Visibility.Visible;

            TxtAviso.Text =
                "Introduce el código para acceder a todas las " +
                "funciones. Sin él, solo tendrás Inicio, Calendario, " +
                "Personas y Configuración.";
        }

        private void BtnEsposa_Click(
            object sender,
            RoutedEventArgs e)
        {
            PerfilAcceso.EstablecerRol(
                PerfilAcceso.PerfilEsposa);

            DialogResult = true;
        }

        private void BtnContinuar_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool ok =
                PerfilAcceso.ValidarCodigo(
                    TxtCodigo.Text);

            if (!ok)
            {
                TxtAviso.Text =
                    "Código incorrecto. Inténtalo de nuevo o continúa " +
                    "con acceso limitado.";

                return;
            }

            DialogResult = true;
        }

        private void BtnSinCodigo_Click(
            object sender,
            RoutedEventArgs e)
        {
            PerfilAcceso.ValidarCodigo("");

            DialogResult = true;
        }
    }
}
