using System.Text.RegularExpressions;

using SCCosmos.Mobile.Data;
using SCCosmos.Mobile.Models;

namespace SCCosmos.Mobile.Views;

/// <summary>
/// Matriz de oradores (Android). Replica la lógica de la pestaña "Oradores"
/// de AsambleasPage de WPF:
///  - La base son los ancianos (más el SC y el representante de Betel).
///  - Solo se muestran los que tienen participación real en las asambleas
///    (AC-BR / AC-CO) o en la reunión de precursores (R. Prec) en algún año
///    con datos, o los añadidos manualmente.
///  - Los valores manuales (MANUAL_*), la evaluación (EV_) y el BOS se
///    guardan en las mismas claves de Configuracion que usa Windows.
/// </summary>
public partial class MatrizOradoresView : ContentView
{
    private const string ClaveManuales =
        "OradoresMatrizManuales";

    private const string ClaveOcultos =
        "OradoresMatrizOcultos";

    private Dictionary<int, OradorMatrizMobile> _base =
        new();

    private List<string> _secciones =
        new();

    private List<string> _anios =
        new();

    private bool _cargando;

    public MatrizOradoresView()
    {
        InitializeComponent();
    }

    public async Task CargarAsync()
    {
        if (_cargando)
        {
            return;
        }

        _cargando = true;

        try
        {
            string seccionAnterior =
                PickerSeccion.SelectedItem as string
                ?? "Todas";

            DatosMatriz datos =
                await Task.Run(
                    () =>
                        Preparar());

            _base =
                datos.Base;

            _secciones =
                datos.Secciones;

            _anios =
                datos.Anios;

            PickerSeccion.ItemsSource =
                new List<string> { "Todas" }
                    .Concat(datos.Secciones)
                    .ToList();

            int indice =
                ((List<string>)PickerSeccion.ItemsSource)
                    .IndexOf(seccionAnterior);

            PickerSeccion.SelectedIndex =
                indice < 0 ? 0 : indice;

            Pintar();
        }
        catch (Exception ex)
        {
            await MostrarAlertaAsync(
                "Matriz de oradores",
                "No se pudo cargar: " + ex.Message);
        }
        finally
        {
            _cargando = false;
        }
    }

    private sealed class DatosMatriz
    {
        public Dictionary<int, OradorMatrizMobile> Base
        { get; set; } = new();

        public List<string> Secciones { get; set; } = new();

        public List<string> Anios { get; set; } = new();
    }

