using SCCosmos.Mobile.Models;
using SCCosmos.Mobile.Services;

namespace SCCosmos.Mobile.Views;


public partial class JsonImportConflictPage
    : ContentPage
{
    private readonly ResultadoImportacionJsonMobile
        _resultado;

    private readonly JsonImportItemMobile
        _item;

    private readonly bool
        _modoVinculacion;

    private readonly Persona
        _personaBase;

    private readonly Action?
        _alFinalizarVinculacion;

    private readonly List<CampoJsonResolucionMobile>
        _campos =
            new();


    public JsonImportConflictPage(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item)
        : this(
            resultado,
            item,
            item.PersonaDB,
            false,
            null)
    {
    }


    public JsonImportConflictPage(
        ResultadoImportacionJsonMobile resultado,
        JsonImportItemMobile item,
        Persona? personaVincular,
        bool modoVinculacion,
        Action? alFinalizarVinculacion = null)
    {
        InitializeComponent();

        _resultado =
            resultado;

        _item =
            item;

        _modoVinculacion =
            modoVinculacion;

        _alFinalizarVinculacion =
            alFinalizarVinculacion;

        if (personaVincular is null ||
            item.PersonaJson is null)
        {
            throw new ArgumentException(
                "No están disponibles los datos necesarios para resolver " +
                "esta persona.");
        }

        _personaBase =
            ClonarPersona(
                personaVincular);

        LblTitulo.Text =
            modoVinculacion
                ? "VINCULAR PERSONA"
                : "RESOLVER CONFLICTO";

        LblNombre.Text =
            modoVinculacion
                ? item.PersonaJson.Nombre +
                  "  ↔  " +
                  JsonImportAnalysisService
                      .ObtenerNombreVisual(
                          personaVincular)
                : item.NombreVisual;

        LblExplicacion.Text =
            modoVinculacion
                ? "Vas a indicar que el registro S-21 y la ficha seleccionada " +
                  "son la misma persona. NombreJWHub, nombre común, foto, " +
                  "contacto y demás datos JW se conservarán."
                : "Elige qué valor conservar para cada diferencia. " +
                  "Solo se muestran campos que el S-21 puede actualizar; " +
                  "la identidad JW no se toca.";

        CrearCampos();

        ListaCampos.ItemsSource =
            _campos;
    }


    private void CrearCampos()
    {
        PersonaJsonMobile json =
            _item.PersonaJson!;

        AgregarSiDistinto(
            "Nombre",
            "Nombre completo S-21",
            _personaBase.Nombre,
            json.Nombre);

        if (!string.IsNullOrWhiteSpace(
                json.Sexo))
        {
            AgregarSiDistinto(
                "Sexo",
                "Sexo",
                _personaBase.Sexo,
                json.Sexo);
        }

        if (!string.IsNullOrWhiteSpace(
                json.Esperanza))
        {
            AgregarSiDistinto(
                "Esperanza",
                "Esperanza",
                _personaBase.Esperanza,
                json.Esperanza);
        }

        // En estos dos campos, vacío sí puede representar un cambio real.
        AgregarSiDistinto(
            "Nombramiento",
            "Nombramiento",
            _personaBase.Nombramiento,
            json.Nombramiento);

        AgregarSiDistinto(
            "Servicio",
            "Servicio",
            _personaBase.Servicio,
            json.Servicio);

        if (!string.IsNullOrWhiteSpace(
                json.FechaNacimiento))
        {
            AgregarSiDistinto(
                "FechaNacimiento",
                "Fecha de nacimiento",
                _personaBase.FechaNacimiento,
                json.FechaNacimiento);
        }

        if (!string.IsNullOrWhiteSpace(
                json.FechaBautismo))
        {
            AgregarSiDistinto(
                "FechaBautismo",
                "Fecha de bautismo",
                _personaBase.FechaBautismo,
                json.FechaBautismo);
        }

        if (json.Grupo > 0 &&
            _personaBase.Grupo !=
                json.Grupo)
        {
            _campos.Add(
                new CampoJsonResolucionMobile
                {
                    Clave =
                        "Grupo",

                    Etiqueta =
                        "Grupo",

                    ValorBd =
                        _personaBase.Grupo > 0
                            ? _personaBase.Grupo.ToString()
                            : "Sin grupo",

                    ValorJson =
                        json.Grupo.ToString()
                });
        }
    }


    private void AgregarSiDistinto(
        string clave,
        string etiqueta,
        string? valorBd,
        string? valorJson)
    {
        string bd =
            valorBd?.Trim()
            ?? "";

        string json =
            valorJson?.Trim()
            ?? "";

        if (string.Equals(
                bd,
                json,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _campos.Add(
            new CampoJsonResolucionMobile
            {
                Clave =
                    clave,

                Etiqueta =
                    etiqueta,

                ValorBd =
                    string.IsNullOrWhiteSpace(bd)
                        ? "—"
                        : bd,

                ValorJson =
                    string.IsNullOrWhiteSpace(json)
                        ? "—"
                        : json
            });
    }


    private async void BtnGuardar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_item.PersonaJson is null)
        {
            return;
        }

        bool confirmar =
            await DisplayAlert(
                _modoVinculacion
                    ? "Vincular persona"
                    : "Resolver conflicto",
                _modoVinculacion
                    ? "¿Guardar esta vinculación para aplicarla al " +
                      "finalizar la importación?"
                    : "¿Guardar las opciones seleccionadas para aplicarlas " +
                      "al finalizar la importación?",
                "Guardar",
                "Cancelar");

        if (!confirmar)
        {
            return;
        }

        BtnGuardar.IsEnabled =
            false;

        try
        {
            Persona actualizada =
                ClonarPersona(
                    _personaBase);

            foreach (CampoJsonResolucionMobile campo
                     in _campos)
            {
                if (!string.Equals(
                        campo.Seleccion,
                        "S-21",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                JsonImportAnalysisService
                    .AplicarValorJson(
                        actualizada,
                        _item.PersonaJson,
                        campo.Clave);
            }

            JsonImportAnalysisService
                .AsegurarReglaIdentidad(
                    actualizada);

            if (_modoVinculacion)
            {
                JsonImportAnalysisService
                    .VincularNuevo(
                        _resultado,
                        _item,
                        _personaBase,
                        actualizada);
            }
            else
            {
                JsonImportAnalysisService
                    .MarcarResuelto(
                        _resultado,
                        _item,
                        actualizada,
                        "Conflicto S-21 resuelto. Los valores elegidos se " +
                        "aplicarán al finalizar.");
            }

            if (_modoVinculacion)
            {
                // Indicamos al revisor que, tras la vinculación,
                // debe abrir la bandeja Conflictos.
                _alFinalizarVinculacion
                    ?.Invoke();

                // Pila habitual:
                // Review -> Nuevo -> Vinculación
                //
                // Quitamos "Nuevo" antes de cerrar esta pantalla.
                // Así volvemos directamente al revisor, sin pasar
                // visualmente otra vez por la lista de candidatos.
                Page? paginaNuevo =
                    Navigation
                        .NavigationStack
                        .Reverse()
                        .Skip(1)
                        .FirstOrDefault(
                            p =>
                                p is
                                    JsonImportNewPersonPage);

                if (paginaNuevo is not null)
                {
                    Navigation.RemovePage(
                        paginaNuevo);
                }

                await Navigation.PopAsync();
            }
            else
            {
                await Navigation.PopAsync();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Importar actividad",
                "No se pudo guardar la resolución:\n\n" +
                ex.Message,
                "Aceptar");
        }
        finally
        {
            BtnGuardar.IsEnabled =
                true;
        }
    }


    private async void BtnCancelar_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PopAsync();
    }


    private static Persona ClonarPersona(
        Persona p)
    {
        return new Persona
        {
            Id = p.Id,
            Nombre = p.Nombre ?? "",
            NombreJWHub = p.NombreJWHub ?? "",
            NombreComun = p.NombreComun ?? "",
            CongregacionCodigo = p.CongregacionCodigo ?? "",
            CongregacionNombre = p.CongregacionNombre ?? "",
            Grupo = p.Grupo,
            FechaNacimiento = p.FechaNacimiento ?? "",
            Sexo = p.Sexo ?? "",
            FechaBautismo = p.FechaBautismo ?? "",
            Esperanza = p.Esperanza ?? "",
            Nombramiento = p.Nombramiento ?? "",
            Servicio = p.Servicio ?? "",
            FechaEliminacion = p.FechaEliminacion ?? ""
        };
    }
}
