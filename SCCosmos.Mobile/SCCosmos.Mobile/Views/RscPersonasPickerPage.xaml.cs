using System.Globalization;
using System.Text;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Añadir asistentes a una RSC (móvil): busca entre los publicadores
/// de la congregación y arriba ofrece al SC y a su esposa.
/// </summary>
public partial class RscPersonasPickerPage : ContentPage
{
    private readonly string _reunionId;

    private bool _cargado;

    private string _congregacion = "";

    private List<Persona> _personas =
        new List<Persona>();

    private readonly HashSet<int> _yaEstan =
        new HashSet<int>();

    private readonly Dictionary<CheckBox, int> _checkVirtuales =
        new Dictionary<CheckBox, int>();

    private readonly Dictionary<CheckBox, Persona> _checkPersonas =
        new Dictionary<CheckBox, Persona>();

    public RscPersonasPickerPage(
        string reunionId)
    {
        InitializeComponent();

        _reunionId =
            reunionId ?? "";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_cargado)
        {
            return;
        }

        _cargado =
            true;

        try
        {
            var resultado =
                await Task.Run(
                    () =>
                    {
                        var reunion =
                            DatabaseHelper
                                .ObtenerReunionRscPorIdMobile(
                                    _reunionId);

                        if (reunion == null)
                        {
                            return (
                                "",
                                new List<Persona>(),
                                new List<int>());
                        }

                        var personas =
                            DatabaseHelper
                                .ObtenerPersonasPorCongregacion(
                                    reunion
                                        .CongregacionCodigo);

                        var ids =
                            DatabaseHelper
                                .ObtenerAsistentesRscMobile(
                                    _reunionId)
                                .Select(a => a.PersonaId)
                                .ToList();

                        return (
                            reunion.CongregacionCodigo,
                            personas,
                            ids);
                    });

            _congregacion =
                resultado.Item1;

            _personas =
                resultado.Item2;

            _yaEstan.Clear();

            foreach (int id in resultado.Item3)
            {
                _yaEstan.Add(
                    id);
            }

            ConstruirPinned();
            RefrescarLista(
                "");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudo cargar la lista:\n"
                + ex.Message,
                "Aceptar");
        }
    }


    private void ConstruirPinned()
    {
        PanelPinned.Clear();
        _checkVirtuales.Clear();

        string nombreSc =
            (DatabaseHelper.ObtenerConfiguracion(
                 "NombreSC",
                 "")
             + " "
             + DatabaseHelper.ObtenerConfiguracion(
                 "ApellidoSC",
                 ""))
            .Trim();

        string nombreEsposa =
            (DatabaseHelper.ObtenerConfiguracion(
                 "NombreEsposaSC",
                 "")
             + " "
             + DatabaseHelper.ObtenerConfiguracion(
                 "ApellidoEsposaSC",
                 ""))
            .Trim();

        PanelPinned.Add(
            new Label
            {
                Text = "DEL CIRCUITO",
                Style = Estilo("Antetitulo")
            });

        if (!string.IsNullOrWhiteSpace(
                nombreSc))
        {
            PanelPinned.Add(
                FilaVirtual(
                    RscVirtualesMobile.Superintendente,
                    nombreSc
                    + " (superintendente de circuito)"));
        }

        if (!string.IsNullOrWhiteSpace(
                nombreEsposa))
        {
            PanelPinned.Add(
                FilaVirtual(
                    RscVirtualesMobile.Esposa,
                    nombreEsposa
                    + " (su esposa)"));
        }
    }


    private Border FilaVirtual(
        int personaId,
        string texto)
    {
        bool yaEsta =
            _yaEstan.Contains(
                personaId);

        var check =
            new CheckBox
            {
                Color = Color.FromArgb("#4BA3E3"),
                IsEnabled = !yaEsta,
                VerticalOptions =
                    LayoutOptions.Center
            };

        _checkVirtuales[check] =
            personaId;

        var contenido =
            new HorizontalStackLayout
            {
                Spacing = 10
            };

        contenido.Add(
            check);

        contenido.Add(
            new Label
            {
                Text = texto
                       + (yaEsta
                           ? "  —  ya está"
                           : ""),
                FontSize = 13,
                TextColor = Colors.White,
                VerticalTextAlignment =
                    TextAlignment.Center
            });

        var fila =
            new Border
            {
                Style = Estilo("TarjetaPlana")
            };

        fila.Content =
            contenido;

        return fila;
    }


    private void RefrescarLista(
        string filtro)
    {
        ListaPersonas.Clear();
        _checkPersonas.Clear();

        string buscado =
            NormalizarTexto(
                filtro ?? "");

        foreach (var persona in _personas)
        {
            string nombre =
                string.IsNullOrWhiteSpace(
                    persona.NombreComun)
                    ? persona.Nombre
                    : persona.NombreComun;

            if (buscado.Length > 0
                && !NormalizarTexto(nombre)
                    .Contains(buscado))
            {
                continue;
            }

            bool yaEsta =
                _yaEstan.Contains(
                    persona.Id);

            var check =
                new CheckBox
                {
                    Color = Color.FromArgb(
                        "#4BA3E3"),
                    IsEnabled = !yaEsta,
                    VerticalOptions =
                        LayoutOptions.Center
                };

            _checkPersonas[check] =
                persona;

            var contenido =
                new HorizontalStackLayout
                {
                    Spacing = 10
                };

            contenido.Add(
                check);

            contenido.Add(
                new Label
                {
                    Text = nombre
                           + (yaEsta
                               ? "  —  ya está"
                               : ""),
                    FontSize = 13,
                    TextColor = Colors.White,
                    VerticalTextAlignment =
                        TextAlignment.Center
                });

            var fila =
                new Border
                {
                    Style = Estilo("TarjetaPlana")
                };

            fila.Content =
                contenido;

            ListaPersonas.Add(
                fila);
        }
    }


    private static string NormalizarTexto(
        string texto)
    {
        if (string.IsNullOrWhiteSpace(
                texto))
        {
            return "";
        }

        string descompuesto = texto
            .Trim()
            .ToLowerInvariant()
            .Normalize(
                NormalizationForm.FormD);

        var constructor =
            new StringBuilder();

        foreach (char caracter in descompuesto)
        {
            if (CharUnicodeInfo
                    .GetUnicodeCategory(
                        caracter)
                != UnicodeCategory.NonSpacingMark)
            {
                constructor.Append(
                    caracter);
            }
        }

        return constructor
            .ToString()
            .Normalize(
                NormalizationForm.FormC);
    }


    private static Style Estilo(
        string clave)
    {
        return (Style)Application
            .Current!
            .Resources[clave];
    }


    private void TxtBuscar_TextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        RefrescarLista(
            e.NewTextValue ?? "");
    }


    private async void BtnAnadir_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            var nuevas =
                new List<RscPersonaMobile>();

            foreach (var par in _checkVirtuales)
            {
                if (par.Key.IsChecked != true
                    || !par.Key.IsEnabled)
                {
                    continue;
                }

                string nombre =
                    par.Value
                        == RscVirtualesMobile.Superintendente
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

                nuevas.Add(
                    new RscPersonaMobile
                    {
                        PersonaId = par.Value,
                        Nombre = nombre,
                        Sexo = par.Value
                            == RscVirtualesMobile.Superintendente
                            ? "Hombre"
                            : "Mujer"
                    });
            }

            foreach (var par in _checkPersonas)
            {
                if (par.Key.IsChecked != true
                    || !par.Key.IsEnabled)
                {
                    continue;
                }

                nuevas.Add(
                    new RscPersonaMobile
                    {
                        PersonaId = par.Value.Id,
                        Nombre =
                            string.IsNullOrWhiteSpace(
                                par.Value.NombreComun)
                                ? par.Value.Nombre
                                : par.Value.NombreComun,
                        Sexo = par.Value.Sexo
                    });
            }

            if (nuevas.Count == 0)
            {
                await DisplayAlert(
                    "RSC",
                    "Marca al menos una persona.",
                    "Aceptar");

                return;
            }

            int anadidas =
                await Task.Run(
                    () => DatabaseHelper
                        .AnadirAsistentesRscMobile(
                            _reunionId,
                            nuevas));

            if (anadidas > 0)
            {
                DatabaseHelper.AnotarActividadMobile(
                    "RSC",
                    "Añadió "
                    + anadidas
                    + (anadidas == 1
                        ? " persona"
                        : " personas")
                    + " a una RSC");
            }

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "RSC",
                "No se pudieron añadir:\n"
                + ex.Message,
                "Aceptar");
        }
    }
}
