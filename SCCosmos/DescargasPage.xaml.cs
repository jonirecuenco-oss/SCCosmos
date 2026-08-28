using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SCCosmos
{
    public partial class DescargasPage : Page
    {
        public DescargasPage()
        {
            InitializeComponent();
        }

        private async void btnDescargar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Desactivamos el botón y cambiamos el texto
            btnDescargar.IsEnabled = false;
            btnDescargar.Content = "DESCARGANDO... POR FAVOR, ESPERE";

            try
            {
                // 2. Leemos el estado de todas las casillas (Checkboxes) de la pantalla
                OpcionesDescarga misOpciones = new OpcionesDescarga
                {
                    // Betel
                    Betel_AdoracionMatutina = chkBetelAdoracion.IsChecked ?? false,

                    // Teocráticos
                    Teocraticos_DiscursosPublicos = chkTeoDiscursos.IsChecked ?? false,
                    Teocraticos_AsambleaCircuitoSucursal = chkTeoAsambleaSucursal.IsChecked ?? false,
                    Teocraticos_AsambleaCircuitoSuperintendente = chkTeoAsambleaSuper.IsChecked ?? false,
                    Teocraticos_AsambleaRegional = chkTeoAsambleaRegional.IsChecked ?? false,

                    // JW.ORG
                    JwOrg_DiscursosBroadcasting = chkJwDiscursos.IsChecked ?? false,
                    JwOrg_AdoracionMatutina = chkJwAdoracion.IsChecked ?? false,
                    JwOrg_GraduacionGalaad = chkJwGalaad.IsChecked ?? false
                };

                // 3. OBTENEMOS LA RUTA DINÁMICA DEL PERFIL
                string rutaPerfil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCCosmos", "PerfilFirefoxCorreo");

                // Verificamos si la carpeta existe. Si no, la creamos para que Firefox no dé error de "Sesión no creada".
                if (!Directory.Exists(rutaPerfil))
                {
                    Directory.CreateDirectory(rutaPerfil);
                }

                // 4. Ejecutamos el robot en segundo plano
                await Task.Run(() =>
                {
                    DescargadorStreamJW.DescargarUltimoDiscurso(rutaPerfil, misOpciones);
                });

                MessageBox.Show("¡Todas las descargas han finalizado correctamente!", "Proceso Completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error general: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 5. Volvemos a activar el botón al terminar
                btnDescargar.IsEnabled = true;
                btnDescargar.Content = "INICIAR DESCARGAS";
            }
        }
    }
}