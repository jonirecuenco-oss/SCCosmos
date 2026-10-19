using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SCCosmos
{
    // =========================================================
    // RSC · REUNIONES PARA EL SERVICIO DEL CAMPO
    //
    // Página principal: lista de RSC guardadas y buscador por
    // día (mañana y tarde). Editor (página completa): marcar
    // asistentes, indicar arreglos y formar los grupos.
    //
    // Cada cambio se guarda al momento (sin botón de guardar).
    // =========================================================

    public partial class RscPage : Page, IActualizablePorSincro
    {
        private static readonly SolidColorBrush FondoTarjeta =
            new SolidColorBrush(Color.FromRgb(0x17, 0x1A, 0x20));

        private static readonly SolidColorBrush FondoFila =
            new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1B));

        private static readonly SolidColorBrush FondoChip =
            new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x2B));

        private static readonly SolidColorBrush BordeSuave =
            new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x38));

        private static readonly SolidColorBrush TextoGris =
            new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0x99));

        private static readonly SolidColorBrush TextoGrisClaro =
            new SolidColorBrush(Color.FromRgb(0xB5, 0xBC, 0xC4));

        private static readonly SolidColorBrush TextoTitulo =
            new SolidColorBrush(Color.FromRgb(0x8F, 0xB8, 0xE0));

        private static readonly SolidColorBrush TextoAviso =
            new SolidColorBrush(Color.FromRgb(0xE8, 0xB2, 0x5A));

        private static readonly CultureInfo CulturaEs =
            new CultureInfo("es-ES");


        private bool _cargado;

        private RscReunion _reunion;
        private List<RscPersona> _asistentes = new List<RscPersona>();
        private List<RscGrupo> _grupos = new List<RscGrupo>();
        private Dictionary<int, string> _territorios =
            new Dictionary<int, string>();
        private List<Persona> _personasCong = new List<Persona>();

        private RscPersona _personaArreglo;
        private RscPersona _personaMover;

        private readonly Dictionary<CheckBox, Persona> _checkPersonasCong =
            new Dictionary<CheckBox, Persona>();

        private readonly Dictionary<CheckBox, int> _checkVirtuales =
            new Dictionary<CheckBox, int>();


        public RscPage()
        {
            InitializeComponent();

            // Ids estables para las pruebas de interfaz.
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PckFiltroDia, "PckFiltroDia");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                BtnVerTodas, "BtnVerTodas");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PckFechaNueva, "PckFechaNueva");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                CmbCongNueva, "CmbCongNueva");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelArreglosDetectados, "PanelArreglosDetectados");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelAsistentes, "PanelAsistentes");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelGrupos, "PanelGrupos");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelPinnedPersonas, "PanelPinnedPersonas");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                TxtBuscarPersona, "TxtBuscarPersona");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelListaPersonas, "PanelListaPersonas");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelArregloCon, "PanelArregloCon");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                TogAceptaGrupo, "TogAceptaGrupo");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                PanelMoverRadios, "PanelMoverRadios");

            this.Loaded += RscPage_Loaded;
        }


        private void RscPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cargado)
                {
                    return;
                }

                _cargado = true;

                DatabaseHelper.AsegurarTablasRsc();

                CargarCongregaciones();
                RefrescarLista();
            }
            catch
            {
            }
        }


        /// <summary>
        /// El motor de sincronización avisa cuando llegan cambios
        /// de otro dispositivo: refrescamos lista y editor.
        /// </summary>
        public void DatosActualizados()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(DatosActualizados);
                    return;
                }

                RefrescarLista();

                bool algunModalAbierto =
                    OverlayArreglo.Visibility == Visibility.Visible
                    || OverlayMover.Visibility == Visibility.Visible
                    || OverlayElegirPersonas.Visibility == Visibility.Visible
                    || OverlayNuevaRsc.Visibility == Visibility.Visible;

                if (OverlayEditor.Visibility == Visibility.Visible
                    && _reunion != null
                    && !algunModalAbierto)
                {
                    CargarEditorDesdeDb();
                }
            }
            catch
            {
            }
        }


        // =====================================================
        // LISTA Y BUSCADOR POR DÍA
        // =====================================================

        private void CargarCongregaciones()
        {
            try
            {
                CmbCongNueva.Items.Clear();

                foreach (var congregacion in
                         DatabaseHelper.ObtenerCongregaciones()
                             .OrderBy(c => c.Nombre))
                {
                    CmbCongNueva.Items.Add(
                        new ComboBoxItem
                        {
                            Content = congregacion.Nombre,
                            Tag = congregacion.Codigo
                        });
                }
            }
            catch
            {
            }
        }


        private void PckFiltroDia_SelectedDateChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_cargado)
            {
                RefrescarLista();
            }
        }


        private void BtnVerTodas_Click(
            object sender,
            RoutedEventArgs e)
        {
            PckFiltroDia.SelectedDate = null;

            RefrescarLista();
        }


        private void RefrescarLista()
        {
            try
            {
                if (!_cargado)
                {
                    return;
                }

                PanelReuniones.Children.Clear();

                DateTime? filtro = PckFiltroDia.SelectedDate;

                List<RscReunion> reuniones;

                if (filtro.HasValue)
                {
                    reuniones = DatabaseHelper
                        .ObtenerReunionesRscPorFecha(
                            filtro.Value.ToString("yyyy-MM-dd"));

                    TxtInfoFiltro.Text =
                        "Mostrando: " + DescripcionDia(filtro.Value)
                        + " · mañana y tarde";
                }
                else
                {
                    reuniones = DatabaseHelper
                        .ObtenerTodasReunionesRsc();

                    TxtInfoFiltro.Text = reuniones.Count == 1
                        ? "1 RSC guardada"
                        : reuniones.Count + " RSC guardadas";
                }

                if (reuniones.Count == 0)
                {
                    var vacio = new TextBlock
                    {
                        Text = filtro.HasValue
                            ? "No se hizo ninguna RSC ese día."
                            : "Todavía no hay ninguna RSC. Pulsa «＋ Nueva RSC» para preparar la primera.",
                        Foreground = TextoGris,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(2, 10, 0, 0)
                    };

                    PanelReuniones.Children.Add(vacio);
                    return;
                }

                bool expandida = filtro.HasValue;

                foreach (var reunion in reuniones)
                {
                    PanelReuniones.Children.Add(
                        ConstruirTarjetaReunion(reunion, expandida));
                }
            }
            catch
            {
            }
        }


        private Border ConstruirTarjetaReunion(
            RscReunion reunion,
            bool expandida)
        {
            var borde = new Border
            {
                Background = FondoTarjeta,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var pila = new StackPanel();

            var cabecera = new Grid();

            cabecera.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            cabecera.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var textos = new StackPanel();

            string fechaVisual;

            if (DateTime.TryParse(reunion.Fecha, out DateTime fecha))
            {
                fechaVisual = DescripcionDia(fecha);
            }
            else
            {
                fechaVisual = reunion.Fecha;
            }

            textos.Children.Add(new TextBlock
            {
                Text = fechaVisual,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });

            string congregacion = string.IsNullOrWhiteSpace(
                reunion.CongregacionNombre)
                ? "(sin congregación)"
                : reunion.CongregacionNombre;

            textos.Children.Add(new TextBlock
            {
                Text = reunion.Turno + " · " + congregacion
                    + " · " + reunion.NumeroGrupos
                    + (reunion.NumeroGrupos == 1 ? " grupo" : " grupos")
                    + " · " + reunion.NumeroPersonas + " para predicar",
                Foreground = TextoGrisClaro,
                FontSize = 12.5,
                Margin = new Thickness(0, 3, 0, 0)
            });

            cabecera.Children.Add(textos);

            var acciones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var abrir = Boton("Abrir", secundario: true);

            abrir.Margin = new Thickness(0, 0, 8, 0);

            System.Windows.Automation.AutomationProperties.SetName(
                abrir,
                "Abrir RSC del " + reunion.FechaVisual);

            var paraAbrir = reunion;

            abrir.Click += (s, e) => AbrirEditor(paraAbrir);

            var eliminar = Boton("Eliminar", secundario: true);

            System.Windows.Automation.AutomationProperties.SetName(
                eliminar,
                "Eliminar RSC del " + reunion.FechaVisual);

            var paraEliminar = reunion;

            eliminar.Click += (s, e) => EliminarReunion(paraEliminar);

            acciones.Children.Add(abrir);
            acciones.Children.Add(eliminar);

            Grid.SetColumn(acciones, 1);

            cabecera.Children.Add(acciones);

            pila.Children.Add(cabecera);

            // Vista expandida (buscador por día): grupos formados.
            if (expandida)
            {
                var vistas = DatabaseHelper.ObtenerGruposVistaRsc(
                    reunion.Id);

                if (vistas.Count == 0)
                {
                    pila.Children.Add(new TextBlock
                    {
                        Text = "Sin grupos formados todavía.",
                        Foreground = TextoGris,
                        FontSize = 12.5,
                        Margin = new Thickness(0, 10, 0, 0)
                    });
                }
                else
                {
                    var panelGrupos = new StackPanel
                    {
                        Margin = new Thickness(0, 10, 0, 0)
                    };

                    foreach (var vista in vistas)
                    {
                        var bloque = new StackPanel
                        {
                            Margin = new Thickness(0, 0, 0, 6)
                        };

                        bloque.Children.Add(new TextBlock
                        {
                            Text = vista.Titulo
                                + (string.IsNullOrWhiteSpace(vista.Subtitulo)
                                    ? ""
                                    : " · " + vista.Subtitulo),
                            Foreground = TextoTitulo,
                            FontSize = 13,
                            FontWeight = FontWeights.SemiBold
                        });

                        bloque.Children.Add(new TextBlock
                        {
                            Text = string.Join(", ", vista.Miembros),
                            Foreground = TextoGrisClaro,
                            FontSize = 12.5,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0)
                        });

                        if (!string.IsNullOrWhiteSpace(vista.Notas))
                        {
                            bloque.Children.Add(new TextBlock
                            {
                                Text = vista.Notas,
                                Foreground = TextoAviso,
                                FontSize = 12,
                                Margin = new Thickness(0, 2, 0, 0)
                            });
                        }

                        panelGrupos.Children.Add(bloque);
                    }

                    pila.Children.Add(panelGrupos);
                }
            }

            borde.Child = pila;

            return borde;
        }


        private static string DescripcionDia(DateTime fecha)
        {
            string texto = fecha.ToString(
                "ddd d 'de' MMMM 'de' yyyy",
                CulturaEs);

            if (texto.Length > 1)
            {
                texto = char.ToUpper(
                    texto[0],
                    CulturaEs) + texto.Substring(1);
            }

            return texto;
        }


        // =====================================================
        // NUEVA RSC
        // =====================================================

        private void BtnNuevaRsc_Click(
            object sender,
            RoutedEventArgs e)
        {
            PckFechaNueva.SelectedDate = DateTime.Today;

            RadTurnoManana.IsChecked = true;
            RadTurnoTarde.IsChecked = false;

            CmbCongNueva.SelectedItem = null;

            OverlayNuevaRsc.Visibility = Visibility.Visible;
        }


        private void BtnCancelarNueva_Click(
            object sender,
            RoutedEventArgs e)
        {
            OverlayNuevaRsc.Visibility = Visibility.Collapsed;
        }


        private void BtnCrearNueva_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (!PckFechaNueva.SelectedDate.HasValue)
                {
                    MessageBox.Show(
                        "Elige el día de la RSC.",
                        "RSC");

                    return;
                }

                if (!(CmbCongNueva.SelectedItem is ComboBoxItem item)
                    || item.Tag == null)
                {
                    MessageBox.Show(
                        "Elige la congregación.",
                        "RSC");

                    return;
                }

                string codigo = item.Tag.ToString();
                string nombre = item.Content?.ToString() ?? "";

                string turno =
                    RadTurnoTarde.IsChecked == true
                        ? "Tarde"
                        : "Mañana";

                string fecha = PckFechaNueva.SelectedDate.Value
                    .ToString("yyyy-MM-dd");

                OverlayNuevaRsc.Visibility = Visibility.Collapsed;

                var existente = DatabaseHelper.ObtenerReunionRsc(
                    codigo,
                    fecha,
                    turno);

                if (existente != null)
                {
                    MessageBox.Show(
                        "Ya existe una RSC de " + nombre
                        + " ese día por la " + turno.ToLower()
                        + "; se abrirá la que ya hay.",
                        "RSC");

                    AbrirEditor(existente);
                    return;
                }

                var nueva = new RscReunion
                {
                    CongregacionCodigo = codigo,
                    CongregacionNombre = nombre,
                    Fecha = fecha,
                    Turno = turno
                };

                DatabaseHelper.GuardarReunionRsc(nueva);

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Creó la RSC de " + nombre + " ("
                    + nueva.FechaVisual + " · " + turno + ")");

                AbrirEditor(nueva);

                // El superintendente y su esposa van marcados por defecto
                // en toda RSC nueva.
                AnadirSuperintendenteYEsposaPorDefecto();

                RefrescarLista();
            }
            catch
            {
            }
        }


        // =====================================================
        // EDITOR
        // =====================================================

        private void AbrirEditor(RscReunion reunion)
        {
            try
            {
                _reunion = reunion;

                TxtTituloEditor.Text =
                    "RSC · " + reunion.TituloCompleto;

                CargarEditorDesdeDb();

                OverlayEditor.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        }


        private void CargarEditorDesdeDb()
        {
            if (_reunion == null)
            {
                return;
            }

            _territorios = DatabaseHelper.ObtenerTerritoriosPorPersona(
                _reunion.CongregacionCodigo);

            _personasCong = DatabaseHelper.ObtenerPersonasPorCongregacion(
                _reunion.CongregacionCodigo);

            _asistentes = DatabaseHelper.ObtenerAsistentesRsc(_reunion.Id);

            foreach (var asistente in _asistentes)
            {
                AsignarTerritorio(asistente);
            }

            var gruposGuardados = DatabaseHelper.ObtenerGruposRsc(
                _reunion.Id);

            // Repartir el grupo de cada persona desde los grupos guardados.
            var numeroPorPersona = new Dictionary<int, int>();

            foreach (var grupo in gruposGuardados)
            {
                foreach (var miembro in grupo.Miembros)
                {
                    if (!numeroPorPersona.ContainsKey(miembro.PersonaId))
                    {
                        numeroPorPersona[miembro.PersonaId] = grupo.Numero;
                    }
                }
            }

            foreach (var asistente in _asistentes)
            {
                asistente.GrupoNumero =
                    numeroPorPersona.TryGetValue(
                        asistente.PersonaId,
                        out int numero)
                            ? numero
                            : 0;
            }

            _grupos = gruposGuardados;

            ReconstruirColecciones();

            RefrescarAsistentes();
            RefrescarGrupos();
            CargarArreglosDetectados();
        }


        private void BtnVolverEditor_Click(
            object sender,
            RoutedEventArgs e)
        {
            _reunion = null;

            OverlayEditor.Visibility = Visibility.Collapsed;

            RefrescarLista();
        }


        private void BtnEliminarRsc_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_reunion != null)
            {
                EliminarReunion(_reunion);
            }
        }


        private void EliminarReunion(RscReunion reunion)
        {
            try
            {
                var respuesta = MessageBox.Show(
                    "¿Eliminar la RSC de "
                    + reunion.CongregacionNombre + " ("
                    + reunion.FechaVisual + " · " + reunion.Turno
                    + ")?\n\nSe borrarán también sus asistentes y grupos.",
                    "Eliminar RSC",
                    MessageBoxButton.YesNo);

                if (respuesta != MessageBoxResult.Yes)
                {
                    return;
                }

                DatabaseHelper.EliminarReunionRsc(reunion.Id);

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Eliminó la RSC de " + reunion.CongregacionNombre
                    + " (" + reunion.FechaVisual + " · "
                    + reunion.Turno + ")");

                if (_reunion != null && _reunion.Id == reunion.Id)
                {
                    _reunion = null;
                    OverlayEditor.Visibility = Visibility.Collapsed;
                }

                RefrescarLista();
            }
            catch
            {
            }
        }


        private void GuardarTodo()
        {
            try
            {
                if (_reunion == null)
                {
                    return;
                }

                DatabaseHelper.GuardarRscCompleta(
                    _reunion,
                    _asistentes,
                    _grupos);
            }
            catch
            {
            }
        }


        private void AsignarTerritorio(RscPersona persona)
        {
            if (persona.PersonaId > 0
                && _territorios.TryGetValue(
                    persona.PersonaId,
                    out string territorio))
            {
                persona.Territorio = territorio;
            }
        }


        private void ReconstruirColecciones()
        {
            foreach (var grupo in _grupos)
            {
                grupo.Miembros.Clear();

                foreach (var asistente in _asistentes.Where(
                             p => p.SaleEnGrupos
                                  && p.GrupoNumero == grupo.Numero))
                {
                    grupo.Miembros.Add(asistente);
                }

                grupo.Refrescar();
            }
        }


        // =====================================================
        // ASISTENTES
        // =====================================================

        private void RefrescarAsistentes()
        {
            PanelAsistentes.Children.Clear();

            TxtSinAsistentes.Visibility = _asistentes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (_asistentes.Count == 0)
            {
                TxtContadorAsistentes.Text = "";
            }
            else
            {
                int predican = _asistentes.Count(a => a.SaleEnGrupos);

                TxtContadorAsistentes.Text =
                    "· " + _asistentes.Count + " marcados · "
                    + predican + " salen a predicar";
            }

            foreach (var persona in _asistentes)
            {
                PanelAsistentes.Children.Add(
                    ConstruirFilaAsistente(persona));
            }
        }


        private Border ConstruirFilaAsistente(RscPersona persona)
        {
            var borde = new Border
            {
                Background = FondoFila,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var rejilla = new Grid();

            rejilla.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            rejilla.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            rejilla.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            rejilla.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            // Nombre + información.
            var textos = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var lineaNombre = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            lineaNombre.Children.Add(new TextBlock
            {
                Text = persona.Nombre,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            });

            if (persona.PersonaId == RscVirtuales.Superintendente)
            {
                lineaNombre.Children.Add(Etiqueta("SC"));
            }
            else if (persona.PersonaId == RscVirtuales.Esposa)
            {
                lineaNombre.Children.Add(Etiqueta("esposa del SC"));
            }

            textos.Children.Add(lineaNombre);

            var detalles = new System.Collections.Generic.List<string>();

            if (persona.TieneTerritorio)
            {
                detalles.Add("territorio " + persona.Territorio);
            }

            if (persona.GrupoPredicacion > 0)
            {
                detalles.Add("grupo de servicio " + persona.GrupoPredicacion);
            }

            if (detalles.Count > 0)
            {
                textos.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", detalles),
                    Foreground = TextoGris,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            rejilla.Children.Add(textos);

            // Predica (sí/no).
            var predica = new Wpf.Ui.Controls.ToggleSwitch
            {
                Content = "Predica",
                IsChecked = persona.SaleAPredicar,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };

            var personaPredica = persona;

            predica.Checked += (s, e) =>
            {
                personaPredica.SaleAPredicar = true;

                GuardarTodo();
                RefrescarGrupos();
            };

            predica.Unchecked += (s, e) =>
            {
                personaPredica.SaleAPredicar = false;
                personaPredica.GrupoNumero = 0;

                ReconstruirColecciones();
                GuardarTodo();
                RefrescarAsistentes();
                RefrescarGrupos();
            };

            Grid.SetColumn(predica, 1);

            rejilla.Children.Add(predica);

            // Arreglo.
            var arreglo = Boton(TextoArreglo(persona), secundario: true);

            arreglo.Margin = new Thickness(14, 0, 0, 0);
            arreglo.IsEnabled = persona.SaleAPredicar;

            System.Windows.Automation.AutomationProperties.SetName(
                arreglo,
                "Arreglo de " + persona.Nombre);

            var personaArreglo = persona;

            arreglo.Click += (s, e) => AbrirArreglo(personaArreglo);

            Grid.SetColumn(arreglo, 2);

            rejilla.Children.Add(arreglo);

            // Quitar de la RSC.
            var quitar = Boton("Quitar", secundario: true);

            quitar.Margin = new Thickness(10, 0, 0, 0);

            System.Windows.Automation.AutomationProperties.SetName(
                quitar,
                "Quitar a " + persona.Nombre + " de la RSC");

            var personaQuitar = persona;

            quitar.Click += (s, e) => QuitarAsistente(personaQuitar);

            Grid.SetColumn(quitar, 3);

            rejilla.Children.Add(quitar);

            borde.Child = rejilla;

            return borde;
        }


        private static Border Etiqueta(string texto)
        {
            return new Border
            {
                Background = FondoChip,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = texto,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(0x9F, 0xC7, 0xEC)),
                    FontSize = 11
                }
            };
        }


        private string TextoArreglo(RscPersona persona)
        {
            if (!persona.TieneArreglo)
            {
                return persona.AceptaGrupo
                    ? "Arreglo"
                    : "Solo/a · sin grupo";
            }

            var nombres = persona.ArregloConPersonaIds
                .Select(id =>
                {
                    var pareja = _asistentes.FirstOrDefault(
                        p => p.PersonaId == id);

                    return pareja != null
                        ? pareja.Nombre
                        : "otro hermano";
                })
                .ToList();

            string con = nombres.Count == 1
                ? nombres[0]
                : nombres[0] + " +" + (nombres.Count - 1);

            return "Con " + con
                + (persona.AceptaGrupo ? "" : " · sin grupo mayor");
        }


        private void QuitarAsistente(RscPersona persona)
        {
            try
            {
                _asistentes.Remove(persona);

                // Quien la citara en su arreglo pierde ese vínculo.
                foreach (var otra in _asistentes)
                {
                    if (otra.ArregloConPersonaIds == null)
                    {
                        continue;
                    }

                    otra.ArregloConPersonaIds.RemoveAll(
                        id => id == persona.PersonaId);

                    if (otra.ArregloConPersonaIds.Count == 0)
                    {
                        otra.AceptaGrupo = true;
                    }
                }

                ReconstruirColecciones();
                GuardarTodo();
                RefrescarAsistentes();
                RefrescarGrupos();

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Quitó a " + persona.Nombre + " de la RSC de "
                    + Contexto());
            }
            catch
            {
            }
        }


        private string Contexto()
        {
            if (_reunion == null)
            {
                return "";
            }

            return _reunion.CongregacionNombre + " ("
                + _reunion.FechaVisual + " · " + _reunion.Turno + ")";
        }


        // =====================================================
        // AÑADIR PERSONAS
        // =====================================================

        private void BtnAnadirPersonas_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                TxtBuscarPersona.Text = "";

                TxtSubtituloPicker.Text =
                    "Marcadas de " + (_reunion != null
                        ? _reunion.CongregacionNombre
                        : "") + ". El SC y su esposa están arriba.";

                ConstruirPinnedPicker();
                RefrescarListaPicker("");

                OverlayElegirPersonas.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        }


        private void ConstruirPinnedPicker()
        {
            PanelPinnedPersonas.Children.Clear();
            _checkVirtuales.Clear();

            string nombreSc = (
                DatabaseHelper.ObtenerConfiguracion("NombreSC", "")
                + " "
                + DatabaseHelper.ObtenerConfiguracion("ApellidoSC", ""))
                .Trim();

            string nombreEsposa = (
                DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "")
                + " "
                + DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", ""))
                .Trim();

            PanelPinnedPersonas.Children.Add(new TextBlock
            {
                Text = "DEL CIRCUITO",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(0x7F, 0xA6, 0xC6)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            if (!string.IsNullOrWhiteSpace(nombreSc))
            {
                PanelPinnedPersonas.Children.Add(
                    FilaPickerVirtual(
                        RscVirtuales.Superintendente,
                        nombreSc + " (superintendente de circuito)"));
            }

            if (!string.IsNullOrWhiteSpace(nombreEsposa))
            {
                PanelPinnedPersonas.Children.Add(
                    FilaPickerVirtual(
                        RscVirtuales.Esposa,
                        nombreEsposa + " (su esposa)"));
            }
        }


        private Border FilaPickerVirtual(
            int personaId,
            string texto)
        {
            bool yaEsta = _asistentes.Any(
                a => a.PersonaId == personaId);

            var check = new CheckBox
            {
                Content = texto + (yaEsta ? "  —  ya está" : ""),
                Foreground = Brushes.White,
                IsEnabled = !yaEsta,
                // Marcadas por defecto: el SC y su esposa siempre van en la
                // RSC salvo que se desmarquen a propósito.
                IsChecked = !yaEsta
            };

            _checkVirtuales[check] = personaId;

            return new Border
            {
                Background = FondoFila,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Child = check
            };
        }


        private void TxtBuscarPersona_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            RefrescarListaPicker(TxtBuscarPersona.Text);
        }


        private void RefrescarListaPicker(string filtro)
        {
            PanelListaPersonas.Children.Clear();
            _checkPersonasCong.Clear();

            string buscado = NormalizarTexto(filtro ?? "");

            foreach (var persona in _personasCong)
            {
                string nombre = NombreMostrar(persona);

                if (buscado.Length > 0
                    && !NormalizarTexto(nombre).Contains(buscado))
                {
                    continue;
                }

                bool yaEsta = _asistentes.Any(
                    a => a.PersonaId == persona.Id);

                var check = new CheckBox
                {
                    Content = nombre + (yaEsta ? "  —  ya está" : ""),
                    Foreground = Brushes.White,
                    IsEnabled = !yaEsta,
                    Tag = persona.Id
                };

                _checkPersonasCong[check] = persona;

                PanelListaPersonas.Children.Add(new Border
                {
                    Background = FondoFila,
                    BorderBrush = BordeSuave,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 6),
                    Child = check
                });
            }
        }


        private static string NombreMostrar(Persona persona)
        {
            if (!string.IsNullOrWhiteSpace(persona.NombreComun))
            {
                return persona.NombreComun;
            }

            return persona.Nombre ?? "";
        }


        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return "";
            }

            string descompuesto = texto
                .Trim()
                .ToLowerInvariant()
                .Normalize(System.Text.NormalizationForm.FormD);

            var constructor = new System.Text.StringBuilder();

            foreach (char caracter in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(caracter)
                    != UnicodeCategory.NonSpacingMark)
                {
                    constructor.Append(caracter);
                }
            }

            return constructor.ToString()
                .Normalize(System.Text.NormalizationForm.FormC);
        }


        private void BtnCancelarPicker_Click(
            object sender,
            RoutedEventArgs e)
        {
            OverlayElegirPersonas.Visibility = Visibility.Collapsed;
        }


        private void BtnAnadirSeleccionadas_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                int anadidas = 0;

                // SC y esposa (personas virtuales del circuito).
                foreach (var par in _checkVirtuales)
                {
                    if (par.Key.IsChecked != true
                        || !par.Key.IsEnabled)
                    {
                        continue;
                    }

                    string nombre = par.Value
                        == RscVirtuales.Superintendente
                            ? (
                                DatabaseHelper.ObtenerConfiguracion("NombreSC", "")
                                + " "
                                + DatabaseHelper.ObtenerConfiguracion("ApellidoSC", ""))
                                .Trim()
                            : (
                                DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "")
                                + " "
                                + DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", ""))
                                .Trim();

                    if (AnadirAsistente(
                            par.Value,
                            nombre,
                            par.Value == RscVirtuales.Superintendente
                                ? "Hombre"
                                : "Mujer"))
                    {
                        anadidas++;
                    }
                }

                // Publicadores de la congregación.
                foreach (var par in _checkPersonasCong)
                {
                    if (par.Key.IsChecked != true
                        || !par.Key.IsEnabled)
                    {
                        continue;
                    }

                    if (AnadirAsistente(
                            par.Value.Id,
                            NombreMostrar(par.Value),
                            par.Value.Sexo))
                    {
                        anadidas++;
                    }
                }

                if (anadidas > 0)
                {
                    OrdenarAsistentes();

                    GuardarTodo();
                    RefrescarAsistentes();
                    RefrescarGrupos();

                    DatabaseHelper.AnotarActividad(
                        "RSC",
                        "Añadió " + anadidas
                        + (anadidas == 1
                            ? " persona"
                            : " personas")
                        + " a la RSC de " + Contexto());
                }

                OverlayElegirPersonas.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }


        private bool AnadirAsistente(
            int personaId,
            string nombre,
            string sexo = "")
        {
            if (_asistentes.Any(a => a.PersonaId == personaId))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                nombre = "Hermano/a " + personaId;
            }

            var nueva = new RscPersona
            {
                PersonaId = personaId,
                Nombre = nombre,
                Sexo = sexo ?? "",
                Asiste = true,
                SaleAPredicar = true,
                AceptaGrupo = true
            };

            AsignarTerritorio(nueva);

            _asistentes.Add(nueva);

            return true;
        }


        private void OrdenarAsistentes()
        {
            _asistentes = _asistentes
                .OrderBy(
                    a => a.Nombre,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }


        /// <summary>
        /// Marca por defecto al superintendente y a su esposa como
        /// asistentes de la RSC abierta (si aún no estaban).
        /// </summary>
        private void AnadirSuperintendenteYEsposaPorDefecto()
        {
            string nombreSc = (
                DatabaseHelper.ObtenerConfiguracion("NombreSC", "")
                + " "
                + DatabaseHelper.ObtenerConfiguracion("ApellidoSC", ""))
                .Trim();

            string nombreEsposa = (
                DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "")
                + " "
                + DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", ""))
                .Trim();

            bool anadida = false;

            if (!string.IsNullOrWhiteSpace(nombreSc))
            {
                anadida |= AnadirAsistente(
                    RscVirtuales.Superintendente,
                    nombreSc,
                    "Hombre");
            }

            if (!string.IsNullOrWhiteSpace(nombreEsposa))
            {
                anadida |= AnadirAsistente(
                    RscVirtuales.Esposa,
                    nombreEsposa,
                    "Mujer");
            }

            if (anadida)
            {
                OrdenarAsistentes();
                GuardarTodo();
                RefrescarAsistentes();
                RefrescarGrupos();
            }
        }


        // =====================================================
        // ARREGLOS
        // =====================================================

        private void AbrirArreglo(RscPersona persona)
        {
            try
            {
                _personaArreglo = persona;

                TxtArregloPersona.Text =
                    "Arreglo de " + persona.Nombre;

                PanelArregloCon.Children.Clear();

                foreach (var otra in _asistentes.Where(
                             p => p.PersonaId != persona.PersonaId))
                {
                    var check = new CheckBox
                    {
                        Content = otra.Nombre,
                        Foreground = Brushes.White,
                        IsChecked =
                            persona.ArregloConPersonaIds != null
                            && persona.ArregloConPersonaIds.Contains(
                                otra.PersonaId),
                        Tag = otra.PersonaId
                    };

                    PanelArregloCon.Children.Add(new Border
                    {
                        Background = FondoFila,
                        BorderBrush = BordeSuave,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 0, 0, 6),
                        Child = check
                    });
                }

                TogAceptaGrupo.IsChecked = persona.AceptaGrupo;

                OverlayArreglo.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        }


        private void BtnCancelarArreglo_Click(
            object sender,
            RoutedEventArgs e)
        {
            _personaArreglo = null;

            OverlayArreglo.Visibility = Visibility.Collapsed;
        }


        private void BtnQuitarArreglo_Click(
            object sender,
            RoutedEventArgs e)
        {
            var persona = _personaArreglo;

            if (persona == null)
            {
                return;
            }

            EstablecerArreglo(
                persona,
                new List<int>(),
                true);

            _personaArreglo = null;

            GuardarTodo();
            RefrescarAsistentes();

            DatabaseHelper.AnotarActividad(
                "RSC",
                "Quitó el arreglo de " + persona.Nombre
                + " en la RSC de " + Contexto());

            OverlayArreglo.Visibility = Visibility.Collapsed;
        }


        private void BtnGuardarArreglo_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var persona = _personaArreglo;

                if (persona == null)
                {
                    return;
                }

                var elegidos = new List<int>();

                foreach (var hijo in PanelArregloCon.Children)
                {
                    if (hijo is Border borde
                        && borde.Child is CheckBox check
                        && check.IsChecked == true
                        && check.Tag is int id)
                    {
                        elegidos.Add(id);
                    }
                }

                bool acepta = TogAceptaGrupo.IsChecked == true;

                EstablecerArreglo(persona, elegidos, acepta);

                GuardarTodo();
                RefrescarAsistentes();

                string descripcion;

                if (elegidos.Count == 0)
                {
                    descripcion = acepta
                        ? "dejó sin arreglo a " + persona.Nombre
                        : "marcó a " + persona.Nombre
                          + " para predicar solo/a (sin grupo)";
                }
                else
                {
                    var nombres = elegidos.Select(id =>
                    {
                        var pareja = _asistentes.FirstOrDefault(
                            p => p.PersonaId == id);

                        return pareja != null
                            ? pareja.Nombre
                            : "otro hermano";
                    });

                    descripcion = "puso a " + persona.Nombre
                        + " para predicar con "
                        + string.Join(", ", nombres)
                        + (acepta ? "" : " (sin grupo más grande)");
                }

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "En la RSC de " + Contexto() + ", "
                    + descripcion);

                _personaArreglo = null;

                OverlayArreglo.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }


        /// <summary>
        /// Deja el arreglo de una persona: ella y quienes elija quedan
        /// como un conjunto que predica junto (y acepta o no ir en un
        /// grupo más grande). Los vínculos que otras personas tenían
        /// con el conjunto se limpian.
        /// </summary>
        private void EstablecerArreglo(
            RscPersona persona,
            List<int> conIds,
            bool acepta)
        {
            if (persona == null)
            {
                return;
            }

            var conjunto = new List<int>
            {
                persona.PersonaId
            };

            foreach (int id in conIds ?? new List<int>())
            {
                if (id != persona.PersonaId
                    && !conjunto.Contains(id))
                {
                    conjunto.Add(id);
                }
            }

            // Fuera del conjunto, quien apunte a algún miembro
            // pierde ese vínculo.
            foreach (var otra in _asistentes)
            {
                if (conjunto.Contains(otra.PersonaId)
                    || otra.ArregloConPersonaIds == null)
                {
                    continue;
                }

                otra.ArregloConPersonaIds.RemoveAll(
                    id => conjunto.Contains(id));
            }

            // Malla completa dentro del conjunto.
            foreach (var miembro in _asistentes.Where(
                         a => conjunto.Contains(a.PersonaId)))
            {
                miembro.AceptaGrupo = acepta;

                miembro.ArregloConPersonaIds = conjunto
                    .Where(id => id != miembro.PersonaId)
                    .ToList();
            }
        }


        // =====================================================
        // ARREGLOS YA APUNTADOS EN EL PROGRAMA DE LA VISITA
        // =====================================================

        private void CargarArreglosDetectados()
        {
            PanelArreglosDetectados.Children.Clear();

            if (_reunion == null)
            {
                BordeArreglos.Visibility = Visibility.Collapsed;
                return;
            }

            DateTime fecha;

            if (!DateTime.TryParse(_reunion.Fecha, out fecha))
            {
                fecha = DateTime.Today;
            }

            var detectados = DatabaseHelper.ObtenerArreglosDelDia(
                _reunion.CongregacionCodigo,
                fecha,
                _reunion.Turno);

            BordeArreglos.Visibility = detectados.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            foreach (var detectado in detectados)
            {
                PanelArreglosDetectados.Children.Add(
                    ConstruirChipArregloDetectado(detectado));
            }
        }


        private Grid ConstruirChipArregloDetectado(
            RscArregloDetectado detectado)
        {
            var rejilla = new Grid
            {
                Margin = new Thickness(0, 3, 0, 3)
            };

            rejilla.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            rejilla.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            string companero = string.IsNullOrWhiteSpace(
                detectado.CompaneroNombre)
                ? "un hermano (nº " + detectado.CompaneroId + ")"
                : detectado.CompaneroNombre;

            string texto =
                "El programa apunta: " + detectado.Responsable
                + " predicará con " + companero;

            var detalle = new List<string>();

            if (!string.IsNullOrWhiteSpace(detectado.Turno))
            {
                detalle.Add(detectado.Turno.Trim());
            }

            if (!string.IsNullOrWhiteSpace(detectado.Hora))
            {
                detalle.Add(detectado.Hora.Trim());
            }

            if (detalle.Count > 0)
            {
                texto += " (" + string.Join(" · ", detalle) + ")";
            }

            rejilla.Children.Add(new TextBlock
            {
                Text = texto,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(0xC9, 0xD8, 0xE6)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            var boton = Boton("Añadir a la RSC", secundario: true);

            boton.Margin = new Thickness(12, 0, 0, 0);

            var paraAnadir = detectado;

            boton.Click += (s, e) =>
                AplicarArregloDetectado(paraAnadir, boton);

            Grid.SetColumn(boton, 1);

            rejilla.Children.Add(boton);

            return rejilla;
        }


        private void AplicarArregloDetectado(
            RscArregloDetectado detectado,
            Wpf.Ui.Controls.Button boton)
        {
            try
            {
                bool esSc = detectado.ParaSuperintendente;

                int personaId = esSc
                    ? RscVirtuales.Superintendente
                    : RscVirtuales.Esposa;

                string nombrePersona = esSc
                    ? (
                        DatabaseHelper.ObtenerConfiguracion("NombreSC", "")
                        + " "
                        + DatabaseHelper.ObtenerConfiguracion("ApellidoSC", ""))
                        .Trim()
                    : (
                        DatabaseHelper.ObtenerConfiguracion("NombreEsposaSC", "")
                        + " "
                        + DatabaseHelper.ObtenerConfiguracion("ApellidoEsposaSC", ""))
                        .Trim();

                AnadirAsistente(
                    personaId,
                    nombrePersona,
                    esSc ? "Hombre" : "Mujer");

                var personaCompanero = _personasCong.FirstOrDefault(
                    p => p.Id == detectado.CompaneroId);

                string nombreCompanero = personaCompanero != null
                    ? NombreMostrar(personaCompanero)
                    : (string.IsNullOrWhiteSpace(detectado.CompaneroNombre)
                        ? "Hermano/a " + detectado.CompaneroId
                        : detectado.CompaneroNombre);

                AnadirAsistente(
                    detectado.CompaneroId,
                    nombreCompanero,
                    personaCompanero?.Sexo ?? "");

                var persona = _asistentes.First(
                    a => a.PersonaId == personaId);

                EstablecerArreglo(
                    persona,
                    new List<int> { detectado.CompaneroId },
                    true);

                OrdenarAsistentes();

                GuardarTodo();
                RefrescarAsistentes();
                RefrescarGrupos();

                boton.Content = "Añadido ✔";
                boton.IsEnabled = false;

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Añadió el arreglo del programa a la RSC de "
                    + Contexto() + ": " + nombrePersona
                    + " con " + nombreCompanero);
            }
            catch
            {
            }
        }


        // =====================================================
        // FORMAR Y GESTIONAR GRUPOS
        // =====================================================

        private void BtnFormarGrupos_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (_reunion == null)
                {
                    return;
                }

                if (_asistentes.Count(a => a.SaleEnGrupos) == 0)
                {
                    MessageBox.Show(
                        "No hay nadie marcado que salga a predicar.\n\n"
                        + "Marca a los asistentes y deja activado «Predica».",
                        "RSC");

                    return;
                }

                if (_grupos.Count > 0)
                {
                    var respuesta = MessageBox.Show(
                        "Ya hay grupos formados. ¿Quieres rehacerlos?\n\n"
                        + "Se sustituirán por los nuevos grupos.",
                        "RSC",
                        MessageBoxButton.YesNo);

                    if (respuesta != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // Conservar los territorios ya escritos, por número.
                var territoriosPrevios = _grupos
                    .Where(g => g.TieneTerritorio)
                    .GroupBy(g => g.Numero)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.First().Territorio);

                var nuevos = RscMotor.FormarGrupos(
                    _asistentes,
                    out List<string> avisos,
                    null);

                foreach (var grupo in nuevos)
                {
                    grupo.RscReunionId = _reunion.Id;

                    if (territoriosPrevios.TryGetValue(
                            grupo.Numero,
                            out string territorio))
                    {
                        grupo.Territorio = territorio;
                    }
                }

                _grupos = nuevos;

                ReconstruirColecciones();
                GuardarTodo();
                RefrescarGrupos();

                if (avisos.Count > 0)
                {
                    TxtAvisosGrupos.Text = string.Join(
                        "   ",
                        avisos.Select(a => "• " + a));

                    TxtAvisosGrupos.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtAvisosGrupos.Text = "";
                    TxtAvisosGrupos.Visibility = Visibility.Collapsed;
                }

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Formó " + nuevos.Count
                    + (nuevos.Count == 1 ? " grupo" : " grupos")
                    + " para la RSC de " + Contexto());
            }
            catch
            {
            }
        }


        private void RefrescarGrupos()
        {
            PanelGrupos.Children.Clear();

            int predicadores = _asistentes.Count(a => a.SaleEnGrupos);

            int sinGrupo = _asistentes.Count(
                a => a.SaleEnGrupos && a.GrupoNumero == 0);

            int enGrupos = predicadores - sinGrupo;

            if (_grupos.Count == 0)
            {
                TxtResumenGrupos.Text = "";
            }
            else
            {
                TxtResumenGrupos.Text =
                    (_grupos.Count == 1 ? "1 grupo" : _grupos.Count + " grupos")
                    + " · " + enGrupos + " en grupos"
                    + (sinGrupo > 0 ? " · " + sinGrupo + " sin grupo" : "");
            }

            TxtSinGrupos.Visibility =
                _grupos.Count == 0 && sinGrupo == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            foreach (var grupo in _grupos)
            {
                PanelGrupos.Children.Add(ConstruirTarjetaGrupo(grupo));
            }

            if (sinGrupo > 0)
            {
                PanelGrupos.Children.Add(ConstruirTarjetaSinGrupo());
            }
        }


        private Border ConstruirTarjetaGrupo(RscGrupo grupo)
        {
            var borde = new Border
            {
                Background = FondoTarjeta,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var pila = new StackPanel();

            // Cabecera del grupo.
            var cabecera = new Grid();

            cabecera.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            cabecera.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var titulo = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            titulo.Children.Add(new TextBlock
            {
                Text = "Grupo " + grupo.Numero,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            titulo.Children.Add(new TextBlock
            {
                Text = "· " + grupo.Miembros.Count
                    + (grupo.Miembros.Count == 1
                        ? " persona"
                        : " personas"),
                Foreground = TextoGrisClaro,
                FontSize = 12.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            if (grupo.BajoMinimo)
            {
                titulo.Children.Add(new TextBlock
                {
                    Text = "⚠",
                    Foreground = TextoAviso,
                    FontSize = 14,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = grupo.Aviso
                });
            }

            cabecera.Children.Add(titulo);

            var quitarGrupo = Boton("Quitar grupo", secundario: true);

            System.Windows.Automation.AutomationProperties.SetName(
                quitarGrupo,
                "Quitar grupo " + grupo.Numero);

            var grupoQuitar = grupo;

            quitarGrupo.Click += (s, e) => QuitarGrupo(grupoQuitar);

            Grid.SetColumn(quitarGrupo, 1);

            cabecera.Children.Add(quitarGrupo);

            pila.Children.Add(cabecera);

            // Territorio del grupo + territorios de sus miembros.
            var lineaTerritorio = new Grid
            {
                Margin = new Thickness(0, 8, 0, 4)
            };

            lineaTerritorio.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            lineaTerritorio.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            lineaTerritorio.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            lineaTerritorio.Children.Add(new TextBlock
            {
                Text = "Territorio:",
                Foreground = TextoGris,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var cajaTerritorio = new TextBox
            {
                Text = grupo.Territorio,
                Width = 220,
                Padding = new Thickness(6, 3, 6, 3),
                Background = new SolidColorBrush(
                    Color.FromRgb(0x1E, 0x22, 0x28)),
                Foreground = Brushes.White,
                BorderBrush = BordeSuave,
                CaretBrush = Brushes.White
            };

            var grupoTerritorio = grupo;

            cajaTerritorio.LostFocus += (s, e) =>
            {
                grupoTerritorio.Territorio =
                    (cajaTerritorio.Text ?? "").Trim();

                GuardarTodo();
            };

            Grid.SetColumn(cajaTerritorio, 1);

            lineaTerritorio.Children.Add(cajaTerritorio);

            if (!string.IsNullOrWhiteSpace(grupo.TerritoriosMiembros))
            {
                var info = new TextBlock
                {
                    Text = grupo.TerritoriosMiembros,
                    Foreground = TextoGris,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = grupo.TerritoriosMiembros
                };

                Grid.SetColumn(info, 2);

                lineaTerritorio.Children.Add(info);
            }

            pila.Children.Add(lineaTerritorio);

            // Miembros.
            var chips = new WrapPanel
            {
                Margin = new Thickness(0, 6, 0, 0)
            };

            foreach (var miembro in grupo.Miembros)
            {
                chips.Children.Add(ConstruirChipMiembro(miembro, enGrupo: true));
            }

            pila.Children.Add(chips);

            // Notas del grupo (por ejemplo, arreglos cerrados).
            if (!string.IsNullOrWhiteSpace(grupo.Notas))
            {
                pila.Children.Add(new TextBlock
                {
                    Text = grupo.Notas,
                    Foreground = TextoAviso,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            borde.Child = pila;

            return borde;
        }


        private Border ConstruirTarjetaSinGrupo()
        {
            var borde = new Border
            {
                Background = FondoFila,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var pila = new StackPanel();

            pila.Children.Add(new TextBlock
            {
                Text = "Sin grupo",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });

            pila.Children.Add(new TextBlock
            {
                Text = "Predican, pero de momento no están en ningún grupo. Usa «↔» para meterlos en uno.",
                Foreground = TextoGris,
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });

            var chips = new WrapPanel();

            foreach (var persona in _asistentes.Where(
                         a => a.SaleEnGrupos && a.GrupoNumero == 0))
            {
                chips.Children.Add(
                    ConstruirChipMiembro(persona, enGrupo: false));
            }

            pila.Children.Add(chips);

            borde.Child = pila;

            return borde;
        }


        private Border ConstruirChipMiembro(
            RscPersona persona,
            bool enGrupo)
        {
            var contenido = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            contenido.Children.Add(new TextBlock
            {
                Text = persona.Nombre,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(0xE6, 0xEA, 0xF0)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (persona.TieneTerritorio)
            {
                contenido.Children.Add(new TextBlock
                {
                    Text = "🌍",
                    FontSize = 11.5,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Territorio: " + persona.Territorio
                });
            }

            if (persona.TieneArreglo
                || !persona.AceptaGrupo)
            {
                contenido.Children.Add(new TextBlock
                {
                    Text = "👥",
                    FontSize = 11.5,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = TextoArreglo(persona)
                });
            }

            // Mover a otro grupo.
            var mover = Boton("↔", secundario: true, 7, 1);

            mover.Margin = new Thickness(8, 0, 0, 0);
            mover.FontSize = 12;

            System.Windows.Automation.AutomationProperties.SetName(
                mover,
                "Mover a " + persona.Nombre);

            var personaMover = persona;

            mover.Click += (s, e) => AbrirMover(personaMover);

            contenido.Children.Add(mover);

            // Quitar del grupo (solo cuando está en uno).
            if (enGrupo)
            {
                var quitar = Boton("✕", secundario: true, 7, 1);

                quitar.Margin = new Thickness(4, 0, 0, 0);
                quitar.FontSize = 12;

                System.Windows.Automation.AutomationProperties.SetName(
                    quitar,
                    "Sacar del grupo a " + persona.Nombre);

                var personaQuitar = persona;

                quitar.Click += (s, e) =>
                {
                    personaQuitar.GrupoNumero = 0;

                    ReconstruirColecciones();
                    GuardarTodo();
                    RefrescarGrupos();

                    DatabaseHelper.AnotarActividad(
                        "RSC",
                        "Sacó a " + personaQuitar.Nombre
                        + " de su grupo en la RSC de " + Contexto());
                };

                contenido.Children.Add(quitar);
            }

            return new Border
            {
                Background = FondoChip,
                BorderBrush = BordeSuave,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 8),
                Child = contenido
            };
        }


        private void QuitarGrupo(RscGrupo grupo)
        {
            try
            {
                var respuesta = MessageBox.Show(
                    "¿Quitar el Grupo " + grupo.Numero
                    + " de esta RSC?\n\nSus miembros quedarán sin grupo.",
                    "RSC",
                    MessageBoxButton.YesNo);

                if (respuesta != MessageBoxResult.Yes)
                {
                    return;
                }

                _grupos.Remove(grupo);

                RscMotor.Renumerar(_grupos);
                ReconstruirColecciones();

                GuardarTodo();
                RefrescarGrupos();

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Quitó un grupo de la RSC de " + Contexto());
            }
            catch
            {
            }
        }


        private void AbrirMover(RscPersona persona)
        {
            try
            {
                _personaMover = persona;

                TxtMoverPersona.Text = "Mover a " + persona.Nombre;

                PanelMoverRadios.Children.Clear();

                RadioButton primera = null;

                foreach (var grupo in _grupos)
                {
                    var radio = new RadioButton
                    {
                        Content = "Grupo " + grupo.Numero
                            + " · " + grupo.Miembros.Count
                            + (grupo.Miembros.Count == 1
                                ? " persona"
                                : " personas"),
                        Foreground = Brushes.White,
                        GroupName = "MoverGrupo",
                        Tag = grupo.Numero,
                        Margin = new Thickness(0, 0, 0, 6),
                        IsChecked = persona.GrupoNumero == grupo.Numero
                    };

                    if (radio.IsChecked == true)
                    {
                        primera = radio;
                    }

                    PanelMoverRadios.Children.Add(radio);
                }

                var sinGrupo = new RadioButton
                {
                    Content = "Sin grupo",
                    Foreground = Brushes.White,
                    GroupName = "MoverGrupo",
                    Tag = 0,
                    Margin = new Thickness(0, 0, 0, 6),
                    IsChecked = persona.GrupoNumero == 0
                };

                if (sinGrupo.IsChecked == true)
                {
                    primera = sinGrupo;
                }

                PanelMoverRadios.Children.Add(sinGrupo);

                if (primera == null && PanelMoverRadios.Children.Count > 0)
                {
                    ((RadioButton)PanelMoverRadios.Children[0]).IsChecked = true;
                }

                OverlayMover.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        }


        private void BtnCancelarMover_Click(
            object sender,
            RoutedEventArgs e)
        {
            _personaMover = null;

            OverlayMover.Visibility = Visibility.Collapsed;
        }


        private void BtnConfirmarMover_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var persona = _personaMover;

                if (persona == null)
                {
                    return;
                }

                int destino = persona.GrupoNumero;

                foreach (var hijo in PanelMoverRadios.Children)
                {
                    if (hijo is RadioButton radio
                        && radio.IsChecked == true
                        && radio.Tag is int numero)
                    {
                        destino = numero;
                        break;
                    }
                }

                int anterior = persona.GrupoNumero;

                persona.GrupoNumero = destino;

                ReconstruirColecciones();
                GuardarTodo();
                RefrescarGrupos();

                _personaMover = null;

                string donde = destino == 0
                    ? "a sin grupo"
                    : "al Grupo " + destino;

                DatabaseHelper.AnotarActividad(
                    "RSC",
                    "Movió a " + persona.Nombre + " "
                    + (anterior == 0 ? "(estaba sin grupo) " : "")
                    + donde + " en la RSC de " + Contexto());

                OverlayMover.Visibility = Visibility.Collapsed;
            }
            catch
            {
            }
        }


        // =====================================================
        // AYUDAS DE UI
        // =====================================================

        private static Wpf.Ui.Controls.Button Boton(
            string texto,
            bool secundario,
            double rellenoHorizontal = 12,
            double rellenoVertical = 6)
        {
            var boton = new Wpf.Ui.Controls.Button
            {
                Content = texto,
                Padding = new Thickness(
                    rellenoHorizontal,
                    rellenoVertical,
                    rellenoHorizontal,
                    rellenoVertical),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (secundario)
            {
                boton.Appearance =
                    Wpf.Ui.Controls.ControlAppearance.Secondary;
            }

            return boton;
        }
    }
}
