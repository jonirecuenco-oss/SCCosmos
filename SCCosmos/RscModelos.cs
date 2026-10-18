using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SCCosmos
{
    // =========================================================
    // RSC · REUNIÓN PARA EL SERVICIO DEL CAMPO
    //
    // Una RSC es una reunión breve antes de predicar. Se marca
    // quién asiste, quién sale a predicar, quién acepta ir en
    // grupo y los arreglos con otras personas (uno o varios).
    // Con eso se forman grupos (de 4 o 6, y nunca menos de 4
    // salvo arreglo), intentando que sean del mismo sexo.
    //
    // El superintendente de circuito y su esposa no son
    // publicadores locales: se representan con PersonaId
    // negativo (ver RscVirtuales). Su nombre y sexo se guardan
    // en la propia RSC.
    //
    // Las tablas usan Id GUID de texto: así varios dispositivos
    // pueden crear RSC a la vez sin chocar al sincronizar.
    // =========================================================

    /// <summary>
    /// Identificadores reservados para el superintendente de
    /// circuito y su esposa dentro de una RSC.
    /// </summary>
    public static class RscVirtuales
    {
        public const int Superintendente = -1;
        public const int Esposa = -2;
    }


    public class RscReunion
    {
        /// <summary>GUID de texto (vacío hasta guardar por primera vez).</summary>
        public string Id { get; set; } = "";

        public string CongregacionCodigo { get; set; } = "";
        public string CongregacionNombre { get; set; } = "";
        public string Fecha { get; set; } = "";      // yyyy-MM-dd
        public string Turno { get; set; } = "";      // Mañana / Tarde
        public string Notas { get; set; } = "";
        public string FechaCreacion { get; set; } = "";

        /// <summary>Cuántos grupos tiene guardados (informativo).</summary>
        public int NumeroGrupos { get; set; }

        /// <summary>Cuántas personas salen a predicar (informativo).</summary>
        public int NumeroPersonas { get; set; }

        public string FechaVisual =>
            DateTime.TryParse(Fecha, out DateTime valor)
                ? valor.ToString("dd/MM/yyyy")
                : Fecha;

        public string Titulo =>
            $"{FechaVisual} · {Turno}";

        public string TituloCompleto =>
            string.IsNullOrWhiteSpace(CongregacionNombre)
                ? Titulo
                : $"{Titulo} · {CongregacionNombre}";
    }


    /// <summary>
    /// Una persona dentro de la RSC: si asistió, si sale a
    /// predicar, si acepta grupo y con quiénes tiene arreglo.
    /// </summary>
    public class RscPersona : INotifyPropertyChanged
    {
        public int PersonaId { get; set; }
        public string Nombre { get; set; } = "";

        /// <summary>Hombre / Mujer (para agrupar por sexo).</summary>
        public string Sexo { get; set; } = "";

        /// <summary>Grupo de servicio de la congregación (informativo).</summary>
        public int GrupoPredicacion { get; set; }

        /// <summary>Territorio asignado a la persona (de TerritoriosVisitaV3).</summary>
        public string Territorio { get; set; } = "";

        /// <summary>Persona sin ficha local (SC o su esposa).</summary>
        public bool EsVirtual => PersonaId < 0;

        private bool _asiste;
        public bool Asiste
        {
            get => _asiste;
            set { if (_asiste != value) { _asiste = value; Notificar(); Notificar(nameof(Detalle)); } }
        }

        private bool _saleAPredicar;
        public bool SaleAPredicar
        {
            get => _saleAPredicar;
            set { if (_saleAPredicar != value) { _saleAPredicar = value; Notificar(); Notificar(nameof(Detalle)); } }
        }

        private bool _aceptaGrupo = true;
        public bool AceptaGrupo
        {
            get => _aceptaGrupo;
            set { if (_aceptaGrupo != value) { _aceptaGrupo = value; Notificar(); Notificar(nameof(Detalle)); } }
        }

        /// <summary>
        /// Personas con las que va en arreglo (una o varias).
        /// </summary>
        public List<int> ArregloConPersonaIds { get; set; } =
            new List<int>();

        private string _notas = "";
        public string Notas
        {
            get => _notas;
            set { if (_notas != value) { _notas = value; Notificar(); } }
        }

        /// <summary>Número de grupo dentro de la RSC (0 = sin asignar).</summary>
        private int _grupoNumero;
        public int GrupoNumero
        {
            get => _grupoNumero;
            set { if (_grupoNumero != value) { _grupoNumero = value; Notificar(); Notificar(nameof(EnGrupo)); } }
        }

        public bool EnGrupo => GrupoNumero > 0;

        public bool TieneTerritorio =>
            !string.IsNullOrWhiteSpace(Territorio);

        public string TerritorioVisual =>
            TieneTerritorio ? Territorio : "";

        /// <summary>Persona que predica y sale en los grupos de la RSC.</summary>
        public bool SaleEnGrupos =>
            Asiste && SaleAPredicar;

        public bool TieneArreglo =>
            ArregloConPersonaIds != null
            && ArregloConPersonaIds.Count > 0;

        /// <summary>
        /// Un arreglo que no admite grupo: solo sale con las personas
        /// con las que ha quedado (o solo/a, si no tiene arreglo).
        /// </summary>
        public bool EsArregloCerrado =>
            TieneArreglo && !AceptaGrupo;

        public string Detalle
        {
            get
            {
                var partes = new List<string>();

                if (!AceptaGrupo)
                {
                    partes.Add(
                        TieneArreglo
                            ? "solo con su arreglo"
                            : "solo/a");
                }
                else if (TieneArreglo)
                {
                    partes.Add("con arreglo");
                }

                if (TieneTerritorio)
                {
                    partes.Add("territorio " + Territorio);
                }

                return string.Join(" · ", partes);
            }
        }

        /// <summary>Texto útil del sexo: 1 hombre, 2 mujer, 0 sin dato.</summary>
        public int ClaseSexo
        {
            get
            {
                string sexo = (Sexo ?? "").ToLowerInvariant();

                if (sexo.Contains("hombre"))
                {
                    return 1;
                }

                if (sexo.Contains("mujer"))
                {
                    return 2;
                }

                return 0;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notificar([CallerMemberName] string propiedad = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        }
    }


    /// <summary>Grupo formado dentro de una RSC.</summary>
    public class RscGrupo : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string RscReunionId { get; set; } = "";

        private int _numero;
        public int Numero
        {
            get => _numero;
            set { if (_numero != value) { _numero = value; Notificar(); Notificar(nameof(Titulo)); } }
        }

        private string _notas = "";
        public string Notas
        {
            get => _notas;
            set { if (_notas != value) { _notas = value; Notificar(); } }
        }

        /// <summary>Territorio que predicará el grupo (texto libre).</summary>
        private string _territorio = "";
        public string Territorio
        {
            get => _territorio;
            set { if (_territorio != value) { _territorio = value; Notificar(); Notificar(nameof(TieneTerritorio)); } }
        }

        public bool TieneTerritorio =>
            !string.IsNullOrWhiteSpace(Territorio);

        public ObservableCollection<RscPersona> Miembros { get; } =
            new ObservableCollection<RscPersona>();

        public string Titulo =>
            $"Grupo {Numero} · {Miembros.Count} " +
            (Miembros.Count == 1 ? "persona" : "personas");

        /// <summary>Territorios que llevan sus miembros, si alguno tiene.</summary>
        public string TerritoriosMiembros
        {
            get
            {
                var territorios = Miembros
                    .Where(m => m.TieneTerritorio)
                    .Select(m => m.Territorio.Trim())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .ToList();

                return territorios.Count == 0
                    ? ""
                    : "con territorio: " + string.Join(", ", territorios);
            }
        }

        public bool BajoMinimo =>
            Miembros.Count > 0 && Miembros.Count < RscMotor.MinimoGrupo;

        public string Aviso
        {
            get
            {
                var partes = new List<string>();

                if (BajoMinimo)
                {
                    partes.Add(
                        $"⚠ {Miembros.Count} persona(s): por debajo de {RscMotor.MinimoGrupo}");
                }

                if (!string.IsNullOrWhiteSpace(Notas))
                {
                    partes.Add(Notas);
                }

                return string.Join(" · ", partes);
            }
        }

        public void Refrescar()
        {
            Notificar(nameof(Titulo));
            Notificar(nameof(TerritoriosMiembros));
            Notificar(nameof(BajoMinimo));
            Notificar(nameof(Aviso));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notificar([CallerMemberName] string propiedad = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        }
    }


    /// <summary>Opción para el desplegable de arreglos.</summary>
    public class RscOpcionPersona
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
    }


    /// <summary>
    /// Un arreglo ya apuntado en el programa de la visita
    /// (ProgramaVisitaV2): el SC o su esposa predicarán ese día
    /// con alguien en concreto.
    /// </summary>
    public class RscArregloDetectado
    {
        /// <summary>El arreglo es del superintendente de circuito.</summary>
        public bool ParaSuperintendente { get; set; }

        /// <summary>El arreglo es de la esposa del superintendente.</summary>
        public bool ParaEsposa { get; set; }

        /// <summary>Texto tal cual aparece en el programa (Joni, SC…).</summary>
        public string Responsable { get; set; } = "";

        public int CompaneroId { get; set; }
        public string CompaneroNombre { get; set; } = "";
        public string Turno { get; set; } = "";
        public string Hora { get; set; } = "";
        public string Tipo { get; set; } = "";
    }


    /// <summary>Grupo ya guardado, para el buscador por fecha.</summary>
    public class RscGrupoVista
    {
        public string Titulo { get; set; } = "";
        public string Subtitulo { get; set; } = "";
        public string Territorios { get; set; } = "";
        public string Notas { get; set; } = "";
        public List<string> Miembros { get; set; } =
            new List<string>();
    }
}
