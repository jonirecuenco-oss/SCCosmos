using Microsoft.Maui.Layouts;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Editor de una RSC (móvil): asistentes, arreglos y grupos de
/// predicación. Cada cambio se guarda al momento; al volver de las
/// páginas auxiliares (añadir, arreglo, mover) se recarga sola.
/// </summary>
public partial class RscEditorPage : ContentPage, IActualizableMobile
{
    private readonly string _reunionId;

    private RscReunionMobile? _reunion;

    private List<RscPersonaMobile> _asistentes =
        new List<RscPersonaMobile>();

    private List<RscGrupoMobile> _grupos =
        new List<RscGrupoMobile>();

    private List<RscArregloDetectadoMobile> _arreglos =
        new List<RscArregloDetectadoMobile>();

    public RscEditorPage(
        string reunionId)
    {
        InitializeComponent();

        _reunionId =
            reunionId ?? "";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CargarAsync();
    }

    public void DatosActualizados()
    {
        try
        {
            _ = CargarAsync();
        }
        catch
        {
        }
    }


    private async Task CargarAsync()
    {
        try
        {
            string reunionId =
                _reunionId;

            var resultado =
                await Task.Run(
                    () =>
                    {
                        RscReunionMobile? reunion =
                            DatabaseHelper
                                .ObtenerReunionRscPorIdMobile(
                                    reunionId);

                        if (reunion == null)
                        {
                            return (
                                (RscReunionMobile?)null,
                                new List<RscPersonaMobile>(),
                                new List<RscGrupoMobile>(),
                                new List<RscArregloDetectadoMobile>());
                        }

                        var territorios =
                            DatabaseHelper
                                .ObtenerTerritoriosPorPersonaMobile(
                                    reunion
                                        .CongregacionCodigo);

                        var asistentes =
                            DatabaseHelper
                                .ObtenerAsistentesRscMobile(
                                    reunionId);

                        foreach (var asistente in asistentes)
                        {
                            if (asistente.PersonaId > 0
                                && territorios.TryGetValue(
                                    asistente.PersonaId,
                                    out string? territorio))
                            {
                                asistente.Territorio =
                                    territorio ?? "";
                            }
                        }

                        var grupos =
                            DatabaseHelper
                                .ObtenerGruposRscMobile(
                                    reunionId);

                        var numeroPorPersona =
                            new Dictionary<int, int>();

                        foreach (var grupo in grupos)
                        {
                            foreach (var miembro in grupo.Miembros)
                            {
                                numeroPorPersona
                                    .TryAdd(
                                        miembro.PersonaId,
                                        grupo.Numero);
                            }
                        }

                        foreach (var asistente in asistentes)
                        {
                            asistente.GrupoNumero =
                                numeroPorPersona
                                    .GetValueOrDefault(
                                        asistente.PersonaId);
                        }

                        var arreglos =
                            DatabaseHelper
                                .ObtenerArreglosDelDiaMobile(
                                    reunion
                                        .CongregacionCodigo,
                                    DateTime.TryParse(
                                        reunion.Fecha,
                                        out DateTime fecha)
                                            ? fecha
                                            : DateTime.Today,
                                    reunion.Turno);

                        return (
                            (RscReunionMobile?)reunion,
                            asistentes,
                            grupos,
                            arreglos);
                    });

            if (resultado.Item1 == null)
            {
                await Navigation.PopAsync();

                return;
            }

            _reunion =
                resultado.Item1;

            _asistentes =
                resultado.Item2;

            _grupos =
                resultado.Item3;

            _arreglos =
                resultado.Item4;

            LblTitulo.Text =
                "RSC · "
                + _reunion.TituloCompleto;

            RefrescarAsistentes();
            RefrescarGrupos();
            RefrescarArreglos();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo cargar la RSC:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private void RefrescarAsistentes()
    {
        LblSinAsistentes.IsVisible =
            _asistentes.Count == 0;

        int predican =
            _asistentes.Count(
                a => a.SaleEnGrupos);

        LblContadorAsistentes.Text =
            _asistentes.Count == 0
                ? ""
                : "· "
                  + _asistentes.Count
                  + " marcados · "
                  + predican
                  + " salen a predicar";

        ListaAsistentes.Clear();

        foreach (var persona in _asistentes)
        {
            ListaAsistentes.Add(
                ConstruirFilaAsistente(
                    persona));
        }
    }


    private Border ConstruirFilaAsistente(
        RscPersonaMobile persona)
    {
        var fila =
            new Border
            {
                Style = Estilo("TarjetaPlana"),
                Margin = new Thickness(0, 0, 0, 8)
            };

        var rejilla =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Star),
                    new ColumnDefinition(
                        GridLength.Auto),
                    new ColumnDefinition(
                        GridLength.Auto),
                    new ColumnDefinition(
                        GridLength.Auto)
                },
                ColumnSpacing = 8
            };

