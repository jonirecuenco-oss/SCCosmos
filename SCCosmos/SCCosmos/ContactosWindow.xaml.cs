using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using ConvertidorContactos = SCCosmos.Sincro.ConvertidorContactos;
using Campo = SCCosmos.Sincro.ConvertidorContactos.Campo;

namespace SCCosmos
{
    /// <summary>
    /// Paso 2 de «Importar actividad»: los datos de contacto que manda la congregación
    /// (teléfono, email, dirección y contacto de emergencia). Cada fila de la lista, con
    /// la persona que más se le parece, lo que tiene ahora y lo nuevo. Solo se guardan
    /// las filas marcadas, y nunca se borra nada.
    /// </summary>
    public partial class ContactosWindow : Window
    {
        private readonly string _congreCodigo;
        private readonly string _ruta;
        private List<ConvertidorContactos.Analisis> _analisis = new();
        private List<ColumnaContacto> _columnas = new();
        private List<FilaContacto> _filas = new();
        private List<Persona> _personas = new();
        private List<IReadOnlyList<string>> _nombresPersonas = new();
        private Dictionary<int, (string Contacto, string Telefono)> _emergencias = new();
        private bool _propagando;

        public ContactosWindow(string congreCodigo, string congreNombre, string ruta, bool trasPersonas)
        {
            InitializeComponent();
            _congreCodigo = congreCodigo;
            _ruta = ruta;
            TxtTitulo.Text = trasPersonas ? "Paso 2 · Datos de contacto" : "Datos de contacto";
            TxtSubtitulo.Text = $"{congreNombre} · {System.IO.Path.GetFileName(ruta)}";
            Loaded += (_, _) => Cargar();
        }

