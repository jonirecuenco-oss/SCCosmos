using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// Los pueblos del territorio de una congregación (además del del salón):
    /// en Circuito se ve su tiempo y el móvil avisa de sus alertas rojas. Se
    /// guardan al momento en Configuracion, que se sincroniza con el móvil.
    /// </summary>
    public partial class PueblosTerritorioWindow : Window
    {
        private readonly Congregacion _congregacion;

        private readonly string _ineSalon;

        private List<PuebloTerritorio> _pueblos = new List<PuebloTerritorio>();

        private List<(string Ine, string Nombre, int Personas)> _sugerencias = new List<(string Ine, string Nombre, int Personas)>();

        private bool _ocupado;


        public PueblosTerritorioWindow(Congregacion congregacion)
        {
            InitializeComponent();

            _congregacion = congregacion;

            _ineSalon =
                TiempoAemet.BuscarIne(congregacion.Municipio, congregacion.Provincia) ?? "";

            TxtCongregacion.Text = congregacion.Nombre;

            TxtSalon.Text =
                _ineSalon.Length > 0
                    ? $"El del salón: {TiempoAemet.NombreMunicipio(_ineSalon, congregacion.Municipio)}."
                    : "El municipio del salón no está en la lista del INE.";

            Loaded += async (s, e) => await CargarAsync();
        }


        private async Task CargarAsync()
        {
            string codigo = _congregacion.Codigo;

            try
            {
                _pueblos =
                    await Task.Run(() => DatabaseHelper.ObtenerPueblosTerritorio(codigo));

                _sugerencias =
                    await Task.Run(() => PueblosTerritorio.Sugerencias(DatabaseHelper.ObtenerDireccionesCongregacion(codigo), _ineSalon));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudieron leer sus pueblos: " + ex.Message, "Pueblos del territorio");
            }

            Pintar();
            TxtBuscar.Focus();
        }


        private void Pintar()
        {
            ListaPueblos.Children.Clear();

            if (_pueblos.Count == 0)
            {
                ListaPueblos.Children.Add(Nota("Todavía no hay ninguno."));
            }

            foreach (PuebloTerritorio pueblo in _pueblos.ToList())
            {
                ListaPueblos.Children.Add(
                    Fila(
                        pueblo.Nombre,
                        pueblo.Latitud == 0 && pueblo.Longitud == 0 ? "Sin su sitio en el mapa: no avisa de sus alertas rojas" : "",
                        "Quitar",
                        quitar: true,
                        () => _ = QuitarAsync(pueblo)));
            }

            var pendientes =
                _sugerencias
                    .Where(s => _pueblos.All(p => p.Ine != s.Ine))
                    .ToList();

            PanelSugerencias.Visibility =
                pendientes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            ListaSugerencias.Children.Clear();

            foreach (var sugerencia in pendientes)
            {
                ListaSugerencias.Children.Add(
                    Fila(
                        sugerencia.Nombre,
                        sugerencia.Personas == 1 ? "1 publicador" : $"{sugerencia.Personas} publicadores",
                        "Añadir",
                        quitar: false,
                        () => _ = AnadirAsync(sugerencia.Ine)));
            }

            PintarResultados();
        }


        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e) =>
            PintarResultados();


        private void PintarResultados()
        {
            ListaResultados.Children.Clear();

            string provincia =
                _ineSalon.Length >= 2 ? _ineSalon.Substring(0, 2) : "";

            foreach (var municipio in TiempoAemet.BuscarMunicipios(TxtBuscar.Text ?? "", provincia, 8))
            {
                bool esta =
                    municipio.Ine == _ineSalon
                    || _pueblos.Any(p => p.Ine == municipio.Ine);

                string ine = municipio.Ine;

                ListaResultados.Children.Add(
                    Fila(
                        municipio.Nombre,
                        municipio.Provincia + (municipio.Ine == _ineSalon ? " · el del salón" : esta ? " · ya está" : ""),
                        esta ? "" : "Añadir",
                        quitar: false,
                        () => _ = AnadirAsync(ine)));
            }
        }


        private async Task AnadirAsync(string ine)
        {
            if (_ocupado
                || ine == _ineSalon
                || _pueblos.Any(p => p.Ine == ine))
            {
                return;
            }

            _ocupado = true;

            string nombre =
                TiempoAemet.NombreMunicipio(ine, ine);

            TxtEstado.Text = $"Buscando {nombre} en el mapa…";
            TxtEstado.Visibility = Visibility.Visible;

            try
            {
                // Su sitio en el mapa (OpenStreetMap): con él se sabe su zona de aviso.
                var coordenadas =
                    await PueblosTerritorio.CoordenadasAsync(ine, CancellationToken.None);

                _pueblos.Add(
                    new PuebloTerritorio(
                        ine,
                        coordenadas?.Latitud ?? 0,
                        coordenadas?.Longitud ?? 0));

                string codigo = _congregacion.Codigo;
                List<PuebloTerritorio> pueblos = _pueblos.ToList();

                await Task.Run(() => DatabaseHelper.GuardarPueblosTerritorio(codigo, pueblos));

                if (coordenadas == null)
                {
                    MessageBox.Show(
                        this,
                        $"No se ha encontrado {nombre} en el mapa (¿sin conexión?). Se verá su tiempo, pero no avisará de sus alertas rojas: quítalo y vuelve a añadirlo con conexión.",
                        "Pueblos del territorio");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "Pueblos del territorio");
            }
            finally
            {
                _ocupado = false;
                TxtEstado.Visibility = Visibility.Collapsed;
            }

            Pintar();
        }


        private async Task QuitarAsync(PuebloTerritorio pueblo)
        {
            if (_ocupado)
            {
                return;
            }

            _pueblos.RemoveAll(p => p.Ine == pueblo.Ine);

            string codigo = _congregacion.Codigo;
            List<PuebloTerritorio> pueblos = _pueblos.ToList();

            try
            {
                await Task.Run(() => DatabaseHelper.GuardarPueblosTerritorio(codigo, pueblos));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "Pueblos del territorio");
            }

            Pintar();
        }


        private void BtnCerrar_Click(object sender, RoutedEventArgs e) =>
            Close();


        private TextBlock Nota(string texto) =>
            new TextBlock
            {
                Text = texto,
                FontSize = 13,
                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
                Margin = new Thickness(0, 2, 0, 2)
            };


        // Una fila: el pueblo, una línea debajo y su botón (sin texto, sin botón).
        private UIElement Fila(
            string titulo,
            string detalle,
            string boton,
            bool quitar,
            Action alPulsar)
        {
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            textos.Children.Add(new TextBlock { Text = titulo, FontSize = 14 });

            if (detalle.Length > 0)
            {
                textos.Children.Add(new TextBlock
                {
                    Text = detalle,
                    FontSize = 11.5,
                    Foreground = (Brush)FindResource("TextFillColorTertiaryBrush")
                });
            }

            var fila = new Grid { Margin = new Thickness(0, 3, 0, 3) };

            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            fila.Children.Add(textos);

            if (boton.Length > 0)
            {
                var accion = new Button
                {
                    Content = boton,
                    Padding = new Thickness(12, 4, 12, 4),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = InicioPage.Pincel(quitar ? "#3A2023" : "#1B3A52"),
                    Foreground = InicioPage.Pincel(quitar ? "#FFB7BC" : "#F2F4F7")
                };

                accion.Click += (s, e) => alPulsar();

                Grid.SetColumn(accion, 1);
                fila.Children.Add(accion);
            }

            return fila;
        }
    }
}
