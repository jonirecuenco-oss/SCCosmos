using System;
using System.Collections.Generic;
using System.Windows;
using System.Text.Json;
using Microsoft.Win32;

namespace SCCosmos
{
    public partial class ImportarArchivosActividad : Wpf.Ui.Controls.FluentWindow
    {
        private string _congreCodigo;
        private string _congreNombre;
        private bool _yaImportado;
        private string _rutaS21;
        private string _rutaTerritorios;
        private string _rutaJsonConvertido;
        private string _rutaContactos;
        private readonly string _mesReferencia;

        public ImportarArchivosActividad(string congreCodigo, string congreNombre, bool yaImportado, string mesReferencia = null)
        {
            InitializeComponent();
            _congreCodigo = congreCodigo;
            _congreNombre = congreNombre;
            _yaImportado = yaImportado;
            _mesReferencia = string.IsNullOrWhiteSpace(mesReferencia)
                ? DateTime.Now.ToString("yyyy-MM")
                : mesReferencia;
            TxtSubtitulo.Text = $"{congreNombre.ToUpper()}";

            if (_yaImportado)
            {
                PanelAviso.Visibility = Visibility.Visible;
            }

            BuscarContactosSubidos();
        }

        // La lista de contactos que subieron los ancianos para esta visita, si la hay.
        private void BuscarContactosSubidos()
        {
            try
            {
                string carpeta = System.IO.Path.Combine(
                    ProgramaWebPc.CarpetaArchivos(_congreCodigo, _mesReferencia),
                    SCCosmos.Sincro.ArchivosProgramaWeb.Carpeta("contactos") ?? "Datos de contacto");

                if (!System.IO.Directory.Exists(carpeta))
                {
                    return;
                }

                var archivos = new System.IO.DirectoryInfo(carpeta).GetFiles()
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();
                var legible = archivos.FirstOrDefault(f => SCCosmos.Sincro.ConvertidorContactos.Extensiones.Contains(f.Extension.ToLowerInvariant()));

                if (legible is not null)
                {
                    ElegirContactos(legible.FullName, "la subieron los ancianos");
                }
                else if (archivos.Count > 0)
                {
                    TxtContactos.Text = $"Los ancianos subieron «{archivos[0].Name}», que todavía no se puede leer: solo Excel (.xlsx), Word (.docx) o CSV.";
                }
            }
            catch (Exception)
            {
                // Sin la carpeta de los ancianos se elige a mano.
            }
        }

        private void BtnContactos_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Listas de contactos (*.xlsx;*.docx;*.csv)|*.xlsx;*.docx;*.csv;*.txt|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar la lista de contactos"
            };