        /// <summary>Lee la lista y la cruza con las personas de la congregación (al abrirse).</summary>
        public void Cargar()
        {
            try
            {
                var recordado = ConvertidorContactos.ColumnasDe(
                    DatabaseHelper.ObtenerConfiguracion(ConvertidorContactos.ClaveColumnas(_congreCodigo), ""));

                _analisis = ConvertidorContactos.Leer(_ruta)
                    .Where(t => t.Filas.Count > 0)
                    .Select(t => ConvertidorContactos.Analizar(t, recordado))
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"No se pudo leer la lista de contactos:\n{ex.Message}", "Datos de contacto", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            // Las personas que siguen en la congregación (sin bajas ni trasladadas).
            _personas = DatabaseHelper.ObtenerPersonasPorCongregacion(_congreCodigo)
                .Where(p => string.IsNullOrWhiteSpace(p.FechaEliminacion) && p.TrasladoSiguienteId == 0)
                .ToList();
            _nombresPersonas = _personas
                .Select(p => (IReadOnlyList<string>)new[] { p.Nombre ?? "", p.NombreComun ?? "", p.NombreJWHub ?? "" })
                .ToList();
            _emergencias = _personas.ToDictionary(p => p.Id, p => DatabaseHelper.ObtenerEmergenciaPersona(p.Id));

            CrearColumnas();
            Recalcular();
        }

        // «Qué es cada columna»: las que tienen algo, con su título (o su letra) y un ejemplo.
        private void CrearColumnas()
        {
            var tablas = new List<TablaContacto>();
            _columnas = new List<ColumnaContacto>();

            foreach (ConvertidorContactos.Analisis analisis in _analisis)
            {
                var columnas = new List<ColumnaContacto>();

                for (int c = 0; c < analisis.Columnas.Length; c++)
                {
                    int columna = c;
                    string ejemplo = analisis.Tabla.Filas
                        .Skip(analisis.FilaTitulos + 1)
                        .Select(f => columna < f.Length ? f[columna].Trim() : "")
                        .FirstOrDefault(v => v.Length > 0) ?? "";

                    if (ejemplo.Length == 0 && analisis.Titulos[c].Length == 0)
                    {
                        continue;
                    }

                    columnas.Add(new ColumnaContacto(this, analisis, c,
                        analisis.Titulos[c].Length > 0 ? analisis.Titulos[c] : "Columna " + Letra(c),
                        ejemplo.Length > 0 ? "p. ej. " + ejemplo : "(vacía)"));
                }

                if (columnas.Count > 0)
                {
                    int filas = Math.Max(0, analisis.Tabla.Filas.Count - analisis.FilaTitulos - 1);
                    tablas.Add(new TablaContacto($"{analisis.Tabla.Nombre} · {filas} {(filas == 1 ? "fila" : "filas")}", columnas));
                    _columnas.AddRange(columnas);
                }
            }

            ListaTablas.ItemsSource = tablas;
        }

        private static string Letra(int columna)
        {
            string letras = "";

            for (int n = columna + 1; n > 0; n = (n - 1) / 26)
            {
                letras = (char)('A' + (n - 1) % 26) + letras;
            }

            return letras;
        }

        // Lo elegido para un título vale para las columnas con ese título de todas las hojas.
        private void ColumnaCambiada(ColumnaContacto cambiada)
        {
            if (_propagando)
            {
                return;
            }

            _propagando = true;

            try
            {
                string titulo = ConvertidorContactos.Normalizar(cambiada.Analisis.Titulos[cambiada.Indice]);

                if (titulo.Length > 0)
                {
                    foreach (ColumnaContacto otra in _columnas.Where(o => o != cambiada
                                 && ConvertidorContactos.Normalizar(o.Analisis.Titulos[o.Indice]) == titulo))
                    {
                        otra.Eleccion = cambiada.Eleccion;
                    }
                }
            }
            finally
            {
                _propagando = false;
            }

            Recalcular();
        }

        // Las filas de la lista con la persona que más se parece a cada una.
        private void Recalcular()
        {
            List<ConvertidorContactos.Contacto> contactos = ConvertidorContactos.SinRepetir(_analisis.SelectMany(ConvertidorContactos.Contactos));
            int[,] parecidos = ConvertidorContactos.Parecidos(contactos.Select(c => c.Nombre).ToList(), _nombresPersonas);
            var vinculos = ConvertidorContactos.Vincular(parecidos);

            _filas = new List<FilaContacto>(contactos.Count);

            for (int i = 0; i < contactos.Count; i++)
            {
                int fila = i;
                var opciones = new List<OpcionPersona> { OpcionPersona.Ninguna };
                opciones.AddRange(Enumerable.Range(0, _personas.Count)
                    .OrderByDescending(p => parecidos[fila, p])
                    .ThenBy(p => _personas[p].Nombre, StringComparer.CurrentCulture)
                    .Select(p => new OpcionPersona(_personas[p], parecidos[fila, p])));

                (int persona, int porcentaje, bool dudoso) = vinculos[i];
                bool segura = persona >= 0 && porcentaje >= ConvertidorContactos.MinimoSeguro && !dudoso;

                string texto = persona < 0
                    ? (opciones.Count > 1 && opciones[1].Porcentaje >= 50 ? $"sin persona · la más parecida, al {opciones[1].Porcentaje} %" : "sin persona")
                    : segura ? $"{porcentaje} % · segura"
                    : dudoso ? $"{porcentaje} % · dudosa: hay otra parecida"
                    : $"{porcentaje} % · revisar";

                _filas.Add(new FilaContacto(this, contactos[i], opciones,
                    persona >= 0 ? opciones.First(o => o.Persona == _personas[persona]) : OpcionPersona.Ninguna,
                    texto, segura));
            }

            GridContactos.ItemsSource = _filas;
            ActualizarResumen();
        }

        private void ActualizarResumen()
        {
            int conPersona = _filas.Count(f => f.TienePersona);
            int seguras = _filas.Count(f => f.Segura);
            int marcadas = _filas.Count(f => f.Aplicar && f.HayCambios);

            TxtResumen.Text = _filas.Count == 0
                ? "No sale ninguna persona con datos: abre «Qué es cada columna» y elige cuál es el nombre."
                : $"{_filas.Count} en la lista · {conPersona} con persona ({seguras} seguras) · {_filas.Count - conPersona} sin persona · {marcadas} para guardar";
            BtnGuardar.Content = marcadas > 0 ? $"Guardar ({marcadas})" : "Guardar";
            BtnGuardar.IsEnabled = marcadas > 0;

            bool hayNombre = _columnas.Any(c => c.Eleccion.Campo is Campo.NombreCompleto or Campo.Nombre or Campo.Apellidos);
            string[] campos = _columnas
                .Select(c => c.Eleccion.Campo)
                .Where(c => c != Campo.Ignorar)
                .Distinct()
                .Select(c => OpcionCampo.De(c).Texto)
                .ToArray();

            ExpColumnas.Header = hayNombre
                ? "Qué es cada columna: " + string.Join(" · ", campos)
                : "Qué es cada columna: ninguna parece el nombre; elige cuál es";

            if (!hayNombre || _filas.Count == 0)
            {
                ExpColumnas.IsExpanded = true;
            }
        }

        private bool Guarda(Campo campo) => campo switch
        {
            Campo.Telefono => ChkTelefono.IsChecked == true,
            Campo.Email => ChkEmail.IsChecked == true,
            Campo.Direccion => ChkDireccion.IsChecked == true,
            _ => ChkEmergencia.IsChecked == true
        };

        private (string Contacto, string Telefono) EmergenciaDe(Persona persona) =>
            persona is not null && _emergencias.TryGetValue(persona.Id, out var emergencia) ? emergencia : ("", "");

        private void CampoGuardado_Click(object sender, RoutedEventArgs e)
        {
            foreach (FilaContacto fila in _filas)
            {
                fila.Calcular();
            }

            ActualizarResumen();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            int guardadas;

            try
            {
                guardadas = GuardarMarcadas();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"No se pudieron guardar los datos de contacto:\n{ex.Message}", "Datos de contacto", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (guardadas < 0)
            {
                var repetida = _filas.Where(f => f.Aplicar && f.HayCambios).GroupBy(f => f.Seleccion.Persona.Id).First(g => g.Count() > 1);
                MessageBox.Show(this, $"«{repetida.First().Seleccion.Persona.Nombre}» está elegida en {repetida.Count()} filas marcadas: deja marcada solo una.",
                    "Datos de contacto", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(this,
                guardadas == 1 ? "Guardados los datos de contacto de 1 persona." : $"Guardados los datos de contacto de {guardadas} personas.",
                "Datos de contacto", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }

        /// <summary>
        /// Guarda las filas marcadas (cuántas; −1, sin guardar nada, si una persona está en
        /// dos de ellas) y recuerda para esta congregación lo elegido a mano en las columnas.
        /// </summary>
        public int GuardarMarcadas()
        {
            List<FilaContacto> marcadas = _filas.Where(f => f.Aplicar && f.HayCambios).ToList();

            if (marcadas.GroupBy(f => f.Seleccion.Persona.Id).Any(g => g.Count() > 1))
            {
                return -1;
            }

            DatabaseHelper.GuardarDatosContacto(marcadas.Select(f => f.Cambios()).ToList());

            var elegidas = _columnas
                .Where(c => c.Cambiada)
                .Select(c => (c.Analisis.Titulos[c.Indice], c.Eleccion.Campo))
                .ToList();

            if (elegidas.Count > 0)
            {
                string clave = ConvertidorContactos.ClaveColumnas(_congreCodigo);
                DatabaseHelper.GuardarConfiguracion(clave, ConvertidorContactos.ColumnasJson(elegidas, DatabaseHelper.ObtenerConfiguracion(clave, "")));
            }

            return marcadas.Count;
        }

        private void BtnOmitir_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }


        // ================================================================
        // LO QUE SE ENSEÑA
        // ================================================================

        /// <summary>Una tabla del archivo (hoja, tabla del Word…) con sus columnas.</summary>
        public sealed class TablaContacto
        {
            public TablaContacto(string titulo, List<ColumnaContacto> columnas)
            {
                Titulo = titulo;
                Columnas = columnas;
            }

            public string Titulo { get; }

            public List<ColumnaContacto> Columnas { get; }
        }

        /// <summary>Un campo que se puede elegir para una columna.</summary>
        public sealed class OpcionCampo
        {
            public static readonly IReadOnlyList<OpcionCampo> Todas =
                ConvertidorContactos.Campos.Select(c => new OpcionCampo(c.Campo, c.Texto)).ToList();

            private OpcionCampo(Campo campo, string texto)
            {
                Campo = campo;
                Texto = texto;
            }

            public Campo Campo { get; }

            public string Texto { get; }

            public static OpcionCampo De(Campo campo) => Todas.First(o => o.Campo == campo);
        }

        /// <summary>Una columna del archivo y qué es.</summary>
        public sealed class ColumnaContacto : INotifyPropertyChanged
        {
            private readonly ContactosWindow _ventana;
            private OpcionCampo _eleccion;

            public ColumnaContacto(ContactosWindow ventana, ConvertidorContactos.Analisis analisis, int indice, string titulo, string ejemplo)
            {
                _ventana = ventana;
                Analisis = analisis;
                Indice = indice;
                Titulo = titulo;
                Ejemplo = ejemplo;
                _eleccion = OpcionCampo.De(analisis.Columnas[indice]);
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public ConvertidorContactos.Analisis Analisis { get; }

            public int Indice { get; }

            public string Titulo { get; }

            public string Ejemplo { get; }

            /// <summary>Si se eligió a mano (lo que se recuerda).</summary>
            public bool Cambiada { get; private set; }

            public IReadOnlyList<OpcionCampo> Opciones => OpcionCampo.Todas;

            public OpcionCampo Eleccion
            {
                get => _eleccion;
                set
                {
                    if (value is null || value == _eleccion)
                    {
                        return;
                    }

                    _eleccion = value;
                    Analisis.Columnas[Indice] = value.Campo;
                    Cambiada = true;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Eleccion)));
                    _ventana.ColumnaCambiada(this);
                }
            }
        }

        /// <summary>Una persona que se puede elegir para una fila, con lo que se parece.</summary>
        public sealed class OpcionPersona
        {
            public static readonly OpcionPersona Ninguna = new(null, 0);

            public OpcionPersona(Persona persona, int porcentaje)
            {
                Persona = persona;
                Porcentaje = porcentaje;
            }

            public Persona Persona { get; }

            public int Porcentaje { get; }

            /// <summary>Para buscar escribiendo en la lista.</summary>
            public string Nombre => Persona?.Nombre ?? "";

            public string Texto =>
                Persona is null ? "— Sin persona —"
                : Porcentaje >= 30 ? $"{Persona.Nombre} · {Porcentaje} %"
                : Persona.Nombre;
        }

        /// <summary>Lo nuevo de un campo y, aparte, lo que tiene ahora («antes: …», «sin cambios»…).</summary>
        public sealed class DatoContacto
        {
            public static readonly DatoContacto Nada = new("", "", false);

            public DatoContacto(string nuevo, string nota, bool cambia, bool cambiaContacto = false, bool cambiaTelefono = false)
            {
                Nuevo = nuevo;
                Nota = nota;
                Cambia = cambia;
                CambiaContacto = cambiaContacto;
                CambiaTelefono = cambiaTelefono;
            }

            public string Nuevo { get; }

            public string Nota { get; }

            public bool Cambia { get; }

            // Solo en el contacto de emergencia: qué parte cambia.
            public bool CambiaContacto { get; }

            public bool CambiaTelefono { get; }
        }

        /// <summary>Una fila de la lista, la persona elegida y lo que cambiaría en ella.</summary>
        public sealed class FilaContacto : INotifyPropertyChanged
        {
            private readonly ContactosWindow _ventana;
            private readonly OpcionPersona _automatica;
            private readonly string _textoAutomatico;
            private readonly bool _seguraAutomatica;
            private OpcionPersona _seleccion;
            private bool _aplicar;

            public FilaContacto(ContactosWindow ventana, ConvertidorContactos.Contacto contacto, List<OpcionPersona> opciones,
                OpcionPersona elegida, string textoParecido, bool segura)
            {
                _ventana = ventana;
                Contacto = contacto;
                Opciones = opciones;
                _automatica = _seleccion = elegida;
                _textoAutomatico = textoParecido;
                _seguraAutomatica = segura;
                Calcular();

                // Solo se marcan solas las seguras que traen algo nuevo.
                _aplicar = segura && HayCambios;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public ConvertidorContactos.Contacto Contacto { get; }

            public string Nombre => Contacto.Nombre;

            public string Origen => $"{Contacto.Tabla} · fila {Contacto.Fila}";

            public List<OpcionPersona> Opciones { get; }

            public bool TienePersona => _seleccion?.Persona is not null;

            public bool Segura => _seguraAutomatica && _seleccion == _automatica;

            public bool HayCambios { get; private set; }

            public string TextoParecido { get; private set; } = "";

            public DatoContacto Telefono { get; private set; } = DatoContacto.Nada;

            public DatoContacto Email { get; private set; } = DatoContacto.Nada;

            public DatoContacto Direccion { get; private set; } = DatoContacto.Nada;

            public DatoContacto Emergencia { get; private set; } = DatoContacto.Nada;

            public OpcionPersona Seleccion
            {
                get => _seleccion;
                set
                {
                    if (value is null || value == _seleccion)
                    {
                        return;
                    }

                    _seleccion = value;
                    Calcular();
                    Avisar(nameof(Seleccion), nameof(TienePersona));

                    // Elegida a mano: se marca si trae algo nuevo.
                    Aplicar = TienePersona && HayCambios;
                    _ventana.ActualizarResumen();
                }
            }

            public bool Aplicar
            {
                get => _aplicar;
                set
                {
                    if (value == _aplicar)
                    {
                        return;
                    }

                    _aplicar = value;
                    Avisar(nameof(Aplicar));
                    _ventana.ActualizarResumen();
                }
            }

            /// <summary>Lo nuevo frente a lo que tiene la persona elegida, campo a campo.</summary>
            public void Calcular()
            {
                Persona persona = _seleccion?.Persona;
                bool hayPersona = persona is not null;

                Telefono = Dato(Contacto.Telefono, persona?.Telefono, hayPersona, _ventana.Guarda(Campo.Telefono), MismoTelefono);
                Email = Dato(Contacto.Email, persona?.Email, hayPersona, _ventana.Guarda(Campo.Email), MismoEmail);
                Direccion = Dato(Contacto.Direccion, persona?.Direccion, hayPersona, _ventana.Guarda(Campo.Direccion), MismaDireccion);
                Emergencia = DatoEmergencia(persona);
                HayCambios = Telefono.Cambia || Email.Cambia || Direccion.Cambia || Emergencia.Cambia;

                TextoParecido = _seleccion == _automatica ? _textoAutomatico
                    : hayPersona ? $"{_seleccion.Porcentaje} % · elegida a mano"
                    : "sin persona";

                Avisar(nameof(Telefono), nameof(Email), nameof(Direccion), nameof(Emergencia), nameof(HayCambios), nameof(TextoParecido));
            }

            /// <summary>Lo que se guarda (null, lo que no cambia).</summary>
            public (int PersonaId, string Telefono, string Email, string Direccion, string ContactoEmergencia, string TelefonoEmergencia) Cambios() =>
                (_seleccion.Persona.Id,
                 Telefono.Cambia ? Contacto.Telefono : null,
                 Email.Cambia ? Contacto.Email : null,
                 Direccion.Cambia ? Contacto.Direccion : null,
                 Emergencia.CambiaContacto ? Contacto.ContactoEmergencia : null,
                 Emergencia.CambiaTelefono ? Contacto.TelefonoEmergencia : null);

            private static DatoContacto Dato(string nuevo, string antes, bool hayPersona, bool guarda, Func<string, string, bool> mismo)
            {
                nuevo = (nuevo ?? "").Trim();
                antes = (antes ?? "").Trim();

                if (nuevo.Length == 0)
                {
                    return DatoContacto.Nada;
                }

                if (!hayPersona)
                {
                    return new DatoContacto(nuevo, "", false);
                }

                if (mismo(antes, nuevo))
                {
                    return new DatoContacto(nuevo, "sin cambios", false);
                }

                if (!guarda)
                {
                    return new DatoContacto(nuevo, antes.Length == 0 ? "no se guarda" : "no se guarda · ahora: " + antes, false);
                }

                return new DatoContacto(nuevo, antes.Length == 0 ? "nuevo (no tenía)" : "antes: " + antes, true);
            }

            private DatoContacto DatoEmergencia(Persona persona)
            {
                (string contactoAntes, string telefonoAntes) = _ventana.EmergenciaDe(persona);
                string contacto = Contacto.ContactoEmergencia.Trim();
                string telefono = Contacto.TelefonoEmergencia.Trim();
                string nuevo = string.Join(" · ", new[] { contacto, telefono }.Where(t => t.Length > 0));
                string antes = string.Join(" · ", new[] { contactoAntes, telefonoAntes }.Where(t => t.Length > 0));

                if (nuevo.Length == 0)
                {
                    return DatoContacto.Nada;
                }

                if (persona is null)
                {
                    return new DatoContacto(nuevo, "", false);
                }

                bool cambiaContacto = contacto.Length > 0 && !MismoTexto(contactoAntes, contacto);
                bool cambiaTelefono = telefono.Length > 0 && !MismoTelefono(telefonoAntes, telefono);

                if (!cambiaContacto && !cambiaTelefono)
                {
                    return new DatoContacto(nuevo, "sin cambios", false);
                }

                if (!_ventana.Guarda(Campo.ContactoEmergencia))
                {
                    return new DatoContacto(nuevo, antes.Length == 0 ? "no se guarda" : "no se guarda · ahora: " + antes, false);
                }

                return new DatoContacto(nuevo, antes.Length == 0 ? "nuevo (no tenía)" : "antes: " + antes, true, cambiaContacto, cambiaTelefono);
            }

            private static string Compacto(string texto) => ConvertidorContactos.Normalizar(texto).Replace(" ", "");

            // Los números de teléfono que trae un texto (sus 9 últimas cifras: sin el +34).
            private static HashSet<string> Numeros(string texto) =>
                Regex.Matches(Regex.Replace(texto ?? "", @"(?<=\d)[ .\-](?=\d)", ""), @"\d{9,}")
                    .Select(m => m.Value[^9..])
                    .ToHashSet();

            // Ya lo tiene si cada número de la lista está entre los suyos.
            private static bool MismoTelefono(string antes, string nuevo)
            {
                HashSet<string> nuevos = Numeros(nuevo);
                return nuevos.Count > 0 ? nuevos.IsSubsetOf(Numeros(antes)) : Compacto(antes) == Compacto(nuevo);
            }

            private static bool MismoEmail(string antes, string nuevo)
            {
                static HashSet<string> Emails(string texto) =>
                    Regex.Matches(texto ?? "", @"[^\s/;,]+@[^\s/;,]+").Select(m => m.Value.ToLowerInvariant()).ToHashSet();

                HashSet<string> nuevos = Emails(nuevo);
                return nuevos.Count > 0 ? nuevos.IsSubsetOf(Emails(antes)) : Compacto(antes) == Compacto(nuevo);
            }

            // La misma si lo que trae la lista ya está en la que tiene (que suele ser más completa).
            private static bool MismaDireccion(string antes, string nuevo)
            {
                static HashSet<string> Palabras(string texto) =>
                    ConvertidorContactos.Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => p.Length > 1 || p.All(char.IsDigit))
                        .ToHashSet();

                HashSet<string> nuevas = Palabras(nuevo);
                return nuevas.Count > 0 && nuevas.IsSubsetOf(Palabras(antes));
            }

            private static bool MismoTexto(string antes, string nuevo) =>
                Compacto(nuevo).Length > 0 && Compacto(antes).Contains(Compacto(nuevo));

            private void Avisar(params string[] propiedades)
            {
                foreach (string propiedad in propiedades)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
                }
            }
        }
    }
}