    private DatosMatriz Preparar()
    {
        var datos =
            new DatosMatriz();

        // ---- Secciones del circuito ----
        datos.Secciones =
            DatabaseHelper
                .ObtenerSeccionesCircuitoMobile()
                .Select(s => s.Nombre)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

        // ---- Sección a la que pertenece cada congregación ----
        var seccionPorCongregacion =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (
            Congregacion cong
            in DatabaseHelper.ObtenerCongregacionesCompletasMobile())
        {
            if (!string.IsNullOrWhiteSpace(cong.Nombre))
            {
                seccionPorCongregacion[cong.Nombre] =
                    cong.NombreSeccion ?? "";
            }
        }

        // ---- Base de oradores: ancianos (+ SC y Betel) ----
        foreach (
            Persona p in DatabaseHelper.ObtenerPersonas())
        {
            string nombreJW =
                p.NombreJWHub ?? "";

            string nombre =
                p.Nombre ?? "";

            if (nombreJW.Contains(
                    "Representante de Betel",
                    StringComparison.OrdinalIgnoreCase)
                ||
                nombre.Contains(
                    "Representante de Betel",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string nombramiento =
                (p.Nombramiento ?? "")
                    .ToLowerInvariant();

            if (!nombramiento.Contains("anciano"))
            {
                continue;
            }

            string congre =
                p.CongregacionNombre ?? "";

            if (congre == "Sin asignar")
            {
                congre = "";
            }

            string seccion =
                seccionPorCongregacion.TryGetValue(
                    congre,
                    out string? s)
                    ? s
                    : "";

            datos.Base[p.Id] =
                new OradorMatrizMobile
                {
                    Id = p.Id,
                    Nombre = CrearNombreMostrar(
                        p,
                        congre),
                    Seccion = seccion
                };
        }

        string nombreSC =
            (DatabaseHelper.ObtenerConfiguracion(
                 "NombreSC",
                 "")
             + " "
             + DatabaseHelper.ObtenerConfiguracion(
                 "ApellidoSC",
                 ""))
                .Trim();

        if (string.IsNullOrWhiteSpace(nombreSC))
        {
            nombreSC = "Superintendente de Circuito";
        }

        datos.Base[-1] =
            new OradorMatrizMobile
            {
                Id = -1,
                Nombre = nombreSC,
                Seccion = ""
            };

        datos.Base[-2] =
            new OradorMatrizMobile
            {
                Id = -2,
                Nombre = "Representante de Betel",
                Seccion = ""
            };

        // ---- Años con datos (participaciones reales + año siguiente) ----
        List<ParticipacionOradorMobile> participaciones =
            DatabaseHelper
                .ObtenerParticipacionesOradoresMobile();

        var anios =
            participaciones
                .Select(p => p.Anio)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct()
                .ToList();

        string anioSiguiente =
            (DateTime.Now.Year + 1)
                .ToString();

        if (!anios.Contains(anioSiguiente))
        {
            anios.Add(anioSiguiente);
        }

        datos.Anios =
            anios
                .OrderBy(a => a.Length)
                .ThenBy(a => a)
                .ToList();

        // ---- Filas por orador y año ----
        foreach (
            OradorMatrizMobile orador
            in datos.Base.Values)
        {
            orador.Bos =
                DatabaseHelper.ObtenerConfiguracion(
                    $"BOS_{orador.Id}",
                    "");

            foreach (string anio in datos.Anios)
            {
                orador.Anios.Add(
                    new FilaAnioOradorMobile
                    {
                        OradorId = orador.Id,
                        Anio = anio,
                        Evaluacion =
                            DatabaseHelper.ObtenerConfiguracion(
                                $"EV_{anio}_{orador.Id}",
                                "False")
                            == "True"
                    });
            }
        }

        // Participaciones reales (el último parte procesado gana, como WPF).
        var manuales =
            new Dictionary<(int, string), string>();

        foreach (
            ParticipacionOradorMobile p in participaciones)
        {
            if (!datos.Base.TryGetValue(
                    p.AsignadoId,
                    out OradorMatrizMobile? orador))
            {
                continue;
            }

            string texto =
                CrearTextoParte(p);

            if (string.IsNullOrWhiteSpace(texto))
            {
                continue;
            }

            FilaAnioOradorMobile? fila =
                orador.Anios.FirstOrDefault(
                    f => f.Anio == p.Anio);

            if (fila is null)
            {
                continue;
            }

            orador.TieneHistorial = true;

            if (p.Grupo == "AC-BR")
            {
                fila.Acbr = texto;
            }
            else if (p.Grupo == "RP")
            {
                fila.Rp = texto;
            }
            else
            {
                fila.Acco = texto;
            }
        }

        // Valores manuales: tienen prioridad sobre la participación real.
        foreach (
            OradorMatrizMobile orador
            in datos.Base.Values)
        {
            foreach (FilaAnioOradorMobile fila in orador.Anios)
            {
                fila.Acbr =
                    LeerManual(
                        "MANUAL_ACBR",
                        fila.Anio,
                        orador.Id,
                        fila.Acbr);

                fila.Rp =
                    LeerManual(
                        "MANUAL_RP",
                        fila.Anio,
                        orador.Id,
                        fila.Rp);

                fila.Acco =
                    LeerManual(
                        "MANUAL_ACCO",
                        fila.Anio,
                        orador.Id,
                        fila.Acco);
            }
        }

        return datos;
    }

    private static string LeerManual(
        string clave,
        string anio,
        int oradorId,
        string automatico)
    {
        string manual =
            DatabaseHelper.ObtenerConfiguracion(
                $"{clave}_{anio}_{oradorId}",
                "");

        return string.IsNullOrWhiteSpace(manual)
            ? automatico
            : manual;
    }

    /// <summary>Igual que AsignarParteAOrador de Windows.</summary>
    private static string CrearTextoParte(
        ParticipacionOradorMobile p)
    {
        string tipo =
            p.TipoParte ?? "";

        if (tipo == "Canción"
            ||
            tipo.Contains("Oración")
            ||
            tipo == "Música")
        {
            return "";
        }

        string texto =
            tipo.Replace("(serie)", "")
                .Trim();

        Match match =
            Regex.Match(
                p.Titulo ?? "",
                @"\d+");

        if (match.Success)
        {
            texto += " " + match.Value;
        }
        else if (texto == "Resumen de La Atalaya")
        {
            texto = "Atalaya";
        }

        return texto;
    }

    /// <summary>Igual que NombreMuestra de Windows.</summary>
    private static string CrearNombreMostrar(
        Persona p,
        string congre)
    {
        string formateado;

        if (!string.IsNullOrWhiteSpace(p.NombreComun))
        {
            formateado = p.NombreComun;
        }
        else
        {
            string nombreJW =
                p.NombreJWHub ?? "";

            if (string.IsNullOrWhiteSpace(nombreJW))
            {
                return "";
            }

            if (nombreJW.Contains(','))
            {
                string[] partes =
                    nombreJW.Split(',');

                formateado =
                    partes.Length >= 2
                        ? $"{partes[1].Trim()} {partes[0].Trim()}"
                        : nombreJW;
            }
            else
            {
                formateado = nombreJW;
            }
        }

        if (string.IsNullOrWhiteSpace(congre))
        {
            return formateado;
        }

        return $"{formateado} ({congre})";
    }

    private void Pintar()
    {
        string seccionFiltro =
            PickerSeccion.SelectedItem as string
            ?? "Todas";

        bool filtrar =
            !string.IsNullOrWhiteSpace(seccionFiltro)
            &&
            seccionFiltro != "Todas";

        HashSet<int> manuales =
            ParsearIds(
                DatabaseHelper.ObtenerConfiguracion(
                    ClaveManuales,
                    ""));

        HashSet<int> ocultos =
            ParsearIds(
                DatabaseHelper.ObtenerConfiguracion(
                    ClaveOcultos,
                    ""));

        List<OradorMatrizMobile> lista =
            _base.Values
                .Where(o =>
                    (o.Anios.Any(a => a.TieneDatos)
                     || manuales.Contains(o.Id))
                    && !ocultos.Contains(o.Id))
                .Where(o =>
                    !filtrar
                    || o.Seccion == seccionFiltro)
                .OrderBy(o =>
                    string.IsNullOrWhiteSpace(o.Bos)
                        ? "ZZZ"
                        : o.Bos)
                .ThenBy(o => o.Nombre)
                .ToList();

        Lista.ItemsSource =
            lista;

        LblInfo.Text =
            $"{lista.Count} oradores con participación · " +
            $"años: {string.Join(", ", _anios)}";
    }

    private static HashSet<int> ParsearIds(
        string? texto)
    {
        var set =
            new HashSet<int>();

        if (string.IsNullOrWhiteSpace(texto))
        {
            return set;
        }

        foreach (
            string parte
            in texto.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(
                    parte.Trim(),
                    out int id)
                && id != 0)
            {
                set.Add(id);
            }
        }

        return set;
    }

    private static string Serializar(
        HashSet<int> ids)
    {
        return string.Join(
            ",",
            ids);
    }

    private void PickerSeccion_SelectedIndexChanged(
        object sender,
        EventArgs e)
    {
        if (_cargando
            ||
            PickerSeccion.SelectedIndex < 0)
        {
            return;
        }

        Pintar();
    }

    private async void BtnAnadir_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            string seccionFiltro =
                PickerSeccion.SelectedItem as string
                ?? "Todas";

            bool filtrar =
                !string.IsNullOrWhiteSpace(seccionFiltro)
                &&
                seccionFiltro != "Todas";

            List<OradorMatrizMobile> yaEnLista =
                (Lista.ItemsSource
                    as IEnumerable<OradorMatrizMobile>
                 ?? new List<OradorMatrizMobile>())
                .ToList();

            List<OradorMatrizMobile> disponibles =
                _base.Values
                    .Where(o =>
                        o.Id > 0
                        && !yaEnLista.Any(
                            l => l.Id == o.Id))
                    .Where(o =>
                        !filtrar
                        || o.Seccion == seccionFiltro)
                    .OrderBy(o => o.Nombre)
                    .ToList();

            if (disponibles.Count == 0)
            {
                await MostrarAlertaAsync(
                    "Matriz de oradores",
                    "Todos los ancianos de esta sección ya están " +
                    "añadidos a la matriz.");

                return;
            }

            string[] etiquetas =
                disponibles
                    .Select(o => o.Nombre)
                    .ToArray();

            string? elegido =
                await MostrarAccionAsync(
                    "Añadir participante",
                    "Elige al hermano:",
                    etiquetas);

            int indice =
                Array.IndexOf(
                    etiquetas,
                    elegido);

            if (indice < 0)
            {
                return;
            }

            int id =
                disponibles[indice].Id;

            HashSet<int> manuales =
                ParsearIds(
                    DatabaseHelper.ObtenerConfiguracion(
                        ClaveManuales,
                        ""));

            manuales.Add(id);

            DatabaseHelper.GuardarConfiguracion(
                ClaveManuales,
                Serializar(manuales));

            HashSet<int> ocultos =
                ParsearIds(
                    DatabaseHelper.ObtenerConfiguracion(
                        ClaveOcultos,
                        ""));

            if (ocultos.Remove(id))
            {
                DatabaseHelper.GuardarConfiguracion(
                    ClaveOcultos,
                    Serializar(ocultos));
            }

            Pintar();
        }
        catch (Exception ex)
        {
            await MostrarAlertaAsync(
                "Matriz de oradores",
                "No se pudo añadir: " + ex.Message);
        }
    }

    private async void BtnQuitar_Clicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button boton
            ||
            boton.CommandParameter is not
                OradorMatrizMobile orador)
        {
            return;
        }

        bool confirmar =
            await MostrarConfirmacionAsync(
                "Quitar participante",
                $"¿Deseas quitar a {orador.Nombre} de la matriz " +
                "de oradores?");

        if (!confirmar)
        {
            return;
        }

        try
        {
            HashSet<int> manuales =
                ParsearIds(
                    DatabaseHelper.ObtenerConfiguracion(
                        ClaveManuales,
                        ""));

            if (manuales.Remove(
                    orador.Id))
            {
                DatabaseHelper.GuardarConfiguracion(
                    ClaveManuales,
                    Serializar(manuales));
            }

            HashSet<int> ocultos =
                ParsearIds(
                    DatabaseHelper.ObtenerConfiguracion(
                        ClaveOcultos,
                        ""));

            ocultos.Add(
                orador.Id);

            DatabaseHelper.GuardarConfiguracion(
                ClaveOcultos,
                Serializar(ocultos));

            // Igual que Windows: al quitarlo se limpian sus valores.
            foreach (string anio in _anios)
            {
                DatabaseHelper.GuardarConfiguracion(
                    $"MANUAL_ACBR_{anio}_{orador.Id}",
                    "");

                DatabaseHelper.GuardarConfiguracion(
                    $"MANUAL_RP_{anio}_{orador.Id}",
                    "");

                DatabaseHelper.GuardarConfiguracion(
                    $"MANUAL_ACCO_{anio}_{orador.Id}",
                    "");

                DatabaseHelper.GuardarConfiguracion(
                    $"EV_{anio}_{orador.Id}",
                    "False");
            }

            Pintar();
        }
        catch (Exception ex)
        {
            await MostrarAlertaAsync(
                "Matriz de oradores",
                "No se pudo quitar: " + ex.Message);
        }
    }

    private async void Bos_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (e.Parameter is not
            OradorMatrizMobile orador)
        {
            return;
        }

        string? valor =
            await MostrarPromptAsync(
                "BOS",
                $"BOS de {orador.Nombre} (vacío para borrar)",
                orador.Bos);

        if (valor is null)
        {
            return;
        }

        DatabaseHelper.GuardarConfiguracion(
            $"BOS_{orador.Id}",
            valor.Trim());

        Pintar();
    }

    private async void Evaluacion_Toggled(
        object sender,
        ToggledEventArgs e)
    {
        if (sender is not Switch interruptor
            ||
            interruptor.BindingContext is not
                FilaAnioOradorMobile fila
            ||
            fila.OradorId == 0)
        {
            return;
        }

        // El propio enlace dispara el evento al pintar la lista: solo se
        // guarda cuando el usuario cambia realmente el valor.
        if (fila.Evaluacion == e.Value)
        {
            return;
        }

        fila.Evaluacion =
            e.Value;

        DatabaseHelper.GuardarConfiguracion(
            $"EV_{fila.Anio}_{fila.OradorId}",
            e.Value
                ? "True"
                : "False");
    }

    private async void Acbr_Tapped(
        object sender,
        TappedEventArgs e)
    {
        await EditarCeldaAsync(
            e.Parameter as FilaAnioOradorMobile,
            "MANUAL_ACBR",
            "AC-BR",
            f => f.Acbr);
    }

    private async void Rp_Tapped(
        object sender,
        TappedEventArgs e)
    {
        await EditarCeldaAsync(
            e.Parameter as FilaAnioOradorMobile,
            "MANUAL_RP",
            "R. Prec",
            f => f.Rp);
    }

    private async void Acco_Tapped(
        object sender,
        TappedEventArgs e)
    {
        await EditarCeldaAsync(
            e.Parameter as FilaAnioOradorMobile,
            "MANUAL_ACCO",
            "AC-CO",
            f => f.Acco);
    }

    private async Task EditarCeldaAsync(
        FilaAnioOradorMobile? fila,
        string clave,
        string etiqueta,
        Func<FilaAnioOradorMobile, string> leer)
    {
        if (fila is null)
        {
            return;
        }

        string? valor =
            await MostrarPromptAsync(
                etiqueta + " " + fila.Anio,
                $"Valor manual para {etiqueta} " +
                $"(vacío para volver al valor automático)",
                leer(fila));

        if (valor is null)
        {
            return;
        }

        DatabaseHelper.GuardarConfiguracion(
            $"{clave}_{fila.Anio}_{fila.OradorId}",
            valor.Trim());

        await CargarAsync();
    }

    // ------------------------------------------------------------------
    // Ayudas de interfaz (el ContentView no es una página)
    // ------------------------------------------------------------------

    private static Page? Pagina =>
        Application.Current?.Windows.Count > 0
            ? Application.Current.Windows[0].Page
            : null;

    private static Task MostrarAlertaAsync(
        string titulo,
        string mensaje)
    {
        return Pagina?.DisplayAlert(
                   titulo,
                   mensaje,
                   "Aceptar")
               ?? Task.CompletedTask;
    }

    private static Task<bool> MostrarConfirmacionAsync(
        string titulo,
        string mensaje)
    {
        return Pagina?.DisplayAlert(
                   titulo,
                   mensaje,
                   "Sí",
                   "No")
               ?? Task.FromResult(false);
    }

    private static Task<string?> MostrarAccionAsync(
        string titulo,
        string mensaje,
        string[] opciones)
    {
        return Pagina?.DisplayActionSheet(
                   titulo,
                   "Cancelar",
                   null,
                   opciones)
               ?? Task.FromResult<string?>(null);
    }

    private static Task<string?> MostrarPromptAsync(
        string titulo,
        string mensaje,
        string valorInicial)
    {
        return Pagina?.DisplayPromptAsync(
                   titulo,
                   mensaje,
                   "Guardar",
                   "Cancelar",
                   null,
                   -1,
                   null,
                   valorInicial)
               ?? Task.FromResult<string?>(null);
    }
}