            if (ofd.ShowDialog() == true)
            {
                ElegirContactos(ofd.FileName, null);
            }
        }

        // Se lee ya, para decir cuántas personas trae (o por qué no se puede leer).
        private void ElegirContactos(string ruta, string origen)
        {
            try
            {
                var lista = SCCosmos.Sincro.ConvertidorContactos.SinRepetir(
                    SCCosmos.Sincro.ConvertidorContactos.Leer(ruta)
                        .Select(t => SCCosmos.Sincro.ConvertidorContactos.Analizar(t))
                        .SelectMany(SCCosmos.Sincro.ConvertidorContactos.Contactos));

                _rutaContactos = ruta;
                TxtContactos.Text = System.IO.Path.GetFileName(ruta)
                    + (origen is null ? "" : $" ({origen})")
                    + (lista.Count == 0
                        ? " · no se ve ninguna persona con datos: en el paso siguiente elige qué es cada columna."
                        : $" · {lista.Count} {(lista.Count == 1 ? "persona" : "personas")} con datos");
                TxtContactos.Foreground = System.Windows.Media.Brushes.LightGreen;
                TxtContactos.FontStyle = FontStyles.Normal;
            }
            catch (Exception ex)
            {
                _rutaContactos = null;
                TxtContactos.Text = $"{System.IO.Path.GetFileName(ruta)}: {ex.Message}";
                TxtContactos.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush");
                TxtContactos.FontStyle = FontStyles.Italic;
            }
        }

        // Paso 2: los datos de contacto, con las personas ya al día.
        private bool AbrirContactos(bool trasPersonas)
        {
            var ventana = new ContactosWindow(_congreCodigo, _congreNombre, _rutaContactos, trasPersonas)
            {
                Owner = Window.GetWindow(this)
            };

            return ventana.ShowDialog() == true;
        }

        private void BtnS21_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "Archivos JSON (*.json)|*.json", Title = "Seleccionar S-21" };
            if (ofd.ShowDialog() == true)
            {
                _rutaS21 = ofd.FileName;
                TxtS21.Text = System.IO.Path.GetFileName(_rutaS21);
                TxtS21.Foreground = System.Windows.Media.Brushes.LightGreen;
                TxtS21.FontStyle = FontStyles.Normal;
            }
        }

        private void BtnTerritorios_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Filter = "Archivos JSON (*.json)|*.json", Title = "Seleccionar Territorios" };
            if (ofd.ShowDialog() == true)
            {
                _rutaTerritorios = ofd.FileName;
                TxtTerritorios.Text = System.IO.Path.GetFileName(_rutaTerritorios);
                TxtTerritorios.Foreground = System.Windows.Media.Brushes.LightBlue;
                TxtTerritorios.FontStyle = FontStyles.Normal;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void BtnConvertirPdf_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new OpenFolderDialog
            {
                Title = "Carpeta con las tarjetas S-21 (PDF)"
            };

            if (dialogo.ShowDialog(this) != true)
            {
                return;
            }

            string carpeta = dialogo.FolderName;

            TxtPdfS21.Text = "Convirtiendo tarjetas…";
            TxtPdfS21.Foreground = System.Windows.Media.Brushes.LightYellow;
            BtnVerJsonS21.Visibility = Visibility.Collapsed;

            int anio = SCCosmos.Sincro.ConvertidorS21.AnioCicloActual(
                DateTime.Parse(_mesReferencia + "-01"));

            SCCosmos.Sincro.ResultadoConversionS21 resultado = null;
            string rutaJson = null;
            string error = null;

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    resultado = SCCosmos.Sincro.ConvertidorS21
                        .ConvertirCarpeta(carpeta, anio);

                    rutaJson = SCCosmos.Sincro.ConvertidorS21
                        .GuardarJson(resultado.Tarjetas, carpeta);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
            {
                TxtPdfS21.Text = "No se pudo convertir: " + error;
                TxtPdfS21.Foreground = System.Windows.Media.Brushes.Salmon;
                return;
            }

            if (resultado == null || resultado.Tarjetas.Count == 0)
            {
                TxtPdfS21.Text = "No se encontraron tarjetas S-21 en esa carpeta.";
                TxtPdfS21.Foreground = System.Windows.Media.Brushes.Salmon;
                return;
            }

            _rutaJsonConvertido = rutaJson;
            _rutaS21 = rutaJson;

            TxtS21.Text = System.IO.Path.GetFileName(rutaJson)
                + " (" + resultado.Tarjetas.Count
                + " tarjetas, generado desde PDF)";
            TxtS21.Foreground = System.Windows.Media.Brushes.LightGreen;
            TxtS21.FontStyle = FontStyles.Normal;

            TxtPdfS21.Text = resultado.Tarjetas.Count + " tarjetas de "
                + resultado.ArchivosPdf + " PDFs ("
                + resultado.Repetidas + " repetidas). Puedes ver el JSON generado.";
            TxtPdfS21.Foreground = System.Windows.Media.Brushes.LightGreen;

            // Casi todas las personas llevan marcado el sexo en alguna tarjeta: si
            // falta en muchas, lo normal es que no se hayan podido leer las casillas
            // (nombramiento y precursor tampoco).
            var personas = resultado.Tarjetas
                .GroupBy(t => t.Nombre, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int sinCasillas = personas.Count(p => p.All(t => string.IsNullOrWhiteSpace(t.Sexo)));

            if (sinCasillas * 3 > personas.Count)
            {
                TxtPdfS21.Text += " Atención: en " + sinCasillas + " de " + personas.Count
                    + " personas no se han leído las casillas (sexo, nombramiento, precursor). Revísalas antes de importar.";
                TxtPdfS21.Foreground = System.Windows.Media.Brushes.Orange;
            }
            BtnVerJsonS21.Visibility = Visibility.Visible;
        }

        private void BtnVerJsonS21_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_rutaJsonConvertido)
                && System.IO.File.Exists(_rutaJsonConvertido))
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_rutaJsonConvertido)
                    {
                        UseShellExecute = true
                    });
            }
        }


        // =========================================================
        // S-13 · TERRITORIOS DESDE PDF O EXCEL
        // =========================================================

        private string _rutaJsonTerritorios;

        private async void BtnConvertirS13_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new OpenFolderDialog
            {
                Title = "Carpeta con el registro de territorios (PDF o Excel)"
            };

            if (dialogo.ShowDialog(this) != true)
            {
                return;
            }

            string carpeta = dialogo.FolderName;

            TxtPdfS13.Text = "Leyendo territorios…";
            TxtPdfS13.Foreground = System.Windows.Media.Brushes.LightYellow;
            BtnVerJsonS13.Visibility = Visibility.Collapsed;

            SCCosmos.Sincro.ResultadoConversionS13 resultado = null;
            string rutaJson = null;
            string error = null;

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    resultado = SCCosmos.Sincro.ConvertidorS13
                        .ConvertirCarpeta(carpeta);

                    if (resultado.Territorios.Count > 0)
                    {
                        rutaJson = SCCosmos.Sincro.ConvertidorS13
                            .GuardarJson(resultado.Territorios, carpeta);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
            {
                TxtPdfS13.Text = "No se pudo convertir: " + error;
                TxtPdfS13.Foreground = System.Windows.Media.Brushes.Salmon;
                return;
            }

            if (resultado == null || resultado.Territorios.Count == 0)
            {
                TxtPdfS13.Text = "No se reconocieron territorios en esa carpeta.";
                TxtPdfS13.Foreground = System.Windows.Media.Brushes.Salmon;
                return;
            }

            _rutaJsonTerritorios = rutaJson;
            _rutaTerritorios = rutaJson;

            TxtTerritorios.Text = System.IO.Path.GetFileName(rutaJson)
                + " (" + resultado.Territorios.Count
                + " territorios, generado)";
            TxtTerritorios.Foreground = System.Windows.Media.Brushes.LightBlue;
            TxtTerritorios.FontStyle = FontStyles.Normal;

            string detalle = resultado.Territorios.Count + " territorios de "
                + resultado.Archivos.Count + " archivo(s).";

            if (resultado.Omitidos.Count > 0)
            {
                detalle += " No se pudieron leer: "
                    + string.Join(", ", resultado.Omitidos
                        .Take(3)
                        .Select(o => o.Archivo));
            }

            TxtPdfS13.Text = detalle;
            TxtPdfS13.Foreground = System.Windows.Media.Brushes.LightGreen;
            BtnVerJsonS13.Visibility = Visibility.Visible;
        }


        private void BtnVerJsonS13_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_rutaJsonTerritorios)
                && System.IO.File.Exists(_rutaJsonTerritorios))
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_rutaJsonTerritorios)
                    {
                        UseShellExecute = true
                    });
            }
        }

        private void BtnProcesar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_rutaS21))
            {
                // Solo los datos de contacto: no es una importación de actividad (la
                // semana no se marca como importada).
                if (!string.IsNullOrEmpty(_rutaContactos))
                {
                    if (AbrirContactos(false))
                    {
                        this.DialogResult = false;
                        this.Close();
                    }

                    return;
                }

                MessageBox.Show("Debes seleccionar al menos el archivo S-21 (o solo la lista de contactos) para continuar.", "Archivo faltante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string contenidoS21 = System.IO.File.ReadAllText(_rutaS21).Trim();
                if (!contenidoS21.StartsWith("[")) contenidoS21 = "[" + contenidoS21 + "]";

                var opcionesConfig = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var listaPersonas = JsonSerializer.Deserialize<List<PersonaJson>>(contenidoS21, opcionesConfig);

                List<TerritorioJSON> listaTerritorios = null;

                if (!string.IsNullOrEmpty(_rutaTerritorios))
                {
                    string contenidoS13 = System.IO.File.ReadAllText(_rutaTerritorios).Trim();
                    if (!contenidoS13.StartsWith("[")) contenidoS13 = "[" + contenidoS13 + "]";
                    listaTerritorios = JsonSerializer.Deserialize<List<TerritorioJSON>>(contenidoS13, opcionesConfig);
                }

                if (listaPersonas != null)
                {
                    var syncDashboard = new SyncJsonWindow(_congreCodigo, listaPersonas, listaTerritorios, _mesReferencia);
                    syncDashboard.Owner = Window.GetWindow(this);

                    if (syncDashboard.ShowDialog() == true)
                    {
                        if (!string.IsNullOrEmpty(_rutaContactos))
                        {
                            AbrirContactos(true);
                        }

                        this.DialogResult = true;
                        this.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error inesperado al leer los archivos JSON:\n{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