        // Nombre + detalles.
        var textos =
            new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions =
                    LayoutOptions.Center
            };

        string etiqueta =
            persona.PersonaId
                == RscVirtualesMobile.Superintendente
                ? "SC"
                : persona.PersonaId
                  == RscVirtualesMobile.Esposa
                    ? "esposa del SC"
                    : "";

        textos.Add(
            new Label
            {
                Text = persona.Nombre,
                FontSize = 13.5,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor = Colors.White,
                LineBreakMode =
                    LineBreakMode.TailTruncation,
                MaxLines = 1
            });

        var detalles =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                etiqueta))
        {
            detalles.Add(
                etiqueta);
        }

        if (persona.TieneTerritorio)
        {
            detalles.Add(
                "territorio "
                + persona.Territorio);
        }

        if (persona.GrupoPredicacion > 0)
        {
            detalles.Add(
                "grupo de servicio "
                + persona.GrupoPredicacion);
        }

        if (detalles.Count > 0)
        {
            textos.Add(
                new Label
                {
                    Text = string.Join(
                        " · ",
                        detalles),
                    Style = Estilo("TextoNota")
                });
        }

        rejilla.Add(
            textos,
            0,
            0);

        // Predica (sí/no).
        var predica =
            new Switch
            {
                IsToggled =
                    persona.SaleAPredicar,
                OnColor = Color.FromArgb(
                    "#2478B5"),
                ThumbColor = Colors.White,
                VerticalOptions =
                    LayoutOptions.Center
            };

        var personaPredica =
            persona;

        predica.Toggled +=
            async (s, e) =>
            {
                bool valor =
                    e.Value;

                await Task.Run(
                    () => DatabaseHelper
                        .EstablecerPredicaRscMobile(
                            _reunionId,
                            personaPredica.PersonaId,
                            valor));

                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    (valor
                        ? "Marcó que predica "
                        : "Marcó que no predica ")
                    + personaPredica.Nombre
                    + " en la RSC de "
                    + Contexto());

                await CargarAsync();
            };

        rejilla.Add(
            predica,
            1,
            0);

        // Arreglo.
        var arreglo =
            new Button
            {
                Text = TextoArreglo(
                    persona),
                Style = Estilo("BotonSecundario"),
                HeightRequest = 38,
                FontSize = 11.5,
                Padding = new Thickness(8, 0, 8, 0),
                MaximumWidthRequest = 150,
                LineBreakMode =
                    LineBreakMode.TailTruncation,
                IsEnabled =
                    persona.SaleAPredicar,
                VerticalOptions =
                    LayoutOptions.Center
            };

        var personaArreglo =
            persona;

        arreglo.Clicked +=
            async (s, e) =>
            {
                await Navigation.PushAsync(
                    new RscArregloPage(
                        _reunionId,
                        personaArreglo
                            .PersonaId));
            };

        rejilla.Add(
            arreglo,
            2,
            0);

        // Quitar de la RSC.
        var quitar =
            new Button
            {
                Text = "✕",
                Style = Estilo("BotonSecundario"),
                HeightRequest = 38,
                WidthRequest = 40,
                Padding = 0,
                FontSize = 12,
                VerticalOptions =
                    LayoutOptions.Center
            };

        var personaQuitar =
            persona;

        quitar.Clicked +=
            async (s, e) =>
            {
                bool confirmar =
                    await DisplayAlert(
                        "Quitar de la RSC",
                        "¿Quitar a "
                        + personaQuitar.Nombre
                        + " de esta RSC?",
                        "Sí",
                        "No");

                if (!confirmar)
                {
                    return;
                }

                await Task.Run(
                    () => DatabaseHelper
                        .QuitarAsistenteRscMobile(
                            _reunionId,
                            personaQuitar
                                .PersonaId));

                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "Quitó a "
                    + personaQuitar.Nombre
                    + " de la RSC de "
                    + Contexto());

                await CargarAsync();
            };

        rejilla.Add(
            quitar,
            3,
            0);

        fila.Content =
            rejilla;

        return fila;
    }


    private string TextoArreglo(
        RscPersonaMobile persona)
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
                var pareja =
                    _asistentes.FirstOrDefault(
                        p => p.PersonaId == id);

                return pareja != null
                    ? pareja.Nombre
                    : "otro hermano";
            })
            .ToList();

        string con = nombres.Count == 1
            ? nombres[0]
            : nombres[0] + " +" + (nombres.Count - 1);

        return "Con "
               + con
               + (persona.AceptaGrupo
                   ? ""
                   : " · sin grupo mayor");
    }


    private void RefrescarGrupos()
    {
        int predicadores =
            _asistentes.Count(
                a => a.SaleEnGrupos);

        int sinGrupo =
            _asistentes.Count(
                a => a.SaleEnGrupos
                     && a.GrupoNumero == 0);

        int enGrupos =
            predicadores - sinGrupo;

        LblResumenGrupos.Text =
            _grupos.Count == 0
                ? ""
                : (_grupos.Count == 1
                    ? "1 grupo"
                    : _grupos.Count + " grupos")
                  + " · "
                  + enGrupos
                  + " en grupos"
                  + (sinGrupo > 0
                      ? " · "
                        + sinGrupo
                        + " sin grupo"
                      : "");

        LblSinGrupos.IsVisible =
            _grupos.Count == 0
            && sinGrupo == 0;

        ListaGrupos.Clear();

        foreach (var grupo in _grupos)
        {
            ListaGrupos.Add(
                ConstruirTarjetaGrupo(
                    grupo));
        }

        if (sinGrupo > 0)
        {
            ListaGrupos.Add(
                ConstruirTarjetaSinGrupo());
        }
    }


    private Border ConstruirTarjetaGrupo(
        RscGrupoMobile grupo)
    {
        var tarjeta =
            new Border
            {
                Style = Estilo("Tarjeta")
            };

        var contenido =
            new VerticalStackLayout
            {
                Spacing = 8
            };

        // Cabecera.
        var cabecera =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Star),
                    new ColumnDefinition(
                        GridLength.Auto)
                },
                ColumnSpacing = 8
            };

        var titulo =
            new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions =
                    LayoutOptions.Center
            };

        titulo.Add(
            new Label
            {
                Text = "Grupo " + grupo.Numero,
                FontSize = 15,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalTextAlignment =
                    TextAlignment.Center
            });

        titulo.Add(
            new Label
            {
                Text = "· "
                       + grupo.Miembros.Count
                       + (grupo.Miembros.Count == 1
                           ? " persona"
                           : " personas"),
                FontSize = 12,
                TextColor = Color.FromArgb(
                    "#B5BCC4"),
                VerticalTextAlignment =
                    TextAlignment.Center
            });

        if (grupo.BajoMinimo)
        {
            titulo.Add(
                new Label
                {
                    Text = "⚠",
                    FontSize = 14,
                    TextColor = Color.FromArgb(
                        "#D9A441"),
                    VerticalTextAlignment =
                        TextAlignment.Center
                });
        }

        cabecera.Add(
            titulo,
            0,
            0);

        var quitar =
            new Button
            {
                Text = "Quitar",
                Style = Estilo("BotonPeligro"),
                HeightRequest = 36,
                FontSize = 11.5,
                Padding =
                    new Thickness(10, 0, 10, 0),
                VerticalOptions =
                    LayoutOptions.Center
            };

        var grupoQuitar =
            grupo;

        quitar.Clicked +=
            async (s, e) =>
            {
                bool confirmar =
                    await DisplayAlert(
                        "Quitar grupo",
                        "¿Quitar el Grupo "
                        + grupoQuitar.Numero
                        + "?\n\nSus miembros quedarán sin grupo.",
                        "Sí",
                        "No");

                if (!confirmar)
                {
                    return;
                }

                await Task.Run(
                    () => DatabaseHelper
                        .EliminarGrupoRscMobile(
                            _reunionId,
                            grupoQuitar.Id));

                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "Quitó un grupo de la RSC de "
                    + Contexto());

                await CargarAsync();
            };

        cabecera.Add(
            quitar,
            1,
            0);

        contenido.Add(
            cabecera);

        // Territorio + territorios de los miembros.
        var lineaTerritorio =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Auto),
                    new ColumnDefinition(
                        new GridLength(160)),
                    new ColumnDefinition(
                        GridLength.Star)
                },
                ColumnSpacing = 8
            };

        lineaTerritorio.Add(
            new Label
            {
                Text = "Territorio:",
                FontSize = 12.5,
                TextColor = Color.FromArgb(
                    "#A7AEB8"),
                VerticalTextAlignment =
                    TextAlignment.Center
            },
            0,
            0);

        var entradaTerritorio =
            new Entry
            {
                Text = grupo.Territorio,
                Style = Estilo("Campo"),
                FontSize = 13
            };

        var grupoTerritorio =
            grupo;

        entradaTerritorio.Unfocused +=
            (s, e) =>
            {
                grupoTerritorio.Territorio =
                    (entradaTerritorio.Text ?? "")
                        .Trim();

                _ = Task.Run(
                    () => DatabaseHelper
                        .GuardarTerritorioGrupoRscMobile(
                            grupoTerritorio.Id,
                            grupoTerritorio.Territorio));
            };

        lineaTerritorio.Add(
            entradaTerritorio,
            1,
            0);

        if (!string.IsNullOrWhiteSpace(
                grupo.TerritoriosMiembros))
        {
            lineaTerritorio.Add(
                new Label
                {
                    Text = grupo.TerritoriosMiembros,
                    FontSize = 10.5,
                    TextColor = Color.FromArgb(
                        "#808893"),
                    VerticalTextAlignment =
                        TextAlignment.Center,
                    LineBreakMode =
                        LineBreakMode.TailTruncation
                },
                2,
                0);
        }

        contenido.Add(
            lineaTerritorio);

        // Miembros.
        var chips =
            new FlexLayout
            {
                Wrap = FlexWrap.Wrap,
                Direction = FlexDirection.Row
            };

        foreach (var miembro in grupo.Miembros)
        {
            chips.Add(
                ConstruirChipMiembro(
                    miembro,
                    enGrupo: true));
        }

        contenido.Add(
            chips);

        if (!string.IsNullOrWhiteSpace(
                grupo.Notas))
        {
            contenido.Add(
                new Label
                {
                    Text = grupo.Notas,
                    FontSize = 11.5,
                    TextColor = Color.FromArgb(
                        "#D9A441")
                });
        }

        tarjeta.Content =
            contenido;

        return tarjeta;
    }


    private Border ConstruirTarjetaSinGrupo()
    {
        var tarjeta =
            new Border
            {
                Style = Estilo("TarjetaPlana")
            };

        var contenido =
            new VerticalStackLayout
            {
                Spacing = 6
            };

        contenido.Add(
            new Label
            {
                Text = "Sin grupo",
                FontSize = 14,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor = Colors.White
            });

        contenido.Add(
            new Label
            {
                Text = "Predican, pero de momento no están en ningún grupo. Usa «↔» para meterlos en uno.",
                Style = Estilo("TextoNota")
            });

        var chips =
            new FlexLayout
            {
                Wrap = FlexWrap.Wrap,
                Direction = FlexDirection.Row
            };

        foreach (var persona in _asistentes.Where(
                     a => a.SaleEnGrupos
                          && a.GrupoNumero == 0))
        {
            chips.Add(
                ConstruirChipMiembro(
                    persona,
                    enGrupo: false));
        }

        contenido.Add(
            chips);

        tarjeta.Content =
            contenido;

        return tarjeta;
    }


    private Border ConstruirChipMiembro(
        RscPersonaMobile persona,
        bool enGrupo)
    {
        var chip =
            new Border
            {
                Style = Estilo("Chip"),
                Margin = new Thickness(0, 0, 6, 6)
            };

        var contenido =
            new HorizontalStackLayout
            {
                Spacing = 4
            };

        contenido.Add(
            new Label
            {
                Text = persona.Nombre,
                FontSize = 12.5,
                TextColor = Color.FromArgb(
                    "#E6EAF0"),
                LineBreakMode =
                    LineBreakMode.TailTruncation,
                MaxLines = 1,
                MaximumWidthRequest = 165,
                VerticalTextAlignment =
                    TextAlignment.Center
            });

        if (persona.TieneTerritorio)
        {
            contenido.Add(
                new Label
                {
                    Text = "🌍",
                    FontSize = 11,
                    VerticalTextAlignment =
                        TextAlignment.Center
                });
        }

        if (persona.TieneArreglo
            || !persona.AceptaGrupo)
        {
            contenido.Add(
                new Label
                {
                    Text = "👥",
                    FontSize = 11,
                    VerticalTextAlignment =
                        TextAlignment.Center
                });
        }

        var mover =
            new Button
            {
                Text = "↔",
                Style = Estilo("BotonSecundario"),
                HeightRequest = 28,
                WidthRequest = 32,
                Padding = 0,
                FontSize = 11,
                VerticalOptions =
                    LayoutOptions.Center
            };

        var personaMover =
            persona;

        mover.Clicked +=
            async (s, e) =>
            {
                await Navigation.PushAsync(
                    new RscMoverPage(
                        _reunionId,
                        personaMover.PersonaId));
            };

        contenido.Add(
            mover);

        if (enGrupo)
        {
            var sacar =
                new Button
                {
                    Text = "✕",
                    Style = Estilo("BotonSecundario"),
                    HeightRequest = 28,
                    WidthRequest = 32,
                    Padding = 0,
                    FontSize = 11,
                    VerticalOptions =
                        LayoutOptions.Center
                };

            var personaSacar =
                persona;

            sacar.Clicked +=
                async (s, e) =>
                {
                    await Task.Run(
                        () => DatabaseHelper
                            .MoverPersonaRscMobile(
                                _reunionId,
                                personaSacar.PersonaId,
                                0));

                    DatabaseHelper.AnotarActividadMobile(
                        "RSC",
                        "Sacó a "
                        + personaSacar.Nombre
                        + " de su grupo en la RSC de "
                        + Contexto());

                    await CargarAsync();
                };

            contenido.Add(
                sacar);
        }

        chip.Content =
            contenido;

        return chip;
    }


    private void RefrescarArreglos()
    {
        TarjetaArreglos.IsVisible =
            _arreglos.Count > 0;

        PanelArreglos.Clear();

        foreach (var detectado in _arreglos)
        {
            PanelArreglos.Add(
                ConstruirFilaArreglo(
                    detectado));
        }
    }


    private Border ConstruirFilaArreglo(
        RscArregloDetectadoMobile detectado)
    {
        int personaId =
            detectado.ParaSuperintendente
                ? RscVirtualesMobile.Superintendente
                : RscVirtualesMobile.Esposa;

        bool yaAplicado =
            _asistentes.Any(
                a => a.PersonaId == personaId
                     && a.TieneArreglo
                     && a.ArregloConPersonaIds.Contains(
                         detectado.CompaneroId));

        var fila =
            new Border
            {
                Style = Estilo("TarjetaPlana")
            };

        var contenido =
            new VerticalStackLayout
            {
                Spacing = 6
            };

        string companero =
            string.IsNullOrWhiteSpace(
                detectado.CompaneroNombre)
                ? "un hermano (nº "
                  + detectado.CompaneroId
                  + ")"
                : detectado.CompaneroNombre;

        string texto =
            "El programa apunta: "
            + detectado.Responsable
            + " predicará con "
            + companero;

        var detalle =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                detectado.Turno))
        {
            detalle.Add(
                detectado.Turno.Trim());
        }

        if (!string.IsNullOrWhiteSpace(
                detectado.Hora))
        {
            detalle.Add(
                detectado.Hora.Trim());
        }

        if (detalle.Count > 0)
        {
            texto +=
                " ("
                + string.Join(
                    " · ",
                    detalle)
                + ")";
        }

        contenido.Add(
            new Label
            {
                Text = texto,
                FontSize = 12.5,
                TextColor = Color.FromArgb(
                    "#C9D8E6"),
                LineBreakMode =
                    LineBreakMode.WordWrap
            });

        var boton =
            new Button
            {
                Text = yaAplicado
                    ? "Añadido ✔"
                    : "Añadir a la RSC",
                Style = Estilo("BotonSecundario"),
                HeightRequest = 38,
                FontSize = 12,
                IsEnabled = !yaAplicado,
                HorizontalOptions =
                    LayoutOptions.Start
            };

        var arregloDetectado =
            detectado;

        boton.Clicked +=
            async (s, e) =>
            {
                await AplicarArregloAsync(
                    arregloDetectado);
            };

        contenido.Add(
            boton);

        fila.Content =
            contenido;

        return fila;
    }


    private async Task AplicarArregloAsync(
        RscArregloDetectadoMobile detectado)
    {
        try
        {
            bool esSc =
                detectado.ParaSuperintendente;

            int personaId =
                esSc
                    ? RscVirtualesMobile.Superintendente
                    : RscVirtualesMobile.Esposa;

            string nombrePersona =
                esSc
                    ? (DatabaseHelper
                           .ObtenerConfiguracion(
                               "NombreSC",
                               "")
                       + " "
                       + DatabaseHelper
                           .ObtenerConfiguracion(
                               "ApellidoSC",
                               ""))
                        .Trim()
                    : (DatabaseHelper
                           .ObtenerConfiguracion(
                               "NombreEsposaSC",
                               "")
                       + " "
                       + DatabaseHelper
                           .ObtenerConfiguracion(
                               "ApellidoEsposaSC",
                               ""))
                        .Trim();

            string nombreCompanero =
                string.IsNullOrWhiteSpace(
                    detectado.CompaneroNombre)
                    ? "Hermano/a "
                      + detectado.CompaneroId
                    : detectado.CompaneroNombre;

            var nuevas =
                new List<RscPersonaMobile>
                {
                    new RscPersonaMobile
                    {
                        PersonaId = personaId,
                        Nombre = nombrePersona,
                        Sexo = esSc
                            ? "Hombre"
                            : "Mujer"
                    },
                    new RscPersonaMobile
                    {
                        PersonaId =
                            detectado.CompaneroId,
                        Nombre = nombreCompanero
                    }
                };

            await Task.Run(
                () =>
                {
                    DatabaseHelper
                        .AnadirAsistentesRscMobile(
                            _reunionId,
                            nuevas);

                    DatabaseHelper
                        .EstablecerArregloRscMobile(
                            _reunionId,
                            personaId,
                            new List<int>
                            {
                                detectado.CompaneroId
                            },
                            true);
                });

            DatabaseHelper.AnotarActividadMobile(
                "RSC",
                "Añadió el arreglo del programa a la RSC de "
                + Contexto()
                + ": "
                + nombrePersona
                + " con "
                + nombreCompanero);

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo añadir el arreglo:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private string Contexto()
    {
        if (_reunion == null)
        {
            return "";
        }

        return _reunion.CongregacionNombre
               + " ("
               + _reunion.FechaVisual
               + " · "
               + _reunion.Turno
               + ")";
    }


    private static Style Estilo(
        string clave)
    {
        return (Style)Application
            .Current!
            .Resources[clave];
    }


    private async void BtnAnadir_Clicked(
        object? sender,
        EventArgs e)
    {
        await Navigation.PushAsync(
            new RscPersonasPickerPage(
                _reunionId));
    }


    private async void BtnFormar_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (_asistentes.Count(
                    a => a.SaleEnGrupos) == 0)
            {
                await DisplayAlert(
                    "RSC",
                    "No hay nadie marcado que salga a predicar.\n\n"
                    + "Marca a los asistentes y deja activado «Predica».",
                    "Aceptar");

                return;
            }

            if (_grupos.Count > 0)
            {
                bool rehacer =
                    await DisplayAlert(
                        "Rehacer grupos",
                        "Ya hay grupos formados. ¿Quieres rehacerlos?\n\n"
                        + "Se sustituirán por los nuevos grupos.",
                        "Sí",
                        "No");

                if (!rehacer)
                {
                    return;
                }
            }

            var avisos =
                await Task.Run(
                    () => DatabaseHelper
                        .FormarGruposRscMobile(
                            _reunionId));

            DatabaseHelper.AnotarActividadMobile(
                "RSC",
                "Formó grupos para la RSC de "
                + Contexto());

            await CargarAsync();

            if (avisos.Count > 0)
            {
                LblAvisos.Text =
                    string.Join(
                        "   ",
                        avisos.Select(
                            a => "• " + a));

                LblAvisos.IsVisible =
                    true;
            }
            else
            {
                LblAvisos.IsVisible =
                    false;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudieron formar los grupos:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private async void BtnEliminar_Clicked(
        object? sender,
        EventArgs e)
    {
        if (_reunion == null)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                "Eliminar RSC",
                "¿Eliminar la RSC de "
                + _reunion.CongregacionNombre
                + " ("
                + _reunion.FechaVisual
                + " · "
                + _reunion.Turno
                + ")?\n\nSe borrarán también sus asistentes y grupos.",
                "Sí",
                "No");

        if (!confirmar)
        {
            return;
        }

        var reunion =
            _reunion;

        await Task.Run(
            () => DatabaseHelper
                .EliminarReunionRscMobile(
                    reunion.Id));

        DatabaseHelper.AnotarActividadMobile(
            "RSC",
            "Eliminó la RSC de "
            + reunion.CongregacionNombre
            + " ("
            + reunion.FechaVisual
            + " · "
            + reunion.Turno
            + ")");

        await Navigation.PopAsync();
    }
}
